using System;

namespace CMSAPI.Application.Models;

public class JobSettingItem
{
    public long JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastExecutionDateUTC { get; set; }
    // Computed server-side in IST (Last run's calendar day == today in IST) -- LastExecutionDateUTC
    // only advances when the job actually did its work (see easyHMSNightJob's Executor.cs), so this
    // is a truthful "did it really run today" signal, not just "did the container reach this line".
    public bool RanToday { get; set; }
    // Whether this job supports the manual "Run Now" test trigger (see JobsRepository.RunJobNowAsync
    // for the allow-list and why some jobs -- real WhatsApp sends, payroll-affecting attendance
    // writes -- are deliberately excluded).
    public bool CanTestRun { get; set; }
}

public class UpdateJobActiveRequest
{
    public bool IsActive { get; set; }
}

public class UpdateJobActiveResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class NightJobRunItem
{
    public long RunId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? DurationSeconds { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? MachineName { get; set; }
    public string? Environment { get; set; }
    public string? Summary { get; set; }
    public string? Error { get; set; }
}

public class RunJobNowResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}
