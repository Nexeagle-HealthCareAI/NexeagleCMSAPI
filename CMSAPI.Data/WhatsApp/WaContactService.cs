using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.WhatsApp;

/// <summary>
/// See IWaContactService. Changing contacts needs whatsapp.contacts; importing Doctor Dekho patients also
/// needs insights.contacts. Marking someone opted in always records how they agreed and who said so.
/// An opted-out person is never opted back in by an import: only by changing them one at a time.
/// </summary>
public class WaContactService : IWaContactService
{
    private const int MaxImport = 20000;
    private readonly CmsDbContext _db;
    private readonly IWhatsAppBotStore _bot;

    public WaContactService(CmsDbContext db, IWhatsAppBotStore bot)
    {
        _db = db;
        _bot = bot;
    }

    private static WorkResult<T> NoChange<T>() => WorkResult<T>.Fail(403, "You can see contacts but not change them. Ask an administrator for \"Add, import and change WhatsApp contacts\".");

    public async Task<WorkResult<WaContactPage>> ListAsync(WaActor actor, WaContactQuery query)
    {
        var q = _db.WaContacts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            var digits = new string(s.Where(char.IsDigit).ToArray());
            q = q.Where(c => (c.Name != null && c.Name.Contains(s)) || (c.ProfileName != null && c.ProfileName.Contains(s)) || (digits.Length >= 3 && c.Phone.Contains(digits)));
        }
        if (!string.IsNullOrWhiteSpace(query.OptIn)) q = q.Where(c => c.OptIn == query.OptIn);
        if (!string.IsNullOrWhiteSpace(query.Source)) q = q.Where(c => c.Source == query.Source);
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = query.Tag.Trim().ToLowerInvariant();
            q = q.Where(c => c.Tags != null && ("," + c.Tags + ",").Contains("," + tag + ","));
        }
        var total = await q.CountAsync();
        var size = Math.Clamp(query.PageSize, 1, 200);
        var page = Math.Max(1, query.Page);
        var items = await q.OrderBy(c => c.Name ?? c.ProfileName ?? c.Phone).Skip((page - 1) * size).Take(size).ToListAsync();
        var names = await WaShared.NamesAsync(_db, items.Select(c => c.AssignedUserId).Concat(items.Select(c => c.OptInByUserId)));
        var allTags = (await _db.WaContacts.AsNoTracking().Where(c => c.Tags != null).Select(c => c.Tags).Distinct().Take(2000).ToListAsync())
            .SelectMany(WaShared.Tags).Distinct().OrderBy(t => t).ToList();
        var now = DateTime.UtcNow;
        return WorkResult<WaContactPage>.Success(new WaContactPage { Items = items.Select(c => WaShared.Contact(c, names, now)).ToList(), Total = total, AllTags = allTags });
    }

    public async Task<WorkResult<WaContactDto>> GetAsync(WaActor actor, Guid contactId)
    {
        var c = await _db.WaContacts.AsNoTracking().FirstOrDefaultAsync(x => x.ContactId == contactId);
        if (c == null) return WorkResult<WaContactDto>.Fail(404, "This contact does not exist.");
        var names = await WaShared.NamesAsync(_db, new[] { c.AssignedUserId, c.OptInByUserId });
        return WorkResult<WaContactDto>.Success(WaShared.Contact(c, names, DateTime.UtcNow));
    }

    public async Task<WorkResult<WaContactDto>> CreateAsync(WaActor actor, WaSaveContactRequest request)
    {
        if (!actor.CanContacts) return NoChange<WaContactDto>();
        var phone = WaRules.NormalisePhone(request.Phone);
        if (phone == null) return WorkResult<WaContactDto>.Fail(400, "Enter a WhatsApp number with its country code, like +91 98765 43210.");
        if (await _db.WaContacts.AnyAsync(c => c.Phone == phone)) return WorkResult<WaContactDto>.Fail(409, "This number is already a contact.");
        var now = DateTime.UtcNow;
        var contact = new WaContact
        {
            ContactId = Guid.NewGuid(), Phone = phone, Source = "Manual", OptIn = "Unknown", ChatStatus = "Open",
            CreatedByUserId = actor.UserId, CreatedAt = now, UpdatedAt = now,
        };
        var problem = Apply(contact, request, actor, now);
        if (problem != null) return WorkResult<WaContactDto>.Fail(400, problem);
        _db.WaContacts.Add(contact);
        await _db.SaveChangesAsync();
        return await GetAsync(actor, contact.ContactId);
    }

    public async Task<WorkResult<WaContactDto>> UpdateAsync(WaActor actor, Guid contactId, WaSaveContactRequest request)
    {
        if (!actor.CanContacts) return NoChange<WaContactDto>();
        var contact = await _db.WaContacts.FirstOrDefaultAsync(c => c.ContactId == contactId);
        if (contact == null) return WorkResult<WaContactDto>.Fail(404, "This contact does not exist.");
        var problem = Apply(contact, request, actor, DateTime.UtcNow);
        if (problem != null) return WorkResult<WaContactDto>.Fail(400, problem);
        await _db.SaveChangesAsync();
        return await GetAsync(actor, contactId);
    }

    /// <summary>Copies a change onto a contact; the number itself never changes (it is the chat).</summary>
    private static string? Apply(WaContact c, WaSaveContactRequest r, WaActor actor, DateTime now)
    {
        if (r.Name != null) c.Name = r.Name.Trim().Length == 0 ? null : r.Name.Trim()[..Math.Min(r.Name.Trim().Length, 150)];
        if (r.Tags != null) c.Tags = WaShared.JoinTags(r.Tags);
        if (r.Attributes != null) c.Attributes = WaShared.AttributesJson(r.Attributes);
        if (r.OptIn != null && r.OptIn != c.OptIn)
        {
            switch (r.OptIn)
            {
                case "OptedIn":
                    if (string.IsNullOrWhiteSpace(r.OptInSource)) return "Say how they agreed to WhatsApp messages (for example \"Signed up on the website\").";
                    c.OptIn = "OptedIn";
                    c.OptInSource = r.OptInSource.Trim()[..Math.Min(r.OptInSource.Trim().Length, 200)];
                    c.OptInByUserId = actor.UserId;
                    c.OptInAt = now;
                    c.OptOutAt = null;
                    c.OptOutReason = null;
                    break;
                case "OptedOut":
                    c.OptIn = "OptedOut";
                    c.OptOutAt = now;
                    c.OptOutReason = $"Marked by {actor.Name}";
                    break;
                case "Unknown":
                    c.OptIn = "Unknown";
                    break;
                default:
                    return "Opt-in is OptedIn, OptedOut or Unknown.";
            }
        }
        c.UpdatedAt = now;
        return null;
    }

    public async Task<WorkResult<WaImportResult>> ImportAsync(WaActor actor, WaImportRequest request)
    {
        if (!actor.CanContacts) return NoChange<WaImportResult>();
        if (request.Rows.Count == 0) return WorkResult<WaImportResult>.Fail(400, "The list is empty.");
        if (request.Rows.Count > MaxImport) return WorkResult<WaImportResult>.Fail(400, $"Import at most {MaxImport:N0} people at a time.");
        if (request.AllOptedIn && string.IsNullOrWhiteSpace(request.OptInSource))
            return WorkResult<WaImportResult>.Fail(400, "Say how the people in this list agreed to WhatsApp messages.");
        var rows = request.Rows.Select((r, i) => (Row: i + 2, Phone: WaRules.NormalisePhone(r.Phone), r.Name, r.Tags, r.Attributes, r.OptedIn, Source: "Import", LeadId: (Guid?)null));
        return WorkResult<WaImportResult>.Success(await UpsertAsync(actor, rows, request.AllOptedIn, request.OptInSource, request.Tags, "row"));
    }

    public async Task<WorkResult<WaImportResult>> ImportLeadsAsync(WaActor actor, WaImportLeadsRequest request)
    {
        if (!actor.CanContacts) return NoChange<WaImportResult>();
        if (request.AllOptedIn && string.IsNullOrWhiteSpace(request.OptInSource))
            return WorkResult<WaImportResult>.Fail(400, "Say how these leads agreed to WhatsApp messages.");
        var q = _db.CmsSalesLeads.AsNoTracking().Where(l => l.Mobile != null && l.Mobile != "");
        if (request.LeadIds is { Count: > 0 } ids) q = q.Where(l => ids.Contains(l.LeadId));
        if (request.Stages is { Count: > 0 } stages) q = q.Where(l => stages.Contains(l.Stage));
        var leads = await q.Take(MaxImport).ToListAsync();
        if (leads.Count == 0) return WorkResult<WaImportResult>.Fail(400, "No lead with a mobile number matches.");
        var rows = leads.Select((l, i) => (Row: i + 1, Phone: WaRules.NormalisePhone(l.Mobile), Name: (string?)(l.ContactName ?? l.HospitalName),
            Tags: (List<string>?)new List<string> { "lead", l.Stage.ToLowerInvariant() },
            Attributes: (Dictionary<string, string>?)new Dictionary<string, string?> { ["hospital"] = l.HospitalName, ["city"] = l.City, ["state"] = l.State }
                .Where(a => !string.IsNullOrWhiteSpace(a.Value)).ToDictionary(a => a.Key, a => a.Value!),
            OptedIn: (bool?)null, Source: "Lead", LeadId: (Guid?)l.LeadId));
        return WorkResult<WaImportResult>.Success(await UpsertAsync(actor, rows, request.AllOptedIn, request.OptInSource, request.Tags, "lead"));
    }

    public async Task<WorkResult<WaImportResult>> ImportDoctorDekhoAsync(WaActor actor, WaImportDoctorDekhoRequest request)
    {
        if (!actor.CanContacts) return NoChange<WaImportResult>();
        if (!actor.CanPatientContacts)
            return WorkResult<WaImportResult>.Fail(403, "Importing patients' numbers also needs \"See phone numbers and chats of WhatsApp bot users\".");
        if (request.To < request.From) return WorkResult<WaImportResult>.Fail(400, "The end date is before the start date.");
        if (request.AllOptedIn && string.IsNullOrWhiteSpace(request.OptInSource))
            return WorkResult<WaImportResult>.Fail(400, "Say how these people agreed to marketing messages.");
        List<BotSessionRow> sessions;
        try
        {
            sessions = await _bot.SessionsAsync(request.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), request.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        }
        catch (WhatsAppBotUnavailableException)
        {
            return WorkResult<WaImportResult>.Fail(503, "The Doctor Dekho bot's records cannot be reached right now.");
        }
        var people = sessions.GroupBy(s => s.Phone).Select(g => g.OrderByDescending(s => s.LastActivityAt).First()).Take(MaxImport).ToList();
        if (people.Count == 0) return WorkResult<WaImportResult>.Fail(400, "Nobody wrote to the Doctor Dekho bot in those dates.");
        var rows = people.Select((p, i) => (Row: i + 1, Phone: WaRules.NormalisePhone(p.Phone), Name: (string?)null,
            Tags: (List<string>?)new List<string> { "doctor dekho", p.BookingId != null ? "booked" : "searched" },
            Attributes: (Dictionary<string, string>?)new Dictionary<string, string> { ["last_chat"] = p.LastActivityAt.ToString("yyyy-MM-dd") },
            OptedIn: (bool?)null, Source: "DoctorDekho", LeadId: (Guid?)null));
        return WorkResult<WaImportResult>.Success(await UpsertAsync(actor, rows, request.AllOptedIn, request.OptInSource, request.Tags, "person"));
    }

    /// <summary>
    /// Adds new people and adds tags/details to known ones. Opt-in: a row's own "no" wins; "all agreed"
    /// opts in people not opted out; nobody opted out is ever opted back in here.
    /// </summary>
    private async Task<WaImportResult> UpsertAsync(
        WaActor actor,
        IEnumerable<(int Row, string? Phone, string? Name, List<string>? Tags, Dictionary<string, string>? Attributes, bool? OptedIn, string Source, Guid? LeadId)> rows,
        bool allOptedIn, string? optInSource, List<string>? extraTags, string what)
    {
        var result = new WaImportResult();
        var list = rows.ToList();
        var phones = list.Where(r => r.Phone != null).Select(r => r.Phone!).Distinct().ToList();
        var known = new Dictionary<string, WaContact>();
        foreach (var chunk in phones.Chunk(1000))
            foreach (var c in await _db.WaContacts.Where(c => chunk.Contains(c.Phone)).ToListAsync())
                known[c.Phone] = c;

        var now = DateTime.UtcNow;
        var seen = new HashSet<string>();
        var source = optInSource?.Trim();
        if (source?.Length > 200) source = source[..200];
        foreach (var r in list)
        {
            if (r.Phone == null)
            {
                result.Skipped++;
                if (result.Problems.Count < 50) result.Problems.Add($"{Cap(what)} {r.Row}: not a WhatsApp number.");
                continue;
            }
            if (!seen.Add(r.Phone)) { result.Skipped++; continue; }

            var isNew = !known.TryGetValue(r.Phone, out var c);
            if (isNew)
            {
                c = new WaContact
                {
                    ContactId = Guid.NewGuid(), Phone = r.Phone, Source = r.Source, LeadId = r.LeadId, OptIn = "Unknown", ChatStatus = "Open",
                    CreatedByUserId = actor.UserId, CreatedAt = now, UpdatedAt = now,
                };
                _db.WaContacts.Add(c);
                known[r.Phone] = c;
            }
            if (!string.IsNullOrWhiteSpace(r.Name) && string.IsNullOrWhiteSpace(c!.Name)) c.Name = r.Name.Trim()[..Math.Min(r.Name.Trim().Length, 150)];
            c!.LeadId ??= r.LeadId;
            c.Tags = WaShared.JoinTags(WaShared.Tags(c.Tags).Concat(r.Tags ?? new()).Concat(extraTags ?? new()));
            if (r.Attributes is { Count: > 0 })
            {
                var attrs = WaShared.Attributes(c.Attributes);
                foreach (var (k, v) in r.Attributes) attrs[k] = v;
                c.Attributes = WaShared.AttributesJson(attrs);
            }

            var agreed = r.OptedIn ?? (allOptedIn ? true : null);
            if (agreed == false && c.OptIn == "OptedIn") { c.OptIn = "Unknown"; }
            else if (agreed == true && c.OptIn == "Unknown")
            {
                c.OptIn = "OptedIn";
                c.OptInSource = source ?? "Imported list";
                c.OptInByUserId = actor.UserId;
                c.OptInAt = now;
            }
            c.UpdatedAt = now;
            if (isNew) result.Added++; else result.Updated++;
        }
        await _db.SaveChangesAsync();
        return result;
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
