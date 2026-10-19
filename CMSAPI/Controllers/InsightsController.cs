using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Interfaces;
using CMSAPI.Authorization;

using Microsoft.AspNetCore.Authorization;

namespace CMSAPI.Controllers;

// Backs the Doctor Dekho page's "Insights" tab (Site Visits / Patient Logins / Appointments) in
// the CMS. One permission covers all three sub-reports — they're read together as one feature.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/insights")]
public class InsightsController : ControllerBase
{
    private readonly IInsightsService _service;

    public InsightsController(IInsightsService service)
    {
        _service = service;
    }

    // from/to are inclusive "yyyy-MM-dd" India-time dates (omit both for the last 365 days, the
    // tracking retention period); site = doctordekho | nexeagle, omitted for both websites.
    [HasPermission("insights.view")]
    [HttpGet("site-visits")]
    public async Task<IActionResult> GetSiteVisitStats(
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] string? site = null)
    {
        var result = await _service.GetSiteVisitStatsAsync(from, to, site);
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
    // dateBy: "booked" (default; from/to are India-time booking days) | "appointment" (appointment date)
    [HasPermission("insights.view")]
    [HttpGet("appointments")]
    public async Task<IActionResult> GetOnlineAppointments(
        [FromQuery] int page = 1, [FromQuery] int limit = 10,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? search = null, [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null,
        [FromQuery] string? source = null, [FromQuery] string? dateBy = null)
    {
        var result = await _service.GetOnlineAppointmentsAsync(page, limit, from, to, search, sortBy, sortDir, source, dateBy);
        return Ok(result);
    }

    [HasPermission("insights.view")]
    [HttpGet("auth-funnel")]
    public async Task<IActionResult> GetAuthFunnelStats([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
    {
        var result = await _service.GetAuthFunnelStatsAsync(from, to);
        return Ok(result);
    }

    [HasPermission("insights.view")]
    [HttpGet("auth-funnel/attempts")]
    public async Task<IActionResult> GetAuthFunnelAttempts(
        [FromQuery] int page = 1, [FromQuery] int limit = 10,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] string? search = null)
    {
        var result = await _service.GetAuthFunnelAttemptsAsync(page, limit, from, to, search);
        return Ok(result);
    }

    [HasPermission("insights.view")]
    [HttpGet("booking-funnel")]
    public async Task<IActionResult> GetBookingFunnelStats([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
    {
        var result = await _service.GetBookingFunnelStatsAsync(from, to);
        return Ok(result);
    }

    [HasPermission("insights.view")]
    [HttpGet("searches")]
    public async Task<IActionResult> GetSearchLog(
        [FromQuery] int page = 1, [FromQuery] int limit = 10,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? search = null, [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null)
    {
        var result = await _service.GetSearchLogAsync(page, limit, from, to, search, sortBy, sortDir);
        return Ok(result);
    }
}
