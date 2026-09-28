using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories;

public class JobsRepository : IJobsRepository
{
    // Jobs safe to trigger on demand from CMS web: read-then-idempotently-update, no external
    // side effects (no real WhatsApp sends, no payroll-affecting attendance writes). Extend this
    // allow-list deliberately, not by default -- see RunJobNowAsync.
    private static readonly HashSet<string> TestableJobs = new(StringComparer.OrdinalIgnoreCase)
    {
        "FutureAppointmentToPresent",
    };

    private readonly AppDbContext _db;

    public JobsRepository(AppDbContext db)
    {
        _db = db;
    }

    // The container that runs these jobs has a UTC clock but the business day is IST (matches
    // easyHMSNightJob's NightJobRespository.IstToday()) -- every "today" comparison in this class
    // must go through this, never DateTime.UtcNow.Date directly.
    private static DateTime IstNow() => DateTime.UtcNow.AddMinutes(330);
    private static DateOnly IstToday() => DateOnly.FromDateTime(IstNow());

    public async Task<List<JobSettingItem>> GetJobsAsync()
    {
        var jobs = await _db.JobSettings
            .AsNoTracking()
            .OrderBy(j => j.JobName)
            .Select(j => new JobSettingItem
            {
                JobId = j.JobId,
                JobName = j.JobName ?? string.Empty,
                IsActive = j.IsActive,
                LastExecutionDateUTC = j.LastExecutionDateUTC,
            })
            .ToListAsync();

        var istToday = IstToday();
        foreach (var job in jobs)
        {
            job.RanToday = job.LastExecutionDateUTC.HasValue
                && DateOnly.FromDateTime(job.LastExecutionDateUTC.Value.AddMinutes(330)) == istToday;
            job.CanTestRun = TestableJobs.Contains(job.JobName);
        }

        return jobs;
    }

    public async Task<UpdateJobActiveResult> SetJobActiveAsync(string jobName, bool isActive, string? updatedBy)
    {
        var job = await _db.JobSettings.FirstOrDefaultAsync(j => j.JobName == jobName);
        if (job == null)
            return new UpdateJobActiveResult { Success = false, Message = $"Job '{jobName}' not found." };

        job.IsActive = isActive;
        job.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new UpdateJobActiveResult { Success = true, Message = isActive ? "Job enabled." : "Job disabled." };
    }

    public async Task<List<NightJobRunItem>> GetRecentRunsAsync(int take)
    {
        return await _db.NightJobRuns
            .AsNoTracking()
            .OrderByDescending(r => r.StartedAtUtc)
            .Take(take)
            .Select(r => new NightJobRunItem
            {
                RunId = r.RunId,
                StartedAtUtc = r.StartedAtUtc,
                CompletedAtUtc = r.CompletedAtUtc,
                DurationSeconds = r.DurationSeconds,
                Status = r.Status,
                MachineName = r.MachineName,
                Environment = r.Environment,
                Summary = r.Summary,
                Error = r.Error,
            })
            .ToListAsync();
    }

    // Executes the job's real logic immediately (against the same physical easyHMSDatabase
    // catalog the nightly container uses) and records the outcome as a NightJobRun row tagged
    // Environment="Manual (CMS)" so it's clearly distinguishable from a real cron run in the
    // history list. Deliberately does NOT touch JobSettings.LastExecutionDateUTC -- that column
    // means "the nightly cron actually ran this", and conflating a manual test with it would
    // undermine the "ran today" signal GetJobsAsync just fixed.
    public async Task<RunJobNowResult> RunJobNowAsync(string jobName, string? triggeredBy)
    {
        if (!TestableJobs.Contains(jobName))
            return new RunJobNowResult { Success = false, Message = $"'{jobName}' cannot be manually run from here." };

        var startedAtUtc = DateTime.UtcNow;
        string detail;
        try
        {
            detail = jobName switch
            {
                "FutureAppointmentToPresent" => await RunFutureAppointmentToPresentAsync(),
                _ => throw new InvalidOperationException($"'{jobName}' has no Run Now implementation."),
            };
        }
        catch (Exception ex)
        {
            await RecordManualRunAsync(startedAtUtc, "Failed", null, ex.Message, triggeredBy);
            return new RunJobNowResult { Success = false, Message = $"Job failed: {ex.Message}" };
        }

        await RecordManualRunAsync(startedAtUtc, "Success", $"{jobName}=OK: {detail}", null, triggeredBy);
        return new RunJobNowResult { Success = true, Message = detail };
    }

    private async Task<string> RunFutureAppointmentToPresentAsync()
    {
        var istToday = IstToday();
        var toUpdate = await _db.Appointments
            .Where(a => a.ApptDate == istToday
                && a.CurrentStatusCode == "FUTURE"
                && !_db.Hospitals.Any(h => h.HospitalID == a.HospitalID && h.IsArchived))
            .ToListAsync();

        if (toUpdate.Count == 0)
            return $"No FUTURE appointments found for today ({istToday:yyyy-MM-dd} IST) -- nothing to transition.";

        var now = DateTime.UtcNow;
        foreach (var appt in toUpdate)
        {
            appt.CurrentStatusCode = "VITALS_REQUIRED";
            appt.LastStatusCodeAt = now;
        }
        await _db.SaveChangesAsync();

        return $"{toUpdate.Count} appointment(s) transitioned FUTURE → VITALS_REQUIRED for today ({istToday:yyyy-MM-dd} IST).";
    }

    private async Task RecordManualRunAsync(DateTime startedAtUtc, string status, string? summary, string? error, string? triggeredBy)
    {
        _db.NightJobRuns.Add(new NightJobRun
        {
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTime.UtcNow,
            DurationSeconds = (int)(DateTime.UtcNow - startedAtUtc).TotalSeconds,
            Status = status,
            MachineName = string.IsNullOrWhiteSpace(triggeredBy) ? "CMS Web" : $"CMS Web ({triggeredBy})",
            Environment = "Manual (CMS)",
            Summary = summary,
            Error = error,
        });
        await _db.SaveChangesAsync();
    }
}
