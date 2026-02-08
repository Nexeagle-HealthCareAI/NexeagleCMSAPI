using Xunit;
using CMSAPI.Application.Services;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Moq;
using System.Threading.Tasks;

public class DashboardServiceTests
{
    private readonly Mock<IDashboardRepository> _mockRepo;
    private readonly DashboardService _service;

    public DashboardServiceTests()
    {
        _mockRepo = new Mock<IDashboardRepository>();
        _service = new DashboardService(_mockRepo.Object);
    }

    [Fact]
    public async Task GetDashboardAsync_CallsRepository()
    {
        // Arrange
        var stats = new DashboardResponse { TotalHospitals = new DashboardMetric { Value = 10 } };
        _mockRepo.Setup(r => r.GetDashboardAsync()).ReturnsAsync(stats);

        // Act
        var result = await _service.GetDashboardAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.TotalHospitals.Value);
        _mockRepo.Verify(r => r.GetDashboardAsync(), Times.Once);
    }
}
