using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface IWorkService
{
    Task<List<WorkProjectDto>> GetProjectsAsync();
    Task<WorkResult<WorkProjectDto>> CreateProjectAsync(CreateWorkProjectRequest request, Guid? actorUserId);
    Task<List<WorkUserDto>> GetUsersAsync();

    Task<WorkResult<WorkStatusDto>> AddStatusAsync(Guid projectId, SaveWorkStatusRequest request);
    Task<WorkResult<WorkStatusDto>> UpdateStatusAsync(Guid statusId, SaveWorkStatusRequest request);
    Task<WorkResult<List<WorkStatusDto>>> ReorderStatusesAsync(Guid projectId, ReorderWorkStatusesRequest request);
    /// <summary>moveToKey: where the column's items go. Needed only when the column is not empty.</summary>
    Task<WorkResult<bool>> DeleteStatusAsync(Guid statusId, string? moveToKey);

    Task<WorkResult<List<WorkItemDto>>> GetItemsAsync(Guid projectId);
    Task<WorkResult<WorkItemDetailDto>> GetItemAsync(Guid workItemId);
    Task<WorkResult<WorkItemDetailDto>> CreateItemAsync(CreateWorkItemRequest request, Guid? actorUserId);
    Task<WorkResult<WorkItemDetailDto>> UpdateItemAsync(Guid workItemId, UpdateWorkItemRequest request, Guid? actorUserId);
    Task<WorkResult<bool>> DeleteItemAsync(Guid workItemId, Guid? actorUserId);

    Task<WorkResult<WorkCommentDto>> AddCommentAsync(Guid workItemId, AddWorkCommentRequest request, Guid? actorUserId);
    /// <summary>People remove their own comments; canManage (the work.delete permission) lets someone remove anyone's.</summary>
    Task<WorkResult<bool>> DeleteCommentAsync(Guid commentId, Guid? actorUserId, bool canManage);
}
