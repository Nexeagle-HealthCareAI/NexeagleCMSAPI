using System;

namespace CMSAPI.Domain.Entities;

public class Appointment
{
    public Guid ApptId { get; set; }
    public Guid HospitalID { get; set; }
    public Guid DoctorID { get; set; }
    public string PatientID { get; set; } = string.Empty;
    public DateOnly ApptDate { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public string? Reason { get; set; }
    public string? InsuranceId { get; set; }
    public string? PaymentMode { get; set; }
    public string? AppointmentType { get; set; }
    public string CurrentStatusCode { get; set; } = string.Empty;
    public string StatusHistoryJson { get; set; } = "[]";
    public DateTime LastStatusCodeAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? PdfUrl { get; set; }
    public DateOnly? ValidUptoDate { get; set; }
    // "NEXEAGLE_PUBLIC" for patient-initiated bookings via the public site, null/"INTERNAL" for
    // everything booked by hospital staff in easyHMSWeb — the only reliable online-vs-in-hospital
    // signal on this table.
    public string? BookingSource { get; set; }

    // Navigation
    public Doctor? Doctor { get; set; }
    public Hospital? Hospital { get; set; }
    public StatusMaster? StatusMaster { get; set; }
}
