#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Data;
using CMSAPI.Data.Google;
using CMSAPI.Data.Keywords;
using CMSAPI.Data.WhatsApp;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// CMS → Keywords: phrase rules (language, intent, city, privacy), scoring, the daily collection from Search Console,
// Doctor Dekho site search and Keyword Planner (Google is faked), and the screens' opportunities and content plan.
public class KeywordTests
{
    // Saturday 28 Nov 2026, 06:00 IST.
    private static readonly DateTime Now = new(2026, 11, 28, 0, 30, 0, DateTimeKind.Utc);
    private static readonly WaSecretBox Box = new(new byte[32]);
    private static readonly Guid Asha = Guid.NewGuid();

    private sealed class FakeGoogle : IGoogleApi
    {
        public Func<DateOnly, List<GscQueryPageRow>>? Gsc { get; set; }
        public Func<KeywordIdeaRequest, List<KeywordIdea>>? Ideas { get; set; }
        public List<KeywordIdeaRequest> Asked { get; } = new();
        public List<DateOnly> GscDays { get; } = new();

        public string SignInUrl(string clientId, string redirectUri, string state) => "https://accounts.example/";
        public Task<GoogleTokens> ExchangeCodeAsync(string clientId, string clientSecret, string code, string redirectUri, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> EmailAsync(string accessToken, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<List<GaProperty>> Ga4PropertiesAsync(GoogleClient c, CancellationToken ct = default) => Task.FromResult(new List<GaProperty>());
        public Task<GaReport> RunReportAsync(GoogleClient c, string propertyId, GaReportRequest r, CancellationToken ct = default) => Task.FromResult(new GaReport());
        public Task<List<AdsCustomer>> AdsCustomersAsync(GoogleClient c, CancellationToken ct = default) => Task.FromResult(new List<AdsCustomer>());
        public Task<List<JsonElement>> AdsSearchAsync(GoogleClient c, string customerId, string? loginCustomerId, string gaql, CancellationToken ct = default) => Task.FromResult(new List<JsonElement>());
        public Task<List<string>> SearchConsoleSitesAsync(GoogleClient c, CancellationToken ct = default) => Task.FromResult(new List<string>());
        public Task<List<GscRow>> SearchConsoleQueriesAsync(GoogleClient c, string site, DateOnly from, DateOnly to, string? pageUrl, int limit, CancellationToken ct = default) => Task.FromResult(new List<GscRow>());
        public Task<string?> IngestConversionsAsync(GoogleClient c, ConversionBatch batch, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<List<GscQueryPageRow>> SearchConsoleDayAsync(GoogleClient c, string site, DateOnly day, int max, CancellationToken ct = default)
        {
            GscDays.Add(day);
            return Task.FromResult(Gsc?.Invoke(day) ?? new List<GscQueryPageRow>());
        }
        public Task<List<KeywordIdea>> KeywordIdeasAsync(GoogleClient c, string customerId, string? loginCustomerId, KeywordIdeaRequest request, CancellationToken ct = default)
        {
            Asked.Add(request);
            return Task.FromResult(Ideas?.Invoke(request) ?? new List<KeywordIdea>());
        }
    }

    private static CmsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static void Account(CmsDbContext db, string site, bool gsc = true, bool ads = true)
    {
        db.GoogleAccounts.Add(new GoogleAccount
        {
            AccountId = Guid.NewGuid(), Name = site == KeywordSites.DoctorDekho ? "Doctor Dekho" : "NexEagle", Site = site, ClientId = "1-abc.apps.googleusercontent.com",
            ClientSecretEnc = Box.Seal("secret"), RefreshTokenEnc = Box.Seal("refresh"), SearchConsoleSite = gsc ? $"sc-domain:{site}.example" : null,
            AdsCustomerId = ads ? "1112223333" : null, Currency = "INR", IsActive = true, CreatedAt = Now, UpdatedAt = Now,
        });
        db.SaveChanges();
    }

    private static AppDbContext NewEvents() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static KeywordCollector Collector(CmsDbContext db, FakeGoogle g, DateTime? now = null, AppDbContext? events = null) =>
        new(db, events ?? NewEvents(), g, new GoogleKeys(Box), NullLogger<KeywordCollector>.Instance, () => now ?? Now) { PlannerDelayMs = 0 };

    private static KeywordsService Service(CmsDbContext db, DateTime? now = null) => new(db, () => now ?? Now);

    private static KeywordActor Editor => new() { UserId = Asha, CanEdit = true, CanConnect = true };
    private static KeywordActor Viewer => new() { UserId = Asha };

    private static DateOnly Today => TrackingTime.IstDate(Now);

    private static Keyword Kw(CmsDbContext db, string site, string text, long? volume = null, Guid? topic = null)
    {
        var norm = KeywordText.Normalize(text);
        var k = new Keyword
        {
            KeywordId = Guid.NewGuid(), Site = site, Text = norm, Normalized = norm, Language = KeywordText.Language(norm), Intent = KeywordText.Intent(site, norm),
            City = KeywordText.City(norm), TopicId = topic, Sources = volume != null ? "planner" : "gsc", AvgMonthlySearches = volume, VolumeAt = volume != null ? Now : null,
            FirstSeenAt = Now.AddDays(-60), LastSeenAt = Now,
        };
        db.Keywords.Add(k);
        return k;
    }

    private static void Gsc(CmsDbContext db, Keyword k, DateOnly day, int impressions, int clicks, decimal position, string page = "/health/dengue") =>
        db.KeywordObservations.Add(new KeywordObservation { KeywordId = k.KeywordId, Day = day, Source = "gsc", Impressions = impressions, Clicks = clicks, Position = position, Page = page });

    // ── phrase rules ─────────────────────────────────────────────────────────

    [Fact]
    public void Phrases_are_normalised_and_tagged_by_script_language_intent_and_city()
    {
        Assert.Equal("dengue ke lakshan", KeywordText.Normalize("  Dengue  ke LAKSHAN?? "));
        Assert.Equal("en", KeywordText.Language("dengue symptoms"));
        Assert.Equal("hg", KeywordText.Language("dengue ke lakshan"));
        Assert.Equal("hi", KeywordText.Language(KeywordText.Normalize("डेंगू के लक्षण")));
        Assert.Equal("bn", KeywordText.Language(KeywordText.Normalize("ডেঙ্গু জ্বরের লক্ষণ")));
        Assert.Equal("nearme", KeywordText.Intent(KeywordSites.DoctorDekho, "skin doctor near me"));
        Assert.Equal("software", KeywordText.Intent(KeywordSites.NexEagle, "hospital management software"));
        Assert.Equal("Patna", KeywordText.City("gynecologist in patna"));
        Assert.Null(KeywordText.City("gynecologist online"));
        Assert.True(KeywordText.IsQuestion("what is dengue"));
    }

    [Fact]
    public void Phrases_that_may_hold_a_phone_number_or_email_are_never_kept()
    {
        Assert.True(KeywordCollector.Unusable("call 9876543210"));
        Assert.True(KeywordCollector.Unusable(KeywordText.Normalize("me@example.com"), "me@example.com"));
        Assert.True(KeywordCollector.Unusable("asha sharma gmail com"));
        Assert.True(KeywordCollector.Unusable("987 654 3210"));
        Assert.True(KeywordCollector.Unusable("a"));
        Assert.False(KeywordCollector.Unusable("dengue test price in patna"));
    }

    [Fact]
    public void The_daily_run_is_due_once_after_0530_IST()
    {
        var before = new DateTime(2026, 11, 27, 23, 50, 0, DateTimeKind.Utc);  // 05:20 IST
        Assert.False(KeywordCollector.Due(before, null));
        Assert.True(KeywordCollector.Due(Now, null));
        Assert.True(KeywordCollector.Due(Now, Now.AddDays(-1)));
        Assert.False(KeywordCollector.Due(Now.AddHours(3), Now));
    }

    // ── scoring ──────────────────────────────────────────────────────────────

    [Fact]
    public void Scoring_names_the_reason_a_keyword_is_worth_work()
    {
        var w = new KeywordWeights();
        Assert.Equal("close", KeywordScoring.Evaluate(new KeywordFacts { Site = KeywordSites.DoctorDekho, Intent = "info", Impressions = 400, Clicks = 4, Position = 9.4m }, w).Kind);
        Assert.Equal("ctr", KeywordScoring.Evaluate(new KeywordFacts { Site = KeywordSites.DoctorDekho, Intent = "info", Impressions = 1000, Clicks = 5, Position = 2.1m }, w).Kind);
        Assert.Equal("gap", KeywordScoring.Evaluate(new KeywordFacts { Site = KeywordSites.DoctorDekho, Intent = "doctor", Volume = 2400 }, w).Kind);
        Assert.Equal("gap", KeywordScoring.Evaluate(new KeywordFacts { Site = KeywordSites.DoctorDekho, Intent = "doctor", Visitors = 12 }, w).Kind);
        Assert.Equal("rising", KeywordScoring.Evaluate(new KeywordFacts { Site = KeywordSites.DoctorDekho, Last7 = 90, Prev7 = 30 }, w).Kind);
        Assert.Null(KeywordScoring.Evaluate(new KeywordFacts { Site = KeywordSites.DoctorDekho, Impressions = 10, Position = 30m }, w).Kind);

        var lang = KeywordScoring.Evaluate(new KeywordFacts
        {
            Site = KeywordSites.DoctorDekho, Intent = "info", Impressions = 500, Position = 2m, Clicks = 50,
            DemandByLanguage = new() { ["en"] = 300, ["hg"] = 900, ["hi"] = 200 }, PublishedLanguages = new() { "en" },
        }, w, topic: true);
        Assert.Equal("lang", lang.Kind);
        Assert.Contains("Hinglish, Hindi", lang.Why);

        // Demand, a city and a buying intent score higher than a vague phrase.
        var strong = KeywordScoring.Score(new KeywordFacts { Site = KeywordSites.DoctorDekho, Intent = "doctor", City = "Patna", Volume = 5000, Position = 12m }, w);
        var weak = KeywordScoring.Score(new KeywordFacts { Site = KeywordSites.DoctorDekho, Intent = "other", Volume = 20, Position = 60m }, w);
        Assert.True(strong > weak + 30, $"{strong} vs {weak}");
        Assert.InRange(strong, 0, 100);
    }

    // ── collection ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_Console_rows_become_keywords_daily_numbers_and_a_suggested_topic_per_page()
    {
        using var db = NewDb();
        Account(db, KeywordSites.DoctorDekho, ads: false);
        var g = new FakeGoogle
        {
            Gsc = day => new()
            {
                new() { Query = "Dengue ke lakshan", Page = "https://doctordekho.example/health/dengue", Impressions = 40, Clicks = 2, Position = 8 },
                new() { Query = "dengue ke  lakshan", Page = "https://doctordekho.example/blog/fever", Impressions = 10, Clicks = 0, Position = 18 },
                new() { Query = "dengue symptoms", Page = "https://doctordekho.example/health/dengue", Impressions = 100, Clicks = 5, Position = 6 },
                new() { Query = "call 9876543210", Page = "https://doctordekho.example/", Impressions = 3, Clicks = 0, Position = 1 },
            },
        };
        var result = await Collector(db, g).RunAsync("gsc");

        Assert.Equal(30, g.GscDays.Count);  // first run: 30 finished days
        Assert.Equal(Today.AddDays(-2), g.GscDays.Max());
        Assert.Equal(120, result["gsc"]);
        var keywords = db.Keywords.ToList();
        Assert.Equal(2, keywords.Count);  // the phone number is dropped, the two spellings are one keyword
        var hg = keywords.Single(k => k.Normalized == "dengue ke lakshan");
        Assert.Equal("hg", hg.Language);
        var obs = db.KeywordObservations.Where(o => o.KeywordId == hg.KeywordId).ToList();
        Assert.Equal(30, obs.Count);
        var one = obs.First();
        Assert.Equal(50, one.Impressions);
        Assert.Equal(10m, one.Position);  // (40×8 + 10×18) / 50
        Assert.Equal("https://doctordekho.example/health/dengue", one.Page);

        var topic = Assert.Single(db.KeywordTopics.ToList());
        Assert.True(topic.IsSuggested);
        Assert.Equal("https://doctordekho.example/health/dengue", topic.TargetPage);
        Assert.All(keywords, k => Assert.Equal(topic.TopicId, k.TopicId));
        var run = db.KeywordSourceRuns.Single(r => r.SourceKey == "gsc");
        Assert.Null(run.LastError);
        Assert.NotNull(run.LastOkAt);

        // The next day reads only the last three finished days and keeps a person's topic choice.
        hg.TopicId = null;
        hg.TopicByPerson = true;
        g.GscDays.Clear();
        await Collector(db, g, Now.AddDays(1)).RunAsync("gsc");
        Assert.Equal(3, g.GscDays.Count);
        Assert.Null(hg.TopicId);
    }

    [Fact]
    public async Task Site_searches_are_kept_only_when_enough_visitors_typed_them_and_never_doctor_names()
    {
        using var db = NewDb();
        using var events = NewEvents();
        void Search(string query, string session, string kind = "condition", int daysAgo = 3) => events.AnalyticsEvents.Add(new AnalyticsEvent
        {
            EventId = Guid.NewGuid(), EventType = "search_performed", SessionId = session, OccurredAt = Now.AddDays(-daysAgo),
            MetadataJson = JsonSerializer.Serialize(new { query, searchKind = kind }),
        });
        for (var i = 0; i < 6; i++) Search("Piles doctor in Purnia", $"s{i}");
        Search("Piles doctor in Purnia", "s0");  // the same visitor again does not count twice
        for (var i = 0; i < 4; i++) Search("rare condition xyz", $"r{i}");
        for (var i = 0; i < 9; i++) Search("Dr Example Name", $"d{i}", kind: "doctor_name");
        for (var i = 0; i < 7; i++) Search("9876543210", $"p{i}");
        for (var i = 0; i < 7; i++) Search("old search", $"o{i}", daysAgo: 40);
        events.SaveChanges();

        var result = await Collector(db, new FakeGoogle(), events: events).RunAsync("site");

        Assert.Equal(1, result["site"]);
        var k = Assert.Single(db.Keywords.ToList());
        Assert.Equal("piles doctor in purnia", k.Normalized);
        Assert.Equal(KeywordSites.DoctorDekho, k.Site);
        Assert.Equal("Purnea", k.City);
        Assert.Equal("site", k.Sources);
        Assert.Equal(6, db.KeywordObservations.Sum(o => o.Visitors));
    }

    [Fact]
    public async Task Keyword_Planner_fills_volumes_in_Indian_languages_and_rotates_seed_lists()
    {
        using var db = NewDb();
        Account(db, KeywordSites.DoctorDekho, gsc: false);
        db.KeywordSettings.Add(new KeywordSettings
        {
            SettingsId = 1, UpdatedAt = Now,
            SeedsJson = KeywordShared.Write(new Dictionary<string, List<string>> { [KeywordSites.DoctorDekho] = new() { "dengue", "typhoid" } }),
        });
        var known = Kw(db, KeywordSites.DoctorDekho, "dengue test price");
        db.SaveChanges();
        var g = new FakeGoogle
        {
            Ideas = r => r.Seeds.Contains("dengue test price")
                ? new() { new() { Text = "dengue test price", AvgMonthlySearches = 880, Competition = "LOW", LowBidMicros = 4_500_000, HighBidMicros = 12_000_000,
                    Months = new() { new(2026, 10, 900), new(2026, 9, 1200) } } }
                : new()
                {
                    new() { Text = r.LanguageId == "1023" ? "डेंगू के लक्षण" : "dengue symptoms", AvgMonthlySearches = 9900 },
                    new() { Text = "dengue tiny", AvgMonthlySearches = 5 },
                    new() { Text = "dengue", AvgMonthlySearches = null },
                },
        };

        var result = await Collector(db, g).RunAsync("planner");

        // One refresh request for the keyword without a volume, then the seed list in English, Hindi and Bengali.
        Assert.Equal(4, result["planner"]);
        Assert.Equal(new[] { "1000", "1000", "1023", "1056" }, g.Asked.Select(a => a.LanguageId).ToArray());
        Assert.Equal(880, known.AvgMonthlySearches);
        Assert.Equal(4.5m, known.LowBid);
        Assert.Contains("planner", known.Sources);
        Assert.NotNull(known.MonthlyJson);
        Assert.Contains(db.Keywords.ToList(), k => k.Normalized == "dengue symptoms" && k.AvgMonthlySearches == 9900);
        Assert.Contains(db.Keywords.ToList(), k => k.Language == "hi" && k.AvgMonthlySearches == 9900);
        Assert.DoesNotContain(db.Keywords.ToList(), k => k.Normalized == "dengue tiny");  // under 10 a month
        Assert.Contains(db.Keywords.ToList(), k => k.Normalized == "dengue");  // asked for, so kept
        Assert.Equal(3, db.KeywordSettings.Single().PlannerCursor);

        // Tomorrow the rotation goes on from where it stopped, and the fresh volume is not asked again.
        g.Asked.Clear();
        await Collector(db, g, Now.AddDays(1)).RunAsync("planner");
        Assert.DoesNotContain(g.Asked, a => a.Seeds.Contains("dengue test price"));
        Assert.Equal("1000", g.Asked[0].LanguageId);
    }

    [Fact]
    public async Task A_source_that_is_not_set_up_is_reported_and_does_not_stop_the_others()
    {
        using var db = NewDb();
        var result = await Collector(db, new FakeGoogle()).RunAsync();
        Assert.Equal(-1, result["gsc"]);
        Assert.Equal(-1, result["planner"]);
        Assert.Equal(0, result["site"]);
        Assert.Contains("Google → Setup", db.KeywordSourceRuns.Single(r => r.SourceKey == "gsc").LastError);
        var setup = await Service(db).SetupAsync(Viewer);
        Assert.Equal("warn", setup.Sources.Single(s => s.Key == "gsc").State);
        Assert.Equal("ok", setup.Sources.Single(s => s.Key == "site").State);
        Assert.Equal("off", setup.Sources.Single(s => s.Key == "trends").State);
        Assert.False(setup.CanEdit);
    }

    // ── screens ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Overview_lists_close_to_page_one_and_language_gaps_as_opportunities()
    {
        using var db = NewDb();
        var topic = new KeywordTopic { TopicId = Guid.NewGuid(), Site = KeywordSites.DoctorDekho, Name = "Dengue", TargetPage = "/health/dengue", CreatedAt = Now, UpdatedAt = Now };
        db.KeywordTopics.Add(topic);
        var en = Kw(db, KeywordSites.DoctorDekho, "dengue symptoms", 300, topic.TopicId);
        Kw(db, KeywordSites.DoctorDekho, "dengue ke lakshan", 1900, topic.TopicId);
        var close = Kw(db, KeywordSites.DoctorDekho, "piles doctor in purnia");
        for (var d = 3; d < 31; d++)
        {
            Gsc(db, en, Today.AddDays(-d), 20, 2, 3m);
            Gsc(db, close, Today.AddDays(-d), 5, 0, 11m, "/doctors/purnia");
        }
        db.SaveChanges();

        var o = await Service(db).OverviewAsync(null);

        Assert.Equal(3, o.Tracked);
        Assert.Equal(27 * 20 + 27 * 5, o.Impressions);  // the 28-day window ends 2 days ago
        var lang = o.Opportunities.Single(x => x.Kind == "lang");
        Assert.Equal(topic.TopicId, lang.TopicId);
        Assert.Equal("dengue ke lakshan", lang.Keywords[0]);
        var near = o.Opportunities.Single(x => x.Kind == "close");
        Assert.Equal(close.KeywordId, near.KeywordId);
        Assert.Contains(o.Languages, l => l.Language == "hg" && l.Searches == 1900);

        var list = await Service(db).ListAsync(new KeywordQuery { Site = KeywordSites.DoctorDekho, Language = "en", Q = "Dengue" });
        var row = Assert.Single(list.Items);
        Assert.Equal(8, row.Weeks.Count);
        Assert.Equal(3m, row.Position);
        Assert.Equal("Dengue", row.TopicName);
    }

    [Fact]
    public async Task Content_plan_starts_the_brief_from_the_topic_and_shows_results_after_publishing()
    {
        using var db = NewDb();
        var topic = new KeywordTopic { TopicId = Guid.NewGuid(), Site = KeywordSites.DoctorDekho, Name = "Dengue", TargetPage = "/health/dengue", CreatedAt = Now, UpdatedAt = Now };
        db.KeywordTopics.Add(topic);
        var en = Kw(db, KeywordSites.DoctorDekho, "dengue symptoms", 300, topic.TopicId);
        Kw(db, KeywordSites.DoctorDekho, "dengue ke lakshan", 1900, topic.TopicId);
        Kw(db, KeywordSites.DoctorDekho, "dengue ka ilaj", 400, topic.TopicId);
        db.CmsUsers.Add(new CmsUser { UserId = Asha, FullName = "Asha" });
        db.SaveChanges();
        var service = Service(db);
        var save = new KeywordContentSave
        {
            Site = KeywordSites.DoctorDekho, TopicId = topic.TopicId, Kind = "article", Title = "Dengue: signs, tests and when to see a doctor", Stage = "brief",
            Languages = new() { "en", "hg" }, OwnerUserId = Asha,
        };

        Assert.Equal(403, (await service.CreateContentAsync(Viewer, save)).Status);
        Assert.Equal(400, (await service.CreateContentAsync(Editor, new KeywordContentSave { Site = "other", Kind = "article", Title = "x", Stage = "idea", Languages = new() { "en" } })).Status);
        Assert.Equal(400, (await service.CreateContentAsync(Editor, new KeywordContentSave { Site = KeywordSites.DoctorDekho, Kind = "article", Title = "x", Stage = "idea", Languages = new() { "fr" } })).Status);

        var created = await service.CreateContentAsync(Editor, save);
        Assert.True(created.Ok, created.Message);
        var c = created.Data!;
        Assert.Equal("dengue ke lakshan", c.Brief.MainKeywords["hg"]);
        Assert.Equal("dengue symptoms", c.Brief.MainKeywords["en"]);
        Assert.Equal("/health/dengue", c.Brief.Page);
        Assert.Equal("Asha", c.OwnerName);
        Assert.Null(c.PublishedAt);

        // Published 20 days ago: the 28 days before against the days since.
        for (var d = 2; d < 50; d++) Gsc(db, en, Today.AddDays(-d), d > 20 ? 10 : 30, d > 20 ? 0 : 3, d > 20 ? 14m : 6m);
        db.SaveChanges();
        save.Stage = "published";
        save.Url = "https://doctordekho.example/health/dengue";
        var published = await Service(db, Now.AddDays(-20)).UpdateContentAsync(Editor, c.ContentId, save);
        Assert.True(published.Ok, published.Message);
        var item = (await service.ContentAsync(KeywordSites.DoctorDekho, null)).Single();
        Assert.NotNull(item.Result);
        Assert.Equal(18, item.Result!.DaysAfter);
        Assert.Equal(280, item.Result.ImpressionsBefore);
        Assert.Equal(18 * 30, item.Result.ImpressionsAfter);
        Assert.Equal(14m, item.Result.PositionBefore);
        Assert.Equal(6m, item.Result.PositionAfter);

        // A published language counts: the topic no longer shows a Hinglish gap, only Hindi would (none here).
        var topics = await service.TopicsAsync(KeywordSites.DoctorDekho, null);
        Assert.NotEqual("lang", topics.Single().Reason);

        Assert.True((await service.DeleteContentAsync(Editor, c.ContentId)).Ok);
        Assert.Empty(db.KeywordContent.ToList());
    }

    [Fact]
    public async Task People_can_correct_a_keyword_and_merge_topics_of_the_same_website()
    {
        using var db = NewDb();
        var a = new KeywordTopic { TopicId = Guid.NewGuid(), Site = KeywordSites.DoctorDekho, Name = "Dengue", CreatedAt = Now, UpdatedAt = Now };
        var b = new KeywordTopic { TopicId = Guid.NewGuid(), Site = KeywordSites.DoctorDekho, Name = "Dengue fever", IsSuggested = true, TargetPage = "/blog/fever", CreatedAt = Now, UpdatedAt = Now };
        var other = new KeywordTopic { TopicId = Guid.NewGuid(), Site = KeywordSites.NexEagle, Name = "HMS", CreatedAt = Now, UpdatedAt = Now };
        db.KeywordTopics.AddRange(new[] { a, b, other });
        var k = Kw(db, KeywordSites.DoctorDekho, "dengue bukhar", 200, b.TopicId);
        db.SaveChanges();
        var service = Service(db);

        Assert.Equal(403, (await service.UpdateAsync(Viewer, k.KeywordId, new KeywordUpdate { Language = "hi" })).Status);
        Assert.Equal(400, (await service.UpdateAsync(Editor, k.KeywordId, new KeywordUpdate { TopicId = other.TopicId })).Status);
        var fixedK = await service.UpdateAsync(Editor, k.KeywordId, new KeywordUpdate { Language = "hg", Intent = "info" });
        Assert.True(fixedK.Ok, fixedK.Message);
        Assert.True(k.LanguageByPerson);
        Assert.True(k.IntentByPerson);

        Assert.Equal(400, (await service.MergeTopicAsync(Editor, b.TopicId, other.TopicId)).Status);
        var merged = await service.MergeTopicAsync(Editor, b.TopicId, a.TopicId);
        Assert.True(merged.Ok, merged.Message);
        Assert.Equal(a.TopicId, k.TopicId);
        Assert.Equal(2, db.KeywordTopics.Count());

        Assert.Equal(400, (await service.CreateTopicAsync(Editor, new KeywordTopicSave { Site = KeywordSites.DoctorDekho, Name = "Typhoid", TargetPage = "not a page" })).Status);
        var t = await service.CreateTopicAsync(Editor, new KeywordTopicSave { Site = KeywordSites.DoctorDekho, Name = "Typhoid", TargetPage = "/health/typhoid" });
        Assert.True(t.Ok, t.Message);
    }

    [Fact]
    public async Task Setup_keeps_the_site_search_threshold_at_five_or_more_and_checks_competitor_sites()
    {
        using var db = NewDb();
        var service = Service(db);
        Assert.Equal(403, (await service.SaveSetupAsync(new KeywordActor { UserId = Asha, CanEdit = true }, new KeywordSetupSave { MinVisitors = 10 })).Status);
        Assert.Equal(400, (await service.SaveSetupAsync(Editor, new KeywordSetupSave { MinVisitors = 2 })).Status);
        Assert.Equal(400, (await service.SaveSetupAsync(Editor, new KeywordSetupSave { CompetitorSites = new() { [KeywordSites.NexEagle] = new() { "not a site" } } })).Status);
        var saved = await service.SaveSetupAsync(Editor, new KeywordSetupSave
        {
            MinVisitors = 8,
            CompetitorSites = new() { [KeywordSites.NexEagle] = new() { "https://www.Competitor-Example.com/pricing" } },
            Seeds = new() { [KeywordSites.DoctorDekho] = new() { " Dengue ", "dengue", "Typhoid!" } },
        });
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(8, saved.Data!.MinVisitors);
        Assert.Equal(new[] { "competitor-example.com" }, saved.Data.CompetitorSites[KeywordSites.NexEagle].ToArray());
        Assert.Equal(new[] { "dengue", "typhoid" }, saved.Data.Seeds[KeywordSites.DoctorDekho].ToArray());
    }
}
