using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using System;
using System.Threading.Tasks;

namespace CMSAPI.Application.Services;

public class HospitalService : IHospitalService
{
    private readonly IHospitalRepository _repo;

    public HospitalService(IHospitalRepository repo)
    {
        _repo = repo;
    }

    public Task<PagedResult<HospitalListItem>> GetHospitalsAsync(int page, int limit, string? search, string? sortBy, string? sortDir)
        => _repo.GetHospitalsAsync(page, limit, search, sortBy, sortDir);

    public Task<HospitalDetails?> GetHospitalByIdAsync(Guid id)
        => _repo.GetHospitalByIdAsync(id);

    public Task<HospitalAppointmentSourceStats> GetAppointmentSourceStatsAsync(Guid hospitalId, DateOnly? from, DateOnly? to)
        => _repo.GetAppointmentSourceStatsAsync(hospitalId, from, to);
}