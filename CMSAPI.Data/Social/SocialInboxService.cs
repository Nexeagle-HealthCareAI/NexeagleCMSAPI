using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Data.WhatsApp;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Social;

/// <summary>
/// Social → Inbox: Messenger and Instagram direct messages, and comments on our posts. Free replies within 24
/// hours of the person's last message; after that, up to 7 days, as a human agent (Meta's HUMAN_AGENT tag).
/// </summary>
public class SocialInboxService : ISocialInboxService
{
    private readonly CmsDbContext _db;
    private readonly IMetaSocial _meta;
    private readonly SocialKeys _keys;
    private readonly Func<DateTime> _now;

    public SocialInboxService(CmsDbContext db, IMetaSocial meta, SocialKeys keys) : this(db, meta, keys, () => DateTime.UtcNow) { }
    internal SocialInboxService(CmsDbContext db, IMetaSocial meta, SocialKeys keys, Func<DateTime> now)
    {
        _db = db;
        _meta = meta;
        _keys = keys;
        _now = now;
    }

    private const string NoReply = "Replying needs \"Reply to comments and messages\".";
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);
    public static readonly TimeSpan HumanAgent = TimeSpan.FromDays(7);
    private const long MaxFile = 25L * 1024 * 1024;

    // ── mapping ──────────────────────────────────────────────────────────────

    internal static SocialConversationDto Conversation(SocialContact c, IReadOnlyDictionary<Guid, string> names, DateTime now) => new()
    {
        Kind = "dm", Id = c.ContactId, Channel = c.Channel, Name = c.Name ?? (c.Username != null ? "@" + c.Username : SocialShared.Channel(c.Channel) + " user"),
        Handle = c.Username != null ? "@" + c.Username : null, Preview = c.LastMessagePreview, LastAt = c.LastMessageAt, Unread = c.UnreadCount,
        Status = c.ChatStatus, AssignedUserId = c.AssignedUserId, AssignedName = c.AssignedUserId is { } u ? names.GetValueOrDefault(u) : null,
        WindowClosesAt = c.LastInboundAt is { } t && now - t < Window ? t + Window : null,
        HumanAgentUntil = c.LastInboundAt is { } h && now - h < HumanAgent ? h + HumanAgent : null,
        LeadId = c.LeadId,
    };

    internal static SocialConversationDto Conversation(SocialComment c, IReadOnlyDictionary<Guid, string> names, Guid? leadId) => new()
    {
        Kind = "comment", Id = c.CommentId, Channel = c.Channel, Name = c.AuthorName ?? "Someone", Handle = c.Channel == "instagram" && c.AuthorName != null ? "@" + c.AuthorName : null,
        Preview = SocialShared.Preview(c.Text), LastAt = c.CreatedAt, Unread = c.Status == "Open" ? 1 : 0, Status = c.Status == "Open" ? "Open" : "Closed",
        AssignedUserId = c.AssignedUserId, AssignedName = c.AssignedUserId is { } u ? names.GetValueOrDefault(u) : null, LeadId = leadId,
    };

    internal static async Task<Dictionary<Guid, Guid>> CommentLeadsAsync(CmsDbContext db, IEnumerable<Guid> commentIds)
    {
        var ids = commentIds.ToList();
        if (ids.Count == 0) return new();
        return (await db.SocialLeads.AsNoTracking().Where(l => l.CommentId != null && ids.Contains(l.CommentId.Value)).Select(l => new { l.CommentId, l.LeadId }).ToListAsync())
            .GroupBy(x => x.CommentId!.Value).ToDictionary(g => g.Key, g => g.First().LeadId);
    }

    internal static SocialCommentDto Comment(SocialComment c, SocialPost? post, IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<Guid, Guid> leads, DateTime now) => new()
    {
        CommentId = c.CommentId, Channel = c.Channel, MetaId = c.MetaId, PostMetaId = c.PostMetaId, PostId = post?.PostId,
        PostCaption = post == null ? null : SocialShared.Preview(post.Caption, 100), PostPermalink = post?.Permalink, ParentMetaId = c.ParentMetaId,
        AuthorName = c.AuthorName, Text = c.Text, FromUs = c.FromUs, Hidden = c.Hidden, Status = c.Status,
        RepliedBy = c.RepliedByUserId is { } u ? names.GetValueOrDefault(u) : null, PrivateReplyAt = c.PrivateReplyAt,
        CanPrivateReply = !c.FromUs && c.PrivateReplyAt == null && now - c.CreatedAt < TimeSpan.FromDays(7),
        LeadId = leads.TryGetValue(c.CommentId, out var l) ? l : null, CreatedAt = c.CreatedAt,
    };

    /// <summary>Top-level comments with their replies under them.</summary>
    internal static List<SocialCommentDto> Tree(List<SocialComment> all, SocialPost? post, IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<Guid, Guid> leads, DateTime now)
    {
        var tops = all.Where(c => c.ParentMetaId == null || all.All(p => p.MetaId != c.ParentMetaId)).Select(c => Comment(c, post, names, leads, now)).ToList();
        foreach (var t in tops)
            t.Replies = all.Where(c => c.ParentMetaId == t.MetaId).OrderBy(c => c.CreatedAt).Select(c => Comment(c, post, names, leads, now)).ToList();
        return tops;
    }

    private static SocialMessageDto Message(SocialMessage m, IReadOnlyDictionary<Guid, string> names) => new()
    {
        MessageId = m.MessageId, Direction = m.Direction, Text = m.Text, Status = m.Status, Error = m.Error, HumanAgent = m.HumanAgent, CreatedAt = m.CreatedAt,
        Attachments = SocialShared.Read<List<SocialAttachment>>(m.AttachmentsJson) ?? new(),
        SentBy = m.SentByUserId is { } u ? names.GetValueOrDefault(u) : null,
    };

    // ── list and thread ──────────────────────────────────────────────────────

    public async Task<WorkResult<List<SocialConversationDto>>> ConversationsAsync(SocialActor actor, SocialConversationQuery q)
    {
        var now = _now();
        var view = q.View ?? "open";
        var search = q.Search?.Trim();
        var result = new List<SocialConversationDto>();

        if (q.Channel is null or "" or "messenger" or "instagram")
        {
            var dms = _db.SocialContacts.AsNoTracking().Where(c => c.AccountId == q.AccountId && c.LastMessageAt != null);
            if (q.Channel is "messenger" or "instagram") dms = dms.Where(c => c.Channel == q.Channel);
            dms = view switch
            {
                "closed" => dms.Where(c => c.ChatStatus == "Closed"),
                "mine" => dms.Where(c => c.ChatStatus == "Open" && c.AssignedUserId == actor.UserId),
                "unassigned" => dms.Where(c => c.ChatStatus == "Open" && c.AssignedUserId == null),
                "unread" => dms.Where(c => c.UnreadCount > 0),
                "all" => dms,
                _ => dms.Where(c => c.ChatStatus == "Open"),
            };
            if (!string.IsNullOrEmpty(search)) dms = dms.Where(c => (c.Name ?? "").Contains(search) || (c.Username ?? "").Contains(search));
            var list = await dms.OrderByDescending(c => c.LastMessageAt).Take(300).ToListAsync();
            var names = await WaShared.NamesAsync(_db, list.Select(c => c.AssignedUserId));
            result.AddRange(list.Select(c => Conversation(c, names, now)));
        }

        if (q.Channel is null or "" or "comments")
        {
            var comments = _db.SocialComments.AsNoTracking().Where(c => c.AccountId == q.AccountId && !c.FromUs && !c.Deleted);
            comments = view switch
            {
                "closed" => comments.Where(c => c.Status == "Done"),
                "mine" => comments.Where(c => c.Status == "Open" && c.AssignedUserId == actor.UserId),
                "unassigned" or "unread" or "open" or null => comments.Where(c => c.Status == "Open" && (view != "unassigned" || c.AssignedUserId == null)),
                _ => comments,
            };
            if (!string.IsNullOrEmpty(search)) comments = comments.Where(c => (c.AuthorName ?? "").Contains(search) || (c.Text ?? "").Contains(search));
            var list = await comments.OrderByDescending(c => c.CreatedAt).Take(300).ToListAsync();
            var names = await WaShared.NamesAsync(_db, list.Select(c => c.AssignedUserId));
            var leads = await CommentLeadsAsync(_db, list.Select(c => c.CommentId));
            result.AddRange(list.Select(c => Conversation(c, names, leads.TryGetValue(c.CommentId, out var l) ? l : null)));
        }

        return WorkResult<List<SocialConversationDto>>.Success(result.OrderByDescending(c => c.LastAt).Take(300).ToList());
    }

    public async Task<WorkResult<SocialThreadDto>> ThreadAsync(SocialActor actor, Guid contactId)
    {
        var c = await _db.SocialContacts.FirstOrDefaultAsync(x => x.ContactId == contactId);
        if (c == null) return WorkResult<SocialThreadDto>.Fail(404, "This conversation does not exist.");
        if (c.UnreadCount > 0) { c.UnreadCount = 0; await _db.SaveChangesAsync(); }
        var messages = await _db.SocialMessages.AsNoTracking().Where(m => m.ContactId == contactId).OrderByDescending(m => m.CreatedAt).Take(500).ToListAsync();
        messages.Reverse();
        var notes = await _db.SocialNotes.AsNoTracking().Where(n => n.ContactId == contactId).OrderBy(n => n.CreatedAt).ToListAsync();
        var names = await WaShared.NamesAsync(_db, messages.Select(m => m.SentByUserId).Concat(notes.Select(n => (Guid?)n.UserId)).Append(c.AssignedUserId));
        var now = _now();
        return WorkResult<SocialThreadDto>.Success(new SocialThreadDto
        {
            Conversation = Conversation(c, names, now),
            Messages = messages.Select(m => Message(m, names)).ToList(),
            Notes = notes.Select(n => new SocialNoteDto { NoteId = n.NoteId, Author = names.GetValueOrDefault(n.UserId) ?? "Someone", Text = n.Text, CreatedAt = n.CreatedAt }).ToList(),
            WindowOpen = c.LastInboundAt is { } t && now - t < Window,
            HumanAgentOpen = c.LastInboundAt is { } h && now - h < HumanAgent,
        });
    }

    // ── direct messages ──────────────────────────────────────────────────────

    public async Task<WorkResult<SocialMessageDto>> SendAsync(SocialActor actor, Guid contactId, string? text, Stream? file, string? fileName, string? mime)
    {
        if (!actor.CanReply) return WorkResult<SocialMessageDto>.Fail(403, NoReply);
        var c = await _db.SocialContacts.FirstOrDefaultAsync(x => x.ContactId == contactId);
        if (c == null) return WorkResult<SocialMessageDto>.Fail(404, "This conversation does not exist.");
        text = text?.Trim();
        if (file == null && string.IsNullOrEmpty(text)) return WorkResult<SocialMessageDto>.Fail(400, "Write a message.");
        if (text?.Length > 2000) return WorkResult<SocialMessageDto>.Fail(400, "A message can be at most 2,000 characters.");
        var now = _now();
        var since = c.LastInboundAt is { } t ? now - t : TimeSpan.MaxValue;
        if (since >= HumanAgent)
            return WorkResult<SocialMessageDto>.Fail(409, "More than 7 days since their last message: Meta allows no reply until they write again.");
        var asAgent = since >= Window;

        var loaded = await _keys.LoadAsync(c.AccountId);
        if (loaded is not { } k) return WorkResult<SocialMessageDto>.Fail(503, "This account is not connected (Social → Setup).");

        byte[]? data = null;
        if (file != null)
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            if (ms.Length > MaxFile) return WorkResult<SocialMessageDto>.Fail(400, "Files up to 25 MB can be sent.");
            data = ms.ToArray();
        }

        var m = new SocialMessage
        {
            MessageId = Guid.NewGuid(), ContactId = c.ContactId, Direction = "Out", Text = text, Status = "sent", HumanAgent = asAgent, SentByUserId = actor.UserId, CreatedAt = now,
            AttachmentsJson = data == null ? null : SocialShared.Write(new List<SocialAttachment> { new() { Type = (mime ?? "").StartsWith("image/") ? "image" : "file", Name = fileName } }),
        };
        try
        {
            if (data != null) m.MetaId = await _meta.SendAttachmentAsync(k.Creds, c.Channel, c.ExternalId, data, mime ?? "application/octet-stream", fileName ?? "file", asAgent);
            if (!string.IsNullOrEmpty(text))
            {
                var id = await _meta.SendMessageAsync(k.Creds, c.Channel, c.ExternalId, text, asAgent);
                m.MetaId ??= id;
            }
        }
        catch (MetaSocialException ex)
        {
            var why = asAgent && ex.Code is 10 or 200 or 551
                ? "Meta refused a reply after 24 hours: the Human Agent feature is not approved for this app yet. " + ex.Message
                : ex.Message;
            return WorkResult<SocialMessageDto>.Fail(ex.HttpStatus == 401 ? 503 : 502, why);
        }
        _db.SocialMessages.Add(m);
        c.LastMessageAt = now;
        c.LastMessagePreview = SocialShared.Preview(text ?? fileName ?? "File", 200);
        c.AssignedUserId ??= actor.UserId;
        await _db.SaveChangesAsync();
        var names = await WaShared.NamesAsync(_db, new Guid?[] { actor.UserId });
        return WorkResult<SocialMessageDto>.Success(Message(m, names));
    }

    public async Task<WorkResult<SocialNoteDto>> NoteAsync(SocialActor actor, Guid contactId, string text)
    {
        if (!actor.CanReply) return WorkResult<SocialNoteDto>.Fail(403, NoReply);
        text = (text ?? string.Empty).Trim();
        if (text.Length is 0 or > 2000) return WorkResult<SocialNoteDto>.Fail(400, "Write a note (up to 2,000 characters).");
        if (!await _db.SocialContacts.AnyAsync(c => c.ContactId == contactId)) return WorkResult<SocialNoteDto>.Fail(404, "This conversation does not exist.");
        var n = new SocialNote { NoteId = Guid.NewGuid(), ContactId = contactId, UserId = actor.UserId, Text = text, CreatedAt = _now() };
        _db.SocialNotes.Add(n);
        await _db.SaveChangesAsync();
        return WorkResult<SocialNoteDto>.Success(new SocialNoteDto { NoteId = n.NoteId, Author = actor.Name, Text = n.Text, CreatedAt = n.CreatedAt });
    }

    public async Task<WorkResult<SocialConversationDto>> AssignAsync(SocialActor actor, Guid contactId, Guid? userId)
    {
        if (!actor.CanReply) return WorkResult<SocialConversationDto>.Fail(403, NoReply);
        if (userId != null && !await _db.CmsUsers.AnyAsync(u => u.UserId == userId)) return WorkResult<SocialConversationDto>.Fail(400, "That person does not exist.");
        var c = await _db.SocialContacts.FirstOrDefaultAsync(x => x.ContactId == contactId);
        if (c != null)
        {
            c.AssignedUserId = userId;
            await _db.SaveChangesAsync();
            return WorkResult<SocialConversationDto>.Success(Conversation(c, await WaShared.NamesAsync(_db, new[] { userId }), _now()));
        }
        var cm = await _db.SocialComments.FirstOrDefaultAsync(x => x.CommentId == contactId);
        if (cm == null) return WorkResult<SocialConversationDto>.Fail(404, "This conversation does not exist.");
        cm.AssignedUserId = userId;
        await _db.SaveChangesAsync();
        return WorkResult<SocialConversationDto>.Success(Conversation(cm, await WaShared.NamesAsync(_db, new[] { userId }), null));
    }

    public async Task<WorkResult<SocialConversationDto>> StatusAsync(SocialActor actor, Guid contactId, string status)
    {
        if (!actor.CanReply) return WorkResult<SocialConversationDto>.Fail(403, NoReply);
        if (status is not ("Open" or "Closed")) return WorkResult<SocialConversationDto>.Fail(400, "Open or Closed.");
        var c = await _db.SocialContacts.FirstOrDefaultAsync(x => x.ContactId == contactId);
        if (c == null) return WorkResult<SocialConversationDto>.Fail(404, "This conversation does not exist.");
        c.ChatStatus = status;
        if (status == "Closed") c.UnreadCount = 0;
        await _db.SaveChangesAsync();
        return WorkResult<SocialConversationDto>.Success(Conversation(c, await WaShared.NamesAsync(_db, new[] { c.AssignedUserId }), _now()));
    }

    public async Task<int> UnreadAsync(Guid? accountId)
    {
        var dms = _db.SocialContacts.Where(c => c.UnreadCount > 0 && c.ChatStatus == "Open");
        var comments = _db.SocialComments.Where(c => c.Status == "Open" && !c.FromUs && !c.Deleted);
        if (accountId != null) { dms = dms.Where(c => c.AccountId == accountId); comments = comments.Where(c => c.AccountId == accountId); }
        return await dms.CountAsync() + await comments.CountAsync();
    }

    // ── comments ─────────────────────────────────────────────────────────────

    private async Task<SocialCommentDto> CommentDtoAsync(SocialComment c)
    {
        var post = await _db.SocialPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Channel == c.Channel && p.MetaId == c.PostMetaId);
        var replies = await _db.SocialComments.AsNoTracking().Where(r => r.Channel == c.Channel && r.ParentMetaId == c.MetaId && !r.Deleted).OrderBy(r => r.CreatedAt).ToListAsync();
        var names = await WaShared.NamesAsync(_db, replies.Select(r => r.RepliedByUserId).Append(c.RepliedByUserId));
        var leads = await CommentLeadsAsync(_db, new[] { c.CommentId });
        var now = _now();
        var dto = Comment(c, post, names, leads, now);
        dto.Replies = replies.Select(r => Comment(r, post, names, leads, now)).ToList();
        return dto;
    }

    public async Task<WorkResult<SocialCommentDto>> CommentAsync(SocialActor actor, Guid commentId)
    {
        var c = await _db.SocialComments.AsNoTracking().FirstOrDefaultAsync(x => x.CommentId == commentId);
        return c == null ? WorkResult<SocialCommentDto>.Fail(404, "This comment does not exist.") : WorkResult<SocialCommentDto>.Success(await CommentDtoAsync(c));
    }

    public async Task<WorkResult<SocialCommentDto>> ReplyToCommentAsync(SocialActor actor, Guid commentId, SocialCommentReply r)
    {
        if (!actor.CanReply) return WorkResult<SocialCommentDto>.Fail(403, NoReply);
        var c = await _db.SocialComments.FirstOrDefaultAsync(x => x.CommentId == commentId);
        if (c == null) return WorkResult<SocialCommentDto>.Fail(404, "This comment does not exist.");
        var text = (r.Text ?? string.Empty).Trim();
        if (text.Length is 0 or > 2000) return WorkResult<SocialCommentDto>.Fail(400, "Write the reply (up to 2,000 characters).");
        var loaded = await _keys.LoadAsync(c.AccountId);
        if (loaded is not { } k) return WorkResult<SocialCommentDto>.Fail(503, "This account is not connected (Social → Setup).");
        var now = _now();

        try
        {
            if (r.Private)
            {
                if (c.PrivateReplyAt != null) return WorkResult<SocialCommentDto>.Fail(409, "This comment already had its one private reply.");
                if (now - c.CreatedAt >= TimeSpan.FromDays(7)) return WorkResult<SocialCommentDto>.Fail(409, "Private replies are possible only within 7 days of the comment.");
                await _meta.PrivateReplyAsync(k.Creds, c.MetaId, text);
                c.PrivateReplyAt = now;
            }
            else
            {
                // Replies go under the top-level comment (Instagram has one level of replies).
                var target = c.ParentMetaId ?? c.MetaId;
                var id = await _meta.ReplyToCommentAsync(k.Creds, c.Channel, target, text);
                var mine = await SocialCommentStore.UpsertAsync(_db, k.Account, c.Channel, c.PostMetaId, id, target,
                    c.Channel == "facebook" ? k.Account.PageId : k.Account.IgUserId, c.Channel == "facebook" ? k.Account.PageName : k.Account.IgUsername, text, false, now);
                mine.RepliedByUserId = actor.UserId;
            }
        }
        catch (MetaSocialException ex) { return WorkResult<SocialCommentDto>.Fail(502, ex.Message); }

        c.Status = "Done";
        c.RepliedByUserId = actor.UserId;
        c.AssignedUserId ??= actor.UserId;
        await _db.SaveChangesAsync();
        return WorkResult<SocialCommentDto>.Success(await CommentDtoAsync(c), r.Private ? "Sent as a private message." : "Replied.");
    }

    public async Task<WorkResult<SocialCommentDto>> HideCommentAsync(SocialActor actor, Guid commentId, bool hidden)
    {
        if (!actor.CanReply) return WorkResult<SocialCommentDto>.Fail(403, NoReply);
        var c = await _db.SocialComments.FirstOrDefaultAsync(x => x.CommentId == commentId);
        if (c == null) return WorkResult<SocialCommentDto>.Fail(404, "This comment does not exist.");
        var loaded = await _keys.LoadAsync(c.AccountId);
        if (loaded is not { } k) return WorkResult<SocialCommentDto>.Fail(503, "This account is not connected (Social → Setup).");
        try { await _meta.HideCommentAsync(k.Creds, c.Channel, c.MetaId, hidden); }
        catch (MetaSocialException ex) { return WorkResult<SocialCommentDto>.Fail(502, ex.Message); }
        c.Hidden = hidden;
        if (hidden) c.Status = "Done";
        await _db.SaveChangesAsync();
        return WorkResult<SocialCommentDto>.Success(await CommentDtoAsync(c), hidden ? "Hidden: only the person and their friends see it." : "Shown again.");
    }

    public async Task<WorkResult<SocialCommentDto>> CommentDoneAsync(SocialActor actor, Guid commentId, bool done)
    {
        if (!actor.CanReply) return WorkResult<SocialCommentDto>.Fail(403, NoReply);
        var c = await _db.SocialComments.FirstOrDefaultAsync(x => x.CommentId == commentId);
        if (c == null) return WorkResult<SocialCommentDto>.Fail(404, "This comment does not exist.");
        c.Status = done ? "Done" : "Open";
        await _db.SaveChangesAsync();
        return WorkResult<SocialCommentDto>.Success(await CommentDtoAsync(c));
    }
}
