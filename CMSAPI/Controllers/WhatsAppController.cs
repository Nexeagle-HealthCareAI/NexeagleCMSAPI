using System.Security.Claims;
using System.Text.Json;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using CMSAPI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Controllers;

// WhatsApp marketing on the CMS's own number: inbox, contacts, templates, broadcasts, profile, money.
// Every route needs whatsapp.view; each change checks its own permission again in the service.
// Design: claude/whatsapp-marketing-design.md. Tables: 35_CMSDatabase_WhatsApp_Marketing.sql.
// Meta's webhook is WhatsAppWebhookController (no sign-in, signed by Meta instead).
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/whatsapp")]
[HasPermission("whatsapp.view")]
public class WhatsAppController : ControllerBase
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly IWaInboxService _inbox;
    private readonly IWaContactService _contacts;
    private readonly IWaTemplateService _templates;
    private readonly IWaBroadcastService _broadcasts;
    private readonly IWaAccountService _account;
    private readonly CmsDbContext _db;

    public WhatsAppController(IWaInboxService inbox, IWaContactService contacts, IWaTemplateService templates,
        IWaBroadcastService broadcasts, IWaAccountService account, CmsDbContext db)
    {
        _inbox = inbox;
        _contacts = contacts;
        _templates = templates;
        _broadcasts = broadcasts;
        _account = account;
        _db = db;
    }

    private WaActor Actor => new()
    {
        UserId = Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null,
        Name = User.FindFirst("name")?.Value ?? "Someone",
        CanReply = Has("whatsapp.reply"),
        CanContacts = Has("whatsapp.contacts"),
        CanBroadcast = Has("whatsapp.broadcast"),
        CanTemplates = Has("whatsapp.templates"),
        CanApprove = Has("whatsapp.approve"),
        CanSettings = Has("whatsapp.settings"),
        CanPatientContacts = Has("insights.contacts"),
    };

    private bool Has(string permission) =>
        User.Claims.Any(c => c.Type == PermissionConstants.ClaimType && c.Value == permission);

    private IActionResult Reply<T>(WorkResult<T> result) => result.Ok
        ? Ok(new { success = true, data = result.Data, message = result.Message })
        : StatusCode(result.Status, new { success = false, message = result.Message });

    private static async Task<byte[]> ReadAsync(IFormFile file)
    {
        using var memory = new MemoryStream();
        await file.CopyToAsync(memory);
        return memory.ToArray();
    }

    // ── Inbox ───────────────────────────────────────────────────────────────

    [HttpGet("chats")]
    public async Task<IActionResult> Chats([FromQuery] WaChatQuery query) => Reply(await _inbox.ChatsAsync(Actor, query));

    [HttpGet("chats/{contactId:guid}")]
    public async Task<IActionResult> Thread(Guid contactId) => Reply(await _inbox.ThreadAsync(Actor, contactId));

    // Messages, statuses and notes changed after `after` (UTC), for the open chat to refresh.
    [HttpGet("chats/{contactId:guid}/since")]
    public async Task<IActionResult> ThreadSince(Guid contactId, [FromQuery] DateTime after) =>
        Reply(await _inbox.ThreadSinceAsync(Actor, contactId, DateTime.SpecifyKind(after, DateTimeKind.Utc)));

    [HttpPost("chats/{contactId:guid}/text")]
    public async Task<IActionResult> SendText(Guid contactId, [FromBody] WaSendTextRequest request) => Reply(await _inbox.SendTextAsync(Actor, contactId, request));

    [HttpPost("chats/{contactId:guid}/media")]
    [RequestSizeLimit(17 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SendMedia(Guid contactId, IFormFile? file, [FromForm] string? caption)
    {
        if (file == null || file.Length == 0) return BadRequest(new { success = false, message = "Choose a file to send." });
        return Reply(await _inbox.SendMediaAsync(Actor, contactId, await ReadAsync(file), file.ContentType ?? "application/octet-stream", Path.GetFileName(file.FileName), caption));
    }

    [HttpPost("chats/{contactId:guid}/template")]
    public async Task<IActionResult> SendTemplate(Guid contactId, [FromBody] WaSendTemplateRequest request) => Reply(await _inbox.SendTemplateAsync(Actor, contactId, request));

    [HttpPost("chats/{contactId:guid}/notes")]
    public async Task<IActionResult> AddNote(Guid contactId, [FromBody] WaNoteRequest request) => Reply(await _inbox.AddNoteAsync(Actor, contactId, request));

    [HttpPut("chats/{contactId:guid}/assign")]
    public async Task<IActionResult> Assign(Guid contactId, [FromBody] WaAssignRequest request) => Reply(await _inbox.AssignAsync(Actor, contactId, request));

    [HttpPut("chats/{contactId:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid contactId, [FromBody] WaChatStatusRequest request) => Reply(await _inbox.SetStatusAsync(Actor, contactId, request));

    // A picture, video, voice note or document from a message, through the CMS (WhatsApp's links need the token).
    [HttpGet("messages/{messageId:guid}/media")]
    public async Task<IActionResult> Media(Guid messageId)
    {
        var result = await _inbox.MediaAsync(Actor, messageId);
        if (!result.Ok) return StatusCode(result.Status, new { success = false, message = result.Message });
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(result.Data!.Data, result.Data.Mime);
    }

    [HttpGet("unread")]
    public async Task<IActionResult> Unread() => Ok(new { success = true, data = await _inbox.UnreadAsync() });

    // People who can be given chats: active people with whatsapp.reply (from a role or added just for them).
    [HttpGet("agents")]
    public async Task<IActionResult> Agents()
    {
        var permission = await _db.CmsPermissions.AsNoTracking().FirstOrDefaultAsync(p => p.Key == "whatsapp.reply");
        if (permission == null) return Ok(new { success = true, data = Array.Empty<object>() });
        var byRole = from ur in _db.CmsUserRoles
                     join rp in _db.CmsRolePermissions on ur.RoleId equals rp.RoleId
                     join r in _db.CmsRoles on ur.RoleId equals r.RoleId
                     where rp.PermissionId == permission.PermissionId && r.IsActive
                     select ur.UserId;
        var allowed = _db.CmsUserPermissions.Where(x => x.PermissionId == permission.PermissionId && x.Effect == "Allow").Select(x => x.UserId);
        var denied = _db.CmsUserPermissions.Where(x => x.PermissionId == permission.PermissionId && x.Effect == "Deny").Select(x => x.UserId);
        var people = await _db.CmsUsers.AsNoTracking()
            .Where(u => u.IsActive && (byRole.Contains(u.UserId) || allowed.Contains(u.UserId)) && !denied.Contains(u.UserId))
            .OrderBy(u => u.FullName)
            .Select(u => new { userId = u.UserId, fullName = u.FullName, photoVersion = u.PhotoUpdatedAt })
            .ToListAsync();
        return Ok(new { success = true, data = people.Select(p => new { p.userId, p.fullName, photoVersion = p.photoVersion.HasValue ? p.photoVersion.Value.Ticks.ToString() : null }) });
    }

    // ── Contacts ────────────────────────────────────────────────────────────

    [HttpGet("contacts")]
    public async Task<IActionResult> Contacts([FromQuery] WaContactQuery query) => Reply(await _contacts.ListAsync(Actor, query));

    [HttpGet("contacts/{contactId:guid}")]
    public async Task<IActionResult> Contact(Guid contactId) => Reply(await _contacts.GetAsync(Actor, contactId));

    [HttpPost("contacts")]
    public async Task<IActionResult> CreateContact([FromBody] WaSaveContactRequest request) => Reply(await _contacts.CreateAsync(Actor, request));

    [HttpPut("contacts/{contactId:guid}")]
    public async Task<IActionResult> UpdateContact(Guid contactId, [FromBody] WaSaveContactRequest request) => Reply(await _contacts.UpdateAsync(Actor, contactId, request));

    [HttpPost("contacts/import")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Import([FromBody] WaImportRequest request) => Reply(await _contacts.ImportAsync(Actor, request));

    [HttpPost("contacts/import-leads")]
    public async Task<IActionResult> ImportLeads([FromBody] WaImportLeadsRequest request) => Reply(await _contacts.ImportLeadsAsync(Actor, request));

    [HttpPost("contacts/import-doctor-dekho")]
    public async Task<IActionResult> ImportDoctorDekho([FromBody] WaImportDoctorDekhoRequest request) => Reply(await _contacts.ImportDoctorDekhoAsync(Actor, request));

    // ── Templates ───────────────────────────────────────────────────────────

    [HttpGet("templates")]
    public async Task<IActionResult> Templates([FromQuery] bool refresh = false) => Reply(await _templates.ListAsync(Actor, refresh));

    // Multipart: "template" (the WaSaveTemplateRequest as JSON) and, for a media header, "sample" (the file).
    [HttpPost("templates")]
    [RequestSizeLimit(17 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateTemplate([FromForm] string template, IFormFile? sample)
    {
        WaSaveTemplateRequest? request;
        try { request = JsonSerializer.Deserialize<WaSaveTemplateRequest>(template, Web); }
        catch (JsonException) { request = null; }
        if (request == null) return BadRequest(new { success = false, message = "The template could not be read." });
        var bytes = sample is { Length: > 0 } ? await ReadAsync(sample) : null;
        return Reply(await _templates.CreateAsync(Actor, request, bytes, sample?.ContentType, sample?.FileName));
    }

    [HttpDelete("templates/{templateId:guid}")]
    public async Task<IActionResult> DeleteTemplate(Guid templateId) => Reply(await _templates.DeleteAsync(Actor, templateId));

    // ── Broadcasts ──────────────────────────────────────────────────────────

    [HttpGet("broadcasts")]
    public async Task<IActionResult> Broadcasts() => Reply(await _broadcasts.ListAsync(Actor));

    [HttpGet("broadcasts/{broadcastId:guid}")]
    public async Task<IActionResult> Broadcast(Guid broadcastId) => Reply(await _broadcasts.GetAsync(Actor, broadcastId));

    [HttpGet("broadcasts/{broadcastId:guid}/recipients")]
    public async Task<IActionResult> Recipients(Guid broadcastId, [FromQuery] string? status) => Reply(await _broadcasts.RecipientsAsync(Actor, broadcastId, status));

    [HttpPost("broadcasts/preview")]
    public async Task<IActionResult> Preview([FromBody] WaBroadcastRequest request) => Reply(await _broadcasts.PreviewAsync(Actor, request));

    [HttpPost("broadcasts")]
    public async Task<IActionResult> CreateBroadcast([FromBody] WaBroadcastRequest request) => Reply(await _broadcasts.CreateAsync(Actor, request));

    [HttpPost("broadcasts/{broadcastId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid broadcastId) => Reply(await _broadcasts.ApproveAsync(Actor, broadcastId));

    [HttpPost("broadcasts/{broadcastId:guid}/pause")]
    public async Task<IActionResult> Pause(Guid broadcastId) => Reply(await _broadcasts.PauseAsync(Actor, broadcastId));

    [HttpPost("broadcasts/{broadcastId:guid}/resume")]
    public async Task<IActionResult> Resume(Guid broadcastId) => Reply(await _broadcasts.ResumeAsync(Actor, broadcastId));

    [HttpPost("broadcasts/{broadcastId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid broadcastId) => Reply(await _broadcasts.CancelAsync(Actor, broadcastId));

    // ── Number, profile, money ──────────────────────────────────────────────

    [HttpGet("number")]
    public async Task<IActionResult> Number() => Reply(await _account.NumberAsync(Actor));

    [HttpGet("profile")]
    public async Task<IActionResult> Profile() => Reply(await _account.ProfileAsync(Actor));

    // Multipart: "profile" (WaProfileDto as JSON) and optionally "picture" (JPG/PNG).
    [HttpPut("profile")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SaveProfile([FromForm] string profile, IFormFile? picture)
    {
        WaProfileDto? dto;
        try { dto = JsonSerializer.Deserialize<WaProfileDto>(profile, Web); }
        catch (JsonException) { dto = null; }
        if (dto == null) return BadRequest(new { success = false, message = "The profile could not be read." });
        var bytes = picture is { Length: > 0 } ? await ReadAsync(picture) : null;
        return Reply(await _account.SaveProfileAsync(Actor, dto, bytes, picture?.ContentType));
    }

    [HttpGet("settings")]
    public async Task<IActionResult> Settings() => Reply(await _account.SettingsAsync(Actor));

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] WaSaveSettingsRequest request) => Reply(await _account.SaveSettingsAsync(Actor, request));

    [HttpGet("spend")]
    public async Task<IActionResult> Spend([FromQuery] DateOnly from, [FromQuery] DateOnly to) => Reply(await _account.SpendAsync(Actor, from, to));

    [HttpGet("spend/meta")]
    public async Task<IActionResult> MetaSpend([FromQuery] DateOnly from, [FromQuery] DateOnly to) => Reply(await _account.MetaSpendAsync(Actor, from, to));
}
