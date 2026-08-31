using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CMSAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Services;

public class MetaAdSpendWorker : BackgroundService
{
    private readonly ILogger<MetaAdSpendWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    
    // Run every 24 hours (or could be configurable)
    private readonly TimeSpan _interval = TimeSpan.FromHours(24);

    public MetaAdSpendWorker(
        ILogger<MetaAdSpendWorker> logger,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        IConfiguration config)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MetaAdSpendWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAdSpendAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while syncing Meta Ad Spend.");
            }

            // Wait before next run
            await Task.Delay(_interval, stoppingToken);
        }
        
        _logger.LogInformation("MetaAdSpendWorker stopped.");
    }

    private async Task SyncAdSpendAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Meta Ad Spend sync...");
        
        var systemUserToken = _config["Meta:SystemUserToken"];
        if (string.IsNullOrEmpty(systemUserToken))
        {
            _logger.LogWarning("Meta:SystemUserToken is not configured. Skipping spend sync.");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        
        var activeMetaCampaigns = await db.CmsCampaigns
            .Where(c => c.IsActive && c.Platform == "META_ADS" && !string.IsNullOrEmpty(c.ExternalCampaignId))
            .ToListAsync(stoppingToken);
            
        if (!activeMetaCampaigns.Any())
        {
            _logger.LogInformation("No active Meta campaigns found to sync.");
            return;
        }
        
        var client = _httpClientFactory.CreateClient();
        
        foreach (var campaign in activeMetaCampaigns)
        {
            try
            {
                // Note: Meta Insights API requires either Ad Account ID or Campaign ID. 
                // We'll query by Campaign ID for maximum lifetime spend.
                var metaUrl = $"https://graph.facebook.com/v17.0/{campaign.ExternalCampaignId}/insights?fields=spend&date_preset=maximum&access_token={systemUserToken}";
                
                var metaResponse = await client.GetAsync(metaUrl, stoppingToken);
                
                if (metaResponse.IsSuccessStatusCode)
                {
                    var json = await metaResponse.Content.ReadAsStringAsync(stoppingToken);
                    using var doc = JsonDocument.Parse(json);
                    
                    if (doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.GetArrayLength() > 0)
                    {
                        var spendStr = dataArray[0].GetProperty("spend").GetString();
                        if (decimal.TryParse(spendStr, out var spendValue))
                        {
                            campaign.ActualSpend = spendValue;
                            _logger.LogInformation($"Updated campaign '{campaign.CampaignName}' spend to {spendValue}");
                        }
                    }
                }
                else
                {
                    _logger.LogWarning($"Failed to sync spend for campaign '{campaign.ExternalCampaignId}'. Status: {metaResponse.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to process campaign '{campaign.ExternalCampaignId}'");
            }
            
            // Wait 200ms between calls to avoid Meta API rate limiting
            await Task.Delay(200, stoppingToken);
        }
        
        await db.SaveChangesAsync(stoppingToken);
        _logger.LogInformation("Meta Ad Spend sync completed.");
    }
}
