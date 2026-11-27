using CMSAPI.Application.Interfaces;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.Social;

/// <summary>
/// Brings Meta's side into the database for one account: posts (all of them on first connection, then the
/// last few days), each post's numbers for its first 30 days, comments on recent posts (in case a webhook was
/// missed), followers per day, ad numbers, and the missed-lead check once a day.
/// </summary>
public class SocialSyncService : ISocialSyncService
{
    private readonly CmsDbContext _db;
    private readonly IMetaSocial _meta;
    private readonly SocialKeys _keys;
    private readonly ISocialLeadsService _leads;
    private readonly ILogger<SocialSyncService> _logger;
    private readonly Func<DateTime> _now;

    public SocialSyncService(CmsDbContext db, IMetaSocial meta, SocialKeys keys, ISocialLeadsService leads, ILogger<SocialSyncService> logger)
        : this(db, meta, keys, leads, logger, () => DateTime.UtcNow) { }

    internal SocialSyncService(CmsDbContext db, IMetaSocial meta, SocialKeys keys, ISocialLeadsService leads, ILogger<SocialSyncService> logger, Func<DateTime> now)
    {
        _db = db;
        _meta = meta;
        _keys = keys;
        _leads = leads;
        _logger = logger;
        _now = now;
    }

    public async Task SyncAccountAsync(Guid accountId, bool full, CancellationToken ct)
    {
        var loaded = await _keys.LoadAsync(accountId);
        if (loaded is not { } x || x.Account.PageId == null) return;
        var (a, c) = x;
        var problems = new List<string>();

        async Task Step(string what, Func<Task> work)
        {
            try { await work(); }
            catch (MetaSocialException ex)
            {
                problems.Add($"{what}: {ex.Message}");
                _logger.LogWarning("Social sync {Account} {Step}: {Message}", a.Name, what, ex.Message);
            }
        }

        var firstTime = full || a.PostsSyncedAt == null;
        await Step("posts", () => PostsAsync(a, c, firstTime, ct));
        await Step("numbers", () => NumbersAsync(a, c, firstTime, ct));
        await Step("comments", () => CommentsAsync(a, c, ct));
        await Step("followers", () => FollowersAsync(a, c, ct));
        if (a.AdAccountId != null && (a.AdsSyncedAt == null || _now() - a.AdsSyncedAt > TimeSpan.FromHours(3)))
            await Step("ads", () => AdsAsync(a, c, ct));
        if (a.LeadsCheckedAt == null || _now() - a.LeadsCheckedAt > TimeSpan.FromHours(20))
        {
            var check = await _leads.CheckFormsAsync(a.AccountId, ct);
            if (!check.Ok) problems.Add("lead forms: " + check.Message);
        }

        var fresh = await _db.SocialAccounts.FirstAsync(s => s.AccountId == a.AccountId, ct);
        fresh.PostsSyncedAt = _now();
        fresh.SyncError = problems.Count == 0 ? null : SocialShared.Preview(string.Join(" ", problems), 390);
        await _db.SaveChangesAsync(ct);
    }

    private async Task PostsAsync(SocialAccount a, SocialCreds c, bool all, CancellationToken ct)
    {
        DateTime? since = all ? null : (a.PostsSyncedAt ?? _now()).AddDays(-3);
        var items = new List<(string Channel, MetaPostItem Item)>();
        items.AddRange((await _meta.FacebookPostsAsync(c, since, all ? 3000 : 300, ct)).Select(p => ("facebook", p)));
        if (a.IgUserId != null) items.AddRange((await _meta.InstagramMediaAsync(c, since, all ? 3000 : 300, ct)).Select(p => ("instagram", p)));
        if (items.Count == 0) return;

        var ids = items.Select(i => i.Item.Id).ToList();
        var existing = await _db.SocialPosts.Where(p => ids.Contains(p.MetaId)).ToListAsync(ct);
        foreach (var (channel, item) in items)
        {
            var p = existing.FirstOrDefault(e => e.Channel == channel && e.MetaId == item.Id);
            if (p == null)
            {
                // A post the CMS published carries its draft (found by the Meta id it got back).
                var draft = await _db.SocialDrafts.AsNoTracking().Where(d => channel == "facebook" ? d.FacebookPostId == item.Id : d.InstagramMediaId == item.Id).Select(d => (Guid?)d.DraftId).FirstOrDefaultAsync(ct);
                p = new SocialPost { PostId = Guid.NewGuid(), AccountId = a.AccountId, Channel = channel, MetaId = item.Id, DraftId = draft, CreatedAt = _now() };
                _db.SocialPosts.Add(p);
                existing.Add(p);
            }
            p.Type = item.Type;
            p.Caption = item.Caption;
            p.ThumbnailUrl = item.ThumbnailUrl;
            p.Permalink = item.Permalink;
            p.PublishedAt = item.PublishedAt;
            p.Likes = item.Likes ?? p.Likes;
            p.Comments = item.Comments ?? p.Comments;
            if (item.Shares != null) p.Shares = item.Shares;
            // Meta's own media type, to pick the right numbers to ask for in this run.
            _mediaTypes[p.MetaId] = item.MediaType;
        }
        await _db.SaveChangesAsync(ct);
    }

    private readonly Dictionary<string, string> _mediaTypes = new();

    private async Task NumbersAsync(SocialAccount a, SocialCreds c, bool all, CancellationToken ct)
    {
        var now = _now();
        var recent = now.AddDays(-30);
        var query = _db.SocialPosts.Where(p => p.AccountId == a.AccountId && (p.PublishedAt >= recent || p.StatsAt == null));
        var posts = await query.OrderByDescending(p => p.PublishedAt).Take(all ? 400 : 120).ToListAsync(ct);
        var today = now.Date;
        foreach (var p in posts)
        {
            if (p.StatsAt != null && now - p.StatsAt < TimeSpan.FromHours(2)) continue;
            var type = _mediaTypes.GetValueOrDefault(p.MetaId) ?? (p.Channel == "facebook" ? "POST" : p.Type == "Reel" ? "REELS" : p.Type == "Story" ? "STORY" : "FEED");
            Dictionary<string, long> m;
            try { m = await _meta.InsightsAsync(c, p.Channel, p.MetaId, type, ct); }
            catch (MetaSocialException ex) when (ex.Code is 10 or 100 or 200)
            {
                // Numbers not available for this post (too old, a story that ended, missing permission).
                p.StatsAt = now;
                continue;
            }
            int? Get(params string[] names)
            {
                foreach (var n in names) if (m.TryGetValue(n, out var v)) return (int)Math.Min(v, int.MaxValue);
                return null;
            }
            p.Views = Get("views", "post_media_view") ?? p.Views;
            p.Reach = Get("reach", "post_total_media_view_unique") ?? p.Reach;
            p.Saves = Get("saved") ?? p.Saves;
            p.Shares = Get("shares") ?? p.Shares;
            p.Clicks = Get("post_clicks") ?? p.Clicks;
            p.StatsJson = SocialShared.Write(m);
            p.StatsAt = now;

            if (p.PublishedAt >= recent)
            {
                var day = await _db.SocialPostStats.FirstOrDefaultAsync(s => s.PostId == p.PostId && s.Day == today, ct);
                if (day == null) { day = new SocialPostStat { PostId = p.PostId, Day = today }; _db.SocialPostStats.Add(day); }
                day.Views = p.Views; day.Reach = p.Reach; day.Likes = p.Likes; day.Comments = p.Comments; day.Shares = p.Shares; day.Saves = p.Saves;
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task CommentsAsync(SocialAccount a, SocialCreds c, CancellationToken ct)
    {
        var since = _now().AddDays(-7);
        var posts = await _db.SocialPosts.AsNoTracking().Where(p => p.AccountId == a.AccountId && p.PublishedAt >= since && p.Type != "Story").Select(p => new { p.Channel, p.MetaId }).ToListAsync(ct);
        foreach (var p in posts)
            foreach (var x in await _meta.CommentsAsync(c, p.Channel, p.MetaId, ct))
                await SocialCommentStore.UpsertAsync(_db, a, p.Channel, p.MetaId, x.Id, x.ParentId, x.AuthorId, x.AuthorName, x.Text, x.Hidden, x.CreatedAt);
        await _db.SaveChangesAsync(ct);
    }

    private async Task FollowersAsync(SocialAccount a, SocialCreds c, CancellationToken ct)
    {
        var (fb, ig) = await _meta.FollowersAsync(c, ct);
        var today = _now().Date;
        var row = await _db.SocialAccountDaily.FirstOrDefaultAsync(d => d.AccountId == a.AccountId && d.Day == today, ct);
        if (row == null) { row = new SocialAccountDaily { AccountId = a.AccountId, Day = today }; _db.SocialAccountDaily.Add(row); }
        row.FbFollowers = fb ?? row.FbFollowers;
        row.IgFollowers = ig ?? row.IgFollowers;
        await _db.SaveChangesAsync(ct);
    }

    private async Task AdsAsync(SocialAccount a, SocialCreds c, CancellationToken ct)
    {
        var to = _now().Date;
        // First time: the last 90 days; then the last 3 (Meta revises recent days).
        var from = a.AdsSyncedAt == null ? to.AddDays(-89) : to.AddDays(-3);
        foreach (var level in new[] { "campaign", "adset", "ad" })
        {
            var rows = await _meta.AdInsightsAsync(c, level, from, to, ct);
            var statuses = await _meta.AdStatusesAsync(c, level, ct);
            var existing = await _db.SocialAdStats.Where(s => s.AccountId == a.AccountId && s.Level == level && s.Day >= from && s.Day <= to).ToListAsync(ct);
            _db.SocialAdStats.RemoveRange(existing);
            foreach (var r in rows.GroupBy(r => (r.Day.Date, r.Id)).Select(g => g.First()))
                _db.SocialAdStats.Add(new SocialAdStat
                {
                    AccountId = a.AccountId, Day = r.Day.Date, Level = level, ObjectId = r.Id, Name = SocialShared.Preview(r.Name, 300), ParentName = r.ParentName == null ? null : SocialShared.Preview(r.ParentName, 300),
                    Status = statuses.GetValueOrDefault(r.Id), Spend = r.Spend, Impressions = r.Impressions, Clicks = r.Clicks, Leads = r.Leads, Conversations = r.Conversations,
                });
            // Statuses change on days without spend too.
            var older = await _db.SocialAdStats.Where(s => s.AccountId == a.AccountId && s.Level == level && s.Day < from).ToListAsync(ct);
            foreach (var o in older) if (statuses.TryGetValue(o.ObjectId, out var st)) o.Status = st;
        }
        var acc = await _db.SocialAccounts.FirstAsync(s => s.AccountId == a.AccountId, ct);
        acc.AdsSyncedAt = _now();
        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>Writes a comment once (from the sync or a webhook), keeping what people did with it here.</summary>
internal static class SocialCommentStore
{
    public static async Task<SocialComment> UpsertAsync(CmsDbContext db, SocialAccount a, string channel, string postMetaId, string metaId, string? parentId,
        string? authorId, string? authorName, string? text, bool hidden, DateTime createdAt)
    {
        var c = db.SocialComments.Local.FirstOrDefault(x => x.Channel == channel && x.MetaId == metaId)
                ?? await db.SocialComments.FirstOrDefaultAsync(x => x.Channel == channel && x.MetaId == metaId);
        var fromUs = authorId != null && (authorId == a.PageId || authorId == a.IgUserId);
        if (c == null)
        {
            c = new SocialComment
            {
                CommentId = Guid.NewGuid(), AccountId = a.AccountId, Channel = channel, MetaId = metaId, PostMetaId = postMetaId, ParentMetaId = parentId,
                AuthorId = authorId, AuthorName = authorName, CreatedAt = createdAt, FromUs = fromUs, Status = fromUs ? "Done" : "Open",
            };
            db.SocialComments.Add(c);
            // Our reply answers the comment it replies to.
            if (fromUs && parentId != null)
            {
                var parent = db.SocialComments.Local.FirstOrDefault(x => x.Channel == channel && x.MetaId == parentId)
                             ?? await db.SocialComments.FirstOrDefaultAsync(x => x.Channel == channel && x.MetaId == parentId);
                if (parent != null) parent.Status = "Done";
            }
        }
        c.Text = text ?? c.Text;
        c.Hidden = hidden;
        c.AuthorName ??= authorName;
        return c;
    }
}
