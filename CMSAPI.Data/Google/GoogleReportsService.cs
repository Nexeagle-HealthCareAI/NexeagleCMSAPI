using System.Globalization;
using System.Text.Json;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.Google;

/// <summary>
/// Google → Overview, Pages and Ads. Numbers are read from Google Analytics, Search Console and Google Ads when a
/// screen asks and kept for 10 minutes; leads, demos and customers come from the CRM (GoogleLeads + CmsSalesLeads),
/// and Meta's spend from Social (SocialAdStats) for the side-by-side comparison.
/// </summary>
public class GoogleReportsService : IGoogleReportsService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    private static readonly string[] NotACampaign = { "(not set)", "(direct)", "(organic)", "(referral)", "(none)", "" };

    private readonly CmsDbContext _db;
    private readonly IGoogleApi _google;
    private readonly GoogleKeys _keys;
    private readonly IMemoryCache _cache;
    private readonly IServiceProvider _services;
    private readonly ILogger<GoogleReportsService> _logger;
    private readonly Func<DateTime> _now;

    public GoogleReportsService(CmsDbContext db, IGoogleApi google, GoogleKeys keys, IMemoryCache cache, IServiceProvider services, ILogger<GoogleReportsService> logger)
        : this(db, google, keys, cache, services, logger, () => DateTime.UtcNow) { }

    internal GoogleReportsService(CmsDbContext db, IGoogleApi google, GoogleKeys keys, IMemoryCache cache, IServiceProvider services, ILogger<GoogleReportsService> logger, Func<DateTime> now)
    {
        _db = db;
        _google = google;
        _keys = keys;
        _cache = cache;
        _services = services;
        _logger = logger;
        _now = now;
    }

    private static (DateOnly From, DateOnly To) Clamp(GoogleRange r, DateOnly today)
    {
        var to = r.To == default || r.To > today ? today : r.To;
        var from = r.From == default ? to.AddDays(-29) : r.From;
        if (from > to) from = to;
        if (to.DayNumber - from.DayNumber > 730) from = to.AddDays(-730);
        return (from, to);
    }

    /// <summary>The account and its Google sign-in, or the reason the screen cannot be shown.</summary>
    private async Task<(GoogleAccount? Account, GoogleClient? Client, string? Problem, int Status)> LoadAsync(Guid accountId)
    {
        var a = await _db.GoogleAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == accountId && x.IsActive);
        if (a == null) return (null, null, "This account does not exist.", 404);
        var c = _keys.Client(a);
        if (c == null) return (a, null, "Sign in with Google in Setup first.", 400);
        return (a, c, null, 200);
    }

    private async Task NoteAsync(Guid accountId, string? error)
    {
        try
        {
            var a = await _db.GoogleAccounts.FirstOrDefaultAsync(x => x.AccountId == accountId);
            if (a == null || a.LastReadError == error) return;
            a.LastReadError = error;
            await _db.SaveChangesAsync();
        }
        catch (Exception ex) { _logger.LogInformation("Google: could not note the read error: {Message}", ex.Message); }
    }

    private async Task<WorkResult<T>> CachedAsync<T>(string key, Guid accountId, Func<Task<T>> read)
    {
        if (_cache.TryGetValue(key, out T? hit) && hit != null) return WorkResult<T>.Success(hit);
        try
        {
            var value = await read();
            _cache.Set(key, value, CacheFor);
            await NoteAsync(accountId, null);
            return WorkResult<T>.Success(value);
        }
        catch (GoogleApiException ex)
        {
            await NoteAsync(accountId, GoogleShared.Trim(ex.Message, 1000));
            return WorkResult<T>.Fail(ex.SignInGone ? 401 : 502, ex.Message);
        }
    }

    private static int I(GaRow r, int metric) => (int)Math.Round(r.Metrics.ElementAtOrDefault(metric));

    // ── Overview ─────────────────────────────────────────────────────────────

    public async Task<WorkResult<GoogleOverviewDto>> OverviewAsync(GoogleRange range, CancellationToken ct = default)
    {
        var (a, c, problem, status) = await LoadAsync(range.AccountId);
        if (problem != null) return WorkResult<GoogleOverviewDto>.Fail(status, problem);
        if (string.IsNullOrEmpty(a!.Ga4PropertyId)) return WorkResult<GoogleOverviewDto>.Fail(400, "Choose the Google Analytics property in Setup first.");
        var (from, to) = Clamp(range, DateOnly.FromDateTime(_now()));
        var result = await CachedAsync($"google:overview:{a.AccountId}:{a.Ga4PropertyId}:{from}:{to}", a.AccountId, async () =>
        {
            var p = a.Ga4PropertyId!;
            var days = to.DayNumber - from.DayNumber + 1;
            var events = GoogleShared.SiteEvents(a.Site);
            var totalsTask = _google.RunReportAsync(c!, p, new GaReportRequest { From = from, To = to, Metrics = { "totalUsers", "sessions", "engagedSessions", "keyEvents" } }, ct);
            var previousTask = _google.RunReportAsync(c!, p, new GaReportRequest { From = from.AddDays(-days), To = from.AddDays(-1), Metrics = { "totalUsers" } }, ct);
            var dailyTask = _google.RunReportAsync(c!, p, new GaReportRequest { From = from, To = to, Dimensions = { "date" }, Metrics = { "sessions", "keyEvents" }, Limit = 800 }, ct);
            var channelsTask = _google.RunReportAsync(c!, p, new GaReportRequest { From = from, To = to, Dimensions = { "sessionDefaultChannelGroup" }, Metrics = { "sessions", "keyEvents" }, OrderByMetric = "sessions", Limit = 20 }, ct);
            var campaignsTask = _google.RunReportAsync(c!, p, new GaReportRequest { From = from, To = to, Dimensions = { "sessionCampaignName", "sessionSourceMedium" }, Metrics = { "sessions", "keyEvents" }, OrderByMetric = "sessions", Limit = 60 }, ct);
            var eventsTask = _google.RunReportAsync(c!, p, new GaReportRequest { From = from, To = to, Dimensions = { "eventName" }, Metrics = { "eventCount" }, InListDimension = "eventName", InListValues = events.Select(e => e.Name).ToList() }, ct);
            await Task.WhenAll(totalsTask, previousTask, dailyTask, channelsTask, campaignsTask, eventsTask);

            var t = totalsTask.Result.Rows.FirstOrDefault();
            var prev = previousTask.Result.Rows.FirstOrDefault();
            var byDay = dailyTask.Result.Rows.ToDictionary(
                r => DateOnly.TryParseExact(r.Dimensions[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : default, r => r);
            var counts = eventsTask.Result.Rows.ToDictionary(r => r.Dimensions[0], r => I(r, 0));
            return new GoogleOverviewDto
            {
                Users = t == null ? 0 : I(t, 0),
                Sessions = t == null ? 0 : I(t, 1),
                EngagedSessions = t == null ? 0 : I(t, 2),
                KeyEvents = t == null ? 0 : I(t, 3),
                PreviousUsers = prev == null ? null : I(prev, 0),
                Days = Enumerable.Range(0, days).Select(i => from.AddDays(i)).Select(d => byDay.TryGetValue(d, out var r)
                    ? new GoogleDayDto { Day = d, Sessions = I(r, 0), KeyEvents = I(r, 1) }
                    : new GoogleDayDto { Day = d }).ToList(),
                Channels = channelsTask.Result.Rows.Select(r => new GoogleChannelDto { Name = r.Dimensions[0], Sessions = I(r, 0), KeyEvents = I(r, 1) }).ToList(),
                Campaigns = campaignsTask.Result.Rows.Where(r => !NotACampaign.Contains(r.Dimensions[0]))
                    .Select(r => new GoogleCampaignDto { Name = r.Dimensions[0], SourceMedium = r.Dimensions[1], Sessions = I(r, 0), KeyEvents = I(r, 1) }).Take(12).ToList(),
                Events = events.Select(e => new GoogleEventDto { Name = e.Name, Label = e.Label, IsLead = e.IsLead, Count = counts.GetValueOrDefault(e.Name) }).ToList(),
                ReadAt = _now(),
            };
        });
        if (result.Ok && result.Data != null) result.Data.OwnVisits = await OwnVisitsAsync(a.Site, from, to);
        return result;
    }

    /// <summary>The CMS's own Site Visits count for the same days (every visitor, not only those who accepted tracking).</summary>
    private async Task<int?> OwnVisitsAsync(string? site, DateOnly from, DateOnly to)
    {
        if (site == null || _services.GetService<IInsightsService>() is not { } insights) return null;
        try { return (await insights.GetSiteVisitStatsAsync(from, to, site)).Visits; }
        catch (Exception ex) { _logger.LogInformation("Google overview: no Site Visits count ({Message}).", ex.Message); return null; }
    }

    // ── Pages ────────────────────────────────────────────────────────────────

    private static string? SiteUrl(GoogleAccount a) =>
        GoogleShared.SiteUrl(a.SearchConsoleSite) ?? (a.Site == "nexeagle" ? "https://nexeagle.com" : null);

    public async Task<WorkResult<GooglePagesDto>> PagesAsync(GoogleRange range, CancellationToken ct = default)
    {
        var (a, c, problem, status) = await LoadAsync(range.AccountId);
        if (problem != null) return WorkResult<GooglePagesDto>.Fail(status, problem);
        if (string.IsNullOrEmpty(a!.Ga4PropertyId)) return WorkResult<GooglePagesDto>.Fail(400, "Choose the Google Analytics property in Setup first.");
        var (from, to) = Clamp(range, DateOnly.FromDateTime(_now()));
        return await CachedAsync($"google:pages:{a.AccountId}:{a.Ga4PropertyId}:{from}:{to}", a.AccountId, async () =>
        {
            var contacts = GoogleShared.SiteEvents(a.Site).Where(e => !e.IsLead && e.Name.EndsWith("_click", StringComparison.Ordinal)).Select(e => e.Name).ToList();
            var pagesTask = _google.RunReportAsync(c!, a.Ga4PropertyId!, new GaReportRequest
            {
                From = from, To = to, Dimensions = { "landingPage" }, Metrics = { "sessions", "engagedSessions", "userEngagementDuration", "keyEvents" }, OrderByMetric = "sessions", Limit = 100,
            }, ct);
            var contactsTask = contacts.Count == 0 ? Task.FromResult(new GaReport()) : _google.RunReportAsync(c!, a.Ga4PropertyId!, new GaReportRequest
            {
                From = from, To = to, Dimensions = { "landingPage", "eventName" }, Metrics = { "eventCount" }, InListDimension = "eventName", InListValues = contacts, Limit = 2000,
            }, ct);
            await Task.WhenAll(pagesTask, contactsTask);
            var contactsByPage = contactsTask.Result.Rows.GroupBy(r => r.Dimensions[0]).ToDictionary(g => g.Key, g => g.Sum(r => I(r, 0)));
            return new GooglePagesDto
            {
                SiteUrl = SiteUrl(a),
                ReadAt = _now(),
                Pages = pagesTask.Result.Rows.Where(r => r.Dimensions[0] is not ("(not set)" or "")).Select(r =>
                {
                    var sessions = I(r, 0);
                    return new GooglePageDto
                    {
                        Path = r.Dimensions[0], Sessions = sessions, EngagedSessions = I(r, 1),
                        AvgSeconds = sessions == 0 ? 0 : Math.Round(r.Metrics.ElementAtOrDefault(2) / sessions, 1),
                        KeyEvents = I(r, 3), Contacts = contactsByPage.GetValueOrDefault(r.Dimensions[0]),
                    };
                }).ToList(),
            };
        });
    }

    public async Task<WorkResult<GooglePageDetailDto>> PageAsync(GoogleRange range, string path, CancellationToken ct = default)
    {
        var (a, c, problem, status) = await LoadAsync(range.AccountId);
        if (problem != null) return WorkResult<GooglePageDetailDto>.Fail(status, problem);
        if (string.IsNullOrEmpty(a!.Ga4PropertyId)) return WorkResult<GooglePageDetailDto>.Fail(400, "Choose the Google Analytics property in Setup first.");
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') || path.Length > 500) return WorkResult<GooglePageDetailDto>.Fail(400, "Choose a page.");
        var (from, to) = Clamp(range, DateOnly.FromDateTime(_now()));
        return await CachedAsync($"google:page:{a.AccountId}:{from}:{to}:{path}", a.AccountId, async () =>
        {
            var sources = await _google.RunReportAsync(c!, a.Ga4PropertyId!, new GaReportRequest
            {
                From = from, To = to, Dimensions = { "sessionDefaultChannelGroup" }, Metrics = { "sessions" }, OrderByMetric = "sessions", Limit = 10,
                ExactDimension = "landingPage", ExactValue = path,
            }, ct);
            var detail = new GooglePageDetailDto
            {
                Path = path,
                SearchConsoleConnected = !string.IsNullOrEmpty(a.SearchConsoleSite),
                Sources = sources.Rows.Select(r => new GoogleChannelDto { Name = r.Dimensions[0], Sessions = I(r, 0) }).ToList(),
            };
            if (detail.SearchConsoleConnected && SiteUrl(a) is { } site)
            {
                try { detail.Queries = await _google.SearchConsoleQueriesAsync(c!, a.SearchConsoleSite!, from, to, site + path, 10, ct); }
                catch (GoogleApiException ex) { _logger.LogInformation("Search Console for {Path}: {Message}", path, ex.Message); }
            }
            return detail;
        });
    }

    // ── Ads ──────────────────────────────────────────────────────────────────

    private static decimal Money(JsonElement metrics) => (decimal)(GoogleApi.Num(metrics, "costMicros") ?? 0) / 1_000_000m;
    private static long Long(JsonElement e, string name) => (long)(GoogleApi.Num(e, name) ?? 0);

    private static string Gaql(string from, string to, string select, string resource, string? extra = null) =>
        $"SELECT {select}, metrics.cost_micros, metrics.clicks, metrics.impressions, metrics.conversions FROM {resource} WHERE segments.date BETWEEN '{from}' AND '{to}'{(extra != null ? " AND " + extra : "")}";

    public async Task<WorkResult<GoogleAdsDto>> AdsAsync(GoogleAdsQuery q, CancellationToken ct = default)
    {
        var (a, c, problem, status) = await LoadAsync(q.AccountId);
        if (problem != null) return WorkResult<GoogleAdsDto>.Fail(status, problem);
        if (string.IsNullOrEmpty(a!.AdsCustomerId)) return WorkResult<GoogleAdsDto>.Fail(400, "Choose the Google Ads account in Setup first.");
        var (from, to) = Clamp(q, DateOnly.FromDateTime(_now()));
        var level = q.Level is "adgroup" or "keyword" or "search" ? q.Level : "campaign";
        var days = to.DayNumber - from.DayNumber + 1;
        var pFrom = from.AddDays(-days);
        var pTo = from.AddDays(-1);
        var f = from.ToString("yyyy-MM-dd");
        var t = to.ToString("yyyy-MM-dd");

        var google = await CachedAsync($"google:ads:{a.AccountId}:{a.AdsCustomerId}:{from}:{to}:{level}", a.AccountId, async () =>
        {
            var cid = a.AdsCustomerId!;
            var login = a.AdsLoginCustomerId;
            var dailyTask = _google.AdsSearchAsync(c!, cid, login, Gaql(f, t, "segments.date", "customer"), ct);
            var previousTask = _google.AdsSearchAsync(c!, cid, login, Gaql(pFrom.ToString("yyyy-MM-dd"), pTo.ToString("yyyy-MM-dd"), "customer.id", "customer"), ct);
            var budgetsTask = _google.AdsSearchAsync(c!, cid, login, "SELECT campaign.id, campaign_budget.amount_micros FROM campaign WHERE campaign.status = 'ENABLED'", ct);
            var rowsTask = _google.AdsSearchAsync(c!, cid, login, level switch
            {
                "adgroup" => Gaql(f, t, "ad_group.id, ad_group.name, ad_group.status, campaign.name", "ad_group", "ad_group.status != 'REMOVED'"),
                "keyword" => Gaql(f, t, "ad_group_criterion.criterion_id, ad_group_criterion.keyword.text, ad_group_criterion.keyword.match_type, ad_group_criterion.status, ad_group.id, ad_group.name", "keyword_view", "ad_group_criterion.status != 'REMOVED'"),
                "search" => Gaql(f, t, "search_term_view.search_term, search_term_view.status, ad_group.name", "search_term_view") + " ORDER BY metrics.cost_micros DESC LIMIT 300",
                _ => Gaql(f, t, "campaign.id, campaign.name, campaign.status", "campaign", "campaign.status != 'REMOVED'"),
            }, ct);
            await Task.WhenAll(dailyTask, previousTask, budgetsTask, rowsTask);

            var byDay = dailyTask.Result.GroupBy(r => GoogleApi.Str(GoogleApi.Obj(r, "segments"), "date") ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.Sum(r => Money(GoogleApi.Obj(r, "metrics"))));
            var m = dailyTask.Result.Select(r => GoogleApi.Obj(r, "metrics")).ToList();
            var dto = new GoogleAdsDto
            {
                Currency = a.Currency ?? "INR",
                Spend = Math.Round(m.Sum(Money), 2),
                Clicks = m.Sum(x => Long(x, "clicks")),
                Impressions = m.Sum(x => Long(x, "impressions")),
                Conversions = Math.Round(m.Sum(x => GoogleApi.Num(x, "conversions") ?? 0), 2),
                DailyBudgets = budgetsTask.Result.Count == 0 ? null : Math.Round(budgetsTask.Result.Sum(r => (decimal)(GoogleApi.Num(GoogleApi.Obj(r, "campaignBudget"), "amountMicros") ?? 0) / 1_000_000m), 2),
                Days = Enumerable.Range(0, days).Select(i => from.AddDays(i)).Select(d => new GoogleAdsDayDto { Day = d, Spend = Math.Round(byDay.GetValueOrDefault(d.ToString("yyyy-MM-dd")), 2) }).ToList(),
                PreviousCostPerLead = Math.Round(previousTask.Result.Sum(r => Money(GoogleApi.Obj(r, "metrics"))), 2),   // spend for now; turned into cost per lead below
                Rows = rowsTask.Result.Select(r => Row(level, r)).Where(r => r.Spend > 0 || r.Clicks > 0 || r.Impressions > 0).OrderByDescending(r => r.Spend).ToList(),
                ReadAt = _now(),
            };
            return dto;
        });
        if (!google.Ok || google.Data == null) return google;

        // The CRM side is never cached: a lead can move to Demo Done at any moment.
        var copy = GoogleShared.Read<GoogleAdsDto>(GoogleShared.Write(google.Data))!;
        var previousSpend = copy.PreviousCostPerLead ?? 0;
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var leads = await _db.GoogleLeads.AsNoTracking()
            .Where(l => l.AccountId == a.AccountId && l.ReceivedAt >= start && l.ReceivedAt < end && (l.Source == "form" || l.Gclid != null || l.Gbraid != null || l.Wbraid != null))
            .Select(l => new { l.LeadId, l.Source, l.CampaignId, l.CampaignName }).ToListAsync(ct);
        var ids = leads.Select(l => l.LeadId).ToList();
        var stages = await _db.CmsSalesLeads.AsNoTracking().Where(x => ids.Contains(x.LeadId) && !x.IsDeleted).Select(x => new { x.LeadId, x.Stage }).ToListAsync(ct);
        var pStart = pFrom.ToDateTime(TimeOnly.MinValue);
        var previousLeads = await _db.GoogleLeads.AsNoTracking()
            .CountAsync(l => l.AccountId == a.AccountId && l.ReceivedAt >= pStart && l.ReceivedAt < start && (l.Source == "form" || l.Gclid != null || l.Gbraid != null || l.Wbraid != null), ct);

        copy.CrmLeads = leads.Count;
        copy.FormLeads = leads.Count(l => l.Source == "form");
        copy.WebLeads = leads.Count(l => l.Source == "web");
        copy.CostPerLead = leads.Count == 0 ? null : Math.Round(copy.Spend / leads.Count, 0);
        copy.PreviousCostPerLead = previousLeads == 0 ? null : Math.Round(previousSpend / previousLeads, 0);
        copy.Customers = stages.Count(s => s.Stage == "Closed Won");
        if (level == "campaign")
            foreach (var row in copy.Rows)
            {
                row.CrmLeads = leads.Count(l => l.CampaignId == row.Id || (l.CampaignId == null && l.CampaignName != null && string.Equals(l.CampaignName, row.Name, StringComparison.OrdinalIgnoreCase)));
                row.CostPerLead = row.CrmLeads > 0 ? Math.Round(row.Spend / row.CrmLeads.Value, 0) : null;
            }
        else
            foreach (var row in copy.Rows)
                row.CostPerLead = row.Conversions > 0 ? Math.Round(row.Spend / (decimal)row.Conversions, 0) : null;

        copy.Platforms.Add(new GooglePlatformDto
        {
            Platform = "google", Spend = copy.Spend, Leads = leads.Count, CostPerLead = copy.CostPerLead,
            ReachedDemo = stages.Count(s => GoogleShared.DemoStages.Contains(s.Stage)), Customers = copy.Customers,
            CostPerCustomer = copy.Customers > 0 ? Math.Round(copy.Spend / copy.Customers, 0) : null,
        });
        if (await MetaAsync(a, start, end, ct) is { } meta) copy.Platforms.Add(meta);
        return WorkResult<GoogleAdsDto>.Success(copy);
    }

    /// <summary>Meta's spend and lead-form leads for the same days, from the Social account with the same name.</summary>
    private async Task<GooglePlatformDto?> MetaAsync(GoogleAccount a, DateTime start, DateTime end, CancellationToken ct)
    {
        var name = a.Name.Trim().ToLower();
        var social = await _db.SocialAccounts.AsNoTracking().Where(s => s.IsActive && s.Name.ToLower() == name).Select(s => s.AccountId).FirstOrDefaultAsync(ct);
        if (social == Guid.Empty) return null;
        var spend = await _db.SocialAdStats.AsNoTracking().Where(s => s.AccountId == social && s.Level == "campaign" && s.Day >= start && s.Day < end).SumAsync(s => s.Spend, ct);
        var leadIds = await _db.SocialLeads.AsNoTracking().Where(l => l.AccountId == social && l.Source == "form" && l.ReceivedAt >= start && l.ReceivedAt < end).Select(l => l.LeadId).ToListAsync(ct);
        if (spend == 0 && leadIds.Count == 0) return null;
        var stages = await _db.CmsSalesLeads.AsNoTracking().Where(x => leadIds.Contains(x.LeadId) && !x.IsDeleted).Select(x => x.Stage).ToListAsync(ct);
        var customers = stages.Count(s => s == "Closed Won");
        return new GooglePlatformDto
        {
            Platform = "meta", Spend = spend, Leads = leadIds.Count, CostPerLead = leadIds.Count > 0 ? Math.Round(spend / leadIds.Count, 0) : null,
            ReachedDemo = stages.Count(s => GoogleShared.DemoStages.Contains(s)), Customers = customers,
            CostPerCustomer = customers > 0 ? Math.Round(spend / customers, 0) : null,
        };
    }

    private static string Status(string? s) => s switch { "ENABLED" => "Active", "PAUSED" => "Paused", "ADDED" => "Added", "EXCLUDED" => "Excluded", "NONE" or null => "Not added", _ => s.ToLowerInvariant() };

    private static GoogleAdsRowDto Row(string level, JsonElement r)
    {
        var metrics = GoogleApi.Obj(r, "metrics");
        var row = new GoogleAdsRowDto
        {
            Spend = Math.Round(Money(metrics), 2), Clicks = Long(metrics, "clicks"), Impressions = Long(metrics, "impressions"),
            Conversions = Math.Round(GoogleApi.Num(metrics, "conversions") ?? 0, 2),
        };
        switch (level)
        {
            case "adgroup":
                var g = GoogleApi.Obj(r, "adGroup");
                row.Id = GoogleApi.Str(g, "id") ?? string.Empty;
                row.Name = GoogleApi.Str(g, "name") ?? row.Id;
                row.Status = Status(GoogleApi.Str(g, "status"));
                row.Sub = GoogleApi.Str(GoogleApi.Obj(r, "campaign"), "name");
                break;
            case "keyword":
                var k = GoogleApi.Obj(r, "adGroupCriterion");
                var kw = GoogleApi.Obj(k, "keyword");
                var text = GoogleApi.Str(kw, "text") ?? string.Empty;
                row.Id = (GoogleApi.Str(GoogleApi.Obj(r, "adGroup"), "id") ?? "") + "~" + (GoogleApi.Str(k, "criterionId") ?? "");
                row.Name = GoogleApi.Str(kw, "matchType") switch { "EXACT" => $"[{text}]", "PHRASE" => $"\"{text}\"", _ => text };
                row.Status = Status(GoogleApi.Str(k, "status"));
                row.Sub = $"{(GoogleApi.Str(kw, "matchType") ?? "").ToLowerInvariant()} · {GoogleApi.Str(GoogleApi.Obj(r, "adGroup"), "name")}";
                break;
            case "search":
                var s = GoogleApi.Obj(r, "searchTermView");
                row.Name = GoogleApi.Str(s, "searchTerm") ?? string.Empty;
                row.Id = row.Name;
                row.Status = Status(GoogleApi.Str(s, "status"));
                row.Sub = GoogleApi.Str(GoogleApi.Obj(r, "adGroup"), "name");
                break;
            default:
                var cmp = GoogleApi.Obj(r, "campaign");
                row.Id = GoogleApi.Str(cmp, "id") ?? string.Empty;
                row.Name = GoogleApi.Str(cmp, "name") ?? row.Id;
                row.Status = Status(GoogleApi.Str(cmp, "status"));
                break;
        }
        return row;
    }
}
