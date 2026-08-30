using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Services;

public class MarketingService : IMarketingService
{
    private readonly IMarketingRepository _repo;

    public MarketingService(IMarketingRepository repo)
    {
        _repo = repo;
    }

    public Task<PagedResult<DemoLoginLeadItem>> GetDemoLoginLeadsAsync(int page, int limit)
        => _repo.GetDemoLoginLeadsAsync(page, limit);

    public Task<DemoLoginStats> GetDemoLoginStatsAsync()
        => _repo.GetDemoLoginStatsAsync();
}
