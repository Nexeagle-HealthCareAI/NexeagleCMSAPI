using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

/// <summary>Where in a work item someone can be tagged.</summary>
public static class WorkMentionPlaces
{
    public const string Comment = "comment";
    public const string Description = "description";
    public const string Blocked = "blocked";
}

/// <summary>
/// Tagging people (@Name) in a work item's comments, description and blocked reason, and telling
/// them. Only people who can open the item's board can be tagged (everyone active on an open board,
/// its members on a members-only one), and only when "@Their Name" is really in the text; anyone
/// else is left out rather than refused. Nobody is told twice about the same tag.
/// </summary>
public interface IWorkMentionService
{
    /// <summary>Who can be tagged in this item, apart from the person asking.</summary>
    Task<List<Guid>> TaggableAsync(Guid workItemId, Guid? me);

    /// <summary>Saves who is tagged in a new comment and tells them. Returns who was tagged.</summary>
    Task<List<Guid>> TagCommentAsync(Guid workItemId, Guid commentId, string body, List<Guid>? wanted, Guid? actor);

    /// <summary>
    /// place: description | blocked. Replaces who is tagged there with the people in `wanted`
    /// (empty when the text is gone) and tells the newly tagged. A null `wanted` changes nothing.
    /// </summary>
    Task TagTextAsync(Guid workItemId, string place, string? text, List<Guid>? wanted, Guid? actor);

    /// <summary>Fills each comment's MentionUserIds.</summary>
    Task FillAsync(WorkItemDetailDto item);

    /// <summary>A removed comment's tags no longer call anyone.</summary>
    Task CommentRemovedAsync(Guid commentId);

    Task<List<WorkItemNotificationDto>> GetNotificationsAsync(WorkActor actor);
    Task MarkNotificationsReadAsync(Guid userId, List<Guid> notificationIds);
}
