using System.Text.Json;
using CMSAPI.Application.Models;

namespace CMSAPI.Application.Interfaces;

// CMS → Google (GoogleController). Implementations: CMSAPI.Data/Google.

public interface IGoogleSetupService
{
    Task<List<GoogleAccountDto>> AccountsAsync();
    Task<WorkResult<GoogleSetupDto>> CreateAsync(GoogleActor actor, GoogleAccountCreate request);
    Task<WorkResult<GoogleSetupDto>> SetupAsync(GoogleActor actor, Guid accountId, string redirectUrl, string leadWebhookUrl);
    Task<WorkResult<GoogleSetupDto>> SaveClientAsync(GoogleActor actor, Guid accountId, GoogleClientSave request, string redirectUrl, string leadWebhookUrl);
    /// <summary>The address to send the browser to for Google's sign-in.</summary>
    Task<WorkResult<string>> SignInUrlAsync(GoogleActor actor, Guid accountId, string returnUrl, string redirectUrl);
    /// <summary>Google came back with a code: keep the refresh token. Returns the CMS page to go back to.</summary>
    Task<(bool Ok, string ReturnUrl, string? Error)> CompleteSignInAsync(string code, string state, string redirectUrl, CancellationToken ct = default);
    Task<WorkResult<GoogleDiscovery>> DiscoverAsync(GoogleActor actor, Guid accountId);
    Task<WorkResult<GoogleSetupDto>> ChooseAsync(GoogleActor actor, Guid accountId, GoogleChoice request, string redirectUrl, string leadWebhookUrl);
    Task<WorkResult<GoogleSetupDto>> SaveTagAsync(GoogleActor actor, Guid accountId, GoogleTagSave request, string redirectUrl, string leadWebhookUrl);
    Task<WorkResult<GoogleSetupDto>> NewLeadKeyAsync(GoogleActor actor, Guid accountId, string redirectUrl, string leadWebhookUrl);
    Task<WorkResult<GoogleSetupDto>> SaveOutcomesAsync(GoogleActor actor, Guid accountId, List<GoogleOutcome> outcomes, string redirectUrl, string leadWebhookUrl);
    Task<WorkResult<GoogleChecks>> ChecksAsync(Guid accountId);
    /// <summary>Public: what a website needs to load its Google tag (nothing secret).</summary>
    Task<GoogleSiteTagDto?> SiteTagAsync(string site);
}

public interface IGoogleReportsService
{
    Task<WorkResult<GoogleOverviewDto>> OverviewAsync(GoogleRange range, CancellationToken ct = default);
    Task<WorkResult<GooglePagesDto>> PagesAsync(GoogleRange range, CancellationToken ct = default);
    Task<WorkResult<GooglePageDetailDto>> PageAsync(GoogleRange range, string path, CancellationToken ct = default);
    Task<WorkResult<GoogleAdsDto>> AdsAsync(GoogleAdsQuery query, CancellationToken ct = default);
}

public interface IGoogleLeadsService
{
    Task<WorkResult<GoogleLeadPage>> ListAsync(GoogleLeadQuery query);
    /// <summary>Google's lead form webhook. Status 200 when stored or ignored (test lead), 4xx when refused.</summary>
    Task<WorkResult<Guid?>> IngestFormLeadAsync(JsonElement payload, CancellationToken ct = default);
    /// <summary>A website's contact form. Status 200 when stored (or a duplicate), 4xx when refused.</summary>
    Task<WorkResult<Guid?>> IngestWebsiteLeadAsync(string site, GoogleWebsiteLead lead, string? clientIp, CancellationToken ct = default);
}

public interface IGoogleOutcomesService
{
    /// <summary>Sends CRM results that reached the chosen stages to Google Ads (worker, every few minutes).</summary>
    Task<int> SendDueAsync(CancellationToken ct = default);
}
