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
    public List<Guid> RoleIds { get; set; } = new();
}

public class UpdateUserRequest
{
    public string? FullName { get; set; }
    public bool? IsActive { get; set; }
    public List<Guid>? RoleIds { get; set; }
    public List<PermissionOverrideDto>? Overrides { get; set; }
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}
