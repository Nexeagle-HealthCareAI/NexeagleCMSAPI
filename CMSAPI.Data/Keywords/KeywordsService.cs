using System.Text.RegularExpressions;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Keywords;

/// <summary>
/// CMS → Keywords screens: Overview, Keywords, Topics, Content plan and Setup. Numbers are the last 28 finished days
/// (Search Console is 2 days behind) against the 28 before; "rising" is the last 7 days against the 7 before.
/// Search Console showings count as demand on our pages; Doctor Dekho site searches count as visitors; Keyword
/// Planner gives monthly searches in India. Edits need keywords.edit, Setup changes keywords.connect.
/// </summary>
public class KeywordsService : IKeywordsService
{
    private readonly CmsDbContext _db;
    private readonly Func<DateTime> _now;

    public KeywordsService(CmsDbContext db) : this(db, () => DateTime.UtcNow) { }

    internal KeywordsService(CmsDbContext db, Func<DateTime> now)
    {
        _db = db;
        _now = now;
    }

    private DateOnly Ref => TrackingTime.IstDate(_now()).AddDays(-2);

    // ── numbers ──────────────────────────────────────────────────────────────

    internal sealed record Agg(long Imp, long Clicks, decimal PosW, long Visitors)
    {
        public decimal? Position => Imp > 0 ? Math.Round(PosW / Imp, 1) : null;
    }

    private async Task<Dictionary<Guid, Agg>> AggAsync(DateOnly from, DateOnly to, string? site, CancellationToken ct)
    {
        var q = _db.KeywordObservations.AsNoTracking().Where(o => o.Day >= from && o.Day <= to);
        if (site != null) q = q.Where(o => _db.Keywords.Any(k => k.KeywordId == o.KeywordId && k.Site == site));
        var rows = await q.GroupBy(o => o.KeywordId).Select(g => new
        {
            g.Key,
            Imp = g.Sum(o => o.Source == "gsc" ? o.Impressions : 0),
            Clicks = g.Sum(o => o.Source == "gsc" ? o.Clicks : 0),
            PosW = g.Sum(o => o.Source == "gsc" && o.Position != null ? o.Position.Value * o.Impressions : 0m),
            Visitors = g.Sum(o => o.Visitors),
        }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Key, r => new Agg(r.Imp, r.Clicks, r.PosW, r.Visitors));
    }

    private static readonly Agg None = new(0, 0, 0, 0);

    private static KeywordFacts Facts(Keyword k, Agg cur, Agg last7, Agg prev7) => new()
    {
        Site = k.Site, Intent = k.Intent, City = k.City, Volume = k.AvgMonthlySearches,
        Impressions = cur.Imp, Clicks = cur.Clicks, Position = cur.Position, Visitors = cur.Visitors,
        Last7 = last7.Imp + last7.Visitors, Prev7 = prev7.Imp + prev7.Visitors,
    };

    private sealed class Window
    {
        public Dictionary<Guid, Agg> Cur = new(), Prev = new(), Last7 = new(), Prev7 = new();
        public Agg C(Guid id) => Cur.GetValueOrDefault(id, None);
    }

    private async Task<Window> WindowAsync(string? site, CancellationToken ct)
    {
        var r = Ref;
        return new Window
        {
            Cur = await AggAsync(r.AddDays(-27), r, site, ct),
            Prev = await AggAsync(r.AddDays(-55), r.AddDays(-28), site, ct),
            Last7 = await AggAsync(r.AddDays(-6), r, site, ct),
            Prev7 = await AggAsync(r.AddDays(-13), r.AddDays(-7), site, ct),
        };
    }

    private static long Demand(Keyword k, Agg a) => KeywordScoring.Demand(new KeywordFacts { Volume = k.AvgMonthlySearches, Impressions = a.Imp, Visitors = a.Visitors });

    private async Task<Dictionary<Guid, List<long>>> WeeksAsync(List<Guid> ids, CancellationToken ct)
    {
        var r = Ref;
        var from = r.AddDays(-55);
        var obs = await _db.KeywordObservations.AsNoTracking()
            .Where(o => ids.Contains(o.KeywordId) && o.Day >= from && o.Day <= r)
            .Select(o => new { o.KeywordId, o.Day, o.Source, o.Impressions, o.Visitors }).ToListAsync(ct);
        var result = ids.ToDictionary(id => id, _ => Enumerable.Repeat(0L, 8).ToList());
        foreach (var o in obs)
        {
            var week = 7 - (r.DayNumber - o.Day.DayNumber) / 7;
            if (week is < 0 or > 7) continue;
            result[o.KeywordId][week] += (o.Source == "gsc" ? o.Impressions : 0) + o.Visitors;
        }
        return result;
    }

    private static KeywordRowDto Row(Keyword k, Agg a, string? topicName, List<long>? weeks = null) => new()
    {
        KeywordId = k.KeywordId, Site = k.Site, Text = k.Text, Language = k.Language, Intent = k.Intent, City = k.City, TopicId = k.TopicId, TopicName = topicName,
        Status = k.Status, Sources = k.Sources, AvgMonthlySearches = k.AvgMonthlySearches, Impressions = a.Imp, Clicks = a.Clicks, Position = a.Position,
        Visitors = a.Visitors, Weeks = weeks ?? new List<long>(), FirstSeenAt = k.FirstSeenAt,
    };

    private async Task<KeywordWeights> WeightsAsync(CancellationToken ct) => (await KeywordShared.SettingsAsync(_db, ct)).Weights;

    private IQueryable<Keyword> Active(string? site) =>
        _db.Keywords.AsNoTracking().Where(k => k.Status == "active" && (site == null || k.Site == site));

    // ── overview ─────────────────────────────────────────────────────────────

    public async Task<KeywordOverviewDto> OverviewAsync(string? site, CancellationToken ct = default)
    {
        site = KeywordSites.Valid(site) ? site : null;
        var keywords = await Active(site).ToListAsync(ct);
        var w = await WindowAsync(site, ct);
        var weights = await WeightsAsync(ct);
        var weekAgo = _now().AddDays(-7);
        var content = await _db.KeywordContent.AsNoTracking().Where(c => site == null || c.Site == site).Select(c => new { c.TopicId, c.Stage }).ToListAsync(ct);
        var dto = new KeywordOverviewDto
        {
            Tracked = keywords.Count,
            NewThisWeek = keywords.Count(k => k.FirstSeenAt >= weekAgo),
            Impressions = keywords.Sum(k => w.C(k.KeywordId).Imp),
            PreviousImpressions = keywords.Sum(k => w.Prev.GetValueOrDefault(k.KeywordId, None).Imp),
            Clicks = keywords.Sum(k => w.C(k.KeywordId).Clicks),
            TopicsWithPlan = content.Where(c => c.TopicId != null).Select(c => c.TopicId).Distinct().Count(),
            Published = content.Count(c => c.Stage == "published"),
            InProgress = content.Count(c => c.Stage != "published"),
        };
        var cur = keywords.Select(k => w.C(k.KeywordId)).ToList();
        var curImp = cur.Sum(a => a.Imp);
        dto.Position = curImp > 0 ? Math.Round(cur.Sum(a => a.PosW) / curImp, 1) : null;
        var prev = keywords.Select(k => w.Prev.GetValueOrDefault(k.KeywordId, None)).ToList();
        var prevImp = prev.Sum(a => a.Imp);
        dto.PreviousPosition = prevImp > 0 ? Math.Round(prev.Sum(a => a.PosW) / prevImp, 1) : null;

        dto.Languages = keywords.GroupBy(k => k.Language).Select(g => new KeywordLanguageDto
        {
            Language = g.Key, Impressions = g.Sum(k => w.C(k.KeywordId).Imp), Visitors = g.Sum(k => w.C(k.KeywordId).Visitors),
            Searches = g.Sum(k => k.AvgMonthlySearches ?? 0),
        }).Where(l => l.Impressions + l.Visitors + l.Searches > 0)
            .OrderBy(l => Array.IndexOf(KeywordKinds.Languages, l.Language)).ToList();

        dto.Rising = keywords.Select(k => (k, Last: w.Last7.GetValueOrDefault(k.KeywordId, None), Prev: w.Prev7.GetValueOrDefault(k.KeywordId, None)))
            .Select(x => (x.k, Now: x.Last.Imp + x.Last.Visitors, Before: x.Prev.Imp + x.Prev.Visitors))
            .Where(x => x.Now >= KeywordScoring.RisingMin && x.Now >= 1.5 * Math.Max(x.Before, 1))
            .OrderByDescending(x => x.Before == 0 ? x.Now * 10 : (x.Now - x.Before) * 100 / x.Before).Take(6)
            .Select(x => new KeywordRisingDto
            {
                KeywordId = x.k.KeywordId, Text = x.k.Text, Site = x.k.Site, Sources = x.k.Sources, Now = x.Now,
                ChangePercent = x.Before == 0 ? 100 : (int)((x.Now - x.Before) * 100 / x.Before),
            }).ToList();

        // Best opportunities: topics first, then keywords no topic covers.
        var topics = await TopicRowsAsync(site, keywords, w, weights, ct);
        var opps = topics.Where(t => t.Reason != null).Select(t => new KeywordOpportunityDto
        {
            Kind = t.Reason!, Site = t.Site, TopicId = t.TopicId, Title = t.Name, Why = t.Why ?? string.Empty, Score = t.Score,
            Keywords = keywords.Where(k => k.TopicId == t.TopicId).OrderByDescending(k => Demand(k, w.C(k.KeywordId))).Take(3).Select(k => k.Text).ToList(),
        }).ToList();
        foreach (var k in keywords.Where(k => k.TopicId == null))
        {
            var v = KeywordScoring.Evaluate(Facts(k, w.C(k.KeywordId), w.Last7.GetValueOrDefault(k.KeywordId, None), w.Prev7.GetValueOrDefault(k.KeywordId, None)), weights);
            if (v.Kind != null) opps.Add(new KeywordOpportunityDto { Kind = v.Kind, Site = k.Site, KeywordId = k.KeywordId, Title = k.Text, Why = v.Why!, Score = v.Score, Keywords = new() { k.Text } });
        }
        dto.Opportunities = opps.OrderByDescending(o => o.Score).Take(6).ToList();
        dto.Sources = await SourcesAsync(ct);
        dto.CollectedAt = dto.Sources.Max(s => s.LastOkAt);
        return dto;
    }

    // ── keywords ─────────────────────────────────────────────────────────────

    public async Task<KeywordPage> ListAsync(KeywordQuery query, CancellationToken ct = default)
    {
        var site = KeywordSites.Valid(query.Site) ? query.Site : null;
        var q = _db.Keywords.AsNoTracking().Where(k => k.Status == (query.Ignored ? "ignored" : "active"));
        if (site != null) q = q.Where(k => k.Site == site);
        if (!string.IsNullOrEmpty(query.Language)) q = q.Where(k => k.Language == query.Language);
        if (!string.IsNullOrEmpty(query.Intent)) q = q.Where(k => k.Intent == query.Intent);
        if (!string.IsNullOrEmpty(query.City)) q = q.Where(k => k.City == query.City);
        if (!string.IsNullOrEmpty(query.Source)) q = q.Where(k => k.Sources.Contains(query.Source));
        if (query.TopicId is { } tid) q = q.Where(k => k.TopicId == tid);
        var text = KeywordText.Normalize(query.Q);
        if (text.Length > 0) q = q.Where(k => k.Normalized.Contains(text));
        var list = await q.ToListAsync(ct);
        var w = await WindowAsync(site, ct);
        IEnumerable<Keyword> sorted = (query.Sort ?? "demand") switch
        {
            "shown" => list.OrderByDescending(k => w.C(k.KeywordId).Imp),
            "position" => list.OrderBy(k => w.C(k.KeywordId).Position ?? 999),
            "rising" => list.OrderByDescending(k => w.Last7.GetValueOrDefault(k.KeywordId, None).Imp + w.Last7.GetValueOrDefault(k.KeywordId, None).Visitors
                                                    - w.Prev7.GetValueOrDefault(k.KeywordId, None).Imp - w.Prev7.GetValueOrDefault(k.KeywordId, None).Visitors),
            "newest" => list.OrderByDescending(k => k.FirstSeenAt),
            _ => list.OrderByDescending(k => Demand(k, w.C(k.KeywordId))),
        };
        var size = Math.Clamp(query.PageSize, 1, 200);
        var pageItems = sorted.Skip((Math.Max(1, query.Page) - 1) * size).Take(size).ToList();
        var topicNames = await TopicNamesAsync(pageItems.Select(k => k.TopicId), ct);
        var weeks = await WeeksAsync(pageItems.Select(k => k.KeywordId).ToList(), ct);
        var cities = await _db.Keywords.AsNoTracking().Where(k => k.City != null && (site == null || k.Site == site)).Select(k => k.City!).Distinct().OrderBy(c => c).ToListAsync(ct);
        return new KeywordPage
        {
            Total = list.Count, Cities = cities,
            Items = pageItems.Select(k => Row(k, w.C(k.KeywordId), k.TopicId is { } t ? topicNames.GetValueOrDefault(t) : null, weeks[k.KeywordId])).ToList(),
        };
    }

    private async Task<Dictionary<Guid, string>> TopicNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        return list.Count == 0 ? new() : await _db.KeywordTopics.AsNoTracking().Where(t => list.Contains(t.TopicId)).ToDictionaryAsync(t => t.TopicId, t => t.Name, ct);
    }

    public async Task<WorkResult<KeywordDetailDto>> DetailAsync(Guid keywordId, CancellationToken ct = default)
    {
        var k = await _db.Keywords.AsNoTracking().FirstOrDefaultAsync(x => x.KeywordId == keywordId, ct);
        if (k == null) return WorkResult<KeywordDetailDto>.Fail(404, "This keyword no longer exists.");
        var r = Ref;
        var obs = await _db.KeywordObservations.AsNoTracking().Where(o => o.KeywordId == keywordId && o.Day >= r.AddDays(-55) && o.Day <= r).ToListAsync(ct);
        var cur = obs.Where(o => o.Day >= r.AddDays(-27)).ToList();
        var gsc = cur.Where(o => o.Source == "gsc").ToList();
        var imp = gsc.Sum(o => (long)o.Impressions);
        var agg = new Agg(imp, gsc.Sum(o => (long)o.Clicks), gsc.Sum(o => (o.Position ?? 0) * o.Impressions), cur.Sum(o => (long)o.Visitors));
        var weeks = await WeeksAsync(new() { keywordId }, ct);
        var topicName = k.TopicId is { } t ? (await TopicNamesAsync(new Guid?[] { t }, ct)).GetValueOrDefault(t) : null;
        var row = Row(k, agg, topicName, weeks[keywordId]);
        var dto = new KeywordDetailDto
        {
            KeywordId = row.KeywordId, Site = row.Site, Text = row.Text, Language = row.Language, Intent = row.Intent, City = row.City, TopicId = row.TopicId,
            TopicName = row.TopicName, Status = row.Status, Sources = row.Sources, AvgMonthlySearches = row.AvgMonthlySearches, Impressions = row.Impressions,
            Clicks = row.Clicks, Position = row.Position, Visitors = row.Visitors, Weeks = row.Weeks, FirstSeenAt = row.FirstSeenAt,
            Competition = k.Competition, CompetitionIndex = k.CompetitionIndex, LowBid = k.LowBid, HighBid = k.HighBid,
            Months = KeywordShared.Read<List<MonthJson>>(k.MonthlyJson)?.Select(m => new KeywordMonth(m.Y, m.M, m.V)).ToList() ?? new(),
            Page = gsc.Where(o => o.Page != null).GroupBy(o => o.Page!).OrderByDescending(g => g.Sum(o => o.Impressions)).Select(g => g.Key).FirstOrDefault(),
            LanguageByPerson = k.LanguageByPerson, IntentByPerson = k.IntentByPerson,
        };
        dto.Currency = await _db.GoogleAccounts.AsNoTracking().Where(a => a.Site == k.Site && a.Currency != null).Select(a => a.Currency!).FirstOrDefaultAsync(ct) ?? "INR";
        var fmt = (long n) => n.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));
        if (k.AvgMonthlySearches is { } vol) dto.SeenIn.Add(new() { Name = "Keyword Planner", Detail = $"{fmt(vol)} searches a month, India" });
        if (imp > 0) dto.SeenIn.Add(new() { Name = "Search Console", Detail = $"{fmt(imp)} showings in 28 days, position {agg.Position:0.#}" });
        if (agg.Visitors > 0) dto.SeenIn.Add(new() { Name = "Doctor Dekho searches", Detail = $"typed by {fmt(agg.Visitors)} visitors in 28 days" });
        var ads = cur.Where(o => o.Source == "ads").ToList();
        if (ads.Count > 0) dto.SeenIn.Add(new() { Name = "Google Ads", Detail = $"{fmt(ads.Sum(o => (long)o.Impressions))} ad showings, {fmt(ads.Sum(o => (long)o.Clicks))} clicks in 28 days" });
        if (k.TopicId is { } topic)
        {
            var others = await _db.Keywords.AsNoTracking().Where(x => x.TopicId == topic && x.KeywordId != k.KeywordId && x.Status == "active" && x.Language != k.Language).ToListAsync(ct);
            dto.Variants = others.OrderByDescending(x => x.AvgMonthlySearches ?? 0).Take(6).Select(x => Row(x, None, topicName)).ToList();
        }
        return WorkResult<KeywordDetailDto>.Success(dto);
    }

    private sealed record MonthJson(int Y, int M, long V);

    public async Task<WorkResult<KeywordDetailDto>> UpdateAsync(KeywordActor actor, Guid keywordId, KeywordUpdate update, CancellationToken ct = default)
    {
        if (!actor.CanEdit) return WorkResult<KeywordDetailDto>.Fail(403, "You can see keywords but not change them.");
        var k = await _db.Keywords.FirstOrDefaultAsync(x => x.KeywordId == keywordId, ct);
        if (k == null) return WorkResult<KeywordDetailDto>.Fail(404, "This keyword no longer exists.");
        if (update.Language != null)
        {
            if (!KeywordKinds.Languages.Contains(update.Language)) return WorkResult<KeywordDetailDto>.Fail(400, "Choose English, Hinglish, Hindi, Bengali or other.");
            k.Language = update.Language; k.LanguageByPerson = true;
        }
        if (update.Intent != null)
        {
            if (!KeywordKinds.Intents.Contains(update.Intent)) return WorkResult<KeywordDetailDto>.Fail(400, "That is not one of the intents.");
            k.Intent = update.Intent; k.IntentByPerson = true;
        }
        if (update.ClearCity) k.City = null;
        else if (!string.IsNullOrWhiteSpace(update.City)) k.City = KeywordShared.Trim(update.City, 80);
        if (update.ClearTopic) { k.TopicId = null; k.TopicByPerson = true; }
        else if (update.TopicId is { } topic)
        {
            var t = await _db.KeywordTopics.AsNoTracking().FirstOrDefaultAsync(x => x.TopicId == topic, ct);
            if (t == null || t.Site != k.Site) return WorkResult<KeywordDetailDto>.Fail(400, "Choose a topic of the same website.");
            k.TopicId = topic; k.TopicByPerson = true;
        }
        if (update.Status != null)
        {
            if (update.Status is not ("active" or "ignored")) return WorkResult<KeywordDetailDto>.Fail(400, "A keyword is either kept or ignored.");
            k.Status = update.Status;
        }
        await _db.SaveChangesAsync(ct);
        return await DetailAsync(keywordId, ct);
    }

    // ── topics ───────────────────────────────────────────────────────────────

    private async Task<List<KeywordTopicRowDto>> TopicRowsAsync(string? site, List<Keyword> keywords, Window w, KeywordWeights weights, CancellationToken ct)
    {
        var topics = await _db.KeywordTopics.AsNoTracking().Where(t => site == null || t.Site == site).ToListAsync(ct);
        var content = await _db.KeywordContent.AsNoTracking().Where(c => c.TopicId != null && (site == null || c.Site == site))
            .Select(c => new { c.TopicId, c.Stage, c.Languages }).ToListAsync(ct);
        var byTopic = keywords.Where(k => k.TopicId != null).GroupBy(k => k.TopicId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var rows = new List<KeywordTopicRowDto>();
        foreach (var t in topics)
        {
            var ks = byTopic.GetValueOrDefault(t.TopicId, new List<Keyword>());
            var cur = ks.Select(k => w.C(k.KeywordId)).ToList();
            var imp = cur.Sum(a => a.Imp);
            var mine = content.Where(c => c.TopicId == t.TopicId).ToList();
            var facts = new KeywordFacts
            {
                Site = t.Site,
                Intent = ks.GroupBy(k => k.Intent).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "other",
                City = ks.Select(k => k.City).FirstOrDefault(c => c != null),
                Volume = ks.Sum(k => k.AvgMonthlySearches ?? 0), Impressions = imp, Clicks = cur.Sum(a => a.Clicks),
                Position = imp > 0 ? Math.Round(cur.Sum(a => a.PosW) / imp, 1) : null, Visitors = cur.Sum(a => a.Visitors),
                Last7 = ks.Sum(k => w.Last7.GetValueOrDefault(k.KeywordId, None).Imp + w.Last7.GetValueOrDefault(k.KeywordId, None).Visitors),
                Prev7 = ks.Sum(k => w.Prev7.GetValueOrDefault(k.KeywordId, None).Imp + w.Prev7.GetValueOrDefault(k.KeywordId, None).Visitors),
                DemandByLanguage = ks.GroupBy(k => k.Language).ToDictionary(g => g.Key, g => g.Sum(k => Demand(k, w.C(k.KeywordId)))),
                PublishedLanguages = mine.Where(c => c.Stage == "published").SelectMany(c => c.Languages.Split(',', StringSplitOptions.RemoveEmptyEntries)).ToHashSet(),
            };
            if (facts.Volume == 0) facts.Volume = null;
            var v = ks.Count == 0 ? new KeywordVerdict(null, null, 0) : KeywordScoring.Evaluate(facts, weights, topic: true);
            rows.Add(new KeywordTopicRowDto
            {
                TopicId = t.TopicId, Site = t.Site, Name = t.Name, TargetPage = t.TargetPage, IsSuggested = t.IsSuggested, KeywordCount = ks.Count,
                Demand = facts.DemandByLanguage.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value),
                Impressions = imp, Clicks = facts.Clicks, Position = facts.Position, Reason = v.Kind, Why = v.Why, Score = v.Score,
                ContentCount = mine.Count, PublishedCount = mine.Count(c => c.Stage == "published"),
            });
        }
        return rows;
    }

    public async Task<List<KeywordTopicRowDto>> TopicsAsync(string? site, string? kind, CancellationToken ct = default)
    {
        site = KeywordSites.Valid(site) ? site : null;
        var keywords = await Active(site).Where(k => k.TopicId != null).ToListAsync(ct);
        var rows = await TopicRowsAsync(site, keywords, await WindowAsync(site, ct), await WeightsAsync(ct), ct);
        if (!string.IsNullOrEmpty(kind)) rows = rows.Where(r => r.Reason == kind).ToList();
        return rows.OrderByDescending(r => r.Reason != null).ThenByDescending(r => r.Score).ThenBy(r => r.Name).ToList();
    }

    public async Task<WorkResult<KeywordTopicDetailDto>> TopicAsync(Guid topicId, CancellationToken ct = default)
    {
        var t = await _db.KeywordTopics.AsNoTracking().FirstOrDefaultAsync(x => x.TopicId == topicId, ct);
        if (t == null) return WorkResult<KeywordTopicDetailDto>.Fail(404, "This topic no longer exists.");
        var keywords = await Active(t.Site).Where(k => k.TopicId == topicId).ToListAsync(ct);
        var w = await WindowAsync(t.Site, ct);
        var row = (await TopicRowsAsync(t.Site, keywords, w, await WeightsAsync(ct), ct)).First(r => r.TopicId == topicId);
        var ordered = keywords.OrderByDescending(k => Demand(k, w.C(k.KeywordId))).ToList();
        var weeks = await WeeksAsync(ordered.Take(50).Select(k => k.KeywordId).ToList(), ct);
        var dto = new KeywordTopicDetailDto
        {
            TopicId = row.TopicId, Site = row.Site, Name = row.Name, TargetPage = row.TargetPage, IsSuggested = row.IsSuggested, KeywordCount = row.KeywordCount,
            Demand = row.Demand, Impressions = row.Impressions, Clicks = row.Clicks, Position = row.Position, Reason = row.Reason, Why = row.Why, Score = row.Score,
            ContentCount = row.ContentCount, PublishedCount = row.PublishedCount, Notes = t.Notes,
            Keywords = ordered.Take(50).Select(k => Row(k, w.C(k.KeywordId), t.Name, weeks[k.KeywordId])).ToList(),
            Questions = ordered.Where(k => KeywordText.IsQuestion(k.Normalized)).Take(8).Select(k => k.Text).ToList(),
            Content = (await ContentAsync(t.Site, null, ct)).Where(c => c.TopicId == topicId).ToList(),
        };
        return WorkResult<KeywordTopicDetailDto>.Success(dto);
    }

    private static readonly Regex PageAddress = new(@"^(https?://[^\s]+|/[^\s]*)$", RegexOptions.Compiled);

    public async Task<WorkResult<KeywordTopicDetailDto>> CreateTopicAsync(KeywordActor actor, KeywordTopicSave save, CancellationToken ct = default)
    {
        if (!actor.CanEdit) return WorkResult<KeywordTopicDetailDto>.Fail(403, "You can see topics but not change them.");
        if (!KeywordSites.Valid(save.Site)) return WorkResult<KeywordTopicDetailDto>.Fail(400, "Choose Doctor Dekho or nexeagle.com.");
        var problem = TopicProblem(save);
        if (problem != null) return WorkResult<KeywordTopicDetailDto>.Fail(400, problem);
        var now = _now();
        var t = new KeywordTopic
        {
            TopicId = Guid.NewGuid(), Site = save.Site, Name = save.Name.Trim(), TargetPage = KeywordShared.Trim(save.TargetPage, 500), Notes = KeywordShared.Trim(save.Notes, 2000),
            CreatedByUserId = actor.UserId, CreatedAt = now, UpdatedAt = now,
        };
        _db.KeywordTopics.Add(t);
        if (save.KeywordIds is { Count: > 0 } ids)
            foreach (var k in await _db.Keywords.Where(k => ids.Contains(k.KeywordId) && k.Site == save.Site).ToListAsync(ct))
            {
                k.TopicId = t.TopicId;
                k.TopicByPerson = true;
            }
        await _db.SaveChangesAsync(ct);
        return await TopicAsync(t.TopicId, ct);
    }

    private static string? TopicProblem(KeywordTopicSave save)
    {
        if (string.IsNullOrWhiteSpace(save.Name) || save.Name.Trim().Length > 200) return "Give the topic a name (up to 200 characters).";
        if (!string.IsNullOrWhiteSpace(save.TargetPage) && (save.TargetPage.Trim().Length > 500 || !PageAddress.IsMatch(save.TargetPage.Trim())))
            return "The page should be a path such as /health/conditions/dengue or a full https:// address.";
        return null;
    }

    public async Task<WorkResult<KeywordTopicDetailDto>> UpdateTopicAsync(KeywordActor actor, Guid topicId, KeywordTopicSave save, CancellationToken ct = default)
    {
        if (!actor.CanEdit) return WorkResult<KeywordTopicDetailDto>.Fail(403, "You can see topics but not change them.");
        var t = await _db.KeywordTopics.FirstOrDefaultAsync(x => x.TopicId == topicId, ct);
        if (t == null) return WorkResult<KeywordTopicDetailDto>.Fail(404, "This topic no longer exists.");
        var problem = TopicProblem(save);
        if (problem != null) return WorkResult<KeywordTopicDetailDto>.Fail(400, problem);
        t.Name = save.Name.Trim();
        t.Notes = KeywordShared.Trim(save.Notes, 2000);
        var page = KeywordShared.Trim(save.TargetPage, 500);
        if (page != t.TargetPage) { t.TargetPage = page; t.IsSuggested = false; }  // a person chose the page: the CMS stops filling it
        t.UpdatedAt = _now();
        await _db.SaveChangesAsync(ct);
        return await TopicAsync(topicId, ct);
    }

    public async Task<WorkResult<KeywordTopicDetailDto>> MergeTopicAsync(KeywordActor actor, Guid topicId, Guid intoTopicId, CancellationToken ct = default)
    {
        if (!actor.CanEdit) return WorkResult<KeywordTopicDetailDto>.Fail(403, "You can see topics but not change them.");
        if (topicId == intoTopicId) return WorkResult<KeywordTopicDetailDto>.Fail(400, "Choose another topic to merge into.");
        var from = await _db.KeywordTopics.FirstOrDefaultAsync(x => x.TopicId == topicId, ct);
        var into = await _db.KeywordTopics.FirstOrDefaultAsync(x => x.TopicId == intoTopicId, ct);
        if (from == null || into == null) return WorkResult<KeywordTopicDetailDto>.Fail(404, "One of the topics no longer exists.");
        if (from.Site != into.Site) return WorkResult<KeywordTopicDetailDto>.Fail(400, "Topics of different websites cannot be merged.");
        foreach (var k in await _db.Keywords.Where(k => k.TopicId == topicId).ToListAsync(ct)) { k.TopicId = intoTopicId; k.TopicByPerson = true; }
        foreach (var c in await _db.KeywordContent.Where(c => c.TopicId == topicId).ToListAsync(ct)) c.TopicId = intoTopicId;
        _db.KeywordTopics.Remove(from);
        into.UpdatedAt = _now();
        await _db.SaveChangesAsync(ct);
        return await TopicAsync(intoTopicId, ct);
    }

    // ── content plan ─────────────────────────────────────────────────────────

    public async Task<List<KeywordContentDto>> ContentAsync(string? site, string? kind, CancellationToken ct = default)
    {
        var q = _db.KeywordContent.AsNoTracking();
        if (KeywordSites.Valid(site)) q = q.Where(c => c.Site == site);
        if (!string.IsNullOrEmpty(kind)) q = q.Where(c => c.Kind == kind);
        var items = await q.OrderByDescending(c => c.UpdatedAt).Take(500).ToListAsync(ct);
        var topics = await TopicNamesAsync(items.Select(c => c.TopicId), ct);
        var owners = items.Where(c => c.OwnerUserId != null).Select(c => c.OwnerUserId!.Value).Distinct().ToList();
        var names = owners.Count == 0 ? new Dictionary<Guid, string>()
            : await _db.CmsUsers.AsNoTracking().Where(u => owners.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName, ct);
        var result = new List<KeywordContentDto>();
        foreach (var c in items)
        {
            var dto = ContentDto(c, c.TopicId is { } t ? topics.GetValueOrDefault(t) : null, c.OwnerUserId is { } o ? names.GetValueOrDefault(o) : null);
            if (c.Stage == "published" && c.PublishedAt != null && c.TopicId != null) dto.Result = await ResultAsync(c.TopicId.Value, c.PublishedAt.Value, ct);
            result.Add(dto);
        }
        return result;
    }

    private static KeywordContentDto ContentDto(KeywordContent c, string? topicName, string? ownerName) => new()
    {
        ContentId = c.ContentId, Site = c.Site, TopicId = c.TopicId, TopicName = topicName, Kind = c.Kind, Title = c.Title, Stage = c.Stage,
        Languages = c.Languages.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(), Brief = KeywordShared.Read<KeywordBrief>(c.BriefJson) ?? new(),
        OwnerUserId = c.OwnerUserId, OwnerName = ownerName, DueDate = c.DueDate, Url = c.Url, PublishedAt = c.PublishedAt, CreatedAt = c.CreatedAt, UpdatedAt = c.UpdatedAt,
    };

    /// <summary>The topic's Search Console numbers for the 28 days before publishing and up to 28 days after.</summary>
    internal async Task<KeywordResultDto> ResultAsync(Guid topicId, DateTime publishedAt, CancellationToken ct)
    {
        var day = TrackingTime.IstDate(publishedAt);
        var last = Ref;
        var after = Math.Clamp(last.DayNumber - day.DayNumber, 0, 28);
        var obs = await _db.KeywordObservations.AsNoTracking()
            .Where(o => o.Source == "gsc" && o.Day >= day.AddDays(-28) && o.Day <= day.AddDays(28) && _db.Keywords.Any(k => k.KeywordId == o.KeywordId && k.TopicId == topicId))
            .Select(o => new { o.Day, o.Impressions, o.Clicks, o.Position }).ToListAsync(ct);
        var before = obs.Where(o => o.Day < day).ToList();
        var later = obs.Where(o => o.Day > day && o.Day <= day.AddDays(after)).ToList();
        decimal? Pos(IEnumerable<dynamic> xs) { long i = 0; decimal w = 0; foreach (var x in xs) { i += (int)x.Impressions; w += ((decimal?)x.Position ?? 0) * (int)x.Impressions; } return i > 0 ? Math.Round(w / i, 1) : null; }
        return new KeywordResultDto
        {
            DaysAfter = after,
            ImpressionsBefore = before.Sum(o => (long)o.Impressions), ImpressionsAfter = later.Sum(o => (long)o.Impressions),
            ClicksBefore = before.Sum(o => (long)o.Clicks), ClicksAfter = later.Sum(o => (long)o.Clicks),
            PositionBefore = Pos(before), PositionAfter = Pos(later),
        };
    }

    private static readonly HashSet<string> ContentLanguages = new() { "en", "hi", "hg", "bn" };

    private async Task<string?> ContentProblemAsync(KeywordContentSave save, CancellationToken ct)
    {
        if (!KeywordSites.Valid(save.Site)) return "Choose Doctor Dekho or nexeagle.com.";
        if (!KeywordKinds.ContentKinds.Contains(save.Kind)) return "Choose what kind of content this is.";
        if (!KeywordKinds.Stages.Contains(save.Stage)) return "Choose a stage.";
        if (string.IsNullOrWhiteSpace(save.Title) || save.Title.Trim().Length > 300) return "Give it a title (up to 300 characters).";
        if (save.Languages.Count == 0 || save.Languages.Any(l => !ContentLanguages.Contains(l))) return "Choose at least one language: English, Hindi, Hinglish or Bengali.";
        if (!string.IsNullOrWhiteSpace(save.Url) && (save.Url.Trim().Length > 500 || !PageAddress.IsMatch(save.Url.Trim()))) return "The address should be a path or a full https:// address.";
        if (save.TopicId is { } t && !await _db.KeywordTopics.AnyAsync(x => x.TopicId == t && x.Site == save.Site, ct)) return "Choose a topic of the same website.";
        if (save.OwnerUserId is { } o && !await _db.CmsUsers.AnyAsync(u => u.UserId == o, ct)) return "That person is not a CMS user.";
        return null;
    }

    private static string BriefJson(KeywordBrief? brief)
    {
        var b = brief ?? new KeywordBrief();
        b.MainKeywords = b.MainKeywords.Where(kv => ContentLanguages.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim().Length <= 200 ? kv.Value.Trim() : kv.Value.Trim()[..200]);
        b.Outline = KeywordShared.Trim(b.Outline, 8000);
        b.Page = KeywordShared.Trim(b.Page, 500);
        b.Schema = KeywordShared.Trim(b.Schema, 200);
        b.Writer = KeywordShared.Trim(b.Writer, 200);
        b.Reviewer = KeywordShared.Trim(b.Reviewer, 200);
        b.Notes = KeywordShared.Trim(b.Notes, 4000);
        b.Checklist = b.Checklist.Where(kv => kv.Key.Length <= 40).Take(20).ToDictionary(kv => kv.Key, kv => kv.Value);
        return KeywordShared.Write(b);
    }

    public async Task<WorkResult<KeywordContentDto>> CreateContentAsync(KeywordActor actor, KeywordContentSave save, CancellationToken ct = default)
    {
        if (!actor.CanEdit) return WorkResult<KeywordContentDto>.Fail(403, "You can see the content plan but not change it.");
        var problem = await ContentProblemAsync(save, ct);
        if (problem != null) return WorkResult<KeywordContentDto>.Fail(400, problem);
        var brief = save.Brief ?? new KeywordBrief();
        if (save.TopicId is { } topic && brief.MainKeywords.Count == 0)
        {
            // Start the brief with the topic's strongest keyword in each language.
            var w = await WindowAsync(save.Site, ct);
            foreach (var g in (await Active(save.Site).Where(k => k.TopicId == topic).ToListAsync(ct)).GroupBy(k => k.Language).Where(g => ContentLanguages.Contains(g.Key)))
                brief.MainKeywords[g.Key] = g.OrderByDescending(k => Demand(k, w.C(k.KeywordId))).First().Text;
            brief.Page ??= (await _db.KeywordTopics.AsNoTracking().Where(t => t.TopicId == topic).Select(t => t.TargetPage).FirstOrDefaultAsync(ct));
        }
        var now = _now();
        var c = new KeywordContent
        {
            ContentId = Guid.NewGuid(), Site = save.Site, TopicId = save.TopicId, Kind = save.Kind, Title = save.Title.Trim(), Stage = save.Stage,
            Languages = string.Join(',', save.Languages.Distinct()), BriefJson = BriefJson(brief), OwnerUserId = save.OwnerUserId, DueDate = save.DueDate,
            Url = KeywordShared.Trim(save.Url, 500), PublishedAt = save.Stage == "published" ? now : null,
            CreatedByUserId = actor.UserId, UpdatedByUserId = actor.UserId, CreatedAt = now, UpdatedAt = now,
        };
        _db.KeywordContent.Add(c);
        await _db.SaveChangesAsync(ct);
        return WorkResult<KeywordContentDto>.Success((await ContentAsync(c.Site, null, ct)).First(x => x.ContentId == c.ContentId));
    }

    public async Task<WorkResult<KeywordContentDto>> UpdateContentAsync(KeywordActor actor, Guid contentId, KeywordContentSave save, CancellationToken ct = default)
    {
        if (!actor.CanEdit) return WorkResult<KeywordContentDto>.Fail(403, "You can see the content plan but not change it.");
        var c = await _db.KeywordContent.FirstOrDefaultAsync(x => x.ContentId == contentId, ct);
        if (c == null) return WorkResult<KeywordContentDto>.Fail(404, "This item is no longer in the content plan.");
        save.Site = c.Site;  // an item stays with its website
        var problem = await ContentProblemAsync(save, ct);
        if (problem != null) return WorkResult<KeywordContentDto>.Fail(400, problem);
        if (save.Stage == "published" && c.Stage != "published") c.PublishedAt = _now();
        c.TopicId = save.TopicId; c.Kind = save.Kind; c.Title = save.Title.Trim(); c.Stage = save.Stage;
        c.Languages = string.Join(',', save.Languages.Distinct()); c.BriefJson = BriefJson(save.Brief); c.OwnerUserId = save.OwnerUserId;
        c.DueDate = save.DueDate; c.Url = KeywordShared.Trim(save.Url, 500); c.UpdatedByUserId = actor.UserId; c.UpdatedAt = _now();
        await _db.SaveChangesAsync(ct);
        return WorkResult<KeywordContentDto>.Success((await ContentAsync(c.Site, null, ct)).First(x => x.ContentId == c.ContentId));
    }

    public async Task<WorkResult<bool>> DeleteContentAsync(KeywordActor actor, Guid contentId, CancellationToken ct = default)
    {
        if (!actor.CanEdit) return WorkResult<bool>.Fail(403, "You can see the content plan but not change it.");
        var c = await _db.KeywordContent.FirstOrDefaultAsync(x => x.ContentId == contentId, ct);
        if (c == null) return WorkResult<bool>.Fail(404, "This item is no longer in the content plan.");
        _db.KeywordContent.Remove(c);
        await _db.SaveChangesAsync(ct);
        return WorkResult<bool>.Success(true);
    }

    // ── setup ────────────────────────────────────────────────────────────────

    internal async Task<List<KeywordSourceDto>> SourcesAsync(CancellationToken ct)
    {
        var runs = await _db.KeywordSourceRuns.AsNoTracking().ToDictionaryAsync(r => r.SourceKey, ct);
        var accounts = await _db.GoogleAccounts.AsNoTracking().Where(a => a.IsActive && a.Site != null).ToListAsync(ct);
        var hasGsc = accounts.Any(a => !string.IsNullOrEmpty(a.SearchConsoleSite));
        var hasAds = accounts.Any(a => !string.IsNullOrEmpty(a.AdsCustomerId));
        var min = (await KeywordShared.SettingsAsync(_db, ct)).MinVisitors;
        KeywordSourceDto Make(string key, string name, string what, string often, bool available, string? missing)
        {
            var r = runs.GetValueOrDefault(key);
            var dto = new KeywordSourceDto
            {
                Key = key, Name = name, What = what, Often = often, CanRun = available, LastRunAt = r?.LastRunAt, LastOkAt = r?.LastOkAt, LastCount = r?.LastCount ?? 0,
                Detail = r?.LastDetail, Error = r?.LastError,
            };
            if (!available) { dto.State = "warn"; dto.Error = missing; }
            else if (r?.LastError != null) dto.State = "warn";
            else if (r?.LastOkAt != null) dto.State = "ok";
            else { dto.State = "off"; dto.Detail = "First collection at 05:30 IST, or use Run now."; }
            return dto;
        }
        return new List<KeywordSourceDto>
        {
            Make("gsc", "Search Console", "Searches that showed our pages in India, both sites (from the Google section)", "Daily", hasGsc, "Choose a Search Console site in Google → Setup."),
            Make("ads", "Google Ads search terms", "What people searched before clicking our ads", "Daily", hasAds, "Choose a Google Ads account in Google → Setup."),
            Make("planner", "Keyword Planner", "Ideas and monthly searches for India, in English, Hindi and Bengali", "Daily, in rotation", hasAds, "Keyword Planner needs a Google Ads account (Google → Setup)."),
            Make("site", "Doctor Dekho site searches", $"What patients type into Doctor Dekho's search, from {min}+ visitors only", "Daily", true, null),
            new() { Key = "wikipedia", Name = "Wikipedia views", What = "Interest in conditions in English, Hindi and Bengali", Often = "Daily", State = "off", Detail = "Planned for a later phase." },
            new() { Key = "bing", Name = "Bing keyword stats", What = "Weekly showings for our top keywords in India", Often = "Weekly", State = "off", Detail = "Planned for a later phase." },
            new() { Key = "gbp", Name = "Business Profile searches", What = "Searches that showed our Google business profiles", Often = "Monthly", State = "off", Detail = "Planned for a later phase." },
            new() { Key = "trends", Name = "Google Trends", What = "Interest over 5 years by region", Often = "Daily", State = "off", Detail = "Waiting for Google to approve API access (apply at developers.google.com/search/apis/trends)." },
        };
    }

    public async Task<KeywordSetupDto> SetupAsync(KeywordActor actor, CancellationToken ct = default)
    {
        var s = await KeywordShared.SettingsAsync(_db, ct);
        var accounts = await _db.GoogleAccounts.AsNoTracking().Where(a => a.IsActive && a.Site != null).OrderBy(a => a.SortOrder).ToListAsync(ct);
        return new KeywordSetupDto
        {
            CanEdit = actor.CanConnect, Seeds = s.Seeds, CompetitorSites = s.CompetitorSites, ExtraCities = s.ExtraCities, Weights = s.Weights, MinVisitors = s.MinVisitors,
            Sources = await SourcesAsync(ct),
            GoogleAccounts = accounts.Where(a => KeywordSites.Valid(a.Site)).Select(a => new KeywordGoogleAccountDto
            {
                Site = a.Site!, Name = a.Name, SignedIn = a.RefreshTokenEnc != null, HasSearchConsole = !string.IsNullOrEmpty(a.SearchConsoleSite), HasAds = !string.IsNullOrEmpty(a.AdsCustomerId),
            }).ToList(),
            Cities = KeywordText.Cities.Values.Distinct().Concat(s.ExtraCities).Distinct().OrderBy(c => c).ToList(),
        };
    }

    /// <summary>"https://www.Example.com/pricing" → "example.com".</summary>
    internal static string CleanDomain(string d)
    {
        var x = Regex.Replace(d.Trim().ToLowerInvariant(), @"^https?://", string.Empty);
        x = Regex.Replace(x, @"[/?#].*$", string.Empty);
        return Regex.Replace(x, @"^www\.", string.Empty);
    }

    private static readonly Regex Domain = new(@"^(?=.{4,100}$)([a-z0-9-]+\.)+[a-z]{2,}$", RegexOptions.Compiled);

    public async Task<WorkResult<KeywordSetupDto>> SaveSetupAsync(KeywordActor actor, KeywordSetupSave save, CancellationToken ct = default)
    {
        if (!actor.CanConnect) return WorkResult<KeywordSetupDto>.Fail(403, "Only people who can change keyword sources can save Setup.");
        var row = await _db.KeywordSettings.FirstOrDefaultAsync(ct);
        if (row == null) { await KeywordShared.SettingsAsync(_db, ct); row = await _db.KeywordSettings.FirstAsync(ct); }
        if (save.Seeds != null)
        {
            var seeds = new Dictionary<string, List<string>>();
            foreach (var (site, list) in save.Seeds)
            {
                if (!KeywordSites.Valid(site)) return WorkResult<KeywordSetupDto>.Fail(400, "Seed lists are for Doctor Dekho or nexeagle.com.");
                var clean = list.Select(KeywordText.Normalize).Where(x => x.Length > 1).Distinct().ToList();
                if (clean.Count > 200 || clean.Any(x => x.Length > 80)) return WorkResult<KeywordSetupDto>.Fail(400, "Up to 200 seed words per website, each up to 80 characters.");
                seeds[site] = clean;
            }
            row.SeedsJson = KeywordShared.Write(seeds);
        }
        if (save.CompetitorSites != null)
        {
            var sites = new Dictionary<string, List<string>>();
            foreach (var (site, list) in save.CompetitorSites)
            {
                if (!KeywordSites.Valid(site)) return WorkResult<KeywordSetupDto>.Fail(400, "Competitor sites are for Doctor Dekho or nexeagle.com.");
                var clean = list.Select(CleanDomain).Where(d => d.Length > 0).Distinct().ToList();
                if (clean.Count > 10 || clean.Any(d => !Domain.IsMatch(d))) return WorkResult<KeywordSetupDto>.Fail(400, "Up to 10 sites per website, written like example.com.");
                sites[site] = clean;
            }
            row.CompetitorSitesJson = KeywordShared.Write(sites);
        }
        if (save.ExtraCities != null)
        {
            var clean = save.ExtraCities.Select(c => c.Trim()).Where(c => c.Length > 1).Distinct().ToList();
            if (clean.Count > 100 || clean.Any(c => c.Length > 60)) return WorkResult<KeywordSetupDto>.Fail(400, "Up to 100 city or area names, each up to 60 characters.");
            row.CitiesJson = KeywordShared.Write(clean);
        }
        if (save.Weights != null)
        {
            var w = save.Weights;
            if (new[] { w.Demand, w.Fit, w.Close, w.Value }.Any(x => x is < 0 or > 10)) return WorkResult<KeywordSetupDto>.Fail(400, "Each weight is from 0 to 10.");
            row.WeightsJson = KeywordShared.Write(w);
        }
        if (save.MinVisitors is { } min)
        {
            if (min is < 5 or > 100) return WorkResult<KeywordSetupDto>.Fail(400, "Show a site search once at least 5 (and at most 100) visitors typed it.");
            row.MinVisitors = min;
        }
        row.UpdatedAt = _now();
        row.UpdatedByUserId = actor.UserId;
        await _db.SaveChangesAsync(ct);
        return WorkResult<KeywordSetupDto>.Success(await SetupAsync(actor, ct), "Saved. The next collection uses it.");
    }
}
