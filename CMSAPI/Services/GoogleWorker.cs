using CMSAPI.Application.Interfaces;

namespace CMSAPI.Services;

/// <summary>
/// CMS → Google in the background: every 10 minutes, sends Google Ads the Google leads that reached a chosen CRM
/// stage (Google → Setup → "Tell Google which leads turned into business"). Nothing is sent until that is set up.
/// </summary>
public class GoogleWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<GoogleWorker> _logger;

    public GoogleWorker(IServiceProvider services, ILogger<GoogleWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var sent = await scope.ServiceProvider.GetRequiredService<IGoogleOutcomesService>().SendDueAsync(stoppingToken);
                if (sent > 0) _logger.LogInformation("Google: {Count} CRM results sent to Google Ads.", sent);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { _logger.LogError(ex, "Google worker failed; trying again in 10 minutes."); }
            try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
