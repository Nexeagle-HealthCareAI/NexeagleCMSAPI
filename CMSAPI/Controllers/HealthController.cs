using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading.Tasks;

namespace CMSAPI.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;

    public HealthController(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(HealthReport), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthReport), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get()
    {
        var report = await _healthCheckService.CheckHealthAsync();
        
        return report.Status == HealthStatus.Healthy 
            ? Ok(report) 
            : StatusCode(StatusCodes.Status503ServiceUnavailable, report);
    }
}
