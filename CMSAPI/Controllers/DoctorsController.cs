using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

namespace CMSAPI.Controllers;

// Platform-wide Doctor Dekho marketing controls (featured / delisted / consultation-fee
// discount) — writes straight into easyHMSDatabase via AppDbContext, same pattern as
// SubscriptionApprovalController. Deliberately [Authorize]-only, not [HasPermission(...)], to
// match that same existing write-feature precedent rather than inventing a new permission string
// that would need its own CMSDatabase seed.
[Authorize]
[ApiController]
[Route("api/v1/doctors")]
public class DoctorsController : ControllerBase
{
    private readonly IDoctorService _service;

    public DoctorsController(IDoctorService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetDoctors([FromQuery] int page = 1, [FromQuery] int limit = 10, [FromQuery] string? search = null, [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null)
    {
        var result = await _service.GetDoctorsAsync(page, limit, search, sortBy, sortDir);
        return Ok(result);
    }

    // Full "A to Z" profile for the detail view — every hospital affiliation, license/council/
    // registration, qualifications, specializations, languages, fees, and marketing status.
    [HttpGet("{doctorId:guid}")]
    public async Task<IActionResult> GetDoctorDetail([FromRoute] Guid doctorId)
    {
        var result = await _service.GetDoctorDetailAsync(doctorId);
        if (result == null) return NotFound(new { message = "Doctor not found." });
        return Ok(result);
    }

    [HttpPut("{doctorId:guid}/marketing")]
    public async Task<IActionResult> UpdateDoctorMarketing([FromRoute] Guid doctorId, [FromBody] UpdateDoctorMarketingRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Guid? actingUserId = Guid.TryParse(userIdClaim, out var uid) ? uid : null;

        var result = await _service.UpdateDoctorMarketingAsync(doctorId, request, actingUserId);
        if (!result.Success) return BadRequest(new { message = result.Message });
        return Ok(result);
    }

    // Campaign-style bulk action — applies the same featured/delisted/discount change to many
    // doctors in one save. Route is unambiguous against {doctorId:guid}/marketing above since
    // "bulk" never matches the guid constraint.
    [HttpPut("bulk/marketing")]
    public async Task<IActionResult> BulkUpdateDoctorMarketing([FromBody] BulkUpdateDoctorMarketingRequest request)
    {
        var result = await _service.BulkUpdateDoctorMarketingAsync(request);
        if (!result.Success) return BadRequest(new { message = result.Message });
        return Ok(result);
    }
}
