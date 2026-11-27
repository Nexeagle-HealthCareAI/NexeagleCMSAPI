using System.Text.Json;
using Asp.Versioning;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace CMSAPI.Controllers;

// CMS → Google, the routes that cannot have a CMS sign-in:
//   GET  google/oauth/callback        Google's sign-in comes back here (checked by the signed "state")
//   POST google/leads                 Google Ads lead forms (checked by the key each form sends)
//   POST google/website-leads/{site}  the website's contact form (nexeagle.com; checked, rate-limited)
//   GET  google/site-tag/{site}       the website's Google tag IDs (public: they are in every page anyway)
[AllowAnonymous]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/google")]
public class GooglePublicController : ControllerBase
{
    private readonly IGoogleSetupService _setup;
    private readonly IGoogleLeadsService _leads;
    private readonly IConfiguration _config;
    private readonly ILogger<GooglePublicController> _logger;

    public GooglePublicController(IGoogleSetupService setup, IGoogleLeadsService leads, IConfiguration config, ILogger<GooglePublicController> logger)
    {
        _setup = setup;
        _leads = leads;
        _config = config;
        _logger = logger;
    }

    [HttpGet("oauth/callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        var redirect = GoogleController.PublicApi(_config, Request) + "/api/v1/google/oauth/callback";
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return Content(Page("Google sign-in was cancelled" + (error != null ? $" ({error})" : "") + ". Close this tab and try again from CMS → Google → Setup."), "text/html");
        var (ok, back, problem) = await _setup.CompleteSignInAsync(code, state, redirect, HttpContext.RequestAborted);
        if (ok) return Redirect(back);
        if (!string.IsNullOrEmpty(back)) return Redirect(back + (back.Contains('?') ? "&" : "?") + "google=error&reason=" + Uri.EscapeDataString(problem ?? "unknown"));
        return Content(Page(problem ?? "Google sign-in did not complete."), "text/html");
    }

    private static string Page(string message) =>
        "<!doctype html><meta charset=\"utf-8\"><title>Google sign-in</title><body style=\"font:16px system-ui;margin:48px\"><p>"
        + System.Net.WebUtility.HtmlEncode(message) + "</p></body>";

    [HttpPost("leads")]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> Lead([FromBody] JsonElement payload)
    {
        var r = await _leads.IngestFormLeadAsync(payload, HttpContext.RequestAborted);
        if (!r.Ok) _logger.LogWarning("Google lead refused: {Message}", r.Message);
        // Google retries on 5xx; a refused key or a bad body will never succeed, so those answer 4xx.
        return r.Ok ? Ok(new { }) : StatusCode(r.Status, new { message = r.Message });
    }

    [HttpPost("website-leads/{site}")]
    [RequestSizeLimit(32 * 1024)]
    public async Task<IActionResult> WebsiteLead(string site, [FromBody] GoogleWebsiteLead lead)
    {
        var ip = Request.Headers["X-Forwarded-For"].ToString().Split(',')[0].Trim();
        if (string.IsNullOrEmpty(ip)) ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var r = await _leads.IngestWebsiteLeadAsync(site, lead, ip, HttpContext.RequestAborted);
        return r.Ok ? Ok(new { success = true, message = r.Message }) : StatusCode(r.Status, new { success = false, message = r.Message });
    }

    [HttpGet("site-tag/{site}")]
    [EnableCors("GoogleSiteTag")]
    public async Task<IActionResult> SiteTag(string site, [FromServices] IMemoryCache cache)
    {
        var key = "google:site-tag:" + site.ToLowerInvariant();
        if (!cache.TryGetValue(key, out GoogleSiteTagDto? tag))
        {
            tag = await _setup.SiteTagAsync(site);
            if (tag == null) return NotFound();
            cache.Set(key, tag, TimeSpan.FromMinutes(5));
        }
        Response.Headers.CacheControl = "public, max-age=300";
        return Ok(tag);
    }
}
