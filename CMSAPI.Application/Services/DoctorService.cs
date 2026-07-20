using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using System;
using System.Threading.Tasks;

namespace CMSAPI.Application.Services;

public class DoctorService : IDoctorService
{
    private readonly IDoctorRepository _repo;

    public DoctorService(IDoctorRepository repo)
    {
        _repo = repo;
    }

    public Task<PagedResult<DoctorListItem>> GetDoctorsAsync(int page, int limit, string? search, string? sortBy, string? sortDir)
        => _repo.GetDoctorsAsync(page, limit, search, sortBy, sortDir);

    public Task<DoctorDetail?> GetDoctorDetailAsync(Guid doctorId)
        => _repo.GetDoctorDetailAsync(doctorId);

    public Task<UpdateDoctorMarketingResult> UpdateDoctorMarketingAsync(Guid doctorId, UpdateDoctorMarketingRequest request)
        => _repo.UpdateDoctorMarketingAsync(doctorId, request);
}
