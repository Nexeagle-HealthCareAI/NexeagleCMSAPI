using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Services;

/// <summary>
/// Board members of the Daily Work Board. See IWorkAccessService for the rule. Like DocsService this
/// sits next to CmsDbContext: every method is one "may this person see this row" query.
/// </summary>
public class WorkAccessService : IWorkAccessService
{
    private const int LogRows = 50;
    private readonly CmsDbContext _db;

    public WorkAccessService(CmsDbContext db)
    {
        _db = db;
    }

    public async Task<HashSet<Guid>> ClosedProjectIdsAsync(WorkActor actor)
    {
        if (actor.SeesAllBoards) return new HashSet<Guid>();
        var restricted = await _db.WorkProjects.AsNoTracking().Where(p => p.IsRestricted).Select(p => p.ProjectId).ToListAsync();
        if (restricted.Count == 0) return new HashSet<Guid>();
        var mine = actor.UserId.HasValue
            ? await _db.WorkProjectMembers.AsNoTracking().Where(m => m.UserId == actor.UserId.Value).Select(m => m.ProjectId).ToListAsync()
            : new List<Guid>();
        return restricted.Where(id => !mine.Contains(id)).ToHashSet();
    }

    public async Task<bool> CanOpenProjectAsync(Guid projectId, WorkActor actor)
    {
        if (actor.SeesAllBoards) return true;
        var restricted = await _db.WorkProjects.AsNoTracking().AnyAsync(p => p.ProjectId == projectId && p.IsRestricted);
        if (!restricted) return true;
        if (!actor.UserId.HasValue) return false;
        var userId = actor.UserId.Value;
        return await _db.WorkProjectMembers.AsNoTracking().AnyAsync(m => m.ProjectId == projectId && m.UserId == userId);
    }

    public async Task<bool> CanOpenItemAsync(Guid workItemId, WorkActor actor)
    {
        if (actor.SeesAllBoards) return true;
        var projectIds = await _db.WorkItems.AsNoTracking().Where(i => i.WorkItemId == workItemId).Select(i => i.ProjectId).ToListAsync();
        return projectIds.Count == 0 || await CanOpenProjectAsync(projectIds[0], actor);
    }

    public async Task<bool> CanOpenStatusAsync(Guid statusId, WorkActor actor)
    {
        if (actor.SeesAllBoards) return true;
        var projectIds = await _db.WorkStatuses.AsNoTracking().Where(s => s.StatusId == statusId).Select(s => s.ProjectId).ToListAsync();
        return projectIds.Count == 0 || await CanOpenProjectAsync(projectIds[0], actor);
    }

    public async Task<bool> CanOpenCommentAsync(Guid commentId, WorkActor actor)
    {
        if (actor.SeesAllBoards) return true;
        var itemIds = await _db.WorkItemComments.AsNoTracking().Where(c => c.CommentId == commentId).Select(c => c.WorkItemId).ToListAsync();
        return itemIds.Count == 0 || await CanOpenItemAsync(itemIds[0], actor);
    }

    public async Task<WorkResult<WorkProjectAccessDto>> GetAccessAsync(Guid projectId)
    {
        var project = await _db.WorkProjects.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectId == projectId);
        if (project == null) return WorkResult<WorkProjectAccessDto>.Fail(404, "Project not found.");
        return WorkResult<WorkProjectAccessDto>.Success(await BuildAsync(project));
    }

    public async Task<WorkResult<WorkProjectAccessDto>> SaveAccessAsync(Guid projectId, SaveWorkProjectAccessRequest request, Guid? actorUserId)
    {
        var project = await _db.WorkProjects.FirstOrDefaultAsync(p => p.ProjectId == projectId);
        if (project == null) return WorkResult<WorkProjectAccessDto>.Fail(404, "Project not found.");

        var wanted = (request.MemberUserIds ?? new List<Guid>()).Distinct().ToList();
        var known = await _db.CmsUsers.AsNoTracking().Where(u => wanted.Contains(u.UserId) && u.IsActive).Select(u => u.UserId).ToListAsync();
        if (known.Count != wanted.Count) return WorkResult<WorkProjectAccessDto>.Fail(400, "One of the people chosen is not an active CMS user.");

        var now = DateTime.UtcNow;
        var current = await _db.WorkProjectMembers.Where(m => m.ProjectId == projectId).ToListAsync();

        foreach (var member in current.Where(m => !wanted.Contains(m.UserId)))
        {
            _db.WorkProjectMembers.Remove(member);
            Log(projectId, "MemberRemoved", member.UserId, actorUserId, now);
        }
        foreach (var userId in wanted.Where(id => current.All(m => m.UserId != id)))
        {
            _db.WorkProjectMembers.Add(new WorkProjectMember { ProjectId = projectId, UserId = userId, AddedByUserId = actorUserId, AddedAt = now });
            Log(projectId, "MemberAdded", userId, actorUserId, now);
        }
        if (project.IsRestricted != request.IsRestricted)
        {
            project.IsRestricted = request.IsRestricted;
            Log(projectId, request.IsRestricted ? "Restricted" : "Opened", null, actorUserId, now);
        }

        await _db.SaveChangesAsync();
        return WorkResult<WorkProjectAccessDto>.Success(await BuildAsync(project), "Access saved.");
    }

    private void Log(Guid projectId, string action, Guid? targetUserId, Guid? actorUserId, DateTime at) =>
        _db.WorkProjectAccessLogs.Add(new WorkProjectAccessLog
        {
            LogId = Guid.NewGuid(), ProjectId = projectId, Action = action, TargetUserId = targetUserId, ActorUserId = actorUserId, CreatedAt = at,
        });

    private async Task<WorkProjectAccessDto> BuildAsync(WorkProject project)
    {
        var members = await _db.WorkProjectMembers.AsNoTracking().Where(m => m.ProjectId == project.ProjectId).Select(m => m.UserId).ToListAsync();
        var rows = await _db.WorkProjectAccessLogs.AsNoTracking()
            .Where(l => l.ProjectId == project.ProjectId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(LogRows)
            .ToListAsync();

        var people = new List<Guid>();
        foreach (var row in rows)
        {
            if (row.TargetUserId.HasValue) people.Add(row.TargetUserId.Value);
            if (row.ActorUserId.HasValue) people.Add(row.ActorUserId.Value);
        }
        var ids = people.Distinct().ToList();
        var names = await _db.CmsUsers.AsNoTracking().Where(u => ids.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName);

        return new WorkProjectAccessDto
        {
            ProjectId = project.ProjectId,
            IsRestricted = project.IsRestricted,
            MemberUserIds = members,
            Log = rows.Select(row => new WorkAccessLogDto
            {
                Action = row.Action,
                TargetName = row.TargetUserId.HasValue && names.TryGetValue(row.TargetUserId.Value, out var target) ? target : null,
                ActorName = row.ActorUserId.HasValue && names.TryGetValue(row.ActorUserId.Value, out var actor) ? actor : "Someone",
                CreatedAt = DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc),
            }).ToList(),
        };
    }
}
