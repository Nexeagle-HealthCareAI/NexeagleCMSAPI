using CMSAPI.Application.Interfaces;

namespace CMSAPI.Services;

// Program.cs: builder.Services.AddVitaProxy(builder.Configuration);
public static class VitaProxyExtensions
{
    // Named client (not a typed one like the other proxies) so the same client can be shared by anything else that talks to
    // Vita later (a health check, a worker) without re-registering it. 20 s: every Vita route the CMS calls is a read or an
    // enqueue; a run itself is asynchronous (POST runs/enqueue answers at once with a run_id).
    public static IServiceCollection AddVitaProxy(this IServiceCollection services, IConfiguration configuration)
    {
        // BaseAddress is resolved per call from Vita:BaseUrl (see VitaProxyService.RelayAsync), so a placeholder or a reloaded
        // appsettings value never breaks start-up. Vita:TimeoutSeconds (optional) overrides the 20 s default.
        var timeout = int.TryParse(configuration["Vita:TimeoutSeconds"], out var seconds) && seconds > 0 ? seconds : 20;
        services.AddHttpClient(VitaProxyService.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(timeout);
        });
        services.AddScoped<IVitaProxyService, VitaProxyService>();
        return services;
    }
}
