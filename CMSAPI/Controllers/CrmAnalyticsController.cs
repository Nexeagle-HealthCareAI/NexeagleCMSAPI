using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CMSAPI.Authorization;
using CMSAPI.Data;
using Microsoft.EntityFrameworkCore;
using CMSAPI.Application.Models;

namespace CMSAPI.Controllers;

[Authorize]
[HasPermission("marketing.view")]
[ApiController]
[Route("api/v1/crm/analytics")]
public class CrmAnalyticsController : ControllerBase
{
    private readonly CmsDbContext _db;

    public CrmAnalyticsController(CmsDbContext db)
    {
        _db = db;
    }

    [HttpGet("financial")]
    public async Task<ActionResult<FinancialAttributionDto>> GetFinancialAttribution(CancellationToken ct)
    {
        // Total Ad Spend from Campaigns
        var totalSpend = await _db.CmsCampaigns
            .Where(c => c.IsActive)
            .SumAsync(c => c.ActualSpend, ct);

        // Leads metrics
        var totalLeads = await _db.CmsSalesLeads.CountAsync(ct);
        
        var totalQualifiedLeads = await _db.CmsSalesLeads
            .Where(l => l.AiIntentScore >= 50)
            .CountAsync(ct);

        var totalCustomers = await _db.CmsSalesLeads
            .Where(l => l.Stage == "Closed Won")
            .CountAsync(ct);

        var totalRevenue = await _db.CmsSalesLeads
            .Where(l => l.Stage == "Closed Won")
            .SumAsync(l => l.DealValue, ct);

        var dto = new FinancialAttributionDto
        {
            TotalSpend = totalSpend,
            TotalRevenue = totalRevenue,
            TotalLeads = totalLeads,
            TotalQualifiedLeads = totalQualifiedLeads,
            TotalCustomers = totalCustomers
        };

        return Ok(dto);
    }
}
