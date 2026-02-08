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
    [Fact]
    public async Task GetHospitals_ReturnsPagedResult()
    {
        var mockService = new Mock<IHospitalService>();
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
        mockService.Setup(s => s.GetHospitalsAsync(1, 10, null, null, null)).ReturnsAsync(paged);

        var controller = new HospitalsController(mockService.Object);
        var result = await controller.GetHospitals();
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }
}
