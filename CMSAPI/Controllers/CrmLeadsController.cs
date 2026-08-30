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

    public CrmLeadsController(AppDbContext db, IGroqSalesAiService aiService)
    {
        _db = db;
        _aiService = aiService;
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

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateLead(Guid id, [FromBody] UpdateLeadRequest req, CancellationToken ct)
    {
        var lead = await _db.CrmLeads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead == null) return NotFound("Lead not found");

        if (!string.IsNullOrWhiteSpace(req.HospitalName)) lead.FacilityName = req.HospitalName;
        if (req.ContactName != null) lead.ContactName = req.ContactName;
        if (req.Mobile != null) lead.PhoneNumber = req.Mobile;
        if (req.City != null) lead.City = req.City;
        if (req.Stage != null) lead.Status = req.Stage;

        lead.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            leadId = lead.Id,
            hospitalName = lead.FacilityName,
            contactName = lead.ContactName,
            mobile = lead.PhoneNumber,
            city = lead.City,
            stage = lead.Status
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetLead(Guid id, CancellationToken ct)
    {
        var lead = await _db.CrmLeads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead == null) return NotFound("Lead not found");

        return Ok(new
        {
            leadId = lead.Id,
            leadNumber = lead.LeadNumber,
            contactName = lead.ContactName,
            hospitalName = lead.FacilityName,
            facilityType = lead.FacilityType,
            bedCount = lead.BedCount,
            city = lead.City,
            state = "Unknown",
            mobile = lead.PhoneNumber,
            sourceChannel = lead.SourceChannel,
            stage = lead.Status,
            priority = "Medium", // Or fetch from DB if available
            aiIntentScore = lead.AiIntentScore,
            aiPersonaSummary = lead.AiPersonaSummary,
            createdAt = lead.CreatedAt,
            updatedAt = lead.UpdatedAt,
            followUps = new List<object>() // Empty for now, can implement later
        });
    }

    [HttpPost("{id}/followups")]
    public async Task<IActionResult> AddFollowUp(Guid id, [FromBody] AddFollowUpRequest req, CancellationToken ct)
    {
        // Mock implementation to prevent 404
        return Ok(new
        {
            followUpId = Guid.NewGuid(),
            activityType = req.ActivityType,
            notes = req.Notes,
            authorName = "Current User",
            createdAt = DateTime.UtcNow
        });
    }
}

public class UpdateLeadStageRequest
{
    public string Stage { get; set; } = string.Empty;
}

public class UpdateLeadRequest
{
    public string? HospitalName { get; set; }
    public string? ContactName { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Stage { get; set; }
    public string? Priority { get; set; }
    public Guid? AssignedToUserId { get; set; }
}

public class AddFollowUpRequest
{
    public string ActivityType { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
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
