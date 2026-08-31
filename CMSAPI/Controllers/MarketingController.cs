using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using CMSAPI.Application.Services;

namespace CMSAPI.Controllers;

// Backs the CMS "Marketing" tab.
// ── Section 1: Demo Logins  — read-only mirror of HospitalLeads (auto-captured QR funnel).
// ── Section 2: Sales Leads  — manual B2B prospecting pipeline stored in CmsSalesLeads.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/marketing")]
public class MarketingController : ControllerBase
{
    private readonly IMarketingService _marketing;
    private readonly ISalesLeadService _leads;
    private readonly IGroqSalesAiService _aiService;
    private readonly IWhatsAppService _waService;
    private readonly IConfiguration _config;

    public MarketingController(IMarketingService marketing, ISalesLeadService leads, IGroqSalesAiService aiService, IWhatsAppService waService, IConfiguration config)
    {
        _marketing = marketing;
        _leads = leads;
        _aiService = aiService;
        _waService = waService;
        _config = config;
    }

    // ── Demo Logins ───────────────────────────────────────────────────────

    [HasPermission("marketing.view")]
    [HttpGet("demo-logins")]
    public async Task<IActionResult> GetDemoLoginLeads([FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var result = await _marketing.GetDemoLoginLeadsAsync(page, limit);
        return Ok(result);
    }

    [HasPermission("marketing.view")]
    [HttpGet("demo-logins/stats")]
    public async Task<IActionResult> GetDemoLoginStats()
    {
        var result = await _marketing.GetDemoLoginStatsAsync();
        return Ok(result);
    }

    // ── Sales Leads Pipeline ──────────────────────────────────────────────

    [HasPermission("marketing.view")]
    [HttpGet("leads")]
    public async Task<IActionResult> GetLeads(
        [FromQuery] string? stage,
        [FromQuery] string? priority,
        [FromQuery] Guid? assignedToUserId,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20)
    {
        var filter = new SalesLeadFilter
        {
            Stage = stage,
            Priority = priority,
            AssignedToUserId = assignedToUserId,
            Search = search,
            Page = page,
            Limit = limit,
        };
        var result = await _leads.GetLeadsAsync(filter);
        return Ok(result);
    }

    [HasPermission("marketing.view")]
    [HttpGet("leads/{id:guid}")]
    public async Task<IActionResult> GetLead(Guid id)
    {
        var lead = await _leads.GetLeadDetailAsync(id);
        if (lead == null) return NotFound();
        return Ok(lead);
    }

    [HasPermission("marketing.manage")]
    [HttpPost("leads")]
    public async Task<IActionResult> CreateLead([FromBody] CreateSalesLeadRequest req, CancellationToken ct)
    {
        var (userId, userName) = GetCurrentUser();
        if (userId == Guid.Empty) return Unauthorized();

        // AI Scoring
        var aiAnalysis = await _aiService.ScoreAndEnrichLeadAsync(
            req.HospitalName, req.FacilityType ?? "HOSPITAL", req.BedCount, req.City ?? "Unknown", "Manual Lead Addition", ct);

        req.AiIntentScore = aiAnalysis.IntentScore;
        req.AiPersonaSummary = $"{aiAnalysis.BuyerPersona} | Hook: {aiAnalysis.RecommendedHook}";
        if (string.IsNullOrEmpty(req.LeadNumber)) 
            req.LeadNumber = $"LEAD-{DateTime.UtcNow:MMdd}-{Random.Shared.Next(1000, 9999)}";

        var lead = await _leads.CreateLeadAsync(req, userId, userName);
        return CreatedAtAction(nameof(GetLead), new { id = lead.LeadId }, lead);
    }

    [HasPermission("marketing.manage")]
    [HttpPut("leads/{id:guid}")]
    public async Task<IActionResult> UpdateLead(Guid id, [FromBody] UpdateSalesLeadRequest req)
    {
        var lead = await _leads.UpdateLeadAsync(id, req);
        if (lead == null) return NotFound();
        return Ok(lead);
    }

    [HasPermission("marketing.manage")]
    [HttpDelete("leads/{id:guid}")]
    public async Task<IActionResult> DeleteLead(Guid id)
    {
        var deleted = await _leads.DeleteLeadAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HasPermission("marketing.manage")]
    [HttpPost("leads/{id:guid}/followups")]
    public async Task<IActionResult> AddFollowUp(Guid id, [FromBody] AddFollowUpRequest req)
    {
        var (userId, userName) = GetCurrentUser();
        if (userId == Guid.Empty) return Unauthorized();

        var followUp = await _leads.AddFollowUpAsync(id, req, userId, userName);
        if (followUp == null) return NotFound();
        return Ok(followUp);
    }

    [HasPermission("marketing.manage")]
    [HttpPost("leads/{id:guid}/whatsapp-template")]
    public async Task<IActionResult> SendWhatsAppTemplate(Guid id, [FromBody] SendTemplateRequest req, CancellationToken ct)
    {
        var lead = await _leads.GetLeadDetailAsync(id);
        if (lead == null) return NotFound("Lead not found");

        if (string.IsNullOrWhiteSpace(lead.Mobile))
            return BadRequest("Lead has no phone number (Mobile)");

        // Format components based on template
        object[]? components = null;
        if (req.TemplateName == "day1_intro_pitch")
        {
            components = new[]
            {
                new
                {
                    type = "header",
                    parameters = new[]
                    {
                        new { type = "video", video = new { link = _config["WhatsApp:Assets:VideoPitchUrl"] ?? "https://1hms.nexeagle.com/assets/video_pitch.mp4" } }
                    }
                }
            };
        }
        else if (req.TemplateName == "day3_roi_case_study")
        {
            components = new[]
            {
                new
                {
                    type = "header",
                    parameters = new[]
                    {
                        new { type = "document", document = new { link = _config["WhatsApp:Assets:CaseStudyUrl"] ?? "https://1hms.nexeagle.com/assets/case_study.pdf", filename = "CaseStudy.pdf" } }
                    }
                }
            };
        }

        var success = await _waService.SendTemplateMessageAsync(lead.Mobile, req.TemplateName, components: components, ct: ct);
        
        if (success)
        {
            var (userId, userName) = GetCurrentUser();
            var followUpReq = new AddFollowUpRequest
            {
                ActivityType = "WhatsApp",
                Direction = "OUTBOUND",
                Notes = $"Sent Meta Template: {req.TemplateName}",
                TemplateName = req.TemplateName
            };
            await _leads.AddFollowUpAsync(id, followUpReq, userId, userName);
            return Ok(new { success = true });
        }
        else
        {
            return StatusCode(500, new { error = "WhatsApp API failed to send the template." });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private (Guid UserId, string UserName) GetCurrentUser()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var name  = User.FindFirstValue("name") ?? "Unknown";
        return Guid.TryParse(idStr, out var id) ? (id, name) : (Guid.Empty, name);
    }
}
