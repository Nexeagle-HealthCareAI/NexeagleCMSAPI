using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Services;

/// <summary>
/// The Docs tab of the Daily Work Board: folders, pages, uploaded files, versions, the bin, restricted
/// folders and the access log.
///
/// Unlike the other services this one lives next to CmsDbContext and uses it directly: almost every
/// rule here is "which rows may this person see", which is a query, so a repository in between would
/// only repeat each method.
///
/// Who sees what
///   A restricted folder is open to the people listed in DocFolderAccess, to whoever created it, and
///   to anyone holding docs.sensitive. The lock covers everything under the folder. What a person
///   may not open is left out of every answer (tree, lists, search, links) as if it did not exist,
///   and asking for it by id answers "not found".
/// </summary>
public class DocsService : IDocsService
{
    private static readonly TimeSpan PageVersionGap = TimeSpan.FromMinutes(10);
    private const int MaxSearchResults = 200;

    private readonly CmsDbContext _db;
    private readonly IDocFileStore _files;

    public DocsService(CmsDbContext db, IDocFileStore files)
    {
        _db = db;
        _files = files;
    }

    // ── Folders ─────────────────────────────────────────────────────────────────────────────────

    public async Task<List<DocFolderDto>> GetFoldersAsync(DocsActor actor)
    {
        var map = await LoadMapAsync(actor);
        var visible = map.Folders.Values.Where(f => map.CanSee(f.FolderId)).ToList();
        var ids = visible.Select(f => f.FolderId).ToList();

        var counts = await _db.WorkDocuments.AsNoTracking()
            .Where(d => !d.IsDeleted && d.FolderId != null && ids.Contains(d.FolderId.Value))
            .GroupBy(d => d.FolderId)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToListAsync();
        var countOf = counts.Where(c => c.FolderId.HasValue).ToDictionary(c => c.FolderId!.Value, c => c.Count);

        return visible
            .OrderBy(f => f.Name)
            .Select(f => MapFolder(f, actor, countOf.TryGetValue(f.FolderId, out var n) ? n : 0))
            .ToList();
    }

    public async Task<WorkResult<DocFolderDto>> CreateFolderAsync(DocsActor actor, SaveDocFolderRequest request)
    {
        if (!actor.CanEdit) return WorkResult<DocFolderDto>.Fail(403, "You cannot change documents.");
        var name = Clean(request.Name);
        if (name.Length == 0) return WorkResult<DocFolderDto>.Fail(400, "Give the folder a name.");
        if (name.Length > DocsRules.MaxFolderNameLength)
            return WorkResult<DocFolderDto>.Fail(400, $"The name can be up to {DocsRules.MaxFolderNameLength} characters.");
        if (!ColorOk(request.Color)) return WorkResult<DocFolderDto>.Fail(400, "Choose a colour from the list.");

        var map = await LoadMapAsync(actor);
        if (request.ParentId.HasValue && !map.CanSee(request.ParentId.Value))
            return WorkResult<DocFolderDto>.Fail(404, "The folder to put it in was not found.");

        var now = DateTime.UtcNow;
        var folder = new DocFolder
        {
            FolderId = Guid.NewGuid(),
            ParentId = request.ParentId,
            Name = name,
            Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color,
            IsRestricted = request.IsRestricted,
            CreatedByUserId = actor.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.DocFolders.Add(folder);
        // Whoever locks a folder can always get back in.
        if (folder.IsRestricted && actor.UserId.HasValue)
            _db.DocFolderAccesses.Add(new DocFolderAccess { FolderId = folder.FolderId, UserId = actor.UserId.Value, AddedByUserId = actor.UserId, AddedAt = now });
        Log(null, folder.FolderId, actor, "create", $"Folder {name}");
        await _db.SaveChangesAsync();
        return WorkResult<DocFolderDto>.Success(MapFolder(folder, actor, 0), "Folder created.");
    }

    public async Task<WorkResult<DocFolderDto>> UpdateFolderAsync(DocsActor actor, Guid folderId, SaveDocFolderRequest request)
    {
        if (!actor.CanEdit) return WorkResult<DocFolderDto>.Fail(403, "You cannot change documents.");
        var map = await LoadMapAsync(actor);
        if (!map.CanSee(folderId)) return WorkResult<DocFolderDto>.Fail(404, "Folder not found.");

        var name = Clean(request.Name);
        if (name.Length == 0) return WorkResult<DocFolderDto>.Fail(400, "Give the folder a name.");
        if (name.Length > DocsRules.MaxFolderNameLength)
            return WorkResult<DocFolderDto>.Fail(400, $"The name can be up to {DocsRules.MaxFolderNameLength} characters.");
        if (!ColorOk(request.Color)) return WorkResult<DocFolderDto>.Fail(400, "Choose a colour from the list.");

        var folder = await _db.DocFolders.FirstAsync(f => f.FolderId == folderId);

        if (request.ParentId != folder.ParentId)
        {
            if (request.ParentId.HasValue)
            {
                if (!map.CanSee(request.ParentId.Value)) return WorkResult<DocFolderDto>.Fail(404, "The folder to move it into was not found.");
                // A folder cannot go inside itself or anything under it.
                if (request.ParentId.Value == folderId || map.IsUnder(request.ParentId.Value, folderId))
                    return WorkResult<DocFolderDto>.Fail(400, "A folder cannot be moved into itself.");
            }
            Log(null, folderId, actor, "move", $"Folder {folder.Name}");
            folder.ParentId = request.ParentId;
        }

        if (request.IsRestricted != folder.IsRestricted)
        {
            if (!CanManageAccess(folder, actor))
                return WorkResult<DocFolderDto>.Fail(403, "Only the person who made this folder, or someone who manages sensitive documents, can lock or unlock it.");
            folder.IsRestricted = request.IsRestricted;
            Log(null, folderId, actor, "access", request.IsRestricted ? "Folder locked" : "Folder unlocked");
            if (request.IsRestricted && actor.UserId.HasValue
                && !await _db.DocFolderAccesses.AnyAsync(a => a.FolderId == folderId && a.UserId == actor.UserId.Value))
                _db.DocFolderAccesses.Add(new DocFolderAccess { FolderId = folderId, UserId = actor.UserId.Value, AddedByUserId = actor.UserId, AddedAt = DateTime.UtcNow });
        }

        if (folder.Name != name)
        {
            Log(null, folderId, actor, "rename", $"{folder.Name} -> {name}");
            folder.Name = name;
        }
        folder.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color;
        folder.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var count = await _db.WorkDocuments.CountAsync(d => d.FolderId == folderId && !d.IsDeleted);
        return WorkResult<DocFolderDto>.Success(MapFolder(folder, actor, count), "Folder saved.");
    }

    public async Task<WorkResult<List<Guid>>> GetFolderAccessAsync(DocsActor actor, Guid folderId)
    {
        var map = await LoadMapAsync(actor);
        if (!map.CanSee(folderId)) return WorkResult<List<Guid>>.Fail(404, "Folder not found.");
        if (!CanManageAccess(map.Folders[folderId], actor))
            return WorkResult<List<Guid>>.Fail(403, "You cannot see who has access to this folder.");

        var ids = await _db.DocFolderAccesses.AsNoTracking().Where(a => a.FolderId == folderId).Select(a => a.UserId).ToListAsync();
        return WorkResult<List<Guid>>.Success(ids);
    }

    public async Task<WorkResult<List<Guid>>> SetFolderAccessAsync(DocsActor actor, Guid folderId, SetDocFolderAccessRequest request)
    {
        var map = await LoadMapAsync(actor);
        if (!map.CanSee(folderId)) return WorkResult<List<Guid>>.Fail(404, "Folder not found.");
        if (!CanManageAccess(map.Folders[folderId], actor))
            return WorkResult<List<Guid>>.Fail(403, "You cannot change who has access to this folder.");

        var wanted = (request.UserIds ?? new List<Guid>()).Distinct().ToList();
        var known = await _db.CmsUsers.AsNoTracking().Where(u => wanted.Contains(u.UserId)).Select(u => u.UserId).ToListAsync();
        if (known.Count != wanted.Count) return WorkResult<List<Guid>>.Fail(400, "One of the people chosen was not found.");

        var current = await _db.DocFolderAccesses.Where(a => a.FolderId == folderId).ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var row in current.Where(a => !wanted.Contains(a.UserId)).ToList()) _db.DocFolderAccesses.Remove(row);
        foreach (var userId in wanted.Where(id => current.All(a => a.UserId != id)))
            _db.DocFolderAccesses.Add(new DocFolderAccess { FolderId = folderId, UserId = userId, AddedByUserId = actor.UserId, AddedAt = now });
        Log(null, folderId, actor, "access", $"{wanted.Count} people have access");
        await _db.SaveChangesAsync();
        return WorkResult<List<Guid>>.Success(wanted, "Access saved.");
    }

    public async Task<WorkResult<bool>> DeleteFolderAsync(DocsActor actor, Guid folderId)
    {
        if (!actor.CanDelete) return WorkResult<bool>.Fail(403, "You cannot delete documents.");
        var map = await LoadMapAsync(actor);
        if (!map.CanSee(folderId)) return WorkResult<bool>.Fail(404, "Folder not found.");

        // Only this folder is marked; everything under it disappears with it and returns on restore.
        var folder = await _db.DocFolders.FirstAsync(f => f.FolderId == folderId);
        folder.IsDeleted = true;
        folder.DeletedAt = DateTime.UtcNow;
        folder.DeletedByUserId = actor.UserId;
        Log(null, folderId, actor, "bin", $"Folder {folder.Name}");
        await _db.SaveChangesAsync();
        return WorkResult<bool>.Success(true, "Folder moved to the bin.");
    }

    public async Task<WorkResult<bool>> RestoreFolderAsync(DocsActor actor, Guid folderId)
    {
        if (!actor.CanDelete) return WorkResult<bool>.Fail(403, "You cannot restore documents.");
        var folder = await _db.DocFolders.FirstOrDefaultAsync(f => f.FolderId == folderId && f.IsDeleted);
        if (folder == null) return WorkResult<bool>.Fail(404, "Folder not found in the bin.");

        var map = await LoadMapAsync(actor);
        // If the folder it was in has gone too, it comes back at the top level.
        if (folder.ParentId.HasValue && !map.CanSee(folder.ParentId.Value)) folder.ParentId = null;
        folder.IsDeleted = false;
        folder.DeletedAt = null;
        folder.DeletedByUserId = null;
        Log(null, folderId, actor, "restore", $"Folder {folder.Name}");
        await _db.SaveChangesAsync();
        return WorkResult<bool>.Success(true, "Folder restored.");
    }

    // ── Documents: reading ──────────────────────────────────────────────────────────────────────

    public async Task<WorkResult<List<DocDto>>> ListAsync(DocsActor actor, Guid? folderId, string? search)
    {
        var map = await LoadMapAsync(actor);
        var text = (search ?? string.Empty).Trim();
        List<WorkDocument> docs;

        if (text.Length > 0)
        {
            // Search looks everywhere, then drops what the caller may not see.
            var found = await _db.WorkDocuments.AsNoTracking()
                .Where(d => !d.IsDeleted && (d.Name.Contains(text) || (d.Tags != null && d.Tags.Contains(text)) || (d.ContentText != null && d.ContentText.Contains(text))))
                .OrderByDescending(d => d.UpdatedAt)
                .Take(MaxSearchResults * 2)
                .ToListAsync();
            docs = found.Where(d => map.CanSeeDocument(d)).Take(MaxSearchResults).ToList();
        }
        else
        {
            if (folderId.HasValue && !map.CanSee(folderId.Value)) return WorkResult<List<DocDto>>.Fail(404, "Folder not found.");
            docs = await _db.WorkDocuments.AsNoTracking()
                .Where(d => !d.IsDeleted && d.FolderId == folderId)
                .OrderBy(d => d.Name)
                .ToListAsync();
        }

        return WorkResult<List<DocDto>>.Success(await MapDocsAsync(docs, map));
    }

    public async Task<WorkResult<DocDetailDto>> GetAsync(DocsActor actor, Guid documentId)
    {
        var map = await LoadMapAsync(actor);
        var doc = await _db.WorkDocuments.FirstOrDefaultAsync(d => d.DocumentId == documentId && !d.IsDeleted);
        if (doc == null || !map.CanSeeDocument(doc)) return WorkResult<DocDetailDto>.Fail(404, "Document not found.");

        Log(documentId, doc.FolderId, actor, "open", null);
        await _db.SaveChangesAsync();
        return WorkResult<DocDetailDto>.Success(await BuildDetailAsync(doc, map));
    }

    // ── Documents: writing ──────────────────────────────────────────────────────────────────────

    public async Task<WorkResult<DocDetailDto>> CreatePageAsync(DocsActor actor, CreateDocPageRequest request)
    {
        if (!actor.CanEdit) return WorkResult<DocDetailDto>.Fail(403, "You cannot change documents.");
        var name = Clean(request.Name);
        if (name.Length == 0) name = "Untitled page";
        if (name.Length > DocsRules.MaxNameLength) return WorkResult<DocDetailDto>.Fail(400, "The name is too long.");

        var map = await LoadMapAsync(actor);
        if (request.FolderId.HasValue && !map.CanSee(request.FolderId.Value)) return WorkResult<DocDetailDto>.Fail(404, "Folder not found.");

        var now = DateTime.UtcNow;
        var doc = new WorkDocument
        {
            DocumentId = Guid.NewGuid(),
            FolderId = request.FolderId,
            Kind = "Page",
            Name = name,
            VersionNumber = 1,
            CreatedByUserId = actor.UserId,
            CreatedAt = now,
            UpdatedByUserId = actor.UserId,
            UpdatedAt = now,
        };
        _db.WorkDocuments.Add(doc);
        Log(doc.DocumentId, doc.FolderId, actor, "create", $"Page {name}");
        await _db.SaveChangesAsync();
        return WorkResult<DocDetailDto>.Success(await BuildDetailAsync(doc, map), "Page created.");
    }

    public async Task<WorkResult<DocDetailDto>> SavePageAsync(DocsActor actor, Guid documentId, SaveDocPageRequest request)
    {
        if (!actor.CanEdit) return WorkResult<DocDetailDto>.Fail(403, "You cannot change documents.");
        var map = await LoadMapAsync(actor);
        var doc = await _db.WorkDocuments.FirstOrDefaultAsync(d => d.DocumentId == documentId && !d.IsDeleted);
        if (doc == null || !map.CanSeeDocument(doc)) return WorkResult<DocDetailDto>.Fail(404, "Document not found.");
        if (doc.Kind != "Page") return WorkResult<DocDetailDto>.Fail(400, "Only pages can be edited here. Upload a new version of a file instead.");
        if ((request.Content ?? string.Empty).Length > DocsRules.MaxPageContentLength)
            return WorkResult<DocDetailDto>.Fail(400, "This page is too long to save. Split it into smaller pages.");

        var now = DateTime.UtcNow;
        if ((doc.Content ?? string.Empty) != (request.Content ?? string.Empty))
        {
            // The editor saves every few seconds. Keep the earlier text as a version only when the last
            // kept one is older than PageVersionGap, or someone else is now writing.
            var last = await _db.DocVersions.AsNoTracking()
                .Where(v => v.DocumentId == documentId)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync();
            var keep = !string.IsNullOrEmpty(doc.Content)
                && (last == null || now - last.CreatedAt > PageVersionGap || doc.UpdatedByUserId != actor.UserId);
            if (keep)
            {
                _db.DocVersions.Add(new DocVersion
                {
                    VersionId = Guid.NewGuid(),
                    DocumentId = documentId,
                    VersionNumber = doc.VersionNumber,
                    Name = doc.Name,
                    Content = doc.Content,
                    SizeBytes = doc.SizeBytes,
                    CreatedByUserId = doc.UpdatedByUserId,
                    CreatedAt = doc.UpdatedAt,
                });
                doc.VersionNumber = doc.VersionNumber + 1;
                Log(documentId, doc.FolderId, actor, "edit", null);
            }
            doc.Content = string.IsNullOrEmpty(request.Content) ? null : request.Content;
            doc.ContentText = string.IsNullOrWhiteSpace(request.ContentText) ? null : request.ContentText;
            doc.SizeBytes = (request.Content ?? string.Empty).Length;
            doc.UpdatedByUserId = actor.UserId;
            doc.UpdatedAt = now;
            await _db.SaveChangesAsync();
        }
        return WorkResult<DocDetailDto>.Success(await BuildDetailAsync(doc, map), "Saved.");
    }

    public async Task<WorkResult<DocDetailDto>> UpdateAsync(DocsActor actor, Guid documentId, UpdateDocRequest request)
    {
        if (!actor.CanEdit) return WorkResult<DocDetailDto>.Fail(403, "You cannot change documents.");
        var map = await LoadMapAsync(actor);
        var doc = await _db.WorkDocuments.FirstOrDefaultAsync(d => d.DocumentId == documentId && !d.IsDeleted);
        if (doc == null || !map.CanSeeDocument(doc)) return WorkResult<DocDetailDto>.Fail(404, "Document not found.");

        var name = Clean(request.Name);
        if (name.Length == 0) return WorkResult<DocDetailDto>.Fail(400, "Give the document a name.");
        if (name.Length > DocsRules.MaxNameLength) return WorkResult<DocDetailDto>.Fail(400, "The name is too long.");
        if (doc.Kind == "File" && !string.Equals(Path.GetExtension(name), Path.GetExtension(doc.Name), StringComparison.OrdinalIgnoreCase))
            return WorkResult<DocDetailDto>.Fail(400, $"Keep the file's ending ({Path.GetExtension(doc.Name)}) when you rename it.");
        if (request.FolderId.HasValue && !map.CanSee(request.FolderId.Value)) return WorkResult<DocDetailDto>.Fail(404, "The folder to move it into was not found.");

        if (doc.Name != name)
        {
            Log(documentId, doc.FolderId, actor, "rename", $"{doc.Name} -> {name}");
            doc.Name = name;
        }
        if (doc.FolderId != request.FolderId)
        {
            Log(documentId, request.FolderId, actor, "move", doc.Name);
            doc.FolderId = request.FolderId;
        }
        var tags = WorkRules.CleanLabels(request.Tags);
        doc.Tags = tags.Count == 0 ? null : string.Join(",", tags);
        doc.UpdatedByUserId = actor.UserId;
        doc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return WorkResult<DocDetailDto>.Success(await BuildDetailAsync(doc, map), "Saved.");
    }

    public async Task<WorkResult<DocDetailDto>> UploadAsync(DocsActor actor, Guid? folderId, string fileName, string? contentType, long sizeBytes, Stream content)
    {
        if (!actor.CanEdit) return WorkResult<DocDetailDto>.Fail(403, "You cannot change documents.");
        if (!_files.IsConfigured)
            return WorkResult<DocDetailDto>.Fail(503, "File storage is not set up yet, so files cannot be uploaded. Pages still work.");

        var name = Clean(Path.GetFileName(fileName ?? string.Empty));
        if (name.Length == 0) return WorkResult<DocDetailDto>.Fail(400, "The file has no name.");
        if (name.Length > DocsRules.MaxNameLength) return WorkResult<DocDetailDto>.Fail(400, "The file name is too long.");
        if (!DocsRules.IsAllowedFile(name))
            return WorkResult<DocDetailDto>.Fail(400, "This type of file cannot be uploaded. Use PDF, Word, Excel, PowerPoint, text, CSV, an image or a zip.");
        if (sizeBytes <= 0) return WorkResult<DocDetailDto>.Fail(400, "The file is empty.");
        if (sizeBytes > DocsRules.MaxFileBytes) return WorkResult<DocDetailDto>.Fail(413, "The file is larger than 25 MB.");

        var map = await LoadMapAsync(actor);
        if (folderId.HasValue && !map.CanSee(folderId.Value)) return WorkResult<DocDetailDto>.Fail(404, "Folder not found.");

        var type = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType!;
        var now = DateTime.UtcNow;
        var existing = await _db.WorkDocuments.FirstOrDefaultAsync(d => !d.IsDeleted && d.Kind == "File" && d.FolderId == folderId && d.Name == name);
        var documentId = existing?.DocumentId ?? Guid.NewGuid();
        var version = existing == null ? 1 : existing.VersionNumber + 1;
        var key = $"docs/{documentId:N}/v{version}-{Guid.NewGuid():N}{Path.GetExtension(name).ToLowerInvariant()}";

        // Store the bytes first: a database row must never point at a file that is not there.
        await _files.SaveAsync(key, content, type);

        WorkDocument doc;
        if (existing == null)
        {
            doc = new WorkDocument
            {
                DocumentId = documentId,
                FolderId = folderId,
                Kind = "File",
                Name = name,
                ContentType = type,
                SizeBytes = sizeBytes,
                StorageKey = key,
                VersionNumber = 1,
                CreatedByUserId = actor.UserId,
                CreatedAt = now,
                UpdatedByUserId = actor.UserId,
                UpdatedAt = now,
            };
            _db.WorkDocuments.Add(doc);
            Log(documentId, folderId, actor, "upload", name);
        }
        else
        {
            doc = existing;
            _db.DocVersions.Add(new DocVersion
            {
                VersionId = Guid.NewGuid(),
                DocumentId = documentId,
                VersionNumber = doc.VersionNumber,
                Name = doc.Name,
                ContentType = doc.ContentType,
                SizeBytes = doc.SizeBytes,
                StorageKey = doc.StorageKey,
                CreatedByUserId = doc.UpdatedByUserId,
                CreatedAt = doc.UpdatedAt,
            });
            doc.ContentType = type;
            doc.SizeBytes = sizeBytes;
            doc.StorageKey = key;
            doc.VersionNumber = version;
            doc.UpdatedByUserId = actor.UserId;
            doc.UpdatedAt = now;
            Log(documentId, folderId, actor, "upload", $"{name} (version {version})");
        }
        await _db.SaveChangesAsync();
        return WorkResult<DocDetailDto>.Success(await BuildDetailAsync(doc, map), existing == null ? "Uploaded." : $"Uploaded as version {version}.");
    }

    public async Task<WorkResult<DocFileDto>> OpenFileAsync(DocsActor actor, Guid documentId, Guid? versionId)
    {
        var map = await LoadMapAsync(actor);
        var doc = await _db.WorkDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.DocumentId == documentId && !d.IsDeleted);
        if (doc == null || !map.CanSeeDocument(doc)) return WorkResult<DocFileDto>.Fail(404, "Document not found.");
        if (doc.Kind != "File") return WorkResult<DocFileDto>.Fail(400, "This is a page, not a file.");
        if (!_files.IsConfigured) return WorkResult<DocFileDto>.Fail(503, "File storage is not set up yet, so files cannot be opened.");

        var key = doc.StorageKey;
        var name = doc.Name;
        var type = doc.ContentType;
        if (versionId.HasValue)
        {
            var version = await _db.DocVersions.AsNoTracking().FirstOrDefaultAsync(v => v.VersionId == versionId.Value && v.DocumentId == documentId);
            if (version == null) return WorkResult<DocFileDto>.Fail(404, "That version was not found.");
            key = version.StorageKey;
            name = version.Name;
            type = version.ContentType;
        }
        if (string.IsNullOrEmpty(key)) return WorkResult<DocFileDto>.Fail(404, "The file is missing from storage.");

        Stream stream;
        try
        {
            stream = await _files.OpenAsync(key);
        }
        catch (FileNotFoundException)
        {
            return WorkResult<DocFileDto>.Fail(404, "The file is missing from storage.");
        }

        Log(documentId, doc.FolderId, actor, "download", versionId.HasValue ? "An earlier version" : null);
        await _db.SaveChangesAsync();
        return WorkResult<DocFileDto>.Success(new DocFileDto
        {
            Content = stream,
            FileName = name,
            ContentType = string.IsNullOrWhiteSpace(type) ? "application/octet-stream" : type!,
        });
    }

    // ── The bin ─────────────────────────────────────────────────────────────────────────────────

    public async Task<WorkResult<bool>> DeleteAsync(DocsActor actor, Guid documentId)
    {
        if (!actor.CanDelete) return WorkResult<bool>.Fail(403, "You cannot delete documents.");
        var map = await LoadMapAsync(actor);
        var doc = await _db.WorkDocuments.FirstOrDefaultAsync(d => d.DocumentId == documentId && !d.IsDeleted);
        if (doc == null || !map.CanSeeDocument(doc)) return WorkResult<bool>.Fail(404, "Document not found.");

        doc.IsDeleted = true;
        doc.DeletedAt = DateTime.UtcNow;
        doc.DeletedByUserId = actor.UserId;
        Log(documentId, doc.FolderId, actor, "bin", doc.Name);
        await _db.SaveChangesAsync();
        return WorkResult<bool>.Success(true, "Moved to the bin.");
    }

    public async Task<WorkResult<DocDetailDto>> RestoreAsync(DocsActor actor, Guid documentId)
    {
        if (!actor.CanDelete) return WorkResult<DocDetailDto>.Fail(403, "You cannot restore documents.");
        var doc = await _db.WorkDocuments.FirstOrDefaultAsync(d => d.DocumentId == documentId && d.IsDeleted);
        if (doc == null) return WorkResult<DocDetailDto>.Fail(404, "Document not found in the bin.");

        var map = await LoadMapAsync(actor);
        // If its folder has gone, or is locked to the caller, it comes back at the top level.
        if (doc.FolderId.HasValue && !map.CanSee(doc.FolderId.Value))
        {
            if (map.Folders.ContainsKey(doc.FolderId.Value) && !map.Folders[doc.FolderId.Value].IsDeleted && !map.IsBinned(doc.FolderId.Value))
                return WorkResult<DocDetailDto>.Fail(404, "Document not found in the bin.");
            doc.FolderId = null;
        }
        doc.IsDeleted = false;
        doc.DeletedAt = null;
        doc.DeletedByUserId = null;
        Log(documentId, doc.FolderId, actor, "restore", doc.Name);
        await _db.SaveChangesAsync();
        return WorkResult<DocDetailDto>.Success(await BuildDetailAsync(doc, map), "Restored.");
    }

    public async Task<WorkResult<DocBinDto>> GetBinAsync(DocsActor actor)
    {
        if (!actor.CanDelete) return WorkResult<DocBinDto>.Fail(403, "You cannot open the bin.");
        var map = await LoadMapAsync(actor);

        // A binned folder is shown when the caller could see it before it was binned.
        var folders = map.Folders.Values
            .Where(f => f.IsDeleted && map.AllowedIgnoringBin(f.FolderId))
            .OrderByDescending(f => f.DeletedAt)
            .Select(f => MapFolder(f, actor, 0))
            .ToList();

        var deleted = await _db.WorkDocuments.AsNoTracking().Where(d => d.IsDeleted).OrderByDescending(d => d.DeletedAt).Take(500).ToListAsync();
        var docs = deleted.Where(d => !d.FolderId.HasValue || map.AllowedIgnoringBin(d.FolderId.Value)).ToList();

        return WorkResult<DocBinDto>.Success(new DocBinDto { Folders = folders, Documents = await MapDocsAsync(docs, map) });
    }

    public async Task<WorkResult<List<DocLogDto>>> GetLogAsync(DocsActor actor, Guid documentId)
    {
        if (!actor.CanSensitive) return WorkResult<List<DocLogDto>>.Fail(403, "You cannot read the access log.");
        var rows = await _db.DocAccessLogs.AsNoTracking()
            .Where(l => l.DocumentId == documentId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(200)
            .ToListAsync();
        var names = await NamesAsync(rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value));
        return WorkResult<List<DocLogDto>>.Success(rows.Select(r => new DocLogDto
        {
            Action = r.Action,
            Detail = r.Detail,
            UserName = NameOf(names, r.UserId) ?? "Someone",
            CreatedAt = Utc(r.CreatedAt),
        }).ToList());
    }

    // ── Documents tagged on work items ──────────────────────────────────────────────────────────

    public async Task<WorkResult<List<DocDto>>> GetItemDocumentsAsync(DocsActor actor, Guid workItemId)
    {
        var map = await LoadMapAsync(actor);
        var ids = await _db.WorkItemDocuments.AsNoTracking().Where(l => l.WorkItemId == workItemId).Select(l => l.DocumentId).ToListAsync();
        var docs = await _db.WorkDocuments.AsNoTracking().Where(d => ids.Contains(d.DocumentId) && !d.IsDeleted).OrderBy(d => d.Name).ToListAsync();
        // Documents locked to the caller are simply not listed.
        return WorkResult<List<DocDto>>.Success(await MapDocsAsync(docs.Where(d => map.CanSeeDocument(d)).ToList(), map));
    }

    public async Task<WorkResult<bool>> LinkAsync(DocsActor actor, Guid workItemId, Guid documentId)
    {
        if (!actor.CanEdit) return WorkResult<bool>.Fail(403, "You cannot change documents.");
        var map = await LoadMapAsync(actor);
        var doc = await _db.WorkDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.DocumentId == documentId && !d.IsDeleted);
        if (doc == null || !map.CanSeeDocument(doc)) return WorkResult<bool>.Fail(404, "Document not found.");
        if (!await _db.WorkItems.AnyAsync(i => i.WorkItemId == workItemId && !i.IsDeleted)) return WorkResult<bool>.Fail(404, "Work item not found.");

        if (!await _db.WorkItemDocuments.AnyAsync(l => l.WorkItemId == workItemId && l.DocumentId == documentId))
        {
            _db.WorkItemDocuments.Add(new WorkItemDocument { WorkItemId = workItemId, DocumentId = documentId, AddedByUserId = actor.UserId, AddedAt = DateTime.UtcNow });
            await _db.SaveChangesAsync();
        }
        return WorkResult<bool>.Success(true, "Document linked.");
    }

    public async Task<WorkResult<bool>> UnlinkAsync(DocsActor actor, Guid workItemId, Guid documentId)
    {
        if (!actor.CanEdit) return WorkResult<bool>.Fail(403, "You cannot change documents.");
        var link = await _db.WorkItemDocuments.FirstOrDefaultAsync(l => l.WorkItemId == workItemId && l.DocumentId == documentId);
        if (link != null)
        {
            _db.WorkItemDocuments.Remove(link);
            await _db.SaveChangesAsync();
        }
        return WorkResult<bool>.Success(true, "Link removed.");
    }

    // ── Who sees what ───────────────────────────────────────────────────────────────────────────

    /// <summary>Every folder (binned ones too) and the folders this person was let into.</summary>
    private sealed class FolderMap
    {
        public Dictionary<Guid, DocFolder> Folders = new Dictionary<Guid, DocFolder>();
        public HashSet<Guid> Granted = new HashSet<Guid>();
        public DocsActor Actor = new DocsActor();

        /// <summary>The folder exists, nothing above it is in the bin, and no lock on the way keeps the person out.</summary>
        public bool CanSee(Guid folderId) => Walk(folderId, true);

        /// <summary>As CanSee, but a folder in the bin does not hide it. Used to list the bin.</summary>
        public bool AllowedIgnoringBin(Guid folderId) => Walk(folderId, false);

        public bool CanSeeDocument(WorkDocument doc) => !doc.FolderId.HasValue || CanSee(doc.FolderId.Value);

        /// <summary>The folder, or one above it, is in the bin.</summary>
        public bool IsBinned(Guid folderId)
        {
            Guid? at = folderId;
            for (var depth = 0; at.HasValue && depth < 50; depth++)
            {
                if (!Folders.TryGetValue(at.Value, out var folder)) return false;
                if (folder.IsDeleted) return true;
                at = folder.ParentId;
            }
            return false;
        }

        /// <summary>Is `folderId` somewhere under `ancestorId`?</summary>
        public bool IsUnder(Guid folderId, Guid ancestorId)
        {
            Guid? at = Folders.TryGetValue(folderId, out var start) ? start.ParentId : null;
            for (var depth = 0; at.HasValue && depth < 50; depth++)
            {
                if (at.Value == ancestorId) return true;
                at = Folders.TryGetValue(at.Value, out var folder) ? folder.ParentId : null;
            }
            return false;
        }

        public List<string> PathOf(Guid? folderId)
        {
            var names = new List<string>();
            var at = folderId;
            for (var depth = 0; at.HasValue && depth < 50; depth++)
            {
                if (!Folders.TryGetValue(at.Value, out var folder)) break;
                names.Insert(0, folder.Name);
                at = folder.ParentId;
            }
            return names;
        }

        private bool Walk(Guid folderId, bool binHides)
        {
            Guid? at = folderId;
            // The depth limit only guards against a loop in bad data; real trees are a few levels deep.
            for (var depth = 0; at.HasValue && depth < 50; depth++)
            {
                if (!Folders.TryGetValue(at.Value, out var folder)) return false;
                if (binHides && folder.IsDeleted) return false;
                if (folder.IsRestricted && !Actor.CanSensitive && !Granted.Contains(folder.FolderId)
                    && !(Actor.UserId.HasValue && folder.CreatedByUserId == Actor.UserId))
                    return false;
                at = folder.ParentId;
            }
            return true;
        }
    }

    private async Task<FolderMap> LoadMapAsync(DocsActor actor)
    {
        var folders = await _db.DocFolders.AsNoTracking().ToListAsync();
        var map = new FolderMap { Actor = actor };
        foreach (var folder in folders) map.Folders[folder.FolderId] = folder;
        if (actor.UserId.HasValue)
        {
            var granted = await _db.DocFolderAccesses.AsNoTracking().Where(a => a.UserId == actor.UserId.Value).Select(a => a.FolderId).ToListAsync();
            foreach (var id in granted) map.Granted.Add(id);
        }
        return map;
    }

    private static bool CanManageAccess(DocFolder folder, DocsActor actor) =>
        actor.CanSensitive || (actor.CanEdit && actor.UserId.HasValue && folder.CreatedByUserId == actor.UserId);

    // ── Mapping and small helpers ───────────────────────────────────────────────────────────────

    private static DocFolderDto MapFolder(DocFolder f, DocsActor actor, int documentCount) => new DocFolderDto
    {
        FolderId = f.FolderId,
        ParentId = f.ParentId,
        Name = f.Name,
        Color = f.Color,
        IsRestricted = f.IsRestricted,
        CanManageAccess = CanManageAccess(f, actor),
        DocumentCount = documentCount,
        UpdatedAt = Utc(f.UpdatedAt),
    };

    private async Task<List<DocDto>> MapDocsAsync(List<WorkDocument> docs, FolderMap map)
    {
        var people = new List<Guid>();
        foreach (var d in docs)
        {
            if (d.CreatedByUserId.HasValue) people.Add(d.CreatedByUserId.Value);
            if (d.UpdatedByUserId.HasValue) people.Add(d.UpdatedByUserId.Value);
            if (d.DeletedByUserId.HasValue) people.Add(d.DeletedByUserId.Value);
        }
        var names = await NamesAsync(people);

        var ids = docs.Select(d => d.DocumentId).ToList();
        var linkRows = await _db.WorkItemDocuments.AsNoTracking().Where(l => ids.Contains(l.DocumentId)).Select(l => l.DocumentId).ToListAsync();
        var links = linkRows.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

        var list = new List<DocDto>();
        foreach (var d in docs)
        {
            var dto = new DocDto();
            Fill(dto, d, names, map, links.TryGetValue(d.DocumentId, out var n) ? n : 0);
            list.Add(dto);
        }
        return list;
    }

    private async Task<DocDetailDto> BuildDetailAsync(WorkDocument doc, FolderMap map)
    {
        var versions = await _db.DocVersions.AsNoTracking().Where(v => v.DocumentId == doc.DocumentId).OrderByDescending(v => v.VersionNumber).ToListAsync();
        var itemIds = await _db.WorkItemDocuments.AsNoTracking().Where(l => l.DocumentId == doc.DocumentId).Select(l => l.WorkItemId).ToListAsync();
        var items = await _db.WorkItems.AsNoTracking().Where(i => itemIds.Contains(i.WorkItemId) && !i.IsDeleted).ToListAsync();
        var projectIds = items.Select(i => i.ProjectId).Distinct().ToList();
        var projects = await _db.WorkProjects.AsNoTracking().Where(p => projectIds.Contains(p.ProjectId)).ToDictionaryAsync(p => p.ProjectId, p => p.Key);
        // A members-only board's item shows its key here, never its title: opening it is checked by the board.
        var closedBoards = (await _db.WorkProjects.AsNoTracking().Where(p => projectIds.Contains(p.ProjectId) && p.IsRestricted).Select(p => p.ProjectId).ToListAsync()).ToHashSet();

        var people = new List<Guid>();
        if (doc.CreatedByUserId.HasValue) people.Add(doc.CreatedByUserId.Value);
        if (doc.UpdatedByUserId.HasValue) people.Add(doc.UpdatedByUserId.Value);
        people.AddRange(versions.Where(v => v.CreatedByUserId.HasValue).Select(v => v.CreatedByUserId!.Value));
        var names = await NamesAsync(people);

        var detail = new DocDetailDto();
        Fill(detail, doc, names, map, items.Count);
        detail.Content = doc.Kind == "Page" ? doc.Content : null;
        detail.Versions = versions.Select(v => new DocVersionDto
        {
            VersionId = v.VersionId,
            VersionNumber = v.VersionNumber,
            Name = v.Name,
            SizeBytes = v.SizeBytes,
            CreatedByName = NameOf(names, v.CreatedByUserId),
            CreatedAt = Utc(v.CreatedAt),
        }).ToList();
        detail.LinkedItems = items
            .OrderBy(i => i.Number)
            .Select(i => new DocLinkedItemDto
            {
                WorkItemId = i.WorkItemId,
                ProjectId = i.ProjectId,
                Key = (projects.TryGetValue(i.ProjectId, out var key) ? key : "?") + "-" + i.Number,
                Title = closedBoards.Contains(i.ProjectId) ? "Item on a members-only board" : i.Title,
                ItemType = i.ItemType,
            })
            .ToList();
        return detail;
    }

    private static void Fill(DocDto dto, WorkDocument d, Dictionary<Guid, string> names, FolderMap map, int linkedItems)
    {
        dto.DocumentId = d.DocumentId;
        dto.FolderId = d.FolderId;
        dto.Kind = d.Kind;
        dto.Name = d.Name;
        dto.ContentType = d.ContentType;
        dto.SizeBytes = d.SizeBytes;
        dto.VersionNumber = d.VersionNumber;
        dto.Tags = WorkRules.SplitLabels(d.Tags);
        dto.CreatedByName = NameOf(names, d.CreatedByUserId);
        dto.UpdatedByName = NameOf(names, d.UpdatedByUserId);
        dto.CreatedAt = Utc(d.CreatedAt);
        dto.UpdatedAt = Utc(d.UpdatedAt);
        dto.LinkedItemCount = linkedItems;
        dto.Path = map.PathOf(d.FolderId);
        dto.IsDeleted = d.IsDeleted;
        dto.DeletedAt = d.DeletedAt.HasValue ? Utc(d.DeletedAt.Value) : (DateTime?)null;
        dto.DeletedByName = NameOf(names, d.DeletedByUserId);
    }

    private void Log(Guid? documentId, Guid? folderId, DocsActor actor, string action, string? detail)
    {
        _db.DocAccessLogs.Add(new DocAccessLog
        {
            LogId = Guid.NewGuid(),
            DocumentId = documentId,
            FolderId = folderId,
            UserId = actor.UserId,
            Action = action,
            Detail = detail != null && detail.Length > 300 ? detail.Substring(0, 300) : detail,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> userIds)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        return await _db.CmsUsers.AsNoTracking().Where(u => ids.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName);
    }

    private static string? NameOf(Dictionary<Guid, string> names, Guid? userId)
    {
        if (!userId.HasValue) return null;
        return names.TryGetValue(userId.Value, out var name) ? name : null;
    }

    private static string Clean(string? text) => (text ?? string.Empty).Trim();

    private static bool ColorOk(string? color) =>
        string.IsNullOrWhiteSpace(color) || Array.IndexOf(DocsRules.FolderColors, color) >= 0;

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
