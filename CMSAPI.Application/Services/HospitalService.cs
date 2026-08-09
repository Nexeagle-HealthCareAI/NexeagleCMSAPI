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

    public Task<PagedResult<HospitalListItem>> GetHospitalsAsync(int page, int limit, string? search, string? sortBy, string? sortDir, string? status = null, string? subscriptionStatus = null, bool includeArchived = false)
        => _repo.GetHospitalsAsync(page, limit, search, sortBy, sortDir, status, subscriptionStatus, includeArchived);

    public Task<HospitalDetails?> GetHospitalByIdAsync(Guid id)
        => _repo.GetHospitalByIdAsync(id);

    public Task<HospitalAppointmentSourceStats> GetAppointmentSourceStatsAsync(Guid hospitalId, DateOnly? from, DateOnly? to)
        => _repo.GetAppointmentSourceStatsAsync(hospitalId, from, to);

    public Task<bool> ArchiveHospitalAsync(Guid id, Guid archivedByUserId)
        => _repo.ArchiveHospitalAsync(id, archivedByUserId);

    public Task<bool> RestoreHospitalAsync(Guid id)
        => _repo.RestoreHospitalAsync(id);
}