using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CMSAPI.Application.Interfaces;

namespace CMSAPI.Application.Models;

// CMS → Marketing → Keywords: shapes the screens receive, and the rules that tag a search phrase.
// Plan: claude/keyword-content-engine-plan.md. Tables: CMSDatabase script 38.

public static class KeywordSites
{
    public const string DoctorDekho = "doctordekho";
    public const string NexEagle = "nexeagle";
    public static readonly string[] All = { DoctorDekho, NexEagle };
    public static bool Valid(string? site) => site is DoctorDekho or NexEagle;
}

public static class KeywordKinds
{
    public static readonly string[] Languages = { "en", "hg", "hi", "bn", "other" };
    public static readonly string[] Intents = { "doctor", "nearme", "info", "cost", "hospital", "lab", "software", "howto", "brand", "other" };
    public static readonly string[] ContentKinds = { "page", "article", "guide", "social", "whatsapp", "ad" };
    public static readonly string[] Stages = { "idea", "brief", "writing", "review", "ready", "published" };
    /// <summary>Opportunity kinds, in the order the screens show them.</summary>
    public static readonly string[] Opportunities = { "close", "gap", "lang", "ctr", "rising" };
}

/// <summary>
/// The rules that tag a search phrase: clean it, tell its language from its script and words, guess what the
/// searcher wants, and find a city. A person's correction always wins over these (the *ByPerson flags).
/// </summary>
public static class KeywordText
{
    private static readonly Regex NotWord = new(@"[^\p{L}\p{M}\p{N}]+", RegexOptions.Compiled);

    /// <summary>Lower case, one space between words, no punctuation; Indian scripts keep their vowel signs.</summary>
    public static string Normalize(string? text)
    {
        var s = (text ?? string.Empty).Normalize(NormalizationForm.FormC).ToLowerInvariant();
        s = NotWord.Replace(s, " ").Trim();
        return s.Length <= 300 ? s : s[..300].Trim();
    }

    public static string[] Words(string normalized) => normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // Hindi words people type in English letters. "me" counts only when it is not "near me" / "for me".
    private static readonly HashSet<string> HinglishWords = new(StringComparer.Ordinal)
    {
        "ka", "ki", "ke", "ko", "mein", "hai", "hain", "kya", "kaise", "kaisa", "kaisi", "kitna", "kitni", "kitne", "kyu", "kyun", "kyon",
        "kab", "kahan", "kaha", "ilaj", "ilaaj", "dawa", "dawai", "dard", "bimari", "rog", "upay", "gharelu", "khana", "chahiye", "hota",
        "hoti", "hone", "karna", "kare", "karein", "liye", "wala", "wali", "wale", "sabse", "accha", "achha", "acha", "aur", "bhi", "nahi",
        "nahin", "paas", "najdik", "nazdeek", "jaldi", "sujan", "bukhar", "khansi", "jukam", "pathri", "kamar", "ghutno", "daant",
        "aankh", "naak", "zyada", "jyada", "kaun", "kon", "kaunsa", "konsa", "sugar ka", "bacchon", "bachon", "mahila", "garbhavastha",
    };

    /// <summary>en, hg (Hindi in English letters), hi (Devanagari), bn (Bengali script) or other.</summary>
    public static string Language(string normalized)
    {
        int deva = 0, beng = 0, latin = 0, other = 0;
        foreach (var ch in normalized)
        {
            if (ch >= 'ऀ' && ch <= 'ॿ') deva++;
            else if (ch >= 'ঀ' && ch <= '৿') beng++;
            else if ((ch >= 'a' && ch <= 'z')) latin++;
            else if (char.IsLetter(ch)) other++;
        }
        if (beng > 0 && beng >= deva) return "bn";
        if (deva > 0) return "hi";
        if (latin == 0) return other > 0 ? "other" : "en";
        var words = Words(normalized);
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (w == "me" && (i == 0 || (words[i - 1] != "near" && words[i - 1] != "for" && words[i - 1] != "call" && words[i - 1] != "help"))) return "hg";
            if (HinglishWords.Contains(w)) return "hg";
        }
        return other > latin ? "other" : "en";
    }

    private static bool Any(string normalized, params string[] parts) =>
        parts.Any(p => p.Contains(' ') ? normalized.Contains(p, StringComparison.Ordinal) : Words(normalized).Contains(p) || (p.Length > 4 && normalized.Contains(p, StringComparison.Ordinal)));

    /// <summary>What the searcher most likely wants. Doctor Dekho: doctor, nearme, cost, hospital, lab, info. nexeagle.com: brand, software, cost, howto, other.</summary>
    public static string Intent(string site, string normalized)
    {
        if (site == KeywordSites.NexEagle)
        {
            if (Any(normalized, "nexeagle", "1hms", "1rad", "1lab", "1pharma", "nex eagle")) return "brand";
            if (Any(normalized, "price", "pricing", "cost", "rate", "charges", "fees", "kitna", "quotation")) return "cost";
            if (Any(normalized, "how", "kaise", "guide", "steps", "process", "what is", "meaning", "checklist", "requirements")) return "howto";
            if (Any(normalized, "software", "system", "app", "erp", "hms", "his", "lims", "ris", "pacs", "emr", "ehr", "billing", "solution", "platform", "management")) return "software";
            return "other";
        }
        if (Any(normalized, "near me", "nearby", "paas", "najdik", "nazdeek", "पास", "নিকটবর্তী", "কাছে")) return "nearme";
        if (Any(normalized, "fees", "fee", "cost", "price", "charges", "kharcha", "kitne ka", "खर्च", "फीस", "দাম", "খরচ")) return "cost";
        if (Any(normalized, "doctor", "dr", "specialist", "physician", "surgeon", "daktar", "डॉक्टर", "डाक्टर", "ডাক্তার")
            || Regex.IsMatch(normalized, @"\b\p{L}+olog(ist|y)\b|\b\p{L}+iatrician\b|\bdentist\b|\bgynae?c?\b")) return "doctor";
        if (Any(normalized, "hospital", "clinic", "nursing home", "अस्पताल", "हॉस्पिटल", "হাসপাতাল")) return "hospital";
        if (Any(normalized, "lab", "test", "pathology", "scan", "x ray", "xray", "ultrasound", "mri", "ct scan", "जांच", "টেস্ট")) return "lab";
        return "info";
    }

    /// <summary>City names and spellings people type, mapped to the name the CMS shows.</summary>
    public static readonly IReadOnlyDictionary<string, string> Cities = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["purnea"] = "Purnea", ["purnia"] = "Purnea", ["पूर्णिया"] = "Purnea", ["পূর্ণিয়া"] = "Purnea",
        ["kishanganj"] = "Kishanganj", ["किशनगंज"] = "Kishanganj", ["কিশানগঞ্জ"] = "Kishanganj",
        ["katihar"] = "Katihar", ["कटिहार"] = "Katihar", ["araria"] = "Araria", ["अररिया"] = "Araria",
        ["siliguri"] = "Siliguri", ["শিলিগুড়ি"] = "Siliguri", ["patna"] = "Patna", ["पटना"] = "Patna",
        ["kolkata"] = "Kolkata", ["calcutta"] = "Kolkata", ["কলকাতা"] = "Kolkata", ["कोलकाता"] = "Kolkata",
        ["delhi"] = "Delhi NCR", ["दिल्ली"] = "Delhi NCR", ["noida"] = "Delhi NCR", ["gurgaon"] = "Delhi NCR", ["gurugram"] = "Delhi NCR",
        ["mumbai"] = "Mumbai", ["bombay"] = "Mumbai", ["मुंबई"] = "Mumbai",
        ["bengaluru"] = "Bengaluru", ["bangalore"] = "Bengaluru", ["hyderabad"] = "Hyderabad", ["chennai"] = "Chennai",
        ["bihar"] = "Bihar", ["बिहार"] = "Bihar", ["bengal"] = "West Bengal",
    };

    /// <summary>The city (or state) named in the phrase, using <paramref name="extra"/> names too, or null.</summary>
    public static string? City(string normalized, IEnumerable<string>? extra = null)
    {
        foreach (var w in Words(normalized))
            if (Cities.TryGetValue(w, out var c)) return c;
        if (extra != null)
            foreach (var e in extra)
            {
                var n = Normalize(e);
                if (n.Length > 2 && (" " + normalized + " ").Contains(" " + n + " ", StringComparison.Ordinal))
                    return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(n);
            }
        return null;
    }

    private static readonly string[] QuestionStarts =
    {
        "what", "why", "how", "when", "which", "who", "can", "is", "does", "should", "kya", "kaise", "kab", "kyu", "kyun", "kaun", "kitna", "kitni",
        "क्या", "कैसे", "कब", "क्यों", "कितना", "কি", "কেন", "কীভাবে", "কখন",
    };

    /// <summary>A phrase that asks something ("dengue me platelets kitna hona chahiye"), for a topic's "questions people ask".</summary>
    public static bool IsQuestion(string normalized)
    {
        var words = Words(normalized);
        return words.Length >= 3 && words.Any(w => QuestionStarts.Contains(w));
    }

    /// <summary>A readable topic name from a page address: "/specialties/urology/purnea" → "Urology, Purnea".</summary>
    public static string TopicNameFromPage(string page)
    {
        var path = page;
        if (Uri.TryCreate(page, UriKind.Absolute, out var u)) path = u.AbsolutePath;
        var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p is not ("specialties" or "conditions" or "health" or "doctors" or "solutions" or "for" or "guides" or "labs" or "hospitals"))
            .Select(p => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Uri.UnescapeDataString(p).Replace('-', ' ')))
            .ToList();
        if (parts.Count == 0) return "Home page";
        var name = string.Join(", ", parts);
        return name.Length <= 200 ? name : name[..200];
    }

    /// <summary>The path of a page address, for showing ("https://x.com/a/b?c" → "/a/b").</summary>
    public static string PathOf(string page) => Uri.TryCreate(page, UriKind.Absolute, out var u) ? (u.AbsolutePath.Length > 0 ? u.AbsolutePath : "/") : page;
}

public class KeywordActor
{
    public Guid UserId { get; set; }
    public bool CanEdit { get; set; }
    public bool CanConnect { get; set; }
}

public class KeywordWeights
{
    public int Demand { get; set; } = 7;
    public int Fit { get; set; } = 8;
    public int Close { get; set; } = 6;
    public int Value { get; set; } = 9;
}

public class KeywordSourceDto
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string What { get; set; } = string.Empty;
    public string Often { get; set; } = string.Empty;
    /// <summary>ok, warn (ran with a problem or not set up), off (not available yet).</summary>
    public string State { get; set; } = "off";
    public string? Detail { get; set; }
    public string? Error { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? LastOkAt { get; set; }
    public int LastCount { get; set; }
    public bool CanRun { get; set; }
}

public class KeywordOpportunityDto
{
    public string Kind { get; set; } = string.Empty;   // close, gap, lang, ctr, rising
    public string Site { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public Guid? KeywordId { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();
    public string Why { get; set; } = string.Empty;
    public int Score { get; set; }
}

public class KeywordLanguageDto
{
    public string Language { get; set; } = string.Empty;
    public long Impressions { get; set; }
    public long Visitors { get; set; }
    public long Searches { get; set; }
}

public class KeywordRisingDto
{
    public Guid KeywordId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Site { get; set; } = string.Empty;
    public string Sources { get; set; } = string.Empty;
    public long Now { get; set; }
    public int ChangePercent { get; set; }
}

public class KeywordOverviewDto
{
    public int Tracked { get; set; }
    public int NewThisWeek { get; set; }
    public long Impressions { get; set; }
    public long PreviousImpressions { get; set; }
    public long Clicks { get; set; }
    public decimal? Position { get; set; }
    public decimal? PreviousPosition { get; set; }
    public int TopicsWithPlan { get; set; }
    public int Published { get; set; }
    public int InProgress { get; set; }
    public List<KeywordOpportunityDto> Opportunities { get; set; } = new();
    public List<KeywordLanguageDto> Languages { get; set; } = new();
    public List<KeywordRisingDto> Rising { get; set; } = new();
    public List<KeywordSourceDto> Sources { get; set; } = new();
    public DateTime? CollectedAt { get; set; }
}

public class KeywordQuery
{
    public string? Site { get; set; }
    public string? Language { get; set; }
    public string? Intent { get; set; }
    public string? City { get; set; }
    public string? Source { get; set; }
    public string? Q { get; set; }
    public Guid? TopicId { get; set; }
    public bool Ignored { get; set; }
    /// <summary>demand (default), shown, position, rising, newest.</summary>
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class KeywordRowDto
{
    public Guid KeywordId { get; set; }
    public string Site { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public string? City { get; set; }
    public Guid? TopicId { get; set; }
    public string? TopicName { get; set; }
    public string Status { get; set; } = "active";
    public string Sources { get; set; } = string.Empty;
    public long? AvgMonthlySearches { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal? Position { get; set; }
    public long Visitors { get; set; }
    /// <summary>The last 8 weeks, oldest first: times shown plus site-search visitors.</summary>
    public List<long> Weeks { get; set; } = new();
    public DateTime FirstSeenAt { get; set; }
}

public class KeywordPage
{
    public List<KeywordRowDto> Items { get; set; } = new();
    public int Total { get; set; }
    public List<string> Cities { get; set; } = new();
}

public class KeywordSourceLine
{
    public string Name { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public class KeywordDetailDto : KeywordRowDto
{
    public string? Competition { get; set; }
    public int? CompetitionIndex { get; set; }
    public decimal? LowBid { get; set; }
    public decimal? HighBid { get; set; }
    public string Currency { get; set; } = "INR";
    public List<KeywordMonth> Months { get; set; } = new();
    public string? Page { get; set; }
    public List<KeywordSourceLine> SeenIn { get; set; } = new();
    public List<KeywordRowDto> Variants { get; set; } = new();
    public bool LanguageByPerson { get; set; }
    public bool IntentByPerson { get; set; }
}

public class KeywordUpdate
{
    public string? Language { get; set; }
    public string? Intent { get; set; }
    public string? City { get; set; }
    public bool ClearCity { get; set; }
    public Guid? TopicId { get; set; }
    public bool ClearTopic { get; set; }
    public string? Status { get; set; }
}

public class KeywordTopicRowDto
{
    public Guid TopicId { get; set; }
    public string Site { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? TargetPage { get; set; }
    public bool IsSuggested { get; set; }
    public int KeywordCount { get; set; }
    /// <summary>Monthly searches per language (Keyword Planner, else times shown in 28 days).</summary>
    public Dictionary<string, long> Demand { get; set; } = new();
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal? Position { get; set; }
    public string? Reason { get; set; }
    public string? Why { get; set; }
    public int Score { get; set; }
    public int ContentCount { get; set; }
    public int PublishedCount { get; set; }
}

public class KeywordTopicDetailDto : KeywordTopicRowDto
{
    public string? Notes { get; set; }
    public List<KeywordRowDto> Keywords { get; set; } = new();
    public List<string> Questions { get; set; } = new();
    public List<KeywordContentDto> Content { get; set; } = new();
}

public class KeywordTopicSave
{
    public string Site { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? TargetPage { get; set; }
    public string? Notes { get; set; }
    /// <summary>On create: keywords to put in the new topic.</summary>
    public List<Guid>? KeywordIds { get; set; }
}

public class KeywordBrief
{
    /// <summary>Main keyword per language: {"en":"...","hi":"..."}.</summary>
    public Dictionary<string, string> MainKeywords { get; set; } = new();
    public string? Outline { get; set; }
    public string? Page { get; set; }
    public string? Schema { get; set; }
    public string? Writer { get; set; }
    public string? Reviewer { get; set; }
    public string? Notes { get; set; }
    /// <summary>Checklist ticks by key: doctor, facts, nmc, ai, questions, links.</summary>
    public Dictionary<string, bool> Checklist { get; set; } = new();
}

public class KeywordResultDto
{
    public long ImpressionsBefore { get; set; }
    public long ImpressionsAfter { get; set; }
    public long ClicksBefore { get; set; }
    public long ClicksAfter { get; set; }
    public decimal? PositionBefore { get; set; }
    public decimal? PositionAfter { get; set; }
    public int DaysAfter { get; set; }
}

public class KeywordContentDto
{
    public Guid ContentId { get; set; }
    public string Site { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public string? TopicName { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public List<string> Languages { get; set; } = new();
    public KeywordBrief Brief { get; set; } = new();
    public Guid? OwnerUserId { get; set; }
    public string? OwnerName { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Url { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>For published content: the topic's numbers 28 days before and after publishing.</summary>
    public KeywordResultDto? Result { get; set; }
}

public class KeywordContentSave
{
    public string Site { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public string Kind { get; set; } = "page";
    public string Title { get; set; } = string.Empty;
    public string Stage { get; set; } = "idea";
    public List<string> Languages { get; set; } = new() { "en" };
    public KeywordBrief? Brief { get; set; }
    public Guid? OwnerUserId { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Url { get; set; }
}

public class KeywordGoogleAccountDto
{
    public string Site { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool SignedIn { get; set; }
    public bool HasSearchConsole { get; set; }
    public bool HasAds { get; set; }
}

public class KeywordSetupDto
{
    public bool CanEdit { get; set; }
    public Dictionary<string, List<string>> Seeds { get; set; } = new();
    public Dictionary<string, List<string>> CompetitorSites { get; set; } = new();
    public List<string> ExtraCities { get; set; } = new();
    public KeywordWeights Weights { get; set; } = new();
    public int MinVisitors { get; set; } = 5;
    public List<KeywordSourceDto> Sources { get; set; } = new();
    public List<KeywordGoogleAccountDto> GoogleAccounts { get; set; } = new();
    /// <summary>City names the CMS recognises in searches (built in plus ExtraCities).</summary>
    public List<string> Cities { get; set; } = new();
}

public class KeywordSetupSave
{
    public Dictionary<string, List<string>>? Seeds { get; set; }
    public Dictionary<string, List<string>>? CompetitorSites { get; set; }
    public List<string>? ExtraCities { get; set; }
    public KeywordWeights? Weights { get; set; }
    public int? MinVisitors { get; set; }
}
