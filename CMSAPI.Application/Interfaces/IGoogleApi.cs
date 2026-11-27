using System.Text.Json;

namespace CMSAPI.Application.Interfaces;

/// <summary>
/// The Google APIs CMS → Google uses, signed in with the account's own OAuth client and refresh token:
/// Google sign-in, Google Analytics (Admin + Data API), Google Ads (REST, GAQL), Search Console, and the
/// Data Manager API (CRM results sent to Google Ads as offline click conversions).
/// </summary>
public interface IGoogleApi
{
    /// <summary>The address Google sends people to for sign-in.</summary>
    string SignInUrl(string clientId, string redirectUri, string state);
    Task<GoogleTokens> ExchangeCodeAsync(string clientId, string clientSecret, string code, string redirectUri, CancellationToken ct = default);
    Task<string?> EmailAsync(string accessToken, CancellationToken ct = default);

    Task<List<GaProperty>> Ga4PropertiesAsync(GoogleClient c, CancellationToken ct = default);
    Task<GaReport> RunReportAsync(GoogleClient c, string propertyId, GaReportRequest r, CancellationToken ct = default);

    Task<List<AdsCustomer>> AdsCustomersAsync(GoogleClient c, CancellationToken ct = default);
    /// <summary>One GAQL query; every row of every page, as Google's JSON (camelCase).</summary>
    Task<List<JsonElement>> AdsSearchAsync(GoogleClient c, string customerId, string? loginCustomerId, string gaql, CancellationToken ct = default);

    Task<List<string>> SearchConsoleSitesAsync(GoogleClient c, CancellationToken ct = default);
    Task<List<GscRow>> SearchConsoleQueriesAsync(GoogleClient c, string site, DateOnly from, DateOnly to, string? pageUrl, int limit, CancellationToken ct = default);

    /// <summary>Sends click conversions; returns Google's request id.</summary>
    Task<string?> IngestConversionsAsync(GoogleClient c, ConversionBatch batch, CancellationToken ct = default);
}

/// <summary>What signs a call in: the account's OAuth client and the refresh token from its sign-in.</summary>
public record GoogleClient(string ClientId, string ClientSecret, string RefreshToken);

public record GoogleTokens(string AccessToken, string? RefreshToken, int ExpiresIn, string? Scope);

/// <summary>Google's refusal, with its HTTP status and reason ("PERMISSION_DENIED", "invalid_grant", …).</summary>
public class GoogleApiException : Exception
{
    public int HttpStatus { get; }
    public string? Reason { get; }
    public GoogleApiException(string message, int httpStatus = 0, string? reason = null) : base(message)
    {
        HttpStatus = httpStatus;
        Reason = reason;
    }
    /// <summary>The sign-in was withdrawn or expired: someone must sign in again.</summary>
    public bool SignInGone => Reason == "invalid_grant";
}

public class GaProperty
{
    public string PropertyId { get; set; } = string.Empty;   // digits
    public string Name { get; set; } = string.Empty;
    public string? AccountName { get; set; }
}

public class GaReportRequest
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<string> Dimensions { get; set; } = new();
    public List<string> Metrics { get; set; } = new();
    public int Limit { get; set; } = 1000;
    /// <summary>Sort by this metric, largest first.</summary>
    public string? OrderByMetric { get; set; }
    /// <summary>Keep rows whose dimension is one of these values.</summary>
    public string? InListDimension { get; set; }
    public List<string>? InListValues { get; set; }
    /// <summary>Keep rows whose dimension is exactly this value.</summary>
    public string? ExactDimension { get; set; }
    public string? ExactValue { get; set; }
}

public class GaReport
{
    public List<GaRow> Rows { get; set; } = new();
}

public class GaRow
{
    public List<string> Dimensions { get; set; } = new();
    public List<double> Metrics { get; set; } = new();
}

public class AdsCustomer
{
    public string CustomerId { get; set; } = string.Empty;   // digits
    public string Name { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public string? TimeZone { get; set; }
    public bool IsManager { get; set; }
    /// <summary>The manager account it is reached through (login-customer-id), if any.</summary>
    public string? LoginCustomerId { get; set; }
}

public class GscRow
{
    public string Key { get; set; } = string.Empty;   // the query
    public double Clicks { get; set; }
    public double Impressions { get; set; }
    public double Position { get; set; }
}

public class ConversionBatch
{
    public string CustomerId { get; set; } = string.Empty;
    public string? LoginCustomerId { get; set; }
    public string ConversionActionId { get; set; } = string.Empty;
    public List<ConversionEvent> Events { get; set; } = new();
}

public class ConversionEvent
{
    public string? Gclid { get; set; }
    public string? Gbraid { get; set; }
    public string? Wbraid { get; set; }
    public DateTime EventTimeUtc { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public string? Currency { get; set; }
}
