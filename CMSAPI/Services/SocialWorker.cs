using CMSAPI.Application.Interfaces;
using CMSAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Services;

/// <summary>
/// CMS → Social in the background: publishes approved posts when due (every 30 seconds, Instagram containers
/// included), and reads each connected account from Meta every 3 hours (posts, numbers, comments, followers,
/// ads; lead forms once a day). Nothing happens until an account is connected in Social → Setup.
/// </summary>
public class SocialWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SocialWorker> _logger;
    private readonly Dictionary<Guid, DateTime> _lastTry = new();

    public SocialWorker(IServiceProvider services, ILogger<SocialWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _services.CreateScope())
                    await scope.ServiceProvider.GetRequiredService<ISocialComposeService>().PublishDueAsync(stoppingToken);

                List<(Guid Id, DateTime? Synced)> accounts;
                using (var scope = _services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
                    accounts = (await db.SocialAccounts.AsNoTracking().Where(a => a.IsActive && a.PageId != null && a.TokenEnc != null)
                        .Select(a => new { a.AccountId, a.PostsSyncedAt }).ToListAsync(stoppingToken))
                        .Select(a => (a.AccountId, a.PostsSyncedAt)).ToList();
                }
                var now = DateTime.UtcNow;
                foreach (var (id, synced) in accounts)
                {
                    if (synced != null && now - synced < TimeSpan.FromHours(3)) continue;
                    // A failing account is tried again after 30 minutes, not every loop.
                    if (_lastTry.TryGetValue(id, out var tried) && now - tried < TimeSpan.FromMinutes(30)) continue;
                    _lastTry[id] = now;
                    using var scope = _services.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ISocialSyncService>().SyncAccountAsync(id, synced == null, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { _logger.LogError(ex, "Social worker failed; trying again shortly."); }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
