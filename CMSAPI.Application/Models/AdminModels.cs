namespace CMSAPI.Application.Models;

public class PermissionDto
{
    public string Key { get; set; } = string.Empty;
    public string PageKey { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public int SortOrder { get; set; }
}

public class RoleDto
{
    public Guid RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemDefined { get; set; }
    public bool IsActive { get; set; }
    public List<string> PermissionKeys { get; set; } = new();
}

public class CreateRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> PermissionKeys { get; set; } = new();
}

public class UpdateRoleRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
    public List<string>? PermissionKeys { get; set; }
}

public class PermissionOverrideDto
{
    public string Key { get; set; } = string.Empty;
    public string Effect { get; set; } = "Allow"; // 'Allow' | 'Deny'
}

public class UserSummaryDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? JobTitle { get; set; }
    /// <summary>Changes whenever the photo changes (add it to the photo URL); null when there is no photo.</summary>
    public string? PhotoVersion { get; set; }
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public List<string> Roles { get; set; } = new();
}

public class UserDetailDto : UserSummaryDto
{
    public List<Guid> RoleIds { get; set; } = new();
    public List<PermissionOverrideDto> Overrides { get; set; } = new();
}

public class CreateUserRequest
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? JobTitle { get; set; }
    public List<Guid> RoleIds { get; set; } = new();
}

public class UpdateUserRequest
{
    public string? Email { get; set; }
    public string? FullName { get; set; }
    /// <summary>Empty string removes the number; null leaves it.</summary>
    public string? PhoneNumber { get; set; }
    /// <summary>Empty string removes the job title; null leaves it.</summary>
    public string? JobTitle { get; set; }
    public bool? IsActive { get; set; }
    public List<Guid>? RoleIds { get; set; }
    public List<PermissionOverrideDto>? Overrides { get; set; }
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>The signed-in person's own profile (My profile).</summary>
public class ProfileDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? JobTitle { get; set; }
    public string? PhotoVersion { get; set; }
    public List<string> Roles { get; set; } = new();
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>What a person may change on their own profile. Email, roles and access stay with administrators.</summary>
public class UpdateProfileRequest
{
    public string? FullName { get; set; }
    /// <summary>Empty string removes the number; null leaves it.</summary>
    public string? PhoneNumber { get; set; }
    /// <summary>Empty string removes the job title; null leaves it.</summary>
    public string? JobTitle { get; set; }
}

/// <summary>A stored profile photo.</summary>
public class UserPhoto
{
    public string ContentType { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string Version { get; set; } = string.Empty;
}
