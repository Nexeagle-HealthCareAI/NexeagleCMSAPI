using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CMSAPI.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.WhatsApp;

/// <summary>
/// The marketing number on Meta's WhatsApp Cloud API (graph.facebook.com).
///
/// Settings (environment names in brackets), written by the deploy from GitHub secrets:
///   WhatsAppMarketing:PhoneNumberId  [WhatsAppMarketing__PhoneNumberId]  the number's id in WhatsApp Manager (not the phone number)
///   WhatsAppMarketing:WabaId         [WhatsAppMarketing__WabaId]         the WhatsApp Business Account id
///   WhatsAppMarketing:AccessToken    [WhatsAppMarketing__AccessToken]    a system user's permanent token with
///                                                                         whatsapp_business_messaging and whatsapp_business_management
///   WhatsAppMarketing:AppId          [WhatsAppMarketing__AppId]          the Meta app's id: needed to upload template samples and the profile photo
///   WhatsAppMarketing:GraphVersion   [WhatsAppMarketing__GraphVersion]   default v23.0
/// The webhook's app secret and verify token are read by WhatsAppWebhookController.
/// </summary>
public class GraphWaMeta : IWaMeta
{
    private readonly HttpClient _http;
    private readonly ILogger<GraphWaMeta> _logger;
    private readonly string? _phoneId;
    private readonly string? _wabaId;
    private readonly string? _token;
    private readonly string? _appId;
    private readonly string _base;

    public GraphWaMeta(HttpClient http, IConfiguration config, ILogger<GraphWaMeta> logger)
    {
        _http = http;
        _logger = logger;
        _phoneId = Clean(config["WhatsAppMarketing:PhoneNumberId"]);
        _wabaId = Clean(config["WhatsAppMarketing:WabaId"]);
        _token = Clean(config["WhatsAppMarketing:AccessToken"]);
        _appId = Clean(config["WhatsAppMarketing:AppId"]);
        var version = Clean(config["WhatsAppMarketing:GraphVersion"]) ?? "v23.0";
        _base = $"https://graph.facebook.com/{version}/";
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public bool IsConfigured => _phoneId != null && _wabaId != null && _token != null;

    private void Ready()
    {
        if (!IsConfigured) throw new WaNotConnectedException();
    }

    private HttpRequestMessage Request(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path.StartsWith("https://") ? path : _base + path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return request;
    }

    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    /// <summary>Sends and reads Meta's answer; Meta's error becomes a WaMetaException with its words.</summary>
    private async Task<JsonNode> CallAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new WaMetaException("WhatsApp (Meta) cannot be reached right now. Try again in a minute.", null, 503, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new WaMetaException("WhatsApp (Meta) did not answer in time. Try again in a minute.", null, 504, ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonNode? body = null;
            try { body = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); } catch (JsonException) { }
            if (response.IsSuccessStatusCode) return body ?? new JsonObject();

            var error = body?["error"];
            var code = error?["code"].Int();
            var subcode = error?["error_subcode"].Int();
            var message = error?["error_user_msg"].Str() ?? error?["message"].Str() ?? $"Meta answered {(int)response.StatusCode}.";
            var details = error?["error_data"]?["details"].Str();
            if (!string.IsNullOrWhiteSpace(details) && !message.Contains(details, StringComparison.OrdinalIgnoreCase)) message += " " + details;
            _logger.LogWarning("WhatsApp marketing: Meta refused {Method} {Path}: {Status} code {Code}/{Subcode} {Message} (trace {Trace})",
                request.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode, code, subcode, message, error?["fbtrace_id"].Str());
            // A bad or expired token is ours to fix, not the person's: say so plainly.
            if (code == 190) message = "The WhatsApp access token is not valid any more. An administrator needs to renew WhatsAppMarketing__AccessToken.";
            throw new WaMetaException(message, code, (int)response.StatusCode is 400 or 404 ? 400 : 502);
        }
    }

    public async Task<string> SendAsync(JsonObject message, CancellationToken ct = default)
    {
        Ready();
        var body = new JsonObject { ["messaging_product"] = "whatsapp", ["recipient_type"] = "individual" };
        foreach (var (key, value) in message) body[key] = value?.DeepClone();
        var answer = await CallAsync(Request(HttpMethod.Post, $"{_phoneId}/messages", Json(body)), ct);
        return answer["messages"]?[0]?["id"].Str() ?? throw new WaMetaException("Meta did not return a message id.");
    }

    public async Task<string> UploadMediaAsync(byte[] data, string mime, string fileName, CancellationToken ct = default)
    {
        Ready();
        var form = new MultipartFormDataContent
        {
            { new StringContent("whatsapp"), "messaging_product" },
            { new StringContent(mime), "type" },
        };
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(file, "file", fileName);
        var answer = await CallAsync(Request(HttpMethod.Post, $"{_phoneId}/media", form), ct);
        return answer["id"].Str() ?? throw new WaMetaException("Meta did not return a media id.");
    }

    public async Task<WaMediaFile> DownloadMediaAsync(string mediaId, CancellationToken ct = default)
    {
        Ready();
        var info = await CallAsync(Request(HttpMethod.Get, Uri.EscapeDataString(mediaId)), ct);
        var url = info["url"].Str() ?? throw new WaMetaException("Meta has no file for this message any more.", null, 404);
        using var response = await _http.SendAsync(Request(HttpMethod.Get, url), ct);
        if (!response.IsSuccessStatusCode) throw new WaMetaException("The file could not be fetched from WhatsApp.", null, 502);
        return new WaMediaFile
        {
            Data = await response.Content.ReadAsByteArrayAsync(ct),
            Mime = info["mime_type"].Str() ?? response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
        };
    }

    public async Task<string> UploadSampleAsync(byte[] data, string mime, string fileName, CancellationToken ct = default)
    {
        Ready();
        if (_appId == null) throw new WaMetaException("Uploading pictures to Meta needs WhatsAppMarketing__AppId (the Meta app's id).", null, 503);
        // Resumable Upload API: open a session on the app, then send the bytes to it.
        var open = await CallAsync(Request(HttpMethod.Post,
            $"{_appId}/uploads?file_name={Uri.EscapeDataString(fileName)}&file_length={data.Length}&file_type={Uri.EscapeDataString(mime)}"), ct);
        var session = open["id"].Str() ?? throw new WaMetaException("Meta did not open an upload.");
        var send = new HttpRequestMessage(HttpMethod.Post, _base + session) { Content = new ByteArrayContent(data) };
        send.Headers.TryAddWithoutValidation("Authorization", $"OAuth {_token}");
        send.Headers.TryAddWithoutValidation("file_offset", "0");
        var done = await CallAsync(send, ct);
        return done["h"].Str() ?? throw new WaMetaException("Meta did not return the uploaded file's handle.");
    }

    public async Task<WaMetaTemplate> CreateTemplateAsync(JsonObject template, CancellationToken ct = default)
    {
        Ready();
        var answer = await CallAsync(Request(HttpMethod.Post, $"{_wabaId}/message_templates", Json(template)), ct);
        return new WaMetaTemplate
        {
            Id = answer["id"].Str(),
            Name = template["name"].Str() ?? string.Empty,
            Language = template["language"].Str() ?? string.Empty,
            Category = answer["category"].Str() ?? template["category"].Str() ?? string.Empty,
            Status = answer["status"].Str() ?? "PENDING",
            Components = template["components"]?.ToJsonString() ?? "[]",
        };
    }

    public async Task<List<WaMetaTemplate>> ListTemplatesAsync(CancellationToken ct = default)
    {
        Ready();
        var list = new List<WaMetaTemplate>();
        string? next = $"{_wabaId}/message_templates?fields=id,name,language,category,status,rejected_reason,quality_score,parameter_format,components&limit=100";
        for (var page = 0; next != null && page < 20; page++)
        {
            var answer = await CallAsync(Request(HttpMethod.Get, next), ct);
            foreach (var t in answer["data"]?.AsArray() ?? new JsonArray())
            {
                if (t == null) continue;
                list.Add(new WaMetaTemplate
                {
                    Id = t["id"].Str(),
                    Name = t["name"].Str() ?? string.Empty,
                    Language = t["language"].Str() ?? string.Empty,
                    Category = t["category"].Str() ?? string.Empty,
                    Status = t["status"].Str() ?? string.Empty,
                    RejectedReason = t["rejected_reason"].Str() is { } r && r != "NONE" ? r : null,
                    QualityScore = t["quality_score"]?["score"].Str(),
                    ParameterFormat = t["parameter_format"].Str(),
                    Components = t["components"]?.ToJsonString() ?? "[]",
                });
            }
            next = answer["paging"]?["next"].Str();
        }
        return list;
    }

    public async Task DeleteTemplateAsync(string name, string? metaTemplateId, CancellationToken ct = default)
    {
        Ready();
        var path = $"{_wabaId}/message_templates?name={Uri.EscapeDataString(name)}" + (metaTemplateId != null ? $"&hsm_id={Uri.EscapeDataString(metaTemplateId)}" : string.Empty);
        await CallAsync(Request(HttpMethod.Delete, path), ct);
    }

    public async Task<JsonObject> GetProfileAsync(CancellationToken ct = default)
    {
        Ready();
        var answer = await CallAsync(Request(HttpMethod.Get, $"{_phoneId}/whatsapp_business_profile?fields=about,address,description,email,profile_picture_url,websites,vertical"), ct);
        return answer["data"]?[0]?.AsObject() is { } profile ? (JsonObject)profile.DeepClone() : new JsonObject();
    }

    public async Task UpdateProfileAsync(JsonObject profile, CancellationToken ct = default)
    {
        Ready();
        var body = (JsonObject)profile.DeepClone();
        body["messaging_product"] = "whatsapp";
        await CallAsync(Request(HttpMethod.Post, $"{_phoneId}/whatsapp_business_profile", Json(body)), ct);
    }

    public async Task<JsonObject> GetNumberAsync(CancellationToken ct = default)
    {
        Ready();
        var answer = await CallAsync(Request(HttpMethod.Get, $"{_phoneId}?fields=display_phone_number,verified_name,quality_rating,name_status,throughput"), ct);
        var number = (JsonObject)answer.AsObject().DeepClone();
        // The messaging limit is asked for separately: Meta has been moving it, and a field it no longer
        // knows would fail the whole call.
        try
        {
            var limit = await CallAsync(Request(HttpMethod.Get, $"{_phoneId}?fields=messaging_limit_tier"), ct);
            if (limit["messaging_limit_tier"] is { } tier) number["messaging_limit_tier"] = tier.DeepClone();
        }
        catch (WaMetaException) { }
        return number;
    }

    public async Task<List<WaPricePoint>> PricingAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        Ready();
        long Unix(DateTime d) => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var field = $"pricing_analytics.start({Unix(fromUtc)}).end({Unix(toUtc)}).granularity(DAILY)" +
                    ".dimensions([\"PRICING_CATEGORY\",\"PRICING_TYPE\",\"COUNTRY\",\"PHONE\"])";
        var answer = await CallAsync(Request(HttpMethod.Get, $"{_wabaId}?fields={Uri.EscapeDataString(field)}"), ct);
        var number = await NumberDigitsAsync(ct);
        var points = new List<WaPricePoint>();
        foreach (var block in answer["pricing_analytics"]?["data"]?.AsArray() ?? new JsonArray())
        foreach (var p in block?["data_points"]?.AsArray() ?? new JsonArray())
        {
            if (p == null) continue;
            // Only this number's messages: the account also holds the Doctor Dekho bot's number.
            var phone = new string((p["phone_number"].Str() ?? string.Empty).Where(char.IsDigit).ToArray());
            if (number != null && phone.Length > 0 && phone != number) continue;
            points.Add(new WaPricePoint
            {
                Start = DateTimeOffset.FromUnixTimeSeconds(p["start"].Long() ?? 0).UtcDateTime,
                Country = p["country"].Str(),
                PricingType = p["pricing_type"].Str(),
                PricingCategory = p["pricing_category"].Str(),
                Volume = p["volume"].Long() ?? 0,
                Cost = decimal.TryParse(p["cost"].Str(), NumberStyles.Float, CultureInfo.InvariantCulture, out var cost) ? cost : 0m,
            });
        }
        return points;
    }

    private string? _numberDigits;

    private async Task<string?> NumberDigitsAsync(CancellationToken ct)
    {
        if (_numberDigits != null) return _numberDigits;
        try
        {
            var answer = await CallAsync(Request(HttpMethod.Get, $"{_phoneId}?fields=display_phone_number"), ct);
            _numberDigits = new string((answer["display_phone_number"].Str() ?? string.Empty).Where(char.IsDigit).ToArray());
            return _numberDigits.Length > 0 ? _numberDigits : null;
        }
        catch (WaMetaException) { return null; }
    }
}

/// <summary>Reading Meta's JSON whether a value comes as text or as a number.</summary>
internal static class WaJson
{
    public static string? Str(this JsonNode? node) => node is JsonValue v
        ? v.TryGetValue<string>(out var s) ? s
          : v.TryGetValue<long>(out var l) ? l.ToString(CultureInfo.InvariantCulture)
          : v.TryGetValue<double>(out var d) ? d.ToString(CultureInfo.InvariantCulture)
          : v.TryGetValue<bool>(out var b) ? (b ? "true" : "false") : null
        : null;

    public static long? Long(this JsonNode? node) =>
        long.TryParse(node.Str(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null;

    public static int? Int(this JsonNode? node) =>
        int.TryParse(node.Str(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    public static bool? Bool(this JsonNode? node) => node.Str() switch { "true" => true, "false" => false, _ => null };
}
