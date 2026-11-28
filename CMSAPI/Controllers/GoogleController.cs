using System.Security.Claims;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// CMS → Google: Google Analytics, Google Ads, Search Console and Google lead forms for each website (NexEagle,
// Doctor Dekho). Every route needs google.view; Ads needs google.ads; Setup changes need google.connect (checked in
// the service). Public routes (Google's sign-in return, lead webhook, website form, tag IDs): GooglePublicController.
// Plan: claude/google-analytics-ads-plan.md. Tables: 37_CMSDatabase_Google.sql.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/google")]
[HasPermission("google.view")]
public class GoogleController : ControllerBase
{
    private readonly IGoogleSetupService _setup;
    private readonly IGoogleReportsService _reports;
    private readonly IGoogleLeadsService _leads;
    private readonly IConfiguration _config;

    public GoogleController(IGoogleSetupService setup, IGoogleReportsService reports, IGoogleLeadsService leads, IConfiguration config)
    {
        _setup = setup;
        _reports = reports;
        _leads = leads;
        _config = config;
    }

    private bool Has(string permission) => User.HasClaim(PermissionConstants.ClaimType, permission);

    private GoogleActor Actor => new()
    {
        UserId = Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty,
        Name = User.FindFirst("name")?.Value ?? "Someone",
        CanAds = Has("google.ads"),
        CanConnect = Has("google.connect"),
    };

    private IActionResult Reply<T>(WorkResult<T> result) => result.Ok
        ? Ok(new { success = true, data = result.Data, message = result.Message })
        : StatusCode(result.Status, new { success = false, message = result.Message });

    /// <summary>This API's public address: Google__PublicApiUrl (or Social's) when set, else the address of this request.</summary>
    internal static string PublicApi(IConfiguration config, HttpRequest request)
    {
        var configured = config["Google:PublicApiUrl"] ?? config["Social:PublicApiUrl"] ?? config["WhatsAppMarketing:PublicApiUrl"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim().TrimEnd('/');
        var host = request.Host.Host;
        var plain = host == "localhost" || System.Net.IPAddress.TryParse(host, out _);
        var port = request.Host.Port is { } p && p is not (80 or 443) && plain ? $":{p}" : string.Empty;
        return $"{(plain ? request.Scheme : "https")}://{host}{port}";
    }

    private string RedirectUrl => PublicApi(_config, Request) + "/api/v1/google/oauth/callback";
    private string LeadUrl => PublicApi(_config, Request) + "/api/v1/google/leads";

    private static DateOnly Day(string? s) => DateOnly.TryParse(s, out var d) ? d : default;

    // ── accounts and setup ──────────────────────────────────────────────────

    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts() => Ok(new { success = true, data = await _setup.AccountsAsync() });

    [HttpPost("accounts")]
    public async Task<IActionResult> Create([FromBody] GoogleAccountCreate request) => Reply(await _setup.CreateAsync(Actor, request));

    [HttpGet("accounts/{id:guid}/setup")]
    public async Task<IActionResult> Setup(Guid id) => Reply(await _setup.SetupAsync(Actor, id, RedirectUrl, LeadUrl));

    [HttpPut("accounts/{id:guid}/client")]
    public async Task<IActionResult> SaveClient(Guid id, [FromBody] GoogleClientSave request) => Reply(await _setup.SaveClientAsync(Actor, id, request, RedirectUrl, LeadUrl));

    /// <summary>Starts Google's sign-in; the browser goes to the returned address and comes back to returnUrl.</summary>
    [HttpPost("accounts/{id:guid}/signin")]
    public async Task<IActionResult> SignIn(Guid id, [FromBody] GoogleSignInRequest request)
    {
        // Only back to the CMS page that asked (same origin as the request), never to another site.
        var origin = Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && !request.ReturnUrl.StartsWith(origin.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success = false, message = "The page to return to must be in the CMS." });
        return Reply(await _setup.SignInUrlAsync(Actor, id, request.ReturnUrl, RedirectUrl));
    }

    [HttpGet("accounts/{id:guid}/discover")]
    public async Task<IActionResult> Discover(Guid id) => Reply(await _setup.DiscoverAsync(Actor, id));

    [HttpPut("accounts/{id:guid}/choose")]
    public async Task<IActionResult> Choose(Guid id, [FromBody] GoogleChoice request) => Reply(await _setup.ChooseAsync(Actor, id, request, RedirectUrl, LeadUrl));

    [HttpPut("accounts/{id:guid}/tag")]
    public async Task<IActionResult> SaveTag(Guid id, [FromBody] GoogleTagSave request) => Reply(await _setup.SaveTagAsync(Actor, id, request, RedirectUrl, LeadUrl));

    [HttpPost("accounts/{id:guid}/lead-key")]
    public async Task<IActionResult> NewLeadKey(Guid id) => Reply(await _setup.NewLeadKeyAsync(Actor, id, RedirectUrl, LeadUrl));

    [HttpPut("accounts/{id:guid}/outcomes")]
    public async Task<IActionResult> SaveOutcomes(Guid id, [FromBody] List<GoogleOutcome> outcomes) => Reply(await _setup.SaveOutcomesAsync(Actor, id, outcomes, RedirectUrl, LeadUrl));

    [HttpGet("accounts/{id:guid}/checks")]
    public async Task<IActionResult> Checks(Guid id) => Reply(await _setup.ChecksAsync(id));

    // ── numbers ─────────────────────────────────────────────────────────────

    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] Guid accountId, [FromQuery] string? from, [FromQuery] string? to) =>
        Reply(await _reports.OverviewAsync(new GoogleRange { AccountId = accountId, From = Day(from), To = Day(to) }, HttpContext.RequestAborted));

    [HttpGet("pages")]
    public async Task<IActionResult> Pages([FromQuery] Guid accountId, [FromQuery] string? from, [FromQuery] string? to) =>
        Reply(await _reports.PagesAsync(new GoogleRange { AccountId = accountId, From = Day(from), To = Day(to) }, HttpContext.RequestAborted));

    [HttpGet("pages/detail")]
    public async Task<IActionResult> Page([FromQuery] Guid accountId, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string path) =>
        Reply(await _reports.PageAsync(new GoogleRange { AccountId = accountId, From = Day(from), To = Day(to) }, path, HttpContext.RequestAborted));

    [HttpGet("ads")]
    [HasPermission("google.ads")]
    public async Task<IActionResult> Ads([FromQuery] Guid accountId, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? level) =>
        Reply(await _reports.AdsAsync(new GoogleAdsQuery { AccountId = accountId, From = Day(from), To = Day(to), Level = level ?? "campaign" }, HttpContext.RequestAborted));

    [HttpGet("leads")]
    public async Task<IActionResult> Leads([FromQuery] GoogleLeadQuery query) => Reply(await _leads.ListAsync(query));
}
