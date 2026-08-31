using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CMSAPI.Application.Services;
using CMSAPI.Data;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Services;

public class SubscriptionApprovalService : ISubscriptionApprovalService
{
    private readonly AppDbContext _appDb;
    private readonly CmsDbContext _cmsDb;

    // Use a keyed lock to prevent concurrent redemptions of the same referral code
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _referralLocks = new(StringComparer.OrdinalIgnoreCase);

    public SubscriptionApprovalService(AppDbContext appDb, CmsDbContext cmsDb)
    {
        _appDb = appDb;
        _cmsDb = cmsDb;
    }

    public async Task<ApprovalResult> ApprovePaymentAsync(Guid hospitalId)
    {
        var sub = await _appDb.HospitalSubscriptions.FirstOrDefaultAsync(hs => hs.HospitalId == hospitalId);
        if (sub == null) return new ApprovalResult { ErrorMessage = "Hospital subscription not found." };

        if (!sub.PlanId.HasValue)
            return new ApprovalResult { ErrorMessage = "Hospital has not selected a plan." };

        if (sub.Status != "PendingApproval")
            return new ApprovalResult { ErrorMessage = $"There is no pending payment to approve for this hospital (current status: {sub.Status})." };

        var easyHmsPlan = await _cmsDb.EasyHmsSubscriptionPlans.FirstOrDefaultAsync(p => p.PlanId == sub.PlanId.Value);
        var legacyPlan = easyHmsPlan == null
            ? await _cmsDb.SubscriptionPlans.FirstOrDefaultAsync(p => p.PlanId == sub.PlanId.Value)
            : null;

        if (easyHmsPlan == null && legacyPlan == null)
            return new ApprovalResult { ErrorMessage = "Invalid plan selected by hospital." };

        if (easyHmsPlan != null && (easyHmsPlan.MaxDoctors.HasValue || easyHmsPlan.MaxBeds.HasValue))
        {
            var overLimitIssues = new List<string>();

            if (easyHmsPlan.MaxDoctors.HasValue)
            {
                var currentDoctorCount = await _appDb.Doctors
                    .Where(d => d.HospitalID == hospitalId)
                    .Join(_appDb.Users, d => d.UserID, u => u.UserID, (d, u) => u)
                    .CountAsync(u => u.UserStatusId != 3); // 3 = UserStatusEnum.Revoked

                if (currentDoctorCount > easyHmsPlan.MaxDoctors.Value)
                    overLimitIssues.Add($"{currentDoctorCount} doctors (this plan allows {easyHmsPlan.MaxDoctors.Value})");
            }

            if (easyHmsPlan.MaxBeds.HasValue)
            {
                var currentBedCount = await _appDb.BedMaster.CountAsync(b => b.HospitalId == hospitalId && b.IsActive);
                if (currentBedCount > easyHmsPlan.MaxBeds.Value)
                    overLimitIssues.Add($"{currentBedCount} beds (this plan allows {easyHmsPlan.MaxBeds.Value})");
            }

            if (overLimitIssues.Count > 0)
            {
                return new ApprovalResult
                {
                    ErrorMessage = $"Cannot activate this plan — the hospital currently has {string.Join(" and ", overLimitIssues)}. Ask them to reduce their count first, or choose a higher tier."
                };
            }
        }

        var billingCycle = easyHmsPlan?.BillingCycle ?? legacyPlan!.BillingCycle;

        sub.Status = "Active";
        sub.SubscriptionStartDate = DateTime.UtcNow;

        sub.SubscriptionEndDate = billingCycle.ToLowerInvariant() switch
        {
            "yearly" => DateTime.UtcNow.AddYears(1),
            "half-yearly" => DateTime.UtcNow.AddMonths(6),
            "quarterly" => DateTime.UtcNow.AddMonths(3),
            _ => DateTime.UtcNow.AddMonths(1)
        };

        sub.NextBillingDate = sub.SubscriptionEndDate;
        sub.MaxDoctors = easyHmsPlan?.MaxDoctors;
        sub.MaxBeds = easyHmsPlan?.MaxBeds;
        sub.RejectionReason = null;
        sub.RejectedAt = null;
        sub.UpdatedAt = DateTime.UtcNow;

        ReferralCode? referralCode = null;
        var needsReferralCheck = billingCycle.Equals("yearly", StringComparison.OrdinalIgnoreCase) 
                                 && !string.IsNullOrEmpty(sub.ReferralCode) 
                                 && sub.ReferralCodeRedeemedAt == null;

        SemaphoreSlim? referralLock = null;
        if (needsReferralCheck)
        {
            referralLock = _referralLocks.GetOrAdd(sub.ReferralCode!, _ => new SemaphoreSlim(1, 1));
            await referralLock.WaitAsync();
        }

        try
        {
            if (needsReferralCheck)
            {
                referralCode = await _cmsDb.ReferralCodes.FirstOrDefaultAsync(r => r.Code == sub.ReferralCode);
                if (referralCode != null && (referralCode.RedeemedByHospitalId == null || referralCode.RedeemedByHospitalId == hospitalId))
                {
                    if (string.Equals(sub.ReferralCodeRewardKind, "ExtraMonths", StringComparison.OrdinalIgnoreCase) && sub.ReferralCodeRewardValue.HasValue)
                    {
                        sub.SubscriptionEndDate = sub.SubscriptionEndDate!.Value.AddMonths((int)sub.ReferralCodeRewardValue.Value);
                        sub.NextBillingDate = sub.SubscriptionEndDate;
                    }
                    sub.ReferralCodeRedeemedAt = DateTime.UtcNow;
                }
                else
                {
                    referralCode = null;
                }
            }

            var planName = easyHmsPlan?.Name ?? legacyPlan?.Name;
            var latestPayment = await _appDb.HospitalSubscriptionPayments
                .Where(p => p.HospitalId == hospitalId && p.Status == "PendingApproval")
                .OrderByDescending(p => p.SubmittedAt)
                .FirstOrDefaultAsync();
            
            if (latestPayment != null)
            {
                latestPayment.Status = "Approved";
                latestPayment.ReviewedAt = DateTime.UtcNow;
                latestPayment.PlanName = planName;
            }

            await _appDb.SaveChangesAsync();

            if (referralCode != null && sub.ReferralCodeRedeemedAt != null && referralCode.RedeemedByHospitalId == null)
            {
                referralCode.RedeemedByHospitalId = hospitalId;
                referralCode.RedeemedAt = sub.ReferralCodeRedeemedAt;
                await _cmsDb.SaveChangesAsync();
            }

            return new ApprovalResult
            {
                Success = true,
                SubscriptionEndDate = sub.SubscriptionEndDate
            };
        }
        finally
        {
            if (referralLock != null)
            {
                referralLock.Release();
            }
        }
    }

    public async Task<ApprovalResult> RejectPaymentAsync(Guid hospitalId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return new ApprovalResult { ErrorMessage = "A reason is required to reject a payment." };

        var sub = await _appDb.HospitalSubscriptions.FirstOrDefaultAsync(hs => hs.HospitalId == hospitalId);
        if (sub == null) return new ApprovalResult { ErrorMessage = "Hospital subscription not found." };

        if (sub.Status != "PendingApproval")
            return new ApprovalResult { ErrorMessage = $"There is no pending payment to reject for this hospital (current status: {sub.Status})." };

        sub.Status = "Rejected";
        sub.RejectionReason = reason.Trim();
        sub.RejectedAt = DateTime.UtcNow;
        sub.UpdatedAt = DateTime.UtcNow;

        var latestPayment = await _appDb.HospitalSubscriptionPayments
            .Where(p => p.HospitalId == hospitalId && p.Status == "PendingApproval")
            .OrderByDescending(p => p.SubmittedAt)
            .FirstOrDefaultAsync();
        
        if (latestPayment != null)
        {
            latestPayment.Status = "Rejected";
            latestPayment.ReviewedAt = DateTime.UtcNow;
            latestPayment.RejectionReason = reason.Trim();
        }

        await _appDb.SaveChangesAsync();

        return new ApprovalResult { Success = true };
    }
}
