using Xunit;
using CMSAPI.Controllers;
using CMSAPI.Application.Models;
using CMSAPI.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class DashboardControllerTests
{
    private readonly Mock<IDashboardService> _mockService;
    private readonly DashboardController _controller;

    public DashboardControllerTests()
    {
        _mockService = new Mock<IDashboardService>();
        _controller = new DashboardController(_mockService.Object);
    }

    [Fact]
    public async Task GetStats_ReturnsOkResult_WithDashboardResponse()
    {
        // Arrange
        var response = new DashboardResponse
        {
            TotalHospitals = new DashboardMetric { Value = 10, Change = 5, ChangeType = "increase", Period = "last month" },
            TotalDoctors = new DashboardMetric { Value = 50, Change = 2, ChangeType = "increase", Period = "last month" },
            TotalPatients = new DashboardMetric { Value = 200, Change = 10, ChangeType = "increase", Period = "last month" },
            TotalUsers = new DashboardMetric { Value = 300, Change = 15, ChangeType = "increase", Period = "last month" },
            Charts = new DashboardCharts()
        };
        _mockService.Setup(s => s.GetDashboardAsync()).ReturnsAsync(response);

        // Act
        var result = await _controller.GetStats();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnStats = Assert.IsType<DashboardResponse>(okResult.Value);
        Assert.Equal(10, returnStats.TotalHospitals.Value);
        Assert.Equal(50, returnStats.TotalDoctors.Value);
        Assert.Equal("increase", returnStats.TotalHospitals.ChangeType);
        Assert.Equal("increase", returnStats.TotalHospitals.ChangeType);
    }



    [Fact]
    public async Task GetStats_ReturnsInternalServerError_OnException()
    {
        // Arrange
        _mockService.Setup(s => s.GetDashboardAsync()).ThrowsAsync(new System.Exception("Database error"));

        // Act & Assert
        await Assert.ThrowsAsync<System.Exception>(() => _controller.GetStats());
    }
}
