using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Data.WhatsApp;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Social;

/// <summary>
/// Social → Setup: accounts (a Page, its Instagram account, an ad account), their Meta keys (token and app
/// secret encrypted), the checks, and connecting the webhooks (app subscriptions for the page and instagram
/// objects, and the Page's own subscription).
/// </summary>
public class SocialSetupService : ISocialSetupService
{
    private readonly CmsDbContext _db;
    private readonly IMetaSocial _meta;
    private readonly SocialKeys _keys;

    public SocialSetupService(CmsDbContext db, IMetaSocial meta, SocialKeys keys)
    {
        _db = db;
        _meta = meta;
        _keys = keys;
    }

    private const string NotAllowed = "Only people with \"Connect the Facebook Page, Instagram and ad accounts\" can change this.";
    public const string PageFields = "feed,messages,message_echoes,leadgen";
    public const string InstagramFields = "comments,messages";
    private static readonly Regex MetaId = new("^[0-9]{5,25}$");

    // What each part of Social needs from Meta, for the Setup page's permission table.
    private static readonly (string For, string[] Perms)[] Needs =
    {
        ("See posts and their numbers", new[] { "pages_read_engagement", "instagram_basic", "instagram_manage_insights" }),
        ("Publish and schedule", new[] { "pages_manage_posts", "instagram_content_publish" }),
        ("Reply to and hide comments", new[] { "pages_manage_engagement", "pages_read_user_content", "instagram_manage_comments" }),
        ("Lead forms", new[] { "leads_retrieval" }),
        ("Ad numbers", new[] { "ads_read" }),
        ("Messenger", new[] { "pages_messaging" }),
        ("Instagram messages", new[] { "instagram_manage_messages" }),
    };

    public static SocialAccountDto Account(SocialAccount a, bool hasToken) => Fill(new SocialAccountDto(), a, hasToken);

    private static T Fill<T>(T d, SocialAccount a, bool hasToken) where T : SocialAccountDto
    {
        d.AccountId = a.AccountId;
        d.Name = a.Name;
        d.PageId = a.PageId;
        d.PageName = a.PageName;
        d.IgUserId = a.IgUserId;
        d.IgUsername = a.IgUsername;
        d.AdAccountId = a.AdAccountId;
        d.AdAccountName = a.AdAccountName;
        d.Currency = a.Currency ?? "INR";
        d.Connected = hasToken && a.PageId != null;
        return d;
    }

    public async Task<List<SocialAccountDto>> AccountsAsync() =>
        (await _db.SocialAccounts.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToListAsync())
        .Select(a => Account(a, a.TokenEnc != null)).ToList();

    private async Task<SocialSetupDto> DtoAsync(SocialActor actor, SocialAccount a, string suggested)
    {
        var names = a.UpdatedByUserId is { } by ? await WaShared.NamesAsync(_db, new Guid?[] { by }) : new Dictionary<Guid, string>();
        var dto = Fill(new SocialSetupDto(), a, a.TokenEnc != null);
        dto.CanEdit = actor.CanConnect;
        dto.AppId = a.AppId;
        dto.HasAppSecret = a.AppSecretEnc != null;
        dto.HasToken = a.TokenEnc != null;
        dto.TokenHint = a.TokenHint;
        dto.VerifyToken = actor.CanConnect ? _keys.VerifyToken(a) : null;
        dto.Unreadable = _keys.Unreadable(a);
        dto.CanEncrypt = _keys.Box.CanSeal;
        dto.SuggestedWebhookUrl = suggested;
        dto.WebhookUrl = a.WebhookUrl;
        dto.WebhooksConnectedAt = a.WebhooksConnectedAt;
        dto.LastWebhookAt = a.LastWebhookAt;
        dto.PostsSyncedAt = a.PostsSyncedAt;
        dto.AdsSyncedAt = a.AdsSyncedAt;
        dto.LeadsCheckedAt = a.LeadsCheckedAt;
        dto.SyncError = a.SyncError;
        dto.UpdatedAt = a.UpdatedAt;
        dto.UpdatedBy = a.UpdatedByUserId is { } u ? names.GetValueOrDefault(u) : null;
        return dto;
    }

    public async Task<WorkResult<SocialSetupDto>> GetAsync(SocialActor actor, Guid accountId, string suggestedWebhookUrl)
    {
        var a = await _db.SocialAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == accountId && x.IsActive);
        return a == null ? WorkResult<SocialSetupDto>.Fail(404, "This account does not exist.") : WorkResult<SocialSetupDto>.Success(await DtoAsync(actor, a, suggestedWebhookUrl));
    }

    public async Task<WorkResult<SocialSetupDto>> CreateAsync(SocialActor actor, SocialAccountCreate request, string suggestedWebhookUrl)
    {
        if (!actor.CanConnect) return WorkResult<SocialSetupDto>.Fail(403, NotAllowed);
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length is < 2 or > 100) return WorkResult<SocialSetupDto>.Fail(400, "Give the account a name, like the brand (NexEagle, Doctor Dekho).");
        if (await _db.SocialAccounts.AnyAsync(a => a.Name == name)) return WorkResult<SocialSetupDto>.Fail(409, "An account with this name already exists.");
        var now = DateTime.UtcNow;
        var a = new SocialAccount
        {
            AccountId = Guid.NewGuid(), Name = name, IsActive = true, CreatedAt = now, UpdatedAt = now, UpdatedByUserId = actor.UserId,
            SortOrder = (await _db.SocialAccounts.MaxAsync(x => (int?)x.SortOrder) ?? 0) + 1,
        };
        _db.SocialAccounts.Add(a);
        await _db.SaveChangesAsync();
        return WorkResult<SocialSetupDto>.Success(await DtoAsync(actor, a, suggestedWebhookUrl), "Account added. Enter its Meta keys next.");
    }

    public async Task<WorkResult<SocialSetupDto>> SaveKeysAsync(SocialActor actor, Guid accountId, SocialKeysSave r, string suggestedWebhookUrl)
    {
        if (!actor.CanConnect) return WorkResult<SocialSetupDto>.Fail(403, NotAllowed);
        if (!_keys.Box.CanSeal) return WorkResult<SocialSetupDto>.Fail(503, "The server has no key to encrypt the Meta keys with (TokenSettings__Key or WhatsAppMarketing__EncryptionKey).");
        var a = await _db.SocialAccounts.FirstOrDefaultAsync(x => x.AccountId == accountId && x.IsActive);
        if (a == null) return WorkResult<SocialSetupDto>.Fail(404, "This account does not exist.");

        var appId = string.IsNullOrWhiteSpace(r.AppId) ? null : r.AppId.Trim();
        if (appId != null && !MetaId.IsMatch(appId)) return WorkResult<SocialSetupDto>.Fail(400, "The app ID is digits only (Meta app → App settings → Basic).");
        var secret = r.AppSecret?.Trim();
        if (!string.IsNullOrEmpty(secret) && !Regex.IsMatch(secret, "^[0-9a-fA-F]{32}$")) return WorkResult<SocialSetupDto>.Fail(400, "The app secret is 32 letters and digits (Meta app → App settings → Basic → App secret).");
        var token = r.Token?.Trim();
        if (!string.IsNullOrEmpty(token) && (token.Length < 40 || token.Any(char.IsWhiteSpace))) return WorkResult<SocialSetupDto>.Fail(400, "That does not look like a Meta access token (a long text starting with EAA…).");
        var name = r.Name?.Trim();
        if (!string.IsNullOrEmpty(name) && name != a.Name)
        {
            if (name.Length > 100) return WorkResult<SocialSetupDto>.Fail(400, "The name is too long.");
            if (await _db.SocialAccounts.AnyAsync(x => x.Name == name && x.AccountId != accountId)) return WorkResult<SocialSetupDto>.Fail(409, "An account with this name already exists.");
            a.Name = name;
        }

        a.AppId = appId;
        if (!string.IsNullOrEmpty(secret)) a.AppSecretEnc = _keys.Box.Seal(secret);
        if (r.ClearToken) { a.TokenEnc = null; a.TokenHint = null; }
        else if (!string.IsNullOrEmpty(token))
        {
            // Check it before keeping it: a wrong token is the commonest setup mistake.
            try { await _meta.DiscoverAsync(token); }
            catch (MetaSocialException ex) { return WorkResult<SocialSetupDto>.Fail(400, "Meta refused this token: " + ex.Message); }
            a.TokenEnc = _keys.Box.Seal(token);
            a.TokenHint = token[^4..];
        }
        a.VerifyTokenEnc ??= _keys.Box.Seal(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant());
        a.UpdatedByUserId = actor.UserId;
        a.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return WorkResult<SocialSetupDto>.Success(await DtoAsync(actor, a, suggestedWebhookUrl), "Saved.");
    }

    public async Task<WorkResult<MetaAssets>> DiscoverAsync(SocialActor actor, Guid accountId)
    {
        var a = await _db.SocialAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == accountId && x.IsActive);
        if (a == null) return WorkResult<MetaAssets>.Fail(404, "This account does not exist.");
        var c = _keys.Creds(a);
        if (c == null) return WorkResult<MetaAssets>.Fail(400, "Enter and save the token first.");
        try { return WorkResult<MetaAssets>.Success(await _meta.DiscoverAsync(c.Token)); }
        catch (MetaSocialException ex) { return WorkResult<MetaAssets>.Fail(ex.HttpStatus, ex.Message); }
    }

    public async Task<WorkResult<SocialSetupDto>> ChooseAsync(SocialActor actor, Guid accountId, SocialAssetsChoice choice, string suggestedWebhookUrl)
    {
        if (!actor.CanConnect) return WorkResult<SocialSetupDto>.Fail(403, NotAllowed);
        var a = await _db.SocialAccounts.FirstOrDefaultAsync(x => x.AccountId == accountId && x.IsActive);
        if (a == null) return WorkResult<SocialSetupDto>.Fail(404, "This account does not exist.");
        var c = _keys.Creds(a);
        if (c == null) return WorkResult<SocialSetupDto>.Fail(400, "Enter and save the token first.");
        MetaAssets assets;
        try { assets = await _meta.DiscoverAsync(c.Token); }
        catch (MetaSocialException ex) { return WorkResult<SocialSetupDto>.Fail(ex.HttpStatus, ex.Message); }

        var page = choice.PageId == null ? null : assets.Pages.FirstOrDefault(p => p.Id == choice.PageId);
        if (choice.PageId != null && page == null) return WorkResult<SocialSetupDto>.Fail(400, "The token cannot see that Facebook Page.");
        var ad = choice.AdAccountId == null ? null : assets.AdAccounts.FirstOrDefault(x => x.Id == choice.AdAccountId);
        if (choice.AdAccountId != null && ad == null) return WorkResult<SocialSetupDto>.Fail(400, "The token cannot see that ad account.");
        if (page != null && await _db.SocialAccounts.AnyAsync(x => x.PageId == page.Id && x.AccountId != accountId))
            return WorkResult<SocialSetupDto>.Fail(409, "That Page is already used by another account here.");

        var pageChanged = a.PageId != page?.Id;
        a.PageId = page?.Id;
        a.PageName = page?.Name;
        a.IgUserId = page?.InstagramId;
        a.IgUsername = page?.InstagramUsername;
        a.AdAccountId = ad?.Id;
        a.AdAccountName = ad?.Name;
        a.Currency = ad?.Currency ?? a.Currency ?? "INR";
        if (pageChanged) { a.PostsSyncedAt = null; a.WebhooksConnectedAt = null; }
        a.UpdatedByUserId = actor.UserId;
        a.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var note = page != null && page.InstagramId == null ? " This Page has no Instagram professional account linked; link one in Meta Business Suite to use Instagram here." : "";
        return WorkResult<SocialSetupDto>.Success(await DtoAsync(actor, a, suggestedWebhookUrl), "Saved. Past posts are read in the background (a few minutes)." + note);
    }

    public async Task<WorkResult<SocialChecks>> CheckAsync(SocialActor actor, Guid accountId, string suggestedWebhookUrl)
    {
        var a = await _db.SocialAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == accountId && x.IsActive);
        if (a == null) return WorkResult<SocialChecks>.Fail(404, "This account does not exist.");
        return WorkResult<SocialChecks>.Success(await ChecksAsync(a, suggestedWebhookUrl));
    }

    private async Task<SocialChecks> ChecksAsync(SocialAccount a, string suggested)
    {
        var result = new SocialChecks();
        void Add(string key, string label, string state, string? detail = null) => result.Items.Add(new SocialCheck { Key = key, Label = label, State = state, Detail = detail });
        var missing = new List<string>();
        if (a.TokenEnc == null) missing.Add("token");
        if (a.AppId == null) missing.Add("app ID");
        if (a.AppSecretEnc == null) missing.Add("app secret");
        if (_keys.Unreadable(a)) Add("keys", "Keys readable", "fail", "The saved token or secret cannot be read any more (the server's key changed). Enter them again.");
        Add("keys-entered", "Keys entered", missing.Count == 0 ? "ok" : "todo", missing.Count == 0 ? null : "Missing: " + string.Join(", ", missing) + ".");

        var c = _keys.Creds(a);
        MetaAssets? assets = null;
        if (c == null) Add("token", "Token works", "todo");
        else
        {
            try
            {
                assets = await _meta.DiscoverAsync(c.Token);
                Add("token", "Token works", "ok", $"Sees {assets.Pages.Count} Page(s), {assets.Pages.Count(p => p.InstagramId != null)} Instagram account(s), {assets.AdAccounts.Count} ad account(s)");
            }
            catch (MetaSocialException ex) { Add("token", "Token works", "fail", ex.Message); }
        }

        if (a.PageId == null) Add("accounts", "Accounts chosen", "todo", "Choose the Facebook Page (its Instagram account comes with it) and the ad account.");
        else
        {
            var parts = new List<string> { a.PageName ?? a.PageId };
            parts.Add(a.IgUsername != null ? "@" + a.IgUsername : "no Instagram account linked");
            parts.Add(a.AdAccountName ?? "no ad account");
            Add("accounts", "Accounts chosen", a.IgUserId != null && a.AdAccountId != null ? "ok" : "warn", string.Join(" · ", parts));
        }

        var posts = await _db.SocialPosts.Where(p => p.AccountId == a.AccountId).GroupBy(p => p.Channel).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        if (a.PostsSyncedAt == null) Add("posts", "Past posts brought in", a.PageId == null ? "todo" : "warn", a.PageId == null ? null : "Being read; this takes a few minutes after the accounts are chosen." + (a.SyncError != null ? " Last problem: " + a.SyncError : ""));
        else Add("posts", "Past posts brought in", a.SyncError == null ? "ok" : "warn",
            $"{posts.Sum(p => p.N)} posts (Facebook {posts.FirstOrDefault(p => p.Key == "facebook")?.N ?? 0}, Instagram {posts.FirstOrDefault(p => p.Key == "instagram")?.N ?? 0})" + (a.SyncError != null ? ". Last problem: " + a.SyncError : ""));

        // Webhooks: where the app sends Page and Instagram events, and whether the Page is subscribed.
        var ready = missing.Count == 0 && assets != null && a.PageId != null;
        var target = a.WebhookUrl ?? suggested;
        if (c?.AppSecret != null && c.AppId != null && a.PageId != null)
        {
            try
            {
                var subs = await _meta.AppSubscriptionsAsync(c.AppId, c.AppSecret);
                var page = subs.FirstOrDefault(s => s.Object == "page");
                var pageFields = await _meta.PageSubscribedFieldsAsync(c);
                var pageOk = page != null && Same(page.CallbackUrl, target) && pageFields.Contains("feed") && pageFields.Contains("leadgen") && pageFields.Contains("messages");
                Add("webhooks", "Comments, messages and leads come to the CMS", pageOk ? "ok" : "todo",
                    pageOk ? target : page?.CallbackUrl is { } other && !Same(other, target) ? $"The app sends Page events to {other} now." : "Connect webhooks below.");
                if (a.IgUserId != null)
                {
                    var ig = subs.FirstOrDefault(s => s.Object == "instagram");
                    var igOk = ig != null && Same(ig.CallbackUrl, target);
                    Add("ig-webhooks", "Instagram comments and messages come to the CMS", igOk ? "ok" : "todo", igOk ? null : "Connect webhooks below.");
                    ready &= igOk;
                }
                ready &= pageOk;
            }
            catch (MetaSocialException ex) { Add("webhooks", "Comments, messages and leads come to the CMS", "warn", ex.Message); ready = false; }
        }
        else { Add("webhooks", "Comments, messages and leads come to the CMS", "todo", "Needs the app ID, app secret and the Page first."); ready = false; }

        var forms = await _db.SocialLeadForms.AsNoTracking().Where(f => f.AccountId == a.AccountId).ToListAsync();
        var missed = forms.Sum(f => SocialShared.Read<List<string>>(f.MissingJson)?.Count ?? 0);
        if (a.LastWebhookAt == null && forms.Count == 0) Add("arriving", "Events arriving", "todo", "Nothing yet.");
        else Add("arriving", "Events arriving", missed > 0 ? "warn" : "ok",
            (a.LastWebhookAt is { } last ? $"Last from Meta {last:dd MMM HH:mm} UTC." : "No webhook call yet.") + (missed > 0 ? $" {missed} lead(s) at Meta are missing from the CRM: see Leads." : ""));

        // Permissions, from what the token says it has.
        var granted = assets?.Permissions ?? new List<string>();
        foreach (var (what, perms) in Needs)
        {
            if (a.IgUserId == null && perms.All(p => p.StartsWith("instagram_"))) continue;
            result.Permissions.Add(new SocialPermissionDto
            {
                For = what, Permissions = string.Join(", ", perms),
                State = granted.Count == 0 ? "unknown" : perms.All(granted.Contains) ? "granted" : "missing",
            });
        }
        if (granted.Count > 0 && !granted.Contains("instagram_manage_messages") && a.IgUserId != null)
            Add("ig-messages", "Instagram messages allowed", "todo", "Needs Meta's App Review for instagram_manage_messages.");

        result.Ready = ready;
        return result;
    }

    private static bool Same(string? a, string? b) => a != null && b != null && string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    public async Task<WorkResult<SocialChecks>> ConnectWebhooksAsync(SocialActor actor, Guid accountId, SocialWebhookRequest request, string suggestedWebhookUrl)
    {
        if (!actor.CanConnect) return WorkResult<SocialChecks>.Fail(403, NotAllowed);
        var url = (request.Url ?? string.Empty).Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return WorkResult<SocialChecks>.Fail(400, "Meta only calls https addresses. Use the API's https address, ending with /api/v1/meta/webhook.");
        if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/meta/webhook", StringComparison.OrdinalIgnoreCase))
            return WorkResult<SocialChecks>.Fail(400, "The address must end with /api/v1/meta/webhook.");
        var a = await _db.SocialAccounts.FirstOrDefaultAsync(x => x.AccountId == accountId && x.IsActive);
        if (a == null) return WorkResult<SocialChecks>.Fail(404, "This account does not exist.");
        var c = _keys.Creds(a);
        if (c == null || c.AppId == null || c.AppSecret == null) return WorkResult<SocialChecks>.Fail(400, "Enter and save the token, app ID and app secret first.");
        if (a.PageId == null) return WorkResult<SocialChecks>.Fail(400, "Choose the Facebook Page first.");
        var verify = _keys.VerifyToken(a);
        if (verify == null)
        {
            if (!_keys.Box.CanSeal) return WorkResult<SocialChecks>.Fail(503, "The server has no key to encrypt with.");
            verify = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            a.VerifyTokenEnc = _keys.Box.Seal(verify);
            await _db.SaveChangesAsync();
        }

        try
        {
            // One app has one Page callback: if it serves another system now, ask before taking it over.
            var subs = await _meta.AppSubscriptionsAsync(c.AppId, c.AppSecret);
            var current = subs.FirstOrDefault(s => s.Object == "page")?.CallbackUrl;
            if (current != null && !Same(current, url) && !request.Replace)
                return WorkResult<SocialChecks>.Fail(409, $"This Meta app sends Page events to {current} now. Connecting moves them (lead forms included) to the CMS. If something else relies on that address, use a separate Meta app for Social.");

            await _meta.SubscribeAppAsync(c.AppId, c.AppSecret, "page", url, PageFields, verify);
            if (a.IgUserId != null) await _meta.SubscribeAppAsync(c.AppId, c.AppSecret, "instagram", url, InstagramFields, verify);
            await _meta.SubscribePageAsync(c, PageFields);
        }
        catch (MetaSocialException ex)
        {
            var why = ex.Message;
            if (why.Contains("verif", StringComparison.OrdinalIgnoreCase) || why.Contains("callback", StringComparison.OrdinalIgnoreCase))
                why += " Meta could not check the address: it must be this API, reachable from the internet over https.";
            return WorkResult<SocialChecks>.Fail(ex.HttpStatus, why);
        }

        a.WebhookUrl = url;
        a.WebhooksConnectedAt = DateTime.UtcNow;
        a.UpdatedByUserId = actor.UserId;
        a.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return WorkResult<SocialChecks>.Success(await ChecksAsync(a, suggestedWebhookUrl), "Connected: comments, messages and lead forms now come to the CMS.");
    }
}
