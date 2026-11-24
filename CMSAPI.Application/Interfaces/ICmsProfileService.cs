using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

/// <summary>
/// People's own profiles and everyone's photos. Every signed-in person can see and change their own
/// name, phone number, job title and photo; administrators change other people's through Users &amp; access.
/// </summary>
public interface ICmsProfileService
{
    Task<WorkResult<ProfileDto>> GetAsync(Guid userId);
    Task<WorkResult<ProfileDto>> UpdateAsync(Guid userId, UpdateProfileRequest request);

    /// <summary>Stores a JPEG, PNG or WebP photo of at most 2 MB for the person, replacing any earlier one.</summary>
    Task<WorkResult<string>> SetPhotoAsync(Guid userId, byte[] data);
    Task<WorkResult<bool>> RemovePhotoAsync(Guid userId);
    /// <summary>The person's photo, or null when they have none (or the person does not exist).</summary>
    Task<UserPhoto?> GetPhotoAsync(Guid userId);
}
