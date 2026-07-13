using CMSAPI.Application.Services;
using CMSAPI.Authorization;
using CMSAPI.Data;
using CMSAPI.Data.Entities;
using CMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Controllers
{
    /// <summary>
    /// Pricing calculator and module-charge configuration. Prices a (doctors, beds,
    /// modules) combination, surfaces a fitting predefined tier, and commits a
    /// selection into a concrete plan (existing tier's PlanId, or a new custom one).
    /// </summary>
    [ApiController]
    [Route("api/v1/plans")]
    public class PlanCalculatorController : ControllerBase
    {
        private readonly CmsDbContext _db;

        public PlanCalculatorController(CmsDbContext db) => _db = db;

        // ── Calculate a price + find a fitting tier ──────────────────────────────
        [HttpPost("calculate")]
        [HasPermission("subscriptions.view")]
        public async Task<IActionResult> Calculate([FromBody] CalculatePlanRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.ApplicationName))
                return BadRequest("ApplicationName is required.");
            if (req.Doctors < 0 || req.Beds < 0)
                return BadRequest("Doctors and beds must be zero or more.");

            var lines = await BuildModuleLinesAsync(req.ApplicationName, req.Modules, ct);
            var breakdown = PlanPricing.Calculate(req.Doctors, req.Beds, lines);
            var basePrice = PlanPricing.DoctorBase * req.Doctors + PlanPricing.BedBase * req.Beds + breakdown.ModulesSubtotal;

            var tier = await _db.SubscriptionPlans.AsNoTracking()
                .Where(p => p.ApplicationName == req.ApplicationName && !p.IsCustom && p.IsActive
                            && p.MaxDoctors >= req.Doctors && p.MaxBeds >= req.Beds)
                .OrderBy(p => p.MaxDoctors).ThenBy(p => p.MaxBeds)
                .FirstOrDefaultAsync(ct);

            return Ok(new CalculatePlanResponse
            {
                Breakdown = breakdown,
                BasePrice = basePrice,
                MatchedTier = tier is null ? null : new MatchedTierDto
                {
                    PlanId = tier.PlanId,
                    Name = tier.Name,
                    MaxDoctors = tier.MaxDoctors,
                    MaxBeds = tier.MaxBeds,
                    Price = tier.DiscountPrice,
                },
            });
        }

        // ── Commit a selection → PlanId ──────────────────────────────────────────
        [HttpPost("choose")]
        [HasPermission("subscriptions.manage")]
        public async Task<IActionResult> Choose([FromBody] ChoosePlanRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.ApplicationName))
                return BadRequest("ApplicationName is required.");

            // Picking an existing (predefined) tier → return its shared PlanId.
            if (req.PlanId.HasValue)
            {
                var plan = await _db.SubscriptionPlans.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.PlanId == req.PlanId.Value, ct);
                if (plan is null) return BadRequest("Plan not found.");
                if (!plan.IsActive) return BadRequest("Plan is inactive.");
                if (!string.Equals(plan.ApplicationName, req.ApplicationName, StringComparison.OrdinalIgnoreCase))
                    return BadRequest($"Plan belongs to {plan.ApplicationName}, not {req.ApplicationName}.");

                return Ok(new ChoosePlanResponse
                {
                    PlanId = plan.PlanId,
                    IsCustom = plan.IsCustom,
                    Name = plan.Name,
                    Price = plan.DiscountPrice,
                });
            }

            // Custom combination → mint a new plan.
            if (req.Doctors < 0 || req.Beds < 0)
                return BadRequest("Doctors and beds must be zero or more.");

            var lines = await BuildModuleLinesAsync(req.ApplicationName, req.Modules, ct);
            var breakdown = PlanPricing.Calculate(req.Doctors, req.Beds, lines);
            var basePrice = PlanPricing.DoctorBase * req.Doctors + PlanPricing.BedBase * req.Beds + breakdown.ModulesSubtotal;

            var modules = NormalizeModules(req.Modules);
            var custom = new SubscriptionPlan
            {
                PlanId = Guid.NewGuid(),
                Name = $"Custom · {req.Doctors}D / {req.Beds}B",
                ApplicationName = req.ApplicationName,
                BillingCycle = string.IsNullOrWhiteSpace(req.BillingCycle) ? "Monthly" : req.BillingCycle!,
                BasePrice = basePrice,
                DiscountPrice = breakdown.Total,
                MaxDoctors = req.Doctors,
                MaxBeds = req.Beds,
                ModuleIPD = modules.Contains("IPD"),
                ModuleOPD = modules.Contains("OPD"),
                ModuleBilling = modules.Contains("Billing"),
                ModuleLab = modules.Contains("Lab"),
                ModuleRAD = modules.Contains("RAD"),
                IsCustom = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };

            _db.SubscriptionPlans.Add(custom);
            await _db.SaveChangesAsync(ct);

            return Ok(new ChoosePlanResponse
            {
                PlanId = custom.PlanId,
                IsCustom = true,
                Name = custom.Name,
                Price = custom.DiscountPrice,
            });
        }

        // ── Module charge configuration ──────────────────────────────────────────
        [HttpGet("module-charges")]
        [HasPermission("subscriptions.view")]
        public async Task<IActionResult> GetModuleCharges([FromQuery] string application, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(application))
                return BadRequest("application is required.");

            var existing = await _db.ModuleCharges.AsNoTracking()
                .Where(m => m.ApplicationName == application)
                .ToDictionaryAsync(m => m.ModuleKey, m => m.Charge, StringComparer.OrdinalIgnoreCase, ct);

            // Always return all five modules, defaulting missing ones to 0.
            var result = PlanPricing.ModuleKeys
                .Select(k => new ModuleChargeDto { Module = k, Charge = existing.GetValueOrDefault(k, 0m) })
                .ToList();

            return Ok(result);
        }

        [HttpPut("module-charges")]
        [HasPermission("subscriptions.manage")]
        public async Task<IActionResult> PutModuleCharges([FromBody] PutModuleChargesRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.ApplicationName))
                return BadRequest("ApplicationName is required.");

            foreach (var c in req.Charges)
            {
                if (!PlanPricing.ModuleKeys.Contains(c.Module, StringComparer.OrdinalIgnoreCase))
                    return BadRequest($"Unknown module '{c.Module}'.");
                if (c.Charge < 0) return BadRequest("Charge cannot be negative.");

                var row = await _db.ModuleCharges.FirstOrDefaultAsync(
                    m => m.ApplicationName == req.ApplicationName && m.ModuleKey == c.Module, ct);
                if (row is null)
                {
                    _db.ModuleCharges.Add(new ModuleCharge
                    {
                        ModuleChargeId = Guid.NewGuid(),
                        ApplicationName = req.ApplicationName,
                        ModuleKey = c.Module,
                        Charge = c.Charge,
                        UpdatedAt = DateTime.UtcNow,
                    });
                }
                else
                {
                    row.Charge = c.Charge;
                    row.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _db.SaveChangesAsync(ct);
            return await GetModuleCharges(req.ApplicationName, ct);
        }

        // ── helpers ──────────────────────────────────────────────────────────────
        private async Task<List<ModuleLine>> BuildModuleLinesAsync(string application, List<string> modules, CancellationToken ct)
        {
            var enabled = NormalizeModules(modules);
            if (enabled.Count == 0) return new List<ModuleLine>();

            var charges = await _db.ModuleCharges.AsNoTracking()
                .Where(m => m.ApplicationName == application)
                .ToDictionaryAsync(m => m.ModuleKey, m => m.Charge, StringComparer.OrdinalIgnoreCase, ct);

            return PlanPricing.ModuleKeys
                .Where(enabled.Contains)
                .Select(k => new ModuleLine(k, charges.GetValueOrDefault(k, 0m)))
                .ToList();
        }

        /// <summary>Keeps only valid, canonical module keys (case-insensitive, de-duped).</summary>
        private static HashSet<string> NormalizeModules(IEnumerable<string>? modules)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (modules is null) return set;
            foreach (var m in modules)
            {
                var match = PlanPricing.ModuleKeys.FirstOrDefault(k => string.Equals(k, m?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is not null) set.Add(match);
            }
            return set;
        }
    }
}
