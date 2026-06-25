using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, string? ipAddress);
    Task<LoginResponse?> RefreshAsync(string refreshToken, string? ipAddress);
    Task LogoutAsync(string refreshToken);
    Task<MeResponse?> GetMeAsync(Guid userId);
    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request);

    /// <summary>Generate and dispatch a 6-digit OTP to the user's email or phone.</summary>
    Task<OtpRequestResponse> RequestOtpAsync(OtpRequest request, string? ipAddress, bool isDevelopment);
    /// <summary>Verify the OTP and, if valid, issue a full session (same response as Login).</summary>
    Task<LoginResponse?> VerifyOtpLoginAsync(OtpLoginRequest request, string? ipAddress);

    /// <summary>Send a password-reset OTP to the user's email or phone.</summary>
    Task<OtpRequestResponse> RequestForgotPasswordOtpAsync(ForgotPasswordRequest request, string? ipAddress, bool isDevelopment);
    /// <summary>Verify the reset OTP and, if valid, change the password. All existing sessions are revoked.</summary>
    Task<bool> ResetPasswordWithOtpAsync(OtpResetPasswordRequest request);
}
