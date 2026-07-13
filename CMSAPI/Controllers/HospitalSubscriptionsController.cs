using System.Data.Common;
using System.Security.Claims;
using CMSAPI.Authorization;
using CMSAPI.Data;
using CMSAPI.Data.Entities;
using CMSAPI.Domain.Entities;
using CMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Controllers
{
    /// <summary>
    /// CMS management of individual hospital subscriptions across both product
    /// platforms (EasyHMS and 1Rad). Reads/writes the correct product database via
    /// <see cref="IProductDbContextFactory"/>; plan names and the audit trail live
    /// in CMSDatabase. The CMS manages existing subscriptions — product apps create
    /// them during onboarding.
    /// </summary>
    [ApiController]
    [Route("api/v1/hospital-subscriptions")]
    public class HospitalSubscriptionsController : ControllerBase
    {
        private readonly IProductDbContextFactory _factory;
        private readonly CmsDbContext _cmsDb;
        private readonly ILogger<HospitalSubscriptionsController> _logger;

        public HospitalSubscriptionsController(
            IProductDbContextFactory factory,
            CmsDbContext cmsDb,
            ILogger<HospitalSubscriptionsController> logger)
        {
            _factory = factory;
            _cmsDb = cmsDb;
            _logger = logger;
        }

        // ── Reads ────────────────────────────────────────────────────────────────

        [HttpGet]
        [HasPermission("subscriptions.view")]
        public async Task<IActionResult> List(
            [FromQuery] string? platform = null,
            [FromQuery] string? status = null,
            CancellationToken ct = default)
        {
            var platforms = ResolvePlatforms(platform, out var invalid);
            if (invalid is not null) return BadRequest($"Unknown platform '{invalid}'.");

            var raw = new List<(string Platform, HospitalSubscription Sub)>();
            var unavailable = new List<string>();

            foreach (var p in platforms)
            {
                try
                {
                    await using var db = _factory.Create(p);
                    var rows = await db.HospitalSubscriptions
                        .Include(hs => hs.Hospital)
                        .AsNoTracking()
                        .ToListAsync(ct);
                    foreach (var r in rows) raw.Add((p, r));
                }
                catch (DbException ex)
                {
                    _logger.LogWarning(ex, "[HospitalSubscriptions] {Platform} unavailable during list.", p);
                    unavailable.Add(p);
                }
            }

            // A single requested platform being unreachable is a hard error, not an empty list.
            if (platforms.Count == 1 && unavailable.Count == 1)
                return Unavailable(platforms[0]);

            var planNames = await ResolvePlanNamesAsync(raw.Select(x => x.Sub), ct);
            var now = DateTime.UtcNow;

            var items = raw
                .Select(x => ToDto(x.Platform, x.Sub, planNames, now))
                .Where(dto => string.IsNullOrWhiteSpace(status)
                    || string.Equals(dto.Status, status, StringComparison.OrdinalIgnoreCase))
                .OrderBy(dto => dto.HospitalName)
                .ToList();

            return Ok(new { items, unavailablePlatforms = unavailable });
        }

        [HttpGet("summary")]
        [HasPermission("subscriptions.view")]
        public async Task<IActionResult> Summary(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var perPlatform = new List<PlatformSummaryDto>();

            foreach (var p in _factory.Platforms)
            {
                try
                {
                    await using var db = _factory.Create(p);
                    var subs = await db.HospitalSubscriptions.AsNoTracking()
                        .Select(s => new { s.Status, s.TrialEndDate, s.SubscriptionEndDate })
                        .ToListAsync(ct);

                    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var s in subs)
                    {
                        var eff = EffectiveStatus(s.Status, s.TrialEndDate, s.SubscriptionEndDate, now);
                        counts[eff] = counts.GetValueOrDefault(eff) + 1;
                    }

                    perPlatform.Add(new PlatformSummaryDto
                    {
                        Platform = p,
                        Available = true,
                        Total = subs.Count,
                        Active = counts.GetValueOrDefault(SubscriptionStatus.Active),
                        Trial = counts.GetValueOrDefault(SubscriptionStatus.Trial),
                        Pending = counts.GetValueOrDefault(SubscriptionStatus.Pending),
                        Expired = counts.GetValueOrDefault(SubscriptionStatus.Expired),
                        Blocked = counts.GetValueOrDefault(SubscriptionStatus.Blocked),
                    });
                }
                catch (DbException ex)
                {
                    _logger.LogWarning(ex, "[HospitalSubscriptions] {Platform} unavailable during summary.", p);
                    perPlatform.Add(new PlatformSummaryDto { Platform = p, Available = false });
                }
            }

            var overall = new PlatformSummaryDto
            {
                Platform = "All",
                Available = perPlatform.Any(x => x.Available),
                Total = perPlatform.Sum(x => x.Total),
                Active = perPlatform.Sum(x => x.Active),
                Trial = perPlatform.Sum(x => x.Trial),
                Pending = perPlatform.Sum(x => x.Pending),
                Expired = perPlatform.Sum(x => x.Expired),
                Blocked = perPlatform.Sum(x => x.Blocked),
            };

            return Ok(new SubscriptionSummaryResponse { Platforms = perPlatform, Overall = overall });
        }

        [HttpGet("{platform}/{hospitalId:guid}")]
        [HasPermission("subscriptions.view")]
        public async Task<IActionResult> Detail(string platform, Guid hospitalId, CancellationToken ct = default)
        {
            var key = _factory.NormalizePlatform(platform);
            if (key is null) return BadRequest($"Unknown platform '{platform}'.");

            try
            {
                await using var db = _factory.Create(key);
                var sub = await db.HospitalSubscriptions
                    .Include(hs => hs.Hospital)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(hs => hs.HospitalId == hospitalId, ct);

                if (sub is null) return NotFound("Hospital has no subscription record.");

                var planNames = await ResolvePlanNamesAsync(new[] { sub }, ct);
                return Ok(ToDto(key, sub, planNames, DateTime.UtcNow));
            }
            catch (DbException)
            {
                return Unavailable(key);
            }
        }

        // ── Mutations ────────────────────────────────────────────────────────────

        [HttpPost("{platform}/{hospitalId:guid}/status")]
        [HasPermission("subscriptions.manage")]
        public Task<IActionResult> SetStatus(string platform, Guid hospitalId, [FromBody] SetStatusRequest body, CancellationToken ct = default)
            => MutateAsync(platform, hospitalId, "status", (sub, _) =>
            {
                if (body.Active)
                {
                    if (!sub.PlanId.HasValue)
                        return ("Assign a plan before activating this subscription.", null, null);
                    var old = sub.Status;
                    sub.Status = SubscriptionStatus.Active;
                    sub.SubscriptionStartDate ??= DateTime.UtcNow;
                    return (null, old, sub.Status);
                }
                else
                {
                    var old = sub.Status;
                    sub.Status = SubscriptionStatus.Blocked;
                    return (null, old, sub.Status);
                }
            }, ct);

        [HttpPost("{platform}/{hospitalId:guid}/trial")]
        [HasPermission("subscriptions.manage")]
        public Task<IActionResult> SetTrial(string platform, Guid hospitalId, [FromBody] SetTrialRequest body, CancellationToken ct = default)
            => MutateAsync(platform, hospitalId, "trial", (sub, _) =>
            {
                var start = body.TrialStartDate ?? sub.TrialStartDate;
                var end = body.TrialEndDate ?? sub.TrialEndDate;
                if (start.HasValue && end.HasValue && end <= start)
                    return ("Trial end date must be after the start date.", null, null);

                var old = $"{Fmt(sub.TrialStartDate)}..{Fmt(sub.TrialEndDate)}";
                sub.TrialStartDate = start;
                sub.TrialEndDate = end;
                return (null, old, $"{Fmt(start)}..{Fmt(end)}");
            }, ct);

        [HttpPost("{platform}/{hospitalId:guid}/validity")]
        [HasPermission("subscriptions.manage")]
        public Task<IActionResult> SetValidity(string platform, Guid hospitalId, [FromBody] SetValidityRequest body, CancellationToken ct = default)
            => MutateAsync(platform, hospitalId, "validity", (sub, _) =>
            {
                var start = body.SubscriptionStartDate ?? sub.SubscriptionStartDate;
                var end = body.SubscriptionEndDate ?? sub.SubscriptionEndDate;
                if (start.HasValue && end.HasValue && end <= start)
                    return ("Subscription end date must be after the start date.", null, null);

                var old = $"{Fmt(sub.SubscriptionStartDate)}..{Fmt(sub.SubscriptionEndDate)}";
                sub.SubscriptionStartDate = start;
                sub.SubscriptionEndDate = end;
                if (body.NextBillingDate.HasValue) sub.NextBillingDate = body.NextBillingDate;
                return (null, old, $"{Fmt(start)}..{Fmt(end)}");
            }, ct);

        [HttpPost("{platform}/{hospitalId:guid}/plan")]
        [HasPermission("subscriptions.manage")]
        public async Task<IActionResult> AssignPlan(string platform, Guid hospitalId, [FromBody] AssignPlanRequest body, CancellationToken ct = default)
        {
            var key = _factory.NormalizePlatform(platform);
            if (key is null) return BadRequest($"Unknown platform '{platform}'.");

            var plan = await _cmsDb.SubscriptionPlans.AsNoTracking()
                .FirstOrDefaultAsync(p => p.PlanId == body.PlanId, ct);
            if (plan is null) return BadRequest("Plan not found.");
            if (!plan.IsActive) return BadRequest("Plan is inactive.");
            if (!string.Equals(plan.ApplicationName, key, StringComparison.OrdinalIgnoreCase))
                return BadRequest($"Plan '{plan.Name}' belongs to {plan.ApplicationName}, not {key}.");

            return await MutateAsync(platform, hospitalId, "plan", (sub, _) =>
            {
                var old = sub.PlanId?.ToString();
                sub.PlanId = body.PlanId;
                return (null, old, body.PlanId.ToString());
            }, ct);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Loads the subscription for a platform+hospital, applies <paramref name="apply"/>,
        /// persists it, and records an audit entry. Returns 400/404/503 as appropriate.
        /// </summary>
        private async Task<IActionResult> MutateAsync(
            string platformParam, Guid hospitalId, string action,
            Func<HospitalSubscription, AppDbContext, (string? error, string? oldValue, string? newValue)> apply,
            CancellationToken ct)
        {
            var key = _factory.NormalizePlatform(platformParam);
            if (key is null) return BadRequest($"Unknown platform '{platformParam}'.");

            try
            {
                await using var db = _factory.Create(key);
                var sub = await db.HospitalSubscriptions
                    .FirstOrDefaultAsync(hs => hs.HospitalId == hospitalId, ct);
                if (sub is null) return NotFound("Hospital has no subscription record.");

                var (error, oldValue, newValue) = apply(sub, db);
                if (error is not null) return BadRequest(error);

                sub.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                await WriteAuditAsync(key, hospitalId, action, oldValue, newValue, ct);
                return Ok(new { message = "Subscription updated.", hospitalId, action });
            }
            catch (DbException)
            {
                return Unavailable(key);
            }
        }

        private async Task WriteAuditAsync(
            string platform, Guid hospitalId, string action, string? oldValue, string? newValue, CancellationToken ct)
        {
            try
            {
                _cmsDb.SubscriptionAuditLogs.Add(new SubscriptionAuditLog
                {
                    AuditId = Guid.NewGuid(),
                    OccurredAt = DateTime.UtcNow,
                    ActorUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null,
                    ActorEmail = User.FindFirstValue(ClaimTypes.Email),
                    Platform = platform,
                    HospitalId = hospitalId,
                    Action = action,
                    OldValue = Truncate(oldValue, 1000),
                    NewValue = Truncate(newValue, 1000),
                });
                await _cmsDb.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // Audit is best-effort: the subscription change already committed.
                _logger.LogWarning(ex, "[HospitalSubscriptions] failed to write audit log for {Platform}/{Hospital}.", platform, hospitalId);
            }
        }

        private async Task<Dictionary<Guid, string>> ResolvePlanNamesAsync(IEnumerable<HospitalSubscription> subs, CancellationToken ct)
        {
            var planIds = subs.Where(s => s.PlanId.HasValue).Select(s => s.PlanId!.Value).Distinct().ToList();
            if (planIds.Count == 0) return new Dictionary<Guid, string>();
            return await _cmsDb.SubscriptionPlans.AsNoTracking()
                .Where(p => planIds.Contains(p.PlanId))
                .ToDictionaryAsync(p => p.PlanId, p => p.Name, ct);
        }

        private List<string> ResolvePlatforms(string? platform, out string? invalid)
        {
            invalid = null;
            if (string.IsNullOrWhiteSpace(platform) || string.Equals(platform, "All", StringComparison.OrdinalIgnoreCase))
                return _factory.Platforms.ToList();

            var key = _factory.NormalizePlatform(platform);
            if (key is null) { invalid = platform; return new List<string>(); }
            return new List<string> { key };
        }

        private static HospitalSubscriptionDto ToDto(
            string platform, HospitalSubscription s, IReadOnlyDictionary<Guid, string> planNames, DateTime now) => new()
        {
            HospitalSubscriptionId = s.HospitalSubscriptionId,
            Platform = platform,
            HospitalId = s.HospitalId,
            HospitalName = s.Hospital?.Name ?? "",
            PlanId = s.PlanId,
            PlanName = s.PlanId.HasValue && planNames.TryGetValue(s.PlanId.Value, out var n) ? n : null,
            Status = EffectiveStatus(s.Status, s.TrialEndDate, s.SubscriptionEndDate, now),
            StoredStatus = s.Status,
            TrialStartDate = s.TrialStartDate,
            TrialEndDate = s.TrialEndDate,
            SubscriptionStartDate = s.SubscriptionStartDate,
            SubscriptionEndDate = s.SubscriptionEndDate,
            NextBillingDate = s.NextBillingDate,
        };

        /// <summary>Applies date-based expiry so a past-due Active/Trial reads as Expired.</summary>
        private static string EffectiveStatus(string stored, DateTime? trialEnd, DateTime? subEnd, DateTime now)
        {
            if (string.Equals(stored, SubscriptionStatus.Active, StringComparison.OrdinalIgnoreCase)
                && subEnd.HasValue && subEnd.Value < now)
                return SubscriptionStatus.Expired;

            if (string.Equals(stored, SubscriptionStatus.Trial, StringComparison.OrdinalIgnoreCase)
                && trialEnd.HasValue && trialEnd.Value < now)
                return SubscriptionStatus.Expired;

            return stored;
        }

        private IActionResult Unavailable(string platform) =>
            StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = $"The {platform} database is currently unavailable." });

        private static string Fmt(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "—";

        private static string? Truncate(string? s, int max) =>
            s is not null && s.Length > max ? s[..max] : s;
    }

    /// <summary>Canonical subscription status values.</summary>
    public static class SubscriptionStatus
    {
        public const string Trial = "Trial";
        public const string Pending = "Pending";
        public const string Active = "Active";
        public const string Expired = "Expired";
        public const string Blocked = "Blocked";
    }
}
