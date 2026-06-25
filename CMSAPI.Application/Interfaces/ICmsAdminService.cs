using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface ICmsAdminService
{
    Task<List<PermissionDto>> GetPermissionsAsync();

    Task<List<RoleDto>> GetRolesAsync();
    Task<RoleDto?> CreateRoleAsync(CreateRoleRequest request);
    Task<RoleDto?> UpdateRoleAsync(Guid roleId, UpdateRoleRequest request);
    Task<bool> DeleteRoleAsync(Guid roleId);

    Task<List<UserSummaryDto>> GetUsersAsync();
    Task<UserDetailDto?> GetUserAsync(Guid userId);
    Task<(UserDetailDto? User, string? Error)> CreateUserAsync(CreateUserRequest request);
    Task<(UserDetailDto? User, string? Error)> UpdateUserAsync(Guid userId, UpdateUserRequest request);
    Task<bool> ResetPasswordAsync(Guid userId, string newPassword);
}
