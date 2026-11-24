using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CMSAPI.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users")]
public class UsersController : ControllerBase
{
    private readonly ICmsAdminService _admin;
    private readonly ICmsProfileService _profiles;

    public UsersController(ICmsAdminService admin, ICmsProfileService profiles)
    {
        _admin = admin;
        _profiles = profiles;
    }

    private Guid? Me =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;

    [HasPermission("user-management.view")]
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _admin.GetUsersAsync());

    // Names for the lead "assigned to" pickers in Marketing. Only id + name of active users, so
    // marketing staff don't need user-management.view (the full list carries emails and roles).
    [HasPermission("marketing.view")]
    [HttpGet("assignees")]
    public async Task<IActionResult> GetAssignees() =>
        Ok((await _admin.GetUsersAsync())
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new { u.UserId, u.FullName }));

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
        // Nobody can switch off their own account or take away their own right to manage people:
        // that would lock them (or the last administrator) out.
        if (Me == id && req.IsActive == false)
            return BadRequest(new { message = "You can't switch off your own account. Ask another administrator." });
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

    // Anyone signed in can see people's photos (they appear beside names across the CMS). The
    // version in the URL (?v=) changes with the photo, so the answer can be kept for a long time.
    [Authorize]
    [HttpGet("{id:guid}/photo")]
    public async Task<IActionResult> GetPhoto(Guid id)
    {
        var photo = await _profiles.GetPhotoAsync(id);
        if (photo == null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers.ETag = $"\"{photo.Version}\"";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(photo.Data, photo.ContentType);
    }

    [HasPermission("user-management.manage")]
    [HttpPut("{id:guid}/photo")]
    [RequestSizeLimit(CMSAPI.Application.Services.ProfileRules.MaxPhotoBytes + 64 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SetPhoto(Guid id, IFormFile? file)
    {
        var (data, error) = await ProfileController.ReadPhotoAsync(this, file);
        if (error != null) return error;
        var result = await _profiles.SetPhotoAsync(id, data!);
        return result.Ok ? Ok(new { photoVersion = result.Data }) : StatusCode(result.Status, new { message = result.Message });
    }

    [HasPermission("user-management.manage")]
    [HttpDelete("{id:guid}/photo")]
    public async Task<IActionResult> RemovePhoto(Guid id)
    {
        var result = await _profiles.RemovePhotoAsync(id);
        return result.Ok ? NoContent() : StatusCode(result.Status, new { message = result.Message });
    }
}
