using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface IMarketingRepository
{
    Task<PagedResult<DemoLoginLeadItem>> GetDemoLoginLeadsAsync(int page, int limit);
    Task<DemoLoginStats> GetDemoLoginStatsAsync();
}
