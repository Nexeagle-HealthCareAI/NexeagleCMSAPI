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
    public decimal PharmacyRevenue { get; set; }
    public int OnlineAppointmentsCount { get; set; }
}

public class HospitalOperationsSummaryResponse
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public List<HospitalOperationsSummaryItem> Hospitals { get; set; } = new();
}
