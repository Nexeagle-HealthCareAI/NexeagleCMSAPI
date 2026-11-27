using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Data.WhatsApp;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Social;

/// <summary>Social → Posts: every post on the Page and the Instagram account, with Meta's numbers.</summary>
public class SocialPostsService : ISocialPostsService
{
    private readonly CmsDbContext _db;
    private readonly Func<DateTime> _now;

    public SocialPostsService(CmsDbContext db) : this(db, () => DateTime.UtcNow) { }
    internal SocialPostsService(CmsDbContext db, Func<DateTime> now) { _db = db; _now = now; }

    public static SocialPostDto Dto(SocialPost p, string? by) => new()
    {
        PostId = p.PostId, Channel = p.Channel, MetaId = p.MetaId, Type = p.Type, Caption = p.Caption, ThumbnailUrl = p.ThumbnailUrl,
        Permalink = p.Permalink, PublishedAt = p.PublishedAt, FromCms = p.DraftId != null, PublishedBy = by,
        Views = p.Views, Reach = p.Reach, Likes = p.Likes, Comments = p.Comments, Shares = p.Shares, Saves = p.Saves, Clicks = p.Clicks, StatsAt = p.StatsAt,
    };

    private static long Interactions(SocialPost p) => (long)(p.Likes ?? 0) + (p.Comments ?? 0) + (p.Shares ?? 0) + (p.Saves ?? 0);

    public async Task<WorkResult<SocialPostPage>> ListAsync(SocialPostQuery q)
    {
        var account = await _db.SocialAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == q.AccountId && a.IsActive);
        if (account == null) return WorkResult<SocialPostPage>.Fail(404, "This account does not exist.");
        var to = (q.To ?? _now()).Date.AddDays(1);
        var from = (q.From ?? _now().AddDays(-29)).Date;
        if (from >= to) return WorkResult<SocialPostPage>.Fail(400, "The period ends before it starts.");

        var inPeriod = _db.SocialPosts.AsNoTracking().Where(p => p.AccountId == q.AccountId && p.PublishedAt >= from && p.PublishedAt < to);
        var all = await inPeriod.ToListAsync();

        var filtered = all.AsEnumerable();
        if (q.Channel is "facebook" or "instagram") filtered = filtered.Where(p => p.Channel == q.Channel);
        if (!string.IsNullOrWhiteSpace(q.Type)) filtered = filtered.Where(p => p.Type == q.Type);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            filtered = filtered.Where(p => (p.Caption ?? "").Contains(s, StringComparison.OrdinalIgnoreCase));
        }
        filtered = q.Sort switch
        {
            "views" => filtered.OrderByDescending(p => p.Views ?? -1).ThenByDescending(p => p.PublishedAt),
            "interactions" => filtered.OrderByDescending(Interactions).ThenByDescending(p => p.PublishedAt),
            _ => filtered.OrderByDescending(p => p.PublishedAt),
        };
        var list = filtered.ToList();
        var size = Math.Clamp(q.PageSize, 10, 200);
        var page = list.Skip((Math.Max(1, q.Page) - 1) * size).Take(size).ToList();

        var draftIds = page.Where(p => p.DraftId != null).Select(p => p.DraftId!.Value).ToList();
        var drafts = await _db.SocialDrafts.AsNoTracking().Where(d => draftIds.Contains(d.DraftId)).Select(d => new { d.DraftId, d.CreatedByUserId, d.ApprovedByUserId }).ToListAsync();
        var names = await WaShared.NamesAsync(_db, drafts.SelectMany(d => new[] { d.CreatedByUserId, d.ApprovedByUserId }));
        string? By(SocialPost p)
        {
            var d = drafts.FirstOrDefault(x => x.DraftId == p.DraftId);
            return d?.CreatedByUserId is { } u ? names.GetValueOrDefault(u) : null;
        }

        // Summary over the whole period (both channels), against the period before it.
        var length = to - from;
        var previous = await _db.SocialPosts.AsNoTracking()
            .Where(p => p.AccountId == q.AccountId && p.PublishedAt >= from - length && p.PublishedAt < from && p.Views != null)
            .SumAsync(p => (long?)p.Views);
        var followers = await _db.SocialAccountDaily.AsNoTracking().Where(d => d.AccountId == q.AccountId && d.Day >= from.AddDays(-1) && d.Day < to).OrderBy(d => d.Day).ToListAsync();
        int? Growth(Func<SocialAccountDaily, int?> get)
        {
            var values = followers.Select(get).Where(v => v != null).ToList();
            return values.Count >= 2 ? values[^1] - values[0] : null;
        }

        return WorkResult<SocialPostPage>.Success(new SocialPostPage
        {
            Items = page.Select(p => Dto(p, By(p))).ToList(),
            Total = list.Count,
            SyncedAt = account.PostsSyncedAt,
            Summary = new SocialPostSummary
            {
                Posts = all.Count,
                FacebookPosts = all.Count(p => p.Channel == "facebook"),
                InstagramPosts = all.Count(p => p.Channel == "instagram"),
                Views = all.Sum(p => (long)(p.Views ?? 0)),
                PreviousViews = previous,
                Interactions = all.Sum(Interactions),
                NewFacebookFollowers = Growth(d => d.FbFollowers),
                NewInstagramFollowers = Growth(d => d.IgFollowers),
            },
        });
    }

    public async Task<WorkResult<SocialPostDetail>> DetailAsync(Guid postId)
    {
        var p = await _db.SocialPosts.AsNoTracking().FirstOrDefaultAsync(x => x.PostId == postId);
        if (p == null) return WorkResult<SocialPostDetail>.Fail(404, "This post does not exist.");
        var days = await _db.SocialPostStats.AsNoTracking().Where(s => s.PostId == postId).OrderBy(s => s.Day).ToListAsync();
        var comments = await _db.SocialComments.AsNoTracking().Where(c => c.Channel == p.Channel && c.PostMetaId == p.MetaId && !c.Deleted).OrderBy(c => c.CreatedAt).ToListAsync();
        var names = await WaShared.NamesAsync(_db, comments.Select(c => c.RepliedByUserId));
        var leads = await SocialInboxService.CommentLeadsAsync(_db, comments.Select(c => c.CommentId));
        string? by = null;
        if (p.DraftId != null)
        {
            var d = await _db.SocialDrafts.AsNoTracking().FirstOrDefaultAsync(x => x.DraftId == p.DraftId);
            if (d?.CreatedByUserId is { } u) by = (await WaShared.NamesAsync(_db, new Guid?[] { u })).GetValueOrDefault(u);
        }
        return WorkResult<SocialPostDetail>.Success(new SocialPostDetail
        {
            Post = Dto(p, by),
            Days = days.Select(d => new SocialDayStat { Day = d.Day, Views = d.Views, Likes = d.Likes, Comments = d.Comments }).ToList(),
            Comments = SocialInboxService.Tree(comments, p, names, leads, _now()),
            Metrics = SocialShared.Read<Dictionary<string, long>>(p.StatsJson) ?? new(),
        });
    }
}
