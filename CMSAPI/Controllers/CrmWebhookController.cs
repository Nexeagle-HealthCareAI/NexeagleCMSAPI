using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using CMSAPI.Domain.Entities;
using CMSAPI.Data;
using CMSAPI.Application.Services;
using CMSAPI.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;
using CMSAPI.Hubs;

namespace CMSAPI.Controllers;

[ApiController]
[Route("api/v1/crm/webhooks")]
public class CrmWebhookController : ControllerBase
{
    private readonly ISalesLeadService _salesLeadService;
    private readonly IGroqSalesAiService _aiService;
    private readonly IConfiguration _config;
    private readonly IHubContext<CrmHub> _hubContext;
    private readonly IHttpClientFactory _httpClientFactory;

    public CrmWebhookController(ISalesLeadService salesLeadService, IGroqSalesAiService aiService, IConfiguration config, IHubContext<CrmHub> hubContext, IHttpClientFactory httpClientFactory)
    {
        _salesLeadService = salesLeadService;
        _aiService = aiService;
        _config = config;
        _hubContext = hubContext;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("meta")]
    public IActionResult VerifyMetaWebhook([FromQuery(Name = "hub.mode")] string mode,
                                           [FromQuery(Name = "hub.verify_token")] string token,
                                           [FromQuery(Name = "hub.challenge")] string challenge)
    {
        var expectedToken = _config["Meta:VerifyToken"] ?? "1HMS_CRM_SECRET_2026";
        if (mode == "subscribe" && token == expectedToken)
        {
            return Ok(challenge);
        }
        return Forbid();
    }

    [HttpPost("meta")]
    public async Task<IActionResult> IngestMetaLead([FromBody] JsonElement payload, CancellationToken ct)
    {
        try
        {
            // Parse Meta Webhook Leadgen Payload
            if (!payload.TryGetProperty("entry", out var entry) || entry.GetArrayLength() == 0)
                return Ok();

            var changes = entry[0].GetProperty("changes");
            if (changes.GetArrayLength() == 0) return Ok();

            var value = changes[0].GetProperty("value");
            var leadgenId = value.GetProperty("leadgen_id").GetString();
            var adId = value.TryGetProperty("ad_id", out var a) ? a.GetString() : "META_AD";

            if (string.IsNullOrEmpty(leadgenId)) return Ok();

            // Fetch real data from Meta Graph API
            var systemUserToken = _config["Meta:SystemUserToken"];
            if (string.IsNullOrEmpty(systemUserToken))
            {
                // Fallback to dummy data if token is not configured (e.g. local dev without token)
                return await CreateFallbackLead(leadgenId, adId, ct);
            }

            var client = _httpClientFactory.CreateClient();
            var metaUrl = $"https://graph.facebook.com/v17.0/{leadgenId}?access_token={systemUserToken}";
            var metaResponse = await client.GetAsync(metaUrl, ct);

            if (!metaResponse.IsSuccessStatusCode)
            {
                // Log error or fallback
                return await CreateFallbackLead(leadgenId, adId, ct);
            }

            var metaJson = await metaResponse.Content.ReadAsStringAsync(ct);
            using var metaDoc = JsonDocument.Parse(metaJson);
            var root = metaDoc.RootElement;

            // Extract fields
            string contactName = "Doctor";
            string mobile = "+910000000000";
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
                        case "full_name":
                        case "contact_name":
                        case "name":
                            contactName = val;
                            break;
                        case "phone_number":
                        case "mobile":
                            mobile = val;
                            break;
                        case "city":
                            city = val;
                            break;
                        case "company_name":
                        case "hospital_name":
                        case "clinic_name":
                            hospitalName = val;
                            break;
                        case "job_title":
                        case "facility_type":
                            facilityType = val;
                            break;
                        case "bed_count":
                        case "beds":
                            if (int.TryParse(val, out var parsedBeds)) bedCount = parsedBeds;
                            break;
                    }
                }
            }

            // AI Scoring & Enrichment via Groq 70B
            var aiAnalysis = await _aiService.ScoreAndEnrichLeadAsync(hospitalName, facilityType, bedCount, city, "Inbound Meta Lead Form", ct);

            // Create Lead Request
            var leadRequest = new CMSAPI.Application.Models.CreateSalesLeadRequest
            {
                LeadNumber = $"LEAD-{DateTime.UtcNow:MMdd}-{Random.Shared.Next(1000, 9999)}",
                ContactName = contactName,
                HospitalName = hospitalName,
                FacilityType = facilityType,
                BedCount = bedCount,
                City = city,
                Mobile = mobile,
                Source = "META_ADS",
                UtmAdId = adId,
                Stage = "New",
                AiIntentScore = aiAnalysis.IntentScore,
                AiPersonaSummary = $"{aiAnalysis.BuyerPersona} | Hook: {aiAnalysis.RecommendedHook}"
            };

            // Assuming a system/bot user ID for the creator (or empty Guid if allowed)
            var createdLead = await _salesLeadService.CreateLeadAsync(leadRequest, Guid.Empty, "Meta Webhook");

            // SignalR Live Alert
            if (createdLead.AiIntentScore >= 80)
            {
                await _hubContext.Clients.All.SendAsync("crm-hot-lead-received", createdLead, cancellationToken: ct);
            }

            return Ok(new { success = true, leadId = createdLead.LeadId });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private async Task<IActionResult> CreateFallbackLead(string leadgenId, string adId, CancellationToken ct)
    {
        // AI Scoring & Enrichment via Groq 70B
        var aiAnalysis = await _aiService.ScoreAndEnrichLeadAsync("Healthcare Facility", "HOSPITAL", 30, "Kishanganj", "Interested in hospital software trial", ct);

        var leadRequest = new CMSAPI.Application.Models.CreateSalesLeadRequest
        {
            LeadNumber = $"LEAD-{DateTime.UtcNow:MMdd}-{Random.Shared.Next(1000, 9999)}",
            ContactName = $"Dummy Lead {leadgenId.Substring(0, Math.Min(4, leadgenId.Length))}",
            HospitalName = "Healthcare Facility",
            FacilityType = "HOSPITAL",
            BedCount = 30,
            City = "Kishanganj",
            Mobile = "+919876543210",
            Source = "META_ADS",
            UtmAdId = adId,
            Stage = "New",
            AiIntentScore = aiAnalysis.IntentScore,
            AiPersonaSummary = $"{aiAnalysis.BuyerPersona} | Hook: {aiAnalysis.RecommendedHook}"
        };

        var createdLead = await _salesLeadService.CreateLeadAsync(leadRequest, Guid.Empty, "Meta Webhook");

        if (createdLead.AiIntentScore >= 80)
        {
            await _hubContext.Clients.All.SendAsync("crm-hot-lead-received", createdLead, cancellationToken: ct);
        }

        return Ok(new { success = true, leadId = createdLead.LeadId, fallback = true });
    }

    [HttpGet("whatsapp")]
    public IActionResult VerifyWhatsAppWebhook([FromQuery(Name = "hub.mode")] string mode,
                                           [FromQuery(Name = "hub.verify_token")] string token,
                                           [FromQuery(Name = "hub.challenge")] string challenge)
    {
        var expectedToken = _config["WhatsApp:VerifyToken"] ?? "1HMS_CRM_SECRET_2026";
        if (mode == "subscribe" && token == expectedToken)
        {
            return Ok(challenge);
        }
        return Forbid();
    }

    [HttpPost("whatsapp")]
    public async Task<IActionResult> IngestWhatsAppMessage([FromBody] JsonElement payload, CancellationToken ct)
    {
        try
        {
            if (!payload.TryGetProperty("entry", out var entry) || entry.GetArrayLength() == 0)
                return Ok();

            var changes = entry[0].GetProperty("changes");
            if (changes.GetArrayLength() == 0) return Ok();

            var value = changes[0].GetProperty("value");
            
            // Check if there are messages
            if (value.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0)
            {
                var message = messages[0];
                var fromNumber = message.GetProperty("from").GetString();
                
                string messageText = "Inbound message received";
                if (message.TryGetProperty("text", out var textObj) && textObj.TryGetProperty("body", out var bodyObj))
                {
                    messageText = bodyObj.GetString() ?? messageText;
                }

                if (!string.IsNullOrEmpty(fromNumber))
                {
                    // Ensure the from number is formatted correctly if needed (Meta usually sends it without +)
                    var formattedNumber = fromNumber.StartsWith("+") ? fromNumber : $"+{fromNumber}";
                    
                    var lead = await _salesLeadService.GetLeadByMobileAsync(formattedNumber) ?? 
                               await _salesLeadService.GetLeadByMobileAsync(fromNumber); // Try both with and without +

                    if (lead != null)
                    {
                        var followUpReq = new CMSAPI.Application.Models.AddFollowUpRequest
                        {
                            ActivityType = "WhatsApp",
                            Direction = "INBOUND",
                            Notes = messageText
                        };
                        
                        await _salesLeadService.AddFollowUpAsync(lead.LeadId, followUpReq, Guid.Empty, "WhatsApp User");

                        // SignalR Live Alert
                        await _hubContext.Clients.All.SendAsync("crm-inbound-whatsapp", new { leadId = lead.LeadId, message = messageText }, cancellationToken: ct);
                    }
                }
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
