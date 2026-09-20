using System;
using System.Collections.Generic;

namespace CMSAPI.Application.Models;

// Per-hospital operational activity for a date range -- IPD admissions, pathology orders,
// pharmacy sales, and online (Doctor Dekho) appointment requests. Backed by a single raw-SQL
// query against tables AppDbContext doesn't map (Admission/PathologyOrder/BillingChargeEvent
// live in the shared easyHMSDatabase catalog this context already connects to, just without
// EF entity mappings for them) -- see HospitalOperationsRepository.
public class HospitalOperationsSummaryItem
{
    public Guid HospitalId { get; set; }
    public string HospitalName { get; set; } = string.Empty;
    public int AdmissionsCount { get; set; }
    public int PathologyOrdersCount { get; set; }
    public int PharmacyInvoiceCount { get; set; }
    // Total OPD appointments (any booking source -- walk-in AND online) for the date range,
    // by ApptDate. OnlineAppointmentsCount below is the NEXEAGLE_PUBLIC-only subset of this.
    public int OpdAppointmentsCount { get; set; }
    public int OnlineAppointmentsCount { get; set; }
    // HospitalSubscriptions.Status ("Trial" or missing row = free tier, subject to the usage
    // limit; anything else, e.g. "Active", is an unlimited paid plan) -- same fallback
    // UsageLimitService.IsGatedAsync uses.
    public string SubscriptionStatus { get; set; } = "Trial";
    // Current-month free-tier usage -- only set when SubscriptionStatus == "Trial" (null for a
    // paid plan, which has no cap at all).
    public int? FreeTierUsedCount { get; set; }
    public int? FreeTierLimit { get; set; }
}

public class HospitalOperationsSummaryResponse
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public List<HospitalOperationsSummaryItem> Hospitals { get; set; } = new();
}
