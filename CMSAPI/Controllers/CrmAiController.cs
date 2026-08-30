using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Services;
using CMSAPI.Data;
using Microsoft.EntityFrameworkCore;
using CMSAPI.Domain.Entities;

namespace CMSAPI.Controllers;

[ApiController]
[Route("api/v1/crm/ai")]
public class CrmAiController : ControllerBase
{
    private readonly IGroqSalesAiService _aiService;
    private readonly AppDbContext _db;

    public CrmAiController(IGroqSalesAiService aiService, AppDbContext db)
    {
        _aiService = aiService;
        _db = db;
    }

    [HttpPost("pitch")]
    public async Task<IActionResult> GeneratePitch([FromBody] AiPitchRequest req, CancellationToken ct)
    {
        var lead = await _db.CrmLeads.FirstOrDefaultAsync(l => l.Id == req.LeadId, ct);
        if (lead == null) return NotFound("Lead not found");

        try
        {
            var pitch = await _aiService.GenerateWhatsAppPitchAsync(
                lead.ContactName ?? "Doctor", lead.FacilityName, lead.FacilityType ?? "HOSPITAL", lead.BedCount, lead.City ?? "India", ct);

            return Ok(new { pitch });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("objection")]
    public async Task<IActionResult> HandleObjection([FromBody] AiObjectionRequest req, CancellationToken ct)
    {
        var lead = await _db.CrmLeads.FirstOrDefaultAsync(l => l.Id == req.LeadId, ct);
        if (lead == null) return NotFound("Lead not found");

        try
        {
            var response = await _aiService.ResolveObjectionAsync(
                req.Objection, lead.FacilityType ?? "HOSPITAL", lead.BedCount, ct);

            return Ok(new { response });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("social")]
    public async Task<IActionResult> GenerateSocialCampaign([FromBody] AiSocialRequest req, CancellationToken ct)
    {
        try
        {
            var campaign = await _aiService.GenerateSocialCampaignAsync(req.Topic, req.TargetAudience, ct);
            return Ok(campaign);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class AiPitchRequest
{
    public Guid LeadId { get; set; }
}

public class AiObjectionRequest
{
    public Guid LeadId { get; set; }
    public string Objection { get; set; } = string.Empty;
}

public class AiSocialRequest
{
    public string Topic { get; set; } = string.Empty;
    public string TargetAudience { get; set; } = "Hospital Owners";
}
