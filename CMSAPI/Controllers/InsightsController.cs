using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Interfaces;
using CMSAPI.Authorization;

using Microsoft.AspNetCore.Authorization;

namespace CMSAPI.Controllers;

// Backs the Doctor Dekho page's "Insights" tab (Site Visits / Patient Logins / Appointments) in
// the CMS. One permission covers all three sub-reports — they're read together as one feature.
[Authorize]
[ApiController]
[Route("api/v1/insights")]
public class InsightsController : ControllerBase
{
    private readonly IInsightsService _service;

    public InsightsController(IInsightsService service)
    {
        _service = service;
    }

    // from/to are inclusive "yyyy-MM-dd" dates; omit both for all-time, or pass the same date for
    // both for "today".
    [HasPermission("insights.view")]
    [HttpGet("site-visits")]
    public async Task<IActionResult> GetSiteVisitStats([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
    {
        var result = await _service.GetSiteVisitStatsAsync(from, to);
        return Ok(result);
    }

    [HasPermission("insights.view")]
    [HttpGet("patient-logins")]
    public async Task<IActionResult> GetPatientLogins(
        [FromQuery] int page = 1, [FromQuery] int limit = 10, [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null)
    {
        var result = await _service.GetPatientLoginsAsync(page, limit, search, sortBy, sortDir);
        return Ok(result);
    }

    // source: "All" (default) | "Guest" | "LoggedIn"
    [HasPermission("insights.view")]
    [HttpGet("appointments")]
    public async Task<IActionResult> GetOnlineAppointments(
        [FromQuery] int page = 1, [FromQuery] int limit = 10,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? search = null, [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null,
        [FromQuery] string? source = null)
    {
        var result = await _service.GetOnlineAppointmentsAsync(page, limit, from, to, search, sortBy, sortDir, source);
        return Ok(result);
    }
}
