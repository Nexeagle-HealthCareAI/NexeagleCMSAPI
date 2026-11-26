using System.Text.Json;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.WhatsApp;

/// <summary>
/// Messages and delivery statuses for the marketing number (Meta sends them to /api/v1/whatsapp/webhook
/// through the number's webhook override). Each message is stored once; a status never goes back; cost is
/// recorded when Meta says a message was charged.
/// </summary>
public class WaWebhookService : IWaWebhookService
{
    private readonly CmsDbContext _db;
    private readonly ILogger<WaWebhookService> _logger;
    private readonly string? _phoneId;

    public WaWebhookService(CmsDbContext db, IWaConnectionStore connection, ILogger<WaWebhookService> logger)
    {
        _db = db;
        _logger = logger;
        _phoneId = connection.Current.PhoneNumberId;
    }

    public async Task HandleAsync(JsonElement payload)
    {
        if (!payload.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array) return;
        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (Str(change, "field") != "messages" || !change.TryGetProperty("value", out var value)) continue;
                // Only the marketing number: anything for another number is not ours to store.
                var phoneId = value.TryGetProperty("metadata", out var meta) ? Str(meta, "phone_number_id") : null;
                if (_phoneId != null && phoneId != null && phoneId != _phoneId)
                {
                    _logger.LogWarning("WhatsApp webhook for another number ({PhoneId}) ignored.", phoneId);
                    continue;
                }

                var profiles = new Dictionary<string, string>();
                if (value.TryGetProperty("contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array)
                    foreach (var c in contacts.EnumerateArray())
                        if (Str(c, "wa_id") is { } id && c.TryGetProperty("profile", out var p) && Str(p, "name") is { } name)
                            profiles[id] = name;

                if (value.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                    foreach (var m in messages.EnumerateArray())
                        await InboundAsync(m, profiles);

                if (value.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == JsonValueKind.Array)
                    foreach (var s in statuses.EnumerateArray())
                        await StatusAsync(s);
            }
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null }
            : null;

    private static DateTime When(JsonElement e) =>
        long.TryParse(Str(e, "timestamp"), out var ts) ? DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime : DateTime.UtcNow;

    private async Task InboundAsync(JsonElement m, IReadOnlyDictionary<string, string> profiles)
    {
        var wamId = Str(m, "id");
        var from = WaRules.NormalisePhone(Str(m, "from"));
        if (wamId == null || from == null) return;
        if (await _db.WaMessages.AnyAsync(x => x.WamId == wamId)) return;   // Meta sends again when unsure

        var at = When(m);
        var type = Str(m, "type") ?? "unsupported";
        var message = new WaMessage
        {
            MessageId = Guid.NewGuid(),
            Direction = "In",
            WamId = wamId,
            Type = type,
            Status = "received",
            StatusAt = at,
            CreatedAt = at,
        };
        if (m.TryGetProperty("context", out var context)) message.ReplyToWamId = Str(context, "id");

        string? stopText = null;
        switch (type)
        {
            case "text":
                message.Body = m.TryGetProperty("text", out var text) ? Str(text, "body") : null;
                stopText = message.Body;
                break;
            case "image": case "video": case "audio": case "document": case "sticker":
                if (m.TryGetProperty(type, out var media))
                {
                    message.MediaId = Str(media, "id");
                    message.MediaMime = Str(media, "mime_type");
                    message.MediaName = Str(media, "filename");
                    message.Body = Str(media, "caption");
                }
                break;
            case "location":
                if (m.TryGetProperty("location", out var loc))
                {
                    var place = string.Join(", ", new[] { Str(loc, "name"), Str(loc, "address") }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    message.Body = $"{(place.Length > 0 ? place + " — " : string.Empty)}https://maps.google.com/?q={Str(loc, "latitude")},{Str(loc, "longitude")}";
                }
                break;
            case "contacts":
                if (m.TryGetProperty("contacts", out var cards) && cards.ValueKind == JsonValueKind.Array)
                    message.Body = string.Join("\n", cards.EnumerateArray().Select(card =>
                    {
                        var name = card.TryGetProperty("name", out var n) ? Str(n, "formatted_name") : null;
                        var phones = card.TryGetProperty("phones", out var ps) && ps.ValueKind == JsonValueKind.Array
                            ? string.Join(", ", ps.EnumerateArray().Select(p => Str(p, "phone")).Where(p => p != null)) : string.Empty;
                        return $"{name} {phones}".Trim();
                    }));
                break;
            case "button":   // a quick reply button on a template
                if (m.TryGetProperty("button", out var button)) message.Body = Str(button, "text");
                stopText = message.Body;
                break;
            case "interactive":
                if (m.TryGetProperty("interactive", out var inter))
                {
                    if (inter.TryGetProperty("button_reply", out var br)) message.Body = Str(br, "title");
                    else if (inter.TryGetProperty("list_reply", out var lr)) message.Body = Str(lr, "title");
                    else message.Body = "Form reply";
                }
                stopText = message.Body;
                break;
            case "reaction":
                if (m.TryGetProperty("reaction", out var reaction))
                {
                    message.Body = Str(reaction, "emoji") ?? "Removed a reaction";
                    message.ReplyToWamId = Str(reaction, "message_id");
                }
                break;
            default:
                message.Type = "unsupported";
                message.Body = "A message WhatsApp cannot show here (for example a poll or a view-once photo).";
                break;
        }

        var contact = await _db.WaContacts.FirstOrDefaultAsync(c => c.Phone == from);
        if (contact == null)
        {
            contact = new WaContact
            {
                ContactId = Guid.NewGuid(),
                Phone = from,
                Source = "Inbound",
                OptIn = "Unknown",
                ChatStatus = "Open",
                CreatedAt = at,
                UpdatedAt = at,
            };
            _db.WaContacts.Add(contact);
        }
        if (profiles.TryGetValue(Str(m, "from") ?? string.Empty, out var profileName)) contact.ProfileName = profileName.Length > 150 ? profileName[..150] : profileName;

        message.ContactId = contact.ContactId;
        _db.WaMessages.Add(message);

        // A reaction does not reopen the window's chat or count as unread.
        if (type != "reaction")
        {
            if (contact.LastInboundAt == null || at > contact.LastInboundAt) contact.LastInboundAt = at;
            contact.LastMessageAt = at;
            contact.LastMessagePreview = WaRules.Preview(message.Type, message.Body);
            contact.UnreadCount += 1;
            contact.ChatStatus = "Open";
        }

        if (WaRules.IsStopWord(stopText) && contact.OptIn != "OptedOut")
        {
            contact.OptIn = "OptedOut";
            contact.OptOutAt = at;
            contact.OptOutReason = $"Wrote \"{stopText!.Trim()}\"";
            _logger.LogInformation("WhatsApp contact {ContactId} opted out by message.", contact.ContactId);
        }
        contact.UpdatedAt = DateTime.UtcNow;

        await CountReplyAsync(contact.ContactId, message.ReplyToWamId, at);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // The same message arriving twice at the same moment: the unique Meta id keeps one.
            _logger.LogInformation(ex, "WhatsApp message {WamId} was already stored.", wamId);
            foreach (var e in _db.ChangeTracker.Entries().ToList()) e.State = EntityState.Detached;
        }
    }

    /// <summary>A reply to a broadcast (or within 7 days of one) counts once as "replied".</summary>
    private async Task CountReplyAsync(Guid contactId, string? replyTo, DateTime at)
    {
        WaBroadcastRecipient? recipient = null;
        if (replyTo != null)
        {
            var original = await _db.WaMessages.AsNoTracking().FirstOrDefaultAsync(x => x.WamId == replyTo && x.BroadcastId != null);
            if (original != null)
                recipient = await _db.WaBroadcastRecipients.FirstOrDefaultAsync(r => r.BroadcastId == original.BroadcastId && r.ContactId == contactId);
        }
        if (recipient == null)
        {
            var since = at.AddDays(-7);
            recipient = await _db.WaBroadcastRecipients
                .Where(r => r.ContactId == contactId && r.SentAt != null && r.SentAt >= since && (r.Status == "Sent" || r.Status == "Delivered" || r.Status == "Read"))
                .OrderByDescending(r => r.SentAt)
                .FirstOrDefaultAsync();
        }
        if (recipient == null || recipient.Status == "Replied") return;
        recipient.Status = "Replied";
        var broadcast = await _db.WaBroadcasts.FirstOrDefaultAsync(b => b.BroadcastId == recipient.BroadcastId);
        if (broadcast != null) { broadcast.Replied += 1; broadcast.UpdatedAt = DateTime.UtcNow; }
    }

    private async Task StatusAsync(JsonElement s)
    {
        var wamId = Str(s, "id");
        var status = Str(s, "status");
        if (wamId == null || status is not ("sent" or "delivered" or "read" or "failed")) return;
        var message = await _db.WaMessages.FirstOrDefaultAsync(x => x.WamId == wamId);
        if (message == null)
        {
            _logger.LogInformation("WhatsApp status {Status} for a message the CMS does not know ({WamId}).", status, wamId);
            return;
        }

        var at = When(s);
        var before = message.Status;
        var moves = status == "failed"
            ? before is not ("delivered" or "read" or "failed")
            : WaShared.StatusRank(status) > WaShared.StatusRank(before) && before != "failed";

        if (s.TryGetProperty("pricing", out var pricing) && pricing.ValueKind == JsonValueKind.Object)
        {
            message.PricingCategory = Str(pricing, "category") ?? message.PricingCategory;
            if (Str(pricing, "billable") is { } billable) message.Billable = billable == "true";
        }

        // Meta charges when a message is delivered: the cost is written once, the first time it is
        // delivered (or read, when the "delivered" never came), and only when Meta says it is billable.
        var firstDelivery = status is ("delivered" or "read") && WaShared.StatusRank(before) < 2 && before != "failed";
        if (firstDelivery && message.Billable == true && message.Direction == "Out")
        {
            var rates = await WaShared.RatesAsync(_db);
            var contact = await _db.WaContacts.AsNoTracking().FirstAsync(c => c.ContactId == message.ContactId);
            message.Cost = WaRules.CostOf(true, contact.Phone, WaRules.RateCategory(message.PricingCategory), rates);
            if (message.BroadcastId is { } costOf)
            {
                var b = await _db.WaBroadcasts.FirstOrDefaultAsync(x => x.BroadcastId == costOf);
                if (b != null) b.ActualCost += message.Cost;
            }
        }

        if (moves)
        {
            message.Status = status;
            message.StatusAt = at;
            if (status == "failed" && s.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                var error = errors[0];
                var code = int.TryParse(Str(error, "code"), out var c) ? c : (int?)null;
                var details = error.TryGetProperty("error_data", out var data) ? Str(data, "details") : null;
                message.ErrorCode = code;
                message.ErrorText = WaShared.FriendlyError(code, details ?? Str(error, "message") ?? Str(error, "title"));
                if (code == 131050)
                {
                    var contact = await _db.WaContacts.FirstOrDefaultAsync(x => x.ContactId == message.ContactId);
                    if (contact != null && contact.OptIn != "OptedOut")
                    {
                        contact.OptIn = "OptedOut";
                        contact.OptOutAt = at;
                        contact.OptOutReason = "Stopped marketing messages in WhatsApp";
                        contact.UpdatedAt = DateTime.UtcNow;
                    }
                }
            }
            if (message.BroadcastId is { } broadcastId) await CountStatusAsync(broadcastId, message, before, status);
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>Moves the broadcast's counts and the recipient's status along with a message's.</summary>
    private async Task CountStatusAsync(Guid broadcastId, WaMessage message, string before, string after)
    {
        var b = await _db.WaBroadcasts.FirstOrDefaultAsync(x => x.BroadcastId == broadcastId);
        var r = await _db.WaBroadcastRecipients.FirstOrDefaultAsync(x => x.BroadcastId == broadcastId && x.ContactId == message.ContactId);
        if (b == null) return;
        var from = WaShared.StatusRank(before);
        if (after == "failed") { b.Failed += 1; }
        else
        {
            if (from < 2 && WaShared.StatusRank(after) >= 2) b.Delivered += 1;
            if (from < 3 && after == "read") b.Read += 1;
        }
        b.UpdatedAt = DateTime.UtcNow;
        if (r != null && r.Status != "Replied")
        {
            r.Status = after switch { "delivered" => "Delivered", "read" => "Read", "failed" => "Failed", _ => r.Status };
            if (after == "failed") r.Error = message.ErrorText;
        }
    }
}
