using System.Globalization;
using System.Text.RegularExpressions;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Data.Google;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.Keywords;

/// <summary>
/// CMS → Keywords, the daily collection (KeywordWorker, 05:30 IST; or "Run now" in Setup):
///  * site:    Doctor Dekho's own site searches, only phrases typed by at least MinVisitors (5+) different visitors
///             in 28 days; doctor-name searches and anything that looks like a phone number or email are skipped.
///  * gsc:     Search Console, India, by search and page, per day (finished days: 2 to 4 days back; 30 days the first time).
///  * ads:     Google Ads search terms, per day (the same days).
///  * planner: Google Keyword Planner ideas and monthly volumes for India, in rotation within a daily request budget.
/// Each phrase becomes one Keyword (tagged by KeywordText); each day's numbers one KeywordObservation. Keywords that
/// Search Console shows on a page are put in a suggested topic for that page unless a person chose their topic.
/// </summary>
public class KeywordCollector : IKeywordCollector
{
    public const int SearchConsoleMaxRows = 50_000;
    public const int PlannerBudget = 150;
    public const int PlannerIdeasPerRequest = 100;
    public const int PlannerMinVolume = 10;
    private const string EventSearch = "search_performed";

    private readonly CmsDbContext _db;
    /// <summary>The HMS tracking database, where Doctor Dekho's search_performed events are (read-only, as in Search Insights).</summary>
    private readonly AppDbContext _events;
    private readonly IGoogleApi _google;
    private readonly GoogleKeys _keys;
    private readonly ILogger<KeywordCollector> _logger;
    private readonly Func<DateTime> _now;
    /// <summary>Pause between Keyword Planner calls (Google allows 1 a second per account). Tests set 0.</summary>
    internal int PlannerDelayMs { get; set; } = 1100;

    public KeywordCollector(CmsDbContext db, AppDbContext events, IGoogleApi google, GoogleKeys keys, ILogger<KeywordCollector> logger)
        : this(db, events, google, keys, logger, () => DateTime.UtcNow) { }

    internal KeywordCollector(CmsDbContext db, AppDbContext events, IGoogleApi google, GoogleKeys keys, ILogger<KeywordCollector> logger, Func<DateTime> now)
    {
        _db = db;
        _events = events;
        _google = google;
        _keys = keys;
        _logger = logger;
        _now = now;
    }

    /// <summary>True when today's run (IST) is due: after 05:30 IST and not yet run today.</summary>
    public static bool Due(DateTime utcNow, DateTime? lastRunUtc)
    {
        var ist = utcNow + TrackingTime.IstOffset;
        if (ist.TimeOfDay < new TimeSpan(5, 30, 0)) return false;
        return lastRunUtc == null || TrackingTime.IstDate(lastRunUtc.Value) < DateOnly.FromDateTime(ist);
    }

    public async Task<Dictionary<string, int>> RunAsync(string? only = null, CancellationToken ct = default)
    {
        var settings = await KeywordShared.SettingsAsync(_db, ct);
        var result = new Dictionary<string, int>();
        foreach (var source in new[] { "site", "gsc", "ads", "planner" })
        {
            if (only != null && only != source) continue;
            var run = await _db.KeywordSourceRuns.FirstOrDefaultAsync(r => r.SourceKey == source, ct);
            if (run == null) { run = new KeywordSourceRun { SourceKey = source }; _db.KeywordSourceRuns.Add(run); }
            run.LastRunAt = _now();
            try
            {
                var (count, detail) = source switch
                {
                    "site" => await SiteSearchesAsync(settings, ct),
                    "gsc" => await SearchConsoleAsync(settings, ct),
                    "ads" => await AdsSearchTermsAsync(settings, ct),
                    _ => await PlannerAsync(settings, ct),
                };
                run.LastCount = count;
                run.LastDetail = KeywordShared.Trim(detail, 300);
                run.LastError = null;
                run.LastOkAt = _now();
                result[source] = count;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Keywords: {Source} failed.", source);
                run.LastError = KeywordShared.Trim(ex is GoogleApiException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}", 1000);
                result[source] = -1;
            }
            await _db.SaveChangesAsync(ct);
        }
        if (only is null or "gsc") await SuggestTopicsAsync(ct);
        return result;
    }

    // ── keywords and observations ────────────────────────────────────────────

    private readonly Dictionary<(string Site, string Norm), Keyword> _cache = new();

    private async Task LoadAsync(string site, IEnumerable<string> normalized, CancellationToken ct)
    {
        var want = normalized.Where(n => !_cache.ContainsKey((site, n))).Distinct().ToList();
        foreach (var chunk in want.Chunk(1000))
        {
            var list = chunk.ToList();
            foreach (var k in await _db.Keywords.Where(k => k.Site == site && list.Contains(k.Normalized)).ToListAsync(ct))
                _cache[(site, k.Normalized)] = k;
        }
    }

    private Keyword Keyword(string site, string normalized, string source, IReadOnlyCollection<string> extraCities)
    {
        var now = _now();
        if (!_cache.TryGetValue((site, normalized), out var k))
        {
            k = new Keyword
            {
                KeywordId = Guid.NewGuid(), Site = site, Text = normalized, Normalized = normalized,
                Language = KeywordText.Language(normalized), Intent = KeywordText.Intent(site, normalized), City = KeywordText.City(normalized, extraCities),
                FirstSeenAt = now, LastSeenAt = now,
            };
            _db.Keywords.Add(k);
            _cache[(site, normalized)] = k;
        }
        k.LastSeenAt = now;
        var sources = k.Sources.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (sources.Add(source)) k.Sources = string.Join(',', sources.OrderBy(x => x));
        return k;
    }

    /// <summary>Writes one day's numbers for many keywords of one source, replacing what that day had.</summary>
    private async Task WriteAsync(string source, DateOnly day, Dictionary<Guid, KeywordObservation> values, CancellationToken ct)
    {
        var ids = values.Keys.ToList();
        var existing = new Dictionary<Guid, KeywordObservation>();
        foreach (var chunk in ids.Chunk(1000))
        {
            var list = chunk.ToList();
            foreach (var o in await _db.KeywordObservations.Where(o => o.Day == day && o.Source == source && list.Contains(o.KeywordId)).ToListAsync(ct))
                existing[o.KeywordId] = o;
        }
        foreach (var (id, v) in values)
        {
            if (!existing.TryGetValue(id, out var o))
            {
                o = new KeywordObservation { KeywordId = id, Day = day, Source = source };
                _db.KeywordObservations.Add(o);
            }
            o.Impressions = v.Impressions; o.Clicks = v.Clicks; o.Position = v.Position; o.Visitors = v.Visitors;
            o.Cost = v.Cost; o.Conversions = v.Conversions; o.Page = v.Page;
        }
    }

    private static readonly Regex Private = new(@"\d{6,}|@|\b\d{3}\s?\d{3}\s?\d{4}\b|\b(gmail|yahoo|outlook|hotmail|rediffmail|icloud)\s(com|co\s?in|in)\b", RegexOptions.Compiled);

    /// <summary>
    /// A phrase that may hold someone's phone number or email, or is too short to mean anything. Checks the text as
    /// typed as well, because normalising turns "@" and "." into spaces.
    /// </summary>
    public static bool Unusable(string normalized, string? raw = null) =>
        normalized.Length < 2 || normalized.Length > 120 || Private.IsMatch(normalized) || (raw != null && Private.IsMatch(raw));

    // ── Doctor Dekho site searches ───────────────────────────────────────────

    private async Task<(int, string)> SiteSearchesAsync(KeywordSettingsView settings, CancellationToken ct)
    {
        var end = TrackingTime.IstDate(_now()).AddDays(-1);
        var start = end.AddDays(-27);
        var rows = await _events.AnalyticsEvents.AsNoTracking()
            .Where(e => !e.IsAutomated && e.EventType == EventSearch && e.OccurredAt >= TrackingTime.StartUtc(start) && e.OccurredAt < TrackingTime.EndUtcExclusive(end))
            .Select(e => new { e.EventId, e.SessionId, e.OccurredAt, e.MetadataJson })
            .ToListAsync(ct);
        var searches = new List<(string Norm, string Visitor, DateOnly Day)>();
        foreach (var e in rows)
        {
            var meta = DoctorDekhoSearchMeta.Parse(e.MetadataJson);
            if (string.Equals(meta.SearchKind, "doctor_name", StringComparison.OrdinalIgnoreCase)) continue;  // names of doctors: not content
            var norm = KeywordText.Normalize(meta.Query);
            if (Unusable(norm, meta.Query)) continue;
            searches.Add((norm, e.SessionId ?? e.EventId.ToString(), TrackingTime.IstDate(e.OccurredAt)));
        }
        // Only phrases typed by enough different visitors are kept, so a rare search can never point at a person.
        var qualifying = searches.GroupBy(s => s.Norm)
            .Where(g => g.Select(s => s.Visitor).Distinct().Count() >= settings.MinVisitors)
            .ToDictionary(g => g.Key, g => g.ToList());
        await LoadAsync(KeywordSites.DoctorDekho, qualifying.Keys, ct);
        var byDay = new Dictionary<DateOnly, Dictionary<Guid, KeywordObservation>>();
        foreach (var (norm, list) in qualifying)
        {
            var k = Keyword(KeywordSites.DoctorDekho, norm, "site", settings.ExtraCities);
            foreach (var day in list.GroupBy(s => s.Day))
            {
                if (!byDay.TryGetValue(day.Key, out var map)) byDay[day.Key] = map = new();
                map[k.KeywordId] = new KeywordObservation { Visitors = day.Select(s => s.Visitor).Distinct().Count() };
            }
        }
        foreach (var (day, map) in byDay) await WriteAsync("site", day, map, ct);
        return (qualifying.Count, $"{qualifying.Count} phrases typed by {settings.MinVisitors}+ visitors in 28 days");
    }

    // ── Search Console ───────────────────────────────────────────────────────

    /// <summary>The days to read: finished days (2 to 4 back), or the last 30 the first time.</summary>
    private async Task<List<DateOnly>> DaysAsync(string site, string source, CancellationToken ct)
    {
        var last = TrackingTime.IstDate(_now()).AddDays(-2);
        var first = await _db.KeywordObservations.AnyAsync(o => o.Source == source && _db.Keywords.Any(k => k.KeywordId == o.KeywordId && k.Site == site), ct)
            ? last.AddDays(-2) : last.AddDays(-29);
        var days = new List<DateOnly>();
        for (var d = first; d <= last; d = d.AddDays(1)) days.Add(d);
        return days;
    }

    private async Task<List<GoogleAccount>> AccountsAsync(CancellationToken ct) =>
        (await _db.GoogleAccounts.AsNoTracking().Where(a => a.IsActive && a.Site != null).OrderBy(a => a.SortOrder).ToListAsync(ct))
            .Where(a => KeywordSites.Valid(a.Site)).ToList();

    private async Task<(int, string)> SearchConsoleAsync(KeywordSettingsView settings, CancellationToken ct)
    {
        var total = 0;
        var notes = new List<string>();
        foreach (var a in await AccountsAsync(ct))
        {
            if (string.IsNullOrEmpty(a.SearchConsoleSite)) continue;
            var client = _keys.Client(a) ?? throw new GoogleApiException($"{a.Name}: sign in with Google again in Google → Setup.");
            var site = a.Site!;
            var rows = 0;
            foreach (var day in await DaysAsync(site, "gsc", ct))
            {
                var data = await _google.SearchConsoleDayAsync(client, a.SearchConsoleSite, day, SearchConsoleMaxRows, ct);
                var byQuery = data.Select(r => (Norm: KeywordText.Normalize(r.Query), Row: r)).Where(x => !Unusable(x.Norm, x.Row.Query)).GroupBy(x => x.Norm).ToList();
                await LoadAsync(site, byQuery.Select(g => g.Key), ct);
                var map = new Dictionary<Guid, KeywordObservation>();
                foreach (var g in byQuery)
                {
                    var k = Keyword(site, g.Key, "gsc", settings.ExtraCities);
                    var imp = g.Sum(x => x.Row.Impressions);
                    map[k.KeywordId] = new KeywordObservation
                    {
                        Impressions = (int)Math.Round(imp), Clicks = (int)Math.Round(g.Sum(x => x.Row.Clicks)),
                        Position = imp > 0 ? Math.Round((decimal)(g.Sum(x => x.Row.Position * x.Row.Impressions) / imp), 2) : null,
                        Page = KeywordShared.Trim(g.OrderByDescending(x => x.Row.Impressions).First().Row.Page, 500),
                    };
                }
                await WriteAsync("gsc", day, map, ct);
                await _db.SaveChangesAsync(ct);
                rows += data.Count;
            }
            total += rows;
            notes.Add($"{a.Name}: {rows:N0} rows");
        }
        if (notes.Count == 0) throw new InvalidOperationException("No Search Console site is chosen. Choose one in Google → Setup.");
        return (total, string.Join(" · ", notes));
    }

    // ── Google Ads search terms ──────────────────────────────────────────────

    private async Task<(int, string)> AdsSearchTermsAsync(KeywordSettingsView settings, CancellationToken ct)
    {
        var total = 0;
        var notes = new List<string>();
        foreach (var a in await AccountsAsync(ct))
        {
            if (string.IsNullOrEmpty(a.AdsCustomerId)) continue;
            var client = _keys.Client(a) ?? throw new GoogleApiException($"{a.Name}: sign in with Google again in Google → Setup.");
            var site = a.Site!;
            var days = await DaysAsync(site, "ads", ct);
            var gaql = "SELECT search_term_view.search_term, segments.date, metrics.impressions, metrics.clicks, metrics.cost_micros, metrics.conversions " +
                       $"FROM search_term_view WHERE segments.date BETWEEN '{days[0]:yyyy-MM-dd}' AND '{days[^1]:yyyy-MM-dd}'";
            var rows = await _google.AdsSearchAsync(client, a.AdsCustomerId, a.AdsLoginCustomerId, gaql, ct);
            var parsed = rows.Select(r =>
            {
                var term = GoogleApi.Str(GoogleApi.Obj(r, "searchTermView"), "searchTerm");
                var date = DateOnly.TryParse(GoogleApi.Str(GoogleApi.Obj(r, "segments"), "date"), CultureInfo.InvariantCulture, out var d) ? d : default;
                var m = GoogleApi.Obj(r, "metrics");
                return (Raw: term, Norm: KeywordText.Normalize(term), Day: date, Imp: (int)(GoogleApi.Num(m, "impressions") ?? 0), Clicks: (int)(GoogleApi.Num(m, "clicks") ?? 0),
                    Cost: (decimal)((GoogleApi.Num(m, "costMicros") ?? 0) / 1_000_000), Conv: (decimal)(GoogleApi.Num(m, "conversions") ?? 0));
            }).Where(x => x.Day != default && !Unusable(x.Norm, x.Raw)).ToList();
            await LoadAsync(site, parsed.Select(x => x.Norm), ct);
            foreach (var day in parsed.GroupBy(x => x.Day))
            {
                var map = new Dictionary<Guid, KeywordObservation>();
                foreach (var g in day.GroupBy(x => x.Norm))
                {
                    var k = Keyword(site, g.Key, "ads", settings.ExtraCities);
                    map[k.KeywordId] = new KeywordObservation { Impressions = g.Sum(x => x.Imp), Clicks = g.Sum(x => x.Clicks), Cost = g.Sum(x => x.Cost), Conversions = g.Sum(x => x.Conv) };
                }
                await WriteAsync("ads", day.Key, map, ct);
            }
            await _db.SaveChangesAsync(ct);
            total += parsed.Count;
            notes.Add($"{a.Name}: {parsed.Count:N0} search terms");
        }
        if (notes.Count == 0) throw new InvalidOperationException("No Google Ads account is chosen. Choose one in Google → Setup.");
        return (total, string.Join(" · ", notes));
    }

    // ── Keyword Planner ──────────────────────────────────────────────────────

    public static string LanguageId(string language) => language switch { "hi" => "1023", "bn" => "1056", _ => "1000" };

    /// <summary>The languages Keyword Planner is asked in for a site: Doctor Dekho in English, Hindi and Bengali.</summary>
    public static string[] PlannerLanguages(string site) => site == KeywordSites.DoctorDekho ? new[] { "1000", "1023", "1056" } : new[] { "1000" };

    internal record PlannerUnit(string Site, List<string> Seeds, string LanguageId, string? SiteSeed, bool Refresh);

    /// <summary>The day's Keyword Planner work: volumes for keywords that have none (or a stale one) first, then seed lists in rotation, and competitor sites on Mondays.</summary>
    internal async Task<List<PlannerUnit>> PlannerUnitsAsync(KeywordSettingsView settings, CancellationToken ct)
    {
        var units = new List<PlannerUnit>();
        var stale = _now().AddDays(-30);
        var refresh = await _db.Keywords.AsNoTracking()
            .Where(k => k.Status == "active" && ((k.VolumeAt == null && !k.Sources.Contains("planner")) || k.VolumeAt < stale))
            .OrderByDescending(k => k.LastSeenAt).Take(PlannerBudget / 2 * 20)
            .Select(k => new { k.Site, k.Language, k.Normalized }).ToListAsync(ct);
        foreach (var g in refresh.GroupBy(k => (k.Site, Lang: LanguageId(k.Language))))
            foreach (var chunk in g.Select(k => k.Normalized).Chunk(20))
                units.Add(new PlannerUnit(g.Key.Site, chunk.ToList(), g.Key.Lang, null, true));
        units = units.Take(PlannerBudget / 2).ToList();

        var rotation = new List<PlannerUnit>();
        foreach (var site in KeywordSites.All)
            foreach (var group in settings.Seeds.GetValueOrDefault(site, new List<string>()).Chunk(5))
                foreach (var lang in PlannerLanguages(site))
                    rotation.Add(new PlannerUnit(site, group.ToList(), lang, null, false));
        if (rotation.Count > 0)
        {
            var start = settings.PlannerCursor % rotation.Count;
            units.AddRange(rotation.Skip(start).Concat(rotation.Take(start)).Take(PlannerBudget - units.Count));
        }
        if ((_now() + TrackingTime.IstOffset).DayOfWeek == DayOfWeek.Monday)
            foreach (var site in KeywordSites.All)
                foreach (var domain in settings.CompetitorSites.GetValueOrDefault(site, new List<string>()))
                    units.Add(new PlannerUnit(site, new(), "1000", domain, false));
        return units;
    }

    private async Task<(int, string)> PlannerAsync(KeywordSettingsView settings, CancellationToken ct)
    {
        var accounts = await AccountsAsync(ct);
        var withAds = accounts.Where(a => !string.IsNullOrEmpty(a.AdsCustomerId)).ToList();
        if (withAds.Count == 0) throw new InvalidOperationException("Keyword Planner needs a Google Ads account. Choose one in Google → Setup.");
        var units = await PlannerUnitsAsync(settings, ct);
        int requests = 0, ideas = 0, rotated = 0;
        foreach (var u in units.Take(PlannerBudget))
        {
            var a = withAds.FirstOrDefault(x => x.Site == u.Site) ?? withAds[0];
            var client = _keys.Client(a) ?? throw new GoogleApiException($"{a.Name}: sign in with Google again in Google → Setup.");
            if (requests > 0 && PlannerDelayMs > 0) await Task.Delay(PlannerDelayMs, ct);
            List<KeywordIdea> list;
            try
            {
                list = await _google.KeywordIdeasAsync(client, a.AdsCustomerId!, a.AdsLoginCustomerId,
                    new KeywordIdeaRequest { Seeds = u.Seeds, SiteSeed = u.SiteSeed, LanguageId = u.LanguageId, PageSize = PlannerIdeasPerRequest }, ct);
            }
            catch (GoogleApiException ex) when (ex.Reason is "RESOURCE_EXHAUSTED" || ex.HttpStatus == 429)
            {
                _logger.LogInformation("Keywords: Keyword Planner limit reached after {Requests} requests.", requests);
                break;
            }
            requests++;
            if (!u.Refresh && u.SiteSeed == null) rotated++;
            var asked = u.Seeds.Select(KeywordText.Normalize).ToHashSet();
            var keep = list.Select(i => (Norm: KeywordText.Normalize(i.Text), Idea: i))
                .Where(x => !Unusable(x.Norm, x.Idea.Text) && ((x.Idea.AvgMonthlySearches ?? 0) >= PlannerMinVolume || asked.Contains(x.Norm)))
                .GroupBy(x => x.Norm).Select(g => g.First()).ToList();
            await LoadAsync(u.Site, keep.Select(x => x.Norm), ct);
            foreach (var (norm, idea) in keep)
            {
                var k = Keyword(u.Site, norm, "planner", settings.ExtraCities);
                k.AvgMonthlySearches = idea.AvgMonthlySearches;
                k.Competition = idea.Competition;
                k.CompetitionIndex = idea.CompetitionIndex;
                k.LowBid = idea.LowBidMicros is { } lo ? Math.Round(lo / 1_000_000m, 2) : null;
                k.HighBid = idea.HighBidMicros is { } hi ? Math.Round(hi / 1_000_000m, 2) : null;
                k.MonthlyJson = idea.Months.Count == 0 ? null : KeywordShared.Trim(KeywordShared.Write(idea.Months.TakeLast(24).Select(m => new { y = m.Y, m = m.M, v = m.V })), 2000);
                k.VolumeAt = _now();
            }
            ideas += keep.Count;
            await _db.SaveChangesAsync(ct);
        }
        var stored = await _db.KeywordSettings.FirstAsync(ct);
        stored.PlannerCursor = settings.PlannerCursor + rotated;
        return (requests, $"{requests} requests · {ideas:N0} keywords with volumes");
    }

    // ── suggested topics ─────────────────────────────────────────────────────

    /// <summary>Keywords Search Console shows on a page go in a suggested topic for that page, unless a person chose their topic.</summary>
    private async Task SuggestTopicsAsync(CancellationToken ct)
    {
        var since = TrackingTime.IstDate(_now()).AddDays(-30);
        var top = (await _db.KeywordObservations.AsNoTracking()
                .Where(o => o.Source == "gsc" && o.Day >= since && o.Page != null)
                .GroupBy(o => new { o.KeywordId, o.Page })
                .Select(g => new { g.Key.KeywordId, g.Key.Page, Imp = g.Sum(o => o.Impressions) })
                .ToListAsync(ct))
            .GroupBy(x => x.KeywordId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Imp).First().Page!);
        if (top.Count == 0) return;
        var ids = top.Keys.ToList();
        var keywords = new List<Keyword>();
        foreach (var chunk in ids.Chunk(1000))
        {
            var list = chunk.ToList();
            keywords.AddRange(await _db.Keywords.Where(k => list.Contains(k.KeywordId) && !k.TopicByPerson).ToListAsync(ct));
        }
        var topics = await _db.KeywordTopics.Where(t => t.IsSuggested && t.TargetPage != null).ToListAsync(ct);
        var bySitePage = topics.GroupBy(t => (t.Site, t.TargetPage!)).ToDictionary(g => g.Key, g => g.First());
        var now = _now();
        foreach (var k in keywords)
        {
            var page = top[k.KeywordId];
            if (!bySitePage.TryGetValue((k.Site, page), out var t))
            {
                t = new KeywordTopic
                {
                    TopicId = Guid.NewGuid(), Site = k.Site, Name = KeywordText.TopicNameFromPage(page), TargetPage = page, IsSuggested = true, CreatedAt = now, UpdatedAt = now,
                };
                _db.KeywordTopics.Add(t);
                bySitePage[(k.Site, page)] = t;
            }
            k.TopicId = t.TopicId;
        }
        await _db.SaveChangesAsync(ct);
    }
}
