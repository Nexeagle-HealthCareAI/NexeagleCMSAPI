using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

[ApiController]
[Route("api/v1/roles")]
public class RolesController : ControllerBase
{
    private readonly ICmsAdminService _admin;

    public RolesController(ICmsAdminService admin)
    {
        _admin = admin;
    }

    [HasPermission("user-management.view")]
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _admin.GetRolesAsync());

    [HasPermission("user-management.manage")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest req)
    {
        var role = await _admin.CreateRoleAsync(req);
        if (role == null) return BadRequest(new { message = "Role name is required." });
        return CreatedAtAction(nameof(GetAll), new { id = role.RoleId }, role);
    }

    [HasPermission("user-management.manage")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoleRequest req)
    {
        var role = await _admin.UpdateRoleAsync(id, req);
        return role == null ? NotFound() : Ok(role);
    }

    [HasPermission("user-management.manage")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ok = await _admin.DeleteRoleAsync(id);
        return ok ? NoContent() : BadRequest(new { message = "Role not found or is system-defined and cannot be deleted." });
    }
}
