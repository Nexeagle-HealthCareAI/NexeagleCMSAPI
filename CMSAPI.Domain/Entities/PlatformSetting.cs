using System;

namespace CMSAPI.Domain.Entities;

// Global key-value settings, CMS-editable. Shared physical table with easyHMSAPI's own mapping
// of the same dbo.PlatformSetting rows (both apps' AppDbContexts point at the same
// easyHMSDatabase catalog) -- CMS writes FreeTierMonthlyLimit here, easyHMSAPI's
// UsageLimitService reads it.
public class PlatformSetting
{
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
