using System.Text.Json;
using System.Text.RegularExpressions;

namespace CMSAPI.Application.Models;

// What people searched for, on Doctor Dekho and on the WhatsApp bot, sorted into the same few kinds:
// a symptom or health problem, a doctor's name, a speciality, a hospital, or other words.

public static class SearchKinds
{
    public const string Symptom = "symptom";
    public const string DoctorName = "doctor";
    public const string Specialty = "specialty";
    public const string Hospital = "hospital";
    public const string Other = "other";

    public static readonly string[] All = { Symptom, DoctorName, Specialty, Hospital, Other };

    public static string Label(string kind) => kind switch
    {
        Symptom => "Symptom or health problem",
        DoctorName => "Doctor's name",
        Specialty => "Speciality",
        Hospital => "Hospital name",
        _ => "Other words",
    };

    public const string ChannelDoctorDekho = "doctordekho";
    public const string ChannelWhatsApp = "whatsapp";

    // ── Doctor Dekho ────────────────────────────────────────────────────────────────────────────

    private static readonly Regex DoctorPrefix = new(@"^\s*dr\b\.?\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Lower-case words of at least two letters, without "dr".</summary>
    public static List<string> Words(string? text) =>
        Regex.Split(DoctorPrefix.Replace(text ?? string.Empty, string.Empty).ToLowerInvariant(), @"[^\p{L}\p{N}]+")
            .Where(w => w.Length >= 2)
            .ToList();

    /// <summary>
    /// The kind of one Doctor Dekho search. The site has recorded its own reading since
    /// searchKind was added to the event (doctor_name | symptom | keyword | specialty); older
    /// events are read from what they hold: no words and a speciality = picked from the list;
    /// the symptom AI answered = a symptom; otherwise the words are matched to the doctors,
    /// hospitals and specialities the HMS knows.
    /// </summary>
    public static string ClassifyDoctorDekho(DoctorDekhoSearchMeta meta, string? specialtyId, SearchLookup lookup)
    {
        var query = meta.Query?.Trim();
        switch (meta.SearchKind?.Trim().ToLowerInvariant())
        {
            case "doctor_name": return DoctorName;
            case "symptom": return Symptom;
            case "specialty": return Specialty;
        }
        if (string.IsNullOrEmpty(query)) return string.IsNullOrWhiteSpace(specialtyId) ? Other : Specialty;
        if (meta.AiUsed) return Symptom;
        return lookup.KindOf(query);
    }

    // ── WhatsApp ────────────────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> ModeButtons = new(StringComparer.OrdinalIgnoreCase) { "symptom", "name", "browse" };

    /// <summary>
    /// Every search in one conversation, from what the person sent at each step and what the
    /// bot's language model read in their free text (nlu_logs). A search is "found" when the bot
    /// moved them to another step before they wrote again; when it stayed on the same step it had
    /// asked them again (nothing matched, or it did not understand).
    /// </summary>
    public static List<FoundSearch> FromWhatsApp(BotJourney journey, IReadOnlyCollection<BotNluRow> nlu)
    {
        var list = new List<FoundSearch>();
        var events = journey.Events;
        var nluByText = nlu.Where(n => !string.IsNullOrWhiteSpace(n.Utterance))
            .GroupBy(n => n.Utterance!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.Direction != "in" || string.IsNullOrWhiteSpace(e.Content)) continue;
            var text = e.Content.Trim();
            var step = e.Step?.Trim().ToLowerInvariant();
            var isText = string.Equals(e.Type, "text", StringComparison.OrdinalIgnoreCase);
            nluByText.TryGetValue(text, out var read);

            string? kind = null;
            string? shown = text;
            string? specialty = read?.Specialty;
            if (step == "choosing_specialty" && !isText)
            {
                kind = Specialty; // picked from the list: the row id is the speciality itself
                specialty = text;
            }
            else if (isText && step == "awaiting_doctor_name") kind = DoctorName;
            else if (isText && (step == "awaiting_symptom" || step == "choosing_specialty_group"))
            {
                // The bot treats a name typed here as a doctor search.
                kind = !string.IsNullOrWhiteSpace(read?.DoctorName) || DoctorPrefix.IsMatch(text) ? DoctorName : Symptom;
            }
            else if (isText && read != null && step != "choosing_location" && !ModeButtons.Contains(text))
            {
                // Free text anywhere else (first message, chat with Vita, the search menu): the model's reading.
                kind = !string.IsNullOrWhiteSpace(read.DoctorName) ? DoctorName
                    : !string.IsNullOrWhiteSpace(read.Specialty) ? Specialty
                    : !string.IsNullOrWhiteSpace(read.Symptom) ? Symptom
                    : null;
            }
            else if (isText && (step is "choosing_search_mode" or "vita_chat") && !ModeButtons.Contains(text))
            {
                kind = DoctorPrefix.IsMatch(text) ? DoctorName : Other;
            }
            if (kind == null || !used.Add(kind + "|" + text)) continue;

            // Did the bot move on before the person wrote again (or before the conversation ended)?
            var found = false;
            for (var j = i + 1; j < events.Count; j++)
            {
                if (events[j].Direction == "in") break;
                if (events[j].Direction == "out") { found = true; break; }
            }
            if (kind == Specialty && specialty == text) found = true; // a speciality picked from the bot's own list

            list.Add(new FoundSearch
            {
                Kind = kind,
                Text = shown,
                Specialty = string.IsNullOrWhiteSpace(specialty) ? null : specialty.Trim(),
                At = e.At,
                Found = found,
            });
        }

        // Model readings whose message is not in the transcript (it is written from the first
        // message on, but older conversations may lack it).
        foreach (var n in nlu)
        {
            var text = n.Utterance?.Trim();
            if (string.IsNullOrEmpty(text) || list.Any(s => string.Equals(s.Text, text, StringComparison.OrdinalIgnoreCase))) continue;
            var kind = !string.IsNullOrWhiteSpace(n.DoctorName) ? DoctorName
                : !string.IsNullOrWhiteSpace(n.Specialty) ? Specialty
                : !string.IsNullOrWhiteSpace(n.Symptom) ? Symptom
                : null;
            if (kind == null || !used.Add(kind + "|" + text)) continue;
            list.Add(new FoundSearch { Kind = kind, Text = text, Specialty = n.Specialty?.Trim(), At = n.CreatedAt });
        }
        return list;
    }

    /// <summary>"cardiology", "Cardiologist (Heart)" → "Cardiology", "Cardiologist".</summary>
    public static string SpecialtyName(string raw)
    {
        var t = raw.Trim();
        var paren = t.IndexOf('(');
        if (paren > 0) t = t[..paren].Trim();
        t = t.Replace('-', ' ').Replace('_', ' ');
        return t.Length == 0 ? raw : char.ToUpperInvariant(t[0]) + t[1..];
    }

    /// <summary>What a person searched for, cut to a readable length, spaces tidied.</summary>
    public static string Tidy(string text)
    {
        var t = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return t.Length <= 60 ? t : t[..60] + "…";
    }
}

/// <summary>What a Doctor Dekho search_performed event carries in MetadataJson.</summary>
public class DoctorDekhoSearchMeta
{
    public string? Query { get; set; }
    public int? ResultsCount { get; set; }
    public bool AiUsed { get; set; }
    /// <summary>doctor_name | symptom | keyword | specialty (sent by the site since this report).</summary>
    public string? SearchKind { get; set; }
    public bool Emergency { get; set; }

    public static DoctorDekhoSearchMeta Parse(string? json)
    {
        var meta = new DoctorDekhoSearchMeta();
        if (string.IsNullOrWhiteSpace(json)) return meta;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return meta;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                switch (p.Name.ToLowerInvariant())
                {
                    case "query" when p.Value.ValueKind == JsonValueKind.String: meta.Query = p.Value.GetString(); break;
                    case "resultscount" when p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n): meta.ResultsCount = n; break;
                    case "aiused" when p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False: meta.AiUsed = p.Value.GetBoolean(); break;
                    case "searchkind" when p.Value.ValueKind == JsonValueKind.String: meta.SearchKind = p.Value.GetString(); break;
                    case "emergency" when p.Value.ValueKind == JsonValueKind.True: meta.Emergency = true; break;
                }
            }
        }
        catch (JsonException) { }
        return meta;
    }
}

/// <summary>
/// Names the HMS knows, for telling a doctor's name from a hospital or a speciality in typed words.
/// A query is a doctor's name when every word of it is in one doctor's name, and so on.
/// </summary>
public class SearchLookup
{
    private readonly List<HashSet<string>> _doctors;
    private readonly List<HashSet<string>> _hospitals;
    private readonly List<string> _specialties;

    public SearchLookup(IEnumerable<string> doctorNames, IEnumerable<string> hospitalNames, IEnumerable<string> specialtyNames)
    {
        _doctors = doctorNames.Select(n => SearchKinds.Words(n).ToHashSet()).Where(s => s.Count > 0).ToList();
        _hospitals = hospitalNames.Select(n => SearchKinds.Words(n).ToHashSet()).Where(s => s.Count > 0).ToList();
        _specialties = specialtyNames.SelectMany(n => SearchKinds.Words(n)).Where(w => w.Length >= 4 && !NotSpecialtyWords.Contains(w)).Distinct().ToList();
    }

    // Words in speciality names that say nothing about the speciality.
    private static readonly HashSet<string> NotSpecialtyWords = new() { "care", "with", "doctor", "doctors", "specialist", "clinic", "department", "general", "other", "services" };

    public static readonly SearchLookup Empty = new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    public string KindOf(string query)
    {
        var words = SearchKinds.Words(query);
        if (words.Count == 0) return SearchKinds.Other;
        var significant = words.Where(w => w.Length >= 3).ToList();
        if (significant.Count > 0 && _doctors.Any(d => significant.All(d.Contains))) return SearchKinds.DoctorName;
        if (significant.Count > 0 && _hospitals.Any(h => significant.All(h.Contains))) return SearchKinds.Hospital;
        // "cardio", "skin doctor", "orthopedic": a word that starts a speciality's word, or the other way round.
        if (words.Any(w => w.Length >= 4 && _specialties.Any(s => s.StartsWith(w) || (s.Length >= 6 && w.StartsWith(s[..6]))))) return SearchKinds.Specialty;
        return Regex.IsMatch(query, @"^\s*dr\b", RegexOptions.IgnoreCase) ? SearchKinds.DoctorName : SearchKinds.Other;
    }
}

/// <summary>One search, from either channel, as the reports need it.</summary>
public class FoundSearch
{
    public string Kind { get; set; } = SearchKinds.Other;
    public string? Text { get; set; }
    public string? Specialty { get; set; }
    public DateTime? At { get; set; }
    /// <summary>Doctor Dekho: results &gt; 0. WhatsApp: the bot moved on. Null when unknown.</summary>
    public bool? Found { get; set; }
}

// ── What the CMS screen gets ────────────────────────────────────────────────────────────────────

public class SearchKindRow
{
    public string Kind { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int DoctorDekho { get; set; }
    public int WhatsApp { get; set; }
    public int Total => DoctorDekho + WhatsApp;
    /// <summary>Searches of this kind made by someone who went on to book (same visit / conversation).</summary>
    public int LedToBooking { get; set; }
    /// <summary>Searches of this kind that found nothing (Doctor Dekho: no doctors; WhatsApp: bot asked again).</summary>
    public int FoundNothing { get; set; }
}

public class SearchTermRow
{
    public string Term { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int DoctorDekho { get; set; }
    public int WhatsApp { get; set; }
    public int Total => DoctorDekho + WhatsApp;
    public int LedToBooking { get; set; }
    public int FoundNothing { get; set; }
}

public class SearchDayRow
{
    public string Date { get; set; } = string.Empty;
    public int DoctorDekho { get; set; }
    public int WhatsApp { get; set; }
    public int Symptom { get; set; }
    public int DoctorName { get; set; }
    public int Specialty { get; set; }
    public int Hospital { get; set; }
    public int Other { get; set; }
}

public class SearchInsights
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public int Searches { get; set; }
    /// <summary>Doctor Dekho visits plus WhatsApp numbers that searched at least once.</summary>
    public int People { get; set; }
    public int LedToBooking { get; set; }
    public int FoundNothing { get; set; }
    public List<SearchKindRow> Kinds { get; set; } = new List<SearchKindRow>();
    public List<SearchDayRow> Days { get; set; } = new List<SearchDayRow>();
    /// <summary>Most searched words for each kind: up to 10 per kind.</summary>
    public List<SearchTermRow> TopTerms { get; set; } = new List<SearchTermRow>();
    /// <summary>Specialities searched for (picked, typed, or what a symptom was matched to).</summary>
    public List<SearchTermRow> Specialties { get; set; } = new List<SearchTermRow>();
    /// <summary>Most common searches that found nothing: missing doctors or specialities.</summary>
    public List<SearchTermRow> NothingFound { get; set; } = new List<SearchTermRow>();
    public bool WhatsAppAvailable { get; set; }
    public string? WhatsAppProblem { get; set; }
}

public class SearchListRow
{
    public DateTime At { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string KindLabel { get; set; } = string.Empty;
    public string? Text { get; set; }
    public string? Specialty { get; set; }
    /// <summary>Doctor Dekho: doctors shown. Null on WhatsApp.</summary>
    public int? Results { get; set; }
    public bool? Found { get; set; }
    public bool Booked { get; set; }
    public string? City { get; set; }
    /// <summary>WhatsApp: the number (masked without insights.contacts) and its conversation.</summary>
    public string? Phone { get; set; }
    public string? SessionId { get; set; }
}

public class SearchListQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    /// <summary>all | doctordekho | whatsapp</summary>
    public string? Channel { get; set; }
    /// <summary>all | symptom | doctor | specialty | hospital | other | nothing</summary>
    public string? Kind { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int Limit { get; set; } = 25;
}
