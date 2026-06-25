using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Application.Services;

public class CmsAdminService : ICmsAdminService
{
    private const int MinPasswordLength = 8;

    private readonly ICmsAdminRepository _repo;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<CmsAdminService> _logger;

    public CmsAdminService(ICmsAdminRepository repo, IPasswordHasher hasher, ILogger<CmsAdminService> logger)
    {
        _repo = repo;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task<List<PermissionDto>> GetPermissionsAsync()
    {
        var perms = await _repo.GetPermissionsAsync();
        return perms.Select(p => new PermissionDto
        {
            Key = p.Key,
            PageKey = p.PageKey,
            Action = p.Action,
            DisplayName = p.DisplayName,
            Category = p.Category,
            SortOrder = p.SortOrder
        }).ToList();
    }

    public async Task<List<RoleDto>> GetRolesAsync()
    {
        var idToKey = (await _repo.GetPermissionsAsync()).ToDictionary(p => p.PermissionId, p => p.Key);
        var roles = await _repo.GetRolesWithPermissionsAsync();
        return roles.Select(r => MapRole(r, idToKey)).ToList();
    }

    public async Task<RoleDto?> CreateRoleAsync(CreateRoleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return null;

        var keyToId = (await _repo.GetPermissionsAsync()).ToDictionary(p => p.Key, p => p.PermissionId);
        var role = new CmsRole
        {
            RoleId = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description,
            IsSystemDefined = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        await _repo.AddRoleAsync(role);
        await _repo.SetRolePermissionsAsync(role.RoleId, ResolveIds(request.PermissionKeys, keyToId));
        await _repo.SaveChangesAsync();

        return await SingleRoleAsync(role.RoleId);
    }

    public async Task<RoleDto?> UpdateRoleAsync(Guid roleId, UpdateRoleRequest request)
    {
        var role = await _repo.GetRoleAsync(roleId);
        if (role == null) return null;

        if (!string.IsNullOrWhiteSpace(request.Name) && !role.IsSystemDefined) role.Name = request.Name.Trim();
        if (request.Description != null) role.Description = request.Description;
        if (request.IsActive.HasValue && !role.IsSystemDefined) role.IsActive = request.IsActive.Value;

        if (request.PermissionKeys != null)
        {
            var keyToId = (await _repo.GetPermissionsAsync()).ToDictionary(p => p.Key, p => p.PermissionId);
            await _repo.SetRolePermissionsAsync(roleId, ResolveIds(request.PermissionKeys, keyToId));
        }

        await _repo.SaveChangesAsync();
        return await SingleRoleAsync(roleId);
    }

    public async Task<bool> DeleteRoleAsync(Guid roleId)
    {
        var role = await _repo.GetRoleAsync(roleId);
        if (role == null || role.IsSystemDefined) return false;
        await _repo.RemoveRoleAsync(role);
        await _repo.SaveChangesAsync();
        return true;
    }

    public async Task<List<UserSummaryDto>> GetUsersAsync()
    {
        var roleNameById = (await _repo.GetRolesWithPermissionsAsync()).ToDictionary(r => r.RoleId, r => r.Name);
        var users = await _repo.GetUsersWithRolesAsync();
        return users.Select(u => new UserSummaryDto
        {
            UserId = u.UserId,
            Email = u.Email,
            FullName = u.FullName,
            IsActive = u.IsActive,
            MustChangePassword = u.MustChangePassword,
            LastLoginAt = u.LastLoginAt,
            Roles = u.UserRoles.Select(ur => roleNameById.GetValueOrDefault(ur.RoleId, "")).Where(n => n.Length > 0).ToList()
        }).ToList();
    }

    public async Task<UserDetailDto?> GetUserAsync(Guid userId)
    {
        var user = await _repo.GetUserWithRolesAndOverridesAsync(userId);
        if (user == null) return null;

        var idToKey = (await _repo.GetPermissionsAsync()).ToDictionary(p => p.PermissionId, p => p.Key);
        var roleNameById = (await _repo.GetRolesWithPermissionsAsync()).ToDictionary(r => r.RoleId, r => r.Name);

        return new UserDetailDto
        {
            UserId = user.UserId,
            Email = user.Email,
            FullName = user.FullName,
            IsActive = user.IsActive,
            MustChangePassword = user.MustChangePassword,
            LastLoginAt = user.LastLoginAt,
            Roles = user.UserRoles.Select(ur => roleNameById.GetValueOrDefault(ur.RoleId, "")).Where(n => n.Length > 0).ToList(),
            RoleIds = user.UserRoles.Select(ur => ur.RoleId).ToList(),
            Overrides = user.UserPermissions
                .Select(up => new PermissionOverrideDto { Key = idToKey.GetValueOrDefault(up.PermissionId, ""), Effect = up.Effect })
                .Where(o => o.Key.Length > 0).ToList()
        };
    }

    public async Task<(UserDetailDto? User, string? Error)> CreateUserAsync(CreateUserRequest request)
    {
        var email = (request.Email ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email)) return (null, "Email is required.");
        if (!IsValidEmail(email)) return (null, "Invalid email format.");
        if (string.IsNullOrWhiteSpace(request.FullName)) return (null, "Full name is required.");
        if ((request.Password ?? string.Empty).Length < MinPasswordLength) return (null, $"Password must be at least {MinPasswordLength} characters.");
        if (await _repo.EmailExistsAsync(email)) return (null, "A user with that email already exists.");

        var user = new CmsUser
        {
            UserId = Guid.NewGuid(),
            Email = email,
            FullName = request.FullName.Trim(),
            PasswordHash = _hasher.Hash(request.Password!),
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow
        };
        await _repo.AddUserAsync(user, request.RoleIds);
        await _repo.SaveChangesAsync();

        _logger.LogInformation("AUDIT: User created. UserId={UserId} Email={Email}", user.UserId, email);
        return (await GetUserAsync(user.UserId), null);
    }

    public async Task<(UserDetailDto? User, string? Error)> UpdateUserAsync(Guid userId, UpdateUserRequest request)
    {
        var user = await _repo.GetUserWithRolesAndOverridesAsync(userId);
        if (user == null) return (null, "User not found.");

        if (request.FullName != null) user.FullName = request.FullName.Trim();
        if (request.IsActive.HasValue) user.IsActive = request.IsActive.Value;
        user.UpdatedAt = DateTime.UtcNow;

        if (request.RoleIds != null)
            await _repo.SetUserRolesAsync(userId, request.RoleIds);

        if (request.Overrides != null)
        {
            var keyToId = (await _repo.GetPermissionsAsync()).ToDictionary(p => p.Key, p => p.PermissionId);
            var resolved = request.Overrides
                .Where(o => keyToId.ContainsKey(o.Key) && (o.Effect == "Allow" || o.Effect == "Deny"))
                .Select(o => (keyToId[o.Key], o.Effect));
            await _repo.SetUserOverridesAsync(userId, resolved);
        }

        await _repo.SaveChangesAsync();
        return (await GetUserAsync(userId), null);
    }

    public async Task<bool> ResetPasswordAsync(Guid userId, string newPassword)
    {
        if ((newPassword ?? string.Empty).Length < MinPasswordLength) return false;
        var user = await _repo.GetUserWithRolesAndOverridesAsync(userId);
        if (user == null) return false;

        user.PasswordHash = _hasher.Hash(newPassword!);
        user.MustChangePassword = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _repo.SaveChangesAsync();

        _logger.LogWarning(
            "AUDIT: Password reset for UserId={UserId} Email={Email}. User must change password on next login.",
            user.UserId, user.Email);
        return true;
    }

    private static bool IsValidEmail(string email)
    {
        try { _ = new System.Net.Mail.MailAddress(email); return true; }
        catch { return false; }
    }

    private async Task<RoleDto?> SingleRoleAsync(Guid roleId)
    {
        var idToKey = (await _repo.GetPermissionsAsync()).ToDictionary(p => p.PermissionId, p => p.Key);
        var role = await _repo.GetRoleAsync(roleId);
        return role == null ? null : MapRole(role, idToKey);
    }

    private static RoleDto MapRole(CmsRole r, Dictionary<Guid, string> idToKey) => new()
    {
        RoleId = r.RoleId,
        Name = r.Name,
        Description = r.Description,
        IsSystemDefined = r.IsSystemDefined,
        IsActive = r.IsActive,
        PermissionKeys = r.RolePermissions
            .Select(rp => idToKey.GetValueOrDefault(rp.PermissionId, ""))
            .Where(k => k.Length > 0).ToList()
    };

    private static IEnumerable<Guid> ResolveIds(IEnumerable<string> keys, Dictionary<string, Guid> keyToId) =>
        keys.Where(keyToId.ContainsKey).Select(k => keyToId[k]);
}
