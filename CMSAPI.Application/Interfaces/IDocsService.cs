using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

/// <summary>
/// The Docs tab of the Daily Work Board. Every call takes the caller (DocsActor): what a person can
/// see depends on restricted folders, so there is no "get everything" here.
/// </summary>
public interface IDocsService
{
    // Folders
    Task<List<DocFolderDto>> GetFoldersAsync(DocsActor actor);
    Task<WorkResult<DocFolderDto>> CreateFolderAsync(DocsActor actor, SaveDocFolderRequest request);
    Task<WorkResult<DocFolderDto>> UpdateFolderAsync(DocsActor actor, Guid folderId, SaveDocFolderRequest request);
    Task<WorkResult<List<Guid>>> GetFolderAccessAsync(DocsActor actor, Guid folderId);
    Task<WorkResult<List<Guid>>> SetFolderAccessAsync(DocsActor actor, Guid folderId, SetDocFolderAccessRequest request);
    Task<WorkResult<bool>> DeleteFolderAsync(DocsActor actor, Guid folderId);
    Task<WorkResult<bool>> RestoreFolderAsync(DocsActor actor, Guid folderId);

    // Documents
    /// <summary>Documents in one folder (null: the top level), or everywhere the caller can see when search has text.</summary>
    Task<WorkResult<List<DocDto>>> ListAsync(DocsActor actor, Guid? folderId, string? search);
    Task<WorkResult<DocDetailDto>> GetAsync(DocsActor actor, Guid documentId);
    /// <summary>True when the document exists, is not in the bin and this person may open it. Logs nothing.</summary>
    Task<bool> CanSeeDocumentAsync(DocsActor actor, Guid documentId);
    Task<WorkResult<DocDetailDto>> CreatePageAsync(DocsActor actor, CreateDocPageRequest request);
    Task<WorkResult<DocDetailDto>> SavePageAsync(DocsActor actor, Guid documentId, SaveDocPageRequest request);
    Task<WorkResult<DocDetailDto>> UpdateAsync(DocsActor actor, Guid documentId, UpdateDocRequest request);
    /// <summary>A file with the same name already in the folder gets a new version; otherwise a new document is made.</summary>
    Task<WorkResult<DocDetailDto>> UploadAsync(DocsActor actor, Guid? folderId, string fileName, string? contentType, long sizeBytes, Stream content);
    /// <summary>versionId null: the current file.</summary>
    Task<WorkResult<DocFileDto>> OpenFileAsync(DocsActor actor, Guid documentId, Guid? versionId);
    Task<WorkResult<bool>> DeleteAsync(DocsActor actor, Guid documentId);
    Task<WorkResult<DocDetailDto>> RestoreAsync(DocsActor actor, Guid documentId);
    Task<WorkResult<DocBinDto>> GetBinAsync(DocsActor actor);
    Task<WorkResult<List<DocLogDto>>> GetLogAsync(DocsActor actor, Guid documentId);

    // Documents tagged on work items
    Task<WorkResult<List<DocDto>>> GetItemDocumentsAsync(DocsActor actor, Guid workItemId);
    Task<WorkResult<bool>> LinkAsync(DocsActor actor, Guid workItemId, Guid documentId);
    Task<WorkResult<bool>> UnlinkAsync(DocsActor actor, Guid workItemId, Guid documentId);
}
