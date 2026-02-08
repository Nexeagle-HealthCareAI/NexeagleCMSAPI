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
            
            // --- Helper to get date range counts ---
            // We need raw dates for charts
            
            // 1. Hospitals
            var hospitals = await _db.Hospitals
                .Select(h => new { h.HospitalID, h.CreatedAt })
                .ToListAsync();

            // 2. Patients
            var patients = await _db.PatientRegistrations
                .Select(p => new { p.PatientID, p.RegisteredAt })
                .ToListAsync();

            // 3. Doctors (Joined via UserProfile for date)
            // Note: If Doctor.UserID is null, we can't determine date easily. Ignoring those for charts.
            var doctors = await (from d in _db.Doctors
                                     join u in _db.UserProfiles on d.UserID equals u.UserID
                                     select new { d.DoctorID, u.CreatedAt })
                                     .ToListAsync();

            // 4. Users (UserAuth)
            var users = await _db.UserAuths
                .Select(u => new { u.UserID, u.CreatedAt })
                .ToListAsync();

            // --- Metrics Calculation (This Week vs Last Week) ---
            var startOfThisWeek = now.AddDays(-7);
            var startOfLastWeek = now.AddDays(-14);

            var resp = new DashboardResponse();

            // Hospitals Metric
            var hospThisWeek = hospitals.Count(h => h.CreatedAt >= startOfThisWeek);
            var hospLastWeek = hospitals.Count(h => h.CreatedAt >= startOfLastWeek && h.CreatedAt < startOfThisWeek);
            resp.TotalHospitals = CalculateMetric(hospitals.Select(h => h.HospitalID).Distinct().Count(), hospThisWeek, hospLastWeek, "this week");

            // Doctors Metric
            var docThisWeek = doctors.Count(d => d.CreatedAt >= startOfThisWeek);
            var docLastWeek = doctors.Count(d => d.CreatedAt >= startOfLastWeek && d.CreatedAt < startOfThisWeek);
            resp.TotalDoctors = CalculateMetric(doctors.Select(d => d.DoctorID).Distinct().Count(), docThisWeek, docLastWeek, "this week");

            // Patients Metric ("overall" period in example, but calculation same)
            var patThisWeek = patients.Count(p => p.RegisteredAt >= startOfThisWeek);
            var patLastWeek = patients.Count(p => p.RegisteredAt >= startOfLastWeek && p.RegisteredAt < startOfThisWeek);
            resp.TotalPatients = CalculateMetric(patients.Select(p => p.PatientID).Distinct().Count(), patThisWeek, patLastWeek, "overall");

            // Users Metric
            var userThisWeek = users.Count(u => u.CreatedAt >= startOfThisWeek);
            var userLastWeek = users.Count(u => u.CreatedAt >= startOfLastWeek && u.CreatedAt < startOfThisWeek);
            resp.TotalUsers = CalculateMetric(users.Select(u => u.UserID).Distinct().Count(), userThisWeek, userLastWeek, "this week");

            // --- Charts Generation ---
            resp.Charts.Hospitals = GenerateChartData(hospitals.Select(h => h.CreatedAt).ToList(), today);
            resp.Charts.Doctors = GenerateChartData(doctors.Select(d => d.CreatedAt).ToList(), today);
            resp.Charts.Patients = GenerateChartData(patients.Select(p => p.RegisteredAt).ToList(), today);
            resp.Charts.Users = GenerateChartData(users.Select(u => u.CreatedAt).ToList(), today);

            return resp;
        }
        catch(Exception ex)
        {
            throw; // Let global handler handle it, or rethrow
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
