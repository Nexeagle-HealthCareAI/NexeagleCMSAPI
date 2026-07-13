using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CMSAPI.Data;

/// <summary>
/// Resolves an <see cref="AppDbContext"/> bound to the correct product database
/// (EasyHMS or 1Rad) based on a plan's <c>ApplicationName</c>. Both product
/// databases share the same schema and live on the same server, so a single
/// context type is reused with a per-platform connection string.
/// </summary>
public interface IProductDbContextFactory
{
    /// <summary>Canonical platform keys the CMS manages.</summary>
    IReadOnlyList<string> Platforms { get; }

    /// <summary>
    /// Maps an <c>ApplicationName</c> to its canonical platform key
    /// (case-insensitive), or <c>null</c> when it is not a known platform.
    /// </summary>
    string? NormalizePlatform(string? applicationName);

    /// <summary>
    /// Creates a new <see cref="AppDbContext"/> for the given platform.
    /// The caller owns the returned context and must dispose it.
    /// </summary>
    AppDbContext Create(string platform);

    /// <summary>The resolved connection string for a platform (diagnostics / health checks).</summary>
    string GetConnectionString(string platform);
}

/// <inheritdoc />
public sealed class ProductDbContextFactory : IProductDbContextFactory
{
    public const string EasyHms = "EasyHMS";
    public const string OneRad = "1Rad";

    /// <summary>Catalog used when deriving the 1Rad connection from DefaultConnection.</summary>
    private const string OneRadCatalog = "1RadDatabase";

    private static readonly IReadOnlyList<string> _platforms = new[] { EasyHms, OneRad };

    private readonly IReadOnlyDictionary<string, string> _connectionsByPlatform;

    public ProductDbContextFactory(IConfiguration configuration)
    {
        var defaultConn =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection missing.");

        // Prefer explicit per-platform connection strings; otherwise derive from
        // DefaultConnection using the same login/server and swapping only the catalog
        // (mirrors how Program.cs derives CmsConnection).
        var easyHmsConfigured = configuration.GetConnectionString("EasyHmsConnection");
        var easyHms = string.IsNullOrWhiteSpace(easyHmsConfigured)
            ? defaultConn                               // DefaultConnection already targets the HMS DB
            : easyHmsConfigured;

        var oneRadConfigured = configuration.GetConnectionString("OneRadConnection");
        var oneRad = string.IsNullOrWhiteSpace(oneRadConfigured)
            ? WithCatalog(defaultConn, OneRadCatalog)
            : oneRadConfigured;

        _connectionsByPlatform = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [EasyHms] = easyHms,
            [OneRad] = oneRad,
        };
    }

    public IReadOnlyList<string> Platforms => _platforms;

    public string? NormalizePlatform(string? applicationName)
    {
        if (string.IsNullOrWhiteSpace(applicationName)) return null;
        var trimmed = applicationName.Trim();
        foreach (var p in _platforms)
        {
            if (string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }

    public string GetConnectionString(string platform)
    {
        var key = NormalizePlatform(platform)
            ?? throw new ArgumentException($"Unknown platform '{platform}'.", nameof(platform));
        return _connectionsByPlatform[key];
    }

    public AppDbContext Create(string platform)
    {
        var conn = GetConnectionString(platform);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(conn, sql =>
            {
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
                sql.CommandTimeout(60);
            })
            .Options;

        return new AppDbContext(options);
    }

    /// <summary>Returns <paramref name="connectionString"/> with its catalog replaced by <paramref name="catalog"/>.</summary>
    private static string WithCatalog(string connectionString, string catalog) =>
        Regex.Replace(
            connectionString,
            @"(?i)(Initial Catalog|Database)\s*=\s*[^;]+",
            "Initial Catalog=" + catalog);
}
