namespace CMSAPI.Application.Models;

// Daily Work Board, comments on documents.

public static class DocCommentRules
{
    public const int MaxBody = 4000;
    public const int MaxQuote = 500;
}

public class DocCommentDto
{
    public Guid CommentId { get; set; }
    public Guid DocumentId { get; set; }
    /// <summary>Null for the first comment of a thread.</summary>
    public Guid? ParentId { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    /// <summary>Empty when the comment was removed.</summary>
    public string Body { get; set; } = string.Empty;
    public string? Quote { get; set; }
    public List<Guid> MentionUserIds { get; set; } = new List<Guid>();
    /// <summary>Meaningful on the first comment of a thread.</summary>
    public bool IsResolved { get; set; }
    public string? ResolvedByName { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SaveDocCommentRequest
{
    public string Body { get; set; } = string.Empty;
    /// <summary>New comments only: the text selected in the document.</summary>
    public string? Quote { get; set; }
    /// <summary>New comments only: the first comment of the thread being answered.</summary>
    public Guid? ParentId { get; set; }
    public List<Guid> MentionUserIds { get; set; } = new List<Guid>();
}

public class ResolveDocCommentRequest
{
    public bool IsResolved { get; set; }
}

public class DocNotificationDto
{
    public Guid NotificationId { get; set; }
    /// <summary>Mention, Reply or Comment.</summary>
    public string Kind { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public string DocumentName { get; set; } = string.Empty;
    public Guid CommentId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ReadDocNotificationsRequest
{
    /// <summary>Empty means every notification.</summary>
    public List<Guid> NotificationIds { get; set; } = new List<Guid>();
}
