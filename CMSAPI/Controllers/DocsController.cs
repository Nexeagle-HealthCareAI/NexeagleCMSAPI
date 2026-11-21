using System.Security.Claims;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// Daily Work Board, tab "Docs": folders, pages, uploaded files, versions, the bin, restricted folders.
// Permissions:
//   docs.view        every route here needs it
//   docs.edit        create, upload, rename, move, tag, link to a work item
//   docs.delete      move to the bin, open the bin, restore
//   docs.sensitive   open every restricted folder, choose who may open one, read the access log
// The finer checks (who may open which restricted folder) are in DocsService, because they depend
// on the folder, not only on the permission. Tables: 27_CMSDatabase_WorkDocs.sql.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/docs")]
[HasPermission("docs.view")]
public class DocsController : ControllerBase
{
    private readonly IDocsService _service;

    public DocsController(IDocsService service)
    {
        _service = service;
    }

    private DocsActor Actor
    {
        get
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return new DocsActor
            {
                UserId = Guid.TryParse(claim, out var id) ? id : (Guid?)null,
                CanEdit = Has("docs.edit"),
                CanDelete = Has("docs.delete"),
                CanSensitive = Has("docs.sensitive"),
            };
        }
    }

    private bool Has(string permission) =>
        User.Claims.Any(c => c.Type == PermissionConstants.ClaimType && c.Value == permission);

    private IActionResult Reply<T>(WorkResult<T> result)
    {
        if (result.Ok) return Ok(new { success = true, data = result.Data, message = result.Message });
        return StatusCode(result.Status, new { success = false, message = result.Message });
    }

    // ── Folders ─────────────────────────────────────────────────────────────────────────────────

    // Every folder the caller may see, flat; the web app builds the tree from parentId.
    [HttpGet("folders")]
    public async Task<IActionResult> GetFolders() =>
        Ok(new { success = true, data = await _service.GetFoldersAsync(Actor) });

    [HttpPost("folders")]
    public async Task<IActionResult> CreateFolder([FromBody] SaveDocFolderRequest request) =>
        Reply(await _service.CreateFolderAsync(Actor, request));

    // Rename, move (parentId), colour, lock or unlock. Send every field.
    [HttpPut("folders/{id:guid}")]
    public async Task<IActionResult> UpdateFolder(Guid id, [FromBody] SaveDocFolderRequest request) =>
        Reply(await _service.UpdateFolderAsync(Actor, id, request));

    // The people let into a restricted folder: ids of CMS users.
    [HttpGet("folders/{id:guid}/access")]
    public async Task<IActionResult> GetFolderAccess(Guid id) =>
        Reply(await _service.GetFolderAccessAsync(Actor, id));

    [HttpPut("folders/{id:guid}/access")]
    public async Task<IActionResult> SetFolderAccess(Guid id, [FromBody] SetDocFolderAccessRequest request) =>
        Reply(await _service.SetFolderAccessAsync(Actor, id, request));

    [HttpDelete("folders/{id:guid}")]
    public async Task<IActionResult> DeleteFolder(Guid id) =>
        Reply(await _service.DeleteFolderAsync(Actor, id));

    [HttpPost("folders/{id:guid}/restore")]
    public async Task<IActionResult> RestoreFolder(Guid id) =>
        Reply(await _service.RestoreFolderAsync(Actor, id));

    // ── Documents ───────────────────────────────────────────────────────────────────────────────

    // ?folderId= (leave out for the top level) lists one folder; ?search= looks everywhere the caller can see.
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? folderId, [FromQuery] string? search) =>
        Reply(await _service.ListAsync(Actor, folderId, search));

    [HttpGet("bin")]
    public async Task<IActionResult> Bin() =>
        Reply(await _service.GetBinAsync(Actor));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id) =>
        Reply(await _service.GetAsync(Actor, id));

    [HttpPost("pages")]
    public async Task<IActionResult> CreatePage([FromBody] CreateDocPageRequest request) =>
        Reply(await _service.CreatePageAsync(Actor, request));

    // The page's text: { content: editor JSON, contentText: plain text }.
    [HttpPut("{id:guid}/content")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> SavePage(Guid id, [FromBody] SaveDocPageRequest request) =>
        Reply(await _service.SavePageAsync(Actor, id, request));

    // Name, folder and tags of a page or file.
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDocRequest request) =>
        Reply(await _service.UpdateAsync(Actor, id, request));

    // multipart/form-data: file, and folderId (leave out for the top level). A file with the same
    // name already in that folder becomes its next version.
    [HttpPost("upload")]
    [RequestSizeLimit(27_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 27_000_000)]
    public async Task<IActionResult> Upload([FromForm] Guid? folderId, IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "Choose a file to upload." });

        await using var stream = file.OpenReadStream();
        return Reply(await _service.UploadAsync(Actor, folderId, file.FileName, file.ContentType, file.Length, stream));
    }

    // The file itself. ?versionId= gives an earlier version. Sent as a download so a browser never
    // runs it; the web app reads it and shows PDFs and images in its own preview.
    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> GetFile(Guid id, [FromQuery] Guid? versionId)
    {
        var result = await _service.OpenFileAsync(Actor, id, versionId);
        if (!result.Ok || result.Data == null)
            return StatusCode(result.Status, new { success = false, message = result.Message });

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        Reply(await _service.DeleteAsync(Actor, id));

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id) =>
        Reply(await _service.RestoreAsync(Actor, id));

    [HttpGet("{id:guid}/log")]
    public async Task<IActionResult> GetLog(Guid id) =>
        Reply(await _service.GetLogAsync(Actor, id));

    // ── Documents tagged on a work item ─────────────────────────────────────────────────────────

    [HttpGet("items/{workItemId:guid}")]
    public async Task<IActionResult> GetItemDocuments(Guid workItemId) =>
        Reply(await _service.GetItemDocumentsAsync(Actor, workItemId));

    [HttpPost("items/{workItemId:guid}")]
    public async Task<IActionResult> Link(Guid workItemId, [FromBody] LinkDocRequest request) =>
        Reply(await _service.LinkAsync(Actor, workItemId, request.DocumentId));

    [HttpDelete("items/{workItemId:guid}/{documentId:guid}")]
    public async Task<IActionResult> Unlink(Guid workItemId, Guid documentId) =>
        Reply(await _service.UnlinkAsync(Actor, workItemId, documentId));
}
