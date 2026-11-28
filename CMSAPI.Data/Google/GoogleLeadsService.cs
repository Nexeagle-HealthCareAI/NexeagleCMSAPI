using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Data.WhatsApp;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.Google;

/// <summary>
/// Google → Leads. Every lead is a CRM lead (CmsSalesLeads); GoogleLeads records where it came from:
///   form  Google Ads lead form (webhook; checked with the account's key, stored once per Google lead id)
///   web   the website's contact form, with the Google click ID and campaign tags from the visitor's link
/// </summary>
public class GoogleLeadsService : IGoogleLeadsService
{
    private static readonly ConcurrentDictionary<string, Queue<DateTime>> Recent = new();

    private readonly CmsDbContext _db;
    private readonly IGoogleApi _google;
    private readonly GoogleKeys _keys;
    private readonly ILogger<GoogleLeadsService> _logger;
    private readonly Func<DateTime> _now;

    public GoogleLeadsService(CmsDbContext db, IGoogleApi google, GoogleKeys keys, ILogger<GoogleLeadsService> logger)
        : this(db, google, keys, logger, () => DateTime.UtcNow) { }

    internal GoogleLeadsService(CmsDbContext db, IGoogleApi google, GoogleKeys keys, ILogger<GoogleLeadsService> logger, Func<DateTime> now)
    {
        _db = db;
        _google = google;
        _keys = keys;
        _logger = logger;
        _now = now;
    }

    private static string? T(string? s, int max) => GoogleShared.Trim(s, max);

    private CmsSalesLead CrmLead(string contact, string? org, string? phone, string? email, string? city, string source,
        string? utmSource, string? utmMedium, string? campaign, string? notes)
    {
        var now = _now();
        var lead = new CmsSalesLead
        {
            LeadId = Guid.NewGuid(), LeadNumber = $"LEAD-{now:MMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            ContactName = T(contact, 200), HospitalName = T(org, 200) ?? T(contact, 200) ?? "Unknown", Mobile = T(phone, 30), Email = T(email, 200),
            City = T(city, 100), Source = source, Stage = "New", Priority = "Medium", UtmSource = T(utmSource, 100), UtmMedium = T(utmMedium, 100),
            UtmCampaign = T(campaign, 200), Notes = notes, CreatedAt = now, UpdatedAt = now,
        };
        _db.CmsSalesLeads.Add(lead);
        return lead;
    }

    // ── Google lead form webhook ─────────────────────────────────────────────

    private static readonly Dictionary<string, string> Columns = new()
    {
        ["FULL_NAME"] = "Name", ["FIRST_NAME"] = "First name", ["LAST_NAME"] = "Last name", ["EMAIL"] = "Email", ["PHONE_NUMBER"] = "Phone",
        ["CITY"] = "City", ["REGION"] = "State", ["COMPANY_NAME"] = "Organisation", ["JOB_TITLE"] = "Job title", ["POSTAL_CODE"] = "PIN code",
        ["WORK_EMAIL"] = "Work email", ["WORK_PHONE"] = "Work phone",
    };

    public async Task<WorkResult<Guid?>> IngestFormLeadAsync(JsonElement p, CancellationToken ct = default)
    {
        var key = GoogleApi.Str(p, "google_key");
        if (string.IsNullOrEmpty(key)) return WorkResult<Guid?>.Fail(401, "No key.");
        var accounts = await _db.GoogleAccounts.Where(a => a.IsActive && a.LeadKeyEnc != null).ToListAsync(ct);
        var account = accounts.FirstOrDefault(a => _keys.LeadKey(a) is { } k
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(k), Encoding.UTF8.GetBytes(key)));
        if (account == null) return WorkResult<Guid?>.Fail(401, "Unknown key.");

        account.LastLeadAt = _now();
        if (GoogleApi.Str(p, "is_test") == "true")
        {
            // "Send test data" in Google Ads: proves the address and key work; nothing goes into the CRM.
            await _db.SaveChangesAsync(ct);
            return WorkResult<Guid?>.Success(null, "Test lead received.");
        }
        var leadId = GoogleApi.Str(p, "lead_id");
        if (string.IsNullOrEmpty(leadId)) return WorkResult<Guid?>.Fail(400, "No lead_id.");
        var existing = await _db.GoogleLeads.AsNoTracking().FirstOrDefaultAsync(l => l.ExternalLeadId == leadId, ct);
        if (existing != null) { await _db.SaveChangesAsync(ct); return WorkResult<Guid?>.Success(existing.GoogleLeadId, "Already stored."); }

        var answers = new List<GoogleLeadAnswer>();
        var values = new Dictionary<string, string>();
        foreach (var col in GoogleApi.Arr(p, "user_column_data"))
        {
            var id = GoogleApi.Str(col, "column_id") ?? string.Empty;
            var value = GoogleApi.Str(col, "string_value") ?? string.Empty;
            if (value.Length == 0) continue;
            var name = Columns.GetValueOrDefault(id) ?? GoogleApi.Str(col, "column_name") ?? id;
            values.TryAdd(id, value);
            answers.Add(new GoogleLeadAnswer { Name = T(name, 100)!, Value = T(value, 500)! });
        }
        var person = values.GetValueOrDefault("FULL_NAME")
                     ?? string.Join(' ', new[] { values.GetValueOrDefault("FIRST_NAME"), values.GetValueOrDefault("LAST_NAME") }.Where(x => !string.IsNullOrEmpty(x)));
        if (string.IsNullOrWhiteSpace(person)) person = values.GetValueOrDefault("EMAIL") ?? values.GetValueOrDefault("PHONE_NUMBER") ?? "Google lead";
        var campaignId = GoogleApi.Str(p, "campaign_id");
        var campaignName = await CampaignNameAsync(account, campaignId, ct);

        var crm = CrmLead(person, values.GetValueOrDefault("COMPANY_NAME"), values.GetValueOrDefault("PHONE_NUMBER") ?? values.GetValueOrDefault("WORK_PHONE"),
            values.GetValueOrDefault("EMAIL") ?? values.GetValueOrDefault("WORK_EMAIL"), values.GetValueOrDefault("CITY"), "GOOGLE_ADS", "google", "cpc", campaignName ?? campaignId,
            "Google Ads lead form" + (answers.Count > 0 ? ":\n" + string.Join('\n', answers.Select(a => $"{a.Name}: {a.Value}")) : ""));
        var lead = new GoogleLead
        {
            GoogleLeadId = Guid.NewGuid(), AccountId = account.AccountId, LeadId = crm.LeadId, Source = "form", ExternalLeadId = T(leadId, 100),
            FormId = T(GoogleApi.Str(p, "form_id"), 40), CampaignId = T(campaignId, 40), CampaignName = T(campaignName, 300),
            AdGroupId = T(GoogleApi.Str(p, "adgroup_id"), 40), Gclid = T(GoogleApi.Str(p, "gcl_id"), 300),
            UtmSource = "google", UtmMedium = "cpc", AnswersJson = GoogleShared.Write(answers), ReceivedAt = _now(),
        };
        _db.GoogleLeads.Add(lead);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // The same lead delivered twice at once: the unique index kept one; anything else is a real failure.
            if (await _db.GoogleLeads.AsNoTracking().AnyAsync(l => l.ExternalLeadId == leadId, ct)) return WorkResult<Guid?>.Success(null, "Already stored.");
            throw;
        }
        return WorkResult<Guid?>.Success(lead.GoogleLeadId, "Lead stored.");
    }

    private async Task<string?> CampaignNameAsync(GoogleAccount a, string? campaignId, CancellationToken ct)
    {
        var id = GoogleShared.Digits(campaignId);
        if (id.Length == 0 || string.IsNullOrEmpty(a.AdsCustomerId) || _keys.Client(a) is not { } c) return null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            var rows = await _google.AdsSearchAsync(c, a.AdsCustomerId, a.AdsLoginCustomerId, $"SELECT campaign.name FROM campaign WHERE campaign.id = {id}", cts.Token);
            return rows.Select(r => GoogleApi.Str(GoogleApi.Obj(r, "campaign"), "name")).FirstOrDefault(n => n != null);
        }
        catch (Exception ex) when (ex is GoogleApiException or OperationCanceledException)
        {
            _logger.LogInformation("Google lead: campaign {Id} name not read ({Message}).", id, ex.Message);
            return null;
        }
    }

    // ── Website contact form ─────────────────────────────────────────────────

    /// <summary>At most 5 forms per visitor address per 10 minutes, and 300 per website per day.</summary>
    private bool Allowed(string bucket, int max, TimeSpan window)
    {
        var now = _now();
        var q = Recent.GetOrAdd(bucket, _ => new Queue<DateTime>());
        lock (q)
        {
            while (q.Count > 0 && now - q.Peek() > window) q.Dequeue();
            if (q.Count >= max) return false;
            q.Enqueue(now);
            return true;
        }
    }

    public async Task<WorkResult<Guid?>> IngestWebsiteLeadAsync(string site, GoogleWebsiteLead f, string? clientIp, CancellationToken ct = default)
    {
        var key = (site ?? string.Empty).Trim().ToLowerInvariant();
        var account = await _db.GoogleAccounts.FirstOrDefaultAsync(a => a.Site == key && a.IsActive, ct);
        if (account == null) return WorkResult<Guid?>.Fail(404, "This website is not connected in CMS → Google.");
        if (!string.IsNullOrEmpty(f.Website)) return WorkResult<Guid?>.Success(null, "Thank you.");   // honeypot: quietly dropped
        var name = T(f.Name, 200);
        var phone = T(f.Phone, 30);
        var email = T(f.Email, 200);
        if (name == null) return WorkResult<Guid?>.Fail(400, "Give your name.");
        if (phone == null && email == null) return WorkResult<Guid?>.Fail(400, "Give a phone number or an email address.");
        if (email != null && (!email.Contains('@') || email.Contains(' '))) return WorkResult<Guid?>.Fail(400, "That email address does not look right.");
        if (phone != null && GoogleShared.Digits(phone).Length is < 7 or > 15) return WorkResult<Guid?>.Fail(400, "That phone number does not look right.");
        if (!Allowed($"ip:{key}:{clientIp}", 5, TimeSpan.FromMinutes(10)) || !Allowed($"site:{key}", 300, TimeSpan.FromDays(1)))
            return WorkResult<Guid?>.Fail(429, "Too many messages at once. Please try again in a few minutes.");

        // The same person sending the form again within a day: one lead.
        var since = _now().AddDays(-1);
        var digits = GoogleShared.Digits(phone);
        var last10 = digits.Length > 10 ? digits[^10..] : digits;
        var recentIds = _db.GoogleLeads.Where(l => l.AccountId == account.AccountId && l.Source == "web" && l.ReceivedAt >= since).Select(l => l.LeadId);
        var recent = await _db.CmsSalesLeads.AsNoTracking().Where(x => recentIds.Contains(x.LeadId)).Select(x => new { x.Mobile, x.Email }).ToListAsync(ct);
        if (recent.Any(x => (email != null && string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase))
                            || (last10.Length >= 7 && GoogleShared.Digits(x.Mobile).EndsWith(last10, StringComparison.Ordinal))))
            return WorkResult<Guid?>.Success(null, "Thank you.");

        var fromAd = !string.IsNullOrEmpty(f.Gclid) || !string.IsNullOrEmpty(f.Gbraid) || !string.IsNullOrEmpty(f.Wbraid);
        var lead = new GoogleLead
        {
            GoogleLeadId = Guid.NewGuid(), AccountId = account.AccountId, Source = "web", FormName = T(f.Page, 200),
            Gclid = T(f.Gclid, 300), Gbraid = T(f.Gbraid, 300), Wbraid = T(f.Wbraid, 300),
            UtmSource = T(f.UtmSource, 100) ?? (fromAd ? "google" : null), UtmMedium = T(f.UtmMedium, 100) ?? (fromAd ? "cpc" : null),
            UtmCampaign = T(f.UtmCampaign, 200), CampaignName = T(f.UtmCampaign, 300), Keyword = T(f.UtmTerm, 300), LandingPage = T(f.LandingPage, 500),
            ReceivedAt = _now(),
        };
        if (fromAd && !string.IsNullOrEmpty(lead.Gclid)) await ClickAsync(account, lead, ct);

        var answers = new List<GoogleLeadAnswer>();
        if (T(f.Organisation, 200) is { } org) answers.Add(new() { Name = "Organisation", Value = org });
        if (T(f.City, 100) is { } city) answers.Add(new() { Name = "City", Value = city });
        if (T(f.Message, 2000) is { } msg) answers.Add(new() { Name = "Message", Value = msg });
        lead.AnswersJson = answers.Count == 0 ? null : GoogleShared.Write(answers);

        var crm = CrmLead(name, f.Organisation, phone, email, f.City, "WEBSITE", lead.UtmSource, lead.UtmMedium, lead.CampaignName,
            $"Website form ({GoogleShared.Sites.GetValueOrDefault(key, key)}{(lead.FormName != null ? ", " + lead.FormName : "")})" + (T(f.Message, 2000) is { } m ? ":\n" + m : ""));
        lead.LeadId = crm.LeadId;
        _db.GoogleLeads.Add(lead);
        await _db.SaveChangesAsync(ct);
        return WorkResult<Guid?>.Success(lead.GoogleLeadId, "Thank you.");
    }

    /// <summary>Which campaign, ad group and keyword the click came from (Google Ads click_view; one day per query).</summary>
    private async Task ClickAsync(GoogleAccount a, GoogleLead lead, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(a.AdsCustomerId) || _keys.Client(a) is not { } c) return;
        var gclid = lead.Gclid!.Replace("'", "");
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            for (var back = 0; back < 3; back++)   // the click was usually today, sometimes a day or two before the form
            {
                var day = DateOnly.FromDateTime(_now()).AddDays(-back).ToString("yyyy-MM-dd");
                var rows = await _google.AdsSearchAsync(c, a.AdsCustomerId, a.AdsLoginCustomerId,
                    $"SELECT campaign.id, campaign.name, ad_group.id, click_view.keyword_info.text FROM click_view WHERE click_view.gclid = '{gclid}' AND segments.date = '{day}'", cts.Token);
                var r = rows.FirstOrDefault();
                if (r.ValueKind != JsonValueKind.Object) continue;
                var cmp = GoogleApi.Obj(r, "campaign");
                lead.CampaignId = T(GoogleApi.Str(cmp, "id"), 40);
                lead.CampaignName = T(GoogleApi.Str(cmp, "name"), 300) ?? lead.CampaignName;
                lead.AdGroupId = T(GoogleApi.Str(GoogleApi.Obj(r, "adGroup"), "id"), 40);
                lead.Keyword = T(GoogleApi.Str(GoogleApi.Obj(GoogleApi.Obj(r, "clickView"), "keywordInfo"), "text"), 300) ?? lead.Keyword;
                return;
            }
        }
        catch (Exception ex) when (ex is GoogleApiException or OperationCanceledException)
        {
            _logger.LogInformation("Website lead: click not looked up ({Message}).", ex.Message);
        }
    }

    // ── list ─────────────────────────────────────────────────────────────────

    public async Task<WorkResult<GoogleLeadPage>> ListAsync(GoogleLeadQuery q)
    {
        var query = _db.GoogleLeads.AsNoTracking().Where(l => l.AccountId == q.AccountId);
        if (q.Source is "web" or "form") query = query.Where(l => l.Source == q.Source);
        if (!string.IsNullOrWhiteSpace(q.Campaign)) query = query.Where(l => l.CampaignName == q.Campaign);
        var total = await query.CountAsync();
        var size = Math.Clamp(q.PageSize, 10, 200);
        var rows = await query.OrderByDescending(l => l.ReceivedAt).Skip((Math.Max(1, q.Page) - 1) * size).Take(size).ToListAsync();
        var leadIds = rows.Select(r => r.LeadId).ToList();
        var crm = await _db.CmsSalesLeads.AsNoTracking().Where(x => leadIds.Contains(x.LeadId)).ToListAsync();
        var names = await WaShared.NamesAsync(_db, crm.Select(x => x.AssignedToUserId));
        var rowIds = rows.Select(r => r.GoogleLeadId).ToList();
        var uploads = await _db.GoogleConversionUploads.AsNoTracking().Where(u => rowIds.Contains(u.GoogleLeadId)).ToListAsync();

        var now = _now();
        var since7 = now.AddDays(-7);
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var all = _db.GoogleLeads.AsNoTracking().Where(l => l.AccountId == q.AccountId);
        var recent = await all.Where(l => l.ReceivedAt >= since7).Select(l => l.Source).ToListAsync();
        var allIds = all.Select(l => l.LeadId);
        var fresh = await _db.CmsSalesLeads.AsNoTracking().Where(x => allIds.Contains(x.LeadId) && !x.IsDeleted && x.Stage == "New").Select(x => x.CreatedAt).ToListAsync();
        var monthIds = all.Where(l => l.ReceivedAt >= monthStart).Select(l => l.LeadId);
        var monthStages = await _db.CmsSalesLeads.AsNoTracking().Where(x => monthIds.Contains(x.LeadId) && !x.IsDeleted).Select(x => x.Stage).ToListAsync();
        var sent = await _db.GoogleConversionUploads.AsNoTracking().CountAsync(u => u.AccountId == q.AccountId && u.Status == "sent" && u.SentAt >= monthStart);
        var campaigns = await all.Where(l => l.CampaignName != null).Select(l => l.CampaignName!).Distinct().OrderBy(n => n).Take(100).ToListAsync();

        return WorkResult<GoogleLeadPage>.Success(new GoogleLeadPage
        {
            Total = total,
            Campaigns = campaigns,
            Summary = new GoogleLeadSummary
            {
                Last7Days = recent.Count, Web7 = recent.Count(s => s == "web"), Forms7 = recent.Count(s => s == "form"),
                NotContacted = fresh.Count, OldestNotContactedAt = fresh.Count == 0 ? null : fresh.Min(),
                LeadsThisMonth = monthStages.Count, ReachedDemoThisMonth = monthStages.Count(s => GoogleShared.DemoStages.Contains(s)), SentThisMonth = sent,
            },
            Items = rows.Select(r =>
            {
                var c = crm.FirstOrDefault(x => x.LeadId == r.LeadId);
                var mine = uploads.Where(u => u.GoogleLeadId == r.GoogleLeadId).ToList();
                var last = mine.Where(u => u.Status == "sent").OrderByDescending(u => Array.IndexOf(GoogleShared.Stages, u.Stage)).FirstOrDefault();
                var failed = mine.Where(u => u.Status == "failed").OrderByDescending(u => u.SentAt).FirstOrDefault();
                return new GoogleLeadDto
                {
                    GoogleLeadId = r.GoogleLeadId, LeadId = r.LeadId, Name = c?.ContactName ?? c?.HospitalName ?? "(deleted from the CRM)",
                    Phone = c?.Mobile, Email = c?.Email, Source = r.Source, FormName = r.FormName, CampaignName = r.CampaignName, Keyword = r.Keyword,
                    FromGoogleAd = r.Source == "form" || r.Gclid != null || r.Gbraid != null || r.Wbraid != null, UtmSource = r.UtmSource,
                    Answers = GoogleShared.Read<List<GoogleLeadAnswer>>(r.AnswersJson) ?? new(), ReceivedAt = r.ReceivedAt,
                    Stage = c?.Stage, InCrm = c != null && !c.IsDeleted, Owner = c?.AssignedToUserId is { } u ? names.GetValueOrDefault(u) : null,
                    SentStage = last?.Stage, SentAt = last?.SentAt, SendError = failed?.Error,
                };
            }).ToList(),
        });
    }
}
