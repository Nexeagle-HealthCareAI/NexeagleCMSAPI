using System;

namespace CMSAPI.Domain.Entities;

// Mirrors easyHMSNightJob's dbo.JobSettings row -- same physical easyHMSDatabase catalog as
// CMSAPI's own AppDbContext (see FreeTierSettingsController's doc comment), so IsActive can be
// read/toggled here without an HTTP call to another API. IsActive is the per-job on/off switch
// NightJobRespository.IsProcessEligibleToRun checks before running a gated job.
public class JobSetting
{
    public long JobId { get; set; }
    public string? JobName { get; set; }
    public DateTime? LastExecutionDateUTC { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
