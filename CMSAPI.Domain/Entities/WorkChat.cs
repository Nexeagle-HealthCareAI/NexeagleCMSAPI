namespace CMSAPI.Domain.Entities;

// Daily Work Board, team chat (CMSDatabase, script 30_CMSDatabase_WorkChat.sql).

/// <summary>A conversation. Kind: Board (one per project), Group (named) or Direct (two people).</summary>
public class WorkChatChannel
{
    public Guid ChannelId { get; set; }
    public string Kind { get; set; } = "Group";
    public Guid? ProjectId { get; set; }
    public string? Name { get; set; }
    /// <summary>Direct only: the two user ids, smaller first, joined with ':'.</summary>
    public string? DirectKey { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
}

/// <summary>
/// Someone in a Group or Direct channel (IsMember), and for every kind of channel how far that
/// person has read. In a Board channel the row is only a read marker: who is in it comes from who
/// can open the board.
/// </summary>
public class WorkChatMember
{
    public Guid ChannelId { get; set; }
    public Guid UserId { get; set; }
    public bool IsMember { get; set; } = true;
    public DateTime? LastReadAt { get; set; }
    public DateTime JoinedAt { get; set; }
}

public class WorkChatMessage
{
    public Guid MessageId { get; set; }
    public Guid ChannelId { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsImportant { get; set; }
    public bool IsEscalation { get; set; }
    public Guid? WorkItemId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WorkChatMention
{
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
}

/// <summary>Something a person should be told. Kind: Mention, Escalation or Direct.</summary>
public class WorkNotification
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "Mention";
    public Guid ChannelId { get; set; }
    public Guid MessageId { get; set; }
    public Guid? ActorUserId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
