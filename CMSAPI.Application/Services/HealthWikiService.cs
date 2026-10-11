using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Application.Services;

// Calls 1HMSAPI's /internal/health-articles and /internal/health-wiki routes. The Health Wiki's tables are in easyHMSDatabase, which
// this API does not open: every rule (who may publish, the reviewer badge, edits to a live article) is enforced once, in 1HMSAPI.
// See EasyHmsApi:BaseUrl and ServiceAuth:HealthWikiServiceKey (the same value as 1HMSAPI's Internal:HealthArticlesKey).
public class HealthWikiService : IHealthWikiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HealthWikiService> _logger;

    public HealthWikiService(HttpClient httpClient, IConfiguration configuration, ILogger<HealthWikiService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<HealthWikiProxyResponse> GetAsync(string path, string? query, string actor, HealthWikiShape shape = HealthWikiShape.Whole)
    {
        var url = string.IsNullOrEmpty(query) ? path : $"{path}?{query}";
        return RelayAsync(() => new HttpRequestMessage(HttpMethod.Get, url), actor, shape);
    }

    public Task<HealthWikiProxyResponse> SendAsync(HttpMethod method, string path, JsonElement? body, string actor, HealthWikiShape shape) =>
        RelayAsync(() =>
        {
            var request = new HttpRequestMessage(method, path);
            if (body.HasValue && body.Value.ValueKind != JsonValueKind.Undefined)
                request.Content = new StringContent(body.Value.GetRawText(), Encoding.UTF8, "application/json");
            return request;
        }, actor, shape);

    public Task<HealthWikiProxyResponse> UploadImageAsync(string fileName, string contentType, Stream content, string actor) =>
        RelayAsync(() =>
        {
            var file = new StreamContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
            var form = new MultipartFormDataContent { { file, "file", string.IsNullOrWhiteSpace(fileName) ? "image" : fileName } };
            return new HttpRequestMessage(HttpMethod.Post, "internal/health-articles/images") { Content = form };
        }, actor, HealthWikiShape.Whole);

    private async Task<HealthWikiProxyResponse> RelayAsync(Func<HttpRequestMessage> build, string actor, HealthWikiShape shape)
    {
        var baseUrl = Clean(_configuration["EasyHmsApi:BaseUrl"])?.TrimEnd('/');
        var key = Clean(_configuration["ServiceAuth:HealthWikiServiceKey"]);
        if (baseUrl == null || key == null)
        {
            _logger.LogWarning("Health Wiki call skipped: EasyHmsApi:BaseUrl or ServiceAuth:HealthWikiServiceKey isn't configured.");
            return HealthWikiProxyResponse.Fail(503, "The Health Wiki is not connected on this environment yet.");
        }

        using var request = build();
        request.RequestUri = new Uri($"{baseUrl}/{request.RequestUri!.OriginalString.TrimStart('/')}");
        request.Headers.Add("X-Internal-Key", key);
        request.Headers.Add("X-Actor", SafeActor(actor));

        try
        {
            using var response = await _httpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            var json = TryParse(text);
            return response.IsSuccessStatusCode ? Success(response.StatusCode, json, shape) : Failure(response.StatusCode, json);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "1HMSAPI Health Wiki call timed out.");
            return HealthWikiProxyResponse.Fail(504, "The Health Wiki took too long to answer. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not reach 1HMSAPI for the Health Wiki.");
            return HealthWikiProxyResponse.Fail(502, "The Health Wiki could not be reached. Please try again.");
        }
    }

    private static HealthWikiProxyResponse Success(HttpStatusCode status, JsonNode? json, HealthWikiShape shape)
    {
        var result = new HealthWikiProxyResponse { Success = true, StatusCode = (int)status };
        switch (shape)
        {
            case HealthWikiShape.Article:
                result.Data = Detach(json?["article"]);
                break;
            case HealthWikiShape.Contributor:
                result.Data = Detach(json?["contributor"]);
                result.Link = json?["link"]?.GetValue<string>();
                result.LinkDelivered = json?["linkDelivered"]?.GetValue<bool>();
                break;
            default:
                result.Data = json;
                break;
        }
        ShowLinkSentAsText(result.Data);
        return result;
    }

    // 1HMSAPI refusing the key is a problem between the two servers, not with the CMS user's session, so it is never answered as a
    // 401 or 403: the CMS web would take that as "signed out" and log the person out.
    private static HealthWikiProxyResponse Failure(HttpStatusCode status, JsonNode? json)
    {
        var message = json?["message"]?.GetValue<string>();
        var diagnostic = json?["diagnostic"]?.GetValue<string>();
        switch ((int)status)
        {
            case 400 or 404 or 409 or 413 or 422 or 429:
                return HealthWikiProxyResponse.Fail((int)status, message ?? "The request could not be completed.", diagnostic);
            case 401 or 403:
                return HealthWikiProxyResponse.Fail(502, "The Health Wiki service did not accept this server. Please contact the platform team.");
            case 503:
                return HealthWikiProxyResponse.Fail(503, message ?? "The Health Wiki is not available on this environment yet.");
            default:
                return HealthWikiProxyResponse.Fail(502, message ?? "The Health Wiki is not available right now. Please try again.", diagnostic);
        }
    }

    // The CMS shows "Link sent {linkSentAt}" as plain text, so a timestamp becomes "11 Oct 2026, 12:12 PM IST".
    internal static void ShowLinkSentAsText(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array) ShowLinkSentAsText(item);
        }
        else if (node is JsonObject obj && obj.TryGetPropertyValue("linkSentAt", out var value) && value is JsonValue v && v.TryGetValue<string>(out var text)
                 && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
        {
            obj["linkSentAt"] = utc.AddHours(5.5).ToString("d MMM yyyy, h:mm tt", CultureInfo.InvariantCulture) + " IST";
        }
    }

    // A person's name goes into an HTTP header: no line breaks or control characters, and not unreasonably long.
    public static string SafeActor(string? actor)
    {
        var cleaned = new StringBuilder();
        foreach (var ch in actor ?? string.Empty)
            if (!char.IsControl(ch) && ch < 128) cleaned.Append(ch);
        var text = cleaned.ToString().Trim();
        return text.Length == 0 ? "CMS" : text.Length > 200 ? text[..200] : text;
    }

    private static JsonNode? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonNode.Parse(text); }
        catch (JsonException) { return null; }
    }

    // A node can have only one parent, so a piece taken out of the upstream answer is copied before it is reused.
    private static JsonNode? Detach(JsonNode? node) => node?.DeepClone();

    private static string? Clean(string? value)
    {
        var v = value?.Trim();
        // The committed placeholder ("<set-via-env-...>") must never act as a real value.
        return string.IsNullOrEmpty(v) || v.StartsWith("<") ? null : v;
    }
}
