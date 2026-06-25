namespace CMSAPI.Domain.Entities;

/// <summary>Rotating refresh token; only the SHA-256 hash of the raw token is stored.</summary>
public class CmsRefreshToken
{
    public Guid TokenId { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }

    public CmsUser? User { get; set; }
}
