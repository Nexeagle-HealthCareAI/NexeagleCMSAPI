using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers;

// Vita's emergency takeover in the CMS (vision Phase 3): hospitals' emergency details to verify (a Doctor Dekho admin calls the number
// and checks the pin), places that are not on 1HMS, and the emergency alert log. Passed on to 1HMSAPI after the CMS login and the
// emergency.manage permission are checked; the CMS user's name goes along as the verifier.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/emergency")]
[HasPermission("emergency.manage")]
public class EmergencyTakeoverController : HealthWikiControllerBase
{
    private readonly IEmergencyTakeoverService _service;

    public EmergencyTakeoverController(IEmergencyTakeoverService service)
    {
        _service = service;
    }

    // { items, pending, verified, rejected }; status = PENDING | VERIFIED | REJECTED, or empty for all.
    [HttpGet("places")]
    public async Task<IActionResult> Places([FromQuery] string? status) =>
        Reply(await _service.GetAsync("internal/emergency/places", string.IsNullOrWhiteSpace(status) ? null : $"status={Uri.EscapeDataString(status)}", Actor));

    // { decision: "VERIFIED" | "REJECTED", note } (a note is required to reject, so the hospital knows what to fix).
    [HttpPost("places/{id:guid}/verify")]
    public async Task<IActionResult> Verify(Guid id, [FromBody] JsonElement body) =>
        Reply(await _service.SendAsync(HttpMethod.Post, $"internal/emergency/places/{id}/verify", body, Actor));

    // A place not on 1HMS: { name, kind, emergencyPhone, alternatePhone, alertWhatsApp, latitude, longitude, landmark, address, city,
    // is24x7, openTime, closeTime, capabilities, isEnabled }. Verified by the admin who adds it.
    [HttpPost("places")]
    public async Task<IActionResult> Add([FromBody] JsonElement body) =>
        Reply(await _service.SendAsync(HttpMethod.Post, "internal/emergency/places", body, Actor));

    [HttpPut("places/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] JsonElement body) =>
        Reply(await _service.SendAsync(HttpMethod.Put, $"internal/emergency/places/{id}", body, Actor));

    // { events: [{ eventId, at, reason, channel, sharedMobile (masked), places: [{ name, distanceKm, whatsAppSent, inApp, callTaps, acknowledged }] }] }
    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, [FromQuery] int? take)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (fromUtc != null) parts.Add("fromUtc=" + Uri.EscapeDataString(fromUtc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)));
        if (toUtc != null) parts.Add("toUtc=" + Uri.EscapeDataString(toUtc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)));
        if (take != null) parts.Add("take=" + Math.Clamp(take.Value, 1, 500));
        return Reply(await _service.GetAsync("internal/emergency/alerts", parts.Count == 0 ? null : string.Join("&", parts), Actor));
    }
}
