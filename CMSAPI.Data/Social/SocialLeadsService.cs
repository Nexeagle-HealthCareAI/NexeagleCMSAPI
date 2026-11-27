using System.Text.RegularExpressions;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using CMSAPI.Data.WhatsApp;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.Social;

/// <summary>
/// Social → Leads. Every lead is a CRM lead (CmsSalesLeads); SocialLeads records where it came from: a lead
/// form (with ad, campaign and answers), a Messenger or Instagram conversation, or a comment. Lead-form leads
/// are made once per Meta lead id, and the daily check compares each form's last 30 days at Meta with the CRM.
/// </summary>
public class SocialLeadsService : ISocialLeadsService
{
    private readonly CmsDbContext _db;
    private readonly IMetaSocial _meta;
    private readonly SocialKeys _keys;
    private readonly IServiceProvider _services;
    private readonly ILogger<SocialLeadsService> _logger;
    private readonly Func<DateTime> _now;

    public SocialLeadsService(CmsDbContext db, IMetaSocial meta, SocialKeys keys, IServiceProvider services, ILogger<SocialLeadsService> logger)
        : this(db, meta, keys, services, logger, () => DateTime.UtcNow) { }

    internal SocialLeadsService(CmsDbContext db, IMetaSocial meta, SocialKeys keys, IServiceProvider services, ILogger<SocialLeadsService> logger, Func<DateTime> now)
    {
        _db = db;
        _meta = meta;
        _keys = keys;
        _services = services;
        _logger = logger;
        _now = now;
    }

    private const string NoReply = "Making leads needs \"Reply to comments and messages\".";

    private static string Digits(string? s) => Regex.Replace(s ?? string.Empty, "[^0-9]", "");
    private static string Last10(string? s) { var d = Digits(s); return d.Length > 10 ? d[^10..] : d; }

    private static string? Answer(MetaLeadItem lead, params string[] names) =>
        lead.Answers.FirstOrDefault(a => names.Contains(a.Key.ToLowerInvariant())).Value is { Length: > 0 } v ? v : null;

    /// <summary>A CRM lead, the same shape the existing Meta lead webhook makes.</summary>
    private async Task<CmsSalesLead> CrmLeadAsync(string contact, string? org, string? phone, string? email, string? city, string source,
        string? platform, string? medium, string? campaign, string? adId, int beds, string? notes, Guid? userId, bool score)
    {
        var now = _now();
        var lead = new CmsSalesLead
        {
            LeadId = Guid.NewGuid(), LeadNumber = SocialShared.LeadNumber(), ContactName = Trim(contact, 200), HospitalName = Trim(org ?? contact, 200),
            Mobile = Trim(phone, 30), Email = Trim(email, 200), City = Trim(city, 100), Source = source, Stage = "New", Priority = "Medium",
            UtmSource = platform, UtmMedium = medium, UtmCampaign = Trim(campaign, 200), UtmAdId = Trim(adId, 100), BedCount = beds, Notes = notes,
            CreatedByUserId = userId, CreatedAt = now, UpdatedAt = now,
        };
        if (score && _services.GetService<IGroqSalesAiService>() is { } ai)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var r = await ai.ScoreAndEnrichLeadAsync(lead.HospitalName, lead.FacilityType, beds, city ?? "Unknown", "Inbound Meta lead form", cts.Token);
                lead.AiIntentScore = r.IntentScore;
                lead.AiPersonaSummary = $"{r.BuyerPersona} | Hook: {r.RecommendedHook}";
            }
            catch (Exception ex) { _logger.LogInformation("Social lead scoring skipped: {Message}", ex.Message); }
        }
        _db.CmsSalesLeads.Add(lead);
        return lead;
    }

    private static string? Trim(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];

    private async Task<SocialLeadDto> DtoAsync(SocialLead l)
    {
        var crm = await _db.CmsSalesLeads.AsNoTracking().FirstOrDefaultAsync(x => x.LeadId == l.LeadId);
        var names = await WaShared.NamesAsync(_db, new[] { crm?.AssignedToUserId });
        return Dto(l, crm, names);
    }

    private static SocialLeadDto Dto(SocialLead l, CmsSalesLead? crm, IReadOnlyDictionary<Guid, string> names) => new()
    {
        SocialLeadId = l.SocialLeadId, LeadId = l.LeadId, Name = crm?.ContactName ?? crm?.HospitalName ?? "(deleted from the CRM)",
        Phone = crm?.Mobile, Email = crm?.Email, Source = l.Source, FormName = l.FormName, AdName = l.AdName, CampaignName = l.CampaignName,
        Platform = l.Platform, IsOrganic = l.IsOrganic, ReceivedAt = l.ReceivedAt, Stage = crm?.Stage, InCrm = crm != null && !crm.IsDeleted,
        Owner = crm?.AssignedToUserId is { } u ? names.GetValueOrDefault(u) : null,
        Answers = (SocialShared.Read<List<SocialLeadAnswer>>(l.AnswersJson) ?? new()),
    };

    // ── list ─────────────────────────────────────────────────────────────────

    public async Task<WorkResult<SocialLeadPage>> ListAsync(SocialLeadQuery q)
    {
        var query = _db.SocialLeads.AsNoTracking().Where(l => l.AccountId == q.AccountId);
        if (q.Source is "form" or "messenger" or "instagram" or "comment") query = query.Where(l => l.Source == q.Source);
        if (!string.IsNullOrWhiteSpace(q.FormId)) query = query.Where(l => l.FormId == q.FormId);
        var total = await query.CountAsync();
        var size = Math.Clamp(q.PageSize, 10, 200);
        var rows = await query.OrderByDescending(l => l.ReceivedAt).Skip((Math.Max(1, q.Page) - 1) * size).Take(size).ToListAsync();
        var leadIds = rows.Select(r => r.LeadId).ToList();
        var crm = await _db.CmsSalesLeads.AsNoTracking().Where(x => leadIds.Contains(x.LeadId)).ToListAsync();
        var names = await WaShared.NamesAsync(_db, crm.Select(c => c.AssignedToUserId));

        var since = _now().AddDays(-7);
        var recent = await _db.SocialLeads.AsNoTracking().Where(l => l.AccountId == q.AccountId && l.ReceivedAt >= since).Select(l => new { l.Source }).ToListAsync();
        var allIds = _db.SocialLeads.Where(l => l.AccountId == q.AccountId).Select(l => l.LeadId);
        var fresh = await _db.CmsSalesLeads.AsNoTracking().Where(x => allIds.Contains(x.LeadId) && !x.IsDeleted && x.Stage == "New").Select(x => x.CreatedAt).ToListAsync();
        var won = await _db.CmsSalesLeads.AsNoTracking().CountAsync(x => allIds.Contains(x.LeadId) && !x.IsDeleted && x.Stage == "Closed Won");
        var forms = await _db.SocialLeadForms.AsNoTracking().Where(f => f.AccountId == q.AccountId).OrderBy(f => f.Name).ToListAsync();

        return WorkResult<SocialLeadPage>.Success(new SocialLeadPage
        {
            Items = rows.Select(r => Dto(r, crm.FirstOrDefault(c => c.LeadId == r.LeadId), names)).ToList(),
            Total = total,
            Summary = new SocialLeadSummary
            {
                Last7Days = recent.Count, FromForms7 = recent.Count(r => r.Source == "form"), FromChats7 = recent.Count(r => r.Source != "form"),
                NotContacted = fresh.Count, OldestNotContactedAt = fresh.Count == 0 ? null : fresh.Min(), Customers = won,
            },
            Forms = forms.Select(FormDto).ToList(),
        });
    }

    private static SocialLeadFormDto FormDto(SocialLeadForm f) => new()
    {
        FormId = f.FormId, Name = f.Name, Status = f.Status, MetaCount = f.MetaCount, CrmCount = f.CrmCount, CheckedAt = f.CheckedAt,
        Missing = SocialShared.Read<List<string>>(f.MissingJson)?.Count ?? 0,
    };

    // ── lead forms ───────────────────────────────────────────────────────────

    public async Task<WorkResult<SocialLeadDto>> IngestFormLeadAsync(Guid accountId, string leadgenId, CancellationToken ct = default)
    {
        var existing = await _db.SocialLeads.FirstOrDefaultAsync(l => l.MetaLeadId == leadgenId, ct);
        if (existing != null) return WorkResult<SocialLeadDto>.Success(await DtoAsync(existing), "Already in the CRM.");
        var loaded = await _keys.LoadAsync(accountId);
        if (loaded is not { } k) return WorkResult<SocialLeadDto>.Fail(503, "This account is not connected (Social → Setup).");

        MetaLeadItem lead;
        try { lead = await _meta.LeadAsync(k.Creds, leadgenId, ct); }
        catch (MetaSocialException ex) { return WorkResult<SocialLeadDto>.Fail(502, ex.Message); }

        var name = Answer(lead, "full_name", "name", "contact_name") ?? string.Join(' ', new[] { Answer(lead, "first_name"), Answer(lead, "last_name") }.Where(x => x != null));
        if (string.IsNullOrWhiteSpace(name)) name = "Lead form contact";
        var phone = Answer(lead, "phone_number", "phone", "mobile");
        var org = Answer(lead, "company_name", "hospital_name", "clinic_name");
        var formName = lead.FormId == null ? null : await _db.SocialLeadForms.Where(f => f.FormId == lead.FormId).Select(f => f.Name).FirstOrDefaultAsync(ct);
        int.TryParse(Answer(lead, "bed_count", "beds"), out var beds);

        // The older CRM webhook made leads without Meta's lead id: link to one of those instead of a duplicate.
        CmsSalesLead? crm = null;
        var last10 = Last10(phone);
        if (last10.Length == 10)
        {
            var from = lead.CreatedAt.AddDays(-1);
            var to = lead.CreatedAt.AddDays(1);
            var linked = _db.SocialLeads.Select(s => s.LeadId);
            var candidates = await _db.CmsSalesLeads.Where(x => x.Source == "META_ADS" && x.CreatedAt >= from && x.CreatedAt <= to && !linked.Contains(x.LeadId) && x.Mobile != null).ToListAsync(ct);
            crm = candidates.FirstOrDefault(x => Last10(x.Mobile) == last10);
        }
        crm ??= await CrmLeadAsync(name, org, phone, Answer(lead, "email"), Answer(lead, "city"), "META_ADS",
            lead.Platform == "ig" ? "instagram" : "facebook", "lead_form", lead.CampaignName, lead.AdId, beds,
            formName != null ? $"Lead form: {formName}" : null, null, score: true);

        var l = new SocialLead
        {
            SocialLeadId = Guid.NewGuid(), AccountId = accountId, LeadId = crm.LeadId, Source = "form", MetaLeadId = lead.Id, FormId = lead.FormId, FormName = formName,
            AdId = lead.AdId, AdName = lead.AdName, CampaignId = lead.CampaignId, CampaignName = lead.CampaignName, Platform = lead.Platform, IsOrganic = lead.IsOrganic,
            AnswersJson = SocialShared.Write(lead.Answers.Select(a => new SocialLeadAnswer { Name = a.Key, Value = a.Value }).ToList()),
            ReceivedAt = lead.CreatedAt,
        };
        _db.SocialLeads.Add(l);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // The same lead arrived twice at once: the other one won.
            _db.ChangeTracker.Clear();
            var other = await _db.SocialLeads.FirstAsync(x => x.MetaLeadId == leadgenId, ct);
            return WorkResult<SocialLeadDto>.Success(await DtoAsync(other), "Already in the CRM.");
        }
        return WorkResult<SocialLeadDto>.Success(await DtoAsync(l));
    }

    public async Task<WorkResult<List<SocialLeadFormDto>>> CheckFormsAsync(Guid accountId, CancellationToken ct = default)
    {
        var loaded = await _keys.LoadAsync(accountId);
        if (loaded is not { } k || k.Account.PageId == null) return WorkResult<List<SocialLeadFormDto>>.Fail(503, "This account is not connected (Social → Setup).");
        var now = _now();
        try
        {
            var forms = await _meta.LeadFormsAsync(k.Creds, ct);
            foreach (var f in forms)
            {
                var row = await _db.SocialLeadForms.FirstOrDefaultAsync(x => x.FormId == f.Id, ct);
                if (row == null) { row = new SocialLeadForm { FormId = f.Id, AccountId = accountId }; _db.SocialLeadForms.Add(row); }
                row.Name = SocialShared.Preview(f.Name, 200);
                row.Status = f.Status;
                if (f.Status is "ARCHIVED" or "DELETED") continue;
                var leads = await _meta.FormLeadsAsync(k.Creds, f.Id, now.AddDays(-30), ct);
                var ids = leads.Select(x => x.Id).ToList();
                var known = await _db.SocialLeads.Where(x => x.MetaLeadId != null && ids.Contains(x.MetaLeadId)).Select(x => x.MetaLeadId!).ToListAsync(ct);
                var missing = ids.Except(known).ToList();
                row.MetaCount = ids.Count;
                row.CrmCount = known.Count;
                row.MissingJson = SocialShared.Write(missing);
                row.CheckedAt = now;
                // Name the form on its leads (the webhook may have come before the form was known).
                foreach (var l in await _db.SocialLeads.Where(x => x.FormId == f.Id && x.FormName == null).ToListAsync(ct)) l.FormName = row.Name;
            }
            var acc = await _db.SocialAccounts.FirstAsync(a => a.AccountId == accountId, ct);
            acc.LeadsCheckedAt = now;
            await _db.SaveChangesAsync(ct);
        }
        catch (MetaSocialException ex) { return WorkResult<List<SocialLeadFormDto>>.Fail(502, ex.Message); }
        var all = await _db.SocialLeadForms.AsNoTracking().Where(f => f.AccountId == accountId).OrderBy(f => f.Name).ToListAsync(ct);
        return WorkResult<List<SocialLeadFormDto>>.Success(all.Select(FormDto).ToList());
    }

    public async Task<WorkResult<int>> ImportMissingAsync(SocialActor actor, Guid accountId, string formId, CancellationToken ct = default)
    {
        if (!actor.CanReply) return WorkResult<int>.Fail(403, NoReply);
        var form = await _db.SocialLeadForms.FirstOrDefaultAsync(f => f.FormId == formId && f.AccountId == accountId, ct);
        if (form == null) return WorkResult<int>.Fail(404, "This form does not exist.");
        var missing = SocialShared.Read<List<string>>(form.MissingJson) ?? new();
        var added = 0;
        var failed = new List<string>();
        foreach (var id in missing)
        {
            var r = await IngestFormLeadAsync(accountId, id, ct);
            if (r.Ok) added++; else failed.Add(r.Message ?? id);
        }
        form = await _db.SocialLeadForms.FirstAsync(f => f.FormId == formId, ct);
        form.MissingJson = SocialShared.Write(failed.Count == 0 ? new List<string>() : missing.Where(m => !_db.SocialLeads.Any(l => l.MetaLeadId == m)).ToList());
        form.CrmCount = (form.CrmCount ?? 0) + added;
        await _db.SaveChangesAsync(ct);
        return failed.Count == 0
            ? WorkResult<int>.Success(added, $"{added} lead(s) brought into the CRM.")
            : WorkResult<int>.Fail(502, $"{added} brought in; {failed.Count} could not be read from Meta: {failed[0]}");
    }

    // ── leads from conversations and comments ────────────────────────────────

    private static WorkResult<SocialLeadDto>? Check(SocialActor actor, SocialMakeLead r)
    {
        if (!actor.CanReply) return WorkResult<SocialLeadDto>.Fail(403, NoReply);
        if (string.IsNullOrWhiteSpace(r.Name)) return WorkResult<SocialLeadDto>.Fail(400, "Give the lead a name.");
        if (!string.IsNullOrWhiteSpace(r.Phone) && Digits(r.Phone).Length is < 8 or > 15) return WorkResult<SocialLeadDto>.Fail(400, "That phone number does not look right.");
        return null;
    }

    private static string? Notes(SocialMakeLead r, string from) =>
        string.Join("\n", new[] { from, string.IsNullOrWhiteSpace(r.InterestedIn) ? null : "Interested in: " + r.InterestedIn.Trim(), r.Notes?.Trim() }.Where(x => !string.IsNullOrEmpty(x)));

    public async Task<WorkResult<SocialLeadDto>> FromConversationAsync(SocialActor actor, Guid contactId, SocialMakeLead r)
    {
        if (Check(actor, r) is { } bad) return bad;
        var c = await _db.SocialContacts.FirstOrDefaultAsync(x => x.ContactId == contactId);
        if (c == null) return WorkResult<SocialLeadDto>.Fail(404, "This conversation does not exist.");
        if (c.LeadId != null && await _db.CmsSalesLeads.AnyAsync(x => x.LeadId == c.LeadId && !x.IsDeleted))
            return WorkResult<SocialLeadDto>.Fail(409, "This person is already a lead.");
        var channel = SocialShared.Channel(c.Channel);
        var crm = await CrmLeadAsync(r.Name, null, r.Phone, r.Email, r.City, c.Channel == "instagram" ? "INSTAGRAM_DM" : "MESSENGER",
            c.Channel == "instagram" ? "instagram" : "facebook", "dm", null, null, 0,
            Notes(r, $"From a {channel} conversation{(c.Username != null ? " with @" + c.Username : "")}."), actor.UserId, score: false);
        crm.AssignedToUserId = c.AssignedUserId ?? actor.UserId;
        var l = new SocialLead { SocialLeadId = Guid.NewGuid(), AccountId = c.AccountId, LeadId = crm.LeadId, Source = c.Channel, ContactId = c.ContactId, ReceivedAt = _now(), CreatedByUserId = actor.UserId };
        _db.SocialLeads.Add(l);
        c.LeadId = crm.LeadId;
        await _db.SaveChangesAsync();
        return WorkResult<SocialLeadDto>.Success(await DtoAsync(l), "Lead made in the CRM.");
    }

    public async Task<WorkResult<SocialLeadDto>> FromCommentAsync(SocialActor actor, Guid commentId, SocialMakeLead r)
    {
        if (Check(actor, r) is { } bad) return bad;
        var c = await _db.SocialComments.FirstOrDefaultAsync(x => x.CommentId == commentId);
        if (c == null) return WorkResult<SocialLeadDto>.Fail(404, "This comment does not exist.");
        if (await _db.SocialLeads.AnyAsync(x => x.CommentId == commentId)) return WorkResult<SocialLeadDto>.Fail(409, "A lead was already made from this comment.");
        var crm = await CrmLeadAsync(r.Name, null, r.Phone, r.Email, r.City, c.Channel == "instagram" ? "INSTAGRAM_COMMENT" : "FACEBOOK_COMMENT",
            c.Channel, "comment", null, null, 0, Notes(r, $"From a {SocialShared.Channel(c.Channel)} comment: \"{SocialShared.Preview(c.Text, 300)}\""), actor.UserId, score: false);
        crm.AssignedToUserId = c.AssignedUserId ?? actor.UserId;
        var l = new SocialLead { SocialLeadId = Guid.NewGuid(), AccountId = c.AccountId, LeadId = crm.LeadId, Source = "comment", CommentId = c.CommentId, ReceivedAt = _now(), CreatedByUserId = actor.UserId };
        _db.SocialLeads.Add(l);
        await _db.SaveChangesAsync();
        return WorkResult<SocialLeadDto>.Success(await DtoAsync(l), "Lead made in the CRM.");
    }
}
