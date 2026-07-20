using CMSAPI.Application.Models;
using System;
using System.Threading.Tasks;

namespace CMSAPI.Application.Interfaces;

public interface IInsightsRepository
{
    Task<SiteVisitStats> GetSiteVisitStatsAsync(DateOnly? from, DateOnly? to);
    Task<PagedResult<PatientLoginItem>> GetPatientLoginsAsync(int page, int limit, string? search, string? sortBy, string? sortDir);
    Task<PagedResult<OnlineAppointmentItem>> GetOnlineAppointmentsAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search, string? sortBy, string? sortDir, string? source);

    Task<AuthFunnelStats> GetAuthFunnelStatsAsync(DateOnly? from, DateOnly? to);
    Task<PagedResult<AuthFunnelAttemptItem>> GetAuthFunnelAttemptsAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search);
    Task<BookingFunnelStats> GetBookingFunnelStatsAsync(DateOnly? from, DateOnly? to);
    Task<PagedResult<SearchLogItem>> GetSearchLogAsync(int page, int limit, DateOnly? from, DateOnly? to, string? search, string? sortBy, string? sortDir);
}
