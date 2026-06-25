namespace CMSAPI.Domain.Entities;

/// <summary>
/// One-time password record. Created when a user requests OTP login or a password reset.
/// Required SQL (run once against CMSDatabase):
/// <code>
/// CREATE TABLE CmsOtps (
///     OtpId          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
///     UserId         UNIQUEIDENTIFIER NOT NULL,
///     CodeHash       NVARCHAR(64)     NOT NULL,
///     DeliveryTarget NVARCHAR(256)    NOT NULL,
///     DeliveryMethod NVARCHAR(10)     NOT NULL,
///     Purpose        NVARCHAR(20)     NOT NULL DEFAULT 'login',
///     ExpiresAt      DATETIME2        NOT NULL,
///     CreatedAt      DATETIME2        NOT NULL,
///     UsedAt         DATETIME2        NULL,
///     CreatedByIp    NVARCHAR(64)     NULL
/// );
/// CREATE INDEX IX_CmsOtps_UserId ON CmsOtps(UserId);
///
/// -- If upgrading an existing CmsOtps table:
/// ALTER TABLE CmsOtps ADD Purpose NVARCHAR(20) NOT NULL DEFAULT 'login';
///
/// -- Also add PhoneNumber if not already present:
/// ALTER TABLE CmsUsers ADD PhoneNumber NVARCHAR(20) NULL;
/// CREATE INDEX IX_CmsUsers_PhoneNumber ON CmsUsers(PhoneNumber)
///     WHERE PhoneNumber IS NOT NULL;
/// </code>
/// </summary>
public class CmsOtp
{
    public Guid OtpId { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;       // SHA-256 of the 6-digit code
    public string DeliveryTarget { get; set; } = string.Empty; // email address or phone number
    public string DeliveryMethod { get; set; } = string.Empty; // "email" | "sms"
    public string Purpose { get; set; } = "login";            // "login" | "password_reset"
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public string? CreatedByIp { get; set; }
}
