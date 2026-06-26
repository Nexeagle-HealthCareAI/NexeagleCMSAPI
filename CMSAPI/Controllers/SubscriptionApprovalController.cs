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
            // Find hospitals where subscription is expired or trial has ended, or they need activation
            var pending = await _appDb.HospitalSubscriptions
                .Include(hs => hs.Hospital)
                .Where(hs => hs.Status != "Active" && hs.PlanId != null)
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
                    hs.SubscriptionEndDate
                })
                .ToListAsync();

            // We also need plan names from CmsDb
            var planIds = pending.Where(p => p.PlanId.HasValue).Select(p => p.PlanId!.Value).Distinct().ToList();
            var plans = await _cmsDb.SubscriptionPlans.Where(p => planIds.Contains(p.PlanId)).ToDictionaryAsync(p => p.PlanId, p => p.Name);

            var result = pending.Select(p => new
            {
                p.HospitalSubscriptionId,
                p.HospitalId,
                p.HospitalName,
                p.PlanId,
                PlanName = p.PlanId.HasValue && plans.ContainsKey(p.PlanId.Value) ? plans[p.PlanId.Value] : "Unknown",
                p.Status,
                p.TrialStartDate,
                p.TrialEndDate,
                p.SubscriptionEndDate
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

            var plan = await _cmsDb.SubscriptionPlans.FirstOrDefaultAsync(p => p.PlanId == sub.PlanId.Value);
            if (plan == null)
                return BadRequest("Invalid plan selected by hospital.");

            // Activate subscription
            sub.Status = "Active";
            sub.SubscriptionStartDate = DateTime.UtcNow;
            
            if (plan.BillingCycle.Equals("Yearly", StringComparison.OrdinalIgnoreCase))
                sub.SubscriptionEndDate = DateTime.UtcNow.AddYears(1);
            else
                sub.SubscriptionEndDate = DateTime.UtcNow.AddMonths(1);

            sub.NextBillingDate = sub.SubscriptionEndDate;
            sub.UpdatedAt = DateTime.UtcNow;

            await _appDb.SaveChangesAsync();

            return Ok(new { message = "Subscription activated successfully.", sub.SubscriptionEndDate });
        }
    }
}
