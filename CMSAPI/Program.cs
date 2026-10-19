using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using CMSAPI.Data;
using CMSAPI.Data.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using CMSAPI.Authorization;
using CMSAPI.Data.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// =============================
// CONFIGURATION
// =============================
builder.Services.AddHostedService<CMSAPI.Services.ScheduledHealthCheck>();
builder.Services.AddHostedService<CMSAPI.Services.DripCampaignWorker>();
builder.Services.AddHostedService<CMSAPI.Services.MetaAdSpendWorker>();

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// =============================
// LOGGING
// =============================
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// =============================
// SERVICES
// =============================
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache(); // DashboardRepository caches the landing-page stats
// Same key as EasyHMS Tracking:MobileHashKey: lets Insights find a full mobile number by its hash.
builder.Services.AddSingleton(new CMSAPI.Application.Models.TrackingHashSettings(builder.Configuration["Tracking:MobileHashKey"]));
builder.Services.AddHttpClient();
builder.Services.AddControllers();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
}).AddMvc();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 64 * 1024; // 64 KB – prevent oversized payloads
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

// =============================
// RATE LIMITING
// =============================
builder.Services.AddRateLimiter(options =>
{
    // AddFixedWindowLimiter(...) creates ONE shared bucket for every caller, so "10 per minute" was
    // 10 per minute for everyone together - and /chathub used the same bucket, so a handful of
    // website visitors opening chat could lock admins out of login (and the reverse). Both
    // policies are now partitioned per client IP.
    static string ClientKey(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // Login endpoints: max 10 attempts per IP per minute.
    options.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = 10,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
    }));

    // Live-chat hub: negotiate + connect requests (and long-polling, when WebSockets are blocked).
    options.AddPolicy("chathub", ctx => RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = 60,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
    }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Configure TokenSettings
builder.Services.Configure<TokenSettings>(
    builder.Configuration.GetSection("TokenSettings")
);

// Register Application Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IHospitalOperationsService, HospitalOperationsService>();
builder.Services.AddScoped<IFreeTierSettingsService, FreeTierSettingsService>();
builder.Services.AddScoped<IJobsService, JobsService>();
builder.Services.AddScoped<IHospitalService, HospitalService>();
builder.Services.AddScoped<IDoctorService, DoctorService>();
builder.Services.AddScoped<IInsightsService, InsightsService>();
builder.Services.AddScoped<IMarketingService, MarketingService>();
builder.Services.AddScoped<ISalesLeadService, SalesLeadService>();
builder.Services.AddScoped<ISubscriptionApprovalService, CMSAPI.Services.SubscriptionApprovalService>();
// Typed HttpClient (not just AddScoped) -- SymptomRouterService calls out to the NLP router
// and GitHub's API, so it needs an injected HttpClient the DI container manages properly.
builder.Services.AddHttpClient<ISymptomRouterService, SymptomRouterService>();
builder.Services.AddHttpClient<IAgentMonitorService, AgentMonitorService>(c => c.Timeout = TimeSpan.FromSeconds(10));
// Same reasoning -- DataMigrationService calls the Python DataMigrationService. A longer timeout
// than the 100s HttpClient default: CSV parsing + a Groq call can run past that on a large file.
builder.Services.AddHttpClient<IDataMigrationService, DataMigrationService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(120);
});
builder.Services.AddHttpClient<IGroqSalesAiService, GroqSalesAiService>();
builder.Services.AddHttpClient<IWhatsAppService, WhatsAppService>();
builder.Services.AddHttpClient<IRadHospitalsService, RadHospitalsService>();
// The Health Wiki's data is in 1HMSAPI's database; this reaches it over HTTP with a shared key (see HealthWikiService). A longer timeout
// than the default so an image upload through two servers is not cut off.
builder.Services.AddHttpClient<IHealthWikiService, HealthWikiService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(60);
});

// Register Data Repositories
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<IHospitalOperationsRepository, HospitalOperationsRepository>();
builder.Services.AddScoped<IFreeTierSettingsRepository, FreeTierSettingsRepository>();
builder.Services.AddScoped<IJobsRepository, JobsRepository>();
builder.Services.AddScoped<IHospitalRepository, HospitalRepository>();
builder.Services.AddScoped<IDoctorRepository, DoctorRepository>();
builder.Services.AddScoped<IInsightsRepository, InsightsRepository>();
builder.Services.AddScoped<IMarketingRepository, MarketingRepository>();
builder.Services.AddScoped<ISalesLeadRepository, SalesLeadRepository>();
builder.Services.AddScoped<ISymptomRouterRepository, SymptomRouterRepository>();
builder.Services.AddScoped<IAgentMonitorRepository, AgentMonitorRepository>();
builder.Services.AddScoped<IDataMigrationRepository, DataMigrationRepository>();

// CMS identity / RBAC (CMSDatabase)
builder.Services.AddScoped<ICmsAuthRepository, CmsAuthRepository>();
builder.Services.AddScoped<ICmsPartnerRepository, CmsPartnerRepository>();
builder.Services.AddScoped<ICmsAdminRepository, CmsAdminRepository>();
builder.Services.AddScoped<ICmsAdminService, CmsAdminService>();
builder.Services.AddScoped<ICmsPartnerService, CmsPartnerService>();
builder.Services.AddScoped<IReferralCodeRepository, ReferralCodeRepository>();
builder.Services.AddScoped<IReferralCodeService, ReferralCodeService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<IEmailService, EmailService>();
builder.Services.AddScoped<CmsAdminSeeder>();

// =============================
// SWAGGER
// =============================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CMSAPI",
        Version = "v1"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Enter JWT with Bearer prefix",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = "bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// =============================
// CORS
// =============================
var corsOrigins = new List<string>
{
    "http://localhost:3000",
    "http://localhost:3001",
    "https://localhost:3000",
    "https://localhost:3001",
    "http://localhost:5173",
    "http://localhost:5174",
    "http://localhost:5175",
    "https://nexeagle.com",
    "https://cms.nexeagle.com",
    // NexEagleWebsite's actual public domain (the LiveChat widget embedded on its
    // /business, /services, /products, /contact, /careers pages connects to this API's
    // /chathub from here) — was missing entirely, silently breaking live chat in prod.
    "https://1hms.nexeagle.com",
    // The dev website and the www host also embed the live-chat widget (it connects to /chathub).
    "https://www.nexeagle.com",
    "https://nexeagle-dev.nexeagle.com"
};
// Additional origins injected at deploy time (e.g. VM IPs for dev/prod)
var extraOrigins = builder.Configuration["Cors:AllowedOrigins"];
if (!string.IsNullOrWhiteSpace(extraOrigins))
    corsOrigins.AddRange(extraOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCors", policy =>
    {
        policy.WithOrigins(corsOrigins.ToArray())
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials() // Required for SignalR
              .WithExposedHeaders(CMSAPI.Services.RequestLoggingMiddleware.HeaderName); // lets the browser read it when reporting an error
    });
});

// =============================
// JWT AUTHENTICATION
// =============================
var jwtIssuer = builder.Configuration["TokenSettings:Issuer"]
    ?? throw new InvalidOperationException("TokenSettings:Issuer missing.");

var jwtAudience = builder.Configuration["TokenSettings:Audience"]
    ?? throw new InvalidOperationException("TokenSettings:Audience missing.");

var jwtSecret = builder.Configuration["TokenSettings:Key"]
    ?? throw new InvalidOperationException("TokenSettings:Key missing.");

if (jwtSecret.Length < 32)
    throw new InvalidOperationException("TokenSettings:Key must be at least 32 characters.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecret)
            ),
            ClockSkew = TimeSpan.Zero
        };

        // SignalR cannot set Authorization headers on the WebSocket handshake, so the
        // client passes the JWT as an "access_token" query param. Pick it up for the hubs.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && (path.StartsWithSegments("/chathub") || path.StartsWithSegments("/hubs/crm")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// =============================
// DATABASE
// =============================
var sqlConn =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration["ConnectionStrings:DefaultConnection"]
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection missing.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(sqlConn, sql =>
    {
        sql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null);

        sql.CommandTimeout(60);
    })
);

// CMS identity / RBAC database (separate from the shared HMS DB).
// Prefer an explicit CmsConnection; otherwise reuse DefaultConnection's server with the CMSDatabase catalog.
var cmsConn = builder.Configuration.GetConnectionString("CmsConnection");
if (string.IsNullOrWhiteSpace(cmsConn))
{
    cmsConn = System.Text.RegularExpressions.Regex.Replace(
        sqlConn, @"(?i)(Initial Catalog|Database)\s*=\s*[^;]+", "Initial Catalog=CMSDatabase");
}

builder.Services.AddDbContext<CmsDbContext>(options =>
    options.UseSqlServer(cmsConn, sql =>
    {
        sql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null);

        sql.CommandTimeout(60);
    })
);

// =============================
// HEALTH CHECKS
// =============================
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("hms-database")
    .AddDbContextCheck<CmsDbContext>("cms-database")
    // Degraded, not Unhealthy: the CMS works without the router, so /health stays 200 and the
    // container is not restarted; /api/v1/health (admins) still shows it.
    .AddCheck<CMSAPI.Services.NlpRouterHealthCheck>("nlp-router", failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded);

// =============================
// AUTHORIZATION
// =============================
builder.Services.AddAuthorization();

// Permission ("perm") policy support for [HasPermission("page.action")].
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();


// =============================
// BUILD APP
// =============================
try
{
    var app = builder.Build();

    // =============================
    // SEED FIRST CMS ADMIN (env-driven, idempotent)
    // =============================
    using (var scope = app.Services.CreateScope())
    {
        await scope.ServiceProvider.GetRequiredService<CmsAdminSeeder>().SeedAsync();
    }

    // =============================
    // CRITICAL FIX FOR AZURE
    // =============================
    // Trust X-Forwarded-For only from the reverse proxy on this host / Docker networks. Without this,
    // only loopback was trusted, so behind Caddy + Docker every visitor showed up as the same bridge IP
    // and the per-IP rate limits (login, chat) became one shared bucket again. Public callers hitting
    // the API port directly are still not trusted, so they cannot fake their IP with the header.
    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };
    forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8));
    forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12));
    forwardedOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("192.168.0.0"), 16));
    app.UseForwardedHeaders(forwardedOptions);

    // =============================
    // REQUEST LOGGING (outermost, so it sees the final status, including 500s from the handler below)
    // =============================
    app.UseMiddleware<CMSAPI.Services.RequestLoggingMiddleware>();

    // =============================
    // GLOBAL EXCEPTION HANDLER
    // =============================
    app.UseExceptionHandler(errApp =>
    {
        errApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            var feature = context.Features.Get<IExceptionHandlerFeature>();
            if (feature != null)
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogError(feature.Error, "Unhandled exception");
            }
            await context.Response.WriteAsJsonAsync(new { message = "An internal error occurred." });
        });
    });

    // =============================
    // SECURITY HEADERS
    // =============================
    app.Use(async (context, next) =>
    {
        context.Response.Headers.Append("X-Frame-Options",        "DENY");
        context.Response.Headers.Append("X-Content-Type-Options",  "nosniff");
        context.Response.Headers.Append("X-XSS-Protection",        "1; mode=block");
        context.Response.Headers.Append("Referrer-Policy",          "no-referrer");
        await next();
    });

    // =============================
    // RATE LIMITING
    // =============================
    app.UseRateLimiter();

    // =============================
    // STATIC FILES
    // =============================
    app.UseStaticFiles();

    // =============================
    // SWAGGER
    // =============================
    app.UseSwagger();

    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CMSAPI v1");
        c.RoutePrefix = "swagger";
    });

    // =============================
    // HTTPS REDIRECTION (AFTER ForwardedHeaders)
    // =============================
    app.UseHttpsRedirection();

    // =============================
    // ROUTING
    // =============================
    app.UseRouting();

    // =============================
    // CORS
    // =============================
    app.UseCors("FrontendCors");

    // =============================
    // AUTH
    // =============================
    app.UseAuthentication();
    app.UseAuthorization();

    // =============================
    // ENDPOINTS
    // =============================
    app.MapControllers();
    app.MapHub<CMSAPI.Hubs.ChatHub>("/chathub").RequireRateLimiting("chathub").AllowAnonymous(); // guests are anonymous; agent methods check live-support.view
    app.MapHub<CMSAPI.Hubs.CrmHub>("/hubs/crm"); // auth + marketing.view enforced by [HasPermission] on the hub

    app.MapHealthChecks("/health")
        .AllowAnonymous();

    // Root redirect to Swagger
    app.MapGet("/", context =>
    {
        context.Response.Redirect("/swagger/index.html");
        return Task.CompletedTask;
    });

    // =============================
    // RUN
    // =============================
    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine("CRITICAL: Application Startup Failure!");
    Console.WriteLine(ex.ToString());
    throw;
}
