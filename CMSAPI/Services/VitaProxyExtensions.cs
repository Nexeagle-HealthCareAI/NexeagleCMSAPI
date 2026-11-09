using CMSAPI.Application.Interfaces;

namespace CMSAPI.Services;

// Program.cs: builder.Services.AddVitaProxy(builder.Configuration);
public static class VitaProxyExtensions
{
    // Named client (not a typed one like the other proxies) so the same client can be shared by anything else that talks to
    // Vita later (a health check, a worker) without re-registering it. 20 s for reads and enqueues (a run itself is
    // asynchronous: POST runs/enqueue answers at once with a run_id); "VitaLong" for the slow calls below.
    public static IServiceCollection AddVitaProxy(this IServiceCollection services, IConfiguration configuration)
    {
        // BaseAddress is resolved per call from Vita:BaseUrl (see VitaProxyService.RelayAsync), so a placeholder or a reloaded
        // appsettings value never breaks start-up. Vita:TimeoutSeconds (optional) overrides the 20 s default.
        var timeout = int.TryParse(configuration["Vita:TimeoutSeconds"], out var seconds) && seconds > 0 ? seconds : 20;
        services.AddHttpClient(VitaProxyService.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(timeout);
        });
        // "VitaLong": calls that wait on the model or a PR stack (generate, log escape, escape verify, learn, add repo, digest
        // send, run-now, synchronous runs). Vita:LongTimeoutSeconds (optional) overrides the 900 s default.
        var longTimeout = int.TryParse(configuration["Vita:LongTimeoutSeconds"], out var longSeconds) && longSeconds > 0 ? longSeconds : 900;
        services.AddHttpClient(VitaProxyService.LongHttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(longTimeout);
        });
        services.AddScoped<IVitaProxyService, VitaProxyService>();
        return services;
    }
}
