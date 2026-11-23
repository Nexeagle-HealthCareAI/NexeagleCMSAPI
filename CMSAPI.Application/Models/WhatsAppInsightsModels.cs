using System.Text.Json;
using System.Text.RegularExpressions;

namespace CMSAPI.Application.Models;

// Insights on the WhatsApp booking bot (repo Whatsapp-Webhook). The bot keeps its own database
// (WhatsAppBookingProd / WhatsAppBookingDev, script sql/schema.sql in that repo); the CMS only reads
// it. One row of dbo.conversation_sessions is one conversation: the phone number, every message the
// person sent and every step the bot moved them to (transcript_json), and the bot's own booking row
// (pending_appointments.id) once a booking went through.

// ── Raw rows, as the bot stores them ───────────────────────────────────────────────────────────

public class BotSessionRow
{
    public string SessionId { get; set; } = string.Empty;
    /// <summary>WhatsApp number with country code, digits only (e.g. 919876543210).</summary>
    public string Phone { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public string? LastStep { get; set; }
    /// <summary>The bot's pending_appointments.id when the conversation ended in a booking.</summary>
    public Guid? BookingId { get; set; }
    public string? TranscriptJson { get; set; }
}

public class BotNluRow
{
    public string SessionId { get; set; } = string.Empty;
    public string? Utterance { get; set; }
    public string? Specialty { get; set; }
    public string? Symptom { get; set; }
    public string? DoctorName { get; set; }
    /// <summary>The step the person was on when they wrote it.</summary>
    public string? RoutedStep { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class BotBookingRow
{
    public Guid Id { get; set; }
    public string? DoctorName { get; set; }
    public string? DoctorId { get; set; }
    public string? HospitalId { get; set; }
    public DateTime? PreferredDate { get; set; }
    public string? PatientName { get; set; }
    /// <summary>"self", or who the person booked for (family member).</summary>
    public string? BookingFor { get; set; }
    public string? Status { get; set; }
}

public class BotLastSearchRow
{
    public string Phone { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? LocationText { get; set; }
}

// ── What a conversation was: worked out from its transcript ────────────────────────────────────

public class BotTranscriptEvent
{
    /// <summary>"in" = the person sent something; "out" = the bot moved them to a step.</summary>
    public string Direction { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Content { get; set; }
    public string? Step { get; set; }
    public DateTime? At { get; set; }
}

/// <summary>How a conversation started, how far it got and how it ended. Pure: no database.</summary>
public class BotJourney
{
    /// <summary>Direct | HospitalQr | DoctorQr | CheckinQr | DocumentQr</summary>
    public string Source { get; set; } = WhatsAppJourneys.SourceDirect;
    /// <summary>Hospital name from a hospital QR, the hospital code from a check-in QR.</summary>
    public string? SourceName { get; set; }
    /// <summary>The code or id the QR carried (hospital code, doctor id).</summary>
    public string? SourceCode { get; set; }
    /// <summary>Booked | Left | Other</summary>
    public string Outcome { get; set; } = WhatsAppJourneys.OutcomeLeft;
    /// <summary>0 start … 5 final confirmation, 6 booked; -1 when the conversation was not about booking.</summary>
    public int Stage { get; set; }
    public string StageLabel { get; set; } = string.Empty;
    /// <summary>What the person typed when asked for a symptom or a doctor's name.</summary>
    public List<string> TypedSearches { get; set; } = new List<string>();
    public List<BotTranscriptEvent> Events { get; set; } = new List<BotTranscriptEvent>();
}

public static class WhatsAppJourneys
{
    public const string SourceDirect = "Direct";
    public const string SourceHospitalQr = "HospitalQr";
    public const string SourceDoctorQr = "DoctorQr";
    public const string SourceCheckinQr = "CheckinQr";
    public const string SourceDocumentQr = "DocumentQr";

    public const string OutcomeBooked = "Booked";
    public const string OutcomeLeft = "Left";
    public const string OutcomeOther = "Other";

    public const int StageBooked = 6;

    // The messages the bot's QR codes pre-fill (Whatsapp-Webhook app/conversation/checkin.py and
    // app/front_door/qr_handlers.py). Keep in step with those patterns.
    private static readonly Regex HospitalQr = new(@"qr id of this hospital is\s+(\S+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HospitalQrName = new(@"scanned the qr code for\s+(.+?)!", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DoctorQr = new(@"^drbook\s+(\S+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CheckinQr = new(@"^checkin\s+(\S+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DocumentQr = new(@"^(discharge|rx|rxv)\s+(\S+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // The bot's booking steps (STEP_REGISTRY in app/conversation/__init__.py), grouped into the
    // stage a person had reached. A step not listed here is not part of booking.
    private static readonly Dictionary<string, int> BookingStages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["choosing_language"] = 0,
        ["confirming_language"] = 0,
        ["choosing_hospital_action"] = 0,
        ["choosing_location"] = 1,
        ["choosing_search_mode"] = 1,
        ["vita_chat"] = 1,
        ["awaiting_symptom"] = 1,
        ["awaiting_doctor_name"] = 1,
        ["choosing_specialty_group"] = 1,
        ["choosing_specialty"] = 1,
        ["choosing_sort"] = 1,
        ["confirming_last_search"] = 1,
        ["choosing_hospital_from_search"] = 1,
        ["choosing_doctor"] = 2,
        ["choosing_slot"] = 3,
        ["awaiting_patient_details"] = 4,
        ["confirming"] = 5,
        ["confirming_duplicate_booking"] = 5,
    };

    // Steps of everything else the bot does: an existing appointment, check-in, a follow-up.
    private static readonly HashSet<string> OtherSteps = new(StringComparer.OrdinalIgnoreCase)
    {
        "choosing_appointment_to_cancel", "choosing_appointment_to_reschedule", "choosing_appointment_to_view",
        "confirming_appointment_cancel", "confirming_appointment_reschedule", "viewing_appointment_status",
        "awaiting_reschedule_date", "checkin_awaiting_location", "checkin_choosing_appointment",
        "awaiting_followup_reply", "confirming_followup_booking",
    };

    private static readonly string[] StageLabels =
    {
        "At the start (language)",
        "Searching for a doctor",
        "Looking at the doctors found",
        "Choosing a time",
        "Giving patient details",
        "At the final confirmation",
        "Booked",
    };

    public static string LabelOfStage(int stage) =>
        stage >= 0 && stage < StageLabels.Length ? StageLabels[stage] : "Checked or changed an appointment";

    /// <summary>A readable name for one of the bot's steps.</summary>
    public static string LabelOfStep(string? step)
    {
        if (string.IsNullOrWhiteSpace(step)) return string.Empty;
        return step.Trim().ToLowerInvariant() switch
        {
            "choosing_language" or "confirming_language" => "Language",
            "choosing_hospital_action" => "Hospital menu (after a QR scan)",
            "choosing_location" => "Location",
            "choosing_search_mode" => "How to search",
            "vita_chat" => "Chat with Vita",
            "awaiting_symptom" => "Typing a symptom",
            "awaiting_doctor_name" => "Typing a doctor's name",
            "choosing_specialty_group" or "choosing_specialty" => "Choosing a speciality",
            "choosing_sort" => "Sorting results",
            "confirming_last_search" => "Repeat last search?",
            "choosing_hospital_from_search" => "Choosing a hospital",
            "choosing_doctor" => "Choosing a doctor",
            "choosing_slot" => "Choosing a time",
            "awaiting_patient_details" => "Patient details",
            "confirming" or "confirming_duplicate_booking" => "Final confirmation",
            "viewing_appointment_status" or "choosing_appointment_to_view" => "Appointment status",
            "choosing_appointment_to_cancel" or "confirming_appointment_cancel" => "Cancelling",
            "choosing_appointment_to_reschedule" or "confirming_appointment_reschedule" or "awaiting_reschedule_date" => "Rescheduling",
            "checkin_awaiting_location" or "checkin_choosing_appointment" => "Check-in",
            "awaiting_followup_reply" or "confirming_followup_booking" => "Follow-up",
            var other => other.Replace('_', ' '),
        };
    }

    /// <summary>The transcript as the bot wrote it: a JSON array of {direction,type,content,step,at}.</summary>
    public static List<BotTranscriptEvent> ParseTranscript(string? json)
    {
        var list = new List<BotTranscriptEvent>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                DateTime? at = null;
                if (e.TryGetProperty("at", out var atEl) && atEl.ValueKind == JsonValueKind.Number && atEl.TryGetDouble(out var seconds))
                {
                    at = DateTime.UnixEpoch.AddSeconds(seconds);
                }
                list.Add(new BotTranscriptEvent
                {
                    Direction = Text(e, "direction") ?? string.Empty,
                    Type = Text(e, "type"),
                    Content = Text(e, "content"),
                    Step = Text(e, "step"),
                    At = at,
                });
            }
        }
        catch (JsonException)
        {
            // A broken transcript still counts as a conversation; it just has no detail.
        }
        return list;
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static BotJourney Classify(BotSessionRow row)
    {
        var events = ParseTranscript(row.TranscriptJson);
        var journey = new BotJourney { Events = events };

        // Where they came from: the first QR message in the conversation, if any.
        foreach (var e in events.Where(e => e.Direction == "in" && !string.IsNullOrWhiteSpace(e.Content)))
        {
            var text = e.Content!.Trim();
            Match m;
            if ((m = HospitalQr.Match(text)).Success)
            {
                journey.Source = SourceHospitalQr;
                journey.SourceCode = m.Groups[1].Value;
                var name = HospitalQrName.Match(text);
                journey.SourceName = name.Success ? name.Groups[1].Value.Trim() : null;
                break;
            }
            if ((m = DoctorQr.Match(text)).Success) { journey.Source = SourceDoctorQr; journey.SourceCode = m.Groups[1].Value; break; }
            if ((m = CheckinQr.Match(text)).Success) { journey.Source = SourceCheckinQr; journey.SourceCode = m.Groups[1].Value; break; }
            if ((m = DocumentQr.Match(text)).Success) { journey.Source = SourceDocumentQr; journey.SourceName = m.Groups[1].Value.ToUpperInvariant(); break; }
        }

        // Every step the conversation touched: where the bot moved them, and where they were when they wrote.
        var steps = events.Select(e => e.Direction == "out" ? (e.Step ?? e.Content) : e.Step)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .ToList();
        if (!string.IsNullOrWhiteSpace(row.LastStep)) steps.Add(row.LastStep.Trim());

        var furthest = steps.Select(s => BookingStages.TryGetValue(s, out var stage) ? stage : -1).DefaultIfEmpty(-1).Max();
        var touchedOther = steps.Any(s => OtherSteps.Contains(s));

        if (row.BookingId.HasValue)
        {
            journey.Outcome = OutcomeBooked;
            journey.Stage = StageBooked;
        }
        else if (furthest >= 1 || (!touchedOther && journey.Source is not (SourceCheckinQr or SourceDocumentQr)))
        {
            // Got into booking, or never got past hello, and did not book.
            journey.Outcome = OutcomeLeft;
            journey.Stage = Math.Max(furthest, 0);
        }
        else
        {
            // Came for something else: an existing appointment, check-in, a document, a follow-up.
            journey.Outcome = OutcomeOther;
            journey.Stage = -1;
        }
        journey.StageLabel = LabelOfStage(journey.Stage);

        // What they typed when asked for a symptom or a doctor.
        journey.TypedSearches = events
            .Where(e => e.Direction == "in" && string.Equals(e.Type, "text", StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(e.Step, "awaiting_symptom", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(e.Step, "awaiting_doctor_name", StringComparison.OrdinalIgnoreCase))
                        && !string.IsNullOrWhiteSpace(e.Content))
            .Select(e => e.Content!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return journey;
    }

    public static bool IsQr(string source) => source != SourceDirect;

    /// <summary>"+91 98765 43210".</summary>
    public static string FormatPhone(string phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("91")) return $"+91 {digits.Substring(2, 5)} {digits.Substring(7)}";
        return digits.Length > 0 ? "+" + digits : string.Empty;
    }

    /// <summary>"+91 98XXXXXX10": the first two and last two digits of the number itself.</summary>
    public static string MaskPhone(string phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        var country = digits.Length == 12 && digits.StartsWith("91") ? "+91 " : "+";
        var local = digits.Length == 12 && digits.StartsWith("91") ? digits.Substring(2) : digits;
        if (local.Length <= 4) return country + new string('X', local.Length);
        return country + local[..2] + new string('X', local.Length - 4) + local[^2..];
    }

    /// <summary>The Monday of the India-time week a moment falls in.</summary>
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}

// ── What the CMS screens get ───────────────────────────────────────────────────────────────────

public class WhatsAppDailyRow
{
    public string Date { get; set; } = string.Empty;
    public int Conversations { get; set; }
    /// <summary>Different phone numbers that wrote that day.</summary>
    public int People { get; set; }
    /// <summary>Of those, numbers that wrote to the bot for the first time ever.</summary>
    public int NewPeople { get; set; }
    public int FromQr { get; set; }
    public int Booked { get; set; }
    /// <summary>Started booking and stopped before it went through.</summary>
    public int Left { get; set; }
    /// <summary>Came for an existing appointment, check-in, a document or a follow-up.</summary>
    public int Other { get; set; }
}

public class NamedCount
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class WhatsAppDailyStats
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public WhatsAppDailyRow Totals { get; set; } = new WhatsAppDailyRow();
    public List<WhatsAppDailyRow> Days { get; set; } = new List<WhatsAppDailyRow>();
    /// <summary>Where people who left stopped, most common first.</summary>
    public List<NamedCount> LeftAt { get; set; } = new List<NamedCount>();
    /// <summary>Specialities, symptoms and doctors searched, most common first.</summary>
    public List<NamedCount> TopSearches { get; set; } = new List<NamedCount>();
    /// <summary>Which QR codes brought people: hospital names, doctors, check-in.</summary>
    public List<NamedCount> QrSources { get; set; } = new List<NamedCount>();
}

public class WhatsAppPersonRow
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    /// <summary>Full number for people with insights.contacts, masked for everyone else.</summary>
    public string Phone { get; set; } = string.Empty;
    /// <summary>Digits for a wa.me / tel: link; empty without insights.contacts.</summary>
    public string PhoneDigits { get; set; } = string.Empty;
    public bool IsNewPerson { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public int Stage { get; set; }
    public string StageLabel { get; set; } = string.Empty;
    public string LastStepLabel { get; set; } = string.Empty;
    public List<string> Searched { get; set; } = new List<string>();
    public string? City { get; set; }
    public string? BookedDoctor { get; set; }
    public string? BookedHospital { get; set; }
    public string? BookedFor { get; set; }
    public string? PatientName { get; set; }
    public string? AppointmentDate { get; set; }
    public int Messages { get; set; }
}

public class WhatsAppPeopleQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    /// <summary>all | booked | left | other</summary>
    public string? Outcome { get; set; }
    /// <summary>all | qr | direct</summary>
    public string? Source { get; set; }
    /// <summary>Digits of a number, a name, a doctor, a search.</summary>
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int Limit { get; set; } = 25;
}

public class WhatsAppTranscript
{
    public string SessionId { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public List<WhatsAppTranscriptLine> Lines { get; set; } = new List<WhatsAppTranscriptLine>();
}

public class WhatsAppTranscriptLine
{
    /// <summary>"person" = what they sent; "bot" = the step the bot moved them to.</summary>
    public string From { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? StepLabel { get; set; }
    public DateTime? At { get; set; }
}

// ── Weekly growth and Doctor Dekho by day ───────────────────────────────────────────────────────

public class WeeklyGrowthRow
{
    /// <summary>Monday of the India-time week, yyyy-MM-dd.</summary>
    public string WeekStart { get; set; } = string.Empty;
    /// <summary>Different phone numbers that wrote to the WhatsApp bot. Null when it could not be read.</summary>
    public int? WhatsAppPeople { get; set; }
    /// <summary>Visits to nexeagle.com (browser sessions, people only).</summary>
    public int NexeagleVisits { get; set; }
    /// <summary>Visits to the Doctor Dekho website.</summary>
    public int DoctorDekhoVisits { get; set; }
    /// <summary>True for the week that is still running.</summary>
    public bool IsPartial { get; set; }
}

public class WeeklyGrowth
{
    public List<WeeklyGrowthRow> Weeks { get; set; } = new List<WeeklyGrowthRow>();
    public bool WhatsAppAvailable { get; set; }
    public string? WhatsAppProblem { get; set; }
}

public class DoctorDekhoDailyRow
{
    public string Date { get; set; } = string.Empty;
    public int Visits { get; set; }
    public int Searched { get; set; }
    public int ViewedDoctor { get; set; }
    public int StartedBooking { get; set; }
    public int EnteredDetails { get; set; }
    public int Booked { get; set; }
    /// <summary>Started booking and did not finish.</summary>
    public int Left { get; set; }
}

public class DoctorDekhoDailyStats
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public DoctorDekhoDailyRow Totals { get; set; } = new DoctorDekhoDailyRow();
    public List<DoctorDekhoDailyRow> Days { get; set; } = new List<DoctorDekhoDailyRow>();
}
