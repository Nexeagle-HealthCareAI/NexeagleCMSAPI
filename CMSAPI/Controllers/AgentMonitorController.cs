using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;

namespace CMSAPI.Controllers;

// CMS "AI Agents" tab (Doctor Dekho page): what the NLP-router agents decide, the mistakes a person
// reviews so the model can learn from them, and every model rebuild with its test scores.
// Ingest endpoints are server-to-server from 1HMS-NLP-Router (hms_router/telemetry.py), authenticated
// with the same X-Service-Key as the training-examples feed (ServiceAuth:SymptomRouterServiceKey).
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/agent-monitor")]
public class AgentMonitorController : ControllerBase
{
    private const int MaxBatch = 1000;
    private readonly IAgentMonitorService _service;
    private readonly IConfiguration _configuration;

    public AgentMonitorController(IAgentMonitorService service, IConfiguration configuration)
    {
        _service = service;
        _configuration = configuration;
    }

    private bool IsValidServiceKey(string? key)
    {
        var expected = _configuration["ServiceAuth:SymptomRouterServiceKey"];
        return !string.IsNullOrEmpty(expected) && key != null
               && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                   System.Text.Encoding.UTF8.GetBytes(key), System.Text.Encoding.UTF8.GetBytes(expected));
    }

    // ── ingest (router -> CMS) ─────────────────────────────────────────────────────────────────────
    [AllowAnonymous]
    [HttpPost("ingest/decisions")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> IngestDecisions([FromHeader(Name = "X-Service-Key")] string? serviceKey,
        [FromBody] IngestBatch<DecisionIngestItem> batch)
    {
        if (!IsValidServiceKey(serviceKey)) return Unauthorized();
        if (batch.Items.Count > MaxBatch) return BadRequest($"At most {MaxBatch} items per batch.");
        return Ok(await _service.IngestDecisionsAsync(batch.Items));
    }

    [AllowAnonymous]
    [HttpPost("ingest/feedback")]
    public async Task<IActionResult> IngestFeedback([FromHeader(Name = "X-Service-Key")] string? serviceKey,
        [FromBody] IngestBatch<FeedbackIngestItem> batch)
    {
        if (!IsValidServiceKey(serviceKey)) return Unauthorized();
        if (batch.Items.Count > MaxBatch) return BadRequest($"At most {MaxBatch} items per batch.");
        return Ok(await _service.IngestFeedbackAsync(batch.Items));
    }

    [AllowAnonymous]
    [HttpPost("ingest/model-versions")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> IngestModelVersions([FromHeader(Name = "X-Service-Key")] string? serviceKey,
        [FromBody] IngestBatch<ModelVersionIngestItem> batch)
    {
        if (!IsValidServiceKey(serviceKey)) return Unauthorized();
        return Ok(await _service.IngestModelVersionsAsync(batch.Items));
    }

    // ── dashboard ──────────────────────────────────────────────────────────────────────────────────
    // from/to: inclusive India-time dates (default: last 30 days). agent: route | chat. caller: API client name.
    [HasPermission("insights.view")]
    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? agent = null, [FromQuery] string? caller = null)
        => Ok(await _service.GetOverviewAsync(from, to, agent, caller));

    [HasPermission("insights.view")]
    [HttpGet("decisions")]
    public async Task<IActionResult> GetDecisions([FromQuery] int page = 1, [FromQuery] int limit = 20,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] string? agent = null,
        [FromQuery] string? caller = null, [FromQuery] string? decision = null)
        => Ok(await _service.GetDecisionsAsync(page, limit, from, to, agent, caller, decision));

    [HasPermission("insights.view")]
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus() => Ok(await _service.GetServiceStatusAsync());

    // ── learning loop ──────────────────────────────────────────────────────────────────────────────
    [HasPermission("insights.view")]
    [HttpGet("learning/summary")]
    public async Task<IActionResult> GetLearningSummary() => Ok(await _service.GetLearningSummaryAsync());

    // status: pending | approved | rejected | awaiting (approved, not yet in a model) | learned
    [HasPermission("insights.view")]
    [HttpGet("learning/items")]
    public async Task<IActionResult> GetLearningItems([FromQuery] int page = 1, [FromQuery] int limit = 20,
        [FromQuery] string? status = "pending", [FromQuery] string? source = null, [FromQuery] string? search = null)
        => Ok(await _service.GetLearningItemsAsync(page, limit, status, source, search));

    [HasPermission("insights.manage")]
    [HttpPost("learning/scan")]
    public async Task<IActionResult> Scan() => Ok(new { found = await _service.ScanBookingCorrectionsAsync(true) });

    [HasPermission("insights.manage")]
    [HttpPost("learning/items/{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, [FromBody] ReviewLearningItemRequest request)
    {
        try
        {
            var item = await _service.ReviewAsync(id, request, User.Identity?.Name);
            return item == null ? NotFound() : Ok(item);
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    // ── models ─────────────────────────────────────────────────────────────────────────────────────
    [HasPermission("insights.view")]
    [HttpGet("models")]
    public async Task<IActionResult> GetModels([FromQuery] int limit = 50) => Ok(await _service.GetModelVersionsAsync(limit));

    [HasPermission("insights.manage")]
    [HttpPost("models/rebuild")]
    public async Task<IActionResult> Rebuild()
    {
        var (started, status, body) = await _service.TriggerRebuildAsync();
        if (started) return Accepted(new { message = "Rebuild started. It takes a few minutes; the result appears under Model versions." });
        return StatusCode(status == 409 ? 409 : 502, new { message = status == 409 ? "A rebuild is already running." : body });
    }
}
