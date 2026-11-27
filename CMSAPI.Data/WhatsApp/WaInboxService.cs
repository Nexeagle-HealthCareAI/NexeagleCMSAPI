using System.Text.Json.Nodes;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.WhatsApp;

/// <summary>See IWaInboxService. Reading needs whatsapp.view (checked by the controller); writing needs whatsapp.reply.</summary>
public class WaInboxService : IWaInboxService
{
    private const int MaxMediaBytes = 16 * 1024 * 1024;
    private readonly CmsDbContext _db;
    private readonly IWaMeta _meta;
    private readonly IDocFileStore _files;
    private readonly Func<DateTime> _now;

    public WaInboxService(CmsDbContext db, IWaMeta meta, IDocFileStore files) : this(db, meta, files, () => DateTime.UtcNow) { }

    internal WaInboxService(CmsDbContext db, IWaMeta meta, IDocFileStore files, Func<DateTime> now)
    {
        _db = db;
        _meta = meta;
        _files = files;
        _now = now;
    }

    private static WorkResult<T> NotFound<T>() => WorkResult<T>.Fail(404, "This chat does not exist.");
    private static WorkResult<T> NoReply<T>() => WorkResult<T>.Fail(403, "You can read chats but not reply. Ask an administrator for \"Reply to WhatsApp chats\".");

    public async Task<WorkResult<List<WaChatDto>>> ChatsAsync(WaActor actor, WaChatQuery query)
    {
        var q = _db.WaContacts.AsNoTracking().Where(c => c.LastMessageAt != null);
        switch ((query.View ?? "open").ToLowerInvariant())
        {
            case "open": q = q.Where(c => c.ChatStatus == "Open"); break;
            case "closed": q = q.Where(c => c.ChatStatus == "Closed"); break;
            case "mine": q = q.Where(c => c.AssignedUserId == actor.UserId && c.ChatStatus == "Open"); break;
            case "unassigned": q = q.Where(c => c.AssignedUserId == null && c.ChatStatus == "Open"); break;
            case "unread": q = q.Where(c => c.UnreadCount > 0); break;
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            var digits = new string(s.Where(char.IsDigit).ToArray());
            q = q.Where(c => (c.Name != null && c.Name.Contains(s)) || (c.ProfileName != null && c.ProfileName.Contains(s))
                             || (digits.Length >= 3 && c.Phone.Contains(digits)) || (c.LastMessagePreview != null && c.LastMessagePreview.Contains(s)));
        }
        if (query.Before is { } before) q = q.Where(c => c.LastMessageAt < before);
        var list = await q.OrderByDescending(c => c.LastMessageAt).Take(Math.Clamp(query.Take, 1, 200)).ToListAsync();
        var names = await WaShared.NamesAsync(_db, list.Select(c => c.AssignedUserId));
        var now = _now();
        return WorkResult<List<WaChatDto>>.Success(list.Select(c => WaShared.Chat(c, names, now)).ToList());
    }

    public async Task<WorkResult<WaThreadDto>> ThreadAsync(WaActor actor, Guid contactId)
    {
        var contact = await _db.WaContacts.FirstOrDefaultAsync(c => c.ContactId == contactId);
        if (contact == null) return NotFound<WaThreadDto>();
        if (contact.UnreadCount > 0)
        {
            contact.UnreadCount = 0;
            await _db.SaveChangesAsync();
        }
        var messages = await _db.WaMessages.AsNoTracking().Where(m => m.ContactId == contactId)
            .OrderByDescending(m => m.CreatedAt).Take(500).ToListAsync();
        var notes = await _db.WaNotes.AsNoTracking().Where(n => n.ContactId == contactId).OrderBy(n => n.CreatedAt).ToListAsync();
        return WorkResult<WaThreadDto>.Success(await ThreadDtoAsync(contact, messages.OrderBy(m => m.CreatedAt).ToList(), notes));
    }

    public async Task<WorkResult<WaThreadDto>> ThreadSinceAsync(WaActor actor, Guid contactId, DateTime after)
    {
        var contact = await _db.WaContacts.FirstOrDefaultAsync(c => c.ContactId == contactId);
        if (contact == null) return NotFound<WaThreadDto>();
        if (contact.UnreadCount > 0)
        {
            contact.UnreadCount = 0;
            await _db.SaveChangesAsync();
        }
        // Statuses change after a message is made: anything that changed since `after` comes back too.
        var messages = await _db.WaMessages.AsNoTracking()
            .Where(m => m.ContactId == contactId && (m.CreatedAt > after || (m.StatusAt != null && m.StatusAt > after)))
            .OrderBy(m => m.CreatedAt).ToListAsync();
        var notes = await _db.WaNotes.AsNoTracking().Where(n => n.ContactId == contactId && n.CreatedAt > after).OrderBy(n => n.CreatedAt).ToListAsync();
        return WorkResult<WaThreadDto>.Success(await ThreadDtoAsync(contact, messages, notes));
    }

    private async Task<WaThreadDto> ThreadDtoAsync(WaContact contact, List<WaMessage> messages, List<WaNote> notes)
    {
        var names = await WaShared.NamesAsync(_db, messages.Select(m => m.SentByUserId).Concat(notes.Select(n => (Guid?)n.UserId))
            .Append(contact.AssignedUserId).Append(contact.OptInByUserId));
        var now = _now();
        return new WaThreadDto
        {
            Contact = WaShared.Contact(contact, names, now),
            Messages = messages.Select(m => WaShared.Message(m, names)).ToList(),
            Notes = notes.Select(n => new WaNoteDto { NoteId = n.NoteId, UserId = n.UserId, Author = names.GetValueOrDefault(n.UserId) ?? "Someone", Text = n.Text, CreatedAt = n.CreatedAt }).ToList(),
            WindowOpen = WaRules.WindowOpen(contact.LastInboundAt, now),
        };
    }

    public async Task<WorkResult<WaMessageDto>> SendTextAsync(WaActor actor, Guid contactId, WaSendTextRequest request)
    {
        if (!actor.CanReply) return NoReply<WaMessageDto>();
        var text = (request.Text ?? string.Empty).Trim();
        if (text.Length == 0) return WorkResult<WaMessageDto>.Fail(400, "Write a message first.");
        if (text.Length > WaRules.MaxTextMessage) return WorkResult<WaMessageDto>.Fail(400, $"A message can be at most {WaRules.MaxTextMessage} characters.");
        var contact = await _db.WaContacts.FirstOrDefaultAsync(c => c.ContactId == contactId);
        if (contact == null) return NotFound<WaMessageDto>();
        if (!WaRules.WindowOpen(contact.LastInboundAt, _now()))
            return WorkResult<WaMessageDto>.Fail(409, "More than 24 hours since they last wrote: WhatsApp only allows a template now.");
        if (!_meta.IsConfigured) return NotConnected<WaMessageDto>();

        var row = new WaMessage { MessageId = Guid.NewGuid(), Type = "text", Body = text, SentByUserId = actor.UserId, CreatedAt = _now() };
        await WaSender.SendAsync(_db, _meta, contact, row, new JsonObject
        {
            ["type"] = "text",
            ["text"] = new JsonObject { ["body"] = text, ["preview_url"] = text.Contains("https://") },
        });
        await AutoAssignAsync(actor, contact);
        return await ResultAsync(row);
    }

    public async Task<WorkResult<WaMessageDto>> SendMediaAsync(WaActor actor, Guid contactId, byte[] data, string mime, string fileName, string? caption)
    {
        if (!actor.CanReply) return NoReply<WaMessageDto>();
        if (data.Length == 0) return WorkResult<WaMessageDto>.Fail(400, "Choose a file to send.");
        if (data.Length > MaxMediaBytes) return WorkResult<WaMessageDto>.Fail(413, "WhatsApp files can be at most 16 MB (pictures 5 MB).");
        var kind = mime.StartsWith("image/") ? "image" : mime.StartsWith("video/") ? "video" : mime.StartsWith("audio/") ? "audio" : "document";
        if (kind == "image" && data.Length > 5 * 1024 * 1024) return WorkResult<WaMessageDto>.Fail(413, "Pictures can be at most 5 MB. Send it as a document instead.");
        if (kind == "image" && mime is not ("image/jpeg" or "image/png")) kind = "document";   // WhatsApp shows only JPEG and PNG as pictures
        var contact = await _db.WaContacts.FirstOrDefaultAsync(c => c.ContactId == contactId);
        if (contact == null) return NotFound<WaMessageDto>();
        if (!WaRules.WindowOpen(contact.LastInboundAt, _now()))
            return WorkResult<WaMessageDto>.Fail(409, "More than 24 hours since they last wrote: WhatsApp only allows a template now.");
        if (!_meta.IsConfigured) return NotConnected<WaMessageDto>();

        string mediaId;
        try { mediaId = await _meta.UploadMediaAsync(data, mime, fileName); }
        catch (WaMetaException ex) { return WorkResult<WaMessageDto>.Fail(ex.HttpStatus, "The file could not be sent: " + ex.Message); }

        var cleanCaption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        var media = new JsonObject { ["id"] = mediaId };
        if (cleanCaption != null && kind != "audio") media["caption"] = cleanCaption;
        if (kind == "document") media["filename"] = fileName;
        var row = new WaMessage
        {
            MessageId = Guid.NewGuid(), Type = kind, Body = cleanCaption, MediaId = mediaId, MediaMime = mime, MediaName = fileName,
            SentByUserId = actor.UserId, CreatedAt = _now(),
        };
        await WaSender.SendAsync(_db, _meta, contact, row, new JsonObject { ["type"] = kind, [kind] = media });
        await AutoAssignAsync(actor, contact);
        return await ResultAsync(row);
    }

    public async Task<WorkResult<WaMessageDto>> SendTemplateAsync(WaActor actor, Guid contactId, WaSendTemplateRequest request)
    {
        if (!actor.CanReply) return NoReply<WaMessageDto>();
        var contact = await _db.WaContacts.FirstOrDefaultAsync(c => c.ContactId == contactId);
        if (contact == null) return NotFound<WaMessageDto>();
        var template = await _db.WaTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.TemplateId == request.TemplateId);
        if (template == null) return WorkResult<WaMessageDto>.Fail(404, "This template does not exist.");
        if (template.Status != "APPROVED") return WorkResult<WaMessageDto>.Fail(409, "Only templates Meta has approved can be sent.");
        // A business-started marketing message needs the person's agreement.
        if (template.Category == "MARKETING" && contact.OptIn == "OptedOut")
            return WorkResult<WaMessageDto>.Fail(409, "This person has opted out of marketing messages.");
        if (!_meta.IsConfigured) return NotConnected<WaMessageDto>();

        var parts = WaTemplateParts.Parse(template.Components, template.ParameterFormat);
        var values = request.Values ?? new();
        string? mediaId = null;
        string? link = string.IsNullOrWhiteSpace(request.HeaderMediaUrl) ? null : request.HeaderMediaUrl.Trim();
        if (link != null && !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return WorkResult<WaMessageDto>.Fail(400, "The picture, video or document link must start with https://.");
        if (parts.HasMediaHeader && link == null)
        {
            // The sample kept with the template, sent to WhatsApp for this message.
            if (template.HeaderMediaKey != null && _files.IsConfigured)
            {
                try
                {
                    await using var stream = await _files.OpenAsync(template.HeaderMediaKey);
                    using var memory = new MemoryStream();
                    await stream.CopyToAsync(memory);
                    var mime = parts.HeaderFormat switch
                    {
                        "IMAGE" => template.HeaderMediaKey.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg",
                        "VIDEO" => "video/mp4",
                        _ => "application/pdf",
                    };
                    mediaId = await _meta.UploadMediaAsync(memory.ToArray(), mime, Path.GetFileName(template.HeaderMediaKey));
                }
                catch (Exception ex) when (ex is WaMetaException or DocStorageException or FileNotFoundException) { mediaId = null; }
            }
            if (mediaId == null)
                return WorkResult<WaMessageDto>.Fail(400, "This template starts with a picture, video or document: give a https link to it.");
        }

        var row = new WaMessage
        {
            MessageId = Guid.NewGuid(), Type = "template", Body = parts.Render(values), TemplateName = template.Name,
            TemplateLanguage = template.Language, SentByUserId = actor.UserId, CreatedAt = _now(),
        };
        await WaSender.SendAsync(_db, _meta, contact, row, new JsonObject
        {
            ["type"] = "template",
            ["template"] = parts.SendPayload(template.Name, template.Language, values, mediaId, link, null),
        });
        await AutoAssignAsync(actor, contact);
        return await ResultAsync(row);
    }

    /// <summary>Whoever answers an unassigned chat takes it.</summary>
    private async Task AutoAssignAsync(WaActor actor, WaContact contact)
    {
        if (contact.AssignedUserId != null || actor.UserId == null) return;
        contact.AssignedUserId = actor.UserId;
        await _db.SaveChangesAsync();
    }

    private async Task<WorkResult<WaMessageDto>> ResultAsync(WaMessage row)
    {
        var names = await WaShared.NamesAsync(_db, new[] { row.SentByUserId });
        var dto = WaShared.Message(row, names);
        return row.Status == "failed"
            ? WorkResult<WaMessageDto>.Fail(502, row.ErrorText ?? "WhatsApp could not send this.")
            : WorkResult<WaMessageDto>.Success(dto);
    }

    private static WorkResult<T> NotConnected<T>() =>
        WorkResult<T>.Fail(503, "The WhatsApp marketing number is not connected on this environment yet.");

    public async Task<WorkResult<WaNoteDto>> AddNoteAsync(WaActor actor, Guid contactId, WaNoteRequest request)
    {
        if (!actor.CanReply || actor.UserId == null) return NoReply<WaNoteDto>();
        var text = (request.Text ?? string.Empty).Trim();
        if (text.Length == 0) return WorkResult<WaNoteDto>.Fail(400, "Write the note first.");
        if (text.Length > 2000) return WorkResult<WaNoteDto>.Fail(400, "A note can be at most 2,000 characters.");
        if (!await _db.WaContacts.AnyAsync(c => c.ContactId == contactId)) return NotFound<WaNoteDto>();
        var note = new WaNote { NoteId = Guid.NewGuid(), ContactId = contactId, UserId = actor.UserId.Value, Text = text, CreatedAt = _now() };
        _db.WaNotes.Add(note);
        await _db.SaveChangesAsync();
        return WorkResult<WaNoteDto>.Success(new WaNoteDto { NoteId = note.NoteId, UserId = note.UserId, Author = actor.Name, Text = text, CreatedAt = note.CreatedAt });
    }

    public async Task<WorkResult<WaChatDto>> AssignAsync(WaActor actor, Guid contactId, WaAssignRequest request)
    {
        if (!actor.CanReply) return NoReply<WaChatDto>();
        var contact = await _db.WaContacts.FirstOrDefaultAsync(c => c.ContactId == contactId);
        if (contact == null) return NotFound<WaChatDto>();
        if (request.UserId is { } to && !await _db.CmsUsers.AnyAsync(u => u.UserId == to && u.IsActive))
            return WorkResult<WaChatDto>.Fail(400, "That person cannot be given chats.");
        contact.AssignedUserId = request.UserId;
        contact.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var names = await WaShared.NamesAsync(_db, new[] { contact.AssignedUserId });
        return WorkResult<WaChatDto>.Success(WaShared.Chat(contact, names, _now()));
    }

    public async Task<WorkResult<WaChatDto>> SetStatusAsync(WaActor actor, Guid contactId, WaChatStatusRequest request)
    {
        if (!actor.CanReply) return NoReply<WaChatDto>();
        if (request.Status is not ("Open" or "Closed")) return WorkResult<WaChatDto>.Fail(400, "A chat is Open or Closed.");
        var contact = await _db.WaContacts.FirstOrDefaultAsync(c => c.ContactId == contactId);
        if (contact == null) return NotFound<WaChatDto>();
        contact.ChatStatus = request.Status;
        if (request.Status == "Closed") contact.UnreadCount = 0;
        contact.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var names = await WaShared.NamesAsync(_db, new[] { contact.AssignedUserId });
        return WorkResult<WaChatDto>.Success(WaShared.Chat(contact, names, _now()));
    }

    public async Task<WorkResult<WaMediaFile>> MediaAsync(WaActor actor, Guid messageId)
    {
        var message = await _db.WaMessages.AsNoTracking().FirstOrDefaultAsync(m => m.MessageId == messageId);
        if (message?.MediaId == null) return WorkResult<WaMediaFile>.Fail(404, "This message has no file.");
        try
        {
            var file = await _meta.DownloadMediaAsync(message.MediaId);
            if (message.MediaMime != null) file.Mime = message.MediaMime;
            return WorkResult<WaMediaFile>.Success(file);
        }
        catch (WaMetaException ex)
        {
            return WorkResult<WaMediaFile>.Fail(ex.HttpStatus == 400 ? 404 : ex.HttpStatus,
                ex.HttpStatus == 400 ? "WhatsApp keeps files for 30 days: this one is gone." : ex.Message);
        }
        catch (WaNotConnectedException) { return NotConnected<WaMediaFile>(); }
    }

    public Task<int> UnreadAsync() => _db.WaContacts.CountAsync(c => c.UnreadCount > 0 && c.ChatStatus == "Open");
}
