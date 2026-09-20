using System;

namespace CMSAPI.Domain.Entities;

// One row per (HospitalId, YearMonth) -- the current month's free-tier usage count. Read-only
// from CMS's side (easyHMSAPI's UsageLimitService is the only writer); shared physical table,
// both apps' AppDbContexts point at the same easyHMSDatabase catalog.
public class HospitalMonthlyUsage
{
    public Guid HospitalId { get; set; }
    public string YearMonth { get; set; } = string.Empty; // "YYYY-MM"
    public int UsedCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}
