using System;
using System.Collections.Generic;

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

public class DemoLocationCount
{
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public int Count { get; set; }
}

// Summary strip for the Marketing tab -- UniqueVisitors counts distinct SessionId (a fresh
// sessionStorage-scoped UUID per browser tab, see easyHMSWeb's demoLeadApi.ts), same convention
// InsightsRepository already uses for site-visit "unique visitors". Location is IP-based
// (city/region/country resolved server-side via IGeoIpLookupService), not device GPS.
public class DemoLoginStats
{
    public int TotalLogins { get; set; }
    public int UniqueVisitors { get; set; }
    public List<DemoLocationCount> TopLocations { get; set; } = new();
}
