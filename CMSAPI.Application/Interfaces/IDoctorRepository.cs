using CMSAPI.Application.Models;
using System;
using System.Threading.Tasks;

namespace CMSAPI.Application.Interfaces;

public interface IDoctorRepository
{
    Task<PagedResult<DoctorListItem>> GetDoctorsAsync(int page, int limit, string? search, string? sortBy, string? sortDir);
    Task<UpdateDoctorMarketingResult> UpdateDoctorMarketingAsync(Guid doctorId, UpdateDoctorMarketingRequest request);
}
