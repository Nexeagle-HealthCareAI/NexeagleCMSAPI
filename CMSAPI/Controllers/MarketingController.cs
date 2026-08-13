using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Interfaces;
using CMSAPI.Authorization;

using Microsoft.AspNetCore.Authorization;

namespace CMSAPI.Controllers;

// Backs the CMS "Marketing" tab -- currently just the "scan a QR, land in a live 1HMS demo"
// funnel (info@nexeagle.com on NexEagle General Clinic, 1hms-dev.nexeagle.com). Reads
// HospitalLeads directly (same table easyHMSAPI's own Lead Generation page reads, see
// HospitalLead.cs) -- CMS has its own separate auth/user system from easyHMSAPI's staff JWTs,
// so it can't call easyHMSAPI's staff-authenticated /leads endpoint directly; this mirrors the
// same "CMSAPI reads a shared table it doesn't own" pattern InsightsRepository already uses.
[Authorize]
[ApiController]
[Route("api/v1/marketing")]
public class MarketingController : ControllerBase
{
    private readonly IMarketingService _service;

    public MarketingController(IMarketingService service)
    {
        _service = service;
    }

    [HasPermission("marketing.view")]
    [HttpGet("demo-logins")]
    public async Task<IActionResult> GetDemoLoginLeads([FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var result = await _service.GetDemoLoginLeadsAsync(page, limit);
        return Ok(result);
    }
}
