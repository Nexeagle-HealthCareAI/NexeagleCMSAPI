using System.Security.Claims;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// CMS → Marketing → Keywords: every search phrase from Search Console, Google Ads, Keyword Planner and Doctor Dekho's
// own search, in one place, grouped into topics and turned into a content plan. Every route needs keywords.view;
// changes need keywords.edit, Setup and "Run now" need keywords.connect (checked in the service).
// Plan: claude/keyword-content-engine-plan.md. Tables: 38_CMSDatabase_Keywords.sql.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/keywords")]
[HasPermission("keywords.view")]
public class KeywordsController : ControllerBase
{
    private readonly IKeywordsService _keywords;
    private readonly IKeywordCollector _collector;

    public KeywordsController(IKeywordsService keywords, IKeywordCollector collector)
    {
        _keywords = keywords;
        _collector = collector;
    }

    private bool Has(string permission) => User.HasClaim(PermissionConstants.ClaimType, permission);

    private KeywordActor Actor => new()
    {
        UserId = Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty,
        CanEdit = Has("keywords.edit"),
        CanConnect = Has("keywords.connect"),
    };

    private IActionResult Reply<T>(WorkResult<T> result) => result.Ok
        ? Ok(new { success = true, data = result.Data, message = result.Message })
        : StatusCode(result.Status, new { success = false, message = result.Message });

    private IActionResult Data<T>(T data) => Ok(new { success = true, data });

    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] string? site) => Data(await _keywords.OverviewAsync(site));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] KeywordQuery query) => Data(await _keywords.ListAsync(query));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id) => Reply(await _keywords.DetailAsync(id));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] KeywordUpdate update) => Reply(await _keywords.UpdateAsync(Actor, id, update));

    // ── topics ──────────────────────────────────────────────────────────────

    [HttpGet("topics")]
    public async Task<IActionResult> Topics([FromQuery] string? site, [FromQuery] string? kind) => Data(await _keywords.TopicsAsync(site, kind));

    [HttpGet("topics/{id:guid}")]
    public async Task<IActionResult> Topic(Guid id) => Reply(await _keywords.TopicAsync(id));

    [HttpPost("topics")]
    public async Task<IActionResult> CreateTopic([FromBody] KeywordTopicSave save) => Reply(await _keywords.CreateTopicAsync(Actor, save));

    [HttpPut("topics/{id:guid}")]
    public async Task<IActionResult> UpdateTopic(Guid id, [FromBody] KeywordTopicSave save) => Reply(await _keywords.UpdateTopicAsync(Actor, id, save));

    [HttpPost("topics/{id:guid}/merge/{into:guid}")]
    public async Task<IActionResult> Merge(Guid id, Guid into) => Reply(await _keywords.MergeTopicAsync(Actor, id, into));

    // ── content plan ────────────────────────────────────────────────────────

    [HttpGet("content")]
    public async Task<IActionResult> Content([FromQuery] string? site, [FromQuery] string? kind) => Data(await _keywords.ContentAsync(site, kind));

    [HttpPost("content")]
    public async Task<IActionResult> CreateContent([FromBody] KeywordContentSave save) => Reply(await _keywords.CreateContentAsync(Actor, save));

    [HttpPut("content/{id:guid}")]
    public async Task<IActionResult> UpdateContent(Guid id, [FromBody] KeywordContentSave save) => Reply(await _keywords.UpdateContentAsync(Actor, id, save));

    [HttpDelete("content/{id:guid}")]
    public async Task<IActionResult> DeleteContent(Guid id) => Reply(await _keywords.DeleteContentAsync(Actor, id));

    // ── setup ───────────────────────────────────────────────────────────────

    [HttpGet("setup")]
    public async Task<IActionResult> Setup() => Data(await _keywords.SetupAsync(Actor));

    [HttpPut("setup")]
    public async Task<IActionResult> SaveSetup([FromBody] KeywordSetupSave save) => Reply(await _keywords.SaveSetupAsync(Actor, save));

    private static readonly HashSet<string> Runnable = new() { "all", "site", "gsc", "ads", "planner" };

    /// <summary>Collects now from one source (site, gsc, ads, planner) or all of them.</summary>
    [HttpPost("run/{source}")]
    public async Task<IActionResult> Run(string source, CancellationToken ct)
    {
        if (!Actor.CanConnect) return StatusCode(403, new { success = false, message = "Only people who can change keyword sources can run a collection." });
        if (!Runnable.Contains(source)) return BadRequest(new { success = false, message = "Choose site, gsc, ads, planner or all." });
        var counts = await _collector.RunAsync(source == "all" ? null : source, ct);
        var total = counts.Values.Sum();
        return Ok(new { success = true, data = counts, message = $"Collected {total:N0} rows." });
    }
}
