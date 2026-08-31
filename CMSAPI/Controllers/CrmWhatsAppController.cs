using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Services;
using CMSAPI.Data;
using Microsoft.EntityFrameworkCore;
using CMSAPI.Domain.Entities;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;

namespace CMSAPI.Controllers;

[ApiController]
[ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/crm/whatsapp")]
public class CrmWhatsAppController : ControllerBase
{
    private readonly ISalesLeadService _leads;
    private readonly IWhatsAppService _waService;

    public CrmWhatsAppController(ISalesLeadService leads, IWhatsAppService waService)
    {
        _leads = leads;
        _waService = waService;
    }

    [HttpPost("dispatch-template")]
    public async Task<IActionResult> DispatchTemplate([FromBody] DispatchTemplateRequest req, CancellationToken ct)
    {
        var lead = await _leads.GetLeadDetailAsync(req.LeadId);
        if (lead == null) return NotFound("Lead not found");

        var success = await _waService.SendTemplateMessageAsync(lead.Mobile ?? "", req.TemplateName, ct: ct);
        
        if (success)
        {
            var followUpReq = new AddFollowUpRequest
            {
                ActivityType = "WhatsApp",
                Direction = "OUTBOUND",
                Notes = $"Sent template: {req.TemplateName}",
                TemplateName = req.TemplateName
            };
            await _leads.AddFollowUpAsync(req.LeadId, followUpReq, Guid.Empty, "System");
            return Ok(new { success = true });
        }

        return BadRequest("Failed to send WhatsApp message");
    }
}

public class DispatchTemplateRequest
{
    public Guid LeadId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
}
