using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Data.WhatsApp;
using CMSAPI.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace CMSAPI.Data.Google;

/// <summary>An account's secrets (OAuth client secret, refresh token, lead-form key), decrypted only when needed.</summary>
public class GoogleKeys
{
    private readonly WaSecretBox _box;

    public GoogleKeys(WaSecretBox box) => _box = box;

    public WaSecretBox Box => _box;

    private string? Open(string? sealedValue)
    {
        try { return _box.Open(sealedValue); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }

    /// <summary>What signs a Google call in, or null when not signed in (or the keys cannot be read here).</summary>
    public GoogleClient? Client(GoogleAccount a)
    {
        if (string.IsNullOrEmpty(a.ClientId)) return null;
        var secret = Open(a.ClientSecretEnc);
        var refresh = Open(a.RefreshTokenEnc);
        return secret == null || refresh == null ? null : new GoogleClient(a.ClientId, secret, refresh);
    }

    public string? ClientSecret(GoogleAccount a) => Open(a.ClientSecretEnc);
    public string? LeadKey(GoogleAccount a) => Open(a.LeadKeyEnc);

    /// <summary>True when a secret is saved but this server cannot open it (the encryption key changed).</summary>
    public bool Unreadable(GoogleAccount a)
    {
        foreach (var v in new[] { a.ClientSecretEnc, a.RefreshTokenEnc, a.LeadKeyEnc })
        {
            if (string.IsNullOrEmpty(v)) continue;
            try { _box.Open(v); }
            catch (Exception ex) when (ex is CryptographicException or FormatException) { return true; }
        }
        return false;
    }
}

/// <summary>
/// The "state" sent through Google's sign-in and back: which account, who started it, where to return, and
/// until when — signed with a key derived from TokenSettings:Key, so it cannot be made up or reused later.
/// </summary>
public class GoogleSignInState
{
    private readonly byte[]? _key;

    public GoogleSignInState(IConfiguration config)
    {
        var secret = config["TokenSettings:Key"];
        if (!string.IsNullOrWhiteSpace(secret))
            _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 32, info: Encoding.UTF8.GetBytes("nexeagle-cms/google/sign-in-state/v1"));
    }

    /// <summary>For tests.</summary>
    public GoogleSignInState(byte[] key) => _key = key;

    public bool CanSign => _key != null;

    private record Payload(Guid A, Guid U, string R, long E);

    public string? Make(Guid accountId, Guid userId, string returnUrl, DateTime until)
    {
        if (_key == null) return null;
        var json = JsonSerializer.SerializeToUtf8Bytes(new Payload(accountId, userId, returnUrl, new DateTimeOffset(DateTime.SpecifyKind(until, DateTimeKind.Utc)).ToUnixTimeSeconds()));
        var body = B64(json);
        return body + "." + B64(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(body)));
    }

    public (Guid AccountId, Guid UserId, string ReturnUrl)? Read(string? state, DateTime now)
    {
        if (_key == null || string.IsNullOrEmpty(state)) return null;
        var dot = state.IndexOf('.');
        if (dot <= 0) return null;
        var body = state[..dot];
        byte[] sig;
        try { sig = UnB64(state[(dot + 1)..]); } catch (FormatException) { return null; }
        if (!CryptographicOperations.FixedTimeEquals(sig, HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(body)))) return null;
        try
        {
            var p = JsonSerializer.Deserialize<Payload>(UnB64(body));
            if (p == null || DateTimeOffset.FromUnixTimeSeconds(p.E).UtcDateTime < now) return null;
            return (p.A, p.U, p.R);
        }
        catch (Exception ex) when (ex is JsonException or FormatException) { return null; }
    }

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] UnB64(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(t + new string('=', (4 - t.Length % 4) % 4));
    }
}

public static class GoogleShared
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); } catch (JsonException) { return null; }
    }

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);

    /// <summary>The websites the CMS knows and the actions their Google tag sends (names as in GA4).</summary>
    public static readonly IReadOnlyDictionary<string, string> Sites = new Dictionary<string, string>
    {
        ["nexeagle"] = "nexeagle.com",
        ["doctordekho"] = "Doctor Dekho",
    };

    public static List<GoogleSiteEvent> SiteEvents(string? site) => site switch
    {
        "doctordekho" => new()
        {
            new() { Name = "appointment_booked", Label = "Appointments booked", IsLead = true },
            new() { Name = "begin_booking", Label = "Bookings started" },
            new() { Name = "view_doctor", Label = "Doctor profiles viewed" },
            new() { Name = "search", Label = "Searches" },
            new() { Name = "whatsapp_click", Label = "WhatsApp clicks" },
        },
        _ => new()
        {
            new() { Name = "generate_lead", Label = "Contact and demo requests", IsLead = true },
            new() { Name = "whatsapp_click", Label = "WhatsApp clicks" },
            new() { Name = "phone_click", Label = "Call clicks" },
            new() { Name = "email_click", Label = "Email clicks" },
        },
    };

    /// <summary>CRM stages in order; "reached" a stage means being at it or further (Closed Lost is not).</summary>
    public static readonly string[] Stages = { "New", "Contacted", "Demo Scheduled", "Demo Done", "Negotiation", "Closed Won" };

    public static bool Reached(string? current, string stage)
    {
        var c = Array.IndexOf(Stages, current ?? string.Empty);
        var s = Array.IndexOf(Stages, stage);
        return c >= 0 && s >= 0 && c >= s;
    }

    public static readonly string[] DemoStages = { "Demo Done", "Negotiation", "Closed Won" };

    /// <summary>"AW-123456789" and "G-ABC123" style ids: letters, digits and one dash.</summary>
    public static bool ValidTagId(string? id, string prefix) =>
        id != null && id.StartsWith(prefix, StringComparison.Ordinal) && id.Length is > 4 and <= 30 && id.Skip(prefix.Length).All(char.IsLetterOrDigit);

    public static string Digits(string? s) => new((s ?? string.Empty).Where(char.IsDigit).ToArray());

    public static string? Trim(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];

    /// <summary>The site's public address from its Search Console property ("sc-domain:x.com" → "https://x.com").</summary>
    public static string? SiteUrl(string? searchConsoleSite)
    {
        if (string.IsNullOrWhiteSpace(searchConsoleSite)) return null;
        if (searchConsoleSite.StartsWith("sc-domain:", StringComparison.Ordinal)) return "https://" + searchConsoleSite["sc-domain:".Length..].Trim('/');
        return searchConsoleSite.TrimEnd('/');
    }
}
