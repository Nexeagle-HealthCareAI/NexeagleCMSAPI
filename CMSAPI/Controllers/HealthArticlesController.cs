using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// Health Wiki articles in the CMS: list, read, write, approve, send back, take down, history and images. The articles live in 1HMSAPI's
// easyHMS database and all rules are enforced there, so this only checks the CMS login and permission and passes the call on (see
// HealthWikiService). The body of a create or an edit is passed on untouched: an absent field means "unchanged", null means "clear".
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/health-articles")]
[HasPermission("health-wiki.manage")]
public class HealthArticlesController : HealthWikiControllerBase
{
    private const long MaxImageBytes = 3 * 1024 * 1024;

    private readonly IHealthWikiService _service;

    public HealthArticlesController(IHealthWikiService service)
    {
        _service = service;
    }

    private static string Slug(string slug) => Uri.EscapeDataString(slug);

    // Optional ?status=DRAFT|IN_REVIEW|PUBLISHED and ?type=MEDICAL|SECTOR_UPDATE.
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? type)
    {
        var query = string.Join("&", new[]
        {
            string.IsNullOrWhiteSpace(status) ? null : $"status={Uri.EscapeDataString(status)}",
            string.IsNullOrWhiteSpace(type) ? null : $"type={Uri.EscapeDataString(type)}",
        }.Where(q => q != null));
        return Reply(await _service.GetAsync("internal/health-articles", query, Actor));
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Get(string slug) =>
        Reply(await _service.GetAsync($"internal/health-articles/{Slug(slug)}", null, Actor));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JsonElement body) =>
        Reply(await _service.SendAsync(HttpMethod.Post, "internal/health-articles", body, Actor, HealthWikiShape.Article));

    [HttpPatch("{slug}")]
    public async Task<IActionResult> Update(string slug, [FromBody] JsonElement body) =>
        Reply(await _service.SendAsync(HttpMethod.Patch, $"internal/health-articles/{Slug(slug)}", body, Actor, HealthWikiShape.Article));

    // A CMS editor publishes a sector update, or approves the edit waiting on a published article. A medical article is approved by its doctor.
    [HttpPost("{slug}/approve")]
    public async Task<IActionResult> Approve(string slug) =>
        Reply(await _service.SendAsync(HttpMethod.Post, $"internal/health-articles/{Slug(slug)}/approve", null, Actor, HealthWikiShape.Article));

    // Send back for changes; { "reason": "..." } is shown to the author.
    [HttpPost("{slug}/withdraw")]
    public async Task<IActionResult> Withdraw(string slug, [FromBody] JsonElement body) =>
        Reply(await _service.SendAsync(HttpMethod.Post, $"internal/health-articles/{Slug(slug)}/withdraw", body, Actor, HealthWikiShape.Article));

    // Take a live article offline at once; { "reason": "..." } is kept in the history.
    [HttpPost("{slug}/unpublish")]
    public async Task<IActionResult> Unpublish(string slug, [FromBody] JsonElement body) =>
        Reply(await _service.SendAsync(HttpMethod.Post, $"internal/health-articles/{Slug(slug)}/unpublish", body, Actor, HealthWikiShape.Article));

    // Take a live article offline and put it back in review; the author cannot publish it alone again. { "reason": "..." } is kept in the history.
    [HttpPost("{slug}/pull-back")]
    public async Task<IActionResult> PullBack(string slug, [FromBody] JsonElement body) =>
        Reply(await _service.SendAsync(HttpMethod.Post, $"internal/health-articles/{Slug(slug)}/pull-back", body, Actor, HealthWikiShape.Article));

    [HttpGet("{slug}/history")]
    public async Task<IActionResult> History(string slug) =>
        Reply(await _service.GetAsync($"internal/health-articles/{Slug(slug)}/history", null, Actor));

    // One cover or inline image (multipart field "file"; JPG, PNG or WebP up to 2 MB, checked again in 1HMSAPI). Answers { data: { url } }.
    [HttpPost("images")]
    [RequestSizeLimit(MaxImageBytes)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadImage(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "Choose an image to upload." });
        if (file.Length > MaxImageBytes)
            return StatusCode(413, new { success = false, message = "The image is larger than 2 MB. Choose a smaller one." });

        await using var stream = file.OpenReadStream();
        return Reply(await _service.UploadImageAsync(file.FileName, file.ContentType, stream, Actor));
    }
}
