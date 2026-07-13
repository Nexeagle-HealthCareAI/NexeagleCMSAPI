using System.Data.Common;
using CMSAPI.Data;
using CMSAPI.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize] // Assuming CMS admin auth is required
    public class SubscriptionPlansController : ControllerBase
    {
        private readonly CmsDbContext _db;
        private readonly IProductDbContextFactory _productDb;

        public SubscriptionPlansController(CmsDbContext db, IProductDbContextFactory productDb)
        {
            _db = db;
            _productDb = productDb;
        }

        // Copies capacity + module fields from an incoming plan onto a target row.
        private static void ApplyCapacityAndModules(SubscriptionPlan target, SubscriptionPlan src)
        {
            target.MaxDoctors = src.MaxDoctors;
            target.MaxBeds = src.MaxBeds;
            target.ModuleIPD = src.ModuleIPD;
            target.ModuleOPD = src.ModuleOPD;
            target.ModuleBilling = src.ModuleBilling;
            target.ModuleLab = src.ModuleLab;
            target.ModuleRAD = src.ModuleRAD;
        }

        [HttpGet]
        public async Task<IActionResult> GetPlans([FromQuery] string? application = null)
        {
            var query = _db.SubscriptionPlans.AsQueryable();

            // Optional platform filter: EasyHMS | 1Rad (case-insensitive). "All"/blank = no filter.
            if (!string.IsNullOrWhiteSpace(application) &&
                !string.Equals(application, "All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(p => p.ApplicationName == application);
            }

            var plans = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();
            return Ok(plans);
        }

        [HttpPost]
        public async Task<IActionResult> CreatePlan([FromBody] SubscriptionPlan request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.BasePrice < 0 || request.DiscountPrice < 0)
                return BadRequest("Invalid plan data.");

            var plan = new SubscriptionPlan
            {
                PlanId = Guid.NewGuid(),
                Name = request.Name,
                BasePrice = request.BasePrice,
                DiscountPrice = request.DiscountPrice,
                BillingCycle = request.BillingCycle,
                ApplicationName = request.ApplicationName ?? "1Rad",
                IsActive = request.IsActive,
                IsCustom = request.IsCustom,
                CreatedAt = DateTime.UtcNow
            };
            ApplyCapacityAndModules(plan, request);

            _db.SubscriptionPlans.Add(plan);
            await _db.SaveChangesAsync();

            return Ok(plan);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdatePlan(Guid id, [FromBody] SubscriptionPlan request)
        {
            var plan = await _db.SubscriptionPlans.FindAsync(id);
            if (plan == null) return NotFound("Plan not found.");

            plan.Name = request.Name;
            plan.BasePrice = request.BasePrice;
            plan.DiscountPrice = request.DiscountPrice;
            plan.BillingCycle = request.BillingCycle;
            plan.ApplicationName = request.ApplicationName ?? plan.ApplicationName;
            plan.IsActive = request.IsActive;
            ApplyCapacityAndModules(plan, request);

            await _db.SaveChangesAsync();
            return Ok(plan);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePlan(Guid id)
        {
            var plan = await _db.SubscriptionPlans.FindAsync(id);
            if (plan == null) return NotFound("Plan not found.");

            // Guard: a plan mapped to any hospital subscription (either product DB) cannot be deleted.
            var mapped = await CountHospitalsUsingPlanAsync(id);
            if (mapped > 0)
                return Conflict(new { message = $"Cannot delete: {mapped} hospital subscription(s) are mapped to this plan.", mapped });

            _db.SubscriptionPlans.Remove(plan);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Plan deleted successfully." });
        }

        /// <summary>Counts hospital subscriptions referencing this plan across all reachable product DBs.</summary>
        private async Task<int> CountHospitalsUsingPlanAsync(Guid planId)
        {
            var total = 0;
            foreach (var platform in _productDb.Platforms)
            {
                try
                {
                    await using var db = _productDb.Create(platform);
                    total += await db.HospitalSubscriptions.CountAsync(hs => hs.PlanId == planId);
                }
                catch (DbException)
                {
                    // Platform unreachable — skip; the guard is best-effort across available DBs.
                }
            }
            return total;
        }
    }
}
