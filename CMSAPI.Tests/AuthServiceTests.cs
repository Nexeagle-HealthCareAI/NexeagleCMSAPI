using Xunit;
using Moq;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using CMSAPI.Domain.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CMSAPI.Tests;

public class AuthServiceTests
{
    private readonly Mock<ICmsAuthRepository> _repo = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<ILogger<AuthService>> _logger = new();
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _tokens.Setup(t => t.AccessTokenLifetimeSeconds).Returns(900);
        _tokens.Setup(t => t.CreateAccessToken(It.IsAny<CmsUser>(), It.IsAny<IReadOnlyCollection<string>>()))
               .Returns("access-token");
        _tokens.Setup(t => t.CreateRefreshToken())
               .Returns(("raw-refresh", "hashed-refresh", DateTime.UtcNow.AddDays(7)));
        _authService = new AuthService(_repo.Object, _hasher.Object, _tokens.Object, _email.Object, _logger.Object);
    }

    private CmsUser ActiveUser() => new()
    {
        UserId = Guid.NewGuid(),
        Email = "alice@cms.com",
        FullName = "Alice",
        PasswordHash = "stored-hash",
        IsActive = true
    };

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsTokensAndPermissions()
    {
        var user = ActiveUser();
        _repo.Setup(r => r.GetUserByEmailAsync("alice@cms.com")).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify("pw", "stored-hash")).Returns(true);
        _repo.Setup(r => r.GetEffectivePermissionsAsync(user.UserId))
             .ReturnsAsync(new List<string> { "dashboard.view" });
        _repo.Setup(r => r.GetUserRoleNamesAsync(user.UserId))
             .ReturnsAsync(new List<string> { "Administrator" });

        var result = await _authService.LoginAsync(new LoginRequest { Email = "alice@cms.com", Password = "pw" }, "1.2.3.4");

        Assert.NotNull(result);
        Assert.Equal("access-token", result!.Token);
        Assert.Equal("raw-refresh", result.RefreshToken);
        Assert.Equal("alice@cms.com", result.User.Email);
        Assert.Contains("dashboard.view", result.Permissions);
        _repo.Verify(r => r.AddRefreshTokenAsync(It.IsAny<CmsRefreshToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task LoginAsync_WithUnknownUser_ReturnsNull()
    {
        _repo.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((CmsUser?)null);

        var result = await _authService.LoginAsync(new LoginRequest { Email = "ghost@cms.com", Password = "pw" }, null);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_IncrementsFailuresAndReturnsNull()
    {
        var user = ActiveUser();
        _repo.Setup(r => r.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var result = await _authService.LoginAsync(new LoginRequest { Email = user.Email, Password = "bad" }, null);

        Assert.Null(result);
        Assert.Equal(1, user.FailedLoginAttempts);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithInactiveUser_ReturnsNull()
    {
        var user = ActiveUser();
        user.IsActive = false;
        _repo.Setup(r => r.GetUserByEmailAsync(user.Email)).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        var result = await _authService.LoginAsync(new LoginRequest { Email = user.Email, Password = "pw" }, null);

        Assert.Null(result);
    }

    [Fact]
    public async Task RequestOtpAsync_WithEmailIdentifier_SendsOtpEmail()
    {
        var user = ActiveUser();
        _repo.Setup(r => r.GetUserByEmailOrPhoneAsync(user.Email)).ReturnsAsync(user);
        _email.Setup(e => e.SendOtpEmailAsync(user.Email, It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(true);

        var result = await _authService.RequestOtpAsync(new OtpRequest { Identifier = user.Email }, "1.2.3.4", isDevelopment: false);

        Assert.NotNull(result);
        Assert.Equal("email", result!.DeliveryMethod);
        Assert.Null(result.DevOtp); // never populated outside Development
        _email.Verify(e => e.SendOtpEmailAsync(user.Email, It.IsAny<string>(), It.IsAny<int>()), Times.Once);
        _repo.Verify(r => r.AddOtpAsync(It.IsAny<CmsOtp>()), Times.Once);
    }

    [Fact]
    public async Task RequestOtpAsync_WithPhoneIdentifier_DoesNotCallEmailService()
    {
        var user = ActiveUser();
        const string phone = "+919876543210";
        _repo.Setup(r => r.GetUserByEmailOrPhoneAsync(phone)).ReturnsAsync(user);

        var result = await _authService.RequestOtpAsync(new OtpRequest { Identifier = phone }, null, isDevelopment: false);

        Assert.NotNull(result);
        Assert.Equal("sms", result!.DeliveryMethod);
        _email.Verify(e => e.SendOtpEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RequestOtpAsync_InDevelopment_PopulatesDevOtpRegardlessOfEmailDeliveryOutcome()
    {
        var user = ActiveUser();
        _repo.Setup(r => r.GetUserByEmailOrPhoneAsync(user.Email)).ReturnsAsync(user);
        _email.Setup(e => e.SendOtpEmailAsync(user.Email, It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(false);

        var result = await _authService.RequestOtpAsync(new OtpRequest { Identifier = user.Email }, null, isDevelopment: true);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result!.DevOtp));
    }
}
