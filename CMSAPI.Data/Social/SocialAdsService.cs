using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Social;

/// <summary>Social → Ads: the ad account's spend and results, as read by the sync (every 3 hours).</summary>
public class SocialAdsService : ISocialAdsService
{
    private readonly CmsDbContext _db;

    public SocialAdsService(CmsDbContext db) { _db = db; }

    private static decimal? Cpl(decimal spend, int leads) => leads > 0 ? Math.Round(spend / leads, 2) : null;

    public async Task<WorkResult<SocialAdsDto>> SummaryAsync(SocialAdsQuery q)
    {
        var account = await _db.SocialAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == q.AccountId && a.IsActive);
        if (account == null) return WorkResult<SocialAdsDto>.Fail(404, "This account does not exist.");
        var from = q.From.Date;
        var to = q.To.Date;
        if (to < from) return WorkResult<SocialAdsDto>.Fail(400, "The period ends before it starts.");
        if ((to - from).TotalDays > 370) return WorkResult<SocialAdsDto>.Fail(400, "Choose a period of at most a year.");
        var level = q.Level is "adset" or "ad" ? q.Level : "campaign";

        // Totals come from campaign rows (every level adds up to the same spend).
        var campaigns = await _db.SocialAdStats.AsNoTracking().Where(s => s.AccountId == q.AccountId && s.Level == "campaign" && s.Day >= from && s.Day <= to).ToListAsync();
        var rows = level == "campaign" ? campaigns
            : await _db.SocialAdStats.AsNoTracking().Where(s => s.AccountId == q.AccountId && s.Level == level && s.Day >= from && s.Day <= to).ToListAsync();

        var length = (to - from).Days + 1;
        var prev = await _db.SocialAdStats.AsNoTracking()
            .Where(s => s.AccountId == q.AccountId && s.Level == "campaign" && s.Day >= from.AddDays(-length) && s.Day < from)
            .GroupBy(s => 1).Select(g => new { Spend = g.Sum(x => x.Spend), Leads = g.Sum(x => x.Leads) }).FirstOrDefaultAsync();

        var formLeads = _db.SocialLeads.Where(l => l.AccountId == q.AccountId && l.Source == "form" && l.ReceivedAt >= from && l.ReceivedAt < to.AddDays(1)).Select(l => l.LeadId);
        var customers = await _db.CmsSalesLeads.CountAsync(l => formLeads.Contains(l.LeadId) && !l.IsDeleted && l.Stage == "Closed Won");

        var spend = campaigns.Sum(c => c.Spend);
        var leads = campaigns.Sum(c => c.Leads);
        var dto = new SocialAdsDto
        {
            Currency = account.Currency ?? "INR",
            Spend = spend,
            Impressions = campaigns.Sum(c => (long)c.Impressions),
            Clicks = campaigns.Sum(c => (long)c.Clicks),
            Leads = leads,
            Conversations = campaigns.Sum(c => c.Conversations),
            CostPerLead = Cpl(spend, leads),
            PreviousCostPerLead = prev == null ? null : Cpl(prev.Spend, prev.Leads),
            Customers = customers,
            SyncedAt = account.AdsSyncedAt,
        };
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var day = campaigns.Where(c => c.Day == d).ToList();
            dto.Days.Add(new SocialAdDay { Day = d, Spend = day.Sum(x => x.Spend), Leads = day.Sum(x => x.Leads) });
        }
        dto.Rows = rows.GroupBy(r => r.ObjectId).Select(g =>
        {
            var latest = g.OrderByDescending(x => x.Day).First();
            var s = g.Sum(x => x.Spend);
            var l = g.Sum(x => x.Leads);
            return new SocialAdRowDto
            {
                Id = g.Key, Name = latest.Name, ParentName = latest.ParentName, Status = latest.Status, Spend = s,
                Impressions = g.Sum(x => (long)x.Impressions), Clicks = g.Sum(x => (long)x.Clicks), Leads = l, Conversations = g.Sum(x => x.Conversations), CostPerLead = Cpl(s, l),
            };
        }).OrderByDescending(r => r.Spend).ToList();
        return WorkResult<SocialAdsDto>.Success(dto);
    }
}
