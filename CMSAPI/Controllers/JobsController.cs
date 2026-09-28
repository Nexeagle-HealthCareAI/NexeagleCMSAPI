using System.Security.Claims;
using System.Threading.Tasks;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// Admin control surface for the easyHMSNightJob run-once console app's dbo.JobSettings /
// dbo.NightJobRuns rows -- lets an admin see/toggle a job's IsActive flag and see recent run
// history without SSH/DB access. Same physical easyHMSDatabase catalog as easyHMSAPI/NightJob
// (see FreeTierSettingsController's doc comment), so no HTTP call to another API is needed.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/night-jobs")]
public class JobsController : ControllerBase
{
    private readonly IJobsService _service;

    public JobsController(IJobsService service)
    {
        _service = service;
    }

    private string? CurrentUserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    [HasPermission("dashboard.view")]
    [HttpGet]
    public async Task<IActionResult> GetJobs()
    {
        var result = await _service.GetJobsAsync();
        return Ok(result);
    }

    [HasPermission("hospitals.manage")]
    [HttpPut("{jobName}/active")]
    public async Task<IActionResult> SetJobActive([FromRoute] string jobName, [FromBody] UpdateJobActiveRequest request)
    {
        var result = await _service.SetJobActiveAsync(jobName, request.IsActive, CurrentUserId);
        if (!result.Success) return BadRequest(new { message = result.Message });
        return Ok(result);
    }

    [HasPermission("dashboard.view")]
    [HttpGet("runs")]
    public async Task<IActionResult> GetRecentRuns([FromQuery] int take = 20)
    {
        var bounded = take <= 0 || take > 100 ? 20 : take;
        var result = await _service.GetRecentRunsAsync(bounded);
        return Ok(result);
    }

    // Executes the job's real logic immediately and reports what it did -- lets an admin verify
    // a job actually works without waiting for (or SSHing in to check) the 01:00 IST cron run.
    // Only jobs on JobsRepository's TestableJobs allow-list can be run this way.
    [HasPermission("hospitals.manage")]
    [HttpPost("{jobName}/run-now")]
    public async Task<IActionResult> RunNow([FromRoute] string jobName)
    {
        var result = await _service.RunJobNowAsync(jobName, CurrentUserId);
        if (!result.Success) return BadRequest(new { message = result.Message });
        return Ok(result);
    }
}
