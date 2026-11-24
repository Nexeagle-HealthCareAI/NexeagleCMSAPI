using System.Text.RegularExpressions;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Services;

/// <summary>Tagging in work items. See IWorkMentionService.</summary>
public class WorkMentionService : IWorkMentionService
{
    private const string PlaceComment = WorkMentionPlaces.Comment;
    private const string PlaceDescription = WorkMentionPlaces.Description;
    private const string PlaceBlocked = WorkMentionPlaces.Blocked;
    private const int ExcerptLength = 160;

    private readonly CmsDbContext _db;
    private readonly IWorkAccessService _access;

    public WorkMentionService(CmsDbContext db, IWorkAccessService access)
    {
        _db = db;
        _access = access;
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public async Task<List<Guid>> TaggableAsync(Guid workItemId, Guid? me)
    {
        var item = await _db.WorkItems.AsNoTracking().Where(i => i.WorkItemId == workItemId && !i.IsDeleted).Select(i => new { i.ProjectId }).FirstOrDefaultAsync();
        if (item == null) return new List<Guid>();
        var restricted = await _db.WorkProjects.AsNoTracking().AnyAsync(p => p.ProjectId == item.ProjectId && p.IsRestricted);
        // The same people the board's chat can tag: an open board, everyone active; members-only, its members.
        var ids = restricted
            ? await (from m in _db.WorkProjectMembers.AsNoTracking()
                     join u in _db.CmsUsers.AsNoTracking() on m.UserId equals u.UserId
                     where m.ProjectId == item.ProjectId && u.IsActive
                     select u.UserId).ToListAsync()
            : await _db.CmsUsers.AsNoTracking().Where(u => u.IsActive).Select(u => u.UserId).ToListAsync();
        return ids.Where(id => id != me).Distinct().ToList();
    }

    /// <summary>The wanted people who may be tagged here and whose "@Name" is in the text.</summary>
    private async Task<Dictionary<Guid, string>> CheckedAsync(Guid workItemId, string? text, List<Guid>? wanted, Guid? actor)
    {
        var ids = (wanted ?? new List<Guid>()).Where(id => id != actor).Distinct().ToList();
        if (ids.Count == 0 || string.IsNullOrWhiteSpace(text)) return new Dictionary<Guid, string>();
        var body = text;
        var taggable = (await TaggableAsync(workItemId, actor)).ToHashSet();
        var names = await _db.CmsUsers.AsNoTracking().Where(u => ids.Contains(u.UserId)).Select(u => new { u.UserId, u.FullName }).ToListAsync();
        return names
            .Where(n => taggable.Contains(n.UserId) && body.Contains("@" + n.FullName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(n => n.UserId, n => n.FullName);
    }

    /// <summary>The words around "@Name", without markdown marks, for the notification.</summary>
    public static string Excerpt(string text, string name)
    {
        var flat = Regex.Replace(text, @"[*_#`>~\[\]]", string.Empty);
        flat = Regex.Replace(flat, @"\s+", " ").Trim();
        var at = flat.IndexOf("@" + name, StringComparison.OrdinalIgnoreCase);
        if (at < 0 || flat.Length <= ExcerptLength) return flat.Length <= ExcerptLength ? flat : flat[..ExcerptLength] + "…";
        var start = Math.Max(0, at - 50);
        var length = Math.Min(ExcerptLength, flat.Length - start);
        return (start > 0 ? "…" : string.Empty) + flat.Substring(start, length).Trim() + (start + length < flat.Length ? "…" : string.Empty);
    }

    private void Notify(Guid userId, Guid workItemId, string place, Guid? commentId, Guid? actor, string excerpt, DateTime at) =>
        _db.WorkItemNotifications.Add(new WorkItemNotification
        {
            NotificationId = Guid.NewGuid(), UserId = userId, Kind = "Mention", WorkItemId = workItemId, Place = place,
            CommentId = commentId, ActorUserId = actor, Excerpt = excerpt.Length > 300 ? excerpt[..300] : excerpt, CreatedAt = at,
        });

    public async Task<List<Guid>> TagCommentAsync(Guid workItemId, Guid commentId, string body, List<Guid>? wanted, Guid? actor)
    {
        var tagged = await CheckedAsync(workItemId, body, wanted, actor);
        if (tagged.Count == 0) return new List<Guid>();
        var now = DateTime.UtcNow;
        foreach (var (userId, name) in tagged)
        {
            _db.WorkItemMentions.Add(new WorkItemMention { MentionId = Guid.NewGuid(), WorkItemId = workItemId, Place = PlaceComment, CommentId = commentId, UserId = userId, CreatedAt = now });
            Notify(userId, workItemId, PlaceComment, commentId, actor, Excerpt(body, name), now);
        }
        await _db.SaveChangesAsync();
        return tagged.Keys.ToList();
    }

    public async Task TagTextAsync(Guid workItemId, string place, string? text, List<Guid>? wanted, Guid? actor)
    {
        if (wanted == null || (place != PlaceDescription && place != PlaceBlocked)) return;
        var tagged = await CheckedAsync(workItemId, text, wanted, actor);
        var current = await _db.WorkItemMentions.Where(m => m.WorkItemId == workItemId && m.Place == place).ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var gone in current.Where(m => !tagged.ContainsKey(m.UserId))) _db.WorkItemMentions.Remove(gone);
        foreach (var (userId, name) in tagged.Where(t => current.All(m => m.UserId != t.Key)))
        {
            _db.WorkItemMentions.Add(new WorkItemMention { MentionId = Guid.NewGuid(), WorkItemId = workItemId, Place = place, UserId = userId, CreatedAt = now });
            Notify(userId, workItemId, place, null, actor, Excerpt(text ?? string.Empty, name), now);
        }
        await _db.SaveChangesAsync();
    }

    public async Task FillAsync(WorkItemDetailDto item)
    {
        if (item.Comments.Count == 0) return;
        var ids = item.Comments.Select(c => c.CommentId).ToList();
        var rows = await _db.WorkItemMentions.AsNoTracking()
            .Where(m => m.WorkItemId == item.WorkItemId && m.Place == PlaceComment && m.CommentId != null && ids.Contains(m.CommentId.Value))
            .Select(m => new { m.CommentId, m.UserId })
            .ToListAsync();
        foreach (var c in item.Comments) c.MentionUserIds = rows.Where(r => r.CommentId == c.CommentId).Select(r => r.UserId).Distinct().ToList();
    }

    public async Task CommentRemovedAsync(Guid commentId)
    {
        foreach (var n in await _db.WorkItemNotifications.Where(n => n.CommentId == commentId && !n.IsRead).ToListAsync()) n.IsRead = true;
        await _db.SaveChangesAsync();
    }

    public async Task<List<WorkItemNotificationDto>> GetNotificationsAsync(WorkActor actor)
    {
        if (!actor.UserId.HasValue) return new List<WorkItemNotificationDto>();
        var me = actor.UserId.Value;
        var rows = await _db.WorkItemNotifications.AsNoTracking()
            .Where(n => n.UserId == me)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync();
        if (rows.Count == 0) return new List<WorkItemNotificationDto>();

        var itemIds = rows.Select(n => n.WorkItemId).Distinct().ToList();
        var items = await _db.WorkItems.AsNoTracking().Where(i => itemIds.Contains(i.WorkItemId) && !i.IsDeleted)
            .Select(i => new { i.WorkItemId, i.ProjectId, i.Number, i.Title }).ToListAsync();
        var projectIds = items.Select(i => i.ProjectId).Distinct().ToList();
        var keys = await _db.WorkProjects.AsNoTracking().Where(p => projectIds.Contains(p.ProjectId)).ToDictionaryAsync(p => p.ProjectId, p => p.Key);
        // A board this person was taken off since is left out.
        var closed = await _access.ClosedProjectIdsAsync(actor);
        var byId = items.Where(i => !closed.Contains(i.ProjectId)).ToDictionary(i => i.WorkItemId);

        var commentIds = rows.Where(n => n.CommentId.HasValue).Select(n => n.CommentId!.Value).Distinct().ToList();
        var removed = await _db.WorkItemComments.AsNoTracking().Where(c => commentIds.Contains(c.CommentId) && c.IsDeleted).Select(c => c.CommentId).ToListAsync();
        var actors = rows.Where(n => n.ActorUserId.HasValue).Select(n => n.ActorUserId!.Value).Distinct().ToList();
        var names = await _db.CmsUsers.AsNoTracking().Where(u => actors.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName);

        return rows.Where(n => byId.ContainsKey(n.WorkItemId)).Select(n =>
        {
            var item = byId[n.WorkItemId];
            return new WorkItemNotificationDto
            {
                NotificationId = n.NotificationId,
                Kind = n.Kind,
                Place = n.Place,
                WorkItemId = n.WorkItemId,
                ProjectId = item.ProjectId,
                ItemKey = $"{(keys.TryGetValue(item.ProjectId, out var k) ? k : "?")}-{item.Number}",
                ItemTitle = item.Title,
                CommentId = n.CommentId,
                ActorName = n.ActorUserId.HasValue && names.TryGetValue(n.ActorUserId.Value, out var name) ? name : "Someone",
                Text = n.CommentId.HasValue && removed.Contains(n.CommentId.Value) ? "Comment removed" : n.Excerpt ?? string.Empty,
                IsRead = n.IsRead,
                CreatedAt = Utc(n.CreatedAt),
            };
        }).ToList();
    }

    public async Task MarkNotificationsReadAsync(Guid userId, List<Guid> notificationIds)
    {
        var q = _db.WorkItemNotifications.Where(n => n.UserId == userId && !n.IsRead);
        if (notificationIds != null && notificationIds.Count > 0) q = q.Where(n => notificationIds.Contains(n.NotificationId));
        foreach (var n in await q.ToListAsync()) n.IsRead = true;
        await _db.SaveChangesAsync();
    }
}
