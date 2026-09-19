using System;

namespace CMSAPI.Application.Models;

public class FreeTierSettingsResponse
{
    public int GlobalMonthlyLimit { get; set; }
}

public class UpdateGlobalFreeTierLimitRequest
{
    public int MonthlyLimit { get; set; }
}

public class HospitalFreeTierLimitItem
{
    public Guid HospitalId { get; set; }
    public string? HospitalName { get; set; }
    // null = no override -- this hospital uses the global default.
    public int? MonthlyLimit { get; set; }
    public int EffectiveLimit { get; set; }
    // Current calendar month's usage count -- only meaningful when IsGated is true.
    public int UsedCount { get; set; }
    // HospitalSubscriptions.Status ("Trial"/missing row, or any paid status e.g. "Active").
    public string SubscriptionStatus { get; set; } = "Trial";
    // Same rule as UsageLimitService.IsGatedAsync -- only a Trial (or subscription-less)
    // hospital is actually subject to EffectiveLimit; a paid plan has no cap regardless of it.
    public bool IsGated { get; set; } = true;
}

public class UpdateHospitalFreeTierLimitRequest
{
    // null clears any existing override, reverting this hospital to the global default.
    public int? MonthlyLimit { get; set; }
}

public class UpdateFreeTierLimitResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}
