using CMSAPI.Application.Interfaces;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories;

public class CmsAuthRepository : ICmsAuthRepository
{
    private readonly CmsDbContext _db;

    public CmsAuthRepository(CmsDbContext db)
    {
        _db = db;
    }

    public Task<CmsUser?> GetUserByEmailAsync(string email) =>
        _db.CmsUsers.FirstOrDefaultAsync(u => u.Email == email);

    public Task<CmsUser?> GetUserByIdAsync(Guid userId) =>
        _db.CmsUsers.FirstOrDefaultAsync(u => u.UserId == userId);

    public async Task<CmsUser?> GetUserByEmailOrPhoneAsync(string identifier)
    {
        // Try email first.
        var byEmail = await _db.CmsUsers.FirstOrDefaultAsync(u => u.Email == identifier);
        if (byEmail != null) return byEmail;

        // Fall back to phone (normalise: strip spaces/dashes).
        var normalised = NormalisePhone(identifier);
        return await _db.CmsUsers.FirstOrDefaultAsync(
            u => u.PhoneNumber != null && u.PhoneNumber == normalised);
    }

    private static string NormalisePhone(string s) =>
        new string(s.Where(c => char.IsDigit(c) || c == '+').ToArray());

    public async Task<List<string>> GetEffectivePermissionsAsync(Guid userId)
    {
        var rolePerms = await (
            from ur in _db.CmsUserRoles
            join r in _db.CmsRoles on ur.RoleId equals r.RoleId
            join rp in _db.CmsRolePermissions on r.RoleId equals rp.RoleId
            join p in _db.CmsPermissions on rp.PermissionId equals p.PermissionId
            where ur.UserId == userId && r.IsActive
            select p.Key
        ).ToListAsync();

        var allow = await (
            from up in _db.CmsUserPermissions
            join p in _db.CmsPermissions on up.PermissionId equals p.PermissionId
            where up.UserId == userId && up.Effect == "Allow"
            select p.Key
        ).ToListAsync();

        var deny = await (
            from up in _db.CmsUserPermissions
            join p in _db.CmsPermissions on up.PermissionId equals p.PermissionId
            where up.UserId == userId && up.Effect == "Deny"
            select p.Key
        ).ToListAsync();

        var denySet = deny.ToHashSet();
        return rolePerms.Concat(allow).Where(k => !denySet.Contains(k)).Distinct().ToList();
    }

    public async Task<List<string>> GetUserRoleNamesAsync(Guid userId) =>
        await (
            from ur in _db.CmsUserRoles
            join r in _db.CmsRoles on ur.RoleId equals r.RoleId
            where ur.UserId == userId
            select r.Name
        ).ToListAsync();

    public Task AddRefreshTokenAsync(CmsRefreshToken token)
    {
        _db.CmsRefreshTokens.Add(token);
        return Task.CompletedTask;
    }

    public Task<CmsRefreshToken?> GetActiveRefreshTokenAsync(string tokenHash)
    {
        var now = DateTime.UtcNow;
        return _db.CmsRefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.RevokedAt == null && t.ExpiresAt > now);
    }

    public async Task RevokeAllUserRefreshTokensAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        var active = await _db.CmsRefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync();
        foreach (var t in active) t.RevokedAt = now;
    }

    public Task AddOtpAsync(CmsOtp otp)
    {
        _db.CmsOtps.Add(otp);
        return Task.CompletedTask;
    }

    public Task<CmsOtp?> GetActiveOtpAsync(Guid userId, string codeHash, string purpose)
    {
        var now = DateTime.UtcNow;
        return _db.CmsOtps.FirstOrDefaultAsync(o =>
            o.UserId == userId &&
            o.CodeHash == codeHash &&
            o.Purpose == purpose &&
            o.UsedAt == null &&
            o.ExpiresAt > now);
    }

    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}
