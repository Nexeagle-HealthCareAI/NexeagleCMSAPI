namespace CMSAPI.Domain.Entities;

// Daily Work Board, tab "Docs" (CMSDatabase, script 27_CMSDatabase_WorkDocs.sql).

/// <summary>A folder. IsRestricted locks it, and everything under it, to the people in DocFolderAccess.</summary>
public class DocFolder
{
    public Guid FolderId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsRestricted { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
}

/// <summary>One person allowed into a restricted folder.</summary>
public class DocFolderAccess
{
    public Guid FolderId { get; set; }
    public Guid UserId { get; set; }
    public Guid? AddedByUserId { get; set; }
    public DateTime AddedAt { get; set; }
}

/// <summary>
/// A page written in the CMS editor (Kind "Page": Content holds the editor's JSON) or an uploaded
/// file (Kind "File": the bytes are in object storage under StorageKey, never in the database).
/// </summary>
public class WorkDocument
{
    public Guid DocumentId { get; set; }
    /// <summary>Null means the top level.</summary>
    public Guid? FolderId { get; set; }
    public string Kind { get; set; } = "Page";
    public string Name { get; set; } = string.Empty;
    public string? Content { get; set; }
    /// <summary>The page as plain text, for search.</summary>
    public string? ContentText { get; set; }
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string? StorageKey { get; set; }
    public int VersionNumber { get; set; } = 1;
    /// <summary>Comma-separated, lower case.</summary>
    public string? Tags { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
}

/// <summary>An earlier state of a document: a page's previous text, or a file that was replaced.</summary>
public class DocVersion
{
    public Guid VersionId { get; set; }
    public Guid DocumentId { get; set; }
    public int VersionNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string? StorageKey { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DocAccessLog
{
    public Guid LogId { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? FolderId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A document tagged on a work item.</summary>
public class WorkItemDocument
{
    public Guid WorkItemId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? AddedByUserId { get; set; }
    public DateTime AddedAt { get; set; }
}
