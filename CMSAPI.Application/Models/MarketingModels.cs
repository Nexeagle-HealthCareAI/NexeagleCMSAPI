using System;

namespace CMSAPI.Application.Models;

// One row for the Marketing tab's "Demo Logins" table -- HospitalLeads rows with
// Source="1HMSDemo" (see easyHMSAPI's RecordLeadHandler / the 1hms-dev.nexeagle.com QR flow).
public class DemoLoginLeadItem
{
    public Guid LeadId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? PatientName { get; set; }
    public string? Mobile { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
}
