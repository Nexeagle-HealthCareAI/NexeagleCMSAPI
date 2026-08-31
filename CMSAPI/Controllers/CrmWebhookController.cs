using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Services;
using CMSAPI.Data;
using CMSAPI.Domain.Entities;
using CMSAPI.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CMSAPI.Controllers;

[ApiController]
[ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/crm/webhooks")]
public class CrmWebhookController : ControllerBase
{
    private readonly ISalesLeadService _salesLeadService;
    private readonly IGroqSalesAiService _aiService;
    private readonly IConfiguration _config;
    private readonly IHubContext<CrmHub> _hubContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CrmWebhookController> _logger;

    public CrmWebhookController(
        ISalesLeadService salesLeadService,
        IGroqSalesAiService aiService,
        IConfiguration config,
        IHubContext<CrmHub> hubContext,
        IHttpClientFactory httpClientFactory,
        ILogger<CrmWebhookController> logger)
    {
        _salesLeadService = salesLeadService;
        _aiService = aiService;
        _config = config;
        _hubContext = hubContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ── Meta Webhook ──────────────────────────────────────────────────────────

    [HttpGet("meta")]
    public IActionResult VerifyMetaWebhook(
        [FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.verify_token")] string token,
        [FromQuery(Name = "hub.challenge")] string challenge)
    {
        var expectedToken = _config["Meta:VerifyToken"] ?? "1HMS_CRM_SECRET_2026";
        if (mode == "subscribe" && token == expectedToken)
            return Ok(challenge);
        return Forbid();
    }

    [HttpPost("meta")]
    public async Task<IActionResult> IngestMetaLead(CancellationToken ct)
    {
        // ── 1. Read raw body before deserialization (required for HMAC) ───────
        Request.EnableBuffering();
        var rawBody = await new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true).ReadToEndAsync(ct);
        Request.Body.Position = 0;

        // ── 2. Validate HMAC-SHA256 signature ─────────────────────────────────
        var appSecret = _config["Meta:AppSecret"];
        if (!string.IsNullOrEmpty(appSecret))
        {
            var sigHeader = Request.Headers["X-Hub-Signature-256"].ToString();
            if (!IsValidHmac(rawBody, appSecret, sigHeader))
            {
                _logger.LogWarning("Meta webhook: invalid HMAC signature. Request rejected.");
                return Unauthorized(new { error = "Invalid webhook signature." });
            }
        }
        else
        {
            _logger.LogWarning("Meta:AppSecret is not configured — HMAC validation skipped. Set it in production!");
        }

        // ── 3. Parse payload ──────────────────────────────────────────────────
        JsonElement payload;
        try { payload = JsonSerializer.Deserialize<JsonElement>(rawBody); }
        catch { return BadRequest(new { error = "Invalid JSON payload." }); }

        try
        {
            if (!payload.TryGetProperty("entry", out var entry) || entry.GetArrayLength() == 0)
                return Ok();

            var changes = entry[0].GetProperty("changes");
            if (changes.GetArrayLength() == 0) return Ok();

            var value = changes[0].GetProperty("value");
            var leadgenId = value.GetProperty("leadgen_id").GetString();
            var adId = value.TryGetProperty("ad_id", out var a) ? a.GetString() : "META_AD";

            if (string.IsNullOrEmpty(leadgenId)) return Ok();

            // ── 4. Fetch lead data from Meta Graph API ────────────────────────
            var systemUserToken = _config["Meta:SystemUserToken"];
            if (string.IsNullOrEmpty(systemUserToken))
            {
                // Never insert fake data into production. Log and return 200 so Meta stops retrying.
                _logger.LogWarning("Meta:SystemUserToken not configured. Lead {LeadgenId} skipped.", leadgenId);
                return Ok(new { skipped = true, reason = "SystemUserToken not configured." });
            }

            var client = _httpClientFactory.CreateClient();
            var metaUrl = $"https://graph.facebook.com/v17.0/{leadgenId}?access_token={systemUserToken}";
            var metaResponse = await client.GetAsync(metaUrl, ct);

            if (!metaResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Meta Graph API returned {Status} for leadgen {LeadgenId}. Lead skipped.",
                    metaResponse.StatusCode, leadgenId);
                return Ok(new { skipped = true, reason = $"Meta API error: {metaResponse.StatusCode}" });
            }

            var metaJson = await metaResponse.Content.ReadAsStringAsync(ct);
            using var metaDoc = JsonDocument.Parse(metaJson);
            var root = metaDoc.RootElement;

            // ── 5. Extract field_data ─────────────────────────────────────────
            string contactName = "Doctor";
            string mobile = string.Empty;
            string city = "Unknown";
            string hospitalName = "Healthcare Facility";
            string facilityType = "HOSPITAL";
            int bedCount = 0;

            if (root.TryGetProperty("field_data", out var fieldData) && fieldData.ValueKind == JsonValueKind.Array)
            {
                foreach (var field in fieldData.EnumerateArray())
                {
                    var name = field.GetProperty("name").GetString();
                    var values = field.GetProperty("values");
                    if (values.GetArrayLength() == 0) continue;
                    var val = values[0].GetString() ?? "";

                    switch (name?.ToLower())
                    {
                        case "full_name": case "contact_name": case "name":
                            contactName = val; break;
                        case "phone_number": case "mobile":
                            mobile = val; break;
                        case "city":
                            city = val; break;
                        case "company_name": case "hospital_name": case "clinic_name":
                            hospitalName = val; break;
                        case "job_title": case "facility_type":
                            facilityType = val; break;
                        case "bed_count": case "beds":
                            if (int.TryParse(val, out var parsedBeds)) bedCount = parsedBeds;
                            break;
                    }
                }
            }

            // ── 6. AI Scoring & Enrichment ────────────────────────────────────
            var aiAnalysis = await _aiService.ScoreAndEnrichLeadAsync(
                hospitalName, facilityType, bedCount, city, "Inbound Meta Lead Form", ct);

            // ── 7. Create Lead ────────────────────────────────────────────────
            var leadRequest = new CMSAPI.Application.Models.CreateSalesLeadRequest
            {
                LeadNumber       = GenerateLeadNumber(),
                ContactName      = contactName,
                HospitalName     = hospitalName,
                FacilityType     = facilityType,
                BedCount         = bedCount,
                City             = city,
                Mobile           = mobile,
                Source           = "META_ADS",
                UtmAdId          = adId,
                Stage            = "New",
                AiIntentScore    = aiAnalysis.IntentScore,
                AiPersonaSummary = $"{aiAnalysis.BuyerPersona} | Hook: {aiAnalysis.RecommendedHook}"
            };

            var createdLead = await _salesLeadService.CreateLeadAsync(leadRequest, Guid.Empty, "Meta Webhook");

            // ── 8. SignalR hot-lead alert ─────────────────────────────────────
            if (createdLead.AiIntentScore >= 80)
                await _hubContext.Clients.All.SendAsync("crm-hot-lead-received", createdLead, cancellationToken: ct);

            return Ok(new { success = true, leadId = createdLead.LeadId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Meta webhook lead.");
            return StatusCode(500, new { error = "Internal error processing webhook." });
        }
    }

    // ── WhatsApp Webhook ──────────────────────────────────────────────────────

    [HttpGet("whatsapp")]
    public IActionResult VerifyWhatsAppWebhook(
        [FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.verify_token")] string token,
        [FromQuery(Name = "hub.challenge")] string challenge)
    {
        var expectedToken = _config["WhatsApp:VerifyToken"] ?? "1HMS_CRM_SECRET_2026";
        if (mode == "subscribe" && token == expectedToken)
            return Ok(challenge);
        return Forbid();
    }

    [HttpPost("whatsapp")]
    public async Task<IActionResult> IngestWhatsAppMessage(CancellationToken ct)
    {
        // ── 1. Read raw body ──────────────────────────────────────────────────
        Request.EnableBuffering();
        var rawBody = await new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true).ReadToEndAsync(ct);
        Request.Body.Position = 0;

        // ── 2. Validate HMAC-SHA256 signature ─────────────────────────────────
        var appSecret = _config["WhatsApp:AppSecret"];
        if (!string.IsNullOrEmpty(appSecret))
        {
            var sigHeader = Request.Headers["X-Hub-Signature-256"].ToString();
            if (!IsValidHmac(rawBody, appSecret, sigHeader))
            {
                _logger.LogWarning("WhatsApp webhook: invalid HMAC signature. Request rejected.");
                return Unauthorized(new { error = "Invalid webhook signature." });
            }
        }
        else
        {
            _logger.LogWarning("WhatsApp:AppSecret is not configured — HMAC validation skipped. Set it in production!");
        }

        // ── 3. Parse payload ──────────────────────────────────────────────────
        JsonElement payload;
        try { payload = JsonSerializer.Deserialize<JsonElement>(rawBody); }
        catch { return BadRequest(new { error = "Invalid JSON payload." }); }

        try
        {
            if (!payload.TryGetProperty("entry", out var entry) || entry.GetArrayLength() == 0)
                return Ok();

            var changes = entry[0].GetProperty("changes");
            if (changes.GetArrayLength() == 0) return Ok();

            var value = changes[0].GetProperty("value");

            if (value.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0)
            {
                var message = messages[0];
                var fromNumber = message.GetProperty("from").GetString();

                string messageText = "Inbound message received";
                if (message.TryGetProperty("text", out var textObj) && textObj.TryGetProperty("body", out var bodyObj))
                    messageText = bodyObj.GetString() ?? messageText;

                if (!string.IsNullOrEmpty(fromNumber))
                {
                    var formattedNumber = fromNumber.StartsWith("+") ? fromNumber : $"+{fromNumber}";

                    var lead = await _salesLeadService.GetLeadByMobileAsync(formattedNumber)
                            ?? await _salesLeadService.GetLeadByMobileAsync(fromNumber);

                    if (lead != null)
                    {
                        var followUpReq = new CMSAPI.Application.Models.AddFollowUpRequest
                        {
                            ActivityType = "WhatsApp",
                            Direction    = "INBOUND",
                            Notes        = messageText
                        };

                        await _salesLeadService.AddFollowUpAsync(lead.LeadId, followUpReq, Guid.Empty, "WhatsApp User");
                        await _hubContext.Clients.All.SendAsync(
                            "crm-inbound-whatsapp",
                            new { leadId = lead.LeadId, message = messageText },
                            cancellationToken: ct);
                    }
                }
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WhatsApp webhook message.");
            return StatusCode(500, new { error = "Internal error processing webhook." });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates the HMAC-SHA256 signature Meta and WhatsApp include on every POST.
    /// Header format: X-Hub-Signature-256: sha256=&lt;hex_digest&gt;
    /// </summary>
    private static bool IsValidHmac(string payload, string secret, string sigHeader)
    {
        const string prefix = "sha256=";
        if (string.IsNullOrEmpty(sigHeader) || !sigHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var receivedHash = sigHeader[prefix.Length..];
        var computedHash = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))
        ).ToLowerInvariant();

        // Constant-time compare to prevent timing-based attacks
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(receivedHash.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(computedHash));
    }

    /// <summary>Generates a collision-resistant lead number using a GUID segment.</summary>
    private static string GenerateLeadNumber()
        => $"LEAD-{DateTime.UtcNow:MMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
}

