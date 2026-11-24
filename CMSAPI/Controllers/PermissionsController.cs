using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/permissions")]
public class PermissionsController : ControllerBase
{
    private readonly ICmsAdminService _admin;

    public PermissionsController(ICmsAdminService admin)
    {
        _admin = admin;
    }

    /// <summary>
    /// The full page+action permission catalog (for building the access UI). Any signed-in person may
    /// read it: it only names the CMS's areas, and My profile uses it to show what the person can open.
    /// </summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _admin.GetPermissionsAsync());
}
