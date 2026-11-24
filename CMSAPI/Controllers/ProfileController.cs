using System.Security.Claims;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// My profile: every signed-in person sees and changes their own name, phone number, job title and
// photo. No permission is needed (this replaces the old Settings page, which needed settings.view).
// Email, roles and access are changed by administrators in Users & access.
// Photos are in CMSDatabase (34_CMSDatabase_Profiles_And_Roles.sql); rules in CmsProfileService.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/profile")]
public class ProfileController : ControllerBase
{
    private readonly ICmsProfileService _profiles;

    public ProfileController(ICmsProfileService profiles)
    {
        _profiles = profiles;
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out userId);
    }

    internal static IActionResult Reply<T>(ControllerBase c, WorkResult<T> result) =>
        result.Ok ? c.Ok(result.Data) : c.StatusCode(result.Status, new { message = result.Message });

    /// <summary>Reads an uploaded photo (multipart field "file") into memory, at most 2 MB.</summary>
    internal static async Task<(byte[]? Data, IActionResult? Error)> ReadPhotoAsync(ControllerBase c, IFormFile? file)
    {
        if (file == null || file.Length == 0) return (null, c.BadRequest(new { message = "Choose a photo to upload." }));
        if (file.Length > ProfileRules.MaxPhotoBytes)
            return (null, c.StatusCode(413, new { message = "The photo is larger than 2 MB. Choose a smaller one." }));
        using var memory = new MemoryStream();
        await file.CopyToAsync(memory);
        return (memory.ToArray(), null);
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (!TryGetUserId(out var me)) return Unauthorized();
        return Reply(this, await _profiles.GetAsync(me));
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateProfileRequest request)
    {
        if (!TryGetUserId(out var me)) return Unauthorized();
        return Reply(this, await _profiles.UpdateAsync(me, request));
    }

    // Answers { photoVersion }.
    [HttpPut("photo")]
    [RequestSizeLimit(ProfileRules.MaxPhotoBytes + 64 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SetPhoto(IFormFile? file)
    {
        if (!TryGetUserId(out var me)) return Unauthorized();
        var (data, error) = await ReadPhotoAsync(this, file);
        if (error != null) return error;
        var result = await _profiles.SetPhotoAsync(me, data!);
        return result.Ok ? Ok(new { photoVersion = result.Data }) : StatusCode(result.Status, new { message = result.Message });
    }

    [HttpDelete("photo")]
    public async Task<IActionResult> RemovePhoto()
    {
        if (!TryGetUserId(out var me)) return Unauthorized();
        var result = await _profiles.RemovePhotoAsync(me);
        return result.Ok ? NoContent() : StatusCode(result.Status, new { message = result.Message });
    }
}
