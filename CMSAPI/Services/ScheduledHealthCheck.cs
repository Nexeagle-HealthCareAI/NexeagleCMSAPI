using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CMSAPI.Services
{
    public class ScheduledHealthCheck : BackgroundService
    {
        private readonly HealthCheckService _healthCheckService;
        private readonly ILogger<ScheduledHealthCheck> _logger;

        public ScheduledHealthCheck(HealthCheckService healthCheckService, ILogger<ScheduledHealthCheck> logger)
        {
            _healthCheckService = healthCheckService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Scheduled Health Check Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = CalculateDelayForNextRun();
                _logger.LogInformation($"Next health check scheduled in: {delay}");

                try
                {
                    await Task.Delay(delay, stoppingToken);

                    _logger.LogInformation("Running Scheduled Health Check...");
                    
                    var report = await _healthCheckService.CheckHealthAsync(stoppingToken);
                    
                    LogHealthReport(report);
                }
                catch (OperationCanceledException)
                {
                    // Graceful shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while running the scheduled health check.");
                }
            }
        }

        private TimeSpan CalculateDelayForNextRun()
        {
            // Target: 7:00 AM IST.
            // IST is UTC +5:30.
            // So 7:00 AM IST = 1:30 AM UTC.
            
            var nowUtc = DateTime.UtcNow;
            var targetTimeUtc = nowUtc.Date.AddHours(1).AddMinutes(30); // 01:30 AM today

            if (nowUtc > targetTimeUtc)
            {
                targetTimeUtc = targetTimeUtc.AddDays(1); // Next day if passed
            }

            return targetTimeUtc - nowUtc;
        }

        private void LogHealthReport(HealthReport report)
        {
            var status = report.Status;
            var totalDuration = report.TotalDuration;

            _logger.LogInformation($"Health Check Completed. Global Status: {status}. Duration: {totalDuration}");

            foreach (var entry in report.Entries)
            {
                _logger.LogInformation($" - Component: {entry.Key}, Status: {entry.Value.Status}, Description: {entry.Value.Description}");
            }

            if (status == HealthStatus.Unhealthy)
            {
                _logger.LogCritical("SYSTEM UNHEALTHY: Immediate attention required!");
                // Here you could send an email or alert
            }
        }
    }
}
