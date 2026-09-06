using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// Admin control surface for the usage-based free tier that replaced the old time-limited trial --
// a global default monthly "patient management action" quota (IPD admission, OPD appointment
// confirm/walk-in, pathology order, pharmacy checkout), plus optional per-hospital overrides.
// easyHMSAPI's UsageLimitService reads the same dbo.PlatformSetting/dbo.HospitalFreeTierLimit
// rows this writes -- both AppDbContexts point at the same physical easyHMSDatabase catalog, so
// no HTTP call between the two APIs is needed.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/free-tier-settings")]
public class FreeTierSettingsController : ControllerBase
{
    private readonly IFreeTierSettingsService _service;

    public FreeTierSettingsController(IFreeTierSettingsService service)
    {
        _service = service;
    }

    private string? CurrentUserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    [HasPermission("dashboard.view")]
    [HttpGet("global")]
    public async Task<IActionResult> GetGlobal()
    {
        var result = await _service.GetGlobalAsync();
        return Ok(result);
    }

    [HasPermission("hospitals.manage")]
    [HttpPut("global")]
    public async Task<IActionResult> SetGlobal([FromBody] UpdateGlobalFreeTierLimitRequest request)
    {
        var result = await _service.SetGlobalAsync(request.MonthlyLimit, CurrentUserId);
        if (!result.Success) return BadRequest(new { message = result.Message });
        return Ok(result);
    }

    [HasPermission("dashboard.view")]
    [HttpGet("hospitals")]
    public async Task<IActionResult> GetHospitalOverrides()
    {
        var result = await _service.GetHospitalOverridesAsync();
        return Ok(result);
    }

    [HasPermission("hospitals.manage")]
    [HttpPut("hospitals/{hospitalId:guid}")]
    public async Task<IActionResult> SetHospitalOverride([FromRoute] Guid hospitalId, [FromBody] UpdateHospitalFreeTierLimitRequest request)
    {
        var result = await _service.SetHospitalOverrideAsync(hospitalId, request.MonthlyLimit, CurrentUserId);
        if (!result.Success) return BadRequest(new { message = result.Message });
        return Ok(result);
    }
}
