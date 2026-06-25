using CMSAPI.Domain.Entities;

namespace CMSAPI.Application.Interfaces;

public interface ITokenService
{
    /// <summary>Access JWT carrying the user's id/email/name and one "perm" claim per permission key.</summary>
    string CreateAccessToken(CmsUser user, IReadOnlyCollection<string> permissions);
    int AccessTokenLifetimeSeconds { get; }

    /// <summary>Generates a new raw refresh token plus its hash and expiry. Only the hash is persisted.</summary>
    (string Raw, string Hash, DateTime ExpiresAt) CreateRefreshToken();
    string HashRefreshToken(string raw);
}
