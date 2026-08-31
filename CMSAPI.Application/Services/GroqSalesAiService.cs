using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace CMSAPI.Application.Services;

public interface IGroqSalesAiService
{
    Task<LeadEnrichmentResult> ScoreAndEnrichLeadAsync(string facilityName, string facilityType, int beds, string city, string rawInquiry, CancellationToken ct = default);
    Task<string> GenerateWhatsAppPitchAsync(string doctorName, string facilityName, string facilityType, int beds, string city, CancellationToken ct = default);
    Task<string> ResolveObjectionAsync(string objection, string facilityType, int beds, CancellationToken ct = default);
    Task<SocialCampaignPack> GenerateSocialCampaignAsync(string theme, string targetAudience, CancellationToken ct = default);
}

public class GroqSalesAiService : IGroqSalesAiService
{
    private readonly HttpClient _httpClient;
    private const string ModelName = "llama-3.3-70b-versatile";

    private string? _apiKey;

    public GroqSalesAiService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _apiKey = config["Groq:ApiKey"];
        _httpClient.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }
    }

    public async Task<LeadEnrichmentResult> ScoreAndEnrichLeadAsync(string facilityName, string facilityType, int beds, string city, string rawInquiry, CancellationToken ct = default)
    {
        var prompt = $$"""
        You are a B2B SaaS Healthcare Sales Intelligence Engine for 1HMS hospital software.
        Analyze this incoming lead and return a JSON response with:
        1. "intent_score": integer from 0 to 100 based on conversion likelihood.
        2. "buyer_persona": concise analysis of the buyer's pain points.
        3. "recommended_hook": the #1 feature of 1HMS to pitch (e.g. Offline LAN Mode, 1Lab Autofill, Zero-Click Voice Rx, Auto IPD Billing).

        Lead Information:
        - Facility: {{facilityName}} ({{facilityType}}, {{beds}} beds)
        - Location: {{city}}, India
        - Inquiry: "{{rawInquiry}}"
        """;

        var jsonResponse = await ExecuteGroqAsync(prompt, jsonMode: true, ct);
        return JsonSerializer.Deserialize<LeadEnrichmentResult>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) 
               ?? new LeadEnrichmentResult { IntentScore = 60, BuyerPersona = "Standard Clinic Lead", RecommendedHook = "Offline Mode" };
    }

    public async Task<string> GenerateWhatsAppPitchAsync(string doctorName, string facilityName, string facilityType, int beds, string city, CancellationToken ct = default)
    {
        var prompt = $$"""
        You are an elite B2B Healthtech Sales Director selling 1HMS to hospital directors in Tier 2/3/4 India.
        Write a concise, high-converting WhatsApp message in respectful conversational Hinglish.
        Doctor: {{doctorName}}
        Hospital: {{facilityName}} ({{facilityType}}, {{beds}} beds, {{city}})
        Key Value Props: 100% Offline LAN resilience (works with zero internet), 0-click voice prescriptions, auto-billing to stop IPD leakage.
        Constraint: Maximum 55 words. Include clear CTA to book a 10-minute demo.
        """;

        return await ExecuteGroqAsync(prompt, jsonMode: false, ct);
    }

    public async Task<string> ResolveObjectionAsync(string objection, string facilityType, int beds, CancellationToken ct = default)
    {
        var prompt = $$"""
        A hospital owner ({{facilityType}}, {{beds}} beds) raised this sales objection against installing 1HMS:
        "{{objection}}"
        Give the sales rep a 2-sentence conversational, empathetic, and compelling counter-script that shifts perspective and secures a demo.
        """;

        return await ExecuteGroqAsync(prompt, jsonMode: false, ct);
    }

    public async Task<SocialCampaignPack> GenerateSocialCampaignAsync(string theme, string targetAudience, CancellationToken ct = default)
    {
        var prompt = $$"""
        Generate a multi-platform B2B healthcare marketing campaign pack for 1HMS portal.
        Theme: {{theme}}
        Target Audience: {{targetAudience}}

        Output a strict JSON object with:
        - "instagram_carousel": 3 slide headlines and caption with hashtags.
        - "facebook_ad_copy": High-converting primary text and headline.
        - "youtube_shorts_script": 45-second video script with visual cues and voiceover.
        - "twitter_thread": 3-tweet insightful thread on healthcare tech.
        """;

        var json = await ExecuteGroqAsync(prompt, jsonMode: true, ct);
        return JsonSerializer.Deserialize<SocialCampaignPack>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new SocialCampaignPack();
    }

    private async Task<string> ExecuteGroqAsync(string prompt, bool jsonMode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return jsonMode ? "{}" : "";
        }

        var requestBody = new
        {
            model = ModelName,
            messages = new[] { new { role = "user", content = prompt } },
            response_format = jsonMode ? new { type = "json_object" } : null,
            temperature = 0.2,
            max_tokens = 800
        };

        try
        {
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("chat/completions", content, ct);
            response.EnsureSuccessStatusCode();

            var resContent = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(resContent);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch (Exception)
        {
            // Graceful degradation when AI API is down or times out.
            // Returning an empty JSON object for jsonMode ensures deserialization doesn't crash,
            // and an empty string for plain text means the caller can fallback appropriately.
            return jsonMode ? "{}" : "";
        }
    }
}

public class LeadEnrichmentResult
{
    [JsonPropertyName("intent_score")] public int IntentScore { get; set; }
    [JsonPropertyName("buyer_persona")] public string BuyerPersona { get; set; } = string.Empty;
    [JsonPropertyName("recommended_hook")] public string RecommendedHook { get; set; } = string.Empty;
}

public class SocialCampaignPack
{
    [JsonPropertyName("instagram_carousel")] public string InstagramCarousel { get; set; } = string.Empty;
    [JsonPropertyName("facebook_ad_copy")] public string FacebookAdCopy { get; set; } = string.Empty;
    [JsonPropertyName("youtube_shorts_script")] public string YoutubeShortsScript { get; set; } = string.Empty;
    [JsonPropertyName("twitter_thread")] public string TwitterThread { get; set; } = string.Empty;
}
