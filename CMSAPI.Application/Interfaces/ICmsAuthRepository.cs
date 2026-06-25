using CMSAPI.Domain.Entities;

namespace CMSAPI.Application.Interfaces;

/// <summary>Data access for CMS identity + RBAC (backed by CMSDatabase).</summary>
public interface ICmsAuthRepository
{
    Task<CmsUser?> GetUserByEmailAsync(string email);
    Task<CmsUser?> GetUserByIdAsync(Guid userId);
    /// <summary>Looks up by email first; if no match and identifier looks like a phone number, looks up by phone.</summary>
    Task<CmsUser?> GetUserByEmailOrPhoneAsync(string identifier);

    /// <summary>Effective permission keys = (role grants ∪ user Allow) − user Deny.</summary>
    Task<List<string>> GetEffectivePermissionsAsync(Guid userId);
    Task<List<string>> GetUserRoleNamesAsync(Guid userId);

    Task AddRefreshTokenAsync(CmsRefreshToken token);
    Task<CmsRefreshToken?> GetActiveRefreshTokenAsync(string tokenHash);
    Task RevokeAllUserRefreshTokensAsync(Guid userId);

    // OTP
    Task AddOtpAsync(CmsOtp otp);
    /// <summary>Returns the first active (unused, not expired) OTP matching userId + code hash + purpose.</summary>
    Task<CmsOtp?> GetActiveOtpAsync(Guid userId, string codeHash, string purpose);

    Task SaveChangesAsync();
}
