using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

[ApiController]
[Route("api/v1/users")]
public class UsersController : ControllerBase
{
    private readonly ICmsAdminService _admin;

    public UsersController(ICmsAdminService admin)
    {
        _admin = admin;
    }

    [HasPermission("user-management.view")]
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _admin.GetUsersAsync());

    [HasPermission("user-management.view")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var user = await _admin.GetUserAsync(id);
        return user == null ? NotFound() : Ok(user);
    }

    [HasPermission("user-management.manage")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        var (user, error) = await _admin.CreateUserAsync(req);
        if (user == null) return BadRequest(new { message = error });
        return CreatedAtAction(nameof(Get), new { id = user.UserId }, user);
    }

    [HasPermission("user-management.manage")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest req)
    {
        var (user, error) = await _admin.UpdateUserAsync(id, req);
        if (user == null) return error == "User not found." ? NotFound() : BadRequest(new { message = error });
        return Ok(user);
    }

    [HasPermission("user-management.manage")]
    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest req)
    {
        var ok = await _admin.ResetPasswordAsync(id, req.NewPassword);
        return ok ? NoContent() : BadRequest(new { message = "User not found or new password too weak (min 8 chars)." });
    }
}
