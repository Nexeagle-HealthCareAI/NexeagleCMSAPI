using System;

namespace CMSAPI.Application.Models;

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public AuthUser User { get; set; } = new();
}

public class AuthUser
{
    // Use string id so we can return friendly IDs like "USR-1001" without forcing a Guid
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
