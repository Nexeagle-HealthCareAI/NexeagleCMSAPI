using Microsoft.EntityFrameworkCore;
using CMSAPI.Data;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Services;
using CMSAPI.Data.Repositories;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.HttpOverrides;


var builder = WebApplication.CreateBuilder(args);

// Load appsettings.Token.json for non-Development environments
if (!builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Token.json", optional: true, reloadOnChange: true);
}

// Add services to the container.
// register projects
builder.Services.AddControllers();

// Configure Forwarded Headers (Important for App Service)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// JWT Authentication
builder.Services.Configure<CMSAPI.Application.Models.TokenSettings>(builder.Configuration.GetSection("TokenSettings"));
builder.Services.AddScoped<IAuthService, AuthService>();

// Safe JWT Key Logic
var issuer = tokenSettings.GetValue<string>("Issuer");
var audience = tokenSettings.GetValue<string>("Audience");

// Fallback for Development if key is missing
if (string.IsNullOrEmpty(key) && builder.Environment.IsDevelopment())
{
    key = "DevSuperSecretKey_ChangeInProduction_1234567890"; // 32+ chars
}

if (string.IsNullOrEmpty(key))
{
    throw new InvalidOperationException("TokenSettings:Key is missing. Set 'TokenSettings__Key' in App Service Configuration.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // Require HTTPS in non-development
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = !string.IsNullOrEmpty(issuer),
        ValidIssuer = issuer,
        ValidateAudience = !string.IsNullOrEmpty(audience),
        ValidAudience = audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
    };
});

// Register EF Core AppDbContext using the configured connection string.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

// Register repository and service implementations.
// Dashboard
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<CMSAPI.Application.Interfaces.IHospitalRepository, CMSAPI.Data.Repositories.HospitalRepository>();
builder.Services.AddScoped<CMSAPI.Application.Interfaces.IHospitalService, CMSAPI.Application.Services.HospitalService>();

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer {token}'"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            new string[] { }
        }
    });
});

// Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

builder.Services.AddAuthorization();

// Configure Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

try
{
try 
{
    var app = builder.Build();

    app.UseForwardedHeaders();

    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    // Enable Swagger in all environments
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CMSAPI V1");
        c.InjectJavascript("/js/custom.js");
        c.DocumentTitle = "CMSAPI Dashboard";
    });

    app.UseStaticFiles();

    app.UseExceptionHandler(exceptionHandlerApp =>
    {
        exceptionHandlerApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
            
            // Log the exception
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError(exception, "Unhandled Exception in Middleware");

            var response = new CMSAPI.Application.Models.ErrorResponse 
            { 
                Code = "ServerError", 
                Message = "An unexpected error occurred.", 
                Details = builder.Environment.IsDevelopment() ? exception?.ToString() : null 
            };
            await context.Response.WriteAsJsonAsync(response);
        });
    });

    // Ensure authentication middleware runs before authorization
    app.UseAuthentication();

    app.UseHttpsRedirection();

    app.UseAuthorization();

    app.MapControllers();
    // Health Check (Public)
    app.MapHealthChecks("/health")
       .AllowAnonymous()
       .WithName("Health")
       .WithTags("Health");

    // Test Endpoint
    app.MapGet("/test", () => "API is running!").AllowAnonymous();

    // Redirect root to Swagger
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine("CRITICAL: Application Startup Failure!");
    Console.WriteLine(ex.ToString());
    throw;
}
