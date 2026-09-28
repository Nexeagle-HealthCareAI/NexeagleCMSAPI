using System.Threading.Tasks;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// Admin listing of every diagnostic center registered on 1Rad + their active staff roster.
// 1RadAPI is a separate product with its own database, so this proxies a service-key-gated call
// to it (see RadHospitalsService) rather than reading a shared catalog directly.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/rad-hospitals")]
public class RadHospitalsController : ControllerBase
{
    private readonly IRadHospitalsService _service;

    public RadHospitalsController(IRadHospitalsService service)
    {
        _service = service;
    }

    [HasPermission("dashboard.view")]
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _service.GetAllAsync();
        return Ok(result);
    }
}
