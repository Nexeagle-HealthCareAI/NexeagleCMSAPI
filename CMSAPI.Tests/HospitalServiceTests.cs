using Xunit;
using CMSAPI.Application.Services;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Moq;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

public class HospitalServiceTests
{
    private readonly Mock<IHospitalRepository> _mockRepo;
    private readonly HospitalService _service;

    public HospitalServiceTests()
    {
        _mockRepo = new Mock<IHospitalRepository>();
        _service = new HospitalService(_mockRepo.Object);
    }

    [Fact]
    public async Task GetHospitalsAsync_CallsRepository()
    {
        // Arrange
        var paged = new PagedResult<HospitalListItem>
        {
            Data = new List<HospitalListItem> { new HospitalListItem { Name = "Test" } },
            Pagination = new PaginationInfo { TotalItems = 1 }
        };
        _mockRepo.Setup(r => r.GetHospitalsAsync(1, 10, null, null, null)).ReturnsAsync(paged);

        // Act
        var result = await _service.GetHospitalsAsync(1, 10, null, null, null);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Data);
        _mockRepo.Verify(r => r.GetHospitalsAsync(1, 10, null, null, null), Times.Once);
    }

    [Fact]
    public async Task GetHospitalByIdAsync_CallsRepository()
    {
        // Arrange
        var id = Guid.NewGuid();
        var details = new HospitalDetails { Id = id, Name = "Test" };
        _mockRepo.Setup(r => r.GetHospitalByIdAsync(id)).ReturnsAsync(details);

        // Act
        var result = await _service.GetHospitalByIdAsync(id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(id, result.Id);
        _mockRepo.Verify(r => r.GetHospitalByIdAsync(id), Times.Once);
    }
}
