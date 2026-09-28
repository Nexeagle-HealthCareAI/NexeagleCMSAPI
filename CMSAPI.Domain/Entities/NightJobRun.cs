using System;

namespace CMSAPI.Domain.Entities;

// Mirrors easyHMSNightJob's dbo.NightJobRuns row -- one row per overall night-job execution,
// never auto-deleted, so this is "which runs happened and when" for the admin Jobs page.
public class NightJobRun
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
