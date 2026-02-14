using CMSAPI.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// =============================
// CONFIGURATION
// =============================
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
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCors", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
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

// =============================
// HEALTH CHECKS
// =============================
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

// =============================
// AUTHORIZATION
// =============================
builder.Services.AddAuthorization();


// =============================
// BUILD APP
// =============================
try
{
    var app = builder.Build();

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
