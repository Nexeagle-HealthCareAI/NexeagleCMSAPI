using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface IWorkService
{
    Task<List<WorkProjectDto>> GetProjectsAsync();
    Task<WorkResult<WorkProjectDto>> CreateProjectAsync(CreateWorkProjectRequest request, Guid? actorUserId);
    Task<List<WorkUserDto>> GetUsersAsync();

    Task<WorkResult<List<WorkItemDto>>> GetItemsAsync(Guid projectId);
    Task<WorkResult<WorkItemDetailDto>> GetItemAsync(Guid workItemId);
    Task<WorkResult<WorkItemDetailDto>> CreateItemAsync(CreateWorkItemRequest request, Guid? actorUserId);
    Task<WorkResult<WorkItemDetailDto>> UpdateItemAsync(Guid workItemId, UpdateWorkItemRequest request, Guid? actorUserId);
    Task<WorkResult<bool>> DeleteItemAsync(Guid workItemId, Guid? actorUserId);

    Task<WorkResult<WorkCommentDto>> AddCommentAsync(Guid workItemId, AddWorkCommentRequest request, Guid? actorUserId);
    /// <summary>People remove their own comments; canManage lets someone remove anyone's.</summary>
    Task<WorkResult<bool>> DeleteCommentAsync(Guid commentId, Guid? actorUserId, bool canManage);
}
