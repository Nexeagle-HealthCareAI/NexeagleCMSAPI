using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Services;

/// <summary>See IWorkActivityService. Sits next to CmsDbContext because it is all queries.</summary>
public class WorkActivityService : IWorkActivityService
{
    private const int MaxTake = 200;
    private readonly CmsDbContext _db;
    private readonly IWorkAccessService _access;

    public WorkActivityService(CmsDbContext db, IWorkAccessService access)
    {
        _db = db;
        _access = access;
    }

    private static string? Cut(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : (value.Length <= max ? value : value.Substring(0, max));

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public async Task<WorkMyPresenceDto> TouchAsync(Guid userId, WorkPresenceRequest request, string? ip, string? userAgent)
    {
        var now = DateTime.UtcNow;
        ip = Cut(ip, 64);
        userAgent = Cut(userAgent, 300);

        var row = await _db.WorkPresences.FirstOrDefaultAsync(p => p.UserId == userId);
        if (row == null)
        {
            row = new WorkPresence { UserId = userId, LastSeenAt = now };
            _db.WorkPresences.Add(row);
        }
        row.LastSeenAt = now;

        if (request.Opened)
        {
            row.PreviousOpenedAt = row.LastOpenedAt;
            row.PreviousIp = row.LastIp;
            row.LastOpenedAt = now;
            row.LastIp = ip;
            row.LastUserAgent = userAgent;
            AddLog(userId, "Opened", ip, userAgent, now);
        }

        var blocked = request.Event;
        if (!string.IsNullOrWhiteSpace(blocked) && blocked != "Opened" && WorkActivityRules.SessionKinds.Contains(blocked))
        {
            AddLog(userId, blocked, ip, userAgent, now);
        }

        await _db.SaveChangesAsync();
        return new WorkMyPresenceDto
        {
            OpenedAt = row.LastOpenedAt.HasValue ? Utc(row.LastOpenedAt.Value) : (DateTime?)null,
            Ip = row.LastIp,
            PreviousOpenedAt = row.PreviousOpenedAt.HasValue ? Utc(row.PreviousOpenedAt.Value) : (DateTime?)null,
            PreviousIp = row.PreviousIp,
        };
    }

    private void AddLog(Guid userId, string kind, string? ip, string? userAgent, DateTime at) =>
        _db.WorkSessionLogs.Add(new WorkSessionLog { LogId = Guid.NewGuid(), UserId = userId, Kind = kind, Ip = ip, UserAgent = userAgent, CreatedAt = at });

    public async Task<List<WorkLastSeenDto>> GetLastSeenAsync()
    {
        var online = DateTime.UtcNow.AddMinutes(-WorkActivityRules.OnlineMinutes);
        var rows = await _db.WorkPresences.AsNoTracking().ToListAsync();
        return rows
            .Select(r => new WorkLastSeenDto { UserId = r.UserId, LastSeenAt = Utc(r.LastSeenAt), IsOnline = r.LastSeenAt >= online })
            .ToList();
    }

    public async Task<List<WorkActivityDto>> GetActivityAsync(WorkActivityQuery query, WorkActor reader, bool includeDocs)
    {
        var take = Math.Clamp(query.Take, 1, MaxTake);
        var source = WorkActivityRules.Sources.Contains(query.Source ?? string.Empty) ? query.Source : null;
        // A list, so the "not in" below turns into SQL.
        var closed = (await _access.ClosedProjectIdsAsync(reader)).ToList();
        // Asking for a board the reader may not open gives nothing, as if it did not exist.
        if (query.ProjectId.HasValue && closed.Contains(query.ProjectId.Value)) return new List<WorkActivityDto>();

        var projects = await _db.WorkProjects.AsNoTracking().ToDictionaryAsync(p => p.ProjectId, p => p.Key);
        var list = new List<WorkActivityDto>();
        bool Wants(string name) => source == null || source == name;
        var userId = query.UserId;
        var projectId = query.ProjectId;
        var from = query.From;
        var before = query.Before;

        // Each source gives at most `take` rows; the newest `take` of them all are returned.
        if (Wants("items"))
        {
            var q = from h in _db.WorkItemHistories.AsNoTracking()
                    join i in _db.WorkItems.AsNoTracking() on h.WorkItemId equals i.WorkItemId
                    where !closed.Contains(i.ProjectId)
                    select new { h, i };
            if (userId.HasValue) q = q.Where(x => x.h.ActorUserId == userId);
            if (projectId.HasValue) q = q.Where(x => x.i.ProjectId == projectId);
            if (from.HasValue) q = q.Where(x => x.h.CreatedAt >= from);
            if (before.HasValue) q = q.Where(x => x.h.CreatedAt < before);
            foreach (var x in await q.OrderByDescending(x => x.h.CreatedAt).Take(take).ToListAsync())
            {
                list.Add(new WorkActivityDto
                {
                    Source = "items", Action = x.h.Field, ActorUserId = x.h.ActorUserId,
                    ProjectKey = projects.TryGetValue(x.i.ProjectId, out var key) ? key : null,
                    WorkItemId = x.i.WorkItemId, ItemKey = (key ?? "?") + "-" + x.i.Number, ItemTitle = x.i.Title,
                    // A long description is not repeated in the log.
                    OldValue = x.h.Field == "description" ? null : x.h.OldValue,
                    NewValue = x.h.Field == "description" ? null : x.h.NewValue,
                    CreatedAt = Utc(x.h.CreatedAt),
                });
            }
        }

        if (Wants("comments"))
        {
            var q = from c in _db.WorkItemComments.AsNoTracking()
                    join i in _db.WorkItems.AsNoTracking() on c.WorkItemId equals i.WorkItemId
                    where !closed.Contains(i.ProjectId)
                    select new { c, i };
            if (userId.HasValue) q = q.Where(x => x.c.AuthorUserId == userId);
            if (projectId.HasValue) q = q.Where(x => x.i.ProjectId == projectId);
            if (from.HasValue) q = q.Where(x => x.c.CreatedAt >= from);
            if (before.HasValue) q = q.Where(x => x.c.CreatedAt < before);
            foreach (var x in await q.OrderByDescending(x => x.c.CreatedAt).Take(take).ToListAsync())
            {
                list.Add(new WorkActivityDto
                {
                    Source = "comments", Action = x.c.IsDeleted ? "commentRemoved" : "commented", ActorUserId = x.c.AuthorUserId,
                    ProjectKey = projects.TryGetValue(x.i.ProjectId, out var key) ? key : null,
                    WorkItemId = x.i.WorkItemId, ItemKey = (key ?? "?") + "-" + x.i.Number, ItemTitle = x.i.Title,
                    CreatedAt = Utc(x.c.CreatedAt),
                });
            }
        }

        if (Wants("access"))
        {
            var q = _db.WorkProjectAccessLogs.AsNoTracking().Where(l => !closed.Contains(l.ProjectId));
            if (userId.HasValue) q = q.Where(l => l.ActorUserId == userId || l.TargetUserId == userId);
            if (projectId.HasValue) q = q.Where(l => l.ProjectId == projectId);
            if (from.HasValue) q = q.Where(l => l.CreatedAt >= from);
            if (before.HasValue) q = q.Where(l => l.CreatedAt < before);
            var rows = await q.OrderByDescending(l => l.CreatedAt).Take(take).ToListAsync();
            var targets = await NamesAsync(rows.Where(r => r.TargetUserId.HasValue).Select(r => r.TargetUserId!.Value));
            foreach (var l in rows)
            {
                list.Add(new WorkActivityDto
                {
                    Source = "access", Action = l.Action, ActorUserId = l.ActorUserId,
                    ProjectKey = projects.TryGetValue(l.ProjectId, out var key) ? key : null,
                    Detail = l.TargetUserId.HasValue && targets.TryGetValue(l.TargetUserId.Value, out var target) ? target : null,
                    CreatedAt = Utc(l.CreatedAt),
                });
            }
        }

        // Documents and visits do not belong to a board, so a board filter leaves them out.
        if (Wants("docs") && includeDocs && !projectId.HasValue)
        {
            var q = _db.DocAccessLogs.AsNoTracking().AsQueryable();
            if (userId.HasValue) q = q.Where(l => l.UserId == userId);
            if (from.HasValue) q = q.Where(l => l.CreatedAt >= from);
            if (before.HasValue) q = q.Where(l => l.CreatedAt < before);
            var rows = await q.OrderByDescending(l => l.CreatedAt).Take(take).ToListAsync();
            var docIds = rows.Where(r => r.DocumentId.HasValue).Select(r => r.DocumentId!.Value).Distinct().ToList();
            var docs = await _db.WorkDocuments.AsNoTracking().Where(d => docIds.Contains(d.DocumentId)).ToDictionaryAsync(d => d.DocumentId, d => d.Name);
            foreach (var l in rows)
            {
                list.Add(new WorkActivityDto
                {
                    Source = "docs", Action = l.Action, ActorUserId = l.UserId,
                    DocumentId = l.DocumentId,
                    DocumentName = l.DocumentId.HasValue && docs.TryGetValue(l.DocumentId.Value, out var name) ? name : null,
                    Detail = l.Detail,
                    CreatedAt = Utc(l.CreatedAt),
                });
            }
        }

        if (Wants("sessions") && !projectId.HasValue)
        {
            var q = _db.WorkSessionLogs.AsNoTracking().AsQueryable();
            if (userId.HasValue) q = q.Where(l => l.UserId == userId);
            if (from.HasValue) q = q.Where(l => l.CreatedAt >= from);
            if (before.HasValue) q = q.Where(l => l.CreatedAt < before);
            foreach (var l in await q.OrderByDescending(l => l.CreatedAt).Take(take).ToListAsync())
            {
                list.Add(new WorkActivityDto
                {
                    Source = "sessions", Action = l.Kind, ActorUserId = l.UserId,
                    Detail = string.Join(" · ", new[] { l.Ip, l.UserAgent }.Where(v => !string.IsNullOrWhiteSpace(v))),
                    CreatedAt = Utc(l.CreatedAt),
                });
            }
        }

        var newest = list.OrderByDescending(a => a.CreatedAt).Take(take).ToList();
        var names = await NamesAsync(newest.Where(a => a.ActorUserId.HasValue).Select(a => a.ActorUserId!.Value));
        foreach (var a in newest)
        {
            a.ActorName = a.ActorUserId.HasValue && names.TryGetValue(a.ActorUserId.Value, out var name) ? name : "Someone";
        }
        return newest;
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> userIds)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        return await _db.CmsUsers.AsNoTracking().Where(u => ids.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName);
    }
}
