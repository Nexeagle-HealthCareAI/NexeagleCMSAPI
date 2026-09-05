using System;

namespace CMSAPI.Domain.Entities;

// Per-hospital override of PlatformSetting's FreeTierMonthlyLimit -- absent row means "use the
// global default". CMS-editable; easyHMSAPI's UsageLimitService reads it.
public class HospitalFreeTierLimit
{
    public Guid HospitalId { get; set; }
    public int MonthlyLimit { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
