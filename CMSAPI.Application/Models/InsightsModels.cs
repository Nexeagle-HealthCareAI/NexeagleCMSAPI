using System;
using System.Collections.Generic;

namespace CMSAPI.Application.Models;

// ── Site Visits ─────────────────────────────────────────────────────────────
// Aggregate-only (no raw-row pagination) — a page-view table can grow into the millions, and
// nothing in the CMS report needs individual rows, just counts sliced by region/page/day for a
// chosen date range (defaults to all-time when both bounds are omitted).
public class SiteVisitStats
{
    public int TotalVisits { get; set; }
    public int UniqueVisitors { get; set; }
    public List<RegionVisitCount> TopRegions { get; set; } = new();
    public List<PageVisitCount> TopPages { get; set; } = new();
    public List<DailyVisitCount> DailyTrend { get; set; } = new();
}

public class RegionVisitCount
{
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public int Count { get; set; }
}

public class PageVisitCount
{
    public string? PagePath { get; set; }
    public int Count { get; set; }
}

public class DailyVisitCount
{
    public string Date { get; set; } = string.Empty;
    public int Count { get; set; }
}

// ── Patient Logins ──────────────────────────────────────────────────────────
public class PatientLoginItem
{
    // Masked (e.g. "98XXXXXX10") — see InsightsRepository.MaskMobile.
    public string MobileMasked { get; set; } = string.Empty;
    public DateTime? LastLoginAt { get; set; }
    public int LoginCount { get; set; }
    public DateTime FirstSeenAt { get; set; }
}

// ── Online Appointments ─────────────────────────────────────────────────────
public class OnlineAppointmentItem
{
    public Guid ApptId { get; set; }
    public DateTime BookedAt { get; set; }
    public DateOnly ApptDate { get; set; }
    public string Status { get; set; } = string.Empty;

    public string? PatientName { get; set; }
    public string? PatientMobileMasked { get; set; }

    public string? DoctorName { get; set; }
    public string? HospitalName { get; set; }

    // True when a valid OTP-verified patient session was active at booking time (see
    // Appointments.BookedByMobile) — false means a guest booking.
    public bool IsLoggedIn { get; set; }
    public string? BookedByMobileMasked { get; set; }

    public string? IpAddress { get; set; }
    public string? ReferrerUrl { get; set; }
    public string? UtmCampaign { get; set; }
}
