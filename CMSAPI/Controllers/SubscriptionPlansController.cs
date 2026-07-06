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
        private readonly IConfiguration _configuration;

        public SubscriptionPlansController(CmsDbContext db, IConfiguration configuration)
        {
            _db = db;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<IActionResult> GetPlans()
        {
            var plans = await _db.SubscriptionPlans.OrderByDescending(p => p.CreatedAt).ToListAsync();
            return Ok(plans);
        }

        // Server-to-server only: easyHMSAPI proxies its (authenticated, hospital-user-facing)
        // plan list through this, so browsers never need a CMS credential and CMSAPI itself stays
        // fully behind [Authorize]. Protected by a shared key header instead of a user JWT.
        [HttpGet("service")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPlansForService([FromHeader(Name = "X-Service-Key")] string? serviceKey)
        {
            var expectedKey = _configuration["ServiceAuth:EasyHmsServiceKey"];
            if (string.IsNullOrEmpty(expectedKey) || serviceKey != expectedKey)
                return Unauthorized();

            var plans = await _db.SubscriptionPlans
                .Where(p => p.ApplicationName == "EasyHMS" && p.IsActive)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
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
                CreatedAt = DateTime.UtcNow
            };

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

            await _db.SaveChangesAsync();
            return Ok(plan);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePlan(Guid id)
        {
            var plan = await _db.SubscriptionPlans.FindAsync(id);
            if (plan == null) return NotFound("Plan not found.");

            _db.SubscriptionPlans.Remove(plan);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Plan deleted successfully." });
        }
    }
}
