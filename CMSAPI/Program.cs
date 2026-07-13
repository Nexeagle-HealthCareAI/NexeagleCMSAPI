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
builder.Services.AddHttpClient();
builder.Services.AddControllers();
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
    // Login endpoint: max 10 attempts per IP per minute.
    options.AddFixedWindowLimiter(policyName: "login", configureOptions: limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Configure TokenSettings
builder.Services.Configure<TokenSettings>(
    builder.Configuration.GetSection("TokenSettings")
);

// Register Application Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IHospitalService, HospitalService>();

// Register Data Repositories
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<IHospitalRepository, HospitalRepository>();

// CMS identity / RBAC (CMSDatabase)
builder.Services.AddScoped<ICmsAuthRepository, CmsAuthRepository>();
builder.Services.AddScoped<ICmsPartnerRepository, CmsPartnerRepository>();
builder.Services.AddScoped<ICmsAdminRepository, CmsAdminRepository>();
builder.Services.AddScoped<ICmsAdminService, CmsAdminService>();
builder.Services.AddScoped<ICmsPartnerService, CmsPartnerService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();
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
    "http://localhost:5173",
    "http://localhost:5174",
    "http://localhost:5175",
    "https://nexeagle.com",
    "https://cms.nexeagle.com"
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
              .AllowCredentials(); // Required for SignalR
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
        // client passes the JWT as an "access_token" query param. Pick it up for /chathub.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chathub"))
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

// Per-platform product databases (EasyHMS / 1Rad). The factory reuses AppDbContext
// with a connection resolved from ApplicationName; the validator reports each
// product DB's readiness at startup. See ProductDbContextFactory for details.
builder.Services.AddSingleton<IProductDbContextFactory, ProductDbContextFactory>();
builder.Services.AddSingleton<ProductDatabaseValidator>();

// =============================
// HEALTH CHECKS
// =============================
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

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
    // VALIDATE PRODUCT DATABASES (per-platform readiness; logs, never crashes)
    // =============================
    await app.Services.GetRequiredService<ProductDatabaseValidator>().ValidateAsync();

    // =============================
    // CRITICAL FIX FOR AZURE
    // =============================
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto
    });

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
    app.MapHub<CMSAPI.Hubs.ChatHub>("/chathub").RequireRateLimiting("login").AllowAnonymous();

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
