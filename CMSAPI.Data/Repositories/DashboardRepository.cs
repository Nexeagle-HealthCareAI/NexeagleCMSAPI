using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories;

public class DashboardRepository : IDashboardRepository
{
    private readonly AppDbContext _db;

    public DashboardRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardResponse> GetDashboardAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTime.UtcNow;

            // Date boundaries for the this-week / last-week metric comparison
            var startOfThisWeek = now.AddDays(-7);
            var startOfLastWeek = now.AddDays(-14);

            // Chart window: GenerateChartData shows at most 5 years of history.
            // Loading rows older than that wastes memory; filter them out at the DB level.
            var fiveYearsAgo = now.AddYears(-5);

            // ── Metrics: SQL COUNT queries (no full-table load) ───────────────────────
            var totalHospitals  = await _db.Hospitals.CountAsync();
            var hospThisWeek    = await _db.Hospitals.CountAsync(h => h.CreatedAt >= startOfThisWeek);
            var hospLastWeek    = await _db.Hospitals.CountAsync(h => h.CreatedAt >= startOfLastWeek && h.CreatedAt < startOfThisWeek);

            var totalDoctorsCount = await _db.Doctors.Select(d => d.DoctorID).Distinct().CountAsync();
            var docThisWeek  = await (from d in _db.Doctors join u in _db.UserProfiles on d.UserID equals u.UserID
                                      where u.CreatedAt >= startOfThisWeek select d.DoctorID).CountAsync();
            var docLastWeek  = await (from d in _db.Doctors join u in _db.UserProfiles on d.UserID equals u.UserID
                                      where u.CreatedAt >= startOfLastWeek && u.CreatedAt < startOfThisWeek select d.DoctorID).CountAsync();

            var totalPatients = await _db.PatientRegistrations.Select(p => p.RegistrationId).Distinct().CountAsync();
            var patThisWeek   = await _db.PatientRegistrations.CountAsync(p => p.RegisteredAt >= startOfThisWeek);
            var patLastWeek   = await _db.PatientRegistrations.CountAsync(p => p.RegisteredAt >= startOfLastWeek && p.RegisteredAt < startOfThisWeek);

            var totalUsers   = await _db.UserAuths.Select(u => u.UserID).Distinct().CountAsync();
            var userThisWeek = await _db.UserAuths.CountAsync(u => u.CreatedAt >= startOfThisWeek);
            var userLastWeek = await _db.UserAuths.CountAsync(u => u.CreatedAt >= startOfLastWeek && u.CreatedAt < startOfThisWeek);

            // ── Chart data: load only the date column, only for the last 5 years ──────
            var hospitalDates = await _db.Hospitals
                .Where(h => h.CreatedAt >= fiveYearsAgo)
                .Select(h => h.CreatedAt).ToListAsync();

            var doctorDates = await (from d in _db.Doctors
                                     join u in _db.UserProfiles on d.UserID equals u.UserID
                                     where u.CreatedAt >= fiveYearsAgo
                                     select u.CreatedAt).ToListAsync();

            var patientDates = await _db.PatientRegistrations
                .Where(p => p.RegisteredAt >= fiveYearsAgo)
                .Select(p => p.RegisteredAt).ToListAsync();

            var userDates = await _db.UserAuths
                .Where(u => u.CreatedAt >= fiveYearsAgo)
                .Select(u => u.CreatedAt).ToListAsync();

            // ── Assemble response ─────────────────────────────────────────────────────
            var resp = new DashboardResponse();

            resp.TotalHospitals = CalculateMetric(totalHospitals, hospThisWeek, hospLastWeek, "this week");
            resp.TotalDoctors   = CalculateMetric(totalDoctorsCount, docThisWeek, docLastWeek, "this week");
            resp.TotalPatients  = CalculateMetric(totalPatients, patThisWeek, patLastWeek, "overall");
            resp.TotalUsers     = CalculateMetric(totalUsers, userThisWeek, userLastWeek, "this week");

            resp.Charts.Hospitals = GenerateChartData(hospitalDates, today);
            resp.Charts.Doctors   = GenerateChartData(doctorDates,   today);
            resp.Charts.Patients  = GenerateChartData(patientDates,   today);
            resp.Charts.Users     = GenerateChartData(userDates,      today);

            return resp;
        }
        catch (Exception)
        {
            throw;
        }
    }

    private DashboardMetric CalculateMetric(int totalValue, int currentPeriodValue, int previousPeriodValue, string periodLabel)
    {
        int change = currentPeriodValue - previousPeriodValue;
        string changeType = "nochange";

        if (change > 0) changeType = "increase";
        if (change < 0) changeType = "decrease";
        
        return new DashboardMetric
        {
            Value = totalValue,
            Change = Math.Abs(change),
            ChangeType = changeType,
            Period = periodLabel
        };
    }

    private ChartData GenerateChartData(List<DateTime> dates, DateOnly today)
    {
        var data = new ChartData();
        var datesOnly = dates.Select(d => DateOnly.FromDateTime(d)).ToList();

        // Daily (Last 7 days)
        for (int i = 6; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            var val = datesOnly.Count(x => x == d);
            data.Daily.Add(new StatPoint { Label = d.DayOfWeek.ToString().Substring(0, 3), Value = val });
        }

        // Weekly (Last 4 weeks)
        for (int i = 3; i >= 0; i--)
        {
            var start = today.AddDays(-(i * 7) - 6);
            var end = today.AddDays(-(i * 7));
            var val = datesOnly.Count(x => x >= start && x <= end);
            data.Weekly.Add(new StatPoint { Label = $"Week {4 - i}", Value = val });
        }

        // Monthly (Last 12 months)
        var currentMonth = today;
        for (int i = 11; i >= 0; i--)
        {
            var monthDate = currentMonth.AddMonths(-i);
            var monthStart = new DateOnly(monthDate.Year, monthDate.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var val = datesOnly.Count(x => x >= monthStart && x <= monthEnd);
            data.Monthly.Add(new StatPoint { Label = monthDate.ToString("MMM"), Value = val });
        }

        // Yearly (Last 5 years)
        var currentYear = today.Year;
        for (int i = 4; i >= 0; i--)
        {
            var y = currentYear - i;
            var val = datesOnly.Count(x => x.Year == y);
            data.Yearly.Add(new StatPoint { Label = y.ToString(), Value = val });
        }

        return data;
    }
}
