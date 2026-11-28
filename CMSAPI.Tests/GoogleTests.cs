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
using CMSAPI.Data.WhatsApp;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// CMS → Google: lead-form webhook, website leads with the click ID, results sent back to Google Ads, the signed
// sign-in state, Setup rules, and the numbers screens (Google is faked).
public class GoogleTests
{
    private static readonly DateTime Now = new(2026, 11, 27, 6, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly WaSecretBox Box = new(new byte[32]);

    private sealed class FakeGoogle : IGoogleApi
    {
        public List<string> Queries { get; } = new();
        public List<ConversionBatch> Sent { get; } = new();
        public Func<GaReportRequest, GaReport>? Report { get; set; }
        public Func<string, List<JsonElement>>? Ads { get; set; }
        public string? FailIngestWith { get; set; }
        public List<GaProperty> Properties { get; } = new() { new GaProperty { PropertyId = "321", Name = "nexeagle.com - GA4", AccountName = "NexEagle" } };
        public List<AdsCustomer> Customers { get; } = new()
        {
            new AdsCustomer { CustomerId = "1112223333", Name = "NexEagle Ads", Currency = "INR", TimeZone = "Asia/Kolkata", LoginCustomerId = "9990001111" },
        };

        public string SignInUrl(string clientId, string redirectUri, string state) => $"https://accounts.example/?state={state}";
        public Task<GoogleTokens> ExchangeCodeAsync(string clientId, string clientSecret, string code, string redirectUri, CancellationToken ct = default) =>
            Task.FromResult(new GoogleTokens("access", code == "no-refresh" ? null : "refresh-1", 3600, string.Join(' ', GoogleApi.Scopes)));
        public Task<string?> EmailAsync(string accessToken, CancellationToken ct = default) => Task.FromResult<string?>("marketing@example.com");
        public Task<List<GaProperty>> Ga4PropertiesAsync(GoogleClient c, CancellationToken ct = default) => Task.FromResult(Properties);
        public Task<GaReport> RunReportAsync(GoogleClient c, string propertyId, GaReportRequest r, CancellationToken ct = default) =>
            Task.FromResult(Report?.Invoke(r) ?? new GaReport());
        public Task<List<AdsCustomer>> AdsCustomersAsync(GoogleClient c, CancellationToken ct = default) => Task.FromResult(Customers);
        public Task<List<JsonElement>> AdsSearchAsync(GoogleClient c, string customerId, string? loginCustomerId, string gaql, CancellationToken ct = default)
        {
            Queries.Add(gaql);
            return Task.FromResult(Ads?.Invoke(gaql) ?? new List<JsonElement>());
        }
        public Task<List<string>> SearchConsoleSitesAsync(GoogleClient c, CancellationToken ct = default) => Task.FromResult(new List<string> { "sc-domain:nexeagle.com" });
        public Task<List<GscRow>> SearchConsoleQueriesAsync(GoogleClient c, string site, DateOnly from, DateOnly to, string? pageUrl, int limit, CancellationToken ct = default) =>
            Task.FromResult(new List<GscRow> { new() { Key = "hospital software", Clicks = 12, Impressions = 300, Position = 6.2 } });
        public Task<string?> IngestConversionsAsync(GoogleClient c, ConversionBatch batch, CancellationToken ct = default)
        {
            if (FailIngestWith != null) throw new GoogleApiException(FailIngestWith, 400, "INVALID_ARGUMENT");
            Sent.Add(batch);
            return Task.FromResult<string?>("req-" + Sent.Count);
        }
    }

    private static CmsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static GoogleAccount Account(CmsDbContext db, string name = "NexEagle", string? site = "nexeagle", bool ads = true)
    {
        var a = new GoogleAccount
        {
            AccountId = Guid.NewGuid(), Name = name, Site = site, ClientId = "123-abc.apps.googleusercontent.com", ClientSecretEnc = Box.Seal("secret"),
            RefreshTokenEnc = Box.Seal("refresh"), SignedInEmail = "marketing@example.com", Ga4PropertyId = "321", Ga4PropertyName = "nexeagle.com - GA4",
            AdsCustomerId = ads ? "1112223333" : null, AdsLoginCustomerId = ads ? "9990001111" : null, AdsAccountName = ads ? "NexEagle Ads" : null,
            Currency = "INR", LeadKeyEnc = Box.Seal("lead-key-1"), IsActive = true, CreatedAt = Now, UpdatedAt = Now,
        };
        db.GoogleAccounts.Add(a);
        db.SaveChanges();
        return a;
    }

    private static GoogleKeys Keys => new(Box);
    private static GoogleLeadsService Leads(CmsDbContext db, FakeGoogle g, DateTime? now = null) => new(db, g, Keys, NullLogger<GoogleLeadsService>.Instance, () => now ?? Now);
    private static GoogleOutcomesService Outcomes(CmsDbContext db, FakeGoogle g, DateTime? now = null) => new(db, g, Keys, NullLogger<GoogleOutcomesService>.Instance, () => now ?? Now);
    private static GoogleSetupService Setup(CmsDbContext db, FakeGoogle g) => new(db, g, Keys, new GoogleSignInState(new byte[32]), NullLogger<GoogleSetupService>.Instance, () => Now);
    private static GoogleReportsService Reports(CmsDbContext db, FakeGoogle g) =>
        new(db, g, Keys, new MemoryCache(new MemoryCacheOptions()), new ServiceCollection().BuildServiceProvider(), NullLogger<GoogleReportsService>.Instance, () => Now);

    private static GoogleActor Connector => new() { UserId = Asha, Name = "Asha", CanAds = true, CanConnect = true };
    private static GoogleActor Viewer => new() { UserId = Asha, Name = "Asha" };

    private static JsonElement J(object o) => JsonSerializer.SerializeToElement(o);

    // ── lead form webhook ────────────────────────────────────────────────────

    private static JsonElement FormLead(string key, string leadId, bool test = false) => J(new
    {
        lead_id = leadId, api_version = "1.0", form_id = 44, campaign_id = 555, adgroup_id = 66, creative_id = 77, google_key = key, is_test = test,
        gcl_id = "gclid-form-1",
        user_column_data = new object[]
        {
            new { column_name = "Full Name", string_value = "Example Hospital Owner", column_id = "FULL_NAME" },
            new { column_name = "User Phone", string_value = "+91 98000 00041", column_id = "PHONE_NUMBER" },
            new { column_name = "City", string_value = "Purnia", column_id = "CITY" },
            new { column_name = "How many beds?", string_value = "40", column_id = "beds_q" },
        },
    });

    [Fact]
    public async Task A_lead_form_lead_becomes_one_CRM_lead_with_its_campaign()
    {
        using var db = NewDb();
        var a = Account(db);
        var g = new FakeGoogle { Ads = q => q.Contains("FROM campaign WHERE campaign.id = 555") ? new() { J(new { campaign = new { name = "1HMS demo, Bihar" } }) } : new() };
        var leads = Leads(db, g);

        Assert.Equal(401, (await leads.IngestFormLeadAsync(FormLead("wrong", "L1"))).Status);
        var first = await leads.IngestFormLeadAsync(FormLead("lead-key-1", "L1"));
        Assert.True(first.Ok, first.Message);
        Assert.True((await leads.IngestFormLeadAsync(FormLead("lead-key-1", "L1"))).Ok);

        var crm = Assert.Single(db.CmsSalesLeads);
        Assert.Equal("GOOGLE_ADS", crm.Source);
        Assert.Equal("Example Hospital Owner", crm.ContactName);
        Assert.Equal("+91 98000 00041", crm.Mobile);
        Assert.Equal("Purnia", crm.City);
        Assert.Contains("How many beds?: 40", crm.Notes);
        var link = Assert.Single(db.GoogleLeads);
        Assert.Equal("1HMS demo, Bihar", link.CampaignName);
        Assert.Equal("gclid-form-1", link.Gclid);
        Assert.Equal(Now, db.GoogleAccounts.Single().LastLeadAt);
    }

    [Fact]
    public async Task A_test_lead_proves_the_connection_but_makes_no_CRM_lead()
    {
        using var db = NewDb();
        Account(db);
        var r = await Leads(db, new FakeGoogle()).IngestFormLeadAsync(FormLead("lead-key-1", "TEST", test: true));
        Assert.True(r.Ok);
        Assert.Empty(db.CmsSalesLeads);
        Assert.Equal(Now, db.GoogleAccounts.Single().LastLeadAt);
    }

    // ── website form ─────────────────────────────────────────────────────────

    private static GoogleWebsiteLead Web(string phone = "9800000051", string? gclid = "gclid-web-1") => new()
    {
        Name = "Example Clinic Owner", Phone = phone, Email = "owner@example.com", Organisation = "Example Clinic", City = "Purnia",
        Message = "We need OPD software", Page = "/contact", Gclid = gclid, UtmTerm = "opd software", LandingPage = "/1hms",
    };

    [Fact]
    public async Task A_website_lead_from_an_ad_keeps_its_click_campaign_and_keyword()
    {
        using var db = NewDb();
        Account(db);
        var g = new FakeGoogle
        {
            Ads = q => q.Contains("FROM click_view") && q.Contains("2026-11-27")
                ? new() { J(new { campaign = new { id = "555", name = "1HMS demo, Bihar" }, adGroup = new { id = "66" }, clickView = new { keywordInfo = new { text = "hospital management software" } } }) }
                : new(),
        };
        var r = await Leads(db, g).IngestWebsiteLeadAsync("nexeagle", Web(), "203.0.113.1");
        Assert.True(r.Ok, r.Message);
        var crm = Assert.Single(db.CmsSalesLeads);
        Assert.Equal("WEBSITE", crm.Source);
        Assert.Equal("Example Clinic", crm.HospitalName);
        Assert.Equal("google", crm.UtmSource);
        Assert.Contains("We need OPD software", crm.Notes);
        var link = Assert.Single(db.GoogleLeads);
        Assert.Equal("web", link.Source);
        Assert.Equal("555", link.CampaignId);
        Assert.Equal("hospital management software", link.Keyword);
        Assert.Equal("/1hms", link.LandingPage);
    }

    [Fact]
    public async Task The_same_person_twice_in_a_day_is_one_lead_and_bots_are_dropped()
    {
        using var db = NewDb();
        Account(db);
        var leads = Leads(db, new FakeGoogle());
        Assert.True((await leads.IngestWebsiteLeadAsync("nexeagle", Web("+91 98000 00052", null), "203.0.113.2")).Ok);
        Assert.True((await leads.IngestWebsiteLeadAsync("nexeagle", Web("9800000052", null), "203.0.113.2")).Ok);
        var bot = Web("9800000053", null);
        bot.Website = "http://spam.example";
        Assert.True((await leads.IngestWebsiteLeadAsync("nexeagle", bot, "203.0.113.2")).Ok);
        Assert.Single(db.CmsSalesLeads);
        var noName = Web("9800000054", null);
        noName.Name = " ";
        Assert.Equal(400, (await leads.IngestWebsiteLeadAsync("nexeagle", noName, "203.0.113.2")).Status);
        Assert.Equal(404, (await leads.IngestWebsiteLeadAsync("unknown", Web(), "203.0.113.2")).Status);
    }

    [Fact]
    public async Task Too_many_forms_from_one_address_are_refused()
    {
        using var db = NewDb();
        Account(db);
        var leads = Leads(db, new FakeGoogle());
        var ip = "198.51.100." + new Random().Next(1, 250);
        for (var i = 0; i < 5; i++) Assert.True((await leads.IngestWebsiteLeadAsync("nexeagle", Web($"98000001{i:00}", null), ip)).Ok);
        Assert.Equal(429, (await leads.IngestWebsiteLeadAsync("nexeagle", Web("9800000199", null), ip)).Status);
    }

    // ── results sent to Google ───────────────────────────────────────────────

    private static (GoogleLead Lead, CmsSalesLead Crm) GoogleLeadAt(CmsDbContext db, GoogleAccount a, string stage, string? gclid = "gclid-1", decimal deal = 0)
    {
        var crm = new CmsSalesLead { LeadId = Guid.NewGuid(), HospitalName = "Example Hospital", Source = "WEBSITE", Stage = stage, DealValue = deal, CreatedAt = Now.AddDays(-3), UpdatedAt = Now.AddHours(-1) };
        var l = new GoogleLead { GoogleLeadId = Guid.NewGuid(), AccountId = a.AccountId, LeadId = crm.LeadId, Source = "web", Gclid = gclid, ReceivedAt = Now.AddDays(-3) };
        db.CmsSalesLeads.Add(crm);
        db.GoogleLeads.Add(l);
        db.SaveChanges();
        return (l, crm);
    }

    private static void Outcomes(CmsDbContext db, GoogleAccount a)
    {
        a.OutcomesJson = GoogleShared.Write(new List<GoogleOutcome>
        {
            new() { Stage = "Demo Done", ConversionActionId = "111", Value = 1000 },
            new() { Stage = "Closed Won", ConversionActionId = "222" },
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task A_lead_that_reaches_a_stage_is_sent_once_and_its_deal_value_with_Closed_Won()
    {
        using var db = NewDb();
        var a = Account(db);
        Outcomes(db, a);
        var (lead, crm) = GoogleLeadAt(db, a, "Demo Done");
        GoogleLeadAt(db, a, "Demo Done", gclid: null);   // not from an ad: nothing to send
        var g = new FakeGoogle();

        Assert.Equal(1, await Outcomes(db, g).SendDueAsync());
        Assert.Equal(0, await Outcomes(db, g).SendDueAsync());
        var batch = Assert.Single(g.Sent);
        Assert.Equal("111", batch.ConversionActionId);
        Assert.Equal("9990001111", batch.LoginCustomerId);
        var ev = Assert.Single(batch.Events);
        Assert.Equal("gclid-1", ev.Gclid);
        Assert.Equal(1000m, ev.Value);

        crm.Stage = "Closed Won";
        crm.DealValue = 120000;
        db.SaveChanges();
        Assert.Equal(1, await Outcomes(db, g).SendDueAsync());
        Assert.Equal("222", g.Sent[1].ConversionActionId);
        Assert.Equal(120000m, g.Sent[1].Events.Single().Value);
        Assert.Equal(2, db.GoogleConversionUploads.Count(u => u.GoogleLeadId == lead.GoogleLeadId && u.Status == "sent"));
    }

    [Fact]
    public async Task A_refusal_is_kept_tried_again_and_given_up_after_five_tries()
    {
        using var db = NewDb();
        var a = Account(db);
        Outcomes(db, a);
        GoogleLeadAt(db, a, "Demo Done");
        var g = new FakeGoogle { FailIngestWith = "The conversion action is not of type UPLOAD_CLICKS." };
        for (var i = 0; i < 7; i++) await Outcomes(db, g).SendDueAsync();
        var row = Assert.Single(db.GoogleConversionUploads);
        Assert.Equal("failed", row.Status);
        Assert.Equal(GoogleOutcomesService.MaxAttempts, row.Attempts);
        Assert.Contains("UPLOAD_CLICKS", db.GoogleAccounts.Single().LastOutcomeError);
    }

    [Fact]
    public async Task Clicks_older_than_90_days_are_not_sent()
    {
        using var db = NewDb();
        var a = Account(db);
        Outcomes(db, a);
        var (lead, _) = GoogleLeadAt(db, a, "Closed Won");
        lead.ReceivedAt = Now.AddDays(-120);
        db.SaveChanges();
        Assert.Equal(0, await Outcomes(db, new FakeGoogle()).SendDueAsync());
    }

    [Fact]
    public void Stages_count_as_reached_in_CRM_order()
    {
        Assert.True(GoogleShared.Reached("Closed Won", "Demo Done"));
        Assert.True(GoogleShared.Reached("Demo Done", "Demo Done"));
        Assert.False(GoogleShared.Reached("Contacted", "Demo Done"));
        Assert.False(GoogleShared.Reached("Closed Lost", "Demo Done"));
    }

    // ── sign-in and setup ────────────────────────────────────────────────────

    [Fact]
    public void The_sign_in_state_cannot_be_forged_or_reused_later()
    {
        var s = new GoogleSignInState(new byte[32]);
        var id = Guid.NewGuid();
        var state = s.Make(id, Asha, "https://cms.example.com/google/setup", Now.AddMinutes(15))!;
        Assert.Equal(id, s.Read(state, Now)!.Value.AccountId);
        Assert.Null(s.Read(state, Now.AddMinutes(16)));
        Assert.Null(s.Read(state[..^2] + "AA", Now));
        Assert.Null(new GoogleSignInState(Enumerable.Repeat((byte)7, 32).ToArray()).Read(state, Now));
    }

    [Fact]
    public async Task Signing_in_keeps_the_refresh_token_and_goes_back_to_the_CMS()
    {
        using var db = NewDb();
        var a = Account(db);
        a.RefreshTokenEnc = null;
        db.SaveChanges();
        var setup = Setup(db, new FakeGoogle());
        var url = await setup.SignInUrlAsync(Connector, a.AccountId, "https://cms.example.com/google/setup", "https://api.example.com/api/v1/google/oauth/callback");
        Assert.True(url.Ok, url.Message);
        var state = Uri.UnescapeDataString(url.Data!.Split("state=")[1]);
        var (ok, back, error) = await setup.CompleteSignInAsync("code-1", state, "https://api.example.com/api/v1/google/oauth/callback");
        Assert.True(ok, error);
        Assert.StartsWith("https://cms.example.com/google/setup?google=connected", back);
        Assert.Equal("refresh-1", Box.Open(db.GoogleAccounts.Single().RefreshTokenEnc));
        Assert.Equal(403, (await setup.SignInUrlAsync(Viewer, a.AccountId, "https://cms.example.com/x", "https://api.example.com/cb")).Status);
    }

    [Fact]
    public async Task Tag_ids_are_checked_and_the_website_reads_them()
    {
        using var db = NewDb();
        var a = Account(db);
        var setup = Setup(db, new FakeGoogle());
        Assert.Equal(400, (await setup.SaveTagAsync(Connector, a.AccountId, new GoogleTagSave { MeasurementId = "UA-1234" }, "", "")).Status);
        Assert.Equal(400, (await setup.SaveTagAsync(Connector, a.AccountId, new GoogleTagSave { MeasurementId = "G-ABC123", EventLabels = new() { ["generate_lead"] = "AbC" } }, "", "")).Status);
        Assert.Equal(400, (await setup.SaveTagAsync(Connector, a.AccountId, new GoogleTagSave { MeasurementId = "G-ABC123", AdsConversionId = "AW-123456", EventLabels = new() { ["appointment_booked"] = "AbC" } }, "", "")).Status);
        var ok = await setup.SaveTagAsync(Connector, a.AccountId, new GoogleTagSave { MeasurementId = "g-abc123", AdsConversionId = "AW-123456", EventLabels = new() { ["generate_lead"] = "AbC-d_1" } }, "", "");
        Assert.True(ok.Ok, ok.Message);
        var tag = await setup.SiteTagAsync("nexeagle");
        Assert.Equal("G-ABC123", tag!.MeasurementId);
        Assert.Equal("AbC-d_1", tag.EventLabels["generate_lead"]);
        Assert.Null(await setup.SiteTagAsync("other-site"));
    }

    [Fact]
    public async Task Chosen_accounts_take_their_names_and_manager_from_Google()
    {
        using var db = NewDb();
        var a = Account(db, ads: false);
        var setup = Setup(db, new FakeGoogle());
        var r = await setup.ChooseAsync(Connector, a.AccountId, new GoogleChoice { Ga4PropertyId = "properties/321", AdsCustomerId = "111-222-3333", SearchConsoleSite = "sc-domain:nexeagle.com" }, "", "");
        Assert.True(r.Ok, r.Message);
        var saved = db.GoogleAccounts.Single();
        Assert.Equal("NexEagle Ads", saved.AdsAccountName);
        Assert.Equal("9990001111", saved.AdsLoginCustomerId);
        Assert.Equal(400, (await setup.ChooseAsync(Connector, a.AccountId, new GoogleChoice { AdsCustomerId = "4445556666" }, "", "")).Status);
        Assert.Equal(409, (await setup.CreateAsync(Connector, new GoogleAccountCreate { Name = "Second", Site = "nexeagle" })).Status);
    }

    // ── numbers ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_overview_fills_empty_days_and_leaves_out_non_campaigns()
    {
        using var db = NewDb();
        var a = Account(db);
        var g = new FakeGoogle
        {
            Report = r => r.Dimensions.Count switch
            {
                0 when r.Metrics.Count == 4 => new GaReport { Rows = { new GaRow { Metrics = { 300, 420, 260, 9 } } } },
                0 => new GaReport { Rows = { new GaRow { Metrics = { 250 } } } },
                1 when r.Dimensions[0] == "date" => new GaReport { Rows = { new GaRow { Dimensions = { "20261125" }, Metrics = { 40, 2 } } } },
                1 when r.Dimensions[0] == "eventName" => new GaReport { Rows = { new GaRow { Dimensions = { "generate_lead" }, Metrics = { 9 } } } },
                1 => new GaReport { Rows = { new GaRow { Dimensions = { "Organic Search" }, Metrics = { 200, 5 } } } },
                _ => new GaReport { Rows = { new GaRow { Dimensions = { "(direct)", "(direct) / (none)" }, Metrics = { 90, 1 } }, new GaRow { Dimensions = { "1hms_demo", "google / cpc" }, Metrics = { 80, 3 } } } },
            },
        };
        var r = await Reports(db, g).OverviewAsync(new GoogleRange { AccountId = a.AccountId, From = new DateOnly(2026, 11, 21), To = new DateOnly(2026, 11, 27) });
        Assert.True(r.Ok, r.Message);
        Assert.Equal(300, r.Data!.Users);
        Assert.Equal(250, r.Data.PreviousUsers);
        Assert.Equal(7, r.Data.Days.Count);
        Assert.Equal(40, r.Data.Days.Single(d => d.Day == new DateOnly(2026, 11, 25)).Sessions);
        Assert.Equal("1hms_demo", Assert.Single(r.Data.Campaigns).Name);
        Assert.Equal(9, r.Data.Events.Single(e => e.Name == "generate_lead").Count);
    }

    [Fact]
    public async Task Ads_cost_per_lead_and_per_customer_come_from_the_CRM_and_Meta_sits_beside_it()
    {
        using var db = NewDb();
        var a = Account(db);
        GoogleLeadAt(db, a, "Closed Won");
        var (second, _) = GoogleLeadAt(db, a, "New");
        second.CampaignId = "555";
        db.SocialAccounts.Add(new SocialAccount { AccountId = Guid.NewGuid(), Name = "NexEagle", IsActive = true, CreatedAt = Now, UpdatedAt = Now });
        db.SaveChanges();
        var social = db.SocialAccounts.Single();
        db.SocialAdStats.Add(new SocialAdStat { AccountId = social.AccountId, Day = Now.Date.AddDays(-2), Level = "campaign", ObjectId = "m1", Name = "Meta demo", Spend = 900, Impressions = 1, Clicks = 1, Leads = 3 });
        db.SaveChanges();
        var g = new FakeGoogle
        {
            Ads = q => q.Contains("FROM customer WHERE") && q.Contains("segments.date,")
                ? new() { J(new { segments = new { date = "2026-11-26" }, metrics = new { costMicros = "1500000000", clicks = "40", impressions = "1000", conversions = 2.0 } }) }
                : q.StartsWith("SELECT campaign.id, campaign.name")
                    ? new() { J(new { campaign = new { id = "555", name = "1HMS demo", status = "ENABLED" }, metrics = new { costMicros = "1500000000", clicks = "40", impressions = "1000", conversions = 2.0 } }) }
                    : new(),
        };
        var r = await Reports(db, g).AdsAsync(new GoogleAdsQuery { AccountId = a.AccountId, From = new DateOnly(2026, 11, 1), To = new DateOnly(2026, 11, 27) });
        Assert.True(r.Ok, r.Message);
        Assert.Equal(1500m, r.Data!.Spend);
        Assert.Equal(2, r.Data.CrmLeads);
        Assert.Equal(750m, r.Data.CostPerLead);
        Assert.Equal(1, r.Data.Customers);
        Assert.Equal(1, r.Data.Rows.Single().CrmLeads);
        var meta = r.Data.Platforms.Single(p => p.Platform == "meta");
        Assert.Equal(900m, meta.Spend);
        Assert.Equal(1500m, r.Data.Platforms.Single(p => p.Platform == "google").CostPerCustomer);
    }
}
