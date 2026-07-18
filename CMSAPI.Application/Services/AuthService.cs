using System.Security.Cryptography;
using System.Text;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Application.Services;

public class AuthService : IAuthService
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;
    private const int MinPasswordLength = 8;
    private const int OtpExpiryMinutes = 10;

    private readonly ICmsAuthRepository _repo;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly ILogger<AuthService> _logger;

    public AuthService(ICmsAuthRepository repo, IPasswordHasher hasher, ITokenService tokens, ILogger<AuthService> logger)
    {
        _repo = repo;
        _hasher = hasher;
        _tokens = tokens;
        _logger = logger;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, string? ipAddress)
    {
        // Accept either Identifier (email or phone) or the legacy Email field.
        var identifier = ((request.Identifier ?? request.Email) ?? string.Empty).Trim();
        var user = await _repo.GetUserByEmailOrPhoneAsync(identifier);
        if (user == null) return null;

        var now = DateTime.UtcNow;
        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > now) return null;
        if (!user.IsActive) return null;

        if (!_hasher.Verify(request.Password ?? string.Empty, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutEnd = now.AddMinutes(LockoutMinutes);
                user.FailedLoginAttempts = 0;
            }
            user.UpdatedAt = now;
            await _repo.SaveChangesAsync();
            return null;
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = now;
        user.LastLoginIp = ipAddress;
        user.UpdatedAt = now;

        var response = await IssueTokensAsync(user, ipAddress);
        await _repo.SaveChangesAsync();
        return response;
    }

    public async Task<LoginResponse?> RefreshAsync(string refreshToken, string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;

        var existing = await _repo.GetActiveRefreshTokenAsync(_tokens.HashRefreshToken(refreshToken));
        if (existing == null) return null;

        var user = await _repo.GetUserByIdAsync(existing.UserId);
        if (user == null || !user.IsActive)
        {
            existing.RevokedAt = DateTime.UtcNow;
            await _repo.SaveChangesAsync();
            return null;
        }

        var response = await IssueTokensAsync(user, ipAddress, rotatedFrom: existing);
        await _repo.SaveChangesAsync();
        return response;
    }

    public async Task LogoutAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var existing = await _repo.GetActiveRefreshTokenAsync(_tokens.HashRefreshToken(refreshToken));
        if (existing != null)
        {
            existing.RevokedAt = DateTime.UtcNow;
            await _repo.SaveChangesAsync();
        }
    }

    public async Task<MeResponse?> GetMeAsync(Guid userId)
    {
        var user = await _repo.GetUserByIdAsync(userId);
        if (user == null) return null;

        var permissions = await _repo.GetEffectivePermissionsAsync(userId);
        var roles = await _repo.GetUserRoleNamesAsync(userId);

        return new MeResponse
        {
            User = ToAuthUser(user, roles),
            Roles = roles,
            Permissions = permissions,
            MustChangePassword = user.MustChangePassword
        };
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _repo.GetUserByIdAsync(userId);
        if (user == null) return false;
        if (!_hasher.Verify(request.CurrentPassword ?? string.Empty, user.PasswordHash)) return false;
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < MinPasswordLength) return false;

        user.PasswordHash = _hasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _repo.RevokeAllUserRefreshTokensAsync(userId);
        await _repo.SaveChangesAsync();
        return true;
    }

    // ── OTP ──────────────────────────────────────────────────────────────────

    public Task<OtpRequestResponse?> RequestOtpAsync(OtpRequest request, string? ipAddress, bool isDevelopment) =>
        RequestOtpCoreAsync((request.Identifier ?? string.Empty).Trim(), "login", ipAddress, isDevelopment);

    public Task<OtpRequestResponse?> RequestForgotPasswordOtpAsync(ForgotPasswordRequest request, string? ipAddress, bool isDevelopment) =>
        RequestOtpCoreAsync((request.Identifier ?? string.Empty).Trim(), "password_reset", ipAddress, isDevelopment);

    // Returns null when no active account matches -- the controller turns that into a 404 so the
    // login screen can tell the user their email/phone isn't registered. Deliberately *not*
    // anti-enumeration-safe: this is the internal CMS admin tool (a small, known set of NexEagle
    // staff accounts), where a clear "not registered" beats the silent-generic-response pattern
    // used for consumer-facing signup/login flows.
    private async Task<OtpRequestResponse?> RequestOtpCoreAsync(string identifier, string purpose, string? ipAddress, bool isDevelopment)
    {
        var deliveryMethod = identifier.Contains('@') ? "email" : "sms";

        var user = await _repo.GetUserByEmailOrPhoneAsync(identifier);
        if (user == null || !user.IsActive) return null;

        var rawCode = GenerateOtpCode();
        var otp = new CmsOtp
        {
            OtpId = Guid.NewGuid(),
            UserId = user.UserId,
            CodeHash = HashOtp(rawCode),
            DeliveryTarget = identifier,
            DeliveryMethod = deliveryMethod,
            Purpose = purpose,
            ExpiresAt = DateTime.UtcNow.AddMinutes(OtpExpiryMinutes),
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };
        // Always saved regardless of delivery outcome below -- delivery isn't wired to a real
        // provider yet (see the TODO), but even once it is, a delivery failure shouldn't lose the
        // generated code: it stays retrievable (audit log today, DB lookup either way) and the UI
        // still moves to the OTP-entry screen since a real, usable code exists.
        await _repo.AddOtpAsync(otp);
        await _repo.SaveChangesAsync();

        // TODO: integrate a real email/SMS provider here.
        _logger.LogWarning(
            "AUDIT OTP: purpose={Purpose} identifier={Identifier} code={Code} expires={Expiry}",
            purpose, identifier, rawCode, otp.ExpiresAt);

        return new OtpRequestResponse
        {
            Message = "OTP sent successfully.",
            DeliveryMethod = deliveryMethod,
            DevOtp = isDevelopment ? rawCode : null  // never expose in production
        };
    }

    public async Task<LoginResponse?> VerifyOtpLoginAsync(OtpLoginRequest request, string? ipAddress)
    {
        var identifier = (request.Identifier ?? string.Empty).Trim();
        var user = await _repo.GetUserByEmailOrPhoneAsync(identifier);
        if (user == null || !user.IsActive) return null;

        var now = DateTime.UtcNow;
        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > now) return null;

        var codeHash = HashOtp(request.Otp ?? string.Empty);
        var otp = await _repo.GetActiveOtpAsync(user.UserId, codeHash, "login");

        if (otp == null)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutEnd = now.AddMinutes(LockoutMinutes);
                user.FailedLoginAttempts = 0;
            }
            user.UpdatedAt = now;
            await _repo.SaveChangesAsync();
            return null;
        }

        // Consume the OTP so it can't be reused.
        otp.UsedAt = now;
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = now;
        user.LastLoginIp = ipAddress;
        user.UpdatedAt = now;

        var response = await IssueTokensAsync(user, ipAddress);
        await _repo.SaveChangesAsync();
        return response;
    }

    public async Task<bool> ResetPasswordWithOtpAsync(OtpResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < MinPasswordLength)
            return false;

        var identifier = (request.Identifier ?? string.Empty).Trim();
        var user = await _repo.GetUserByEmailOrPhoneAsync(identifier);
        if (user == null || !user.IsActive) return false;

        var now = DateTime.UtcNow;
        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > now) return false;

        var codeHash = HashOtp(request.Otp ?? string.Empty);
        var otp = await _repo.GetActiveOtpAsync(user.UserId, codeHash, "password_reset");

        if (otp == null)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutEnd = now.AddMinutes(LockoutMinutes);
                user.FailedLoginAttempts = 0;
            }
            user.UpdatedAt = now;
            await _repo.SaveChangesAsync();
            return false;
        }

        // Consume the OTP so it can't be reused.
        otp.UsedAt = now;
        user.PasswordHash = _hasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = now;

        // Revoke all sessions so existing tokens are immediately invalidated.
        await _repo.RevokeAllUserRefreshTokensAsync(user.UserId);
        await _repo.SaveChangesAsync();

        _logger.LogWarning(
            "AUDIT PASSWORD_RESET_VIA_OTP: userId={UserId} identifier={Identifier}",
            user.UserId, identifier);
        return true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GenerateOtpCode() =>
        Random.Shared.Next(100_000, 1_000_000).ToString();

    private static string HashOtp(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private async Task<LoginResponse> IssueTokensAsync(CmsUser user, string? ipAddress, CmsRefreshToken? rotatedFrom = null)
    {
        var permissions = await _repo.GetEffectivePermissionsAsync(user.UserId);
        var roles = await _repo.GetUserRoleNamesAsync(user.UserId);

        var (raw, hash, expiresAt) = _tokens.CreateRefreshToken();
        var newToken = new CmsRefreshToken
        {
            TokenId = Guid.NewGuid(),
            UserId = user.UserId,
            TokenHash = hash,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };
        await _repo.AddRefreshTokenAsync(newToken);

        if (rotatedFrom != null)
        {
            rotatedFrom.RevokedAt = DateTime.UtcNow;
            rotatedFrom.ReplacedByTokenId = newToken.TokenId;
        }

        return new LoginResponse
        {
            Token = _tokens.CreateAccessToken(user, permissions),
            RefreshToken = raw,
            ExpiresInSeconds = _tokens.AccessTokenLifetimeSeconds,
            User = ToAuthUser(user, roles),
            Permissions = permissions,
            MustChangePassword = user.MustChangePassword
        };
    }

    private static AuthUser ToAuthUser(CmsUser user, List<string> roles) => new()
    {
        Id = user.UserId.ToString(),
        Name = user.FullName,
        Email = user.Email,
        Role = roles.FirstOrDefault() ?? string.Empty
    };
}
