using CMSAPI.Domain.Entities;

namespace CMSAPI.Application.Interfaces;

/// <summary>Data access for CMS user/role/permission administration (CMSDatabase).</summary>
public interface ICmsAdminRepository
{
    Task<List<CmsPermission>> GetPermissionsAsync();

    Task<List<CmsRole>> GetRolesWithPermissionsAsync();
    Task<CmsRole?> GetRoleAsync(Guid roleId);
    Task AddRoleAsync(CmsRole role);
    Task SetRolePermissionsAsync(Guid roleId, IEnumerable<Guid> permissionIds);
    Task RemoveRoleAsync(CmsRole role);

    Task<List<CmsUser>> GetUsersWithRolesAsync();
    Task<CmsUser?> GetUserWithRolesAndOverridesAsync(Guid userId);
    Task<bool> EmailExistsAsync(string email);
    Task AddUserAsync(CmsUser user, IEnumerable<Guid> roleIds);
    Task SetUserRolesAsync(Guid userId, IEnumerable<Guid> roleIds);
    Task SetUserOverridesAsync(Guid userId, IEnumerable<(Guid PermissionId, string Effect)> overrides);

    Task SaveChangesAsync();
}
