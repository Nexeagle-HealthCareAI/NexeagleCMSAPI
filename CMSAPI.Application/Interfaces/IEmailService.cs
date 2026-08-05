namespace CMSAPI.Application.Interfaces;

public interface IEmailService
{
    /// <summary>Sends the OTP verification email. Returns false (never throws) on any send
    /// failure -- AuthService already keeps the OTP usable via the DB/audit log regardless of
    /// delivery outcome (see RequestOtpCoreAsync), so a failed send shouldn't fail the request.</summary>
    Task<bool> SendOtpEmailAsync(string recipientEmail, string otp, int expiryMinutes);
}
