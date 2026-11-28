namespace CMSAPI.Application.Models;

/// <summary>The numbers one keyword (or a whole topic) has, for scoring.</summary>
public class KeywordFacts
{
    public string Site { get; set; } = string.Empty;
    public string Intent { get; set; } = "other";
    public string? City { get; set; }
    public long? Volume { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal? Position { get; set; }
    public long Visitors { get; set; }
    public long Last7 { get; set; }
    public long Prev7 { get; set; }
    /// <summary>Topics only: monthly demand per language.</summary>
    public Dictionary<string, long> DemandByLanguage { get; set; } = new();
    /// <summary>Topics only: languages already published for it.</summary>
    public HashSet<string> PublishedLanguages { get; set; } = new();
}

public record KeywordVerdict(string? Kind, string? Why, int Score);

/// <summary>
/// Why a keyword or topic is worth work now, and how much: close to page one, demand with no page, seen but not
/// clicked, rising, or (topics) demand in Hindi/Hinglish/Bengali that only English content answers. Score 0–100 from
/// demand, fit (can we serve it), closeness to page one and business value, weighted as Setup says.
/// </summary>
public static class KeywordScoring
{
    public const int CloseMinImpressions = 30;
    public const int CtrMinImpressions = 100;
    public const double LowCtr = 0.015;
    public const int RisingMin = 20;
    public const int GapMinVolume = 100;
    public const int GapMinVisitors = 10;
    public const int LangMinDemand = 100;

    private static readonly HashSet<string> NonEnglish = new() { "hi", "hg", "bn" };

    public static long Demand(KeywordFacts f) => Math.Max(f.Volume ?? 0, Math.Max(f.Impressions * 30 / 28, f.Visitors * 30));

    public static int Score(KeywordFacts f, KeywordWeights w)
    {
        var demand = Math.Clamp(Math.Log10(1 + Demand(f)) / 4.0, 0, 1);
        double fit = f.Site == KeywordSites.DoctorDekho
            ? f.Intent switch { "doctor" or "nearme" or "cost" or "hospital" or "lab" => f.City != null ? 1 : 0.7, "info" => 0.8, _ => 0.4 }
            : f.Intent switch { "software" or "cost" or "howto" => 1, "brand" => 0.6, _ => 0.4 };
        double close = f.Position switch { null => 0.4, <= 3 => 0.3, <= 5 => 0.6, <= 20 => 1, <= 40 => 0.6, _ => 0.3 };
        double value = f.Intent switch { "doctor" or "nearme" or "cost" or "software" => 1, "hospital" or "lab" or "howto" => 0.8, "info" => 0.6, "brand" => 0.5, _ => 0.3 };
        var total = Math.Max(1, w.Demand + w.Fit + w.Close + w.Value);
        return (int)Math.Round(100 * (w.Demand * demand + w.Fit * fit + w.Close * close + w.Value * value) / total);
    }

    public static KeywordVerdict Evaluate(KeywordFacts f, KeywordWeights w, bool topic = false)
    {
        var score = Score(f, w);
        var fmt = (long n) => n.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));
        if (f.Last7 >= RisingMin && f.Last7 >= 1.5 * Math.Max(f.Prev7, 1))
        {
            var pct = f.Prev7 > 0 ? $"up {(f.Last7 - f.Prev7) * 100 / f.Prev7}% on the week before" : "new this week";
            return new("rising", $"{fmt(f.Last7)} searches or showings in the last 7 days, {pct}.", Math.Min(100, score + 10));
        }
        if (topic)
        {
            var all = f.DemandByLanguage.Values.Sum();
            var other = f.DemandByLanguage.Where(kv => NonEnglish.Contains(kv.Key)).Sum(kv => kv.Value);
            var missing = f.DemandByLanguage.Where(kv => NonEnglish.Contains(kv.Key) && kv.Value > 0 && !f.PublishedLanguages.Contains(kv.Key)).Select(kv => kv.Key).ToList();
            if (all > 0 && other >= LangMinDemand && other * 2 >= all && missing.Count > 0)
                return new("lang", $"{other * 100 / all}% of the demand is in {string.Join(", ", missing.Select(LanguageName))}, and no content in {(missing.Count == 1 ? "it" : "them")} is published yet.", Math.Min(100, score + 5));
        }
        if (f.Position is >= 5 and <= 20 && f.Impressions >= CloseMinImpressions)
            return new("close", $"Average position {f.Position:0.#} on {fmt(f.Impressions)} showings in 28 days: a better answer can reach page one.", score);
        if (f.Position is < 5 && f.Impressions >= CtrMinImpressions && f.Clicks < f.Impressions * LowCtr)
            return new("ctr", $"Position {f.Position:0.#} but only {(f.Impressions == 0 ? 0 : f.Clicks * 1000 / f.Impressions / 10.0):0.#}% of {fmt(f.Impressions)} showings were clicked: the title and description need work.", score);
        if (f.Impressions == 0 && ((f.Volume ?? 0) >= GapMinVolume || f.Visitors >= GapMinVisitors))
        {
            var demand = (f.Volume ?? 0) >= GapMinVolume ? $"{fmt(f.Volume!.Value)} searches a month in India" : $"typed into our search by {fmt(f.Visitors)} visitors in 28 days";
            return new("gap", $"{demand}, and none of our pages shows for it.", score);
        }
        return new(null, null, score);
    }

    public static string LanguageName(string code) => code switch
    {
        "en" => "English", "hg" => "Hinglish", "hi" => "Hindi", "bn" => "Bengali", _ => "other languages",
    };
}
