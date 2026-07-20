using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;

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
        var result = await _service.UpdateDoctorMarketingAsync(doctorId, request);
        if (!result.Success) return BadRequest(new { message = result.Message });
        return Ok(result);
    }
}
