using CMSAPI.Application.Interfaces;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories;

/// <summary>Daily Work Board storage (CMSDatabase). Deleted rows are filtered out here.</summary>
public class WorkRepository : IWorkRepository
{
    private const int NumberAttempts = 3;

    private readonly CmsDbContext _db;

    public WorkRepository(CmsDbContext db)
    {
        _db = db;
    }

    public async Task<List<WorkProject>> GetProjectsAsync()
    {
        return await _db.WorkProjects.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
    }

    public async Task<WorkProject?> GetProjectAsync(Guid projectId)
    {
        return await _db.WorkProjects.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectId == projectId);
    }

    public async Task<bool> ProjectKeyExistsAsync(string key)
    {
        return await _db.WorkProjects.AnyAsync(p => p.Key == key);
    }

    public async Task AddProjectAsync(WorkProject project)
    {
        _db.WorkProjects.Add(project);
        await _db.SaveChangesAsync();
    }

    public async Task<List<WorkStatus>> GetStatusesAsync(Guid projectId)
    {
        return await _db.WorkStatuses
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<List<WorkStatus>> GetAllStatusesAsync()
    {
        return await _db.WorkStatuses.AsNoTracking()
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<WorkStatus?> GetStatusAsync(Guid statusId)
    {
        return await _db.WorkStatuses.FirstOrDefaultAsync(s => s.StatusId == statusId);
    }

    public async Task AddStatusesAsync(List<WorkStatus> statuses)
    {
        _db.WorkStatuses.AddRange(statuses);
        await _db.SaveChangesAsync();
    }

    public async Task SaveStatusesAsync(List<WorkStatus> statuses)
    {
        foreach (var status in statuses)
        {
            if (_db.Entry(status).State == EntityState.Detached) _db.WorkStatuses.Update(status);
        }
        await _db.SaveChangesAsync();
    }

    public async Task<int> CountItemsInStatusAsync(Guid projectId, string statusKey)
    {
        return await _db.WorkItems.CountAsync(i => i.ProjectId == projectId && i.Status == statusKey && !i.IsDeleted);
    }

    public async Task DeleteStatusAsync(WorkStatus status, string moveToKey)
    {
        // Deleted items are moved too, so no row is left pointing at a column that is gone.
        var items = await _db.WorkItems
            .Where(i => i.ProjectId == status.ProjectId && i.Status == status.Key)
            .ToListAsync();
        foreach (var item in items) item.Status = moveToKey;
        if (_db.Entry(status).State == EntityState.Detached) _db.WorkStatuses.Attach(status);
        _db.WorkStatuses.Remove(status);
        await _db.SaveChangesAsync();
    }

    public async Task<List<WorkItem>> GetItemsAsync(Guid projectId)
    {
        return await _db.WorkItems.AsNoTracking()
            .Where(i => i.ProjectId == projectId && !i.IsDeleted)
            .ToListAsync();
    }

    public async Task<WorkItem?> GetItemAsync(Guid workItemId)
    {
        return await _db.WorkItems.FirstOrDefaultAsync(i => i.WorkItemId == workItemId && !i.IsDeleted);
    }

    public async Task<List<WorkItem>> GetChildrenAsync(Guid parentId)
    {
        return await _db.WorkItems.AsNoTracking()
            .Where(i => i.ParentId == parentId && !i.IsDeleted)
            .ToListAsync();
    }

    public async Task<double> GetMaxRankAsync(Guid projectId)
    {
        var ranks = _db.WorkItems.Where(i => i.ProjectId == projectId).Select(i => (double?)i.Rank);
        return await ranks.MaxAsync() ?? 0;
    }

    public async Task AddItemAsync(WorkItem item, WorkItemHistory created)
    {
        // Two people creating at the same moment could read the same NextNumber. The unique index on
        // (ProjectId, Number) refuses the second save; read the number again and try once more.
        for (var attempt = 1; ; attempt++)
        {
            var project = await _db.WorkProjects.FirstAsync(p => p.ProjectId == item.ProjectId);
            item.Number = project.NextNumber;
            project.NextNumber = project.NextNumber + 1;
            _db.WorkItems.Add(item);
            _db.WorkItemHistories.Add(created);
            try
            {
                await _db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException) when (attempt < NumberAttempts)
            {
                _db.ChangeTracker.Clear();
            }
        }
    }

    public async Task SaveItemAsync(WorkItem item, List<WorkItemHistory> history)
    {
        if (_db.Entry(item).State == EntityState.Detached) _db.WorkItems.Update(item);
        _db.WorkItemHistories.AddRange(history);
        await _db.SaveChangesAsync();
    }

    public async Task<List<WorkItemComment>> GetCommentsAsync(Guid workItemId)
    {
        return await _db.WorkItemComments.AsNoTracking()
            .Where(c => c.WorkItemId == workItemId && !c.IsDeleted)
            .ToListAsync();
    }

    public async Task<WorkItemComment?> GetCommentAsync(Guid commentId)
    {
        return await _db.WorkItemComments.FirstOrDefaultAsync(c => c.CommentId == commentId && !c.IsDeleted);
    }

    public async Task AddCommentAsync(WorkItemComment comment)
    {
        _db.WorkItemComments.Add(comment);
        await _db.SaveChangesAsync();
    }

    public async Task SaveCommentAsync(WorkItemComment comment)
    {
        if (_db.Entry(comment).State == EntityState.Detached) _db.WorkItemComments.Update(comment);
        await _db.SaveChangesAsync();
    }

    public async Task<List<WorkItemHistory>> GetHistoryAsync(Guid workItemId)
    {
        return await _db.WorkItemHistories.AsNoTracking()
            .Where(h => h.WorkItemId == workItemId)
            .ToListAsync();
    }

    public async Task<List<CmsUser>> GetActiveUsersAsync()
    {
        return await _db.CmsUsers.AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .ToListAsync();
    }

    public async Task<Dictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        return await _db.CmsUsers.AsNoTracking()
            .Where(u => ids.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, u => u.FullName);
    }

    public async Task<bool> SprintBelongsToProjectAsync(Guid sprintId, Guid projectId)
    {
        return await _db.WorkSprints.AnyAsync(s => s.SprintId == sprintId && s.ProjectId == projectId);
    }
}
