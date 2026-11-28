using CMSAPI.Application.Interfaces;
using CMSAPI.Data;
using CMSAPI.Data.Keywords;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Services;

/// <summary>
/// CMS → Keywords in the background: once a day after 05:30 IST, collects search phrases from Doctor Dekho's site
/// search, Search Console, Google Ads search terms and Keyword Planner, then suggests topics. Checks every 10 minutes
/// whether today's collection has run; sources that are not set up are skipped and shown on Keywords → Setup.
/// </summary>
public class KeywordWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<KeywordWorker> _logger;

    public KeywordWorker(IServiceProvider services, ILogger<KeywordWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
                var last = await db.KeywordSourceRuns.AsNoTracking().Where(r => r.SourceKey == "site").Select(r => (DateTime?)r.LastRunAt).FirstOrDefaultAsync(stoppingToken);
                if (KeywordCollector.Due(DateTime.UtcNow, last))
                {
                    var counts = await scope.ServiceProvider.GetRequiredService<IKeywordCollector>().RunAsync(null, stoppingToken);
                    _logger.LogInformation("Keywords: collected {Counts}.", string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}")));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { _logger.LogError(ex, "Keyword worker failed; trying again in 10 minutes."); }
            try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
