using Xunit;
using Moq;
using Microsoft.Extensions.Options;
using CMSAPI.Application.Services;
using CMSAPI.Application.Models;
using System.Threading.Tasks;

namespace CMSAPI.Tests;

public class AuthServiceTests
{
    private readonly Mock<IOptions<TokenSettings>> _mockOptions;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _mockOptions = new Mock<IOptions<TokenSettings>>();
        var settings = new TokenSettings
        {
            Key = "TestSecretKey_12345678901234567890",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            ExpiresInMinutes = 60
        };
        _mockOptions.Setup(o => o.Value).Returns(settings);
        _authService = new AuthService(_mockOptions.Object);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsTokenAndUser()
    {
        // Arrange
        var request = new LoginRequest { Email = "admin@cms.com", Password = "password123" };

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Token);
        Assert.Equal("admin@cms.com", result.User.Email);
        Assert.Equal("admin", result.User.Role);
    }

    [Fact]
    public async Task LoginAsync_WithInvalidCredentials_ReturnsNull()
    {
        // Arrange
        var request = new LoginRequest { Email = "wrong@cms.com", Password = "wrongpassword" };

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        Assert.Null(result);
    }
}
