using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

/// <summary>
/// Comments on documents of the Daily Work Board. Everyone who can open a document can read and
/// write comments on it; a document they cannot open answers "not found".
/// </summary>
public interface IDocCommentsService
{
    /// <summary>Every comment of the document, oldest first. Replies carry their thread's ParentId.</summary>
    Task<WorkResult<List<DocCommentDto>>> GetAsync(DocsActor actor, Guid documentId);
    Task<WorkResult<DocCommentDto>> AddAsync(DocsActor actor, Guid documentId, SaveDocCommentRequest request);
    /// <summary>People change only their own comments.</summary>
    Task<WorkResult<DocCommentDto>> EditAsync(DocsActor actor, Guid commentId, SaveDocCommentRequest request);
    /// <summary>People remove their own comments; docs.delete removes anyone's.</summary>
    Task<WorkResult<bool>> DeleteAsync(DocsActor actor, Guid commentId);
    /// <summary>Resolves or reopens a thread, given its first comment.</summary>
    Task<WorkResult<DocCommentDto>> ResolveAsync(DocsActor actor, Guid commentId, bool isResolved);

    Task<List<DocNotificationDto>> GetNotificationsAsync(DocsActor actor);
    /// <summary>An empty list marks every notification read.</summary>
    Task MarkNotificationsReadAsync(Guid userId, List<Guid> notificationIds);
}
