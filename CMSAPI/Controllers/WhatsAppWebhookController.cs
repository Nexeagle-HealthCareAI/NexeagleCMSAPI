using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// Meta's webhook for the WhatsApp marketing number. Meta reaches it through the number's webhook override,
// which WhatsApp → Setup sets ("Connect webhook"), so the Doctor Dekho bot's number keeps going to the bot.
// No sign-in: every POST must carry Meta's X-Hub-Signature-256 made with the Meta app's secret (entered in
// Setup), or it is refused. The verify token is the one Setup made.
[AllowAnonymous]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/whatsapp/webhook")]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly IWaWebhookService _webhook;
    private readonly IWaConnectionStore _connection;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(IWaWebhookService webhook, IWaConnectionStore connection, IWebHostEnvironment env, ILogger<WhatsAppWebhookController> logger)
    {
        _webhook = webhook;
        _connection = connection;
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
        var expected = _connection.Current.VerifyToken;
        if (string.IsNullOrEmpty(expected)) return StatusCode(503, "The verify token is not set: make one in WhatsApp → Setup.");
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

        var secret = _connection.Current.AppSecret;
        if (string.IsNullOrEmpty(secret))
        {
            if (!_env.IsDevelopment())
            {
                _logger.LogError("WhatsApp marketing webhook refused: the Meta app secret is not set (WhatsApp → Setup).");
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
            await _connection.MarkWebhookAsync();
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
