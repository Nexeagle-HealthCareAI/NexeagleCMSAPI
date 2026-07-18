using CMSAPI.Data;
using CMSAPI.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize] // Assuming CMS admin auth is required
    public class SubscriptionApprovalController : ControllerBase
    {
        private readonly AppDbContext _appDb;
        private readonly CmsDbContext _cmsDb;

        public SubscriptionApprovalController(AppDbContext appDb, CmsDbContext cmsDb)
        {
            _appDb = appDb;
            _cmsDb = cmsDb;
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingApprovals()
        {
            // Only hospitals that have actually submitted a payment reference for review — Trial/
            // Pending (plan picked, not yet paid) and Expired rows have nothing for an admin to
            // verify or act on here, and would otherwise clutter the queue.
            var pending = await _appDb.HospitalSubscriptions
                .Include(hs => hs.Hospital)
                .Where(hs => hs.Status == "PendingApproval" && hs.PlanId != null)
                .OrderByDescending(hs => hs.UpdatedAt)
                .Select(hs => new
                {
                    hs.HospitalSubscriptionId,
                    hs.HospitalId,
                    HospitalName = hs.Hospital != null ? hs.Hospital.Name : "",
                    hs.PlanId,
                    hs.Status,
                    hs.TrialStartDate,
                    hs.TrialEndDate,
                    hs.SubscriptionEndDate,
                    hs.PaymentAmount,
                    hs.PaymentReference,
                    hs.PaymentMode,
                    hs.PaymentDate
                })
                .ToListAsync();

            // HospitalSubscription.PlanId can reference either catalog: the dedicated EasyHMS one
            // (the expected case going forward -- HospitalSubscription/Hospital are EasyHMS
            // entities) or the legacy shared (1Rad) one, for any older rows created before the
            // split. Check both and prefer the EasyHMS match.
            var planIds = pending.Where(p => p.PlanId.HasValue).Select(p => p.PlanId!.Value).Distinct().ToList();
            var easyHmsPlans = await _cmsDb.EasyHmsSubscriptionPlans.Where(p => planIds.Contains(p.PlanId)).ToDictionaryAsync(p => p.PlanId, p => p.Name);
            var legacyPlans = await _cmsDb.SubscriptionPlans.Where(p => planIds.Contains(p.PlanId)).ToDictionaryAsync(p => p.PlanId, p => new { p.Name, p.ApplicationName });

            // Each hospital in `pending` has at most one live PendingApproval row in the payment
            // log (older ones get marked Superseded when a new submission comes in) — pull it in
            // so the queue can flag "this is a prorated mid-cycle switch" before an admin approves.
            var pendingHospitalIds = pending.Select(p => p.HospitalId).ToList();
            var latestPayments = await _appDb.HospitalSubscriptionPayments
                .Where(p => pendingHospitalIds.Contains(p.HospitalId) && p.Status == "PendingApproval")
                .ToDictionaryAsync(p => p.HospitalId, p => new { p.IsProratedSwitch, p.PreviousPlanName, p.ProratedCreditAmount });

            var result = pending.Select(p =>
            {
                string planName = "Unknown";
                string applicationName = "EasyHMS";
                if (p.PlanId.HasValue && easyHmsPlans.TryGetValue(p.PlanId.Value, out var easyHmsName))
                {
                    planName = easyHmsName;
                }
                else if (p.PlanId.HasValue && legacyPlans.TryGetValue(p.PlanId.Value, out var legacyPlan))
                {
                    planName = legacyPlan.Name;
                    applicationName = legacyPlan.ApplicationName;
                }
                latestPayments.TryGetValue(p.HospitalId, out var proration);

                return new
                {
                    p.HospitalSubscriptionId,
                    p.HospitalId,
                    p.HospitalName,
                    p.PlanId,
                    PlanName = planName,
                    ApplicationName = applicationName,
                    p.Status,
                    p.TrialStartDate,
                    p.TrialEndDate,
                    p.SubscriptionEndDate,
                    p.PaymentAmount,
                    p.PaymentReference,
                    p.PaymentMode,
                    p.PaymentDate,
                    IsProratedSwitch = proration?.IsProratedSwitch ?? false,
                    PreviousPlanName = proration?.PreviousPlanName,
                    ProratedCreditAmount = proration?.ProratedCreditAmount
                };
            }).ToList();

            return Ok(result);
        }

        // Every payment ever submitted (PendingApproval/Approved/Rejected) across all hospitals —
        // the audit trail behind "pending", sourced from the same append-only table the hospital's
        // own Payment History view reads from.
        [HttpGet("history")]
        public async Task<IActionResult> GetApprovalHistory()
        {
            var payments = await _appDb.HospitalSubscriptionPayments
                .OrderByDescending(p => p.SubmittedAt)
                .Select(p => new
                {
                    p.PaymentId,
                    p.HospitalId,
                    p.PlanId,
                    p.PlanName,
                    p.Amount,
                    p.Reference,
                    p.PaymentMode,
                    p.Status,
                    p.SubmittedAt,
                    p.ReviewedAt,
                    p.RejectionReason,
                    p.IsProratedSwitch,
                    p.PreviousPlanName,
                    p.ProratedCreditAmount
                })
                .ToListAsync();

            var hospitalIds = payments.Select(p => p.HospitalId).Distinct().ToList();
            var hospitalNames = await _appDb.Hospitals
                .Where(h => hospitalIds.Contains(h.HospitalID))
                .ToDictionaryAsync(h => h.HospitalID, h => h.Name);

            var planIds = payments.Where(p => p.PlanId.HasValue).Select(p => p.PlanId!.Value).Distinct().ToList();
            var easyHmsPlans = await _cmsDb.EasyHmsSubscriptionPlans.Where(p => planIds.Contains(p.PlanId)).ToDictionaryAsync(p => p.PlanId, p => p.Name);
            var legacyPlans = await _cmsDb.SubscriptionPlans.Where(p => planIds.Contains(p.PlanId)).ToDictionaryAsync(p => p.PlanId, p => new { p.Name, p.ApplicationName });

            var result = payments.Select(p =>
            {
                string applicationName = "EasyHMS";
                string? resolvedPlanName = null;
                if (p.PlanId.HasValue && easyHmsPlans.TryGetValue(p.PlanId.Value, out var easyHmsName))
                {
                    resolvedPlanName = easyHmsName;
                }
                else if (p.PlanId.HasValue && legacyPlans.TryGetValue(p.PlanId.Value, out var legacyPlan))
                {
                    resolvedPlanName = legacyPlan.Name;
                    applicationName = legacyPlan.ApplicationName;
                }

                return new
                {
                    p.PaymentId,
                    p.HospitalId,
                    HospitalName = hospitalNames.TryGetValue(p.HospitalId, out var name) ? name : "",
                    p.PlanId,
                    PlanName = p.PlanName ?? resolvedPlanName ?? "Unknown",
                    ApplicationName = applicationName,
                    p.Amount,
                    p.Reference,
                    p.PaymentMode,
                    p.Status,
                    p.SubmittedAt,
                    p.ReviewedAt,
                    p.RejectionReason,
                    p.IsProratedSwitch,
                    p.PreviousPlanName,
                    p.ProratedCreditAmount
                };
            }).ToList();

            return Ok(result);
        }

        [HttpPost("{hospitalId}/approve")]
        public async Task<IActionResult> ApprovePayment(Guid hospitalId)
        {
            var sub = await _appDb.HospitalSubscriptions.FirstOrDefaultAsync(hs => hs.HospitalId == hospitalId);
            if (sub == null) return NotFound("Hospital subscription not found.");

            if (!sub.PlanId.HasValue)
                return BadRequest("Hospital has not selected a plan.");

            if (sub.Status != "PendingApproval")
                return BadRequest($"There is no pending payment to approve for this hospital (current status: {sub.Status}).");

            // Prefer the dedicated EasyHMS catalog; fall back to the legacy shared (1Rad) one for
            // any older rows created before the split.
            var easyHmsPlan = await _cmsDb.EasyHmsSubscriptionPlans.FirstOrDefaultAsync(p => p.PlanId == sub.PlanId.Value);
            var legacyPlan = easyHmsPlan == null
                ? await _cmsDb.SubscriptionPlans.FirstOrDefaultAsync(p => p.PlanId == sub.PlanId.Value)
                : null;

            if (easyHmsPlan == null && legacyPlan == null)
                return BadRequest("Invalid plan selected by hospital.");

            // A downgrade (new plan's limits below what the hospital already has) would leave the
            // hospital permanently over its own cap the moment this activates. Block it up front
            // rather than silently activating and letting easyHMSAPI's enforcement only catch the
            // *next* doctor/bed the hospital tries to add. Legacy (1Rad) plans have no doctor/bed
            // concept at all, so there's nothing to check for those.
            if (easyHmsPlan != null && (easyHmsPlan.MaxDoctors.HasValue || easyHmsPlan.MaxBeds.HasValue))
            {
                var overLimitIssues = new List<string>();

                if (easyHmsPlan.MaxDoctors.HasValue)
                {
                    // Doctor has no active/inactive flag of its own — exclude revoked user
                    // accounts the same way easyHMSAPI's SubscriptionLimitHelper does.
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
                    return BadRequest(new
                    {
                        message = $"Cannot activate this plan — the hospital currently has {string.Join(" and ", overLimitIssues)}. " +
                                   "Ask them to reduce their count first, or choose a higher tier."
                    });
                }
            }

            var billingCycle = easyHmsPlan?.BillingCycle ?? legacyPlan!.BillingCycle;

            // Activate subscription
            sub.Status = "Active";
            sub.SubscriptionStartDate = DateTime.UtcNow;

            sub.SubscriptionEndDate = billingCycle.ToLowerInvariant() switch
            {
                "yearly" => DateTime.UtcNow.AddYears(1),
                "half-yearly" => DateTime.UtcNow.AddMonths(6),
                "quarterly" => DateTime.UtcNow.AddMonths(3),
                _ => DateTime.UtcNow.AddMonths(1) // Monthly, and any unrecognized legacy value
            };

            sub.NextBillingDate = sub.SubscriptionEndDate;
            // Denormalize the plan's limits onto the subscription row so easyHMSAPI can enforce
            // doctor/bed limits with a local lookup — NULL (Enterprise, or a legacy plan with no
            // concept of limits) carries through as NULL, meaning unlimited.
            sub.MaxDoctors = easyHmsPlan?.MaxDoctors;
            sub.MaxBeds = easyHmsPlan?.MaxBeds;
            sub.RejectionReason = null;
            sub.RejectedAt = null;
            sub.UpdatedAt = DateTime.UtcNow;

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

            return Ok(new { message = "Subscription activated successfully.", sub.SubscriptionEndDate });
        }

        [HttpPost("{hospitalId}/reject")]
        public async Task<IActionResult> RejectPayment(Guid hospitalId, [FromBody] RejectPaymentRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
                return BadRequest("A reason is required to reject a payment.");

            var sub = await _appDb.HospitalSubscriptions.FirstOrDefaultAsync(hs => hs.HospitalId == hospitalId);
            if (sub == null) return NotFound("Hospital subscription not found.");

            if (sub.Status != "PendingApproval")
                return BadRequest($"There is no pending payment to reject for this hospital (current status: {sub.Status}).");

            sub.Status = "Rejected";
            sub.RejectionReason = request.Reason.Trim();
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
                latestPayment.RejectionReason = request.Reason.Trim();
            }

            await _appDb.SaveChangesAsync();

            return Ok(new { message = "Payment rejected." });
        }
    }

    public class RejectPaymentRequest
    {
        public string Reason { get; set; } = string.Empty;
    }
}
