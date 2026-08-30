using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace CMSAPI.Controllers;

// Backs the CMS "Marketing" tab.
// ── Section 1: Demo Logins  — read-only mirror of HospitalLeads (auto-captured QR funnel).
// ── Section 2: Sales Leads  — manual B2B prospecting pipeline stored in CmsSalesLeads.
[Authorize]
[ApiController]
[Route("api/v1/marketing")]
public class MarketingController : ControllerBase
{
    private readonly IMarketingService _marketing;
    private readonly ISalesLeadService _leads;

    public MarketingController(IMarketingService marketing, ISalesLeadService leads)
    {
        _marketing = marketing;
        _leads = leads;
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
    public async Task<IActionResult> CreateLead([FromBody] CreateSalesLeadRequest req)
    {
        var (userId, userName) = GetCurrentUser();
        if (userId == Guid.Empty) return Unauthorized();

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

    // ── Helpers ───────────────────────────────────────────────────────────

    private (Guid UserId, string UserName) GetCurrentUser()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var name  = User.FindFirstValue("name") ?? "Unknown";
        return Guid.TryParse(idStr, out var id) ? (id, name) : (Guid.Empty, name);
    }
}
