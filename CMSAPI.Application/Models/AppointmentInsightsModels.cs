namespace CMSAPI.Application.Models;

// Every appointment in the HMS, by where it came from: Doctor Dekho, the WhatsApp bot, or the
// hospital's own desk. Online bookings (Doctor Dekho and WhatsApp) carry BookingSource
// NEXEAGLE_PUBLIC; the WhatsApp bot writes "WhatsApp booking — preferred …" as the reason
// (Whatsapp-Webhook app/messengers/hms_client.py book_appointment), which tells the two apart.

public static class AppointmentChannels
{
    public const string DoctorDekho = "doctordekho";
    public const string WhatsApp = "whatsapp";
    public const string HospitalDesk = "hospital";

    public const string NexeaglePublicSource = "NEXEAGLE_PUBLIC";
    /// <summary>The start of the reason the WhatsApp bot gives every booking. Keep in step with the bot.</summary>
    public const string WhatsAppReasonPrefix = "WhatsApp booking";
    public const string Cancelled = "CANCELLED";

    public static string Of(string? bookingSource, string? reason) =>
        bookingSource != NexeaglePublicSource ? HospitalDesk
        : reason != null && reason.StartsWith(WhatsAppReasonPrefix, StringComparison.OrdinalIgnoreCase) ? WhatsApp
        : DoctorDekho;

    public static string Label(string channel) => channel switch
    {
        DoctorDekho => "Doctor Dekho",
        WhatsApp => "WhatsApp bot",
        _ => "Hospital desk",
    };
}

public class AppointmentPeriodRow
{
    /// <summary>First day of the period (day, Monday of the week, or 1st of the month), yyyy-MM-dd.</summary>
    public string Start { get; set; } = string.Empty;
    public int DoctorDekho { get; set; }
    public int WhatsApp { get; set; }
    public int HospitalDesk { get; set; }
    public int Online => DoctorDekho + WhatsApp;
    public int Total => DoctorDekho + WhatsApp + HospitalDesk;
    public int Cancelled { get; set; }
    /// <summary>The period has not finished yet.</summary>
    public bool IsPartial { get; set; }
}

public class AppointmentPlaceRow
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>The doctor's hospital (doctors only).</summary>
    public string? Hospital { get; set; }
    public int DoctorDekho { get; set; }
    public int WhatsApp { get; set; }
    public int HospitalDesk { get; set; }
    public int Online => DoctorDekho + WhatsApp;
    public int Total => DoctorDekho + WhatsApp + HospitalDesk;
}

public class AppointmentOption
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Doctors: the hospital id they belong to.</summary>
    public string? HospitalId { get; set; }
}

public class AppointmentTrend
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    /// <summary>day | week | month</summary>
    public string GroupBy { get; set; } = "day";
    /// <summary>booked | appointment</summary>
    public string DateBy { get; set; } = "booked";
    public AppointmentPeriodRow Totals { get; set; } = new AppointmentPeriodRow();
    public List<AppointmentPeriodRow> Periods { get; set; } = new List<AppointmentPeriodRow>();
    /// <summary>Hospitals with the most online bookings (up to 10).</summary>
    public List<AppointmentPlaceRow> TopHospitals { get; set; } = new List<AppointmentPlaceRow>();
    /// <summary>Doctors with the most online bookings (up to 10).</summary>
    public List<AppointmentPlaceRow> TopDoctors { get; set; } = new List<AppointmentPlaceRow>();
    /// <summary>Every hospital and doctor that has an appointment, for the filters.</summary>
    public List<AppointmentOption> Hospitals { get; set; } = new List<AppointmentOption>();
    public List<AppointmentOption> Doctors { get; set; } = new List<AppointmentOption>();
}

public class AppointmentQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    /// <summary>booked (default: the day the booking was made, India time) | appointment (the appointment date)</summary>
    public string? DateBy { get; set; }
    /// <summary>day | week | month (chart only)</summary>
    public string? GroupBy { get; set; }
    /// <summary>all | online | doctordekho | whatsapp | hospital</summary>
    public string? Channel { get; set; }
    public Guid? HospitalId { get; set; }
    public Guid? DoctorId { get; set; }
    /// <summary>List only: patient, doctor or hospital name.</summary>
    public string? Search { get; set; }
    /// <summary>List only: include cancelled appointments (default yes).</summary>
    public bool IncludeCancelled { get; set; } = true;
    public int Page { get; set; } = 1;
    public int Limit { get; set; } = 25;
}

public class AppointmentRow
{
    public Guid ApptId { get; set; }
    public DateTime BookedAt { get; set; }
    public DateOnly ApptDate { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string ChannelLabel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public string? PatientName { get; set; }
    /// <summary>Masked (98XXXXXX10) unless the reader holds insights.contacts.</summary>
    public string? PatientMobile { get; set; }
    public string? DoctorName { get; set; }
    public string? HospitalName { get; set; }
    /// <summary>Doctor Dekho: the patient had a verified WhatsApp sign-in when booking.</summary>
    public bool LoggedIn { get; set; }
}
