using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CMSAPI.Application.Interfaces;
using CMSAPI.Data.WhatsApp;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CMSAPI.Data.Social;

/// <summary>An account's keys, decrypted only when a Meta call needs them.</summary>
public class SocialKeys
{
    private readonly CmsDbContext _db;
    private readonly WaSecretBox _box;

    public SocialKeys(CmsDbContext db, WaSecretBox box)
    {
        _db = db;
        _box = box;
    }

    public WaSecretBox Box => _box;

    /// <summary>The keys, or null when the token is missing or cannot be read.</summary>
    public SocialCreds? Creds(SocialAccount a)
    {
        try
        {
            var token = _box.Open(a.TokenEnc);
            if (token == null) return null;
            return new SocialCreds(token, a.AppId, _box.Open(a.AppSecretEnc), a.PageId, a.IgUserId, a.AdAccountId);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }

    public bool Unreadable(SocialAccount a)
    {
        try { _box.Open(a.TokenEnc); _box.Open(a.AppSecretEnc); _box.Open(a.VerifyTokenEnc); return false; }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return true; }
    }

    public string? VerifyToken(SocialAccount a)
    {
        try { return _box.Open(a.VerifyTokenEnc); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }

    public async Task<(SocialAccount Account, SocialCreds Creds)?> LoadAsync(Guid accountId)
    {
        var a = await _db.SocialAccounts.FirstOrDefaultAsync(x => x.AccountId == accountId && x.IsActive);
        if (a == null) return null;
        var c = Creds(a);
        return c == null ? null : (a, c);
    }
}

/// <summary>
/// Time-limited public links to a draft's media, for Instagram: Meta fetches pictures and videos by URL, and
/// the docs bucket is private. A link names the file, an expiry and an HMAC; nothing else is reachable.
/// </summary>
public class SocialMediaLinks
{
    private readonly byte[]? _key;
    private readonly string? _publicApi;

    public SocialMediaLinks(IConfiguration config)
    {
        var secret = config["TokenSettings:Key"];
        if (!string.IsNullOrWhiteSpace(secret))
            _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 32, info: Encoding.UTF8.GetBytes("nexeagle-cms/social/media-links/v1"));
        _publicApi = string.IsNullOrWhiteSpace(config["Social:PublicApiUrl"]) ? null : config["Social:PublicApiUrl"]!.Trim().TrimEnd('/');
    }

    /// <summary>For tests.</summary>
    public SocialMediaLinks(byte[] key, string publicApi) { _key = key; _publicApi = publicApi; }

    private string Sig(string key, long exp) =>
        Convert.ToHexString(HMACSHA256.HashData(_key!, Encoding.UTF8.GetBytes($"{key}\n{exp}"))).ToLowerInvariant();

    /// <summary>The API's public address: Social__PublicApiUrl, else taken from the account's webhook address.</summary>
    public string? PublicApi(SocialAccount? a)
    {
        if (_publicApi != null) return _publicApi;
        var hook = a?.WebhookUrl;
        if (string.IsNullOrEmpty(hook)) return null;
        var i = hook.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? hook[..i] : null;
    }

    public string? Link(SocialAccount a, string key, TimeSpan life)
    {
        var api = PublicApi(a);
        if (_key == null || api == null) return null;
        var exp = DateTimeOffset.UtcNow.Add(life).ToUnixTimeSeconds();
        return $"{api}/api/v1/social/public-media?key={Uri.EscapeDataString(key)}&exp={exp}&sig={Sig(key, exp)}";
    }

    public bool Valid(string key, long exp, string sig)
    {
        if (_key == null || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp || !key.StartsWith("social/", StringComparison.Ordinal) || key.Contains("..")) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sig(key, exp)), Encoding.ASCII.GetBytes(sig ?? ""));
    }
}

internal static class SocialShared
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Preview(string? text, int max = 120)
    {
        var t = (text ?? string.Empty).Replace('\n', ' ').Trim();
        return t.Length <= max ? t : t[..(max - 1)] + "…";
    }

    /// <summary>A CRM lead number in the same form as the existing Meta lead webhook.</summary>
    public static string LeadNumber() => $"LEAD-{DateTime.UtcNow:MMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    public static string Channel(string c) => c switch { "facebook" => "Facebook", "instagram" => "Instagram", "messenger" => "Messenger", _ => c };

    public static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); } catch (JsonException) { return null; }
    }

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);

    /// <summary>People who hold a permission through a role or their own grant (and are not denied it).</summary>
    public static async Task<List<(Guid UserId, string FullName)>> PeopleWithAsync(CmsDbContext db, string key)
    {
        var permission = await db.CmsPermissions.AsNoTracking().FirstOrDefaultAsync(p => p.Key == key);
        if (permission == null) return new();
        var byRole = from ur in db.CmsUserRoles
                     join rp in db.CmsRolePermissions on ur.RoleId equals rp.RoleId
                     join r in db.CmsRoles on ur.RoleId equals r.RoleId
                     where rp.PermissionId == permission.PermissionId && r.IsActive
                     select ur.UserId;
        var allowed = db.CmsUserPermissions.Where(x => x.PermissionId == permission.PermissionId && x.Effect == "Allow").Select(x => x.UserId);
        var denied = db.CmsUserPermissions.Where(x => x.PermissionId == permission.PermissionId && x.Effect == "Deny").Select(x => x.UserId);
        var people = await db.CmsUsers.AsNoTracking()
            .Where(u => u.IsActive && (byRole.Contains(u.UserId) || allowed.Contains(u.UserId)) && !denied.Contains(u.UserId))
            .OrderBy(u => u.FullName).Select(u => new { u.UserId, u.FullName }).ToListAsync();
        return people.Select(p => (p.UserId, p.FullName)).ToList();
    }
}
