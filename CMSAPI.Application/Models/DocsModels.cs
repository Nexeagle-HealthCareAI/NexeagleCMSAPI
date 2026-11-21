namespace CMSAPI.Application.Models;

// Daily Work Board, tab "Docs": what the API sends and accepts.

/// <summary>Who is asking, and what their permissions allow. Built by DocsController from the login.</summary>
public class DocsActor
{
    public Guid? UserId { get; set; }
    /// <summary>docs.edit</summary>
    public bool CanEdit { get; set; }
    /// <summary>docs.delete</summary>
    public bool CanDelete { get; set; }
    /// <summary>docs.sensitive: opens every restricted folder and manages who else may.</summary>
    public bool CanSensitive { get; set; }
}

public static class DocsRules
{
    public const long MaxFileBytes = 25L * 1024 * 1024;
    public const int MaxNameLength = 255;
    public const int MaxFolderNameLength = 150;
    public const int MaxPageContentLength = 2_000_000;
    public const int MaxTags = 10;

    /// <summary>File types the team may upload. Programs and scripts are left out on purpose.</summary>
    public static readonly string[] AllowedExtensions =
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".csv", ".json",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".zip",
    };

    public static readonly string[] FolderColors = { "grey", "blue", "green", "amber", "red", "purple" };

    public static bool IsAllowedFile(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return Array.IndexOf(AllowedExtensions, ext) >= 0;
    }
}

public class DocFolderDto
{
    public Guid FolderId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsRestricted { get; set; }
    /// <summary>The caller may change who can open this folder.</summary>
    public bool CanManageAccess { get; set; }
    public int DocumentCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DocDto
{
    public Guid DocumentId { get; set; }
    public Guid? FolderId { get; set; }
    /// <summary>Page or File.</summary>
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public int VersionNumber { get; set; }
    public List<string> Tags { get; set; } = new List<string>();
    public string? CreatedByName { get; set; }
    public string? UpdatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int LinkedItemCount { get; set; }
    /// <summary>Folder names from the top down; empty at the top level.</summary>
    public List<string> Path { get; set; } = new List<string>();
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByName { get; set; }
}

public class DocVersionDto
{
    public Guid VersionId { get; set; }
    public int VersionNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A work item a document is tagged on.</summary>
public class DocLinkedItemDto
{
    public Guid WorkItemId { get; set; }
    public Guid ProjectId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
}

public class DocDetailDto : DocDto
{
    /// <summary>Page: the editor's JSON. Null for a file.</summary>
    public string? Content { get; set; }
    public List<DocVersionDto> Versions { get; set; } = new List<DocVersionDto>();
    public List<DocLinkedItemDto> LinkedItems { get; set; } = new List<DocLinkedItemDto>();
}

public class DocLogDto
{
    public string Action { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class DocBinDto
{
    public List<DocFolderDto> Folders { get; set; } = new List<DocFolderDto>();
    public List<DocDto> Documents { get; set; } = new List<DocDto>();
}

/// <summary>A file ready to send: the stream, and what to call it.</summary>
public class DocFileDto
{
    public System.IO.Stream Content { get; set; } = System.IO.Stream.Null;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
}

public class SaveDocFolderRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public string? Color { get; set; }
    public bool IsRestricted { get; set; }
}

public class SetDocFolderAccessRequest
{
    public List<Guid> UserIds { get; set; } = new List<Guid>();
}

public class CreateDocPageRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid? FolderId { get; set; }
}

/// <summary>A page's text. Content is the editor's JSON, ContentText the same as plain text (for search).</summary>
public class SaveDocPageRequest
{
    public string? Content { get; set; }
    public string? ContentText { get; set; }
}

/// <summary>Name, place and tags of a page or file. Every field is sent; a null FolderId means the top level.</summary>
public class UpdateDocRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid? FolderId { get; set; }
    public List<string>? Tags { get; set; }
}

public class LinkDocRequest
{
    public Guid DocumentId { get; set; }
}
