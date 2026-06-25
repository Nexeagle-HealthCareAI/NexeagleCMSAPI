using System.Security.Claims;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private const string RefreshCookieName = "cms_rt";

    private readonly IAuthService _authService;
    private readonly IWebHostEnvironment _env;

    public AuthController(IAuthService authService, IWebHostEnvironment env)
    {
        _authService = authService;
        _env = env;
    }

    private string? ClientIp => HttpContext?.Connection?.RemoteIpAddress?.ToString();

    // Stores the refresh token in an HttpOnly, Secure cookie so it cannot be read by JS.
    private void SetRefreshCookie(string refreshToken)
    {
        var isProd = !_env.IsDevelopment();
        Response.Cookies.Append(RefreshCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure   = isProd,
            SameSite = isProd ? SameSiteMode.None : SameSiteMode.Lax,
            Expires  = DateTimeOffset.UtcNow.AddDays(7),
            Path     = "/api/v1/auth"
        });
    }

    private void ClearRefreshCookie()
    {
        var isProd = !_env.IsDevelopment();
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure   = isProd,
            SameSite = isProd ? SameSiteMode.None : SameSiteMode.Lax,
            Path     = "/api/v1/auth"
        });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var response = await _authService.LoginAsync(req, ClientIp);
        if (response == null)
            return Unauthorized(new { message = "Invalid credentials", code = "AUTH_INVALID_CREDENTIALS" });

        SetRefreshCookie(response.RefreshToken);
        response.RefreshToken = string.Empty; // Never expose refresh token in response body
        return Ok(response);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var refreshToken = Request.Cookies[RefreshCookieName];
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Unauthorized(new { message = "Invalid or expired refresh token", code = "AUTH_REFRESH_INVALID" });

        var response = await _authService.RefreshAsync(refreshToken, ClientIp);
        if (response == null)
        {
            ClearRefreshCookie();
            return Unauthorized(new { message = "Invalid or expired refresh token", code = "AUTH_REFRESH_INVALID" });
        }

        SetRefreshCookie(response.RefreshToken);
        response.RefreshToken = string.Empty;
        return Ok(response);
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var refreshToken = Request.Cookies[RefreshCookieName];
        if (!string.IsNullOrWhiteSpace(refreshToken))
            await _authService.LogoutAsync(refreshToken);
        ClearRefreshCookie();
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var me = await _authService.GetMeAsync(userId);
        return me == null ? Unauthorized() : Ok(me);
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var ok = await _authService.ChangePasswordAsync(userId, req);
        if (!ok)
            return BadRequest(new { message = "Password change failed (wrong current password or new password too weak).", code = "AUTH_PASSWORD_CHANGE_FAILED" });
        return NoContent();
    }

    // ── OTP ──────────────────────────────────────────────────────────────────

    /// <summary>Step 1: generate and dispatch a 6-digit OTP to the user's email or phone.</summary>
    [AllowAnonymous]
    [HttpPost("request-otp")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    public async Task<IActionResult> RequestOtp([FromBody] OtpRequest req)
    {
        var result = await _authService.RequestOtpAsync(req, ClientIp, _env.IsDevelopment());
        // Always 200 – never reveal whether an account exists (anti-enumeration).
        return Ok(result);
    }

    /// <summary>Step 2: verify the OTP and issue a session.</summary>
    [AllowAnonymous]
    [HttpPost("login-otp")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    public async Task<IActionResult> LoginWithOtp([FromBody] OtpLoginRequest req)
    {
        var response = await _authService.VerifyOtpLoginAsync(req, ClientIp);
        if (response == null)
            return Unauthorized(new { message = "Invalid or expired OTP.", code = "AUTH_OTP_INVALID" });

        SetRefreshCookie(response.RefreshToken);
        response.RefreshToken = string.Empty;
        return Ok(response);
    }

    // ── Forgot Password ───────────────────────────────────────────────────────

    /// <summary>Step 1: send a password-reset OTP to the user's email or phone.</summary>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        var result = await _authService.RequestForgotPasswordOtpAsync(req, ClientIp, _env.IsDevelopment());
        // Always 200 – never reveal whether an account exists (anti-enumeration).
        return Ok(result);
    }

    /// <summary>Step 2: verify the OTP and set a new password.</summary>
    [AllowAnonymous]
    [HttpPost("reset-password")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    public async Task<IActionResult> ResetPassword([FromBody] OtpResetPasswordRequest req)
    {
        var ok = await _authService.ResetPasswordWithOtpAsync(req);
        if (!ok)
            return BadRequest(new { message = "Invalid or expired OTP, or password does not meet requirements.", code = "AUTH_RESET_FAILED" });
        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out userId);
    }
}
