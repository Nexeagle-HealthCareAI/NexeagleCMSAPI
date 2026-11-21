using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

/// <summary>
/// Team chat of the Daily Work Board. A person sees a Board channel when they can open that board,
/// and a Group or Direct channel when they are in it. A channel they cannot see answers "not found".
/// </summary>
public interface IWorkChatService
{
    /// <summary>The caller's channels with unread counts; new messages of one channel; unread notifications.</summary>
    Task<WorkChatStateDto> GetStateAsync(WorkActor actor, Guid? channelId, DateTime? after);
    Task<WorkResult<WorkChatChannelDto>> CreateGroupAsync(WorkActor actor, CreateWorkChatGroupRequest request);
    /// <summary>The one conversation between the caller and this person, made on first use.</summary>
    Task<WorkResult<WorkChatChannelDto>> OpenDirectAsync(WorkActor actor, Guid otherUserId);

    /// <summary>Newest page, oldest first within it. before: load the page older than this moment.</summary>
    Task<WorkResult<List<WorkChatMessageDto>>> GetMessagesAsync(WorkActor actor, Guid channelId, DateTime? before, bool importantOnly);
    Task<WorkResult<WorkChatMessageDto>> SendAsync(WorkActor actor, Guid channelId, SendWorkChatMessageRequest request);
    Task<WorkResult<WorkChatMessageDto>> EscalateItemAsync(WorkActor actor, Guid workItemId, EscalateWorkItemRequest request);
    Task<WorkResult<WorkChatMessageDto>> MarkImportantAsync(WorkActor actor, Guid messageId, bool isImportant);
    /// <summary>People remove their own messages; canRemoveAny (work.delete) removes anyone's.</summary>
    Task<WorkResult<bool>> DeleteMessageAsync(WorkActor actor, Guid messageId, bool canRemoveAny);
    Task<WorkResult<bool>> MarkReadAsync(WorkActor actor, Guid channelId);

    Task<List<WorkNotificationDto>> GetNotificationsAsync(Guid userId);
    Task MarkNotificationsReadAsync(Guid userId, List<Guid> notificationIds);
}
