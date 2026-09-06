using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface IFreeTierSettingsRepository
{
    Task<int> GetGlobalMonthlyLimitAsync();
    Task SetGlobalMonthlyLimitAsync(int monthlyLimit, string? updatedBy);
    Task<List<HospitalFreeTierLimitItem>> GetHospitalOverridesAsync();
    Task<UpdateFreeTierLimitResult> SetHospitalOverrideAsync(Guid hospitalId, int? monthlyLimit, string? updatedBy);
}
