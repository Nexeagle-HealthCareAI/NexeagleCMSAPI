using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data;

/// <summary>
/// Validates at startup that each configured product database (EasyHMS, 1Rad)
/// is reachable and contains the tables and columns the CMS depends on.
/// Reports per-platform readiness to the log and never throws, so one
/// unavailable product database does not take the whole API down.
/// </summary>
public sealed class ProductDatabaseValidator
{
    /// <summary>Tables (and the columns we read/write) each product database must expose.</summary>
    private static readonly (string Table, string[] Columns)[] Required =
    {
        ("Hospitals", new[] { "HospitalID", "Name", "IsActive" }),
        ("HospitalSubscriptions", new[]
        {
            "HospitalSubscriptionId", "HospitalId", "PlanId", "Status",
            "TrialStartDate", "TrialEndDate", "SubscriptionStartDate",
            "SubscriptionEndDate", "NextBillingDate", "UpdatedAt",
        }),
    };

    private readonly IProductDbContextFactory _factory;
    private readonly ILogger<ProductDatabaseValidator> _logger;

    public ProductDatabaseValidator(
        IProductDbContextFactory factory,
        ILogger<ProductDatabaseValidator> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <summary>Validates every configured platform and logs the outcome.</summary>
    public async Task ValidateAsync(CancellationToken ct = default)
    {
        foreach (var platform in _factory.Platforms)
        {
            var catalog = CatalogOf(_factory.GetConnectionString(platform));
            try
            {
                await using var db = _factory.Create(platform);
                var conn = db.Database.GetDbConnection();
                await conn.OpenAsync(ct);

                var missing = new List<string>();
                foreach (var (table, columns) in Required)
                {
                    var present = await GetColumnsAsync(conn, table, ct);
                    if (present.Count == 0)
                    {
                        missing.Add($"table {table}");
                        continue;
                    }
                    foreach (var col in columns)
                    {
                        if (!present.Contains(col))
                            missing.Add($"{table}.{col}");
                    }
                }

                if (missing.Count == 0)
                {
                    _logger.LogInformation(
                        "[ProductDb] {Platform} ({Catalog}): OK — schema validated.",
                        platform, catalog);
                }
                else
                {
                    _logger.LogError(
                        "[ProductDb] {Platform} ({Catalog}): schema mismatch — missing {Missing}. " +
                        "Endpoints for this platform will fail until the database is fixed.",
                        platform, catalog, string.Join(", ", missing));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[ProductDb] {Platform} ({Catalog}): NOT reachable. " +
                    "Endpoints for this platform will be unavailable.",
                    platform, catalog);
            }
        }
    }

    private static async Task<HashSet<string>> GetColumnsAsync(
        DbConnection conn, string table, CancellationToken ct)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table";
        var p = cmd.CreateParameter();
        p.ParameterName = "@table";
        p.Value = table;
        cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            columns.Add(reader.GetString(0));
        return columns;
    }

    /// <summary>Extracts the catalog name from a connection string for logging.</summary>
    private static string CatalogOf(string connectionString)
    {
        var m = Regex.Match(
            connectionString,
            @"(?i)(?:Initial Catalog|Database)\s*=\s*([^;]+)");
        return m.Success ? m.Groups[1].Value.Trim() : "?";
    }
}
