namespace CMSAPI.Domain.Entities;

/// <summary>
/// One-time password record. Created when a user requests OTP login or a password reset.
///
/// Code is stored in PLAINTEXT (see CMSDatabase migration 11) -- a deliberate choice, not an
/// oversight: this trades away defense-in-depth against a DB leak/dump (previously the stored
/// CodeHash was useless to an attacker without brute-forcing it) for being able to read a
/// currently-valid code directly from the row instead of waiting on email delivery. Rows are
/// short-lived (OtpExpiryMinutes) and single-use (UsedAt), which bounds but doesn't eliminate
/// the exposure window.
///
/// Required SQL (run once against CMSDatabase):
/// <code>
/// CREATE TABLE CmsOtps (
///     OtpId          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
///     UserId         UNIQUEIDENTIFIER NOT NULL,
///     Code           NVARCHAR(6)      NOT NULL,
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
///
/// -- Migration 11: replace hashed storage with plaintext (see CMSDatabase repo for the
/// -- actual idempotent script; this is the shape of what it does):
/// ALTER TABLE CmsOtps ADD Code NVARCHAR(6) NULL;
/// ALTER TABLE CmsOtps DROP COLUMN CodeHash;
/// ALTER TABLE CmsOtps ALTER COLUMN Code NVARCHAR(6) NOT NULL;
/// </code>
/// </summary>
public class CmsOtp
{
    public Guid OtpId { get; set; }
    public Guid UserId { get; set; }
    public string Code { get; set; } = string.Empty;           // plaintext 6-digit code -- see class doc above
    public string DeliveryTarget { get; set; } = string.Empty; // email address or phone number
    public string DeliveryMethod { get; set; } = string.Empty; // "email" | "sms"
    public string Purpose { get; set; } = "login";            // "login" | "password_reset"
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public string? CreatedByIp { get; set; }
}
