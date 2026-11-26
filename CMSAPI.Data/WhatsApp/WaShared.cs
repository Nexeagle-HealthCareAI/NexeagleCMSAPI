using System.Text.Json;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.WhatsApp;

/// <summary>What the WhatsApp services share: rates, names, the shapes the screens get, friendly errors.</summary>
internal static class WaShared
{
    public static async Task<Dictionary<(string, string), decimal>> RatesAsync(CmsDbContext db) =>
        (await db.WaRates.AsNoTracking().ToListAsync()).ToDictionary(r => (r.Country, r.Category), r => r.Rate);

    public static async Task<WaSettings> SettingsAsync(CmsDbContext db) =>
        await db.WaSettings.FirstOrDefaultAsync(s => s.SettingsId == 1)
        ?? new WaSettings { SettingsId = 1, Currency = "INR", SendPerSecond = 20, UpdatedAt = DateTime.UtcNow };

    public static async Task<Dictionary<Guid, string>> NamesAsync(CmsDbContext db, IEnumerable<Guid?> ids)
    {
        var wanted = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (wanted.Count == 0) return new();
        return await db.CmsUsers.AsNoTracking().Where(u => wanted.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName);
    }

    public static List<string> Tags(string? tags) =>
        (tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

    public static string? JoinTags(IEnumerable<string>? tags)
    {
        var list = (tags ?? Enumerable.Empty<string>()).Select(t => t.Trim().ToLowerInvariant().Replace(",", " "))
            .Where(t => t.Length > 0).Distinct().Take(20).ToList();
        var joined = string.Join(",", list);
        return joined.Length == 0 ? null : joined.Length > 400 ? joined[..400] : joined;
    }

    public static Dictionary<string, string> Attributes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static string? AttributesJson(Dictionary<string, string>? attrs)
    {
        if (attrs == null) return null;
        var clean = attrs.Where(a => !string.IsNullOrWhiteSpace(a.Key))
            .ToDictionary(a => a.Key.Trim().ToLowerInvariant().Replace(' ', '_'), a => (a.Value ?? string.Empty).Trim());
        return clean.Count == 0 ? null : JsonSerializer.Serialize(clean);
    }

    public static WaChatDto Chat(WaContact c, IReadOnlyDictionary<Guid, string> names, DateTime now) => Fill(new WaChatDto(), c, names, now);

    public static WaContactDto Contact(WaContact c, IReadOnlyDictionary<Guid, string> names, DateTime now)
    {
        var dto = Fill(new WaContactDto(), c, names, now);
        dto.LeadId = c.LeadId;
        dto.OptInSource = c.OptInSource;
        dto.OptInBy = c.OptInByUserId is { } by && names.TryGetValue(by, out var n) ? n : null;
        dto.OptInAt = c.OptInAt;
        dto.OptOutAt = c.OptOutAt;
        dto.OptOutReason = c.OptOutReason;
        dto.Attributes = Attributes(c.Attributes);
        dto.LastInboundAt = c.LastInboundAt;
        dto.CreatedAt = c.CreatedAt;
        return dto;
    }

    private static T Fill<T>(T dto, WaContact c, IReadOnlyDictionary<Guid, string> names, DateTime now) where T : WaChatDto
    {
        dto.ContactId = c.ContactId;
        dto.Phone = c.Phone;
        dto.DisplayPhone = WaRules.Display(c.Phone);
        dto.Name = c.Name;
        dto.ProfileName = c.ProfileName;
        dto.Source = c.Source;
        dto.OptIn = c.OptIn;
        dto.Tags = Tags(c.Tags);
        dto.LastMessageAt = c.LastMessageAt;
        dto.LastMessagePreview = c.LastMessagePreview;
        dto.UnreadCount = c.UnreadCount;
        dto.ChatStatus = c.ChatStatus;
        dto.AssignedUserId = c.AssignedUserId;
        dto.AssignedName = c.AssignedUserId is { } a && names.TryGetValue(a, out var name) ? name : null;
        dto.WindowClosesAt = WaRules.WindowClosesAt(c.LastInboundAt, now);
        return dto;
    }

    public static WaMessageDto Message(WaMessage m, IReadOnlyDictionary<Guid, string> names) => new()
    {
        MessageId = m.MessageId,
        Direction = m.Direction,
        Type = m.Type,
        Body = m.Body,
        HasMedia = m.MediaId != null,
        MediaMime = m.MediaMime,
        MediaName = m.MediaName,
        TemplateName = m.TemplateName,
        BroadcastId = m.BroadcastId,
        Status = m.Status,
        StatusAt = m.StatusAt,
        ErrorText = m.ErrorText,
        SentBy = m.SentByUserId is { } u && names.TryGetValue(u, out var n) ? n : m.BroadcastId != null ? "Broadcast" : null,
        Cost = m.Cost,
        CreatedAt = m.CreatedAt,
    };

    /// <summary>Meta's error codes in words people can act on.</summary>
    public static string FriendlyError(int? code, string? fallback) => code switch
    {
        131047 => "More than 24 hours since they last wrote: only a template can be sent now.",
        131049 => "Meta held this marketing message back to protect the person from too many (not charged). Try again in a few days.",
        131050 => "This person has stopped marketing messages from you in WhatsApp.",
        131026 => "Could not be delivered: the number may not be on WhatsApp, or their app is too old.",
        131021 => "You cannot send a message to your own number.",
        131051 => "WhatsApp does not support this kind of message.",
        131052 or 131053 => "The file could not be sent. Check its size and type.",
        131056 => "Too many messages to this person in a short time. Wait a little.",
        132000 => "The template's variables do not match: check each one is filled.",
        132001 => "This template does not exist in that language (or is not approved yet).",
        132007 => "The text breaks WhatsApp's template rules.",
        132012 => "A template variable is in the wrong format.",
        132015 => "This template is paused by Meta because of low quality.",
        132016 => "This template is disabled by Meta.",
        130429 or 131048 or 80007 => "Sending too fast for WhatsApp: it will be retried.",
        368 => "Meta has temporarily blocked this number for policy reasons.",
        _ => string.IsNullOrWhiteSpace(fallback) ? "WhatsApp could not send this." : fallback!.Length > 380 ? fallback[..380] : fallback,
    };

    /// <summary>How far a message has got: a status never goes back (a late "delivered" after "read" changes nothing).</summary>
    public static int StatusRank(string status) => status switch
    {
        "queued" => 0, "sent" => 1, "delivered" => 2, "read" => 3, "failed" => 4, _ => 0,
    };
}
