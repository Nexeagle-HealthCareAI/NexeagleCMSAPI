using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CMSAPI.Data;
using Microsoft.EntityFrameworkCore;

using CMSAPI.Application.Services;

namespace CMSAPI.Services;

public class DripCampaignWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DripCampaignWorker> _logger;

    public DripCampaignWorker(IServiceProvider serviceProvider, ILogger<DripCampaignWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Drip Campaign Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDripCampaignsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing drip campaigns");
            }

            // Run once a day or every hour, for demo purposes we run it frequently
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task ProcessDripCampaignsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var waService = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();

        // Find leads in "NEW" or "CONTACTED" state created within last 14 days
        var fourteenDaysAgo = DateTime.UtcNow.AddDays(-14);
        var leads = await db.CrmLeads
            .Include(l => l.Activities)
            .Where(l => (l.Status == "NEW" || l.Status == "CONTACTED") && l.CreatedAt >= fourteenDaysAgo)
            .ToListAsync(ct);

        foreach (var lead in leads)
        {
            // Halt drip sequence if the lead has responded
            bool hasInboundActivity = lead.Activities.Any(a => a.Direction == "INBOUND");
            if (hasInboundActivity)
            {
                continue;
            }

            var daysSinceCreation = (DateTime.UtcNow - lead.CreatedAt).Days;

            // 14-day Drip schedule
            string? templateToRun = daysSinceCreation switch
            {
                1 => "day1_intro_pitch",
                3 => "day3_roi_case_study",
                7 => "day7_offline_mode_demo",
                14 => "day14_final_offer",
                _ => null
            };

            if (templateToRun != null)
            {
                // check if already sent this template
                var alreadySent = lead.Activities
                    .Any(a => a.TemplateName == templateToRun);

                if (!alreadySent)
                {
                    _logger.LogInformation("Sending {Template} to Lead {LeadId}", templateToRun, lead.Id);
                    var success = await waService.SendTemplateMessageAsync(lead.PhoneNumber, templateToRun, ct: ct);
                    if (success)
                    {
                        db.CrmLeadActivities.Add(new Domain.Entities.CrmLeadActivity
                        {
                            LeadId = lead.Id,
                            ActivityType = "WHATSAPP_DRIP",
                            Direction = "OUTBOUND",
                            TemplateName = templateToRun,
                            MessageBody = $"Drip Campaign: {templateToRun}",
                            Status = "SENT"
                        });
                    }
                }
            }
        }
        
        await db.SaveChangesAsync(ct);
    }
}
