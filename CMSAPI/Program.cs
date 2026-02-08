using Microsoft.EntityFrameworkCore;
using CMSAPI.Data;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Services;
using CMSAPI.Data.Repositories;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;


var builder = WebApplication.CreateBuilder(args);

// Load appsettings.Token.json for non-Development environments
if (!builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Token.json", optional: true, reloadOnChange: true);
}

// Add services to the container.
// register projects
builder.Services.AddControllers();

// JWT Authentication
builder.Services.Configure<CMSAPI.Application.Models.TokenSettings>(builder.Configuration.GetSection("TokenSettings"));
builder.Services.AddScoped<IAuthService, AuthService>();

var tokenSettings = builder.Configuration.GetSection("TokenSettings");
var key = tokenSettings.GetValue<string>("Key") ?? "DevSuperSecretKey_ChangeInProduction_1234567890";
var issuer = tokenSettings.GetValue<string>("Issuer");
var audience = tokenSettings.GetValue<string>("Audience");
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

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
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

app.Run();
