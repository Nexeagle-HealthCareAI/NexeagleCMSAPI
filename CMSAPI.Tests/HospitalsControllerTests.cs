using Xunit;
using CMSAPI.Controllers;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class HospitalsControllerTests
{
    private readonly Mock<IHospitalService> _mockService;
    private readonly HospitalsController _controller;

    public HospitalsControllerTests()
    {
        _mockService = new Mock<IHospitalService>();
        _controller = new HospitalsController(_mockService.Object);
    }

    [Fact]
    public async Task GetHospitals_ReturnsPagedResult()
    {
        // Arrange
        var paged = new PagedResult<HospitalListItem>
        {
            Data = new List<HospitalListItem> { new HospitalListItem { Id = Guid.NewGuid(), Name = "Test", ContactNumber = "123" } },
            Pagination = new PaginationInfo
            {
                CurrentPage = 1,
                TotalPages = 1,
                TotalItems = 1,
                ItemsPerPage = 10
            }
        };
        _mockService.Setup(s => s.GetHospitalsAsync(1, 10, null, null, null)).ReturnsAsync(paged);

        // Act
        var result = await _controller.GetHospitals(1, 10, null, null, null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnData = Assert.IsType<PagedResult<HospitalListItem>>(okResult.Value);
        Assert.Single(returnData.Data);
        Assert.Equal(1, returnData.Pagination.TotalItems);
    }

    [Fact]
    public async Task GetHospitalById_WithValidId_ReturnsOkResult()
    {
        // Arrange
        var id = Guid.NewGuid();
        var hospital = new HospitalDetails { Id = id, Name = "Test Hospital", Email = "test@hospital.com" };
        _mockService.Setup(s => s.GetHospitalByIdAsync(id)).ReturnsAsync(hospital);

        // Act
        var result = await _controller.GetHospitalById(id);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnHospital = Assert.IsType<HospitalDetails>(okResult.Value);
        Assert.Equal(id, returnHospital.Id);
        Assert.Equal("Test Hospital", returnHospital.Name);
    }

    [Fact]
    public async Task GetHospitalById_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockService.Setup(s => s.GetHospitalByIdAsync(id)).ReturnsAsync((HospitalDetails?)null);

        // Act
        var result = await _controller.GetHospitalById(id);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }
}
