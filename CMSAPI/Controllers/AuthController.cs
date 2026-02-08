using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Models;
using CMSAPI.Application.Interfaces;

namespace CMSAPI.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var response = await _authService.LoginAsync(req);
        if (response == null)
        {
            return Unauthorized(new { message = "Invalid credentials", code = "AUTH_INVALID_CREDENTIALS" });
        }
        
        return Ok(response);
    }
}
