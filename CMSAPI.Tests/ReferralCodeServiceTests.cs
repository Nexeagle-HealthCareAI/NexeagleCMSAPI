using Xunit;
using CMSAPI.Application.Services;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Moq;
using System;
using System.Threading.Tasks;

public class ReferralCodeServiceTests
{
    private readonly Mock<IReferralCodeRepository> _mockRepo;
    private readonly ReferralCodeService _service;

    public ReferralCodeServiceTests()
    {
        _mockRepo = new Mock<IReferralCodeRepository>();
        _service = new ReferralCodeService(_mockRepo.Object);
    }

    [Fact]
    public async Task ValidateAsync_CodeNotFound_ReturnsInvalid()
    {
        _mockRepo.Setup(r => r.GetByCodeAsync("MISSING")).ReturnsAsync((ReferralCode?)null);

        var result = await _service.ValidateAsync("MISSING");

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task ValidateAsync_InactiveCode_ReturnsInvalid()
    {
        var type = new ReferralCodeType { ReferralCodeTypeId = Guid.NewGuid(), Name = "Promo", RewardKind = "PercentageOff", RewardValue = 5m, IsActive = true };
        var code = new ReferralCode { ReferralCodeId = Guid.NewGuid(), Code = "OLDCODE", IsActive = false, ReferralCodeType = type };
        _mockRepo.Setup(r => r.GetByCodeAsync("OLDCODE")).ReturnsAsync(code);

        var result = await _service.ValidateAsync("OLDCODE");

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task ValidateAsync_InactiveType_ReturnsInvalid()
    {
        var type = new ReferralCodeType { ReferralCodeTypeId = Guid.NewGuid(), Name = "Promo", RewardKind = "PercentageOff", RewardValue = 5m, IsActive = false };
        var code = new ReferralCode { ReferralCodeId = Guid.NewGuid(), Code = "PROMO5", IsActive = true, ReferralCodeType = type };
        _mockRepo.Setup(r => r.GetByCodeAsync("PROMO5")).ReturnsAsync(code);

        var result = await _service.ValidateAsync("PROMO5");

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task ValidateAsync_AlreadyRedeemed_ReturnsInvalid()
    {
        var type = new ReferralCodeType { ReferralCodeTypeId = Guid.NewGuid(), Name = "Promo", RewardKind = "PercentageOff", RewardValue = 5m, IsActive = true };
        var code = new ReferralCode { ReferralCodeId = Guid.NewGuid(), Code = "USED1", IsActive = true, ReferralCodeType = type, RedeemedByHospitalId = Guid.NewGuid() };
        _mockRepo.Setup(r => r.GetByCodeAsync("USED1")).ReturnsAsync(code);

        var result = await _service.ValidateAsync("USED1");

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task ValidateAsync_ActiveUnredeemedCode_ReturnsValidWithRewardDetails()
    {
        var type = new ReferralCodeType { ReferralCodeTypeId = Guid.NewGuid(), Name = "Launch Promo", RewardKind = "ExtraMonths", RewardValue = 2m, IsActive = true };
        var code = new ReferralCode { ReferralCodeId = Guid.NewGuid(), Code = "LAUNCH2", IsActive = true, ReferralCodeType = type };
        _mockRepo.Setup(r => r.GetByCodeAsync("LAUNCH2")).ReturnsAsync(code);

        var result = await _service.ValidateAsync("LAUNCH2");

        Assert.True(result.Valid);
        Assert.Equal("ExtraMonths", result.RewardKind);
        Assert.Equal(2m, result.RewardValue);
        Assert.Equal("Launch Promo", result.ReferralCodeTypeName);
    }

    [Fact]
    public async Task CreateCodeAsync_ManualCodeAlreadyExists_Throws()
    {
        var typeId = Guid.NewGuid();
        _mockRepo.Setup(r => r.GetTypeByIdAsync(typeId)).ReturnsAsync(new ReferralCodeType { ReferralCodeTypeId = typeId, Name = "Promo", RewardKind = "PercentageOff", RewardValue = 5m });
        _mockRepo.Setup(r => r.ExistsCodeAsync("TAKEN1")).ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateCodeAsync(new CreateReferralCodeRequest { ReferralCodeTypeId = typeId, Code = "TAKEN1" }, null));
    }

    [Fact]
    public async Task CreateCodeAsync_BlankCode_GeneratesAndPersistsAUniqueCode()
    {
        var typeId = Guid.NewGuid();
        _mockRepo.Setup(r => r.GetTypeByIdAsync(typeId)).ReturnsAsync(new ReferralCodeType { ReferralCodeTypeId = typeId, Name = "Promo", RewardKind = "PercentageOff", RewardValue = 5m });
        _mockRepo.Setup(r => r.ExistsCodeAsync(It.IsAny<string>())).ReturnsAsync(false);
        _mockRepo.Setup(r => r.CreateCodeAsync(It.IsAny<ReferralCode>())).ReturnsAsync((ReferralCode c) => c);

        var result = await _service.CreateCodeAsync(new CreateReferralCodeRequest { ReferralCodeTypeId = typeId, Code = null }, null);

        Assert.False(string.IsNullOrWhiteSpace(result.Code));
        Assert.Equal(6, result.Code.Length);
        _mockRepo.Verify(r => r.CreateCodeAsync(It.IsAny<ReferralCode>()), Times.Once);
    }

    [Fact]
    public async Task CreateCodeAsync_UnknownType_Throws()
    {
        var typeId = Guid.NewGuid();
        _mockRepo.Setup(r => r.GetTypeByIdAsync(typeId)).ReturnsAsync((ReferralCodeType?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateCodeAsync(new CreateReferralCodeRequest { ReferralCodeTypeId = typeId }, null));
    }
}
