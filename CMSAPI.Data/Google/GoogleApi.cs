using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CMSAPI.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace CMSAPI.Data.Google;

/// <summary>
/// Google's REST APIs over HttpClient, signed in with an account's OAuth client and refresh token.
///   Sign-in      accounts.google.com / oauth2.googleapis.com (offline access, so a refresh token comes back)
///   Analytics    analyticsadmin.googleapis.com v1beta (properties) and analyticsdata.googleapis.com v1beta (reports)
///   Google Ads   googleads.googleapis.com, version Google:AdsApiVersion (default v26). Access comes from the Google
///                Cloud project of the OAuth client (since Sep 2026); a developer token is optional
///                (Google:AdsDeveloperToken) and sent only when set.
///   Search Console  www.googleapis.com/webmasters/v3
///   Data Manager datamanager.googleapis.com v1 events:ingest (offline click conversions)
/// </summary>
public class GoogleApi : IGoogleApi
{
    public static readonly string[] Scopes =
    {
        "openid", "email",
        "https://www.googleapis.com/auth/analytics.readonly",
        "https://www.googleapis.com/auth/adwords",
        "https://www.googleapis.com/auth/webmasters.readonly",
        "https://www.googleapis.com/auth/datamanager",
    };

    private static readonly ConcurrentDictionary<string, (string Token, DateTime Until)> Tokens = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _adsVersion;
    private readonly string? _developerToken;

    public GoogleApi(HttpClient http, IConfiguration config)
    {
        _http = http;
        _adsVersion = string.IsNullOrWhiteSpace(config["Google:AdsApiVersion"]) ? "v26" : config["Google:AdsApiVersion"]!.Trim();
        _developerToken = string.IsNullOrWhiteSpace(config["Google:AdsDeveloperToken"]) ? null : config["Google:AdsDeveloperToken"]!.Trim();
    }

    // ── sign-in ──────────────────────────────────────────────────────────────

    public string SignInUrl(string clientId, string redirectUri, string state)
    {
        var q = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', Scopes),
            ["access_type"] = "offline",
            ["prompt"] = "consent",            // always returns a refresh token
            ["include_granted_scopes"] = "true",
            ["state"] = state,
        };
        return "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join('&', q.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
    }

    public async Task<GoogleTokens> ExchangeCodeAsync(string clientId, string clientSecret, string code, string redirectUri, CancellationToken ct = default)
    {
        var doc = await TokenCallAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["client_id"] = clientId, ["client_secret"] = clientSecret, ["redirect_uri"] = redirectUri,
        }, ct);
        return new GoogleTokens(Str(doc, "access_token") ?? string.Empty, Str(doc, "refresh_token"), Int(doc, "expires_in") ?? 3600, Str(doc, "scope"));
    }

    public async Task<string?> EmailAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var doc = await SendAsync(req, ct);
        return Str(doc, "email");
    }

    private async Task<JsonElement> TokenCallAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token") { Content = new FormUrlEncodedContent(form) };
        return await SendAsync(req, ct);
    }

    private async Task<string> AccessTokenAsync(GoogleClient c, CancellationToken ct)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(c.ClientId + "\n" + c.RefreshToken)));
        if (Tokens.TryGetValue(key, out var t) && t.Until > DateTime.UtcNow.AddMinutes(2)) return t.Token;
        var doc = await TokenCallAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = c.RefreshToken, ["client_id"] = c.ClientId, ["client_secret"] = c.ClientSecret,
        }, ct);
        var token = Str(doc, "access_token") ?? throw new GoogleApiException("Google did not return an access token.");
        Tokens[key] = (token, DateTime.UtcNow.AddSeconds(Int(doc, "expires_in") ?? 3600));
        return token;
    }

    private async Task<JsonElement> CallAsync(GoogleClient c, HttpMethod method, string url, object? body, CancellationToken ct, Action<HttpRequestMessage>? headers = null)
    {
        var token = await AccessTokenAsync(c, ct);
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        headers?.Invoke(req);
        return await SendAsync(req, ct);
    }

    private async Task<JsonElement> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        HttpResponseMessage res;
        try { res = await _http.SendAsync(req, ct); }
        catch (HttpRequestException ex) { throw new GoogleApiException($"Google could not be reached ({ex.Message}).", 0, "unreachable"); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new GoogleApiException("Google did not answer in time.", 0, "timeout"); }
        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            JsonElement doc = default;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { doc = JsonSerializer.Deserialize<JsonElement>(text); }
                catch (JsonException) { if (res.IsSuccessStatusCode) throw new GoogleApiException("Google answered with something that is not JSON.", (int)res.StatusCode); }
            }
            if (res.IsSuccessStatusCode) return doc;
            throw Refusal(doc, (int)res.StatusCode);
        }
    }

    /// <summary>Google's error in its own words: OAuth ({error, error_description}) or API ({error:{message,status,details}}).</summary>
    public static GoogleApiException Refusal(JsonElement doc, int status)
    {
        if (doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("error", out var e))
        {
            if (e.ValueKind == JsonValueKind.String)
            {
                var reason = e.GetString();
                var desc = Str(doc, "error_description");
                var words = reason == "invalid_grant"
                    ? "The Google sign-in has expired or was withdrawn. Sign in with Google again in Setup."
                    : $"Google refused the sign-in ({reason}{(desc != null ? ": " + desc : "")}).";
                return new GoogleApiException(words, status, reason);
            }
            if (e.ValueKind == JsonValueKind.Object)
            {
                var message = Str(e, "message") ?? "Google refused the request.";
                // Google Ads puts the useful words in details[].errors[].message.
                if (e.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                    foreach (var d in details.EnumerateArray())
                        if (d.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
                            foreach (var x in errs.EnumerateArray())
                                if (Str(x, "message") is { } m) { message = m; goto done; }
                done:
                return new GoogleApiException(message, status, Str(e, "status"));
            }
        }
        return new GoogleApiException($"Google answered {status}.", status);
    }

    // ── Analytics ────────────────────────────────────────────────────────────

    public async Task<List<GaProperty>> Ga4PropertiesAsync(GoogleClient c, CancellationToken ct = default)
    {
        var list = new List<GaProperty>();
        string? page = null;
        do
        {
            var url = "https://analyticsadmin.googleapis.com/v1beta/accountSummaries?pageSize=200" + (page != null ? "&pageToken=" + Uri.EscapeDataString(page) : "");
            var doc = await CallAsync(c, HttpMethod.Get, url, null, ct);
            foreach (var acc in Arr(doc, "accountSummaries"))
                foreach (var p in Arr(acc, "propertySummaries"))
                {
                    var id = (Str(p, "property") ?? string.Empty).Replace("properties/", "");
                    if (id.Length > 0) list.Add(new GaProperty { PropertyId = id, Name = Str(p, "displayName") ?? id, AccountName = Str(acc, "displayName") });
                }
            page = Str(doc, "nextPageToken");
        } while (!string.IsNullOrEmpty(page));
        return list;
    }

    public async Task<GaReport> RunReportAsync(GoogleClient c, string propertyId, GaReportRequest r, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object>
        {
            ["dateRanges"] = new[] { new { startDate = r.From.ToString("yyyy-MM-dd"), endDate = r.To.ToString("yyyy-MM-dd") } },
            ["dimensions"] = r.Dimensions.Select(d => new { name = d }).ToArray(),
            ["metrics"] = r.Metrics.Select(m => new { name = m }).ToArray(),
            ["limit"] = r.Limit,
            ["keepEmptyRows"] = false,
        };
        if (r.OrderByMetric != null) body["orderBys"] = new[] { new { metric = new { metricName = r.OrderByMetric }, desc = true } };
        if (r.InListDimension != null && r.InListValues is { Count: > 0 })
            body["dimensionFilter"] = new { filter = new { fieldName = r.InListDimension, inListFilter = new { values = r.InListValues } } };
        else if (r.ExactDimension != null && r.ExactValue != null)
            body["dimensionFilter"] = new { filter = new { fieldName = r.ExactDimension, stringFilter = new { matchType = "EXACT", value = r.ExactValue } } };

        var doc = await CallAsync(c, HttpMethod.Post, $"https://analyticsdata.googleapis.com/v1beta/properties/{Uri.EscapeDataString(propertyId)}:runReport", body, ct);
        var report = new GaReport();
        foreach (var row in Arr(doc, "rows"))
            report.Rows.Add(new GaRow
            {
                Dimensions = Arr(row, "dimensionValues").Select(v => Str(v, "value") ?? string.Empty).ToList(),
                Metrics = Arr(row, "metricValues").Select(v => double.TryParse(Str(v, "value"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0).ToList(),
            });
        return report;
    }

    // ── Google Ads ───────────────────────────────────────────────────────────

    private string AdsUrl(string path) => $"https://googleads.googleapis.com/{_adsVersion}/{path}";

    private void AdsHeaders(HttpRequestMessage req, string? loginCustomerId)
    {
        if (_developerToken != null) req.Headers.TryAddWithoutValidation("developer-token", _developerToken);
        if (!string.IsNullOrEmpty(loginCustomerId)) req.Headers.TryAddWithoutValidation("login-customer-id", loginCustomerId);
    }

    public async Task<List<AdsCustomer>> AdsCustomersAsync(GoogleClient c, CancellationToken ct = default)
    {
        var doc = await CallAsync(c, HttpMethod.Get, AdsUrl("customers:listAccessibleCustomers"), null, ct, r => AdsHeaders(r, null));
        var ids = Arr(doc, "resourceNames").Select(x => (x.GetString() ?? string.Empty).Replace("customers/", "")).Where(x => x.Length > 0).ToList();
        var result = new List<AdsCustomer>();
        foreach (var id in ids)
        {
            List<JsonElement> rows;
            try
            {
                rows = await AdsSearchAsync(c, id, null,
                    "SELECT customer.id, customer.descriptive_name, customer.currency_code, customer.time_zone, customer.manager FROM customer", ct);
            }
            catch (GoogleApiException) { continue; }   // e.g. a cancelled account the sign-in can still list
            foreach (var row in rows)
            {
                var cu = Obj(row, "customer");
                var manager = Bool(cu, "manager") ?? false;
                result.Add(new AdsCustomer { CustomerId = id, Name = Str(cu, "descriptiveName") ?? id, Currency = Str(cu, "currencyCode"), TimeZone = Str(cu, "timeZone"), IsManager = manager });
                if (!manager) continue;
                // A manager account: the client accounts under it, reached with login-customer-id = the manager.
                List<JsonElement> clients;
                try
                {
                    clients = await AdsSearchAsync(c, id, id,
                        "SELECT customer_client.id, customer_client.descriptive_name, customer_client.currency_code, customer_client.time_zone, customer_client.manager, customer_client.level FROM customer_client WHERE customer_client.level <= 1", ct);
                }
                catch (GoogleApiException) { continue; }
                foreach (var cr in clients)
                {
                    var cc = Obj(cr, "customerClient");
                    var cid = Str(cc, "id") ?? string.Empty;
                    if (cid.Length == 0 || cid == id || (Bool(cc, "manager") ?? false)) continue;
                    result.Add(new AdsCustomer { CustomerId = cid, Name = Str(cc, "descriptiveName") ?? cid, Currency = Str(cc, "currencyCode"), TimeZone = Str(cc, "timeZone"), LoginCustomerId = id });
                }
            }
        }
        // The same account can be listed directly and under its manager: keep the direct one.
        return result.GroupBy(x => x.CustomerId).Select(g => g.OrderBy(x => x.LoginCustomerId == null ? 0 : 1).First()).ToList();
    }

    public async Task<List<JsonElement>> AdsSearchAsync(GoogleClient c, string customerId, string? loginCustomerId, string gaql, CancellationToken ct = default)
    {
        var rows = new List<JsonElement>();
        string? page = null;
        var guard = 0;
        do
        {
            var body = new Dictionary<string, object> { ["query"] = gaql };
            if (page != null) body["pageToken"] = page;
            var doc = await CallAsync(c, HttpMethod.Post, AdsUrl($"customers/{Uri.EscapeDataString(customerId)}/googleAds:search"), body, ct, r => AdsHeaders(r, loginCustomerId));
            rows.AddRange(Arr(doc, "results").Select(x => x.Clone()));
            page = Str(doc, "nextPageToken");
        } while (!string.IsNullOrEmpty(page) && ++guard < 50);
        return rows;
    }

    // ── Search Console ───────────────────────────────────────────────────────

    public async Task<List<string>> SearchConsoleSitesAsync(GoogleClient c, CancellationToken ct = default)
    {
        var doc = await CallAsync(c, HttpMethod.Get, "https://www.googleapis.com/webmasters/v3/sites", null, ct);
        return Arr(doc, "siteEntry")
            .Where(s => Str(s, "permissionLevel") != "siteUnverifiedUser")
            .Select(s => Str(s, "siteUrl") ?? string.Empty).Where(s => s.Length > 0).ToList();
    }

    public async Task<List<GscRow>> SearchConsoleQueriesAsync(GoogleClient c, string site, DateOnly from, DateOnly to, string? pageUrl, int limit, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object>
        {
            ["startDate"] = from.ToString("yyyy-MM-dd"), ["endDate"] = to.ToString("yyyy-MM-dd"), ["dimensions"] = new[] { "query" }, ["rowLimit"] = limit,
        };
        if (pageUrl != null)
            body["dimensionFilterGroups"] = new[] { new { filters = new[] { new { dimension = "page", @operator = "equals", expression = pageUrl } } } };
        var doc = await CallAsync(c, HttpMethod.Post, $"https://www.googleapis.com/webmasters/v3/sites/{Uri.EscapeDataString(site)}/searchAnalytics/query", body, ct);
        return Arr(doc, "rows").Select(r => new GscRow
        {
            Key = Arr(r, "keys").Select(k => k.GetString()).FirstOrDefault() ?? string.Empty,
            Clicks = Num(r, "clicks") ?? 0, Impressions = Num(r, "impressions") ?? 0, Position = Num(r, "position") ?? 0,
        }).ToList();
    }

    // ── Data Manager ─────────────────────────────────────────────────────────

    public async Task<string?> IngestConversionsAsync(GoogleClient c, ConversionBatch batch, CancellationToken ct = default)
    {
        var destination = new Dictionary<string, object>
        {
            ["operatingAccount"] = new { accountType = "GOOGLE_ADS", accountId = batch.CustomerId },
            ["productDestinationId"] = batch.ConversionActionId,
        };
        if (!string.IsNullOrEmpty(batch.LoginCustomerId))
            destination["loginAccount"] = new { accountType = "GOOGLE_ADS", accountId = batch.LoginCustomerId };
        var events = batch.Events.Select(e =>
        {
            var ids = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(e.Gclid)) ids["gclid"] = e.Gclid;
            else if (!string.IsNullOrEmpty(e.Gbraid)) ids["gbraid"] = e.Gbraid;
            else if (!string.IsNullOrEmpty(e.Wbraid)) ids["wbraid"] = e.Wbraid;
            var ev = new Dictionary<string, object>
            {
                ["adIdentifiers"] = ids,
                ["eventTimestamp"] = DateTime.SpecifyKind(e.EventTimeUtc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["transactionId"] = e.TransactionId,
                ["eventSource"] = "WEB",
            };
            if (e.Value is { } v) { ev["conversionValue"] = v; ev["currency"] = e.Currency ?? "INR"; }
            return ev;
        }).ToList();
        var doc = await CallAsync(c, HttpMethod.Post, "https://datamanager.googleapis.com/v1/events:ingest",
            new Dictionary<string, object> { ["destinations"] = new[] { destination }, ["events"] = events, ["validateOnly"] = false }, ct);
        return Str(doc, "requestId");
    }

    // ── JSON helpers ─────────────────────────────────────────────────────────

    internal static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        } : null;

    internal static int? Int(JsonElement e, string name) => int.TryParse(Str(e, name), out var n) ? n : null;
    internal static double? Num(JsonElement e, string name) =>
        double.TryParse(Str(e, name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    internal static bool? Bool(JsonElement e, string name) => Str(e, name) switch { "true" => true, "false" => false, _ => null };
    internal static JsonElement Obj(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;
    internal static IEnumerable<JsonElement> Arr(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();
}
