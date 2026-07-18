using CMSAPI.Data;
using CMSAPI.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Controllers
{
    // Dedicated EasyHMS plan catalog -- see EasyHmsSubscriptionPlan for why this isn't just the
    // shared (1Rad) SubscriptionPlansController with an ApplicationName filter.
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize] // CMS admin auth required
    public class EasyHmsSubscriptionPlansController : ControllerBase
    {
        private readonly CmsDbContext _db;
        private readonly IConfiguration _configuration;

        public EasyHmsSubscriptionPlansController(CmsDbContext db, IConfiguration configuration)
        {
            _db = db;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<IActionResult> GetPlans()
        {
            var plans = await _db.EasyHmsSubscriptionPlans.OrderByDescending(p => p.CreatedAt).ToListAsync();
            return Ok(plans);
        }

        // Server-to-server only: easyHMSAPI proxies its (authenticated, hospital-user-facing)
        // plan list through this, so browsers never need a CMS credential and CMSAPI itself stays
        // fully behind [Authorize]. Protected by a shared key header instead of a user JWT --
        // same convention as the 1Rad SubscriptionPlansController's /service endpoint.
        [HttpGet("service")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPlansForService([FromHeader(Name = "X-Service-Key")] string? serviceKey)
        {
            var expectedKey = _configuration["ServiceAuth:EasyHmsServiceKey"];
            if (string.IsNullOrEmpty(expectedKey) || serviceKey != expectedKey)
                return Unauthorized();

            var plans = await _db.EasyHmsSubscriptionPlans
                .Where(p => p.IsActive)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
            return Ok(plans);
        }

        [HttpPost]
        public async Task<IActionResult> CreatePlan([FromBody] EasyHmsSubscriptionPlan request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.BasePrice < 0 || request.DiscountPrice < 0)
                return BadRequest("Invalid plan data.");

            var plan = new EasyHmsSubscriptionPlan
            {
                PlanId = Guid.NewGuid(),
                Name = request.Name,
                BasePrice = request.BasePrice,
                DiscountPrice = request.DiscountPrice,
                BillingCycle = request.BillingCycle,
                IsActive = request.IsActive,
                Features = request.Features,
                MaxDoctors = request.MaxDoctors,
                MaxBeds = request.MaxBeds,
                IsEnterprise = request.IsEnterprise,
                CreatedAt = DateTime.UtcNow
            };

            _db.EasyHmsSubscriptionPlans.Add(plan);
            await _db.SaveChangesAsync();

            return Ok(plan);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdatePlan(Guid id, [FromBody] EasyHmsSubscriptionPlan request)
        {
            var plan = await _db.EasyHmsSubscriptionPlans.FindAsync(id);
            if (plan == null) return NotFound("Plan not found.");

            plan.Name = request.Name;
            plan.BasePrice = request.BasePrice;
            plan.DiscountPrice = request.DiscountPrice;
            plan.BillingCycle = request.BillingCycle;
            plan.IsActive = request.IsActive;
            plan.Features = request.Features;
            plan.MaxDoctors = request.MaxDoctors;
            plan.MaxBeds = request.MaxBeds;
            plan.IsEnterprise = request.IsEnterprise;

            await _db.SaveChangesAsync();
            return Ok(plan);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePlan(Guid id)
        {
            var plan = await _db.EasyHmsSubscriptionPlans.FindAsync(id);
            if (plan == null) return NotFound("Plan not found.");

            _db.EasyHmsSubscriptionPlans.Remove(plan);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Plan deleted successfully." });
        }
    }
}
