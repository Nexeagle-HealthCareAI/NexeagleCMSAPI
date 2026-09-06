using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Services;

public class FreeTierSettingsService : IFreeTierSettingsService
{
    private readonly IFreeTierSettingsRepository _repo;

    public FreeTierSettingsService(IFreeTierSettingsRepository repo)
    {
        _repo = repo;
    }

    public async Task<FreeTierSettingsResponse> GetGlobalAsync()
    {
        var limit = await _repo.GetGlobalMonthlyLimitAsync();
        return new FreeTierSettingsResponse { GlobalMonthlyLimit = limit };
    }

    public async Task<UpdateFreeTierLimitResult> SetGlobalAsync(int monthlyLimit, string? updatedBy)
    {
        if (monthlyLimit <= 0)
            return new UpdateFreeTierLimitResult { Success = false, Message = "Monthly limit must be greater than zero." };

        await _repo.SetGlobalMonthlyLimitAsync(monthlyLimit, updatedBy);
        return new UpdateFreeTierLimitResult { Success = true, Message = "Global free-tier limit updated." };
    }

    public Task<List<HospitalFreeTierLimitItem>> GetHospitalOverridesAsync() => _repo.GetHospitalOverridesAsync();

    public Task<UpdateFreeTierLimitResult> SetHospitalOverrideAsync(Guid hospitalId, int? monthlyLimit, string? updatedBy) =>
        _repo.SetHospitalOverrideAsync(hospitalId, monthlyLimit, updatedBy);
}
