using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.Services;

/// <summary>See ICmsProfileService. Uses the same rules as Users &amp; access (ProfileRules).</summary>
public class CmsProfileService : ICmsProfileService
{
    private readonly CmsDbContext _db;
    private readonly ILogger<CmsProfileService> _logger;

    public CmsProfileService(CmsDbContext db, ILogger<CmsProfileService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private static WorkResult<T> NotFound<T>() => WorkResult<T>.Fail(404, "Person not found.");

    public async Task<WorkResult<ProfileDto>> GetAsync(Guid userId)
    {
        var user = await _db.CmsUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) return NotFound<ProfileDto>();

        var roles = await (
            from ur in _db.CmsUserRoles
            join r in _db.CmsRoles on ur.RoleId equals r.RoleId
            where ur.UserId == userId && r.IsActive
            orderby r.Name
            select r.Name
        ).ToListAsync();

        return WorkResult<ProfileDto>.Success(new ProfileDto
        {
            UserId = user.UserId,
            Email = user.Email,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            JobTitle = user.JobTitle,
            PhotoVersion = ProfileRules.PhotoVersion(user.PhotoUpdatedAt),
            Roles = roles,
            LastLoginAt = user.LastLoginAt,
        });
    }

    public async Task<WorkResult<ProfileDto>> UpdateAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await _db.CmsUsers.FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) return NotFound<ProfileDto>();

        if (request.FullName != null && ProfileRules.CheckName(request.FullName) is { } nameError)
            return WorkResult<ProfileDto>.Fail(400, nameError);
        if (ProfileRules.CheckJobTitle(request.JobTitle) is { } titleError)
            return WorkResult<ProfileDto>.Fail(400, titleError);

        if (request.PhoneNumber != null)
        {
            var (phone, phoneError) = ProfileRules.CleanPhone(request.PhoneNumber);
            if (phoneError != null) return WorkResult<ProfileDto>.Fail(400, phoneError);
            if (phone != null && phone != user.PhoneNumber
                && await _db.CmsUsers.AnyAsync(u => u.PhoneNumber == phone && u.UserId != userId))
                return WorkResult<ProfileDto>.Fail(409, "Someone else already has that phone number.");
            user.PhoneNumber = phone;
        }
        if (request.FullName != null) user.FullName = request.FullName.Trim();
        if (request.JobTitle != null) user.JobTitle = string.IsNullOrWhiteSpace(request.JobTitle) ? null : request.JobTitle.Trim();
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await GetAsync(userId);
    }

    public async Task<WorkResult<string>> SetPhotoAsync(Guid userId, byte[] data)
    {
        if (data.Length == 0) return WorkResult<string>.Fail(400, "Choose a photo to upload.");
        if (data.Length > ProfileRules.MaxPhotoBytes) return WorkResult<string>.Fail(413, "The photo is larger than 2 MB. Choose a smaller one.");
        var type = ProfileRules.SniffImageType(data);
        if (type == null) return WorkResult<string>.Fail(415, "The photo must be a JPG, PNG or WebP image.");

        var user = await _db.CmsUsers.FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) return NotFound<string>();

        var now = DateTime.UtcNow;
        var photo = await _db.CmsUserPhotos.FirstOrDefaultAsync(p => p.UserId == userId);
        if (photo == null)
        {
            photo = new CmsUserPhoto { UserId = userId };
            _db.CmsUserPhotos.Add(photo);
        }
        photo.ContentType = type;
        photo.Data = data;
        photo.UpdatedAt = now;
        user.PhotoUpdatedAt = now;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Profile photo changed for UserId={UserId} ({Bytes} bytes).", userId, data.Length);
        return WorkResult<string>.Success(ProfileRules.PhotoVersion(now)!);
    }

    public async Task<WorkResult<bool>> RemovePhotoAsync(Guid userId)
    {
        var user = await _db.CmsUsers.FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) return NotFound<bool>();
        var photo = await _db.CmsUserPhotos.FirstOrDefaultAsync(p => p.UserId == userId);
        if (photo != null) _db.CmsUserPhotos.Remove(photo);
        user.PhotoUpdatedAt = null;
        await _db.SaveChangesAsync();
        return WorkResult<bool>.Success(true);
    }

    public async Task<UserPhoto?> GetPhotoAsync(Guid userId)
    {
        var photo = await _db.CmsUserPhotos.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
        return photo == null ? null : new UserPhoto
        {
            ContentType = photo.ContentType,
            Data = photo.Data,
            Version = ProfileRules.PhotoVersion(photo.UpdatedAt)!,
        };
    }
}
