using CMSAPI.Application.Interfaces;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

[ApiController]
[Route("api/v1/permissions")]
public class PermissionsController : ControllerBase
{
    private readonly ICmsAdminService _admin;

    public PermissionsController(ICmsAdminService admin)
    {
        _admin = admin;
    }

    /// <summary>The full page+action permission catalog (for building the access UI).</summary>
    [HasPermission("user-management.view")]
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _admin.GetPermissionsAsync());
}
