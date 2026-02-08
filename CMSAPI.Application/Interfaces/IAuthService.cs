using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);
}
