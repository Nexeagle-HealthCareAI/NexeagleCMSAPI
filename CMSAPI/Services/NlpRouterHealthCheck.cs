using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CMSAPI.Services
{
    // Pings the NLP router's /health (NlpRouter:BaseUrl). Registered with failureStatus Degraded, so a
    // router outage shows up in /api/v1/health without making /health fail and the container restart:
    // the CMS still works without it, only the Symptom Router tab does not.
    public class NlpRouterHealthCheck : IHealthCheck
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public NlpRouterHealthCheck(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var baseUrl = _configuration["NlpRouter:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
                return HealthCheckResult.Healthy("NlpRouter:BaseUrl not configured; check skipped.");

            try
            {
                using var client = _httpClientFactory.CreateClient();
                client.Timeout = Timeout;
                using var response = await client.GetAsync($"{baseUrl.TrimEnd('/')}/health", cancellationToken);
                return response.IsSuccessStatusCode
                    ? HealthCheckResult.Healthy()
                    : new HealthCheckResult(context.Registration.FailureStatus, $"NLP router /health returned {(int)response.StatusCode}.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return new HealthCheckResult(context.Registration.FailureStatus, "NLP router unreachable.", ex);
            }
        }
    }
}
