namespace CMSAPI.Application.Models;

// Daily Work Board, team chat. Channels are Board (one per project), Group or Direct.

public static class WorkChatRules
{
    public const int MaxBody = 4000;
    public const int MaxGroupName = 80;
    public const int PageSize = 50;

    /// <summary>The same key whichever of the two people asks.</summary>
    public static string DirectKey(Guid a, Guid b) =>
        string.CompareOrdinal(a.ToString("N"), b.ToString("N")) <= 0 ? $"{a:N}:{b:N}" : $"{b:N}:{a:N}";
}

public class WorkChatChannelDto
{
    public Guid ChannelId { get; set; }
    /// <summary>Board, Group or Direct.</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Board: the project's name. Group: its name. Direct: the other person's name.</summary>
    public string Name { get; set; } = string.Empty;
    public Guid? ProjectId { get; set; }
    public string? ProjectKey { get; set; }
    /// <summary>Direct: the other person.</summary>
    public Guid? OtherUserId { get; set; }
    /// <summary>Group and Direct: who is in it. Empty for Board (everyone who can open the board).</summary>
    public List<Guid> MemberUserIds { get; set; } = new List<Guid>();
    public int Unread { get; set; }
    public string? LastMessageText { get; set; }
    public string? LastMessageBy { get; set; }
    public DateTime? LastMessageAt { get; set; }
}

public class WorkChatMessageDto
{
    public Guid MessageId { get; set; }
    public Guid ChannelId { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    /// <summary>Empty when the message was removed.</summary>
    public string Body { get; set; } = string.Empty;
    public bool IsImportant { get; set; }
    public bool IsEscalation { get; set; }
    public bool IsDeleted { get; set; }
    public List<Guid> MentionUserIds { get; set; } = new List<Guid>();
    public Guid? WorkItemId { get; set; }
    public string? WorkItemKey { get; set; }
    public string? WorkItemTitle { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WorkNotificationDto
{
    public Guid NotificationId { get; set; }
    /// <summary>Mention, Escalation or Direct.</summary>
    public string Kind { get; set; } = string.Empty;
    public Guid ChannelId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public Guid MessageId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Everything the open chat asks for in one call, every few seconds.</summary>
public class WorkChatStateDto
{
    public List<WorkChatChannelDto> Channels { get; set; } = new List<WorkChatChannelDto>();
    /// <summary>Messages in the open channel newer than the caller's "after".</summary>
    public List<WorkChatMessageDto> NewMessages { get; set; } = new List<WorkChatMessageDto>();
    public int UnreadNotifications { get; set; }
}

public class CreateWorkChatGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public List<Guid> MemberUserIds { get; set; } = new List<Guid>();
}

public class OpenWorkChatDirectRequest
{
    public Guid UserId { get; set; }
}

public class SendWorkChatMessageRequest
{
    public string Body { get; set; } = string.Empty;
    public bool IsImportant { get; set; }
    /// <summary>A call for help. Needs at least one person tagged.</summary>
    public bool IsEscalation { get; set; }
    public List<Guid> MentionUserIds { get; set; } = new List<Guid>();
    public Guid? WorkItemId { get; set; }
}

/// <summary>Escalating a work item: a message in its board's channel that tags these people.</summary>
public class EscalateWorkItemRequest
{
    public List<Guid> UserIds { get; set; } = new List<Guid>();
    public string? Note { get; set; }
}

public class MarkWorkChatImportantRequest
{
    public bool IsImportant { get; set; }
}

public class ReadWorkNotificationsRequest
{
    /// <summary>Empty means every notification.</summary>
    public List<Guid> NotificationIds { get; set; } = new List<Guid>();
}
