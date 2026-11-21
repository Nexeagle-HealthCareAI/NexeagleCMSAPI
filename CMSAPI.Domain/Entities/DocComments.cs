namespace CMSAPI.Domain.Entities;

// Daily Work Board, comments on documents (CMSDatabase, script 31_CMSDatabase_DocComments.sql).

/// <summary>
/// A comment on a document. ParentId null starts a thread; a reply points at that first comment.
/// A thread is resolved on its first comment.
/// </summary>
public class DocComment
{
    public Guid CommentId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? ParentId { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    /// <summary>The text the comment is about, when the person selected some.</summary>
    public string? Quote { get; set; }
    public bool IsResolved { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DocCommentMention
{
    public Guid CommentId { get; set; }
    public Guid UserId { get; set; }
}

/// <summary>Kind: Mention (tagged), Reply (in a thread you started) or Comment (on a document you created).</summary>
public class DocNotification
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "Mention";
    public Guid DocumentId { get; set; }
    public Guid CommentId { get; set; }
    public Guid? ActorUserId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
