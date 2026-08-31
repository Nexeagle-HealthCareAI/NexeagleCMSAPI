using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Services;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;

namespace CMSAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/crm/ai")]
public class CrmAiController : ControllerBase
{
    private readonly IGroqSalesAiService _aiService;
    private readonly ISalesLeadService _leads;

    public CrmAiController(IGroqSalesAiService aiService, ISalesLeadService leads)
    {
        _aiService = aiService;
        _leads = leads;
    }

    [HttpPost("pitch")]
    public async Task<IActionResult> GeneratePitch([FromBody] AiPitchRequest req, CancellationToken ct)
    {
        var lead = await _leads.GetLeadDetailAsync(req.LeadId);
        if (lead == null) return NotFound("Lead not found");

        try
        {
            var pitch = await _aiService.GenerateWhatsAppPitchAsync(
                lead.ContactName ?? "Doctor", lead.HospitalName ?? "Hospital", lead.FacilityType ?? "HOSPITAL", lead.BedCount, lead.City ?? "India", ct);

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
        var lead = await _leads.GetLeadDetailAsync(req.LeadId);
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
