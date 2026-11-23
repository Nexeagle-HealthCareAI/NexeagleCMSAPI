using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;

using Microsoft.AspNetCore.Authorization;

namespace CMSAPI.Controllers;

// Backs the Doctor Dekho page's "Insights" tab (Site Visits / Patient Logins / Appointments) in
// the CMS. One permission covers all three sub-reports — they're read together as one feature.
[Authorize]
[ApiController]
[ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/insights")]
public class InsightsController : ControllerBase
{
    private readonly IInsightsService _service;
    private readonly IWhatsAppInsightsService _whatsApp;

    public InsightsController(IInsightsService service, IWhatsAppInsightsService whatsApp)
    {
        _service = service;
        _whatsApp = whatsApp;
    }

    // insights.contacts: full phone numbers of people who wrote to the WhatsApp bot, and their chats.
    private bool CanSeeContacts =>
        User.Claims.Any(c => c.Type == PermissionConstants.ClaimType && c.Value == "insights.contacts");

    private ObjectResult BotUnavailable(WhatsAppBotUnavailableException ex) =>
        StatusCode(503, new { success = false, message = "The WhatsApp bot's figures cannot be read right now. " + ex.Message });

    // from/to are inclusive "yyyy-MM-dd" India-time dates (omit both for the last 365 days, the
    // tracking retention period); site = doctordekho | nexeagle, omitted for both websites.
    [HasPermission("insights.view")]
    [HttpGet("site-visits")]
    public async Task<IActionResult> GetSiteVisitStats(
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] string? site = null)
    {
        var result = await _service.GetSiteVisitStatsAsync(from, to, site);
        return Ok(result);
    }

    [HasPermission("insights.view")]
    [HttpGet("patient-logins")]
    public async Task<IActionResult> GetPatientLogins(
        [FromQuery] int page = 1, [FromQuery] int limit = 10, [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null)
    {
        var result = await _service.GetPatientLoginsAsync(page, limit, search, sortBy, sortDir);
        return Ok(result);
    }

    // source: "All" (default) | "Guest" | "LoggedIn"
    // dateBy: "booked" (default; from/to are India-time booking days) | "appointment" (appointment date)
    [HasPermission("insights.view")]
    [HttpGet("appointments")]
    public async Task<IActionResult> GetOnlineAppointments(
        [FromQuery] int page = 1, [FromQuery] int limit = 10,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? search = null, [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null,
        [FromQuery] string? source = null, [FromQuery] string? dateBy = null)
    {
        var result = await _service.GetOnlineAppointmentsAsync(page, limit, from, to, search, sortBy, sortDir, source, dateBy);
        return Ok(result);
    }

    [HasPermission("insights.view")]
    [HttpGet("auth-funnel")]
    public async Task<IActionResult> GetAuthFunnelStats([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
    {
        var result = await _service.GetAuthFunnelStatsAsync(from, to);
        return Ok(result);
    }

    [HasPermission("insights.view")]
    [HttpGet("auth-funnel/attempts")]
    public async Task<IActionResult> GetAuthFunnelAttempts(
        [FromQuery] int page = 1, [FromQuery] int limit = 10,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] string? search = null)
    {
        var result = await _service.GetAuthFunnelAttemptsAsync(page, limit, from, to, search);
        return Ok(result);
    }

    [HasPermission("insights.view")]
    [HttpGet("booking-funnel")]
    public async Task<IActionResult> GetBookingFunnelStats([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
    {
        var result = await _service.GetBookingFunnelStatsAsync(from, to);
        return Ok(result);
    }

    [HasPermission("insights.view")]
    [HttpGet("searches")]
    public async Task<IActionResult> GetSearchLog(
        [FromQuery] int page = 1, [FromQuery] int limit = 10,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? search = null, [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null)
    {
        var result = await _service.GetSearchLogAsync(page, limit, from, to, search, sortBy, sortDir);
        return Ok(result);
    }

    // ── WhatsApp bot, growth by week, Doctor Dekho by day ───────────────────────────────────────
    // from/to are inclusive India-time days, "yyyy-MM-dd"; default the last 7 days; at most 92.

    [HasPermission("insights.view")]
    [HttpGet("whatsapp/daily")]
    public async Task<IActionResult> GetWhatsAppDaily([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
    {
        try { return Ok(await _whatsApp.DailyAsync(from, to)); }
        catch (WhatsAppBotUnavailableException ex) { return BotUnavailable(ex); }
    }

    // outcome: all | booked | left | other. source: all | qr | direct. limit up to 1000 (for export).
    // Numbers are masked unless the reader holds insights.contacts.
    [HasPermission("insights.view")]
    [HttpGet("whatsapp/people")]
    public async Task<IActionResult> GetWhatsAppPeople(
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? outcome = null, [FromQuery] string? source = null, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int limit = 25)
    {
        var query = new WhatsAppPeopleQuery { From = from, To = to, Outcome = outcome, Source = source, Search = search, Page = page, Limit = limit };
        try { return Ok(await _whatsApp.PeopleAsync(query, CanSeeContacts)); }
        catch (WhatsAppBotUnavailableException ex) { return BotUnavailable(ex); }
    }

    // The whole conversation: what the person sent (names, ages, symptoms) and every step. Personal
    // data, so it needs insights.contacts.
    [HasPermission("insights.contacts")]
    [HttpGet("whatsapp/people/{sessionId}")]
    public async Task<IActionResult> GetWhatsAppTranscript(string sessionId)
    {
        try
        {
            var transcript = await _whatsApp.TranscriptAsync(sessionId);
            return transcript == null ? NotFound(new { success = false, message = "Conversation not found." }) : Ok(transcript);
        }
        catch (WhatsAppBotUnavailableException ex) { return BotUnavailable(ex); }
    }

    // Week by week (Monday to Sunday, India time): people who wrote to the WhatsApp bot, visits to
    // nexeagle.com and to Doctor Dekho. weeks: 2 to 52, default 12. The WhatsApp line is left empty
    // (with whatsAppProblem saying why) when the bot's database cannot be read.
    [HasPermission("insights.view")]
    [HttpGet("growth/weekly")]
    public async Task<IActionResult> GetWeeklyGrowth([FromQuery] int weeks = 12) =>
        Ok(await _whatsApp.WeeklyGrowthAsync(weeks));

    [HasPermission("insights.view")]
    [HttpGet("doctordekho/daily")]
    public async Task<IActionResult> GetDoctorDekhoDaily([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null) =>
        Ok(await _whatsApp.DoctorDekhoDailyAsync(from, to));
}
