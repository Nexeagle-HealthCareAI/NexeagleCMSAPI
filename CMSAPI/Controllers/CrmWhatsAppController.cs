using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Services;
using CMSAPI.Data;
using Microsoft.EntityFrameworkCore;
using CMSAPI.Domain.Entities;

namespace CMSAPI.Controllers;

[ApiController]
[Route("api/v1/crm/whatsapp")]
public class CrmWhatsAppController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWhatsAppService _waService;

    public CrmWhatsAppController(AppDbContext db, IWhatsAppService waService)
    {
        _db = db;
        _waService = waService;
    }

    [HttpPost("dispatch-template")]
    public async Task<IActionResult> DispatchTemplate([FromBody] DispatchTemplateRequest req, CancellationToken ct)
    {
        var lead = await _db.CrmLeads.FirstOrDefaultAsync(l => l.Id == req.LeadId, ct);
        if (lead == null) return NotFound("Lead not found");

        var success = await _waService.SendTemplateMessageAsync(lead.PhoneNumber, req.TemplateName, ct: ct);
        
        if (success)
        {
            var activity = new CrmLeadActivity
            {
                LeadId = lead.Id,
                ActivityType = "WHATSAPP_TEMPLATE",
                MessageBody = $"Sent template: {req.TemplateName}",
                TemplateName = req.TemplateName,
                Status = "SENT"
            };
            _db.CrmLeadActivities.Add(activity);
            await _db.SaveChangesAsync(ct);
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
