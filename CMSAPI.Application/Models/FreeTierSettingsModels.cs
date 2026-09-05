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
