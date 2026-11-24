namespace CMSAPI.Domain.Entities;

/// <summary>A CMS staff account (lives in CMSDatabase, not the shared HMS DB).</summary>
public class CmsUser
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }   // E.164 format, e.g. +919876543210
    public string? JobTitle { get; set; }
    /// <summary>When the photo in CmsUserPhotos last changed; null when the person has no photo.</summary>
    public DateTime? PhotoUpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = true;
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<CmsUserRole> UserRoles { get; set; } = new List<CmsUserRole>();
    public ICollection<CmsUserPermission> UserPermissions { get; set; } = new List<CmsUserPermission>();
}
