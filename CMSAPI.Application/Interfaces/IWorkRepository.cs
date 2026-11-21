using CMSAPI.Domain.Entities;

namespace CMSAPI.Application.Interfaces;

/// <summary>Storage for the Daily Work Board. Deleted items and comments are never returned.</summary>
public interface IWorkRepository
{
    Task<List<WorkProject>> GetProjectsAsync();
    Task<WorkProject?> GetProjectAsync(Guid projectId);
    Task<bool> ProjectKeyExistsAsync(string key);
    Task AddProjectAsync(WorkProject project);

    /// <summary>Columns of one project, in board order.</summary>
    Task<List<WorkStatus>> GetStatusesAsync(Guid projectId);
    /// <summary>Columns of every project.</summary>
    Task<List<WorkStatus>> GetAllStatusesAsync();
    Task<WorkStatus?> GetStatusAsync(Guid statusId);
    Task AddStatusesAsync(List<WorkStatus> statuses);
    Task SaveStatusesAsync(List<WorkStatus> statuses);
    Task<int> CountItemsInStatusAsync(Guid projectId, string statusKey);
    /// <summary>Removes a column. Items still in it (deleted ones included) move to moveToKey first.</summary>
    Task DeleteStatusAsync(WorkStatus status, string moveToKey);

    Task<List<WorkItem>> GetItemsAsync(Guid projectId);
    Task<WorkItem?> GetItemAsync(Guid workItemId);
    Task<List<WorkItem>> GetChildrenAsync(Guid parentId);
    Task<double> GetMaxRankAsync(Guid projectId);
    /// <summary>Gives the item the project's next number and saves it with its "created" history row.</summary>
    Task AddItemAsync(WorkItem item, WorkItemHistory created);
    Task SaveItemAsync(WorkItem item, List<WorkItemHistory> history);

    Task<List<WorkItemComment>> GetCommentsAsync(Guid workItemId);
    Task<WorkItemComment?> GetCommentAsync(Guid commentId);
    Task AddCommentAsync(WorkItemComment comment);
    Task SaveCommentAsync(WorkItemComment comment);

    Task<List<WorkItemHistory>> GetHistoryAsync(Guid workItemId);

    /// <summary>Active CMS staff, by name.</summary>
    Task<List<CmsUser>> GetActiveUsersAsync();
    /// <summary>Names for the given people, active or not (an item keeps showing who it belonged to).</summary>
    Task<Dictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds);
    Task<bool> SprintBelongsToProjectAsync(Guid sprintId, Guid projectId);
}
