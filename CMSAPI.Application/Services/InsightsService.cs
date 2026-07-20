using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using System;
using System.Threading.Tasks;

namespace CMSAPI.Application.Services;

public class InsightsService : IInsightsService
{
    private readonly IInsightsRepository _repo;

    public InsightsService(IInsightsRepository repo)
    {
        _repo = repo;
    }

    public Task<SiteVisitStats> GetSiteVisitStatsAsync(DateOnly? from, DateOnly? to)
        => _repo.GetSiteVisitStatsAsync(from, to);

    public Task<PagedResult<PatientLoginItem>> GetPatientLoginsAsync(int page, int limit, string? search, string? sortBy, string? sortDir)
        => _repo.GetPatientLoginsAsync(page, limit, search, sortBy, sortDir);

    public Task<PagedResult<OnlineAppointmentItem>> GetOnlineAppointmentsAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search, string? sortBy, string? sortDir, string? source)
        => _repo.GetOnlineAppointmentsAsync(page, limit, from, to, search, sortBy, sortDir, source);
}
