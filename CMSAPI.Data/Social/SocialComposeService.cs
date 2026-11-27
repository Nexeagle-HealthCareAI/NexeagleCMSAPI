using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Data.WhatsApp;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.Social;

/// <summary>
/// Social → Compose and Calendar. A post goes Draft → Waiting for approval → Scheduled → Published (or
/// Failed, with Meta's reason). Approval records who approved and a hash of exactly what was approved; a change
/// after that needs approval again. The worker publishes at the time: Facebook directly, Instagram through a
/// media container that Meta fetches from a short-lived signed link and prepares before it can be published.
/// </summary>
public class SocialComposeService : ISocialComposeService
{
    private readonly CmsDbContext _db;
    private readonly IMetaSocial _meta;
    private readonly SocialKeys _keys;
    private readonly IDocFileStore _files;
    private readonly SocialMediaLinks _links;
    private readonly ILogger<SocialComposeService> _logger;
    private readonly Func<DateTime> _now;

    public SocialComposeService(CmsDbContext db, IMetaSocial meta, SocialKeys keys, IDocFileStore files, SocialMediaLinks links, ILogger<SocialComposeService> logger)
        : this(db, meta, keys, files, links, logger, () => DateTime.UtcNow) { }

    internal SocialComposeService(CmsDbContext db, IMetaSocial meta, SocialKeys keys, IDocFileStore files, SocialMediaLinks links, ILogger<SocialComposeService> logger, Func<DateTime> now)
    {
        _db = db;
        _meta = meta;
        _keys = keys;
        _files = files;
        _links = links;
        _logger = logger;
        _now = now;
    }

    public static readonly TimeSpan India = TimeSpan.FromMinutes(330);
    public const int MaxCarousel = 10;
    public const int MaxInstagramCaption = 2200;
    public const int MaxHashtags = 30;
    /// <summary>The two checks an approver must see ticked (healthcare advertising rules).</summary>
    public static readonly string[] RequiredChecks = { "noTestimonials", "factsBackedUp" };

    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["video/mp4"] = ".mp4", ["video/quicktime"] = ".mov",
    };

    // ── media ────────────────────────────────────────────────────────────────

    public async Task<WorkResult<SocialMediaRef>> UploadAsync(SocialActor actor, Stream content, string fileName, string mime, long size)
    {
        if (!actor.CanCompose) return WorkResult<SocialMediaRef>.Fail(403, "Writing posts needs \"Write posts and send them for approval\".");
        if (!_files.IsConfigured) return WorkResult<SocialMediaRef>.Fail(503, "File storage is not set up on this server.");
        if (!Extensions.TryGetValue(mime, out var ext)) return WorkResult<SocialMediaRef>.Fail(400, "Pictures (JPG, PNG) and videos (MP4, MOV) can be added.");
        var image = mime.StartsWith("image/");
        if (image && size > 8 * 1024 * 1024) return WorkResult<SocialMediaRef>.Fail(400, "Pictures can be at most 8 MB (Instagram's limit).");
        if (!image && size > 300L * 1024 * 1024) return WorkResult<SocialMediaRef>.Fail(400, "Videos can be at most 300 MB.");
        var key = $"social/media/{_now():yyyy/MM}/{Guid.NewGuid():N}{ext}";
        try { await _files.SaveAsync(key, content, mime); }
        catch (DocStorageException ex) { return WorkResult<SocialMediaRef>.Fail(503, ex.Message); }
        var name = Path.GetFileName(fileName ?? "file");
        return WorkResult<SocialMediaRef>.Success(new SocialMediaRef { Key = key, Mime = mime, Name = name.Length > 120 ? name[..120] : name, Size = size });
    }

    public async Task<(Stream Content, string Mime)?> MediaAsync(string key)
    {
        if (!key.StartsWith("social/", StringComparison.Ordinal) || key.Contains("..") || !_files.IsConfigured) return null;
        var mime = Extensions.FirstOrDefault(e => key.EndsWith(e.Value, StringComparison.OrdinalIgnoreCase)).Key ?? "application/octet-stream";
        try { return (await _files.OpenAsync(key), mime); }
        catch (DocStorageException) { return null; }
    }

    // ── mapping and rules ────────────────────────────────────────────────────

    private static Dictionary<string, bool> Checklist(string? json) => SocialShared.Read<Dictionary<string, bool>>(json) ?? new();
    private static List<SocialMediaRef> Media(string? json) => SocialShared.Read<List<SocialMediaRef>>(json) ?? new();

    private SocialDraftDto Dto(SocialDraft d, IReadOnlyDictionary<Guid, string> names) => new()
    {
        DraftId = d.DraftId, AccountId = d.AccountId, Title = d.Title, ToFacebook = d.ToFacebook, ToInstagram = d.ToInstagram, Kind = d.Kind,
        Caption = d.Caption, FacebookCaption = d.FacebookCaption, InstagramCaption = d.InstagramCaption, Link = d.Link, Media = Media(d.MediaJson),
        WhenMode = d.WhenMode, ScheduledAt = d.ScheduledAt, Status = d.Status, Checklist = Checklist(d.ChecklistJson),
        ApproverUserId = d.ApproverUserId, ApproverName = d.ApproverUserId is { } a ? names.GetValueOrDefault(a) : null,
        ApprovedBy = d.ApprovedByUserId is { } b ? names.GetValueOrDefault(b) : null, ApprovedAt = d.ApprovedAt, SentBackNote = d.SentBackNote,
        FacebookPostId = d.FacebookPostId, InstagramMediaId = d.InstagramMediaId, Error = d.Error, PublishedAt = d.PublishedAt,
        CreatedByUserId = d.CreatedByUserId, CreatedBy = d.CreatedByUserId is { } c ? names.GetValueOrDefault(c) : null, CreatedAt = d.CreatedAt, UpdatedAt = d.UpdatedAt,
    };

    private async Task<SocialDraftDto> DtoAsync(SocialDraft d) =>
        Dto(d, await WaShared.NamesAsync(_db, new[] { d.ApproverUserId, d.ApprovedByUserId, d.CreatedByUserId }));

    /// <summary>What was approved: captions, link, kind, channels and media, in one hash.</summary>
    public static string Hash(SocialDraft d)
    {
        var text = string.Join("\u001f", d.ToFacebook, d.ToInstagram, d.Kind, d.Caption, d.FacebookCaption, d.InstagramCaption, d.Link,
            string.Join(",", Media(d.MediaJson).Select(m => m.Key)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    public static string? FacebookText(SocialDraft d) => string.IsNullOrWhiteSpace(d.FacebookCaption) ? d.Caption : d.FacebookCaption;
    public static string? InstagramText(SocialDraft d) => string.IsNullOrWhiteSpace(d.InstagramCaption) ? d.Caption : d.InstagramCaption;

    /// <summary>What is wrong with a post, or null. The same rules as the Compose screen.</summary>
    public static string? Check(SocialDraftSave r, DateTime now)
    {
        if (!r.ToFacebook && !r.ToInstagram) return "Choose Facebook, Instagram or both.";
        var kinds = new[] { "Photo", "Carousel", "Video", "Reel", "Story", "Text", "Link" };
        if (!kinds.Contains(r.Kind)) return "Choose the kind of post.";
        if (r.ToInstagram && r.Kind is "Text" or "Link") return "Instagram posts need a picture or a video.";
        if (r.ToFacebook && r.Kind == "Story") return "Stories are posted to Instagram only here.";
        var images = r.Media.Count(m => m.Mime.StartsWith("image/"));
        var videos = r.Media.Count(m => m.Mime.StartsWith("video/"));
        switch (r.Kind)
        {
            case "Photo" when r.Media.Count != 1 || images != 1: return "A photo post has one picture.";
            case "Carousel" when images < 2 || videos > 0: return "A carousel has 2 to 10 pictures.";
            case "Carousel" when images > MaxCarousel: return $"A carousel has at most {MaxCarousel} pictures.";
            case "Video" or "Reel" when r.Media.Count != 1 || videos != 1: return "Add one video.";
            case "Story" when r.Media.Count != 1: return "A story has one picture or one video.";
            case "Text" or "Link" when r.Media.Count > 0: return "Text and link posts have no pictures; choose Photo instead.";
        }
        if (r.ToInstagram && r.Media.Any(m => m.Mime == "image/png")) return "Instagram takes JPEG pictures only.";
        var ig = string.IsNullOrWhiteSpace(r.InstagramCaption) ? r.Caption : r.InstagramCaption;
        var fb = string.IsNullOrWhiteSpace(r.FacebookCaption) ? r.Caption : r.FacebookCaption;
        if (r.ToInstagram && (ig?.Length ?? 0) > MaxInstagramCaption) return $"Instagram captions can be at most {MaxInstagramCaption:N0} characters.";
        if (r.ToInstagram && Regex.Matches(ig ?? "", @"#\w").Count > MaxHashtags) return $"Instagram allows at most {MaxHashtags} hashtags.";
        if (r.ToFacebook && (fb?.Length ?? 0) > 60000) return "The Facebook text is too long.";
        if (r.Kind is "Text" && string.IsNullOrWhiteSpace(fb)) return "Write the post.";
        if (r.Kind == "Link" && string.IsNullOrWhiteSpace(r.Link)) return "Add the link.";
        if (!string.IsNullOrWhiteSpace(r.Link)
            && (!Uri.TryCreate(r.Link.Trim(), UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)))
            return "The link must start with https://.";
        if (r.WhenMode is not ("approved" or "schedule" or "queue")) return "Choose when to post.";
        if (r.WhenMode == "schedule")
        {
            if (r.ScheduledAt == null) return "Choose the date and time.";
            if (r.ScheduledAt < now.AddMinutes(10)) return "Schedule at least 10 minutes ahead.";
            if (r.ScheduledAt > now.AddDays(180)) return "Schedule at most six months ahead.";
        }
        if ((r.Title ?? "").Length > 150) return "The title can be at most 150 characters.";
        return null;
    }

    private static bool Editable(string status) => status is "Draft" or "SentBack" or "AwaitingApproval" or "Scheduled" or "Failed";

    // ── drafts ───────────────────────────────────────────────────────────────

    public async Task<WorkResult<List<SocialDraftDto>>> ListAsync(Guid accountId, string? status)
    {
        var q = _db.SocialDrafts.AsNoTracking().Where(d => d.AccountId == accountId);
        q = status switch
        {
            null or "" => q.Where(d => d.Status != "Cancelled"),
            "open" => q.Where(d => d.Status == "Draft" || d.Status == "SentBack" || d.Status == "AwaitingApproval" || d.Status == "Scheduled" || d.Status == "Failed"),
            _ => q.Where(d => d.Status == status),
        };
        var list = await q.OrderByDescending(d => d.UpdatedAt).Take(300).ToListAsync();
        var names = await WaShared.NamesAsync(_db, list.SelectMany(d => new[] { d.ApproverUserId, d.ApprovedByUserId, d.CreatedByUserId }));
        return WorkResult<List<SocialDraftDto>>.Success(list.Select(d => Dto(d, names)).ToList());
    }

    public async Task<WorkResult<SocialDraftDto>> GetAsync(Guid draftId)
    {
        var d = await _db.SocialDrafts.AsNoTracking().FirstOrDefaultAsync(x => x.DraftId == draftId);
        return d == null ? WorkResult<SocialDraftDto>.Fail(404, "This post does not exist.") : WorkResult<SocialDraftDto>.Success(await DtoAsync(d));
    }

    public async Task<WorkResult<SocialDraftDto>> SaveAsync(SocialActor actor, Guid? draftId, SocialDraftSave r)
    {
        if (!actor.CanCompose) return WorkResult<SocialDraftDto>.Fail(403, "Writing posts needs \"Write posts and send them for approval\".");
        r.Media ??= new();
        if (r.Media.Count > 20) return WorkResult<SocialDraftDto>.Fail(400, "Too many files.");
        if (r.Media.Any(m => !m.Key.StartsWith("social/media/", StringComparison.Ordinal) || m.Key.Contains("..")))
            return WorkResult<SocialDraftDto>.Fail(400, "A file is not one uploaded here.");
        if (r.ApproverUserId != null && !await _db.CmsUsers.AnyAsync(u => u.UserId == r.ApproverUserId))
            return WorkResult<SocialDraftDto>.Fail(400, "That approver does not exist.");

        SocialDraft d;
        var now = _now();
        if (draftId == null)
        {
            if (!await _db.SocialAccounts.AnyAsync(a => a.AccountId == r.AccountId && a.IsActive)) return WorkResult<SocialDraftDto>.Fail(400, "Choose the account.");
            d = new SocialDraft { DraftId = Guid.NewGuid(), AccountId = r.AccountId, Status = "Draft", CreatedByUserId = actor.UserId, CreatedAt = now };
            _db.SocialDrafts.Add(d);
        }
        else
        {
            var found = await _db.SocialDrafts.FirstOrDefaultAsync(x => x.DraftId == draftId);
            if (found == null) return WorkResult<SocialDraftDto>.Fail(404, "This post does not exist.");
            d = found;
            if (!Editable(d.Status)) return WorkResult<SocialDraftDto>.Fail(409, d.Status == "Published" ? "This post is published; change it on Facebook or Instagram." : "This post is being published.");
        }

        d.Title = string.IsNullOrWhiteSpace(r.Title) ? SocialShared.Preview(r.Caption ?? r.FacebookCaption ?? r.InstagramCaption ?? "Untitled post", 60) : r.Title.Trim();
        if (d.Title.Length > 150) d.Title = d.Title[..150];
        d.ToFacebook = r.ToFacebook;
        d.ToInstagram = r.ToInstagram;
        d.Kind = r.Kind;
        d.Caption = r.Caption;
        d.FacebookCaption = string.IsNullOrWhiteSpace(r.FacebookCaption) ? null : r.FacebookCaption;
        d.InstagramCaption = string.IsNullOrWhiteSpace(r.InstagramCaption) ? null : r.InstagramCaption;
        d.Link = string.IsNullOrWhiteSpace(r.Link) ? null : r.Link.Trim();
        d.MediaJson = SocialShared.Write(r.Media);
        d.WhenMode = r.WhenMode is "schedule" or "queue" ? r.WhenMode : "approved";
        d.ScheduledAt = d.WhenMode == "schedule" ? r.ScheduledAt : null;
        d.ChecklistJson = SocialShared.Write(r.Checklist ?? new());
        d.ApproverUserId = r.ApproverUserId;
        d.UpdatedAt = now;

        // A change to what was approved goes back for approval.
        if (d.ApprovedHash != null && d.ApprovedHash != Hash(d))
        {
            d.ApprovedHash = null;
            d.ApprovedByUserId = null;
            d.ApprovedAt = null;
            if (d.Status is "Scheduled") d.Status = "AwaitingApproval";
        }
        if (d.Status == "Failed") { d.Status = "Draft"; d.Error = null; d.Attempts = 0; }
        await _db.SaveChangesAsync();
        return WorkResult<SocialDraftDto>.Success(await DtoAsync(d), "Saved.");
    }

    private SocialDraftSave Request(SocialDraft d) => new()
    {
        AccountId = d.AccountId, Title = d.Title, ToFacebook = d.ToFacebook, ToInstagram = d.ToInstagram, Kind = d.Kind, Caption = d.Caption,
        FacebookCaption = d.FacebookCaption, InstagramCaption = d.InstagramCaption, Link = d.Link, Media = Media(d.MediaJson),
        WhenMode = d.WhenMode, ScheduledAt = d.ScheduledAt, Checklist = Checklist(d.ChecklistJson), ApproverUserId = d.ApproverUserId,
    };

    public async Task<WorkResult<SocialDraftDto>> SubmitAsync(SocialActor actor, Guid draftId)
    {
        if (!actor.CanCompose) return WorkResult<SocialDraftDto>.Fail(403, "Writing posts needs \"Write posts and send them for approval\".");
        var d = await _db.SocialDrafts.FirstOrDefaultAsync(x => x.DraftId == draftId);
        if (d == null) return WorkResult<SocialDraftDto>.Fail(404, "This post does not exist.");
        if (d.Status is not ("Draft" or "SentBack")) return WorkResult<SocialDraftDto>.Fail(409, "Only a draft can be sent for approval.");
        if (Check(Request(d), _now()) is { } problem) return WorkResult<SocialDraftDto>.Fail(400, problem);
        d.Status = "AwaitingApproval";
        d.SentBackNote = null;
        d.UpdatedAt = _now();
        await _db.SaveChangesAsync();
        return WorkResult<SocialDraftDto>.Success(await DtoAsync(d), "Sent for approval.");
    }

    public async Task<WorkResult<SocialDraftDto>> ApproveAsync(SocialActor actor, Guid draftId)
    {
        if (!actor.CanPublish) return WorkResult<SocialDraftDto>.Fail(403, "Approving needs \"Approve and publish posts\".");
        var d = await _db.SocialDrafts.FirstOrDefaultAsync(x => x.DraftId == draftId);
        if (d == null) return WorkResult<SocialDraftDto>.Fail(404, "This post does not exist.");
        if (d.Status is not ("AwaitingApproval" or "Draft" or "SentBack")) return WorkResult<SocialDraftDto>.Fail(409, "This post is not waiting for approval.");
        var now = _now();
        if (Check(Request(d), now) is { } problem) return WorkResult<SocialDraftDto>.Fail(400, problem);
        var checks = Checklist(d.ChecklistJson);
        if (RequiredChecks.Any(k => !checks.GetValueOrDefault(k))) return WorkResult<SocialDraftDto>.Fail(400, "Tick the checks under \"Before it goes out\" first.");

        if (d.WhenMode == "queue")
        {
            var slot = await NextSlotAsync(d, now);
            if (slot == null) return WorkResult<SocialDraftDto>.Fail(409, "No free queue slot in the next two weeks for these channels. Add slots in Calendar, or schedule a time.");
            d.ScheduledAt = slot;
        }
        else if (d.WhenMode == "approved" || d.ScheduledAt == null || d.ScheduledAt < now) d.ScheduledAt = now;

        d.Status = "Scheduled";
        d.ApprovedByUserId = actor.UserId;
        d.ApprovedAt = now;
        d.ApprovedHash = Hash(d);
        d.SentBackNote = null;
        d.UpdatedAt = now;
        await _db.SaveChangesAsync();
        var when = d.ScheduledAt <= now.AddMinutes(1) ? "It goes out within a minute." : $"It goes out {(d.ScheduledAt!.Value + India):ddd d MMM, HH:mm} (India time).";
        return WorkResult<SocialDraftDto>.Success(await DtoAsync(d), "Approved. " + when);
    }

    public async Task<WorkResult<SocialDraftDto>> SendBackAsync(SocialActor actor, Guid draftId, string note)
    {
        if (!actor.CanPublish) return WorkResult<SocialDraftDto>.Fail(403, "Sending back needs \"Approve and publish posts\".");
        note = (note ?? string.Empty).Trim();
        if (note.Length is 0 or > 1000) return WorkResult<SocialDraftDto>.Fail(400, "Say what to change (up to 1,000 characters).");
        var d = await _db.SocialDrafts.FirstOrDefaultAsync(x => x.DraftId == draftId);
        if (d == null) return WorkResult<SocialDraftDto>.Fail(404, "This post does not exist.");
        if (d.Status is not ("AwaitingApproval" or "Scheduled")) return WorkResult<SocialDraftDto>.Fail(409, "Only a post waiting for approval or scheduled can be sent back.");
        d.Status = "SentBack";
        d.SentBackNote = note;
        d.ApprovedHash = null;
        d.ApprovedByUserId = null;
        d.ApprovedAt = null;
        d.UpdatedAt = _now();
        await _db.SaveChangesAsync();
        return WorkResult<SocialDraftDto>.Success(await DtoAsync(d), "Sent back with your note.");
    }

    public async Task<WorkResult<SocialDraftDto>> CancelAsync(SocialActor actor, Guid draftId)
    {
        var d = await _db.SocialDrafts.FirstOrDefaultAsync(x => x.DraftId == draftId);
        if (d == null) return WorkResult<SocialDraftDto>.Fail(404, "This post does not exist.");
        if (!actor.CanPublish && !(actor.CanCompose && d.CreatedByUserId == actor.UserId)) return WorkResult<SocialDraftDto>.Fail(403, "Only its writer or someone who approves posts can cancel it.");
        if (d.Status is "Published" or "Publishing") return WorkResult<SocialDraftDto>.Fail(409, "It is already going out; delete it on Facebook or Instagram.");
        d.Status = "Cancelled";
        d.UpdatedAt = _now();
        await _db.SaveChangesAsync();
        return WorkResult<SocialDraftDto>.Success(await DtoAsync(d), "Cancelled.");
    }

    public async Task<WorkResult<SocialDraftDto>> RetryAsync(SocialActor actor, Guid draftId)
    {
        if (!actor.CanPublish) return WorkResult<SocialDraftDto>.Fail(403, "Publishing needs \"Approve and publish posts\".");
        var d = await _db.SocialDrafts.FirstOrDefaultAsync(x => x.DraftId == draftId);
        if (d == null) return WorkResult<SocialDraftDto>.Fail(404, "This post does not exist.");
        if (d.Status != "Failed") return WorkResult<SocialDraftDto>.Fail(409, "Only a post that failed can be tried again.");
        if (d.ApprovedHash != Hash(d)) return WorkResult<SocialDraftDto>.Fail(409, "It changed since it was approved: send it for approval again.");
        d.Status = "Scheduled";
        d.ScheduledAt = _now();
        d.Error = null;
        d.Attempts = 0;
        d.PublishStateJson = null;
        d.UpdatedAt = _now();
        await _db.SaveChangesAsync();
        return WorkResult<SocialDraftDto>.Success(await DtoAsync(d), "Trying again within a minute.");
    }

    // ── calendar and queue ───────────────────────────────────────────────────

    public async Task<WorkResult<List<SocialCalendarItem>>> CalendarAsync(Guid accountId, DateTime from, DateTime to)
    {
        if (to <= from || (to - from).TotalDays > 62) return WorkResult<List<SocialCalendarItem>>.Fail(400, "Choose up to two months.");
        var drafts = await _db.SocialDrafts.AsNoTracking()
            .Where(d => d.AccountId == accountId && d.Status != "Cancelled" && d.Status != "Published" && d.ScheduledAt != null && d.ScheduledAt >= from && d.ScheduledAt < to)
            .ToListAsync();
        var posts = await _db.SocialPosts.AsNoTracking().Where(p => p.AccountId == accountId && p.PublishedAt >= from && p.PublishedAt < to).ToListAsync();
        var items = drafts.Select(d => new SocialCalendarItem
        {
            DraftId = d.DraftId, Title = d.Title, At = d.ScheduledAt!.Value, Status = d.Status, Error = d.Error,
            Channels = new[] { d.ToFacebook ? "facebook" : null, d.ToInstagram ? "instagram" : null }.Where(c => c != null).Select(c => c!).ToList(),
        }).ToList();
        items.AddRange(posts.Select(p => new SocialCalendarItem
        {
            PostId = p.PostId, DraftId = p.DraftId, Title = SocialShared.Preview(p.Caption ?? p.Type, 60), At = p.PublishedAt, Status = "Published", Channels = new() { p.Channel },
        }));
        return WorkResult<List<SocialCalendarItem>>.Success(items.OrderBy(i => i.At).ToList());
    }

    private static SocialSlotDto SlotDto(SocialQueueSlot s) => new()
    {
        SlotId = s.SlotId, DayOfWeek = s.DayOfWeek, Time = $"{s.TimeOfDay.Hours:00}:{s.TimeOfDay.Minutes:00}", ToFacebook = s.ToFacebook, ToInstagram = s.ToInstagram,
    };

    public async Task<List<SocialSlotDto>> SlotsAsync(Guid accountId) =>
        (await _db.SocialQueueSlots.AsNoTracking().Where(s => s.AccountId == accountId).ToListAsync())
        .OrderBy(s => (s.DayOfWeek + 6) % 7).ThenBy(s => s.TimeOfDay).Select(SlotDto).ToList();

    public async Task<WorkResult<List<SocialSlotDto>>> SaveSlotsAsync(SocialActor actor, Guid accountId, List<SocialSlotDto> slots)
    {
        if (!actor.CanPublish) return WorkResult<List<SocialSlotDto>>.Fail(403, "Changing the queue needs \"Approve and publish posts\".");
        if (slots.Count > 50) return WorkResult<List<SocialSlotDto>>.Fail(400, "At most 50 slots.");
        var rows = new List<SocialQueueSlot>();
        foreach (var s in slots)
        {
            if (s.DayOfWeek is < 0 or > 6 || !TimeSpan.TryParse(s.Time, out var t) || t < TimeSpan.Zero || t >= TimeSpan.FromDays(1))
                return WorkResult<List<SocialSlotDto>>.Fail(400, "Each slot needs a day and a time like 18:30.");
            if (!s.ToFacebook && !s.ToInstagram) return WorkResult<List<SocialSlotDto>>.Fail(400, "Each slot needs Facebook, Instagram or both.");
            rows.Add(new SocialQueueSlot { SlotId = Guid.NewGuid(), AccountId = accountId, DayOfWeek = (byte)s.DayOfWeek, TimeOfDay = new TimeSpan(t.Hours, t.Minutes, 0), ToFacebook = s.ToFacebook, ToInstagram = s.ToInstagram });
        }
        _db.SocialQueueSlots.RemoveRange(await _db.SocialQueueSlots.Where(s => s.AccountId == accountId).ToListAsync());
        _db.SocialQueueSlots.AddRange(rows);
        await _db.SaveChangesAsync();
        return WorkResult<List<SocialSlotDto>>.Success(await SlotsAsync(accountId), "Saved.");
    }

    /// <summary>The next queue slot (UTC) that covers the post's channels and holds no other post.</summary>
    internal async Task<DateTime?> NextSlotAsync(SocialDraft d, DateTime now)
    {
        var slots = await _db.SocialQueueSlots.AsNoTracking().Where(s => s.AccountId == d.AccountId).ToListAsync();
        slots = slots.Where(s => (!d.ToFacebook || s.ToFacebook) && (!d.ToInstagram || s.ToInstagram)).ToList();
        if (slots.Count == 0) return null;
        var taken = await _db.SocialDrafts.AsNoTracking()
            .Where(x => x.AccountId == d.AccountId && x.DraftId != d.DraftId && x.Status == "Scheduled" && x.ScheduledAt > now)
            .Select(x => x.ScheduledAt!.Value).ToListAsync();
        var localNow = now + India;
        for (var day = 0; day < 15; day++)
        {
            var date = localNow.Date.AddDays(day);
            foreach (var s in slots.Where(s => s.DayOfWeek == (int)date.DayOfWeek).OrderBy(s => s.TimeOfDay))
            {
                var utc = date + s.TimeOfDay - India;
                if (utc <= now.AddMinutes(10) || taken.Any(t => Math.Abs((t - utc).TotalMinutes) < 1)) continue;
                return utc;
            }
        }
        return null;
    }

    public async Task<List<SocialApprover>> ApproversAsync() =>
        (await SocialShared.PeopleWithAsync(_db, "social.publish")).Select(p => new SocialApprover { UserId = p.UserId, FullName = p.FullName }).ToList();

    // ── publishing (worker) ──────────────────────────────────────────────────

    private class PublishState
    {
        public List<string> Children { get; set; } = new();
        public string? Container { get; set; }
        public DateTime? StartedAt { get; set; }
    }

    private static bool Transient(MetaSocialException ex) => ex.HttpStatus is 503 or 504 || ex.Code is 1 or 2 or 4 or 17 or 32 or 613;

    public async Task<int> PublishDueAsync(CancellationToken ct)
    {
        var now = _now();
        var due = await _db.SocialDrafts
            .Where(d => (d.Status == "Scheduled" && d.ScheduledAt <= now) || d.Status == "Publishing")
            .OrderBy(d => d.ScheduledAt).Take(5).ToListAsync(ct);
        var changed = 0;
        foreach (var d in due)
        {
            ct.ThrowIfCancellationRequested();
            if (d.ApprovedHash == null || d.ApprovedHash != Hash(d))
            {
                d.Status = "AwaitingApproval";
                d.SentBackNote = "It changed after approval, so it needs approval again.";
                await _db.SaveChangesAsync(ct);
                changed++;
                continue;
            }
            var loaded = await _keys.LoadAsync(d.AccountId);
            if (loaded is not { } k)
            {
                Fail(d, "The account is not connected (Social → Setup).");
                await _db.SaveChangesAsync(ct);
                changed++;
                continue;
            }
            if (d.Status == "Scheduled") { d.Status = "Publishing"; d.Attempts++; await _db.SaveChangesAsync(ct); }

            try
            {
                if (d.ToFacebook && d.FacebookPostId == null)
                {
                    d.FacebookPostId = await _meta.PublishFacebookAsync(k.Creds, new FacebookPublish
                    {
                        Kind = d.Kind == "Carousel" ? "Carousel" : d.Kind, Message = FacebookText(d), Link = d.Link, Media = await FilesAsync(d, ct),
                    }, ct);
                    await _db.SaveChangesAsync(ct);
                }
                if (d.ToInstagram && d.InstagramMediaId == null)
                {
                    var done = await InstagramStepAsync(d, k.Account, k.Creds, now, ct);
                    await _db.SaveChangesAsync(ct);
                    if (!done) { changed++; continue; }  // Meta is still preparing the video or pictures
                }

                d.Status = "Published";
                d.PublishedAt = now;
                d.Error = null;
                d.PublishStateJson = null;
                await AddPostsAsync(d, k.Account, k.Creds, now, ct);
                await _db.SaveChangesAsync(ct);
            }
            catch (MetaSocialException ex)
            {
                if (Transient(ex) && d.Attempts < 4)
                {
                    d.Status = "Scheduled";
                    d.ScheduledAt = now.AddMinutes(5 * d.Attempts);
                    d.Error = "Trying again: " + ex.Message;
                }
                else Fail(d, ex.Message);
                await _db.SaveChangesAsync(ct);
            }
            catch (DocStorageException ex)
            {
                Fail(d, ex.Message);
                await _db.SaveChangesAsync(ct);
            }
            changed++;
        }
        return changed;
    }

    private static void Fail(SocialDraft d, string why)
    {
        d.Status = "Failed";
        d.Error = SocialShared.Preview(why, 1000);
        d.PublishStateJson = null;
    }

    private async Task<List<MediaFile>> FilesAsync(SocialDraft d, CancellationToken ct)
    {
        var list = new List<MediaFile>();
        foreach (var m in Media(d.MediaJson))
        {
            await using var s = await _files.OpenAsync(m.Key);
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms, ct);
            list.Add(new MediaFile { Data = ms.ToArray(), Mime = m.Mime, Name = m.Name });
        }
        return list;
    }

    /// <summary>One step of Instagram's container flow; true once the post is published.</summary>
    private async Task<bool> InstagramStepAsync(SocialDraft d, SocialAccount a, SocialCreds c, DateTime now, CancellationToken ct)
    {
        var state = SocialShared.Read<PublishState>(d.PublishStateJson) ?? new PublishState();
        if (state.Container == null)
        {
            var media = Media(d.MediaJson);
            string Url(SocialMediaRef m) => _links.Link(a, m.Key, TimeSpan.FromHours(6))
                ?? throw new MetaSocialException("The API's public address is not known, so Instagram cannot fetch the picture. Connect the webhooks in Social → Setup (or set Social__PublicApiUrl).", httpStatus: 400);
            var caption = InstagramText(d);
            if (d.Kind == "Carousel")
            {
                foreach (var m in media)
                    state.Children.Add(await _meta.CreateInstagramContainerAsync(c, new InstagramContainer { MediaType = m.Mime.StartsWith("video/") ? "VIDEO" : "IMAGE", ImageUrl = m.Mime.StartsWith("image/") ? Url(m) : null, VideoUrl = m.Mime.StartsWith("video/") ? Url(m) : null, IsCarouselItem = true }, ct));
                state.Container = await _meta.CreateInstagramContainerAsync(c, new InstagramContainer { MediaType = "CAROUSEL", Children = state.Children, Caption = caption }, ct);
            }
            else
            {
                var m = media.First();
                var video = m.Mime.StartsWith("video/");
                var type = d.Kind switch { "Story" => "STORIES", "Reel" or "Video" => "REELS", _ => "IMAGE" };
                state.Container = await _meta.CreateInstagramContainerAsync(c, new InstagramContainer { MediaType = type, ImageUrl = video ? null : Url(m), VideoUrl = video ? Url(m) : null, Caption = caption }, ct);
            }
            state.StartedAt = now;
            d.PublishStateJson = SocialShared.Write(state);
        }

        var status = await _meta.InstagramContainerStatusAsync(c, state.Container, ct);
        if (status is "ERROR" or "EXPIRED")
            throw new MetaSocialException(status == "EXPIRED" ? "Instagram's prepared post expired before it was published." : "Instagram could not prepare the picture or video (check its size, shape and format).", httpStatus: 400);
        if (status is not ("FINISHED" or "PUBLISHED"))
        {
            if (now - (state.StartedAt ?? now) > TimeSpan.FromMinutes(45)) throw new MetaSocialException("Instagram took too long to prepare the video.", httpStatus: 400);
            return false;
        }
        d.InstagramMediaId = await _meta.PublishInstagramAsync(c, state.Container, ct);
        return true;
    }

    /// <summary>Shows the published post in Posts right away; the sync fills in its numbers later.</summary>
    private async Task AddPostsAsync(SocialDraft d, SocialAccount a, SocialCreds c, DateTime now, CancellationToken ct)
    {
        async Task Add(string channel, string? id, string? text)
        {
            if (id == null || await _db.SocialPosts.AnyAsync(p => p.Channel == channel && p.MetaId == id, ct)) return;
            string? link = null;
            try { link = await _meta.PermalinkAsync(c, channel, id, ct); } catch (MetaSocialException) { }
            _db.SocialPosts.Add(new SocialPost
            {
                PostId = Guid.NewGuid(), AccountId = a.AccountId, Channel = channel, MetaId = id, Type = d.Kind == "Video" && channel == "instagram" ? "Reel" : d.Kind,
                Caption = text, Permalink = link, PublishedAt = now, DraftId = d.DraftId, CreatedAt = now,
            });
        }
        if (d.ToFacebook) await Add("facebook", d.FacebookPostId, FacebookText(d));
        if (d.ToInstagram) await Add("instagram", d.InstagramMediaId, InstagramText(d));
    }
}
