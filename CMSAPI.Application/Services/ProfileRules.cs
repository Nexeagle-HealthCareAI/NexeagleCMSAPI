namespace CMSAPI.Application.Services;

/// <summary>
/// Checks shared by My profile and Users &amp; access, so a person's details follow the same rules
/// whoever changes them. A phone number is also a sign-in name (OTP), so it is stored the way
/// sign-in looks it up: digits with an optional leading "+".
/// </summary>
public static class ProfileRules
{
    public const int MaxNameLength = 150;
    public const int MaxJobTitleLength = 100;
    public const int MaxEmailLength = 256;
    public const int MaxPhotoBytes = 2 * 1024 * 1024;

    /// <summary>Digits and "+" only, as CmsAuthRepository normalises a sign-in name.</summary>
    public static string NormalisePhone(string s) =>
        new string(s.Where(c => char.IsDigit(c) || c == '+').ToArray());

    /// <summary>Null when the name is fine, otherwise what is wrong with it.</summary>
    public static string? CheckName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0) return "Full name is required.";
        if (value.Length > MaxNameLength) return $"Full name can be at most {MaxNameLength} characters.";
        return null;
    }

    public static string? CheckJobTitle(string? title) =>
        (title ?? string.Empty).Trim().Length > MaxJobTitleLength ? $"Job title can be at most {MaxJobTitleLength} characters." : null;

    public static string? CheckEmail(string? email)
    {
        var value = (email ?? string.Empty).Trim();
        if (value.Length == 0) return "Email is required.";
        if (value.Length > MaxEmailLength) return "Email is too long.";
        try
        {
            var parsed = new System.Net.Mail.MailAddress(value);
            return parsed.Address == value ? null : "Invalid email format.";
        }
        catch { return "Invalid email format."; }
    }

    /// <summary>
    /// The phone number to store (null to remove it), or an error. Spaces, dashes and brackets are
    /// dropped; what is left must be 8 to 15 digits, with "+" only at the start.
    /// </summary>
    public static (string? Phone, string? Error) CleanPhone(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        if (raw.Any(c => char.IsLetter(c))) return (null, "Phone number can only have digits, spaces, dashes and a leading +.");
        var phone = NormalisePhone(raw.Trim());
        if (phone.LastIndexOf('+') > 0) return (null, "Phone number can only have + at the start.");
        var digits = phone.TrimStart('+').Length;
        if (digits < 8 || digits > 15) return (null, "Phone number must have 8 to 15 digits, for example +919876543210.");
        return (phone, null);
    }

    /// <summary>JPEG, PNG or WebP, read from the file's first bytes (not its name or stated type).</summary>
    public static string? SniffImageType(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A) return "image/png";
        if (data.Length >= 12 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P') return "image/webp";
        return null;
    }

    public static string? PhotoVersion(DateTime? updatedAt) => updatedAt?.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
