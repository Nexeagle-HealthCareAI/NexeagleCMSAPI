using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Interfaces;

using Microsoft.AspNetCore.Authorization;

namespace CMSAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _service;

    public DashboardController(IDashboardService service)
    {
        _service = service;
    }

    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CMSAPI.Application.Models.ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CMSAPI.Application.Models.ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetStats()
    {
        try
        {
            var stats = await _service.GetDashboardAsync();
            return Ok(stats);
        }
        catch(Exception ex)
        {
            throw;
        }
    }
}
