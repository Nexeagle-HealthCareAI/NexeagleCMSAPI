using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Data;
using CMSAPI.Data.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Controllers;

// Meta's webhook for CMS → Social (Page comments, Messenger, lead forms; Instagram comments and messages),
// set up by Social → Setup → "Connect webhooks". No sign-in: every POST must carry Meta's X-Hub-Signature-256
// made with the app secret of a connected account, or it is refused. Also serves short-lived signed links to
// a post's pictures and videos, which Instagram fetches when publishing.
[AllowAnonymous]
[ApiController]
[ApiVersion("1.0")]
public class MetaWebhookController : ControllerBase
{
    private readonly ISocialWebhookService _webhook;
    private readonly CmsDbContext _db;
    private readonly SocialKeys _keys;
    private readonly ILogger<MetaWebhookController> _logger;

    public MetaWebhookController(ISocialWebhookService webhook, CmsDbContext db, SocialKeys keys, ILogger<MetaWebhookController> logger)
    {
        _webhook = webhook;
        _db = db;
        _keys = keys;
        _logger = logger;
    }

    private async Task<List<Domain.Entities.SocialAccount>> AccountsAsync() =>
        await _db.SocialAccounts.AsNoTracking().Where(a => a.IsActive).ToListAsync();

    [HttpGet("api/v{version:apiVersion}/meta/webhook")]
    public async Task<IActionResult> Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (mode != "subscribe" || string.IsNullOrEmpty(token)) return StatusCode(403);
        foreach (var a in await AccountsAsync())
        {
            var expected = _keys.VerifyToken(a);
            if (expected != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected)))
                return Content(challenge ?? string.Empty, "text/plain");
        }
        return StatusCode(403);
    }

    [HttpPost("api/v{version:apiVersion}/meta/webhook")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> Receive()
    {
        using var memory = new MemoryStream();
        await Request.Body.CopyToAsync(memory);
        var raw = memory.ToArray();
        var header = Request.Headers["X-Hub-Signature-256"].ToString();

        var secrets = (await AccountsAsync()).Select(a => _keys.Creds(a)?.AppSecret).Where(s => s != null).Distinct().ToList();
        if (secrets.Count == 0)
        {
            _logger.LogError("Meta webhook refused: no connected Social account has an app secret.");
            return StatusCode(503);
        }
        if (!secrets.Any(s => WhatsAppWebhookController.Signed(raw, s!, header)))
        {
            _logger.LogWarning("Meta webhook: wrong signature, refused.");
            return Unauthorized();
        }

        JsonElement payload;
        try { payload = JsonSerializer.Deserialize<JsonElement>(raw); }
        catch (JsonException) { return BadRequest(); }
        try { await _webhook.HandleAsync(payload, HttpContext.RequestAborted); }
        catch (Exception ex)
        {
            // A 500 makes Meta send it again; comments, messages and leads are stored once.
            _logger.LogError(ex, "Meta webhook could not be stored.");
            return StatusCode(500);
        }
        return Ok();
    }

    [HttpGet("api/v{version:apiVersion}/social/public-media")]
    public async Task<IActionResult> PublicMedia([FromQuery] string key, [FromQuery] long exp, [FromQuery] string sig,
        [FromServices] SocialMediaLinks links, [FromServices] ISocialComposeService compose)
    {
        if (string.IsNullOrEmpty(key) || !links.Valid(key, exp, sig)) return NotFound();
        var file = await compose.MediaAsync(key);
        return file is { } f ? File(f.Content, f.Mime, enableRangeProcessing: true) : NotFound();
    }
}
