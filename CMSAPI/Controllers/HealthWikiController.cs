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
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// Health Wiki people and requests in the CMS: contributors (invite, send a link, verify, reject), contributors' topic requests, the
// conditions an article can link to, and the counts on the CMS tabs. Like HealthArticlesController this only passes the call on to
// 1HMSAPI after the CMS login and the health-wiki.manage permission are checked.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/health-wiki")]
[HasPermission("health-wiki.manage")]
public class HealthWikiController : HealthWikiControllerBase
{
    private readonly IHealthWikiService _service;

    public HealthWikiController(IHealthWikiService service)
    {
        _service = service;
    }

    private static string Query(params (string Name, string? Value)[] parts) =>
        string.Join("&", parts.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value!)}"));

    [HttpGet("conditions")]
    public async Task<IActionResult> Conditions() => Reply(await _service.GetAsync("internal/health-wiki/conditions", null, Actor));

    // { pendingContributors, openTopics } for the numbers on the CMS tabs.
    [HttpGet("summary")]
    public async Task<IActionResult> Summary() => Reply(await _service.GetAsync("internal/health-wiki/summary", null, Actor));

    // ---- contributors ----------------------------------------------------------------------------------------------

    [HttpGet("contributors")]
    public async Task<IActionResult> Contributors([FromQuery] string? status, [FromQuery] string? type) =>
        Reply(await _service.GetAsync("internal/health-wiki/contributors", Query(("status", status), ("type", type)), Actor));

    // { fullName, mobile, type } -> creates the person and sends the join link. Answers the contributor, plus link and linkDelivered so the
    // link can be copied when WhatsApp did not deliver it.
    [HttpPost("contributors/invite")]
    public async Task<IActionResult> Invite([FromBody] JsonElement body) =>
        Reply(await _service.SendAsync(HttpMethod.Post, "internal/health-wiki/contributors/invite", body, Actor, HealthWikiShape.Contributor));

    // { articleSlug? } -> a new link, replacing the unused one: the review link (the article's reviewer) or the write link (its author).
    [HttpPost("contributors/{id:guid}/send-link")]
    public async Task<IActionResult> SendLink(Guid id, [FromBody] JsonElement? body) =>
        Reply(await _service.SendAsync(HttpMethod.Post, $"internal/health-wiki/contributors/{id}/send-link", body, Actor, HealthWikiShape.Contributor));

    // A hospital doctor is verified in the CMS doctor list instead (the NMC register check); this answers 409 for them, with that message.
    [HttpPost("contributors/{id:guid}/verify")]
    public async Task<IActionResult> Verify(Guid id) =>
        Reply(await _service.SendAsync(HttpMethod.Post, $"internal/health-wiki/contributors/{id}/verify", null, Actor, HealthWikiShape.Contributor));

    [HttpPost("contributors/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] JsonElement body) =>
        Reply(await _service.SendAsync(HttpMethod.Post, $"internal/health-wiki/contributors/{id}/reject", body, Actor, HealthWikiShape.Contributor));

    // ---- topic requests --------------------------------------------------------------------------------------------

    [HttpGet("topic-requests")]
    public async Task<IActionResult> TopicRequests([FromQuery] string? status) =>
        Reply(await _service.GetAsync("internal/health-wiki/topic-requests", Query(("status", status)), Actor));

    [HttpGet("topic-requests/{id:guid}")]
    public async Task<IActionResult> TopicRequest(Guid id) =>
        Reply(await _service.GetAsync($"internal/health-wiki/topic-requests/{id}", null, Actor));

    // Creates a draft for the contributor to write.
    [HttpPost("topic-requests/{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id) =>
        Reply(await _service.SendAsync(HttpMethod.Post, $"internal/health-wiki/topic-requests/{id}/accept", null, Actor, HealthWikiShape.Whole));

    // { reason } is required.
    [HttpPost("topic-requests/{id:guid}/decline")]
    public async Task<IActionResult> Decline(Guid id, [FromBody] JsonElement body) =>
        Reply(await _service.SendAsync(HttpMethod.Post, $"internal/health-wiki/topic-requests/{id}/decline", body, Actor, HealthWikiShape.Whole));

    // { message } says what detail is needed.
    [HttpPost("topic-requests/{id:guid}/ask-detail")]
    public async Task<IActionResult> AskDetail(Guid id, [FromBody] JsonElement body) =>
        Reply(await _service.SendAsync(HttpMethod.Post, $"internal/health-wiki/topic-requests/{id}/ask-detail", body, Actor, HealthWikiShape.Whole));
}
