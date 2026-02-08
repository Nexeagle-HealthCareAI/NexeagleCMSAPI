using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;

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

    [HttpGet]
    public async Task<IActionResult> GetHospitals([FromQuery] int page = 1, [FromQuery] int limit = 10, [FromQuery] string? search = null, [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null)
    {
        var result = await _service.GetHospitalsAsync(page, limit, search, sortBy, sortDir);
        return Ok(new { data = result.Data, pagination = new { currentPage = result.CurrentPage, totalPages = result.TotalPages, totalItems = result.TotalItems, itemsPerPage = result.ItemsPerPage } });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetHospitalById([FromRoute] Guid id)
    {
        var details = await _service.GetHospitalByIdAsync(id);
        if (details == null) return NotFound(new { message = "Hospital not found" });
        return Ok(details);
    }
}
