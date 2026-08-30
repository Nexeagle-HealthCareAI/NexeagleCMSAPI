using System;
using System.Security.Claims;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/data-migration")]
public class DataMigrationController : ControllerBase
{
    private readonly IDataMigrationService _service;

    public DataMigrationController(IDataMigrationService service)
    {
        _service = service;
    }

    [HasPermission("data-migration.manage")]
    [HttpPost("batches")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadBatch([FromForm] Guid hospitalId, [FromForm] string dataType, [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "A CSV file is required." });

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var actorId)) return Unauthorized();

        await using var stream = file.OpenReadStream();
        var result = await _service.UploadBatchAsync(hospitalId, dataType, file.FileName, stream, actorId);
        if (result == null) return StatusCode(502, new { message = "Could not process the file -- the migration service may be unavailable." });
        return Ok(result);
    }

    [HasPermission("data-migration.manage")]
    [HttpGet("batches")]
    public async Task<IActionResult> GetBatches([FromQuery] Guid? hospitalId, [FromQuery] int page = 1, [FromQuery] int limit = 20)
    {
        var result = await _service.GetBatchesAsync(hospitalId, page, limit);
        return Ok(result);
    }

    [HasPermission("data-migration.manage")]
    [HttpGet("batches/{id:guid}")]
    public async Task<IActionResult> GetBatch([FromRoute] Guid id)
    {
        var result = await _service.GetBatchAsync(id);
        if (result == null) return NotFound(new { message = "Batch not found" });
        return Ok(result);
    }

    [HasPermission("data-migration.manage")]
    [HttpPut("batches/{id:guid}/column-mapping")]
    public async Task<IActionResult> UpdateColumnMapping([FromRoute] Guid id, [FromBody] UpdateColumnMappingRequest request)
    {
        var success = await _service.UpdateColumnMappingAsync(id, request);
        if (!success) return NotFound(new { message = "Batch not found" });
        return Ok(new { success = true });
    }

    [HasPermission("data-migration.manage")]
    [HttpPost("batches/{id:guid}/transform")]
    public async Task<IActionResult> Transform([FromRoute] Guid id)
    {
        var result = await _service.TransformAsync(id);
        if (result == null) return NotFound(new { message = "Batch not found" });
        return Ok(result);
    }

    [HasPermission("data-migration.manage")]
    [HttpGet("batches/{id:guid}/rows")]
    public async Task<IActionResult> GetRows([FromRoute] Guid id, [FromQuery] int page = 1, [FromQuery] int limit = 50, [FromQuery] string? status = null)
    {
        var result = await _service.GetRowsAsync(id, page, limit, status);
        return Ok(result);
    }

    [HasPermission("data-migration.manage")]
    [HttpGet("batches/{id:guid}/doctor-map")]
    public async Task<IActionResult> GetDoctorMap([FromRoute] Guid id)
    {
        var result = await _service.GetDoctorMapAsync(id);
        return Ok(result);
    }

    [HasPermission("data-migration.manage")]
    [HttpPut("batches/{id:guid}/doctor-map")]
    public async Task<IActionResult> UpdateDoctorMap([FromRoute] Guid id, [FromBody] UpdateDoctorMapRequest request)
    {
        var success = await _service.UpdateDoctorMapAsync(id, request);
        if (!success) return NotFound(new { message = "Batch not found" });
        return Ok(new { success = true });
    }
}
