using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;

namespace CMSAPI.Services;

// Calls the Vita Test Agent control API (vita/api/app.py). Vita keeps its own database (runs, cases, verdicts, the PR gate), which
// this API does not open: every rule is enforced once, in Vita. This lives in CMSAPI (not CMSAPI.Application) because it uses
// the named HttpClient "Vita" from IHttpClientFactory, which AddVitaProxy registers (see VitaProxyExtensions).
// See Vita:BaseUrl and ServiceAuth:VitaServiceKey (the same value as Vita's VITA_SERVICE_KEY).
public class VitaProxyService : IVitaProxyService
{
    public const string HttpClientName = "Vita";
    public const string LongHttpClientName = "VitaLong";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VitaProxyService> _logger;

    private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web)
    {
        // Vita is a FastAPI/pydantic service: its field names are snake_case (actor, reason, manifest_path, ...).
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public VitaProxyService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<VitaProxyService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<VitaProxyResponse> GetAsync(string path, string? query, string actor)
    {
        var url = string.IsNullOrEmpty(query) ? path : $"{path}?{query}";
        return RelayAsync(() => new HttpRequestMessage(HttpMethod.Get, url), actor, false);
    }

    public Task<VitaProxyResponse> SendAsync(HttpMethod method, string path, JsonElement? body, string actor, bool longRunning = false) =>
        RelayAsync(() =>
        {
            var request = new HttpRequestMessage(method, path);
            if (body.HasValue && body.Value.ValueKind != JsonValueKind.Undefined && body.Value.ValueKind != JsonValueKind.Null)
                request.Content = new StringContent(body.Value.GetRawText(), Encoding.UTF8, "application/json");
            return request;
        }, actor, longRunning);

    public Task<VitaProxyResponse> SendObjectAsync(HttpMethod method, string path, object? body, string actor, bool longRunning = false) =>
        RelayAsync(() =>
        {
            var request = new HttpRequestMessage(method, path);
            if (body != null)
                request.Content = new StringContent(JsonSerializer.Serialize(body, BodyOptions), Encoding.UTF8, "application/json");
            return request;
        }, actor, longRunning);

    private async Task<VitaProxyResponse> RelayAsync(Func<HttpRequestMessage> build, string actor, bool longRunning)
    {
        var baseUrl = Clean(_configuration["Vita:BaseUrl"])?.TrimEnd('/');
        // Vita's CMS-scoped key (VITA_CMS_KEY: everything but run imports); the shared VitaServiceKey for older setups.
        var key = Clean(_configuration["ServiceAuth:VitaCmsKey"]) ?? Clean(_configuration["ServiceAuth:VitaServiceKey"]);
        if (baseUrl == null || key == null)
        {
            _logger.LogWarning("Vita call skipped: Vita:BaseUrl or ServiceAuth:VitaServiceKey isn't configured.");
            return VitaProxyResponse.Fail(503, "Vita Test Agent is not connected on this environment yet.");
        }

        using var request = build();
        request.RequestUri = new Uri($"{baseUrl}/{request.RequestUri!.OriginalString.TrimStart('/')}");
        request.Headers.Add("X-Service-Key", key);
        var who = SafeActor(actor);
        request.Headers.Add("X-Actor", who);
        // Optional: sign the actor so a leaked CMS key alone cannot name someone else (Vita's VITA_ACTOR_SECRET).
        var actorSecret = Clean(_configuration["ServiceAuth:VitaActorSecret"]);
        if (actorSecret != null)
        {
            var (ts, sig) = SignActor(actorSecret, who, DateTimeOffset.UtcNow);
            request.Headers.Add("X-Actor-Ts", ts);
            request.Headers.Add("X-Actor-Sig", sig);
        }

        var client = _httpClientFactory.CreateClient(longRunning ? LongHttpClientName : HttpClientName);
        try
        {
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            var json = TryParse(text);
            return response.IsSuccessStatusCode
                ? new VitaProxyResponse { Success = true, StatusCode = (int)response.StatusCode, Data = json }
                : Failure(response.StatusCode, json);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Vita Test Agent call timed out.");
            return VitaProxyResponse.Fail(504, "Vita Test Agent took too long to answer. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not reach Vita Test Agent.");
            return VitaProxyResponse.Fail(502, "Vita Test Agent could not be reached. Please try again.");
        }
    }

    // Vita refusing the key is a problem between the two servers, not with the CMS user's session, so it is never answered as a
    // 401 or 403: the CMS web would take that as "signed out" and log the person out.
    private static VitaProxyResponse Failure(HttpStatusCode status, JsonNode? json)
    {
        // FastAPI answers errors as { "detail": "..." } (or a list of validation errors); pydantic validation is 422.
        var message = json?["detail"] is JsonValue v && v.TryGetValue<string>(out var s) ? s
            : json?["detail"] is JsonArray a ? string.Join("; ", a.Select(x => x?["msg"]?.ToString() ?? x?.ToString() ?? ""))
            : json?["message"]?.GetValue<string>();
        switch ((int)status)
        {
            case 400 or 404 or 409 or 413 or 422 or 429:
                return VitaProxyResponse.Fail((int)status, message ?? "The request could not be completed.");
            case 401 or 403:
                return VitaProxyResponse.Fail(502, "Vita Test Agent did not accept this server's key. Please contact the platform team.");
            case 503:
                return VitaProxyResponse.Fail(503, message ?? "Vita Test Agent is not available on this environment yet.");
            default:
                return VitaProxyResponse.Fail(502, message ?? "Vita Test Agent is not available right now. Please try again.");
        }
    }

    // A person's email goes into an HTTP header: no line breaks or control characters, and not unreasonably long.
    public static string SafeActor(string? actor)
    {
        var cleaned = new StringBuilder();
        foreach (var ch in actor ?? string.Empty)
            if (!char.IsControl(ch) && ch < 128) cleaned.Append(ch);
        var text = cleaned.ToString().Trim();
        return text.Length == 0 ? "CMS" : text.Length > 200 ? text[..200] : text;
    }

    // hex(HMAC-SHA256(secret, actor + "|" + ts)), ts = Unix seconds; Vita accepts it for 300 s (vita/api/auth.py).
    public static (string Ts, string Sig) SignActor(string secret, string actor, DateTimeOffset now)
    {
        var ts = now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var h = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes($"{actor}|{ts}"))).ToLowerInvariant();
        return (ts, sig);
    }

    private static JsonNode? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonNode.Parse(text); }
        catch (JsonException) { return null; }
    }

    private static string? Clean(string? value)
    {
        var v = value?.Trim();
        // The committed placeholder ("<set-via-env-...>") must never act as a real value.
        return string.IsNullOrEmpty(v) || v.StartsWith("<") ? null : v;
    }
}
