namespace CMSAPI.Application.Models;

public class LoginRequest
{
    /// <summary>Legacy field – kept for backwards compatibility.</summary>
    public string? Email { get; set; }
    /// <summary>New field – accepts email address OR phone number (E.164).</summary>
    public string? Identifier { get; set; }
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;          // access JWT
    public string RefreshToken { get; set; } = string.Empty;   // cleared before HTTP response; set as HttpOnly cookie
    public int ExpiresInSeconds { get; set; }
    public AuthUser User { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public bool MustChangePassword { get; set; }
}

public class AuthUser
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class MeResponse
{
    public AuthUser User { get; set; } = new();
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public bool MustChangePassword { get; set; }
}

// ── OTP ──────────────────────────────────────────────────────────────────────

public class OtpRequest
{
    /// <summary>Email address or phone number (E.164 format, e.g. +919876543210).</summary>
    public string Identifier { get; set; } = string.Empty;
}

public class OtpRequestResponse
{
    public string Message { get; set; } = string.Empty;
    /// <summary>"email" or "sms".</summary>
    public string DeliveryMethod { get; set; } = string.Empty;
    /// <summary>Only populated in Development environment for testing without a real mail/SMS provider.</summary>
    public string? DevOtp { get; set; }
}

public class OtpLoginRequest
{
    /// <summary>Same identifier used to request the OTP.</summary>
    public string Identifier { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
}

// ── Forgot Password ───────────────────────────────────────────────────────

public class ForgotPasswordRequest
{
    /// <summary>Email address or phone number (E.164 format).</summary>
    public string Identifier { get; set; } = string.Empty;
}

public class OtpResetPasswordRequest
{
    /// <summary>Same identifier used to request the forgot-password OTP.</summary>
    public string Identifier { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
