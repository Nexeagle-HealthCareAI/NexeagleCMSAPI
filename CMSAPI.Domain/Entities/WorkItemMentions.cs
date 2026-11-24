namespace CMSAPI.Domain.Entities;

// Tagging people in work items (33_CMSDatabase_WorkItemMentions.sql).

/// <summary>Someone tagged in a work item now. Place: comment (CommentId says which), description or blocked.</summary>
public class WorkItemMention
{
    public Guid MentionId { get; set; }
    public Guid WorkItemId { get; set; }
    public string Place { get; set; } = "comment";
    public Guid? CommentId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Kind: Mention (someone tagged you) in that Place of the item.</summary>
public class WorkItemNotification
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "Mention";
    public Guid WorkItemId { get; set; }
    public string Place { get; set; } = "comment";
    public Guid? CommentId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? Excerpt { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
