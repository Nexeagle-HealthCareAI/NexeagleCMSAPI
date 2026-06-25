using CMSAPI.Application.Interfaces;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories;

public class CmsAdminRepository : ICmsAdminRepository
{
    private readonly CmsDbContext _db;

    public CmsAdminRepository(CmsDbContext db)
    {
        _db = db;
    }

    public Task<List<CmsPermission>> GetPermissionsAsync() =>
        _db.CmsPermissions.OrderBy(p => p.SortOrder).ThenBy(p => p.Key).ToListAsync();

    public Task<List<CmsRole>> GetRolesWithPermissionsAsync() =>
        _db.CmsRoles.Include(r => r.RolePermissions).OrderBy(r => r.Name).ToListAsync();

    public Task<CmsRole?> GetRoleAsync(Guid roleId) =>
        _db.CmsRoles.Include(r => r.RolePermissions).FirstOrDefaultAsync(r => r.RoleId == roleId);

    public Task AddRoleAsync(CmsRole role)
    {
        _db.CmsRoles.Add(role);
        return Task.CompletedTask;
    }

    public async Task SetRolePermissionsAsync(Guid roleId, IEnumerable<Guid> permissionIds)
    {
        var existing = await _db.CmsRolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync();
        _db.CmsRolePermissions.RemoveRange(existing);
        foreach (var pid in permissionIds.Distinct())
            _db.CmsRolePermissions.Add(new CmsRolePermission { RoleId = roleId, PermissionId = pid });
    }

    public Task RemoveRoleAsync(CmsRole role)
    {
        _db.CmsRoles.Remove(role);
        return Task.CompletedTask;
    }

    public Task<List<CmsUser>> GetUsersWithRolesAsync() =>
        _db.CmsUsers.Include(u => u.UserRoles).OrderBy(u => u.FullName).ToListAsync();

    public Task<CmsUser?> GetUserWithRolesAndOverridesAsync(Guid userId) =>
        _db.CmsUsers
            .Include(u => u.UserRoles)
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.UserId == userId);

    public Task<bool> EmailExistsAsync(string email) =>
        _db.CmsUsers.AnyAsync(u => u.Email == email);

    public Task AddUserAsync(CmsUser user, IEnumerable<Guid> roleIds)
    {
        _db.CmsUsers.Add(user);
        foreach (var rid in roleIds.Distinct())
            _db.CmsUserRoles.Add(new CmsUserRole { UserId = user.UserId, RoleId = rid });
        return Task.CompletedTask;
    }

    public async Task SetUserRolesAsync(Guid userId, IEnumerable<Guid> roleIds)
    {
        var existing = await _db.CmsUserRoles.Where(ur => ur.UserId == userId).ToListAsync();
        _db.CmsUserRoles.RemoveRange(existing);
        foreach (var rid in roleIds.Distinct())
            _db.CmsUserRoles.Add(new CmsUserRole { UserId = userId, RoleId = rid });
    }

    public async Task SetUserOverridesAsync(Guid userId, IEnumerable<(Guid PermissionId, string Effect)> overrides)
    {
        var existing = await _db.CmsUserPermissions.Where(up => up.UserId == userId).ToListAsync();
        _db.CmsUserPermissions.RemoveRange(existing);
        foreach (var o in overrides)
            _db.CmsUserPermissions.Add(new CmsUserPermission { UserId = userId, PermissionId = o.PermissionId, Effect = o.Effect });
    }

    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}
