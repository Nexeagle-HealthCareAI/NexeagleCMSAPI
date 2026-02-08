using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using System.Threading.Tasks;

namespace CMSAPI.Application.Services;

public class DashboardService : IDashboardService
{
    private readonly IDashboardRepository _repo;

    public DashboardService(IDashboardRepository repo)
    {
        _repo = repo;
    }

    public Task<DashboardResponse> GetDashboardAsync() => _repo.GetDashboardAsync();
}
