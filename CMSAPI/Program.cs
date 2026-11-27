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
using CMSAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// =============================
// CONFIGURATION
// =============================
builder.Services.AddHostedService<CMSAPI.Services.ScheduledHealthCheck>();
builder.Services.AddHostedService<CMSAPI.Services.DripCampaignWorker>();
builder.Services.AddHostedService<CMSAPI.Services.MetaAdSpendWorker>();
builder.Services.AddHostedService<CMSAPI.Services.WhatsAppSendWorker>();
builder.Services.AddHostedService<CMSAPI.Services.SocialWorker>();

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
builder.Services.AddControllers()
    .AddApplicationPart(typeof(CMSAPI.Controllers.PlanCalculatorController).Assembly);

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
// WhatsApp marketing on the CMS's own number (WhatsAppMarketing__* settings; design: claude/whatsapp-marketing-design.md)
builder.Services.AddSingleton<CMSAPI.Data.WhatsApp.WaSecretBox>();
builder.Services.AddSingleton<IWaConnectionStore, CMSAPI.Data.WhatsApp.WaConnectionStore>();
builder.Services.AddHttpClient<IWaMeta, CMSAPI.Data.WhatsApp.GraphWaMeta>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<IWaSetupService, CMSAPI.Data.WhatsApp.WaSetupService>();
// Social: Facebook Page, Instagram, ads (design: claude/social-facebook-instagram-design.md)
builder.Services.AddHttpClient<IMetaSocial, CMSAPI.Data.Social.GraphMetaSocial>(c => c.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddSingleton<CMSAPI.Data.Social.SocialMediaLinks>();
builder.Services.AddScoped<CMSAPI.Data.Social.SocialKeys>();
builder.Services.AddScoped<ISocialSetupService, CMSAPI.Data.Social.SocialSetupService>();
builder.Services.AddScoped<ISocialPostsService, CMSAPI.Data.Social.SocialPostsService>();
builder.Services.AddScoped<ISocialComposeService, CMSAPI.Data.Social.SocialComposeService>();
builder.Services.AddScoped<ISocialInboxService, CMSAPI.Data.Social.SocialInboxService>();
builder.Services.AddScoped<ISocialLeadsService, CMSAPI.Data.Social.SocialLeadsService>();
builder.Services.AddScoped<ISocialAdsService, CMSAPI.Data.Social.SocialAdsService>();
builder.Services.AddScoped<ISocialWebhookService, CMSAPI.Data.Social.SocialWebhookService>();
builder.Services.AddScoped<ISocialSyncService, CMSAPI.Data.Social.SocialSyncService>();
builder.Services.AddScoped<IWaWebhookService, CMSAPI.Data.WhatsApp.WaWebhookService>();
builder.Services.AddScoped<IWaInboxService, CMSAPI.Data.WhatsApp.WaInboxService>();
builder.Services.AddScoped<IWaContactService, CMSAPI.Data.WhatsApp.WaContactService>();
builder.Services.AddScoped<IWaTemplateService, CMSAPI.Data.WhatsApp.WaTemplateService>();
builder.Services.AddScoped<IWaBroadcastService, CMSAPI.Data.WhatsApp.WaBroadcastService>();
builder.Services.AddScoped<IWaAccountService, CMSAPI.Data.WhatsApp.WaAccountService>();
builder.Services.AddHttpClient<IRadHospitalsService, RadHospitalsService>();
builder.Services.AddHttpClient<IRadSubscriptionsService, RadSubscriptionsService>();
// The Health Wiki's data is in 1HMSAPI's database; this reaches it over HTTP with a shared key (see HealthWikiService). A longer timeout
// than the default so an image upload through two servers is not cut off.
builder.Services.AddHttpClient<IHealthWikiService, HealthWikiService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(60);
});
// Vita's emergency takeover (verify hospitals, places not on 1HMS, alert log) lives in 1HMSAPI too: same address and internal key.
builder.Services.AddHttpClient<IEmergencyTakeoverService, EmergencyTakeoverService>(c => c.Timeout = TimeSpan.FromSeconds(20));
// Vita Test Agent (runs, generated cases, the PR gate) lives in its own service; VitaController relays to it with a shared key.
// Named client "Vita" (Vita:TimeoutSeconds, default 20 s). See CMSAPI/Services/VitaProxyExtensions.cs, Vita:BaseUrl, ServiceAuth:VitaServiceKey.
builder.Services.AddVitaProxy(builder.Configuration);

// Register Data Repositories
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<IHospitalOperationsRepository, HospitalOperationsRepository>();
builder.Services.AddScoped<IFreeTierSettingsRepository, FreeTierSettingsRepository>();
builder.Services.AddScoped<IJobsRepository, JobsRepository>();
builder.Services.AddScoped<IHospitalRepository, HospitalRepository>();
builder.Services.AddScoped<IDoctorRepository, DoctorRepository>();
builder.Services.AddScoped<IInsightsRepository, InsightsRepository>();
builder.Services.AddScoped<IWhatsAppBotStore, CMSAPI.Data.Services.SqlWhatsAppBotStore>();
builder.Services.AddScoped<IWhatsAppInsightsService, CMSAPI.Data.Services.WhatsAppInsightsService>();
builder.Services.AddScoped<ISearchInsightsService, CMSAPI.Data.Services.SearchInsightsService>();
builder.Services.AddScoped<IAppointmentInsightsService, CMSAPI.Data.Services.AppointmentInsightsService>();
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
builder.Services.AddScoped<ICmsProfileService, CMSAPI.Data.Services.CmsProfileService>();
builder.Services.AddScoped<ICmsPartnerService, CmsPartnerService>();
builder.Services.AddScoped<IReferralCodeRepository, ReferralCodeRepository>();
builder.Services.AddScoped<IReferralCodeService, ReferralCodeService>();
builder.Services.AddScoped<IWorkRepository, WorkRepository>();
builder.Services.AddScoped<IWorkService, WorkService>();
builder.Services.AddScoped<IWorkAccessService, CMSAPI.Data.Services.WorkAccessService>();
builder.Services.AddScoped<IWorkActivityService, CMSAPI.Data.Services.WorkActivityService>();
builder.Services.AddScoped<IWorkMentionService, CMSAPI.Data.Services.WorkMentionService>();
builder.Services.AddScoped<IWorkChatService, CMSAPI.Data.Services.WorkChatService>();
// Docs tab: uploaded files go to the S3-compatible store (MinIO / E2E Object Storage) when all three
// settings are there; without them pages still work and uploads are refused with a clear message.
// The deploy workflow writes them (Storage__PublicBaseUrl, Storage__Minio__AccessKey, __SecretKey).
var docsStorageReady =
    !string.IsNullOrWhiteSpace(builder.Configuration["Storage:PublicBaseUrl"] ?? builder.Configuration["Storage:Minio:PublicBaseUrl"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["Storage:Minio:AccessKey"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["Storage:Minio:SecretKey"]);
if (docsStorageReady)
    builder.Services.AddSingleton<IDocFileStore, CMSAPI.Data.Storage.MinioDocFileStore>();
else
    builder.Services.AddSingleton<IDocFileStore, CMSAPI.Data.Storage.MissingDocFileStore>();
builder.Services.AddScoped<IDocsService, CMSAPI.Data.Services.DocsService>();
builder.Services.AddScoped<IDocCommentsService, CMSAPI.Data.Services.DocCommentsService>();
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
                if (!string.IsNullOrEmpty(accessToken) && (path.StartsWithSegments("/chathub") || path.StartsWithSegments("/hubs/crm") || path.StartsWithSegments("/hubs/vita")))
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

// The WhatsApp booking bot's own database (repo Whatsapp-Webhook), read for the Insights page.
// Prefer an explicit WhatsAppBotConnection; otherwise the same server as DefaultConnection with the
// bot's catalog (WhatsAppBot:Database, else WhatsAppBookingProd in Production and WhatsAppBookingDev
// elsewhere). If it cannot be reached, only the WhatsApp figures say so; nothing else is affected.
var botConn = builder.Configuration.GetConnectionString("WhatsAppBotConnection");
if (string.IsNullOrWhiteSpace(botConn))
{
    var botDatabase = builder.Configuration["WhatsAppBot:Database"];
    if (string.IsNullOrWhiteSpace(botDatabase))
        botDatabase = builder.Environment.IsProduction() ? "WhatsAppBookingProd" : "WhatsAppBookingDev";
    botConn = System.Text.RegularExpressions.Regex.Replace(
        sqlConn, @"(?i)(Initial Catalog|Database)\s*=\s*[^;]+", "Initial Catalog=" + botDatabase);
}
builder.Services.AddSingleton(new CMSAPI.Data.Services.WhatsAppBotConnection(botConn));

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

// Per-platform product databases (EasyHMS / 1Rad). The factory reuses AppDbContext
// with a connection resolved from ApplicationName; the validator reports each
// product DB's readiness at startup. See ProductDbContextFactory for details.
builder.Services.AddSingleton<IProductDbContextFactory, ProductDbContextFactory>();
builder.Services.AddSingleton<ProductDatabaseValidator>();

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

    // One line in `docker logs` that says whether document uploads can work here.
    // Never stops the API from starting: a mistyped address only means files can't be stored.
    try
    {
        var store = app.Services.GetRequiredService<IDocFileStore>();
        if (store is CMSAPI.Data.Storage.MinioDocFileStore minio)
            app.Logger.LogInformation("Docs file storage: on, {Where}.", minio.Describe());
        else
            app.Logger.LogWarning("Docs file storage: OFF. Uploading and opening files answer 503. Set Storage__PublicBaseUrl, Storage__Minio__AccessKey and Storage__Minio__SecretKey.");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Docs file storage: the settings are there but unusable ({Message}). Check Storage__PublicBaseUrl.", ex.Message);
    }

    // =============================
    // SEED FIRST CMS ADMIN (env-driven, idempotent)
    // =============================
    using (var scope = app.Services.CreateScope())
    {
        await scope.ServiceProvider.GetRequiredService<CmsAdminSeeder>().SeedAsync();
    }

    // =============================
    // VALIDATE PRODUCT DATABASES (per-platform readiness; logs, never crashes)
    // =============================
    await app.Services.GetRequiredService<ProductDatabaseValidator>().ValidateAsync();

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
    app.MapHub<CMSAPI.Hubs.VitaHub>("/hubs/vita"); // auth + vita.view enforced by [HasPermission] on the hub

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
