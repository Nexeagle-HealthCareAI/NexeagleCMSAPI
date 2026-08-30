using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using CMSAPI.Domain.Entities;
using CMSAPI.Data;
using CMSAPI.Application.Services;

namespace CMSAPI.Controllers;

[ApiController]
[Route("api/v1/crm/webhooks")]
public class CrmWebhookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IGroqSalesAiService _aiService;
    private readonly IConfiguration _config;

    public CrmWebhookController(AppDbContext db, IGroqSalesAiService aiService, IConfiguration config)
    {
        _db = db;
        _aiService = aiService;
        _config = config;
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

            // Ingest Lead Mock/Fetched Form Data
            var lead = new CrmLead
            {
                LeadNumber = $"LEAD-{DateTime.UtcNow:MMdd}-{Random.Shared.Next(1000, 9999)}",
                ContactName = "Dr. Inbound Lead",
                FacilityName = "Healthcare Facility",
                FacilityType = "HOSPITAL",
                BedCount = 30,
                City = "Kishanganj",
                PhoneNumber = "+919876543210",
                SourceChannel = "META_ADS",
                UtmAdId = adId,
                Status = "NEW"
            };

            // AI Scoring & Enrichment via Groq 70B
            var aiAnalysis = await _aiService.ScoreAndEnrichLeadAsync(lead.FacilityName, lead.FacilityType, lead.BedCount, lead.City, "Interested in hospital software trial", ct);
            lead.AiIntentScore = aiAnalysis.IntentScore;
            lead.AiPersonaSummary = $"{aiAnalysis.BuyerPersona} | Hook: {aiAnalysis.RecommendedHook}";

            _db.CrmLeads.Add(lead);
            await _db.SaveChangesAsync(ct);

            return Ok(new { success = true, leadId = lead.Id });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
