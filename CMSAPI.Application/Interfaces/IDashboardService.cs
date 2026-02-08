using CMSAPI.Application.Models;
using System.Threading.Tasks;

namespace CMSAPI.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardResponse> GetDashboardAsync();
}
