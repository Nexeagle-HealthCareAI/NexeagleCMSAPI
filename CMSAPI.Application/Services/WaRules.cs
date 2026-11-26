using System.Globalization;
using System.Text.RegularExpressions;

namespace CMSAPI.Application.Services;

/// <summary>
/// WhatsApp's rules, in one place: phone numbers, countries, the 24-hour reply window, opt-out words,
/// template names and limits, template variables and the price of a message.
/// </summary>
public static class WaRules
{
    /// <summary>Free-form messages may only be sent this long after the person last wrote.</summary>
    public static readonly TimeSpan ReplyWindow = TimeSpan.FromHours(24);

    public const int MaxBody = 1024;
    public const int MaxHeaderText = 60;
    public const int MaxFooter = 60;
    public const int MaxButtons = 10;
    public const int MaxButtonText = 25;
    public const int MaxTextMessage = 4096;

    public static readonly string[] Categories = { "MARKETING", "UTILITY", "AUTHENTICATION" };

    /// <summary>Words that mean "stop sending me marketing", in English and Hindi.</summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "stop", "unsubscribe", "stop promotions", "stop all", "opt out", "optout", "cancel", "remove me",
        "बंद", "बंद करो", "रोको",
    };

    /// <summary>
    /// A WhatsApp number as Meta uses it: digits with the country code, no "+" — or null when it can't be one.
    /// A 10-digit Indian mobile (starting 6–9) gets 91 in front; a leading 0 is dropped first.
    /// </summary>
    public static string? NormalisePhone(string? raw, string defaultCountryCode = "91")
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var plus = raw.TrimStart().StartsWith('+') || raw.TrimStart().StartsWith("00");
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (raw.TrimStart().StartsWith("00")) digits = digits[2..];
        if (!plus)
        {
            if (digits.Length == 11 && digits[0] == '0') digits = digits[1..];
            if (digits.Length == 10 && "6789".Contains(digits[0])) digits = defaultCountryCode + digits;
        }
        return digits.Length is >= 8 and <= 15 && digits[0] != '0' ? digits : null;
    }

    /// <summary>"+91 98765 43210" for showing a stored number.</summary>
    public static string Display(string phone) =>
        phone.StartsWith("91") && phone.Length == 12 ? $"+91 {phone[2..7]} {phone[7..]}" : "+" + phone;

    // Calling codes → country, longest first where they share a start.
    private static readonly (string Code, string Country)[] CallingCodes =
    {
        ("971", "AE"), ("966", "SA"), ("974", "QA"), ("968", "OM"), ("973", "BH"), ("965", "KW"), ("977", "NP"),
        ("880", "BD"), ("975", "BT"), ("960", "MV"), ("94", "LK"), ("92", "PK"), ("65", "SG"), ("60", "MY"),
        ("61", "AU"), ("64", "NZ"), ("44", "GB"), ("49", "DE"), ("33", "FR"), ("34", "ES"), ("39", "IT"),
        ("31", "NL"), ("27", "ZA"), ("234", "NG"), ("254", "KE"), ("62", "ID"), ("63", "PH"), ("66", "TH"),
        ("84", "VN"), ("86", "CN"), ("81", "JP"), ("82", "KR"), ("55", "BR"), ("52", "MX"), ("7", "RU"),
        ("91", "IN"), ("1", "US"),
    };

    /// <summary>The country of a number from its calling code (two letters), or "**" when unknown.</summary>
    public static string CountryOf(string phone)
    {
        foreach (var (code, country) in CallingCodes.OrderByDescending(c => c.Code.Length))
            if (phone.StartsWith(code, StringComparison.Ordinal)) return country;
        return "**";
    }

    /// <summary>Whether a free-form message may be sent now: the person wrote within the last 24 hours.</summary>
    public static bool WindowOpen(DateTime? lastInboundAt, DateTime nowUtc) =>
        lastInboundAt.HasValue && nowUtc - lastInboundAt.Value < ReplyWindow;

    /// <summary>When the window closes, or null when it is closed.</summary>
    public static DateTime? WindowClosesAt(DateTime? lastInboundAt, DateTime nowUtc) =>
        WindowOpen(lastInboundAt, nowUtc) ? lastInboundAt!.Value + ReplyWindow : null;

    public static bool IsStopWord(string? text) =>
        !string.IsNullOrWhiteSpace(text) && StopWords.Contains(text.Trim().TrimEnd('.', '!').Trim());

    /// <summary>Meta's template name rule: lower case letters, digits and underscores, up to 512.</summary>
    public static bool ValidTemplateName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 512 && Regex.IsMatch(name, "^[a-z0-9_]+$");

    /// <summary>"Diwali offer 2026!" → "diwali_offer_2026".</summary>
    public static string SuggestTemplateName(string text)
    {
        var name = Regex.Replace(text.Trim().ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        return name.Length > 0 ? name[..Math.Min(name.Length, 100)] : "template";
    }

    private static readonly Regex Variable = new(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled);

    /// <summary>The variables in a text, in order of first use: {{1}} {{2}} → ["1","2"]; {{name}} → ["name"].</summary>
    public static List<string> VariablesIn(string? text)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(text)) return list;
        foreach (Match m in Variable.Matches(text))
            if (!list.Contains(m.Groups[1].Value)) list.Add(m.Groups[1].Value);
        return list;
    }

    /// <summary>
    /// Problems with positional variables: they must be {{1}}, {{2}}… with none missing, and a text may not
    /// start or end with one (Meta rejects those).
    /// </summary>
    public static string? CheckVariables(string? text, bool named, string part)
    {
        var vars = VariablesIn(text);
        if (vars.Count == 0) return null;
        var t = text!.Trim();
        if (t.StartsWith("{{") || t.EndsWith("}}")) return $"The {part} cannot start or end with a variable. Add some words around it.";
        if (named)
            return vars.All(v => Regex.IsMatch(v, "^[a-z][a-z0-9_]*$")) ? null : $"Variable names in the {part} must be lower case letters, digits and _ (like {{{{first_name}}}}).";
        if (!vars.All(v => int.TryParse(v, out _))) return $"Use numbered variables in the {part}: {{{{1}}}}, {{{{2}}}}…";
        var numbers = vars.Select(int.Parse).OrderBy(n => n).ToList();
        for (var i = 0; i < numbers.Count; i++)
            if (numbers[i] != i + 1) return $"The variables in the {part} must be {{{{1}}}}, {{{{2}}}}… in order, with none missing.";
        return null;
    }

    /// <summary>Fills {{1}} / {{name}} with values (for showing a sent template as people saw it).</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values) =>
        Variable.Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    /// <summary>Meta's pricing category in a status webhook ("marketing") → the rate key.</summary>
    public static string RateCategory(string? metaCategory) => (metaCategory ?? "service").ToLowerInvariant() switch
    {
        "marketing" or "marketing_lite" => "marketing",
        "utility" => "utility",
        "authentication" or "authentication_international" => "authentication",
        _ => "service",
    };

    /// <summary>
    /// What a message costs: its rate when Meta says it is billable, otherwise nothing.
    /// rates: (country, category) → rate; "**" is the rate for any other country.
    /// </summary>
    public static decimal CostOf(bool billable, string phone, string category, IReadOnlyDictionary<(string, string), decimal> rates)
    {
        if (!billable) return 0m;
        var country = CountryOf(phone);
        if (rates.TryGetValue((country, category), out var rate)) return rate;
        return rates.TryGetValue(("**", category), out var other) ? other : 0m;
    }

    /// <summary>The first day of the month (UTC) that `when` falls in — budgets run by calendar month.</summary>
    public static DateTime MonthStart(DateTime when) => new(when.Year, when.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    public static string Money(decimal amount, string currency) =>
        currency == "INR" ? "₹" + amount.ToString("N2", CultureInfo.GetCultureInfo("en-IN")) : $"{amount:N2} {currency}";

    /// <summary>A short line for the chat list: the text, or what kind of message it was.</summary>
    public static string Preview(string type, string? body)
    {
        var text = (body ?? string.Empty).Replace('\n', ' ').Trim();
        if (text.Length > 0) return text.Length > 120 ? text[..117] + "…" : text;
        return type switch
        {
            "image" => "Photo", "video" => "Video", "audio" => "Voice message", "document" => "Document",
            "sticker" => "Sticker", "location" => "Location", "contacts" => "Contact card", "reaction" => "Reaction",
            "template" => "Template message", _ => "Message",
        };
    }
}
