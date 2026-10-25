using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Application.Services;

// Calls 1HMSAPI's /internal/emergency routes (EmergencyInternalController): hospitals' emergency opt-ins to verify or reject, places
// that are not on 1HMS (added and verified here), and the emergency alert log. Every rule is enforced once, in 1HMSAPI. Same address
// and key as the Health Wiki (EasyHmsApi:BaseUrl, ServiceAuth:HealthWikiServiceKey = 1HMSAPI's internal key).
public class EmergencyTakeoverService : IEmergencyTakeoverService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmergencyTakeoverService> _logger;

    public EmergencyTakeoverService(HttpClient httpClient, IConfiguration configuration, ILogger<EmergencyTakeoverService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<HealthWikiProxyResponse> GetAsync(string path, string? query, string actor) =>
        RelayAsync(() => new HttpRequestMessage(HttpMethod.Get, string.IsNullOrEmpty(query) ? path : $"{path}?{query}"), actor);

    public Task<HealthWikiProxyResponse> SendAsync(HttpMethod method, string path, JsonElement? body, string actor) =>
        RelayAsync(() =>
        {
            var request = new HttpRequestMessage(method, path);
            if (body.HasValue && body.Value.ValueKind != JsonValueKind.Undefined)
                request.Content = new StringContent(body.Value.GetRawText(), Encoding.UTF8, "application/json");
            return request;
        }, actor);

    private async Task<HealthWikiProxyResponse> RelayAsync(Func<HttpRequestMessage> build, string actor)
    {
        var baseUrl = Clean(_configuration["EasyHmsApi:BaseUrl"])?.TrimEnd('/');
        var key = Clean(_configuration["ServiceAuth:HealthWikiServiceKey"]);
        if (baseUrl == null || key == null)
        {
            _logger.LogWarning("Emergency takeover call skipped: EasyHmsApi:BaseUrl or ServiceAuth:HealthWikiServiceKey isn't configured.");
            return HealthWikiProxyResponse.Fail(503, "Emergency takeover is not connected on this environment yet.");
        }

        using var request = build();
        request.RequestUri = new Uri($"{baseUrl}/{request.RequestUri!.OriginalString.TrimStart('/')}");
        request.Headers.Add("X-Internal-Key", key);
        request.Headers.Add("X-Actor", HealthWikiService.SafeActor(actor));
        try
        {
            using var response = await _httpClient.SendAsync(request);
            var json = TryParse(await response.Content.ReadAsStringAsync());
            if (response.IsSuccessStatusCode)
                return new HealthWikiProxyResponse { Success = true, StatusCode = (int)response.StatusCode, Data = json };
            var message = json?["message"]?.GetValue<string>();
            return (int)response.StatusCode switch
            {
                400 or 404 or 409 or 422 or 429 => HealthWikiProxyResponse.Fail((int)response.StatusCode, message ?? "The request could not be completed."),
                // a key problem between the two servers, never the CMS user's session (a 401 here would sign them out)
                401 or 403 => HealthWikiProxyResponse.Fail(502, "1HMS did not accept this server. Please contact the platform team."),
                _ => HealthWikiProxyResponse.Fail(502, message ?? "1HMS is not available right now. Please try again."),
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "1HMSAPI emergency call timed out.");
            return HealthWikiProxyResponse.Fail(504, "1HMS took too long to answer. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not reach 1HMSAPI for emergency takeover.");
            return HealthWikiProxyResponse.Fail(502, "1HMS could not be reached. Please try again.");
        }
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
        return string.IsNullOrEmpty(v) || v.StartsWith("<") ? null : v;
    }
}
