using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

/// <summary>
/// Who can open which board of the Daily Work Board. A board (project) is open to everyone with
/// work.view until it is switched to members-only; then only its members and holders of
/// work.members can open it. The CanOpen checks answer true for an id that does not exist, so the
/// caller's own "not found" answer is the one people see.
/// </summary>
public interface IWorkAccessService
{
    /// <summary>The members-only boards this person may not open.</summary>
    Task<HashSet<Guid>> ClosedProjectIdsAsync(WorkActor actor);
    Task<bool> CanOpenProjectAsync(Guid projectId, WorkActor actor);
    Task<bool> CanOpenItemAsync(Guid workItemId, WorkActor actor);
    Task<bool> CanOpenStatusAsync(Guid statusId, WorkActor actor);
    Task<bool> CanOpenCommentAsync(Guid commentId, WorkActor actor);

    Task<WorkResult<WorkProjectAccessDto>> GetAccessAsync(Guid projectId);
    /// <summary>Replaces the board's switch and member list, and logs each difference.</summary>
    Task<WorkResult<WorkProjectAccessDto>> SaveAccessAsync(Guid projectId, SaveWorkProjectAccessRequest request, Guid? actorUserId);
}
