using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Data.WhatsApp;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CMSAPI.Data.Google;

/// <summary>
/// Google → Setup: accounts (one per website), Google sign-in (the account's own OAuth client; the refresh token is
/// encrypted), choosing the Analytics property, Google Ads account and Search Console site, the website tag IDs,
/// the key Google lead forms send, the CRM stages sent back to Google Ads, and the checklist.
/// </summary>
public class GoogleSetupService : IGoogleSetupService
{
    private readonly CmsDbContext _db;
    private readonly IGoogleApi _google;
    private readonly GoogleKeys _keys;
    private readonly GoogleSignInState _state;
    private readonly ILogger<GoogleSetupService> _logger;
    private readonly Func<DateTime> _now;

    public GoogleSetupService(CmsDbContext db, IGoogleApi google, GoogleKeys keys, GoogleSignInState state, ILogger<GoogleSetupService> logger)
        : this(db, google, keys, state, logger, () => DateTime.UtcNow) { }

    internal GoogleSetupService(CmsDbContext db, IGoogleApi google, GoogleKeys keys, GoogleSignInState state, ILogger<GoogleSetupService> logger, Func<DateTime> now)
    {
        _db = db;
        _google = google;
        _keys = keys;
        _state = state;
        _logger = logger;
        _now = now;
    }

    private const string NoConnect = "Changing this needs \"Connect Google accounts\".";

    private static GoogleAccountDto Public(GoogleAccount a, GoogleKeys keys) => Fill(new GoogleAccountDto(), a, keys);

    private static T Fill<T>(T d, GoogleAccount a, GoogleKeys keys) where T : GoogleAccountDto
    {
        d.AccountId = a.AccountId;
        d.Name = a.Name;
        d.Site = a.Site;
        d.Ga4PropertyName = a.Ga4PropertyName;
        d.AdsAccountName = a.AdsAccountName;
        d.SearchConsoleSite = a.SearchConsoleSite;
        d.Currency = a.Currency ?? "INR";
        d.SignedIn = keys.Client(a) != null;
        d.HasAnalytics = !string.IsNullOrEmpty(a.Ga4PropertyId);
        d.HasAds = !string.IsNullOrEmpty(a.AdsCustomerId);
        return d;
    }

    private async Task<GoogleSetupDto> SetupDtoAsync(GoogleAccount a, GoogleActor actor, string redirectUrl, string leadWebhookUrl)
    {
        var names = await WaShared.NamesAsync(_db, new[] { a.UpdatedByUserId });
        var d = Fill(new GoogleSetupDto(), a, _keys);
        d.CanEdit = actor.CanConnect;
        d.CanEncrypt = _keys.Box.CanSeal;
        d.Unreadable = _keys.Unreadable(a);
        d.ClientId = a.ClientId;
        d.HasClientSecret = !string.IsNullOrEmpty(a.ClientSecretEnc);
        d.SignedInEmail = a.SignedInEmail;
        d.SignedInAt = a.SignedInAt;
        d.Ga4PropertyId = a.Ga4PropertyId;
        d.AdsCustomerId = a.AdsCustomerId;
        d.AdsLoginCustomerId = a.AdsLoginCustomerId;
        d.RedirectUrl = redirectUrl;
        d.MeasurementId = a.MeasurementId;
        d.AdsConversionId = a.AdsConversionId;
        d.EventLabels = GoogleShared.Read<Dictionary<string, string>>(a.EventLabelsJson) ?? new();
        d.SiteEvents = GoogleShared.SiteEvents(a.Site);
        d.LeadWebhookUrl = leadWebhookUrl;
        d.LeadKey = actor.CanConnect ? _keys.LeadKey(a) : null;
        d.LastLeadAt = a.LastLeadAt;
        d.Outcomes = GoogleShared.Read<List<GoogleOutcome>>(a.OutcomesJson) ?? new();
        d.LastOutcomeAt = a.LastOutcomeAt;
        d.LastOutcomeError = a.LastOutcomeError;
        d.LastReadError = a.LastReadError;
        d.UpdatedAt = a.UpdatedAt;
        d.UpdatedBy = a.UpdatedByUserId is { } u ? names.GetValueOrDefault(u) : null;
        return d;
    }

    private Task<GoogleAccount?> FindAsync(Guid accountId) => _db.GoogleAccounts.FirstOrDefaultAsync(a => a.AccountId == accountId && a.IsActive);

    private void Touch(GoogleAccount a, GoogleActor actor)
    {
        a.UpdatedAt = _now();
        a.UpdatedByUserId = actor.UserId == Guid.Empty ? null : actor.UserId;
    }

    public async Task<List<GoogleAccountDto>> AccountsAsync()
    {
        var list = await _db.GoogleAccounts.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.SortOrder).ThenBy(a => a.CreatedAt).ToListAsync();
        return list.Select(a => Public(a, _keys)).ToList();
    }

    public async Task<WorkResult<GoogleSetupDto>> CreateAsync(GoogleActor actor, GoogleAccountCreate request)
    {
        if (!actor.CanConnect) return WorkResult<GoogleSetupDto>.Fail(403, NoConnect);
        var name = GoogleShared.Trim(request.Name, 100);
        if (name == null) return WorkResult<GoogleSetupDto>.Fail(400, "Give the account a name, e.g. NexEagle.");
        var site = string.IsNullOrWhiteSpace(request.Site) ? null : request.Site.Trim().ToLowerInvariant();
        if (site != null && !GoogleShared.Sites.ContainsKey(site)) return WorkResult<GoogleSetupDto>.Fail(400, "Choose nexeagle.com or Doctor Dekho as the website.");
        if (site != null && await _db.GoogleAccounts.AnyAsync(a => a.Site == site && a.IsActive))
            return WorkResult<GoogleSetupDto>.Fail(409, $"{GoogleShared.Sites[site]} already has an account here.");
        var now = _now();
        var a = new GoogleAccount
        {
            AccountId = Guid.NewGuid(), Name = name, Site = site, Currency = "INR", CreatedAt = now, UpdatedAt = now,
            SortOrder = await _db.GoogleAccounts.CountAsync(), UpdatedByUserId = actor.UserId == Guid.Empty ? null : actor.UserId,
        };
        if (_keys.Box.CanSeal) a.LeadKeyEnc = _keys.Box.Seal(NewKey());
        _db.GoogleAccounts.Add(a);
        await _db.SaveChangesAsync();
        return WorkResult<GoogleSetupDto>.Success(await SetupDtoAsync(a, actor, string.Empty, string.Empty), $"{name} added.");
    }

    private static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public async Task<WorkResult<GoogleSetupDto>> SetupAsync(GoogleActor actor, Guid accountId, string redirectUrl, string leadWebhookUrl)
    {
        var a = await FindAsync(accountId);
        return a == null ? WorkResult<GoogleSetupDto>.Fail(404, "This account does not exist.") : WorkResult<GoogleSetupDto>.Success(await SetupDtoAsync(a, actor, redirectUrl, leadWebhookUrl));
    }

    public async Task<WorkResult<GoogleSetupDto>> SaveClientAsync(GoogleActor actor, Guid accountId, GoogleClientSave r, string redirectUrl, string leadWebhookUrl)
    {
        if (!actor.CanConnect) return WorkResult<GoogleSetupDto>.Fail(403, NoConnect);
        var a = await FindAsync(accountId);
        if (a == null) return WorkResult<GoogleSetupDto>.Fail(404, "This account does not exist.");
        if (GoogleShared.Trim(r.Name, 100) is { } name) a.Name = name;
        if (r.Site != null)
        {
            var site = r.Site.Trim().Length == 0 ? null : r.Site.Trim().ToLowerInvariant();
            if (site != null && !GoogleShared.Sites.ContainsKey(site)) return WorkResult<GoogleSetupDto>.Fail(400, "Choose nexeagle.com or Doctor Dekho as the website.");
            if (site != null && site != a.Site && await _db.GoogleAccounts.AnyAsync(x => x.Site == site && x.IsActive && x.AccountId != a.AccountId))
                return WorkResult<GoogleSetupDto>.Fail(409, $"{GoogleShared.Sites[site]} already belongs to another account here.");
            a.Site = site;
        }
        if (r.ClientId != null)
        {
            var id = r.ClientId.Trim();
            if (id.Length > 0 && !id.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal))
                return WorkResult<GoogleSetupDto>.Fail(400, "That is not an OAuth client ID: it ends with .apps.googleusercontent.com (Google Cloud → APIs & Services → Credentials).");
            if (id != a.ClientId) { a.RefreshTokenEnc = null; a.SignedInEmail = null; a.SignedInAt = null; }   // another client: sign in again
            a.ClientId = id.Length == 0 ? null : id;
        }
        if (!string.IsNullOrWhiteSpace(r.ClientSecret))
        {
            if (!_keys.Box.CanSeal) return WorkResult<GoogleSetupDto>.Fail(503, "This server has no encryption key, so secrets cannot be saved. Ask whoever runs the API to set TokenSettings__Key.");
            a.ClientSecretEnc = _keys.Box.Seal(r.ClientSecret.Trim());
        }
        if (r.SignOut) { a.RefreshTokenEnc = null; a.SignedInEmail = null; a.SignedInAt = null; }
        Touch(a, actor);
        await _db.SaveChangesAsync();
        return WorkResult<GoogleSetupDto>.Success(await SetupDtoAsync(a, actor, redirectUrl, leadWebhookUrl), r.SignOut ? "Signed out of Google." : "Saved.");
    }

    public async Task<WorkResult<string>> SignInUrlAsync(GoogleActor actor, Guid accountId, string returnUrl, string redirectUrl)
    {
        if (!actor.CanConnect) return WorkResult<string>.Fail(403, NoConnect);
        var a = await FindAsync(accountId);
        if (a == null) return WorkResult<string>.Fail(404, "This account does not exist.");
        if (string.IsNullOrEmpty(a.ClientId) || _keys.ClientSecret(a) == null)
            return WorkResult<string>.Fail(400, "Save the OAuth client ID and secret first.");
        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var back) || back.Scheme is not ("https" or "http"))
            return WorkResult<string>.Fail(400, "The page to return to is not a web address.");
        var state = _state.Make(a.AccountId, actor.UserId, returnUrl, _now().AddMinutes(15));
        if (state == null) return WorkResult<string>.Fail(503, "This server has no signing key (TokenSettings__Key), so Google sign-in cannot start.");
        return WorkResult<string>.Success(_google.SignInUrl(a.ClientId, redirectUrl, state));
    }

    private static string WithQuery(string url, string query) => url + (url.Contains('?') ? "&" : "?") + query;

    public async Task<(bool Ok, string ReturnUrl, string? Error)> CompleteSignInAsync(string code, string state, string redirectUrl, CancellationToken ct = default)
    {
        var s = _state.Read(state, _now());
        if (s is not { } st) return (false, string.Empty, "The sign-in link has expired. Start again from Google → Setup.");
        var a = await FindAsync(st.AccountId);
        var secret = a == null ? null : _keys.ClientSecret(a);
        if (a == null || secret == null || string.IsNullOrEmpty(a.ClientId)) return (false, st.ReturnUrl, "This account has no OAuth client any more.");
        try
        {
            var tokens = await _google.ExchangeCodeAsync(a.ClientId, secret, code, redirectUrl, ct);
            if (string.IsNullOrEmpty(tokens.RefreshToken))
                return (false, st.ReturnUrl, "Google did not give lasting access. Remove the CMS's access at myaccount.google.com/permissions and sign in again.");
            var granted = (tokens.Scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            a.RefreshTokenEnc = _keys.Box.Seal(tokens.RefreshToken);
            a.SignedInEmail = await _google.EmailAsync(tokens.AccessToken, ct);
            a.SignedInAt = _now();
            a.UpdatedAt = _now();
            a.UpdatedByUserId = st.UserId == Guid.Empty ? null : st.UserId;
            a.LastReadError = null;
            await _db.SaveChangesAsync(ct);
            var missing = GoogleApi.Scopes.Where(x => x.StartsWith("https://", StringComparison.Ordinal) && !granted.Contains(x)).Select(x => x[(x.LastIndexOf('/') + 1)..]).ToList();
            return (true, WithQuery(st.ReturnUrl, missing.Count == 0 ? "google=connected" : "google=partial&missing=" + Uri.EscapeDataString(string.Join(',', missing))), null);
        }
        catch (GoogleApiException ex)
        {
            _logger.LogWarning("Google sign-in for {Account} failed: {Message}", a.Name, ex.Message);
            return (false, st.ReturnUrl, ex.Message);
        }
    }

    public async Task<WorkResult<GoogleDiscovery>> DiscoverAsync(GoogleActor actor, Guid accountId)
    {
        if (!actor.CanConnect) return WorkResult<GoogleDiscovery>.Fail(403, NoConnect);
        var a = await FindAsync(accountId);
        if (a == null) return WorkResult<GoogleDiscovery>.Fail(404, "This account does not exist.");
        if (_keys.Client(a) is not { } c) return WorkResult<GoogleDiscovery>.Fail(400, "Sign in with Google first.");
        var d = new GoogleDiscovery { SignedInEmail = a.SignedInEmail };
        try { d.Properties = await _google.Ga4PropertiesAsync(c); }
        catch (GoogleApiException ex) { if (ex.SignInGone) return WorkResult<GoogleDiscovery>.Fail(401, ex.Message); d.Problems["analytics"] = ex.Message; }
        try
        {
            d.AdsAccounts = (await _google.AdsCustomersAsync(c)).Where(x => !x.IsManager)
                .Select(x => new GoogleAdsAccountDto { CustomerId = x.CustomerId, Name = x.Name, Currency = x.Currency, LoginCustomerId = x.LoginCustomerId }).ToList();
        }
        catch (GoogleApiException ex) { d.Problems["ads"] = ex.Message; }
        try { d.SearchConsoleSites = await _google.SearchConsoleSitesAsync(c); }
        catch (GoogleApiException ex) { d.Problems["searchConsole"] = ex.Message; }
        if (!string.IsNullOrEmpty(a.AdsCustomerId))
        {
            try { d.ConversionActions = await ConversionActionsAsync(c, a); }
            catch (GoogleApiException ex) { d.Problems["conversionActions"] = ex.Message; }
        }
        return WorkResult<GoogleDiscovery>.Success(d);
    }

    /// <summary>Conversion actions that accept imported clicks (the only kind results can be sent to).</summary>
    private async Task<List<GoogleConversionActionDto>> ConversionActionsAsync(GoogleClient c, GoogleAccount a)
    {
        var rows = await _google.AdsSearchAsync(c, a.AdsCustomerId!, a.AdsLoginCustomerId,
            "SELECT conversion_action.id, conversion_action.name FROM conversion_action WHERE conversion_action.type = 'UPLOAD_CLICKS' AND conversion_action.status = 'ENABLED'");
        return rows.Select(r => GoogleApi.Obj(r, "conversionAction"))
            .Select(x => new GoogleConversionActionDto { Id = GoogleApi.Str(x, "id") ?? string.Empty, Name = GoogleApi.Str(x, "name") ?? string.Empty })
            .Where(x => x.Id.Length > 0).OrderBy(x => x.Name).ToList();
    }

    public async Task<WorkResult<GoogleSetupDto>> ChooseAsync(GoogleActor actor, Guid accountId, GoogleChoice r, string redirectUrl, string leadWebhookUrl)
    {
        if (!actor.CanConnect) return WorkResult<GoogleSetupDto>.Fail(403, NoConnect);
        var a = await FindAsync(accountId);
        if (a == null) return WorkResult<GoogleSetupDto>.Fail(404, "This account does not exist.");
        if (_keys.Client(a) is not { } c) return WorkResult<GoogleSetupDto>.Fail(400, "Sign in with Google first.");
        try
        {
            // Names, currency and the manager account come from Google, not from the browser.
            var property = GoogleShared.Digits(r.Ga4PropertyId);
            if (property.Length > 0)
            {
                var p = (await _google.Ga4PropertiesAsync(c)).FirstOrDefault(x => x.PropertyId == property);
                if (p == null) return WorkResult<GoogleSetupDto>.Fail(400, "The signed-in Google account cannot see that Analytics property.");
                a.Ga4PropertyId = p.PropertyId;
                a.Ga4PropertyName = p.Name;
            }
            else { a.Ga4PropertyId = null; a.Ga4PropertyName = null; }

            var customer = GoogleShared.Digits(r.AdsCustomerId);
            if (customer.Length > 0)
            {
                var cu = (await _google.AdsCustomersAsync(c)).FirstOrDefault(x => x.CustomerId == customer && !x.IsManager);
                if (cu == null) return WorkResult<GoogleSetupDto>.Fail(400, "The signed-in Google account cannot reach that Google Ads account.");
                a.AdsCustomerId = cu.CustomerId;
                a.AdsLoginCustomerId = cu.LoginCustomerId;
                a.AdsAccountName = cu.Name;
                a.Currency = cu.Currency ?? a.Currency;
                a.TimeZone = cu.TimeZone;
            }
            else { a.AdsCustomerId = null; a.AdsLoginCustomerId = null; a.AdsAccountName = null; }

            var gsc = r.SearchConsoleSite?.Trim();
            if (!string.IsNullOrEmpty(gsc))
            {
                if (!(await _google.SearchConsoleSitesAsync(c)).Contains(gsc))
                    return WorkResult<GoogleSetupDto>.Fail(400, "The signed-in Google account cannot see that Search Console site.");
                a.SearchConsoleSite = gsc;
            }
            else a.SearchConsoleSite = null;
        }
        catch (GoogleApiException ex)
        {
            return WorkResult<GoogleSetupDto>.Fail(ex.SignInGone ? 401 : 502, ex.Message);
        }
        a.LastReadError = null;
        Touch(a, actor);
        await _db.SaveChangesAsync();
        return WorkResult<GoogleSetupDto>.Success(await SetupDtoAsync(a, actor, redirectUrl, leadWebhookUrl), "Saved.");
    }

    private static readonly Regex Label = new("^[A-Za-z0-9_-]{1,60}$", RegexOptions.Compiled);

    public async Task<WorkResult<GoogleSetupDto>> SaveTagAsync(GoogleActor actor, Guid accountId, GoogleTagSave r, string redirectUrl, string leadWebhookUrl)
    {
        if (!actor.CanConnect) return WorkResult<GoogleSetupDto>.Fail(403, NoConnect);
        var a = await FindAsync(accountId);
        if (a == null) return WorkResult<GoogleSetupDto>.Fail(404, "This account does not exist.");
        var g = r.MeasurementId?.Trim().ToUpperInvariant();
        var aw = r.AdsConversionId?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(g) && !GoogleShared.ValidTagId(g, "G-")) return WorkResult<GoogleSetupDto>.Fail(400, "The measurement ID looks like G-XXXXXXXXXX (Analytics → Admin → Data streams).");
        if (!string.IsNullOrEmpty(aw) && !GoogleShared.ValidTagId(aw, "AW-")) return WorkResult<GoogleSetupDto>.Fail(400, "The Google Ads conversion ID looks like AW-123456789 (Google Ads → Goals → Conversions → Tag setup).");
        var known = GoogleShared.SiteEvents(a.Site).Select(e => e.Name).ToHashSet();
        var labels = new Dictionary<string, string>();
        foreach (var (k, v) in r.EventLabels ?? new())
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            if (!known.Contains(k)) return WorkResult<GoogleSetupDto>.Fail(400, $"\"{k}\" is not an action this website sends.");
            if (!Label.IsMatch(v.Trim())) return WorkResult<GoogleSetupDto>.Fail(400, $"The conversion label for {k} has letters, digits, - and _ only (the part after the / in AW-123/AbCdEf).");
            labels[k] = v.Trim();
        }
        if (labels.Count > 0 && string.IsNullOrEmpty(aw)) return WorkResult<GoogleSetupDto>.Fail(400, "Conversion labels need the Google Ads conversion ID too.");
        a.MeasurementId = string.IsNullOrEmpty(g) ? null : g;
        a.AdsConversionId = string.IsNullOrEmpty(aw) ? null : aw;
        a.EventLabelsJson = labels.Count == 0 ? null : GoogleShared.Write(labels);
        Touch(a, actor);
        await _db.SaveChangesAsync();
        return WorkResult<GoogleSetupDto>.Success(await SetupDtoAsync(a, actor, redirectUrl, leadWebhookUrl), "Saved. The website picks it up within 5 minutes.");
    }

    public async Task<WorkResult<GoogleSetupDto>> NewLeadKeyAsync(GoogleActor actor, Guid accountId, string redirectUrl, string leadWebhookUrl)
    {
        if (!actor.CanConnect) return WorkResult<GoogleSetupDto>.Fail(403, NoConnect);
        var a = await FindAsync(accountId);
        if (a == null) return WorkResult<GoogleSetupDto>.Fail(404, "This account does not exist.");
        if (!_keys.Box.CanSeal) return WorkResult<GoogleSetupDto>.Fail(503, "This server has no encryption key (TokenSettings__Key).");
        a.LeadKeyEnc = _keys.Box.Seal(NewKey());
        Touch(a, actor);
        await _db.SaveChangesAsync();
        return WorkResult<GoogleSetupDto>.Success(await SetupDtoAsync(a, actor, redirectUrl, leadWebhookUrl), "New key made. Paste it into every lead form in Google Ads; the old key no longer works.");
    }

    public async Task<WorkResult<GoogleSetupDto>> SaveOutcomesAsync(GoogleActor actor, Guid accountId, List<GoogleOutcome> outcomes, string redirectUrl, string leadWebhookUrl)
    {
        if (!actor.CanConnect) return WorkResult<GoogleSetupDto>.Fail(403, NoConnect);
        var a = await FindAsync(accountId);
        if (a == null) return WorkResult<GoogleSetupDto>.Fail(404, "This account does not exist.");
        var clean = new List<GoogleOutcome>();
        foreach (var o in outcomes ?? new())
        {
            if (!GoogleShared.Stages.Contains(o.Stage) || o.Stage == "New") return WorkResult<GoogleSetupDto>.Fail(400, $"\"{o.Stage}\" is not a CRM stage that can be sent.");
            var id = GoogleShared.Digits(o.ConversionActionId);
            if (id.Length == 0) return WorkResult<GoogleSetupDto>.Fail(400, $"Choose the Google Ads conversion action for {o.Stage}.");
            if (clean.Any(x => x.Stage == o.Stage)) return WorkResult<GoogleSetupDto>.Fail(400, $"{o.Stage} is listed twice.");
            if (o.Value is < 0) return WorkResult<GoogleSetupDto>.Fail(400, "A value cannot be negative.");
            clean.Add(new GoogleOutcome { Stage = o.Stage, ConversionActionId = id, ConversionActionName = GoogleShared.Trim(o.ConversionActionName, 200), Value = o.Value });
        }
        if (clean.Count > 0 && string.IsNullOrEmpty(a.AdsCustomerId)) return WorkResult<GoogleSetupDto>.Fail(400, "Choose the Google Ads account first.");
        a.OutcomesJson = clean.Count == 0 ? null : GoogleShared.Write(clean);
        Touch(a, actor);
        await _db.SaveChangesAsync();
        return WorkResult<GoogleSetupDto>.Success(await SetupDtoAsync(a, actor, redirectUrl, leadWebhookUrl), clean.Count == 0 ? "Nothing is sent to Google now." : "Saved. Leads that reach these stages are sent within 10 minutes.");
    }

    public async Task<WorkResult<GoogleChecks>> ChecksAsync(Guid accountId)
    {
        var a = await FindAsync(accountId);
        if (a == null) return WorkResult<GoogleChecks>.Fail(404, "This account does not exist.");
        var items = new List<GoogleCheck>();
        var c = _keys.Client(a);
        var signin = new GoogleCheck { Key = "signin", Label = "Signed in with Google" };
        if (_keys.Unreadable(a)) { signin.State = "fail"; signin.Detail = "The saved keys cannot be read on this server. Save the client secret and sign in again."; }
        else if (c == null) { signin.State = "todo"; signin.Detail = string.IsNullOrEmpty(a.ClientId) ? "Enter the OAuth client, then sign in." : "Sign in with Google."; }
        else { signin.State = "ok"; signin.Detail = a.SignedInEmail; }
        items.Add(signin);

        var today = DateOnly.FromDateTime(_now());
        var analytics = new GoogleCheck { Key = "analytics", Label = "Google Analytics property chosen" };
        var tag = new GoogleCheck { Key = "tag", Label = "Website tag is sending" };
        if (string.IsNullOrEmpty(a.Ga4PropertyId)) { analytics.State = "todo"; tag.State = "todo"; tag.Detail = "Choose the Analytics property first."; }
        else if (c == null) { analytics.State = "todo"; analytics.Detail = a.Ga4PropertyName; tag.State = "todo"; }
        else
        {
            try
            {
                var events = GoogleShared.SiteEvents(a.Site);
                var r = await _google.RunReportAsync(c, a.Ga4PropertyId, new GaReportRequest
                {
                    From = today.AddDays(-1), To = today, Dimensions = { "eventName" }, Metrics = { "eventCount" },
                    InListDimension = "eventName", InListValues = events.Select(e => e.Name).Append("page_view").ToList(),
                });
                analytics.State = "ok";
                analytics.Detail = a.Ga4PropertyName;
                var seen = r.Rows.Where(x => x.Metrics.FirstOrDefault() > 0).Select(x => x.Dimensions.FirstOrDefault()).ToHashSet();
                if (string.IsNullOrEmpty(a.MeasurementId)) { tag.State = "todo"; tag.Detail = "Enter the measurement ID (G-…) below."; }
                else if (!seen.Contains("page_view")) { tag.State = "warn"; tag.Detail = "No page views in the last 24 hours yet. It can take a day for Google to report them."; }
                else
                {
                    var actions = events.Count(e => seen.Contains(e.Name));
                    tag.State = "ok";
                    tag.Detail = $"Page views and {actions} of {events.Count} site actions seen in the last 24 hours";
                }
            }
            catch (GoogleApiException ex)
            {
                analytics.State = "fail";
                analytics.Detail = ex.Message;
                tag.State = "todo";
            }
        }
        items.Add(analytics);

        var ads = new GoogleCheck { Key = "ads", Label = "Google Ads account chosen" };
        if (string.IsNullOrEmpty(a.AdsCustomerId)) ads.State = "todo";
        else if (c == null) { ads.State = "todo"; ads.Detail = a.AdsAccountName; }
        else
        {
            try
            {
                await _google.AdsSearchAsync(c, a.AdsCustomerId, a.AdsLoginCustomerId, "SELECT customer.id FROM customer");
                ads.State = "ok";
                ads.Detail = a.AdsAccountName;
            }
            catch (GoogleApiException ex) { ads.State = "fail"; ads.Detail = ex.Message; }
        }
        items.Add(ads);
        items.Add(tag);

        items.Add(new GoogleCheck
        {
            Key = "leads", Label = "Google lead forms send to the CMS",
            State = a.LastLeadAt != null ? "ok" : "todo",
            Detail = a.LastLeadAt is { } t ? $"Last lead {t:dd MMM, HH:mm} UTC" : "Paste the webhook address and key into each lead form, then use “Send test data”.",
        });
        var outcomes = GoogleShared.Read<List<GoogleOutcome>>(a.OutcomesJson) ?? new();
        items.Add(new GoogleCheck
        {
            Key = "outcomes", Label = "Results sent to Google",
            State = outcomes.Count == 0 ? "todo" : a.LastOutcomeError != null ? "warn" : "ok",
            Detail = outcomes.Count == 0 ? "Choose which CRM stages are sent." : a.LastOutcomeError ?? (a.LastOutcomeAt is { } o ? $"Last sent {o:dd MMM, HH:mm} UTC" : "Nothing sent yet."),
        });
        items.Add(new GoogleCheck
        {
            Key = "searchConsole", Label = "Search Console (optional)",
            State = string.IsNullOrEmpty(a.SearchConsoleSite) ? "todo" : "ok",
            Detail = a.SearchConsoleSite ?? "Shows the searches that led to each page.",
        });
        var ready = items.Where(i => i.Key is "signin" or "analytics").All(i => i.State == "ok");
        return WorkResult<GoogleChecks>.Success(new GoogleChecks { Ready = ready, Items = items });
    }

    public async Task<GoogleSiteTagDto?> SiteTagAsync(string site)
    {
        var key = (site ?? string.Empty).Trim().ToLowerInvariant();
        if (!GoogleShared.Sites.ContainsKey(key)) return null;
        var a = await _db.GoogleAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Site == key && x.IsActive);
        if (a == null) return new GoogleSiteTagDto();
        return new GoogleSiteTagDto
        {
            MeasurementId = a.MeasurementId,
            AdsConversionId = a.AdsConversionId,
            EventLabels = GoogleShared.Read<Dictionary<string, string>>(a.EventLabelsJson) ?? new(),
        };
    }
}
