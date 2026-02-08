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
        // Basic metrics. For change and period values we return placeholders because computing historical change
        // requires additional data (e.g. previous period counts). Replace these with real calculations as needed.
        var hospitalsCount = await _db.Hospitals.CountAsync();
        var doctorsCount = await _db.Doctors.CountAsync();
        var patientsCount = await _db.PatientRegistrations.CountAsync();

        return new DashboardResponse
        {
            TotalHospitals = new DashboardMetric { Value = hospitalsCount, Change = 0, ChangeType = "nochange", Period = "this week" },
            TotalDoctors = new DashboardMetric { Value = doctorsCount, Change = 0, ChangeType = "nochange", Period = "this week" },
            TotalPatients = new DashboardMetric { Value = patientsCount, Change = 0, ChangeType = "nochange", Period = "overall" }
        };
    }
}
