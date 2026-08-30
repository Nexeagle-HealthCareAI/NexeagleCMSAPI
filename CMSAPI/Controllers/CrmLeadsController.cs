using Microsoft.AspNetCore.Mvc;
using CMSAPI.Domain.Entities;
using CMSAPI.Data;
using Microsoft.EntityFrameworkCore;
using CMSAPI.Application.Services;

namespace CMSAPI.Controllers;

[ApiController]
[Route("api/v1/crm/leads")]
public class CrmLeadsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IGroqSalesAiService _aiService;
    private readonly IWhatsAppService _waService;

    public CrmLeadsController(AppDbContext db, IGroqSalesAiService aiService, IWhatsAppService waService)
    {
        _db = db;
        _aiService = aiService;
        _waService = waService;
    }

    [HttpPost("quick-add")]
    public async Task<IActionResult> QuickAddLead([FromBody] QuickAddLeadRequest req, CancellationToken ct)
    {
        try
        {
            // Basic deduplication
            var existing = await _db.CrmLeads
                .FirstOrDefaultAsync(l => l.PhoneNumber == req.PhoneNumber, ct);

            if (existing != null)
                return BadRequest(new { error = "Lead with this phone number already exists." });

            var lead = new CrmLead
            {
                LeadNumber = $"LEAD-{DateTime.UtcNow:MMdd}-{Random.Shared.Next(1000, 9999)}",
                ContactName = req.ContactName,
                FacilityName = req.FacilityName,
                FacilityType = req.FacilityType ?? "HOSPITAL",
                BedCount = req.BedCount,
                City = req.City,
                PhoneNumber = req.PhoneNumber,
                SourceChannel = "MANUAL",
                Status = "NEW"
            };

            // AI Scoring
            var aiAnalysis = await _aiService.ScoreAndEnrichLeadAsync(
                lead.FacilityName, lead.FacilityType, lead.BedCount, lead.City, "Manual Lead Addition", ct);

            lead.AiIntentScore = aiAnalysis.IntentScore;
            lead.AiPersonaSummary = $"{aiAnalysis.BuyerPersona} | Hook: {aiAnalysis.RecommendedHook}";

            _db.CrmLeads.Add(lead);
            await _db.SaveChangesAsync(ct);

            return Ok(new { success = true, leadId = lead.Id });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetLeads(CancellationToken ct)
    {
        try
        {
            var leads = await _db.CrmLeads
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => new 
                {
                    id = l.Id,
                    leadNumber = l.LeadNumber,
                    contactName = l.ContactName,
                    facilityName = l.FacilityName,
                    facilityType = l.FacilityType,
                    bedCount = l.BedCount,
                    city = l.City,
                    state = "Unknown", // Assuming mapping logic here
                    phoneNumber = l.PhoneNumber,
                    sourceChannel = l.SourceChannel,
                    status = l.Status,
                    aiIntentScore = l.AiIntentScore,
                    aiPersonaSummary = l.AiPersonaSummary,
                    dealValue = l.BedCount * 2500, // Dummy calculation for deal value
                    createdAt = l.CreatedAt,
                    updatedAt = l.UpdatedAt
                })
                .ToListAsync(ct);
                
            return Ok(leads);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, inner = ex.InnerException?.Message });
        }
    }

    [HttpPatch("{id}/stage")]
    public async Task<IActionResult> UpdateLeadStage(Guid id, [FromBody] UpdateLeadStageRequest req, CancellationToken ct)
    {
        var lead = await _db.CrmLeads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead == null) return NotFound("Lead not found");

        lead.Status = req.Stage;
        lead.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }

    [HttpPost("{id}/whatsapp-template")]
    public async Task<IActionResult> SendWhatsAppTemplate(Guid id, [FromBody] SendTemplateRequest req, CancellationToken ct)
    {
        var lead = await _db.CrmLeads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead == null) return NotFound("Lead not found");

        if (string.IsNullOrWhiteSpace(lead.PhoneNumber))
            return BadRequest("Lead has no phone number");

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
                        new { type = "video", video = new { link = "https://1hms.nexeagle.com/assets/video_pitch.mp4" } }
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
                        new { type = "document", document = new { link = "https://1hms.nexeagle.com/assets/case_study.pdf", filename = "CaseStudy.pdf" } }
                    }
                }
            };
        }

        var success = await _waService.SendTemplateMessageAsync(lead.PhoneNumber, req.TemplateName, components: components, ct: ct);
        
        if (success)
        {
            var activity = new CrmLeadActivity
            {
                LeadId = lead.Id,
                ActivityType = "WhatsApp",
                Direction = "OUTBOUND",
                MessageBody = $"Sent Meta Template: {req.TemplateName}",
                TemplateName = req.TemplateName,
                CreatedAt = DateTime.UtcNow
            };
            _db.CrmLeadActivities.Add(activity);
            await _db.SaveChangesAsync(ct);
            return Ok(new { success = true });
        }
        else
        {
            return StatusCode(500, new { error = "WhatsApp API failed to send the template." });
        }
    }
}

public class UpdateLeadStageRequest
{
    public string Stage { get; set; } = string.Empty;
}

public class QuickAddLeadRequest
{
    public string ContactName { get; set; } = string.Empty;
    public string FacilityName { get; set; } = string.Empty;
    public string? FacilityType { get; set; }
    public int BedCount { get; set; }
    public string City { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
}

public class SendTemplateRequest
{
    public string TemplateName { get; set; } = string.Empty;
}
