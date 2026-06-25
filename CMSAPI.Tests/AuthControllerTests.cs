using Xunit;
using CMSAPI.Controllers;
using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Models;
using CMSAPI.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Moq;
using System.Threading.Tasks;

public class AuthControllerTests
{
    private readonly Mock<IAuthService> _mockAuthService;
    private readonly Mock<IWebHostEnvironment> _mockEnv;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockEnv = new Mock<IWebHostEnvironment>();
        _mockEnv.Setup(e => e.EnvironmentName).Returns("Development");
        _controller = new AuthController(_mockAuthService.Object, _mockEnv.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsOkResult()
    {
        // Arrange
        var req = new LoginRequest { Email = "admin@cms.com", Password = "password123" };
        var expectedResponse = new LoginResponse 
        { 
            Token = "valid_token", 
            User = new AuthUser { Email = "admin@cms.com", Role = "admin" } 
        };

        _mockAuthService.Setup(s => s.LoginAsync(req, It.IsAny<string?>())).ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.Login(req);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<LoginResponse>(okResult.Value);
        Assert.Equal("valid_token", resp.Token);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        // Arrange
        var req = new LoginRequest { Email = "bad@cms.com", Password = "nope" };
        _mockAuthService.Setup(s => s.LoginAsync(req, It.IsAny<string?>())).ReturnsAsync((LoginResponse?)null);

        // Act
        var result = await _controller.Login(req);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }
}
