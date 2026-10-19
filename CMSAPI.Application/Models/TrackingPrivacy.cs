using System.Security.Cryptography;
using System.Text;

namespace CMSAPI.Application.Models;

/// <summary>
/// Mirror of EasyHMSAPI.Application.Common.TrackingPrivacy (1HMSAPI). EasyHMS stores AnalyticsEvents.Mobile
/// masked and AnalyticsEvents.MobileHash as HMAC-SHA256 of the normalized number; the CMS hashes a searched
/// number the same way to find it. Keep both copies and the Tracking:MobileHashKey value identical.
/// </summary>
public static class TrackingPrivacy
{
    public const int MinHashKeyLength = 32;

    public static string? NormalizeMobile(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile)) return null;
        var digits = new string(mobile.Where(char.IsDigit).ToArray());
        if (digits.Length < 6) return null;
        return digits.Length > 10 ? digits[^10..] : digits;
    }

    public static string? HashMobile(string? normalizedMobile, string? key)
    {
        if (string.IsNullOrEmpty(normalizedMobile) || string.IsNullOrEmpty(key) || key.Length < MinHashKeyLength)
            return null;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(normalizedMobile))).ToLowerInvariant();
    }
}

/// <summary>Tracking:MobileHashKey, registered as a singleton in Program.cs.</summary>
public sealed record TrackingHashSettings(string? MobileHashKey);
