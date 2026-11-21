using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Services;

/// <summary>
/// Comments on documents. See IDocCommentsService. Whether a person may open a document is asked of
/// IDocsService, so sensitive folders hide a document's comments exactly as they hide the document.
/// </summary>
public class DocCommentsService : IDocCommentsService
{
    private const int PreviewLength = 90;
    private readonly CmsDbContext _db;
    private readonly IDocsService _docs;

    public DocCommentsService(CmsDbContext db, IDocsService docs)
    {
        _db = db;
        _docs = docs;
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static WorkResult<T> NotFound<T>(string what) => WorkResult<T>.Fail(404, what + " not found.");

    private static string Preview(string body)
    {
        var flat = body.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flat.Length <= PreviewLength ? flat : flat.Substring(0, PreviewLength) + "…";
    }

    public async Task<WorkResult<List<DocCommentDto>>> GetAsync(DocsActor actor, Guid documentId)
    {
        if (!await _docs.CanSeeDocumentAsync(actor, documentId)) return NotFound<List<DocCommentDto>>("Document");
        var rows = await _db.DocComments.AsNoTracking()
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
        return WorkResult<List<DocCommentDto>>.Success(await BuildAsync(rows));
    }

    public async Task<WorkResult<DocCommentDto>> AddAsync(DocsActor actor, Guid documentId, SaveDocCommentRequest request)
    {
        if (!actor.UserId.HasValue) return WorkResult<DocCommentDto>.Fail(401, "Sign in again.");
        if (!await _docs.CanSeeDocumentAsync(actor, documentId)) return NotFound<DocCommentDto>("Document");
        var me = actor.UserId.Value;

        var body = (request.Body ?? string.Empty).Trim();
        if (body.Length == 0) return WorkResult<DocCommentDto>.Fail(400, "Write a comment first.");
        if (body.Length > DocCommentRules.MaxBody) return WorkResult<DocCommentDto>.Fail(400, $"A comment can be up to {DocCommentRules.MaxBody} characters.");

        DocComment? parent = null;
        if (request.ParentId.HasValue)
        {
            var parentId = request.ParentId.Value;
            parent = await _db.DocComments.FirstOrDefaultAsync(c => c.CommentId == parentId && c.DocumentId == documentId && !c.IsDeleted);
            if (parent == null) return WorkResult<DocCommentDto>.Fail(400, "The comment you are answering is no longer there.");
            // Threads are one level deep: an answer to a reply joins the same thread.
            if (parent.ParentId.HasValue)
            {
                var rootId = parent.ParentId.Value;
                parent = await _db.DocComments.FirstOrDefaultAsync(c => c.CommentId == rootId);
                if (parent == null) return WorkResult<DocCommentDto>.Fail(400, "The comment you are answering is no longer there.");
            }
        }

        var quote = parent == null ? (request.Quote ?? string.Empty).Trim() : string.Empty;
        if (quote.Length > DocCommentRules.MaxQuote) quote = quote.Substring(0, DocCommentRules.MaxQuote);

        var now = DateTime.UtcNow;
        var comment = new DocComment
        {
            CommentId = Guid.NewGuid(), DocumentId = documentId, ParentId = parent?.CommentId, AuthorUserId = me,
            Body = body, Quote = quote.Length == 0 ? null : quote, CreatedAt = now,
        };
        _db.DocComments.Add(comment);

        // An answer brings a resolved thread back.
        if (parent != null && parent.IsResolved)
        {
            parent.IsResolved = false;
            parent.ResolvedByUserId = null;
            parent.ResolvedAt = null;
        }

        var doc = await _db.WorkDocuments.AsNoTracking().FirstAsync(d => d.DocumentId == documentId);
        var mentions = await TaggableAsync(documentId, request.MentionUserIds, me);
        var told = new HashSet<Guid>();
        foreach (var userId in mentions)
        {
            _db.DocCommentMentions.Add(new DocCommentMention { CommentId = comment.CommentId, UserId = userId });
            Notify(userId, "Mention", comment, now);
            told.Add(userId);
        }
        // The person who started the thread hears about an answer; the document's author about a new thread.
        var also = parent != null ? parent.AuthorUserId : doc.CreatedByUserId;
        if (also.HasValue && also.Value != me && !told.Contains(also.Value)
            && await _docs.CanSeeDocumentAsync(new DocsActor { UserId = also.Value }, documentId))
        {
            Notify(also.Value, parent != null ? "Reply" : "Comment", comment, now);
        }

        _db.DocAccessLogs.Add(new DocAccessLog
        {
            LogId = Guid.NewGuid(), DocumentId = documentId, FolderId = doc.FolderId, UserId = me,
            Action = "comment", Detail = parent != null ? "reply" : null, CreatedAt = now,
        });

        await _db.SaveChangesAsync();
        return WorkResult<DocCommentDto>.Success((await BuildAsync(new List<DocComment> { comment }))[0]);
    }

    /// <summary>
    /// The people from the list who can be tagged: active, not the author, and able to open the
    /// document as an ordinary member. Anyone else is left out rather than refused.
    /// </summary>
    private async Task<List<Guid>> TaggableAsync(Guid documentId, List<Guid>? wanted, Guid me)
    {
        var ids = (wanted ?? new List<Guid>()).Where(id => id != me).Distinct().ToList();
        if (ids.Count == 0) return ids;
        var active = await _db.CmsUsers.AsNoTracking().Where(u => ids.Contains(u.UserId) && u.IsActive).Select(u => u.UserId).ToListAsync();
        var allowed = new List<Guid>();
        foreach (var userId in active)
        {
            if (await _docs.CanSeeDocumentAsync(new DocsActor { UserId = userId }, documentId)) allowed.Add(userId);
        }
        return allowed;
    }

    private void Notify(Guid userId, string kind, DocComment comment, DateTime at) =>
        _db.DocNotifications.Add(new DocNotification
        {
            NotificationId = Guid.NewGuid(), UserId = userId, Kind = kind, DocumentId = comment.DocumentId,
            CommentId = comment.CommentId, ActorUserId = comment.AuthorUserId, CreatedAt = at,
        });

    public async Task<WorkResult<DocCommentDto>> EditAsync(DocsActor actor, Guid commentId, SaveDocCommentRequest request)
    {
        if (!actor.UserId.HasValue) return WorkResult<DocCommentDto>.Fail(401, "Sign in again.");
        var comment = await _db.DocComments.FirstOrDefaultAsync(c => c.CommentId == commentId && !c.IsDeleted);
        if (comment == null || !await _docs.CanSeeDocumentAsync(actor, comment.DocumentId)) return NotFound<DocCommentDto>("Comment");
        var me = actor.UserId.Value;
        if (comment.AuthorUserId != me) return WorkResult<DocCommentDto>.Fail(403, "You can only change your own comments.");

        var body = (request.Body ?? string.Empty).Trim();
        if (body.Length == 0) return WorkResult<DocCommentDto>.Fail(400, "Write a comment first.");
        if (body.Length > DocCommentRules.MaxBody) return WorkResult<DocCommentDto>.Fail(400, $"A comment can be up to {DocCommentRules.MaxBody} characters.");

        var now = DateTime.UtcNow;
        comment.Body = body;
        comment.EditedAt = now;

        // People newly tagged by the change are told; nobody is told twice.
        var wanted = await TaggableAsync(comment.DocumentId, request.MentionUserIds, me);
        var current = await _db.DocCommentMentions.Where(m => m.CommentId == commentId).ToListAsync();
        foreach (var gone in current.Where(m => !wanted.Contains(m.UserId))) _db.DocCommentMentions.Remove(gone);
        foreach (var userId in wanted.Where(id => current.All(m => m.UserId != id)))
        {
            _db.DocCommentMentions.Add(new DocCommentMention { CommentId = commentId, UserId = userId });
            Notify(userId, "Mention", comment, now);
        }

        await _db.SaveChangesAsync();
        return WorkResult<DocCommentDto>.Success((await BuildAsync(new List<DocComment> { comment }))[0]);
    }

    public async Task<WorkResult<bool>> DeleteAsync(DocsActor actor, Guid commentId)
    {
        var comment = await _db.DocComments.FirstOrDefaultAsync(c => c.CommentId == commentId && !c.IsDeleted);
        if (comment == null || !await _docs.CanSeeDocumentAsync(actor, comment.DocumentId)) return NotFound<bool>("Comment");
        if (comment.AuthorUserId != actor.UserId && !actor.CanDelete) return WorkResult<bool>.Fail(403, "You can only remove your own comments.");

        comment.IsDeleted = true;
        // What it told people about is gone too.
        foreach (var n in await _db.DocNotifications.Where(n => n.CommentId == commentId && !n.IsRead).ToListAsync()) n.IsRead = true;
        await _db.SaveChangesAsync();
        return WorkResult<bool>.Success(true, "Comment removed.");
    }

    public async Task<WorkResult<DocCommentDto>> ResolveAsync(DocsActor actor, Guid commentId, bool isResolved)
    {
        if (!actor.UserId.HasValue) return WorkResult<DocCommentDto>.Fail(401, "Sign in again.");
        var comment = await _db.DocComments.FirstOrDefaultAsync(c => c.CommentId == commentId && !c.IsDeleted);
        if (comment == null || !await _docs.CanSeeDocumentAsync(actor, comment.DocumentId)) return NotFound<DocCommentDto>("Comment");
        if (comment.ParentId.HasValue) return WorkResult<DocCommentDto>.Fail(400, "Resolve the first comment of the thread.");

        comment.IsResolved = isResolved;
        comment.ResolvedByUserId = isResolved ? actor.UserId : null;
        comment.ResolvedAt = isResolved ? DateTime.UtcNow : (DateTime?)null;
        await _db.SaveChangesAsync();
        return WorkResult<DocCommentDto>.Success((await BuildAsync(new List<DocComment> { comment }))[0]);
    }

    private async Task<List<DocCommentDto>> BuildAsync(List<DocComment> rows)
    {
        var ids = rows.Select(c => c.CommentId).ToList();
        var mentions = await _db.DocCommentMentions.AsNoTracking().Where(m => ids.Contains(m.CommentId)).ToListAsync();
        var people = new List<Guid>();
        foreach (var c in rows)
        {
            if (c.AuthorUserId.HasValue) people.Add(c.AuthorUserId.Value);
            if (c.ResolvedByUserId.HasValue) people.Add(c.ResolvedByUserId.Value);
        }
        var names = await NamesAsync(people);

        return rows.Select(c =>
        {
            var dto = new DocCommentDto
            {
                CommentId = c.CommentId,
                DocumentId = c.DocumentId,
                ParentId = c.ParentId,
                AuthorUserId = c.AuthorUserId,
                AuthorName = c.AuthorUserId.HasValue && names.TryGetValue(c.AuthorUserId.Value, out var author) ? author : "Someone",
                IsDeleted = c.IsDeleted,
                IsResolved = c.IsResolved,
                ResolvedByName = c.ResolvedByUserId.HasValue && names.TryGetValue(c.ResolvedByUserId.Value, out var resolver) ? resolver : null,
                ResolvedAt = c.ResolvedAt.HasValue ? Utc(c.ResolvedAt.Value) : (DateTime?)null,
                CreatedAt = Utc(c.CreatedAt),
            };
            // A removed comment keeps its place in the thread and its author, nothing else.
            if (c.IsDeleted) return dto;

            dto.Body = c.Body;
            dto.Quote = c.Quote;
            dto.IsEdited = c.EditedAt.HasValue;
            dto.MentionUserIds = mentions.Where(m => m.CommentId == c.CommentId).Select(m => m.UserId).ToList();
            return dto;
        }).ToList();
    }

    // ── Notifications ───────────────────────────────────────────────────────────────────────────

    public async Task<List<DocNotificationDto>> GetNotificationsAsync(DocsActor actor)
    {
        if (!actor.UserId.HasValue) return new List<DocNotificationDto>();
        var me = actor.UserId.Value;
        var rows = await _db.DocNotifications.AsNoTracking()
            .Where(n => n.UserId == me)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync();
        if (rows.Count == 0) return new List<DocNotificationDto>();

        var commentIds = rows.Select(n => n.CommentId).Distinct().ToList();
        var comments = await _db.DocComments.AsNoTracking().Where(c => commentIds.Contains(c.CommentId)).ToDictionaryAsync(c => c.CommentId, c => c);
        var docIds = rows.Select(n => n.DocumentId).Distinct().ToList();
        var docs = await _db.WorkDocuments.AsNoTracking().Where(d => docIds.Contains(d.DocumentId) && !d.IsDeleted).ToDictionaryAsync(d => d.DocumentId, d => d.Name);
        var names = await NamesAsync(rows.Where(n => n.ActorUserId.HasValue).Select(n => n.ActorUserId!.Value));

        // A document moved into a folder this person cannot open since then is left out.
        var visible = new HashSet<Guid>();
        foreach (var docId in docs.Keys)
        {
            if (await _docs.CanSeeDocumentAsync(actor, docId)) visible.Add(docId);
        }

        return rows.Where(n => visible.Contains(n.DocumentId)).Select(n =>
        {
            var comment = comments.TryGetValue(n.CommentId, out var c) ? c : null;
            return new DocNotificationDto
            {
                NotificationId = n.NotificationId,
                Kind = n.Kind,
                DocumentId = n.DocumentId,
                DocumentName = docs[n.DocumentId],
                CommentId = n.CommentId,
                ActorName = n.ActorUserId.HasValue && names.TryGetValue(n.ActorUserId.Value, out var name) ? name : "Someone",
                Text = comment == null || comment.IsDeleted ? "Comment removed" : Preview(comment.Body),
                IsRead = n.IsRead,
                CreatedAt = Utc(n.CreatedAt),
            };
        }).ToList();
    }

    public async Task MarkNotificationsReadAsync(Guid userId, List<Guid> notificationIds)
    {
        var q = _db.DocNotifications.Where(n => n.UserId == userId && !n.IsRead);
        if (notificationIds != null && notificationIds.Count > 0) q = q.Where(n => notificationIds.Contains(n.NotificationId));
        foreach (var n in await q.ToListAsync()) n.IsRead = true;
        await _db.SaveChangesAsync();
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> userIds)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        return await _db.CmsUsers.AsNoTracking().Where(u => ids.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName);
    }
}
