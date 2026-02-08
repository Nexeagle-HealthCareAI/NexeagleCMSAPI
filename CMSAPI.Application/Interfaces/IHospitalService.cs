using CMSAPI.Application.Models;
using System.Threading.Tasks;

namespace CMSAPI.Application.Interfaces;

public interface IHospitalService
{
    Task<PagedResult<HospitalListItem>> GetHospitalsAsync(int page, int limit, string? search, string? sortBy, string? sortDir);
    Task<HospitalDetails?> GetHospitalByIdAsync(Guid id);
}
