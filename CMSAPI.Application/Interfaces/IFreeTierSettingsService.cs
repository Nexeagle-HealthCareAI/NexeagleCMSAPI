using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

public interface IFreeTierSettingsService
{
    Task<FreeTierSettingsResponse> GetGlobalAsync();
    Task<UpdateFreeTierLimitResult> SetGlobalAsync(int monthlyLimit, string? updatedBy);
    Task<List<HospitalFreeTierLimitItem>> GetHospitalOverridesAsync();
    Task<UpdateFreeTierLimitResult> SetHospitalOverrideAsync(Guid hospitalId, int? monthlyLimit, string? updatedBy);
}
