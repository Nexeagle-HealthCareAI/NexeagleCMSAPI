using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories;

public class FreeTierSettingsRepository : IFreeTierSettingsRepository
{
    private const string GlobalLimitSettingKey = "FreeTierMonthlyLimit";
    private const int FallbackLimit = 100;

    private readonly AppDbContext _db;

    public FreeTierSettingsRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetGlobalMonthlyLimitAsync()
    {
        var value = await _db.PlatformSettings
            .AsNoTracking()
            .Where(s => s.SettingKey == GlobalLimitSettingKey)
            .Select(s => s.SettingValue)
            .FirstOrDefaultAsync();

        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : FallbackLimit;
    }

    public async Task SetGlobalMonthlyLimitAsync(int monthlyLimit, string? updatedBy)
    {
        var existing = await _db.PlatformSettings.FirstOrDefaultAsync(s => s.SettingKey == GlobalLimitSettingKey);
        var now = DateTime.UtcNow;
        if (existing == null)
        {
            _db.PlatformSettings.Add(new PlatformSetting
            {
                SettingKey = GlobalLimitSettingKey,
                SettingValue = monthlyLimit.ToString(),
                UpdatedAt = now,
                UpdatedBy = updatedBy,
            });
        }
        else
        {
            existing.SettingValue = monthlyLimit.ToString();
            existing.UpdatedAt = now;
            existing.UpdatedBy = updatedBy;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<List<HospitalFreeTierLimitItem>> GetHospitalOverridesAsync()
    {
        var globalLimit = await GetGlobalMonthlyLimitAsync();

        var overrides = await _db.HospitalFreeTierLimits.AsNoTracking().ToListAsync();
        var overrideByHospitalId = overrides.ToDictionary(o => o.HospitalId, o => o.MonthlyLimit);

        var hospitals = await _db.Hospitals
            .AsNoTracking()
            .Where(h => !h.IsArchived)
            .Select(h => new { h.HospitalID, h.Name })
            .OrderBy(h => h.Name)
            .ToListAsync();

        return hospitals.Select(h => new HospitalFreeTierLimitItem
        {
            HospitalId = h.HospitalID,
            HospitalName = h.Name,
            MonthlyLimit = overrideByHospitalId.TryGetValue(h.HospitalID, out var ov) ? ov : null,
            EffectiveLimit = overrideByHospitalId.TryGetValue(h.HospitalID, out var ov2) ? ov2 : globalLimit,
        }).ToList();
    }

    public async Task<UpdateFreeTierLimitResult> SetHospitalOverrideAsync(Guid hospitalId, int? monthlyLimit, string? updatedBy)
    {
        var existing = await _db.HospitalFreeTierLimits.FirstOrDefaultAsync(o => o.HospitalId == hospitalId);

        if (monthlyLimit == null)
        {
            if (existing != null)
            {
                _db.HospitalFreeTierLimits.Remove(existing);
                await _db.SaveChangesAsync();
            }
            return new UpdateFreeTierLimitResult { Success = true, Message = "Reverted to the global default." };
        }

        if (monthlyLimit.Value < 0)
            return new UpdateFreeTierLimitResult { Success = false, Message = "Monthly limit cannot be negative." };

        var now = DateTime.UtcNow;
        if (existing == null)
        {
            _db.HospitalFreeTierLimits.Add(new HospitalFreeTierLimit
            {
                HospitalId = hospitalId,
                MonthlyLimit = monthlyLimit.Value,
                UpdatedAt = now,
                UpdatedBy = updatedBy,
            });
        }
        else
        {
            existing.MonthlyLimit = monthlyLimit.Value;
            existing.UpdatedAt = now;
            existing.UpdatedBy = updatedBy;
        }

        await _db.SaveChangesAsync();
        return new UpdateFreeTierLimitResult { Success = true, Message = "Override saved." };
    }
}
