using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CMSAPI.Data.WhatsApp;

/// <summary>
/// WhatsApp → Setup. The Meta keys are typed in here and kept in WaConnection (token, app secret and verify
/// token encrypted by WaSecretBox). The checks and buttons do what the setup guide used to ask for by hand:
/// is the token good, is the number in the account, is the app subscribed, where does Meta send this
/// number's webhooks, and has anything arrived.
/// </summary>
public class WaSetupService : IWaSetupService
{
    private readonly CmsDbContext _db;
    private readonly IWaConnectionStore _store;
    private readonly WaSecretBox _box;
    private readonly IServiceProvider _services;

    public WaSetupService(CmsDbContext db, IWaConnectionStore store, WaSecretBox box, IServiceProvider services)
    {
        _db = db;
        _store = store;
        _box = box;
        _services = services;
    }

    private const string NotAllowed = "Only people with \"Connect the WhatsApp number\" can change this.";
    private static readonly Regex MetaId = new("^[0-9]{5,25}$");

    /// <summary>A Meta client made now, so it uses the connection as just saved.</summary>
    private IWaMeta Meta() => _services.GetRequiredService<IWaMeta>();

    private async Task<WaConnection> RowAsync()
    {
        var row = await _db.WaConnections.FirstOrDefaultAsync(x => x.ConnectionId == 1);
        if (row != null) return row;
        row = new WaConnection { ConnectionId = 1, UpdatedAt = DateTime.UtcNow };
        _db.WaConnections.Add(row);
        return row;
    }

    private async Task<WaSetupDto> DtoAsync(WaActor actor, string suggested)
    {
        var c = await _store.RefreshAsync();
        var row = await _db.WaConnections.AsNoTracking().FirstOrDefaultAsync(x => x.ConnectionId == 1);
        var names = row?.UpdatedByUserId is { } by ? await WaShared.NamesAsync(_db, new Guid?[] { by }) : new Dictionary<Guid, string>();
        return new WaSetupDto
        {
            CanEdit = actor.CanConnect,
            PhoneNumberId = c.PhoneNumberId,
            WabaId = c.WabaId,
            AppId = c.AppId,
            HasAccessToken = c.AccessToken != null,
            AccessTokenHint = c.AccessToken is { Length: > 8 } t ? t[^4..] : null,
            TokenSource = c.TokenSource,
            HasAppSecret = c.AppSecret != null,
            HasVerifyToken = c.VerifyToken != null,
            VerifyToken = actor.CanConnect ? c.VerifyToken : null,
            Unreadable = c.Unreadable,
            CanEncrypt = _box.CanSeal,
            SuggestedWebhookUrl = suggested,
            WebhookUrl = row?.WebhookUrl,
            WebhookConnectedAt = row?.WebhookConnectedAt,
            LastWebhookAt = row?.LastWebhookAt,
            UpdatedAt = row?.UpdatedByUserId != null ? row.UpdatedAt : null,
            UpdatedBy = row?.UpdatedByUserId is { } u ? names.GetValueOrDefault(u) : null,
        };
    }

    public async Task<WorkResult<WaSetupDto>> GetAsync(WaActor actor, string suggestedWebhookUrl) =>
        WorkResult<WaSetupDto>.Success(await DtoAsync(actor, suggestedWebhookUrl));

    public async Task<WorkResult<WaSetupDto>> SaveAsync(WaActor actor, WaSetupSaveRequest r, string suggestedWebhookUrl)
    {
        if (!actor.CanConnect) return WorkResult<WaSetupDto>.Fail(403, NotAllowed);
        if (!_box.CanSeal) return WorkResult<WaSetupDto>.Fail(503, "The server has no key to encrypt the WhatsApp keys with (TokenSettings__Key or WhatsAppMarketing__EncryptionKey).");
        string? Id(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var phone = Id(r.PhoneNumberId);
        var waba = Id(r.WabaId);
        var app = Id(r.AppId);
        if (phone != null && !MetaId.IsMatch(phone)) return WorkResult<WaSetupDto>.Fail(400, "The phone number ID is the long number Meta shows under the phone number (digits only), not the phone number itself.");
        if (waba != null && !MetaId.IsMatch(waba)) return WorkResult<WaSetupDto>.Fail(400, "The WhatsApp Business Account ID is digits only.");
        if (app != null && !MetaId.IsMatch(app)) return WorkResult<WaSetupDto>.Fail(400, "The app ID is digits only.");
        if (phone != null && phone == waba) return WorkResult<WaSetupDto>.Fail(400, "The phone number ID and the account ID are two different numbers.");
        var token = r.AccessToken?.Trim();
        if (!string.IsNullOrEmpty(token) && (token.Length < 40 || token.Any(char.IsWhiteSpace)))
            return WorkResult<WaSetupDto>.Fail(400, "That does not look like a Meta access token (a long text starting with EAA…).");
        var secret = r.AppSecret?.Trim();
        if (!string.IsNullOrEmpty(secret) && !Regex.IsMatch(secret, "^[0-9a-fA-F]{32}$"))
            return WorkResult<WaSetupDto>.Fail(400, "The app secret is 32 letters and digits (Meta app → App settings → Basic → App secret).");

        var row = await RowAsync();
        var numberChanged = row.PhoneNumberId != phone;
        row.PhoneNumberId = phone;
        row.WabaId = waba;
        row.AppId = app;
        if (r.ClearAccessToken) { row.AccessTokenEnc = null; row.AccessTokenHint = null; }
        else if (!string.IsNullOrEmpty(token)) { row.AccessTokenEnc = _box.Seal(token); row.AccessTokenHint = token[^4..]; }
        if (r.ClearAppSecret) row.AppSecretEnc = null;
        else if (!string.IsNullOrEmpty(secret)) row.AppSecretEnc = _box.Seal(secret);
        // The webhook was set up for the old number: it has to be connected again.
        if (numberChanged) { row.WebhookConnectedAt = null; row.WebhookUrl = null; }
        row.VerifyTokenEnc ??= _box.Seal(NewToken());
        row.UpdatedByUserId = actor.UserId;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return WorkResult<WaSetupDto>.Success(await DtoAsync(actor, suggestedWebhookUrl), "Saved.");
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public async Task<WorkResult<WaSetupDto>> NewVerifyTokenAsync(WaActor actor, string suggestedWebhookUrl)
    {
        if (!actor.CanConnect) return WorkResult<WaSetupDto>.Fail(403, NotAllowed);
        if (!_box.CanSeal) return WorkResult<WaSetupDto>.Fail(503, "The server has no key to encrypt with.");
        var row = await RowAsync();
        row.VerifyTokenEnc = _box.Seal(NewToken());
        row.UpdatedByUserId = actor.UserId;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return WorkResult<WaSetupDto>.Success(await DtoAsync(actor, suggestedWebhookUrl), "New verify token made. Connect the webhook again.");
    }

    public async Task<WorkResult<WaSetupChecks>> CheckAsync(WaActor actor, string suggestedWebhookUrl) =>
        WorkResult<WaSetupChecks>.Success(await ChecksAsync(suggestedWebhookUrl));

    private async Task<WaSetupChecks> ChecksAsync(string suggested)
    {
        var c = await _store.RefreshAsync();
        var row = await _db.WaConnections.AsNoTracking().FirstOrDefaultAsync(x => x.ConnectionId == 1);
        var list = new List<WaSetupCheck>();
        void Add(string key, string label, string state, string? detail = null) => list.Add(new WaSetupCheck { Key = key, Label = label, State = state, Detail = detail });

        if (c.Unreadable) Add("keys", "Keys readable", "fail", "The saved keys cannot be read any more (the server's key changed). Enter the token and app secret again.");
        var missing = new List<string>();
        if (c.PhoneNumberId == null) missing.Add("phone number ID");
        if (c.WabaId == null) missing.Add("WhatsApp Business Account ID");
        if (c.AccessToken == null) missing.Add("access token");
        if (c.AppSecret == null) missing.Add("app secret");
        Add("keys-entered", "Keys entered", missing.Count == 0 ? "ok" : "todo", missing.Count == 0 ? null : "Missing: " + string.Join(", ", missing) + ".");
        if (c.AppId == null) Add("app-id", "App ID", "warn", "Without it, template pictures and the profile photo cannot be uploaded.");

        var ready = missing.Count == 0 && !c.Unreadable;
        if (!c.IsConfigured)
        {
            Add("token", "Token works", "todo");
            Add("number", "Number is in the account", "todo");
            Add("subscribed", "App receives the account's webhooks", "todo");
            Add("webhook", "Number's messages come to the CMS", "todo");
            Add("arriving", "Messages arriving", "todo");
            return new WaSetupChecks { Items = list, Ready = false };
        }

        var meta = Meta();
        // 1. The token, by reading the number with it.
        try
        {
            var n = await meta.GetNumberAsync();
            Add("token", "Token works", "ok", $"{n["display_phone_number"]?.ToString()} · {n["verified_name"]?.ToString()}".Trim(' ', '·'));
            // The name people see above the chat. Meta reviews it; until then messages may show the number only.
            var nameStatus = n["name_status"]?.ToString();
            Add("display-name", "Display name approved by Meta",
                nameStatus is "APPROVED" or "AVAILABLE_WITHOUT_REVIEW" ? "ok" : nameStatus is "DECLINED" or "NON_EXISTS" ? "fail" : "warn",
                nameStatus switch
                {
                    "APPROVED" or "AVAILABLE_WITHOUT_REVIEW" => n["verified_name"]?.ToString(),
                    "DECLINED" => "Meta declined the display name. Change it in WhatsApp Manager, in the phone number's settings.",
                    "NON_EXISTS" => "No display name yet. Set it in WhatsApp Manager, in the phone number's settings.",
                    null => null,
                    _ => $"Meta's review: {nameStatus.ToLowerInvariant().Replace('_', ' ')}. Messages can go out meanwhile.",
                });
        }
        catch (WaMetaException ex)
        {
            Add("token", "Token works", "fail", ex.Code == 100
                ? "Meta does not know this phone number ID, or the token cannot see it. Check the ID and that the system user has the WhatsApp account."
                : ex.Message);
            Add("number", "Number is in the account", "todo");
            Add("subscribed", "App receives the account's webhooks", "todo");
            Add("webhook", "Number's messages come to the CMS", "todo");
            Add("arriving", "Messages arriving", "todo");
            return new WaSetupChecks { Items = list, Ready = false };
        }

        // 2. The number belongs to the account (templates and pricing are read from the account).
        try
        {
            var numbers = await meta.AccountNumbersAsync();
            if (numbers.ContainsKey(c.PhoneNumberId!)) Add("number", "Number is in the account", "ok", $"{numbers.Count} number(s) in the account.");
            else { Add("number", "Number is in the account", "fail", "This phone number ID is not in that WhatsApp Business Account. Numbers there: " + string.Join(", ", numbers.Values) + "."); ready = false; }
        }
        catch (WaMetaException ex) { Add("number", "Number is in the account", "fail", ex.Message); ready = false; }

        // 3. The app must be subscribed to the account, or Meta sends no webhooks at all.
        try
        {
            var apps = await meta.SubscribedAppsAsync();
            if (apps.Count == 0) { Add("subscribed", "App receives the account's webhooks", "todo", "No app is subscribed to this account yet."); ready = false; }
            else if (c.AppId != null && !apps.ContainsKey(c.AppId)) { Add("subscribed", "App receives the account's webhooks", "todo", "Subscribed: " + string.Join(", ", apps.Values) + ". This app is not."); ready = false; }
            else Add("subscribed", "App receives the account's webhooks", "ok", string.Join(", ", apps.Values));
        }
        catch (WaMetaException ex) { Add("subscribed", "App receives the account's webhooks", "warn", ex.Message); }

        // 4. Where Meta sends this number's webhooks.
        try
        {
            var config = await meta.WebhookConfigurationAsync();
            var current = config["phone_number"]?.ToString();
            var target = row?.WebhookUrl ?? suggested;
            if (!string.IsNullOrEmpty(current) && Same(current, target)) Add("webhook", "Number's messages come to the CMS", "ok", current);
            else
            {
                var now = current ?? config["whatsapp_business_account"]?.ToString() ?? config["application"]?.ToString();
                Add("webhook", "Number's messages come to the CMS", "todo", now != null ? $"They go to {now} now." : "Not connected yet.");
                ready = false;
            }
        }
        catch (WaMetaException ex) { Add("webhook", "Number's messages come to the CMS", "warn", ex.Message); }

        // 5. Something has actually arrived.
        if (row?.LastWebhookAt is { } last) Add("arriving", "Messages arriving", "ok", $"Last from Meta: {last:dd MMM yyyy HH:mm} UTC.");
        else Add("arriving", "Messages arriving", "todo", "Nothing yet. Send any message from a phone to the number.");

        // 6. The business profile people open in WhatsApp. Not needed to send, but a bare profile looks untrustworthy.
        try
        {
            var p = await meta.GetProfileAsync();
            var gaps = new List<string>();
            if (string.IsNullOrWhiteSpace(p["profile_picture_url"]?.ToString())) gaps.Add("photo");
            if (string.IsNullOrWhiteSpace(p["about"]?.ToString())) gaps.Add("about");
            if (string.IsNullOrWhiteSpace(p["description"]?.ToString())) gaps.Add("description");
            if (string.IsNullOrWhiteSpace(p["email"]?.ToString())) gaps.Add("email");
            if (p["websites"] is not JsonArray { Count: > 0 }) gaps.Add("website");
            Add("profile", "Business profile filled in", gaps.Count == 0 ? "ok" : "warn",
                gaps.Count == 0 ? null : "Missing: " + string.Join(", ", gaps) + ". Fill it in below.");
        }
        catch (WaMetaException ex) { Add("profile", "Business profile filled in", "warn", ex.Message); }

        return new WaSetupChecks { Items = list, Ready = ready };
    }

    private static bool Same(string a, string b) => string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    public async Task<WorkResult<WaSetupChecks>> SubscribeAppAsync(WaActor actor, string suggestedWebhookUrl)
    {
        if (!actor.CanConnect) return WorkResult<WaSetupChecks>.Fail(403, NotAllowed);
        await _store.RefreshAsync();
        try { await Meta().SubscribeAppAsync(); }
        catch (WaNotConnectedException ex) { return WorkResult<WaSetupChecks>.Fail(400, ex.Message); }
        catch (WaMetaException ex) { return WorkResult<WaSetupChecks>.Fail(ex.HttpStatus, ex.Message); }
        return WorkResult<WaSetupChecks>.Success(await ChecksAsync(suggestedWebhookUrl), "The app now receives this account's webhooks.");
    }

    public async Task<WorkResult<WaSetupChecks>> ConnectWebhookAsync(WaActor actor, WaConnectWebhookRequest request, string suggestedWebhookUrl)
    {
        if (!actor.CanConnect) return WorkResult<WaSetupChecks>.Fail(403, NotAllowed);
        var url = (request.Url ?? string.Empty).Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return WorkResult<WaSetupChecks>.Fail(400, "Meta only calls https addresses. Use the API's https address (like https://cms-api.nexeagle.com/api/v1/whatsapp/webhook).");
        if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/whatsapp/webhook", StringComparison.OrdinalIgnoreCase))
            return WorkResult<WaSetupChecks>.Fail(400, "The address must end with /api/v1/whatsapp/webhook.");
        var c = await _store.RefreshAsync();
        if (!c.IsConfigured) return WorkResult<WaSetupChecks>.Fail(400, "Enter and save the phone number ID, account ID and token first.");
        if (c.AppSecret == null) return WorkResult<WaSetupChecks>.Fail(400, "Enter the app secret first: without it the CMS refuses what Meta sends.");
        var row = await RowAsync();
        if (c.VerifyToken == null || (row.VerifyTokenEnc == null && _box.CanSeal))
        {
            if (!_box.CanSeal) return WorkResult<WaSetupChecks>.Fail(503, "The server has no key to encrypt with.");
            row.VerifyTokenEnc = _box.Seal(NewToken());
            await _db.SaveChangesAsync();
            c = await _store.RefreshAsync();
        }
        try
        {
            // Meta calls GET <url>?hub.verify_token=… now; WhatsAppWebhookController answers with the store's token.
            await Meta().SetWebhookAsync(url, c.VerifyToken!);
        }
        catch (WaMetaException ex)
        {
            var why = ex.Message;
            if (why.Contains("verif", StringComparison.OrdinalIgnoreCase) || why.Contains("callback", StringComparison.OrdinalIgnoreCase))
                why += " Meta could not check the address: it must be this API, reachable from the internet over https.";
            return WorkResult<WaSetupChecks>.Fail(ex.HttpStatus, why);
        }
        row.WebhookUrl = url;
        row.WebhookConnectedAt = DateTime.UtcNow;
        row.UpdatedByUserId = actor.UserId;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return WorkResult<WaSetupChecks>.Success(await ChecksAsync(suggestedWebhookUrl), "Connected: this number's messages now come to the CMS. The Doctor Dekho bot's number is unchanged.");
    }

    public async Task<WorkResult<string>> TestAsync(WaActor actor, WaTestMessageRequest request)
    {
        if (!actor.CanConnect) return WorkResult<string>.Fail(403, NotAllowed);
        var phone = WaRules.NormalisePhone(request.Phone);
        if (phone == null) return WorkResult<string>.Fail(400, "That is not a phone number.");
        await _store.RefreshAsync();
        try
        {
            var body = new JsonObject
            {
                ["to"] = phone,
                ["type"] = "template",
                ["template"] = new JsonObject { ["name"] = "hello_world", ["language"] = new JsonObject { ["code"] = "en_US" } },
            };
            var id = await Meta().SendAsync(body);
            return WorkResult<string>.Success(id, $"Sent Meta's sample message to {WaRules.Display(phone)}. Reply to it from the phone: the reply should appear in the Inbox.");
        }
        catch (WaNotConnectedException ex) { return WorkResult<string>.Fail(400, ex.Message); }
        catch (WaMetaException ex) when (ex.Code == 132001)
        {
            return WorkResult<string>.Fail(400, "Meta's sample template hello_world is not in this account. Instead, send any message from your phone to the number and look for it in the Inbox.");
        }
        catch (WaMetaException ex) { return WorkResult<string>.Fail(ex.HttpStatus, ex.Message); }
    }
}
