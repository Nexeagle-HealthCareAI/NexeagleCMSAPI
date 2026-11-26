using CMSAPI.Application.Interfaces;
using CMSAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Services;

/// <summary>
/// Sends WhatsApp broadcasts at the speed set in WhatsApp → Settings (messages a second), once a second;
/// reads templates' approval status from Meta every 10 minutes while some are waiting, and every hour
/// otherwise. Does nothing until the marketing number is connected.
/// </summary>
public class WhatsAppSendWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<WhatsAppSendWorker> _logger;

    public WhatsAppSendWorker(IServiceProvider services, ILogger<WhatsAppSendWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastSync = DateTime.MinValue;
        // Let the API start first.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var sent = 0;
            try
            {
                using var scope = _services.CreateScope();
                var meta = scope.ServiceProvider.GetRequiredService<IWaMeta>();
                if (meta.IsConfigured)
                {
                    var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
                    var speed = await db.WaSettings.AsNoTracking().Where(s => s.SettingsId == 1).Select(s => (int?)s.SendPerSecond).FirstOrDefaultAsync(stoppingToken) ?? 20;
                    sent = await scope.ServiceProvider.GetRequiredService<IWaBroadcastService>().SendDueAsync(Math.Clamp(speed, 1, 80), stoppingToken);

                    var pending = await db.WaTemplates.AnyAsync(t => t.Status == "PENDING" || t.Status == "IN_APPEAL", stoppingToken);
                    if (DateTime.UtcNow - lastSync > (pending ? TimeSpan.FromMinutes(10) : TimeSpan.FromHours(1)))
                    {
                        lastSync = DateTime.UtcNow;
                        var changed = await scope.ServiceProvider.GetRequiredService<IWaTemplateService>().SyncAsync();
                        if (changed > 0) _logger.LogInformation("WhatsApp templates: {Count} changed at Meta.", changed);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WhatsApp send worker failed; trying again shortly.");
                lastSync = DateTime.UtcNow;   // do not hammer Meta when it is the problem
                sent = 0;
                try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (OperationCanceledException) { return; }
            }
            // Busy: next batch in a second. Nothing to do: look again in five.
            try { await Task.Delay(TimeSpan.FromSeconds(sent > 0 ? 1 : 5), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
