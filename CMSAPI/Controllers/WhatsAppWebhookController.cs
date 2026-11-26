using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// Meta's webhook for the WhatsApp marketing number. Meta reaches it through the number's webhook override:
//   POST graph.facebook.com/<version>/<WhatsAppMarketing__PhoneNumberId>
//   { "webhook_configuration": { "override_callback_uri": "https://cms-api.nexeagle.com/api/v1/whatsapp/webhook",
//                                "verify_token": "<WhatsAppMarketing__VerifyToken>" } }
// so the Doctor Dekho bot's number keeps going to the bot. No sign-in: every POST must carry Meta's
// X-Hub-Signature-256 made with WhatsAppMarketing__AppSecret (the Meta app's secret), or it is refused.
[AllowAnonymous]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/whatsapp/webhook")]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly IWaWebhookService _webhook;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(IWaWebhookService webhook, IConfiguration config, IWebHostEnvironment env, ILogger<WhatsAppWebhookController> logger)
    {
        _webhook = webhook;
        _config = config;
        _env = env;
        _logger = logger;
    }

    // Meta checks the address once, when the override is set.
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var expected = _config["WhatsAppMarketing:VerifyToken"];
        if (string.IsNullOrEmpty(expected)) return StatusCode(503, "WhatsAppMarketing__VerifyToken is not set.");
        if (mode == "subscribe" && token != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected)))
            return Content(challenge ?? string.Empty, "text/plain");
        return StatusCode(403);
    }

    [HttpPost]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Receive()
    {
        using var memory = new MemoryStream();
        await Request.Body.CopyToAsync(memory);
        var raw = memory.ToArray();

        var secret = _config["WhatsAppMarketing:AppSecret"];
        if (string.IsNullOrEmpty(secret))
        {
            if (!_env.IsDevelopment())
            {
                _logger.LogError("WhatsApp marketing webhook refused: WhatsAppMarketing__AppSecret is not set.");
                return StatusCode(503, new { error = "Webhook signature check is not set up." });
            }
        }
        else if (!Signed(raw, secret, Request.Headers["X-Hub-Signature-256"].ToString()))
        {
            _logger.LogWarning("WhatsApp marketing webhook: wrong signature, refused.");
            return Unauthorized();
        }

        JsonElement payload;
        try { payload = JsonSerializer.Deserialize<JsonElement>(raw); }
        catch (JsonException) { return BadRequest(); }

        try
        {
            await _webhook.HandleAsync(payload);
        }
        catch (Exception ex)
        {
            // A 500 makes Meta send it again later; each message is stored once, so that is safe.
            _logger.LogError(ex, "WhatsApp marketing webhook could not be stored.");
            return StatusCode(500);
        }
        return Ok();
    }

    /// <summary>X-Hub-Signature-256: "sha256=" + HMAC-SHA256 of the exact body with the app secret.</summary>
    public static bool Signed(byte[] body, string secret, string header)
    {
        const string prefix = "sha256=";
        if (string.IsNullOrEmpty(header) || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(header[prefix.Length..].Trim().ToLowerInvariant()), Encoding.ASCII.GetBytes(expected));
    }
}
