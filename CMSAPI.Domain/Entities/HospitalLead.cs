using System;

namespace CMSAPI.Domain.Entities;

// Hospital-scoped marketing lead (see easyHMSAPI's /public/leads, RecordLeadHandler) -- same
// table, read-only from CMSAPI's side. Used here specifically for Source="1HMSDemo" rows (the
// "scan a QR, land in a live demo" flow), not the Doctor Dekho/WhatsApp leads easyHMSAPI's own
// staff-facing Lead Generation page already covers.
public class HospitalLead
{
    public Guid LeadId { get; set; }
    public Guid HospitalId { get; set; }
    public Guid? DoctorId { get; set; }

    public string Source { get; set; } = string.Empty;
    public string LeadType { get; set; } = string.Empty;

    public string? SearchQuery { get; set; }
    public string? Mobile { get; set; }
    public string? PatientName { get; set; }
    public string? SessionId { get; set; }

    public string? IpAddress { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }

    public DateTime OccurredAt { get; set; }
}
