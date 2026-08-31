using Asp.Versioning;
using CMSAPI.Authorization;
using CMSAPI.Data;
using CMSAPI.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;

namespace CMSAPI.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Authorize] // Assuming CMS admin auth is required
    public class SubscriptionApprovalController : ControllerBase
    {
        private readonly ISubscriptionApprovalService _approvalService;
        private readonly AppDbContext _appDb; // Needed for GetPendingApprovals/GetApprovalHistory
        private readonly CmsDbContext _cmsDb; // Needed for GetPendingApprovals/GetApprovalHistory

        public SubscriptionApprovalController(
            ISubscriptionApprovalService approvalService,
            AppDbContext appDb, 
            CmsDbContext cmsDb)
        {
            _approvalService = approvalService;
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
                    hs.PaymentDate,
                    hs.ReferralCode,
                    hs.ReferralCodeRewardKind,
                    hs.ReferralCodeRewardValue
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
                    p.ReferralCode,
                    p.ReferralCodeRewardKind,
                    p.ReferralCodeRewardValue,
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
        public async Task<IActionResult> GetApprovalHistory([FromQuery] int page = 1, [FromQuery] int limit = 50)
        {
            var query = _appDb.HospitalSubscriptionPayments
                .OrderByDescending(p => p.SubmittedAt);

            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)limit);

            var payments = await query
                .Skip((page - 1) * limit)
                .Take(limit)
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

            // A hospital's referral code (if any) is attached once, at registration, to its single
            // subscription row -- join by HospitalId rather than duplicating these columns onto the
            // append-only payments table.
            var referralByHospital = await _appDb.HospitalSubscriptions
                .Where(hs => hospitalIds.Contains(hs.HospitalId) && hs.ReferralCode != null)
                .ToDictionaryAsync(hs => hs.HospitalId, hs => new { hs.ReferralCode, hs.ReferralCodeRewardKind, hs.ReferralCodeRewardValue });

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
                referralByHospital.TryGetValue(p.HospitalId, out var referral);

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
                    p.ProratedCreditAmount,
                    ReferralCode = referral?.ReferralCode,
                    ReferralCodeRewardKind = referral?.ReferralCodeRewardKind,
                    ReferralCodeRewardValue = referral?.ReferralCodeRewardValue
                };
            }).ToList();

            return Ok(new
            {
                Data = result,
                TotalItems = totalItems,
                TotalPages = totalPages,
                CurrentPage = page
            });
        }

        [HasPermission("subscriptions.approve")]
        [HttpPost("{hospitalId}/approve")]
        public async Task<IActionResult> ApprovePayment(Guid hospitalId)
        {
            var result = await _approvalService.ApprovePaymentAsync(hospitalId);
            if (!result.Success)
            {
                if (result.ErrorMessage == "Hospital subscription not found.")
                    return NotFound(result.ErrorMessage);
                return BadRequest(result.ErrorMessage);
            }

            return Ok(new { message = "Subscription activated successfully.", result.SubscriptionEndDate });
        }

        [HasPermission("subscriptions.approve")]
        [HttpPost("{hospitalId}/reject")]
        public async Task<IActionResult> RejectPayment(Guid hospitalId, [FromBody] RejectPaymentRequest request)
        {
            var result = await _approvalService.RejectPaymentAsync(hospitalId, request.Reason);
            if (!result.Success)
            {
                if (result.ErrorMessage == "Hospital subscription not found.")
                    return NotFound(result.ErrorMessage);
                return BadRequest(result.ErrorMessage);
            }

            return Ok(new { message = "Payment rejected." });
        }
    }
}
