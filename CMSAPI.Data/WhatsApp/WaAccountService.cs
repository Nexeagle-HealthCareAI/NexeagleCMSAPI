using System.Text.Json.Nodes;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.WhatsApp;

/// <summary>See IWaAccountService. Changing the profile, budget or rates needs whatsapp.settings.</summary>
public class WaAccountService : IWaAccountService
{
    public static readonly string[] Verticals =
    {
        "UNDEFINED", "OTHER", "AUTO", "BEAUTY", "APPAREL", "EDU", "ENTERTAIN", "EVENT_PLAN", "FINANCE", "GROCERY", "GOVT",
        "HOTEL", "HEALTH", "NONPROFIT", "PROF_SERVICES", "RETAIL", "TRAVEL", "RESTAURANT", "NOT_A_BIZ",
    };
    private static readonly string[] RateCategories = { "marketing", "utility", "authentication", "service" };

    private readonly CmsDbContext _db;
    private readonly IWaMeta _meta;

    public WaAccountService(CmsDbContext db, IWaMeta meta)
    {
        _db = db;
        _meta = meta;
    }

    private static WorkResult<T> NoSettings<T>() => WorkResult<T>.Fail(403, "Only people who may change WhatsApp settings can do this.");

    private static WorkResult<T> FromMeta<T>(Exception ex) => ex switch
    {
        WaNotConnectedException n => WorkResult<T>.Fail(503, n.Message),
        WaMetaException m => WorkResult<T>.Fail(m.HttpStatus, m.Message),
        _ => throw ex,
    };

    public async Task<WorkResult<WaNumberDto>> NumberAsync(WaActor actor)
    {
        if (!_meta.IsConfigured)
            return WorkResult<WaNumberDto>.Success(new WaNumberDto { Connected = false, Problem = "Not connected on this environment: set WhatsAppMarketing__PhoneNumberId, __WabaId and __AccessToken." });
        try
        {
            var n = await _meta.GetNumberAsync();
            return WorkResult<WaNumberDto>.Success(new WaNumberDto
            {
                Connected = true,
                DisplayPhone = n["display_phone_number"].Str(),
                VerifiedName = n["verified_name"].Str(),
                NameStatus = n["name_status"].Str(),
                Quality = n["quality_rating"].Str(),
                MessagingLimit = n["messaging_limit_tier"].Str(),
                Throughput = n["throughput"]?["level"].Str(),
            });
        }
        catch (WaMetaException ex)
        {
            return WorkResult<WaNumberDto>.Success(new WaNumberDto { Connected = false, Problem = ex.Message });
        }
    }

    public async Task<WorkResult<WaProfileDto>> ProfileAsync(WaActor actor)
    {
        try
        {
            var p = await _meta.GetProfileAsync();
            return WorkResult<WaProfileDto>.Success(new WaProfileDto
            {
                About = p["about"].Str(),
                Address = p["address"].Str(),
                Description = p["description"].Str(),
                Email = p["email"].Str(),
                Websites = (p["websites"]?.AsArray() ?? new JsonArray()).Select(w => w.Str()).Where(w => w != null).Select(w => w!).ToList(),
                Vertical = p["vertical"].Str(),
                PictureUrl = p["profile_picture_url"].Str(),
            });
        }
        catch (Exception ex) when (ex is WaMetaException or WaNotConnectedException) { return FromMeta<WaProfileDto>(ex); }
    }

    public async Task<WorkResult<WaProfileDto>> SaveProfileAsync(WaActor actor, WaProfileDto profile, byte[]? picture, string? pictureMime)
    {
        if (!actor.CanSettings) return NoSettings<WaProfileDto>();
        string? Cap(string? s, int max, string what, out string? problem)
        {
            problem = null;
            var t = (s ?? string.Empty).Trim();
            if (t.Length > max) problem = $"{what} can be at most {max} characters.";
            return t;
        }
        var body = new JsonObject
        {
            ["about"] = Cap(profile.About, 139, "\"About\"", out var p1),
            ["address"] = Cap(profile.Address, 256, "The address", out var p2),
            ["description"] = Cap(profile.Description, 512, "The description", out var p3),
            ["email"] = Cap(profile.Email, 128, "The email", out var p4),
        };
        var problem = p1 ?? p2 ?? p3 ?? p4;
        if (problem != null) return WorkResult<WaProfileDto>.Fail(400, problem);
        if (string.IsNullOrWhiteSpace(profile.About)) return WorkResult<WaProfileDto>.Fail(400, "\"About\" is required: it shows under your name in WhatsApp.");
        var email = body["email"].Str();
        if (!string.IsNullOrEmpty(email) && (!email.Contains('@') || email.Contains(' '))) return WorkResult<WaProfileDto>.Fail(400, "The email is not valid.");
        var sites = (profile.Websites ?? new()).Select(w => w.Trim()).Where(w => w.Length > 0).ToList();
        if (sites.Count > 2) return WorkResult<WaProfileDto>.Fail(400, "WhatsApp shows two websites at most.");
        if (sites.Any(w => w.Length > 256 || !(w.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || w.StartsWith("http://", StringComparison.OrdinalIgnoreCase))))
            return WorkResult<WaProfileDto>.Fail(400, "Websites must start with https:// (at most 256 characters).");
        body["websites"] = new JsonArray(sites.Select(w => (JsonNode)JsonValue.Create(w)!).ToArray());
        if (!string.IsNullOrWhiteSpace(profile.Vertical))
        {
            if (!Verticals.Contains(profile.Vertical)) return WorkResult<WaProfileDto>.Fail(400, "Choose the business category from the list.");
            body["vertical"] = profile.Vertical;
        }
        if (picture != null)
        {
            if (pictureMime is not ("image/jpeg" or "image/png")) return WorkResult<WaProfileDto>.Fail(415, "The profile photo must be a JPG or PNG.");
            if (picture.Length > 5 * 1024 * 1024) return WorkResult<WaProfileDto>.Fail(413, "The profile photo can be at most 5 MB.");
        }
        try
        {
            if (picture != null)
                body["profile_picture_handle"] = await _meta.UploadSampleAsync(picture, pictureMime!, pictureMime == "image/png" ? "profile.png" : "profile.jpg");
            await _meta.UpdateProfileAsync(body);
        }
        catch (Exception ex) when (ex is WaMetaException or WaNotConnectedException) { return FromMeta<WaProfileDto>(ex); }
        return await ProfileAsync(actor);
    }

    public async Task<WorkResult<WaSettingsDto>> SettingsAsync(WaActor actor)
    {
        var s = await WaShared.SettingsAsync(_db);
        var rates = await _db.WaRates.AsNoTracking().OrderBy(r => r.Country == "IN" ? 0 : 1).ThenBy(r => r.Country).ThenBy(r => r.Category).ToListAsync();
        return WorkResult<WaSettingsDto>.Success(new WaSettingsDto
        {
            MonthlyBudget = s.MonthlyBudget,
            Currency = s.Currency,
            SendPerSecond = s.SendPerSecond,
            Rates = rates.Select(r => new WaRateDto { Country = r.Country, Category = r.Category, Rate = r.Rate, Currency = r.Currency, UpdatedAt = r.UpdatedAt }).ToList(),
        });
    }

    public async Task<WorkResult<WaSettingsDto>> SaveSettingsAsync(WaActor actor, WaSaveSettingsRequest request)
    {
        if (!actor.CanSettings) return NoSettings<WaSettingsDto>();
        if (request.MonthlyBudget is < 0) return WorkResult<WaSettingsDto>.Fail(400, "The budget cannot be negative.");
        if (request.SendPerSecond is < 1 or > 80) return WorkResult<WaSettingsDto>.Fail(400, "Send between 1 and 80 messages a second (WhatsApp's usual limit is 80).");
        var now = DateTime.UtcNow;
        var s = await _db.WaSettings.FirstOrDefaultAsync(x => x.SettingsId == 1);
        if (s == null)
        {
            s = new WaSettings { SettingsId = 1, Currency = "INR" };
            _db.WaSettings.Add(s);
        }
        s.MonthlyBudget = request.MonthlyBudget is > 0 ? request.MonthlyBudget : null;
        if (request.SendPerSecond is { } speed) s.SendPerSecond = speed;
        s.UpdatedByUserId = actor.UserId;
        s.UpdatedAt = now;

        if (request.Rates != null)
        {
            foreach (var r in request.Rates)
            {
                var country = (r.Country ?? string.Empty).Trim().ToUpperInvariant();
                var category = (r.Category ?? string.Empty).Trim().ToLowerInvariant();
                if (!(country == "**" || (country.Length == 2 && country.All(char.IsLetter)))) return WorkResult<WaSettingsDto>.Fail(400, $"\"{r.Country}\" is not a two-letter country code.");
                if (!RateCategories.Contains(category)) return WorkResult<WaSettingsDto>.Fail(400, $"\"{r.Category}\" is not a WhatsApp price category.");
                if (r.Rate < 0 || r.Rate > 1000) return WorkResult<WaSettingsDto>.Fail(400, "A rate must be between 0 and 1,000.");
            }
            var existing = await _db.WaRates.ToListAsync();
            foreach (var r in request.Rates)
            {
                var country = r.Country.Trim().ToUpperInvariant();
                var category = r.Category.Trim().ToLowerInvariant();
                var row = existing.FirstOrDefault(x => x.Country == country && x.Category == category);
                if (row == null)
                {
                    row = new WaRate { Country = country, Category = category };
                    _db.WaRates.Add(row);
                    existing.Add(row);
                }
                if (row.Rate != r.Rate || row.Currency != s.Currency)
                {
                    row.Rate = r.Rate;
                    row.Currency = s.Currency;
                    row.UpdatedByUserId = actor.UserId;
                    row.UpdatedAt = now;
                }
            }
            // Rates not sent back were removed on the screen.
            foreach (var gone in existing.Where(x => !request.Rates.Any(r => r.Country.Trim().ToUpperInvariant() == x.Country && r.Category.Trim().ToLowerInvariant() == x.Category)).ToList())
                _db.WaRates.Remove(gone);
        }
        await _db.SaveChangesAsync();
        return await SettingsAsync(actor);
    }

    public async Task<WorkResult<WaSpendSummary>> SpendAsync(WaActor actor, DateOnly from, DateOnly to)
    {
        if (to < from) return WorkResult<WaSpendSummary>.Fail(400, "The end date is before the start date.");
        if (to.DayNumber - from.DayNumber > 366) return WorkResult<WaSpendSummary>.Fail(400, "Choose a year at most.");
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = await _db.WaMessages.AsNoTracking()
            .Where(m => m.Direction == "Out" && m.Billable != null && m.StatusAt >= start && m.StatusAt < end && (m.Status == "delivered" || m.Status == "read"))
            .Select(m => new { m.StatusAt, m.PricingCategory, m.Billable, m.Cost, m.BroadcastId })
            .ToListAsync();
        var settings = await WaShared.SettingsAsync(_db);

        var days = new List<WaSpendDay>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var day = rows.Where(r => DateOnly.FromDateTime(r.StatusAt!.Value) == d).ToList();
            decimal Sum(string category) => day.Where(r => WaRules.RateCategory(r.PricingCategory) == category).Sum(r => r.Cost);
            var entry = new WaSpendDay
            {
                Date = d,
                Marketing = Sum("marketing"), Utility = Sum("utility"), Authentication = Sum("authentication"), Service = Sum("service"),
                Charged = day.Count(r => r.Billable == true), Free = day.Count(r => r.Billable == false),
            };
            entry.Total = entry.Marketing + entry.Utility + entry.Authentication + entry.Service;
            days.Add(entry);
        }

        var broadcastCosts = rows.Where(r => r.BroadcastId != null).GroupBy(r => r.BroadcastId!.Value)
            .Select(g => new { Id = g.Key, Cost = g.Sum(r => r.Cost), Delivered = g.Count() }).OrderByDescending(x => x.Cost).Take(20).ToList();
        var ids = broadcastCosts.Select(x => x.Id).ToList();
        var broadcastNames = await _db.WaBroadcasts.AsNoTracking().Where(b => ids.Contains(b.BroadcastId)).ToDictionaryAsync(b => b.BroadcastId, b => b.Name);

        var monthStart = WaRules.MonthStart(DateTime.UtcNow);
        var spentThisMonth = await _db.WaMessages.AsNoTracking().Where(m => m.Direction == "Out" && m.Cost > 0 && m.StatusAt >= monthStart).SumAsync(m => (decimal?)m.Cost) ?? 0m;
        var committed = await (from rec in _db.WaBroadcastRecipients.AsNoTracking()
                               join b in _db.WaBroadcasts.AsNoTracking() on rec.BroadcastId equals b.BroadcastId
                               where rec.Status == "Pending" && (b.Status == "Scheduled" || b.Status == "Sending" || b.Status == "Paused")
                               select (decimal?)rec.EstimatedCost).SumAsync() ?? 0m;

        return WorkResult<WaSpendSummary>.Success(new WaSpendSummary
        {
            From = from,
            To = to,
            Currency = settings.Currency,
            Total = days.Sum(d => d.Total),
            SpentThisMonth = spentThisMonth,
            Committed = committed,
            MonthlyBudget = settings.MonthlyBudget,
            Days = days,
            ByCategory = RateCategories.ToDictionary(c => c, c => rows.Where(r => WaRules.RateCategory(r.PricingCategory) == c).Sum(r => r.Cost)),
            ByBroadcast = broadcastCosts.Select(x => new WaSpendBroadcast { BroadcastId = x.Id, Name = broadcastNames.GetValueOrDefault(x.Id) ?? "(deleted)", Cost = x.Cost, Delivered = x.Delivered }).ToList(),
        });
    }

    public async Task<WorkResult<List<WaMetaSpendDay>>> MetaSpendAsync(WaActor actor, DateOnly from, DateOnly to)
    {
        if (to < from) return WorkResult<List<WaMetaSpendDay>>.Fail(400, "The end date is before the start date.");
        if (to.DayNumber - from.DayNumber > 92) return WorkResult<List<WaMetaSpendDay>>.Fail(400, "Meta's figures can be read three months at a time.");
        try
        {
            var points = await _meta.PricingAsync(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            var days = points
                .GroupBy(p => (Date: DateOnly.FromDateTime(p.Start), Category: WaRules.RateCategory(p.PricingCategory)))
                .Select(g => new WaMetaSpendDay { Date = g.Key.Date, Category = g.Key.Category, Volume = g.Sum(p => p.Volume), Cost = g.Sum(p => p.Cost) })
                .OrderBy(d => d.Date).ThenBy(d => d.Category).ToList();
            return WorkResult<List<WaMetaSpendDay>>.Success(days);
        }
        catch (Exception ex) when (ex is WaMetaException or WaNotConnectedException) { return FromMeta<List<WaMetaSpendDay>>(ex); }
    }
}
