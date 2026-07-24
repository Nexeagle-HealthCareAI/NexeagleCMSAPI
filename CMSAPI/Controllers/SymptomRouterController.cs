using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;

using Microsoft.AspNetCore.Authorization;

namespace CMSAPI.Controllers;

// Backs the Doctor Dekho page's "NLP" tab in the CMS — the Hinglish symptom-router training
// data editor, production feedback log, and model info / retrain trigger. Reuses
// "insights.view" for now rather than seeding a brand-new RBAC permission for this one
// feature; split into its own permission later if finer-grained access control is needed.
[Authorize]
[ApiController]
[Route("api/v1/symptom-router")]
public class SymptomRouterController : ControllerBase
{
    private readonly ISymptomRouterService _service;
    private readonly IConfiguration _configuration;

    public SymptomRouterController(ISymptomRouterService service, IConfiguration configuration)
    {
        _service = service;
        _configuration = configuration;
    }

    // ── Training data editor ────────────────────────────────────────────────

    [HasPermission("insights.view")]
    [HttpGet("training-examples")]
    public async Task<IActionResult> GetTrainingExamples(
        [FromQuery] int page = 1, [FromQuery] int limit = 20,
        [FromQuery] string? specialist = null, [FromQuery] string? search = null)
    {
        var result = await _service.GetTrainingExamplesAsync(page, limit, specialist, search);
        return Ok(result);
    }

    [HasPermission("insights.view")]
    [HttpPost("training-examples")]
    public async Task<IActionResult> AddTrainingExample([FromBody] UpsertTrainingExampleRequest request)
    {
        var createdBy = User.Identity?.Name;
        var result = await _service.AddTrainingExampleAsync(request, createdBy);
        if (result == null) return BadRequest("Text is required and Specialist must be one of the router's known specialist labels.");
        return Ok(result);
    }

    [HasPermission("insights.view")]
    [HttpPut("training-examples/{id:guid}")]
    public async Task<IActionResult> UpdateTrainingExample(Guid id, [FromBody] UpsertTrainingExampleRequest request)
    {
        var result = await _service.UpdateTrainingExampleAsync(id, request);
        if (result == null) return NotFound("Example not found, or Specialist isn't one of the router's known labels.");
        return Ok(result);
    }

    [HasPermission("insights.view")]
    [HttpDelete("training-examples/{id:guid}")]
    public async Task<IActionResult> DeleteTrainingExample(Guid id)
    {
        var deleted = await _service.DeleteTrainingExampleAsync(id);
        if (!deleted) return NotFound();
        return Ok(new { message = "Deleted." });
    }

    // Server-to-server: the retrain pipeline (running in GitHub Actions, no CMS session)
    // pulls the current active training set through here — same shared-key convention as
    // EasyHmsSubscriptionPlansController's /service endpoint.
    [AllowAnonymous]
    [HttpGet("training-examples/service")]
    public async Task<IActionResult> GetTrainingExamplesForService(
        [FromHeader(Name = "X-Service-Key")] string? serviceKey,
        [FromQuery] int page = 1, [FromQuery] int limit = 500)
    {
        if (!IsValidServiceKey(serviceKey)) return Unauthorized();
        var result = await _service.GetTrainingExamplesAsync(page, limit, null, null);
        return Ok(result);
    }

    // ── Feedback log ─────────────────────────────────────────────────────────

    [HasPermission("insights.view")]
    [HttpGet("feedback-log")]
    public async Task<IActionResult> GetFeedbackLog(
        [FromQuery] int page = 1, [FromQuery] int limit = 20,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] bool? correctionsOnly = null)
    {
        var result = await _service.GetFeedbackLogAsync(page, limit, from, to, correctionsOnly);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("feedback-log/service")]
    public async Task<IActionResult> GetFeedbackLogForService(
        [FromHeader(Name = "X-Service-Key")] string? serviceKey,
        [FromQuery] int page = 1, [FromQuery] int limit = 500,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
    {
        if (!IsValidServiceKey(serviceKey)) return Unauthorized();
        var result = await _service.GetFeedbackLogAsync(page, limit, from, to, null);
        return Ok(result);
    }

    // ── Model info / retrain trigger ────────────────────────────────────────

    [HasPermission("insights.view")]
    [HttpGet("model-info")]
    public async Task<IActionResult> GetModelInfo()
    {
        var info = await _service.GetModelInfoAsync();
        if (info == null) return StatusCode(502, "Could not reach the NLP router service.");
        return Ok(info);
    }

    [HasPermission("insights.view")]
    [HttpPost("retrain")]
    public async Task<IActionResult> TriggerRetrain()
    {
        var triggered = await _service.TriggerRetrainAsync();
        if (!triggered) return StatusCode(502, "Could not trigger the retrain workflow — check GitHub:RetrainWorkflowToken/Repo config.");
        return Ok(new { message = "Retrain triggered. This runs in the background — check back in a few minutes." });
    }

    private bool IsValidServiceKey(string? serviceKey)
    {
        var expectedKey = _configuration["ServiceAuth:SymptomRouterServiceKey"];
        return !string.IsNullOrEmpty(expectedKey) && serviceKey == expectedKey;
    }
}
