using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;

using Microsoft.AspNetCore.Authorization;

namespace CMSAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/hospitals")]
public class HospitalsController : ControllerBase
{
    private readonly IHospitalService _service;

    public HospitalsController(IHospitalService service)
    {
        _service = service;
    }

    [HasPermission("onboarded-hospitals.view")]
    [HttpGet]
    public async Task<IActionResult> GetHospitals(
        [FromQuery] int page = 1, [FromQuery] int limit = 10, [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null,
        [FromQuery] string? status = null, [FromQuery] string? subscriptionStatus = null,
        [FromQuery] bool includeArchived = false)
    {
        var result = await _service.GetHospitalsAsync(page, limit, search, sortBy, sortDir, status, subscriptionStatus, includeArchived);
        return Ok(result);
    }

    [HasPermission("hospital-details.view")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetHospitalById([FromRoute] Guid id)
    {
        var details = await _service.GetHospitalByIdAsync(id);
        if (details == null) return NotFound(new { message = "Hospital not found" });
        return Ok(details);
    }

    // Online vs hospital-booked appointment counts for a date range. from/to are inclusive
    // "yyyy-MM-dd" dates; omit both for all-time, or pass the same date for both for "today".
    [HasPermission("hospital-details.view")]
    [HttpGet("{id:guid}/appointment-stats")]
    public async Task<IActionResult> GetAppointmentSourceStats([FromRoute] Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
    {
        var stats = await _service.GetAppointmentSourceStatsAsync(id, from, to);
        return Ok(stats);
    }

    // Soft-delete only. Hiding the hospital everywhere is enforced server-side in easyHMSAPI
    // (HospitalAccessFilter, public directory queries, nightly jobs) — this just flips the flag.
    [HasPermission("hospitals.manage")]
    [HttpPatch("{id:guid}/archive")]
    public async Task<IActionResult> ArchiveHospital([FromRoute] Guid id)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var actorId))
            return Unauthorized();

        var success = await _service.ArchiveHospitalAsync(id, actorId);
        if (!success) return NotFound(new { message = "Hospital not found" });
        return Ok(new { success = true });
    }

    [HasPermission("hospitals.manage")]
    [HttpPatch("{id:guid}/restore")]
    public async Task<IActionResult> RestoreHospital([FromRoute] Guid id)
    {
        var success = await _service.RestoreHospitalAsync(id);
        if (!success) return NotFound(new { message = "Hospital not found" });
        return Ok(new { success = true });
    }
}
