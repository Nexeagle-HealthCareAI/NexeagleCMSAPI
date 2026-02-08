using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace CMSAPI.Application.Services;

public class AuthService : IAuthService
{
    private readonly TokenSettings _tokenSettings;

    public AuthService(IOptions<TokenSettings> tokenSettings)
    {
        _tokenSettings = tokenSettings.Value;
    }

    public Task<LoginResponse?> LoginAsync(LoginRequest req)
    {
        // Very small demo: replace with real user validation
        if (string.Equals(req.Email, "admin@cms.com", StringComparison.OrdinalIgnoreCase) && req.Password == "password123")
        {
            var key = _tokenSettings.Key;
            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenKey = Encoding.UTF8.GetBytes(key);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Email, req.Email),
                new Claim(ClaimTypes.Role, "admin")
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(_tokenSettings.ExpiresInMinutes),
                Issuer = _tokenSettings.Issuer,
                Audience = _tokenSettings.Audience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(tokenKey), SecurityAlgorithms.HmacSha256)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = tokenHandler.WriteToken(token);

            var user = new AuthUser
            {
                Id = "USR-1001",
                Name = "Admin User",
                Role = "admin",
                Email = req.Email
            };

            return Task.FromResult<LoginResponse?>(new LoginResponse { Token = tokenString, User = user });
        }

        return Task.FromResult<LoginResponse?>(null);
    }
}
