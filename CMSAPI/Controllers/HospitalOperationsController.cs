using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Interfaces;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace CMSAPI.Controllers;

// Per-hospital IPD admissions / pathology orders / pharmacy sales / online appointment counts
// for a single day or custom date range -- a cross-hospital operations view, distinct from
// DashboardController's platform-wide totals and HospitalsController's per-hospital detail page.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/hospital-operations")]
public class HospitalOperationsController : ControllerBase
{
    private readonly IHospitalOperationsService _service;

    public HospitalOperationsController(IHospitalOperationsService service)
    {
        _service = service;
    }

    [HasPermission("dashboard.view")]
    [HttpGet("summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CMSAPI.Application.Models.ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSummary([FromQuery] DateTime fromDate, [FromQuery] DateTime? toDate)
    {
        var result = await _service.GetSummaryAsync(fromDate, toDate ?? fromDate);
        if (!result.Success)
            return BadRequest(new { message = result.Message });
        return Ok(result);
    }
}
