using System.Security.Claims;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using CMSAPI.Data.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// CMS → Social: the Facebook Page, Instagram account and ad account of each brand. Every route needs
// social.view; each change checks its own permission again in the service.
// Design: claude/social-facebook-instagram-design.md. Tables: 36_CMSDatabase_Social.sql.
// Meta's webhook is MetaWebhookController (no sign-in, signed by Meta instead).
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/social")]
[HasPermission("social.view")]
public class SocialController : ControllerBase
{
    private readonly ISocialSetupService _setup;
    private readonly ISocialPostsService _posts;
    private readonly ISocialComposeService _compose;
    private readonly ISocialInboxService _inbox;
    private readonly ISocialLeadsService _leads;
    private readonly ISocialAdsService _ads;
    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopes;

    public SocialController(ISocialSetupService setup, ISocialPostsService posts, ISocialComposeService compose, ISocialInboxService inbox,
        ISocialLeadsService leads, ISocialAdsService ads, IConfiguration config, IServiceScopeFactory scopes)
    {
        _setup = setup;
        _posts = posts;
        _compose = compose;
        _inbox = inbox;
        _leads = leads;
        _ads = ads;
        _config = config;
        _scopes = scopes;
    }

    private bool Has(string permission) => User.HasClaim(PermissionConstants.ClaimType, permission);

    private SocialActor Actor => new()
    {
        UserId = Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty,
        Name = User.FindFirst("name")?.Value ?? "Someone",
        CanReply = Has("social.reply"),
        CanCompose = Has("social.compose"),
        CanPublish = Has("social.publish"),
        CanAds = Has("social.ads"),
        CanConnect = Has("social.connect"),
    };

    private IActionResult Reply<T>(WorkResult<T> result) => result.Ok
        ? Ok(new { success = true, data = result.Data, message = result.Message })
        : StatusCode(result.Status, new { success = false, message = result.Message });

    private IActionResult Data<T>(T data) => Ok(new { success = true, data });

    /// <summary>This API's Meta webhook address: Social__PublicApiUrl when set, else the address this request came to.</summary>
    private string WebhookUrl()
    {
        var configured = _config["Social:PublicApiUrl"] ?? _config["WhatsAppMarketing:PublicApiUrl"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim().TrimEnd('/') + "/api/v1/meta/webhook";
        var host = Request.Host.Host;
        var plain = host == "localhost" || System.Net.IPAddress.TryParse(host, out _);
        var port = Request.Host.Port is { } p && p is not (80 or 443) && plain ? $":{p}" : string.Empty;
        return $"{(plain ? Request.Scheme : "https")}://{host}{port}/api/v1/meta/webhook";
    }

    // ── accounts and setup ──────────────────────────────────────────────────

    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts() => Data(await _setup.AccountsAsync());

    [HttpPost("accounts")]
    [HasPermission("social.connect")]
    public async Task<IActionResult> CreateAccount([FromBody] SocialAccountCreate request) => Reply(await _setup.CreateAsync(Actor, request, WebhookUrl()));

    [HttpGet("accounts/{accountId:guid}/setup")]
    public async Task<IActionResult> Setup(Guid accountId) => Reply(await _setup.GetAsync(Actor, accountId, WebhookUrl()));

    [HttpPut("accounts/{accountId:guid}/keys")]
    [HasPermission("social.connect")]
    public async Task<IActionResult> SaveKeys(Guid accountId, [FromBody] SocialKeysSave request) => Reply(await _setup.SaveKeysAsync(Actor, accountId, request, WebhookUrl()));

    [HttpGet("accounts/{accountId:guid}/discover")]
    [HasPermission("social.connect")]
    public async Task<IActionResult> Discover(Guid accountId) => Reply(await _setup.DiscoverAsync(Actor, accountId));

    [HttpPut("accounts/{accountId:guid}/assets")]
    [HasPermission("social.connect")]
    public async Task<IActionResult> Choose(Guid accountId, [FromBody] SocialAssetsChoice choice)
    {
        var result = await _setup.ChooseAsync(Actor, accountId, choice, WebhookUrl());
        if (result.Ok && result.Data?.PageId != null) StartSync(accountId, full: true);
        return Reply(result);
    }

    [HttpGet("accounts/{accountId:guid}/checks")]
    public async Task<IActionResult> Checks(Guid accountId) => Reply(await _setup.CheckAsync(Actor, accountId, WebhookUrl()));

    [HttpPost("accounts/{accountId:guid}/webhooks")]
    [HasPermission("social.connect")]
    public async Task<IActionResult> ConnectWebhooks(Guid accountId, [FromBody] SocialWebhookRequest request) => Reply(await _setup.ConnectWebhooksAsync(Actor, accountId, request, WebhookUrl()));

    [HttpPost("accounts/{accountId:guid}/sync")]
    [HasPermission("social.connect")]
    public IActionResult Sync(Guid accountId, [FromQuery] bool full = false)
    {
        StartSync(accountId, full);
        return Ok(new { success = true, message = "Reading from Meta now; this can take a few minutes." });
    }

    /// <summary>Runs the sync in its own scope after the request ends (the first full read takes minutes).</summary>
    private void StartSync(Guid accountId, bool full)
    {
        var scopes = _scopes;
        _ = Task.Run(async () =>
        {
            using var scope = scopes.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<SocialController>>();
            try { await scope.ServiceProvider.GetRequiredService<ISocialSyncService>().SyncAccountAsync(accountId, full, CancellationToken.None); }
            catch (Exception ex) { logger.LogError(ex, "Social sync of {Account} failed.", accountId); }
        });
    }

    // ── posts ───────────────────────────────────────────────────────────────

    [HttpGet("posts")]
    public async Task<IActionResult> Posts([FromQuery] SocialPostQuery query) => Reply(await _posts.ListAsync(query));

    [HttpGet("posts/{postId:guid}")]
    public async Task<IActionResult> Post(Guid postId) => Reply(await _posts.DetailAsync(postId));

    // ── compose ─────────────────────────────────────────────────────────────

    [HttpPost("media")]
    [RequestSizeLimit(310L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 310L * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { success = false, message = "Choose a file." });
        await using var stream = file.OpenReadStream();
        return Reply(await _compose.UploadAsync(Actor, stream, file.FileName, file.ContentType, file.Length));
    }

    [HttpGet("media")]
    public async Task<IActionResult> Media([FromQuery] string key)
    {
        var file = await _compose.MediaAsync(key ?? string.Empty);
        return file is { } f ? File(f.Content, f.Mime) : NotFound();
    }

    [HttpGet("drafts")]
    public async Task<IActionResult> Drafts([FromQuery] Guid accountId, [FromQuery] string? status) => Reply(await _compose.ListAsync(accountId, status));

    [HttpGet("drafts/{draftId:guid}")]
    public async Task<IActionResult> Draft(Guid draftId) => Reply(await _compose.GetAsync(draftId));

    [HttpPost("drafts")]
    public async Task<IActionResult> CreateDraft([FromBody] SocialDraftSave request) => Reply(await _compose.SaveAsync(Actor, null, request));

    [HttpPut("drafts/{draftId:guid}")]
    public async Task<IActionResult> SaveDraft(Guid draftId, [FromBody] SocialDraftSave request) => Reply(await _compose.SaveAsync(Actor, draftId, request));

    [HttpPost("drafts/{draftId:guid}/submit")]
    public async Task<IActionResult> Submit(Guid draftId) => Reply(await _compose.SubmitAsync(Actor, draftId));

    [HttpPost("drafts/{draftId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid draftId) => Reply(await _compose.ApproveAsync(Actor, draftId));

    [HttpPost("drafts/{draftId:guid}/send-back")]
    public async Task<IActionResult> SendBack(Guid draftId, [FromBody] SocialSendBackRequest request) => Reply(await _compose.SendBackAsync(Actor, draftId, request.Note));

    [HttpPost("drafts/{draftId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid draftId) => Reply(await _compose.CancelAsync(Actor, draftId));

    [HttpPost("drafts/{draftId:guid}/retry")]
    public async Task<IActionResult> Retry(Guid draftId) => Reply(await _compose.RetryAsync(Actor, draftId));

    [HttpGet("calendar")]
    public async Task<IActionResult> Calendar([FromQuery] Guid accountId, [FromQuery] DateTime from, [FromQuery] DateTime to) =>
        Reply(await _compose.CalendarAsync(accountId, from, to));

    [HttpGet("slots")]
    public async Task<IActionResult> Slots([FromQuery] Guid accountId) => Data(await _compose.SlotsAsync(accountId));

    [HttpPut("slots")]
    public async Task<IActionResult> SaveSlots([FromQuery] Guid accountId, [FromBody] List<SocialSlotDto> slots) => Reply(await _compose.SaveSlotsAsync(Actor, accountId, slots));

    [HttpGet("approvers")]
    public async Task<IActionResult> Approvers() => Data(await _compose.ApproversAsync());

    [HttpGet("publishing-limit")]
    public async Task<IActionResult> PublishingLimit([FromQuery] Guid accountId, [FromServices] SocialKeys keys, [FromServices] IMetaSocial meta)
    {
        if (await keys.LoadAsync(accountId) is not { } k || k.Account.IgUserId == null) return Data<object?>(null);
        try
        {
            var (used, total) = await meta.InstagramPublishingLimitAsync(k.Creds);
            return Data(new { used, total });
        }
        catch (MetaSocialException) { return Data<object?>(null); }
    }

    // ── inbox ───────────────────────────────────────────────────────────────

    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox([FromQuery] SocialConversationQuery query) => Reply(await _inbox.ConversationsAsync(Actor, query));

    [HttpGet("inbox/unread")]
    public async Task<IActionResult> Unread([FromQuery] Guid? accountId) => Data(await _inbox.UnreadAsync(accountId));

    [HttpGet("inbox/agents")]
    public async Task<IActionResult> Agents([FromServices] Data.CmsDbContext db) =>
        Data((await SocialShared.PeopleWithAsync(db, "social.reply")).Select(p => new { userId = p.UserId, fullName = p.FullName }));

    [HttpGet("inbox/dm/{contactId:guid}")]
    public async Task<IActionResult> Thread(Guid contactId) => Reply(await _inbox.ThreadAsync(Actor, contactId));

    [HttpPost("inbox/dm/{contactId:guid}/messages")]
    [RequestSizeLimit(30L * 1024 * 1024)]
    public async Task<IActionResult> Send(Guid contactId, [FromForm] string? text, IFormFile? file)
    {
        await using var stream = file?.OpenReadStream();
        return Reply(await _inbox.SendAsync(Actor, contactId, text, stream, file?.FileName, file?.ContentType));
    }

    [HttpPost("inbox/dm/{contactId:guid}/notes")]
    public async Task<IActionResult> Note(Guid contactId, [FromBody] SocialTextRequest request) => Reply(await _inbox.NoteAsync(Actor, contactId, request.Text));

    [HttpPut("inbox/{id:guid}/assign")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] SocialAssignRequest request) => Reply(await _inbox.AssignAsync(Actor, id, request.UserId));

    [HttpPut("inbox/dm/{contactId:guid}/status")]
    public async Task<IActionResult> Status(Guid contactId, [FromBody] SocialStatusRequest request) => Reply(await _inbox.StatusAsync(Actor, contactId, request.Status));

    [HttpPost("inbox/dm/{contactId:guid}/lead")]
    public async Task<IActionResult> LeadFromChat(Guid contactId, [FromBody] SocialMakeLead request) => Reply(await _leads.FromConversationAsync(Actor, contactId, request));

    [HttpGet("inbox/comments/{commentId:guid}")]
    public async Task<IActionResult> Comment(Guid commentId) => Reply(await _inbox.CommentAsync(Actor, commentId));

    [HttpPost("inbox/comments/{commentId:guid}/reply")]
    public async Task<IActionResult> ReplyToComment(Guid commentId, [FromBody] SocialCommentReply request) => Reply(await _inbox.ReplyToCommentAsync(Actor, commentId, request));

    [HttpPut("inbox/comments/{commentId:guid}/hidden")]
    public async Task<IActionResult> Hide(Guid commentId, [FromBody] SocialHideRequest request) => Reply(await _inbox.HideCommentAsync(Actor, commentId, request.Hidden));

    [HttpPut("inbox/comments/{commentId:guid}/done")]
    public async Task<IActionResult> Done(Guid commentId, [FromQuery] bool done = true) => Reply(await _inbox.CommentDoneAsync(Actor, commentId, done));

    [HttpPost("inbox/comments/{commentId:guid}/lead")]
    public async Task<IActionResult> LeadFromComment(Guid commentId, [FromBody] SocialMakeLead request) => Reply(await _leads.FromCommentAsync(Actor, commentId, request));

    // ── leads and ads ───────────────────────────────────────────────────────

    [HttpGet("leads")]
    public async Task<IActionResult> Leads([FromQuery] SocialLeadQuery query) => Reply(await _leads.ListAsync(query));

    [HttpPost("leads/forms/check")]
    public async Task<IActionResult> CheckForms([FromQuery] Guid accountId) => Reply(await _leads.CheckFormsAsync(accountId, HttpContext.RequestAborted));

    [HttpPost("leads/forms/{formId}/import")]
    public async Task<IActionResult> ImportMissing(string formId, [FromQuery] Guid accountId) => Reply(await _leads.ImportMissingAsync(Actor, accountId, formId, HttpContext.RequestAborted));

    [HttpGet("ads")]
    [HasPermission("social.ads")]
    public async Task<IActionResult> Ads([FromQuery] SocialAdsQuery query) => Reply(await _ads.SummaryAsync(query));
}
