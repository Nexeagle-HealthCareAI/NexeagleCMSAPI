using System.Text.Json;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.Social;

/// <summary>
/// What Meta sends to /api/v1/meta/webhook for the connected Pages and Instagram accounts: comments (page
/// "feed", instagram "comments"), direct messages and their read/delivery receipts (entry "messaging"), and
/// lead forms ("leadgen"). Each comment, message and lead is stored once, however often Meta resends it.
/// </summary>
public class SocialWebhookService : ISocialWebhookService
{
    private readonly CmsDbContext _db;
    private readonly IMetaSocial _meta;
    private readonly SocialKeys _keys;
    private readonly ISocialLeadsService _leads;
    private readonly ILogger<SocialWebhookService> _logger;
    private readonly Func<DateTime> _now;

    public SocialWebhookService(CmsDbContext db, IMetaSocial meta, SocialKeys keys, ISocialLeadsService leads, ILogger<SocialWebhookService> logger)
        : this(db, meta, keys, leads, logger, () => DateTime.UtcNow) { }

    internal SocialWebhookService(CmsDbContext db, IMetaSocial meta, SocialKeys keys, ISocialLeadsService leads, ILogger<SocialWebhookService> logger, Func<DateTime> now)
    {
        _db = db;
        _meta = meta;
        _keys = keys;
        _leads = leads;
        _logger = logger;
        _now = now;
    }

    private static string? S(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        } : null;

    private static JsonElement O(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    private static IEnumerable<JsonElement> A(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static DateTime FromUnix(string? s, bool millis, DateTime fallback) =>
        long.TryParse(s, out var n) ? (millis ? DateTimeOffset.FromUnixTimeMilliseconds(n) : DateTimeOffset.FromUnixTimeSeconds(n)).UtcDateTime : fallback;

    public async Task HandleAsync(JsonElement payload, CancellationToken ct = default)
    {
        var obj = S(payload, "object");
        if (obj is not ("page" or "instagram")) return;
        foreach (var entry in A(payload, "entry"))
        {
            var id = S(entry, "id");
            if (id == null) continue;
            var account = obj == "page"
                ? await _db.SocialAccounts.FirstOrDefaultAsync(a => a.PageId == id && a.IsActive, ct)
                : await _db.SocialAccounts.FirstOrDefaultAsync(a => a.IgUserId == id && a.IsActive, ct);
            if (account == null) { _logger.LogInformation("Meta webhook for {Object} {Id}: no connected account; ignored.", obj, id); continue; }

            foreach (var change in A(entry, "changes"))
            {
                var field = S(change, "field");
                var value = O(change, "value");
                if (obj == "page" && field == "feed" && S(value, "item") == "comment") await FacebookCommentAsync(account, value);
                else if (obj == "page" && field == "leadgen" && S(value, "leadgen_id") is { } leadgenId)
                {
                    await _db.SaveChangesAsync(ct);
                    var r = await _leads.IngestFormLeadAsync(account.AccountId, leadgenId, ct);
                    if (!r.Ok) _logger.LogWarning("Meta lead {Lead} could not be brought in: {Message}", leadgenId, r.Message);
                }
                else if (obj == "instagram" && field == "comments") await InstagramCommentAsync(account, value);
            }

            foreach (var m in A(entry, "messaging"))
                await MessagingAsync(account, obj == "page" ? "messenger" : "instagram", m, ct);

            var now = _now();
            if (account.LastWebhookAt == null || now - account.LastWebhookAt > TimeSpan.FromMinutes(1)) account.LastWebhookAt = now;
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task FacebookCommentAsync(SocialAccount a, JsonElement v)
    {
        var commentId = S(v, "comment_id");
        var postId = S(v, "post_id");
        if (commentId == null || postId == null) return;
        if (S(v, "verb") == "remove")
        {
            var gone = await _db.SocialComments.FirstOrDefaultAsync(c => c.Channel == "facebook" && c.MetaId == commentId);
            if (gone != null) { gone.Deleted = true; gone.Status = "Done"; }
            return;
        }
        var parent = S(v, "parent_id");
        var from = O(v, "from");
        await SocialCommentStore.UpsertAsync(_db, a, "facebook", postId, commentId, parent == postId ? null : parent,
            S(from, "id"), S(from, "name"), S(v, "message"), false, FromUnix(S(v, "created_time"), false, _now()));
    }

    private async Task InstagramCommentAsync(SocialAccount a, JsonElement v)
    {
        var commentId = S(v, "id");
        var mediaId = S(O(v, "media"), "id");
        if (commentId == null || mediaId == null) return;
        var from = O(v, "from");
        await SocialCommentStore.UpsertAsync(_db, a, "instagram", mediaId, commentId, S(v, "parent_id"),
            S(from, "id"), S(from, "username"), S(v, "text"), false, _now());
    }

    private async Task<SocialContact> ContactAsync(SocialAccount a, string channel, string externalId, CancellationToken ct)
    {
        var c = _db.SocialContacts.Local.FirstOrDefault(x => x.AccountId == a.AccountId && x.Channel == channel && x.ExternalId == externalId)
                ?? await _db.SocialContacts.FirstOrDefaultAsync(x => x.AccountId == a.AccountId && x.Channel == channel && x.ExternalId == externalId, ct);
        if (c != null) return c;
        c = new SocialContact { ContactId = Guid.NewGuid(), AccountId = a.AccountId, Channel = channel, ExternalId = externalId, ChatStatus = "Open", CreatedAt = _now() };
        // Their name, best effort: a webhook must not fail because a profile is private.
        if (_keys.Creds(a) is { } creds)
        {
            try
            {
                var p = await _meta.PersonAsync(creds, channel, externalId, ct);
                c.Name = p.Name;
                c.Username = p.Username;
            }
            catch (MetaSocialException ex) { _logger.LogInformation("Social: no profile for {Channel} {Id}: {Message}", channel, externalId, ex.Message); }
        }
        _db.SocialContacts.Add(c);
        return c;
    }

    private async Task MessagingAsync(SocialAccount a, string channel, JsonElement m, CancellationToken ct)
    {
        var sender = S(O(m, "sender"), "id");
        var recipient = S(O(m, "recipient"), "id");
        var at = FromUnix(S(m, "timestamp"), true, _now());
        var ours = channel == "messenger" ? a.PageId : a.IgUserId;

        if (O(m, "read") is { ValueKind: JsonValueKind.Object } read && sender != null)
        {
            var watermark = FromUnix(S(read, "watermark"), true, at);
            var c = await _db.SocialContacts.FirstOrDefaultAsync(x => x.AccountId == a.AccountId && x.Channel == channel && x.ExternalId == sender, ct);
            if (c == null) return;
            foreach (var msg in await _db.SocialMessages.Where(x => x.ContactId == c.ContactId && x.Direction == "Out" && x.Status != "read" && x.Status != "failed" && x.CreatedAt <= watermark).ToListAsync(ct))
                msg.Status = "read";
            return;
        }
        if (O(m, "delivery") is { ValueKind: JsonValueKind.Object } delivery)
        {
            var mids = A(delivery, "mids").Select(x => x.GetString()).Where(x => x != null).ToList();
            foreach (var msg in await _db.SocialMessages.Where(x => x.MetaId != null && mids.Contains(x.MetaId) && x.Status == "sent").ToListAsync(ct))
                msg.Status = "delivered";
            return;
        }

        var message = O(m, "message");
        if (message.ValueKind != JsonValueKind.Object || sender == null || recipient == null) return;
        if (S(message, "is_deleted") == "true") return;
        var mid = S(message, "mid");
        if (mid != null && (await _db.SocialMessages.AnyAsync(x => x.MetaId == mid, ct) || _db.SocialMessages.Local.Any(x => x.MetaId == mid))) return;

        var echo = S(message, "is_echo") == "true" || sender == ours;
        var contact = await ContactAsync(a, channel, echo ? recipient : sender, ct);
        var attachments = A(message, "attachments").Select(x => new SocialAttachment
        {
            Type = S(x, "type") ?? "file", Url = S(O(x, "payload"), "url"), Name = S(O(x, "payload"), "title"),
        }).ToList();
        var text = S(message, "text");
        _db.SocialMessages.Add(new SocialMessage
        {
            MessageId = Guid.NewGuid(), ContactId = contact.ContactId, Direction = echo ? "Out" : "In", MetaId = mid, Text = text,
            AttachmentsJson = attachments.Count == 0 ? null : SocialShared.Write(attachments), Status = echo ? "sent" : "received", CreatedAt = at,
        });
        contact.LastMessageAt = at;
        contact.LastMessagePreview = SocialShared.Preview(text ?? (attachments.Count > 0 ? $"[{attachments[0].Type}]" : "Message"), 200);
        if (!echo)
        {
            contact.LastInboundAt = at;
            contact.UnreadCount++;
            contact.ChatStatus = "Open";
        }
    }
}
