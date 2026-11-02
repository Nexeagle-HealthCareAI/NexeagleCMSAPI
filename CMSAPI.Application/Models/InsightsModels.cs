using System;
using System.Collections.Generic;

namespace CMSAPI.Application.Models;

// ── Site Visits ─────────────────────────────────────────────────────────────
// People only: page views flagged IsAutomated (crawlers, link-preview renderers, data-centre IPs)
// are left out and reported separately. A "visit" is one browser session: page views from the same
// browser with no gap of 30 minutes or more (DoctorDekhoWeb src/lib/analytics.ts). Every count below
// is in visits unless the name says page views. Days are India Standard Time days.
public class SiteVisitStats
{
    /// <summary>"doctordekho" | "nexeagle" | null (both sites).</summary>
    public string? Site { get; set; }
    public string From { get; set; } = string.Empty;   // IST dates actually applied, yyyy-MM-dd
    public string To { get; set; } = string.Empty;

    public int Visits { get; set; }
    public int PageViews { get; set; }
    public int AutomatedVisitsExcluded { get; set; }
    public int AutomatedPageViewsExcluded { get; set; }

    // Visitor location (from the IP address; country and state are reliable, city is approximate
    // because Indian mobile networks route through a few large hubs).
    public int VisitsInIndia { get; set; }
    public int VisitsOutsideIndia { get; set; }
    public int VisitsLocationUnknown { get; set; }
    public List<LocationCount> IndiaStates { get; set; } = new();
    public List<LocationCount> IndiaCities { get; set; } = new();
    public List<LocationCount> Countries { get; set; } = new();

    // Where people look for and book care: the city of the hospital whose doctor or page they opened
    // (HospitalLeads) and of the hospital they booked with (online appointments). Exact, not IP-based.
    public List<LocationCount> CareInterestCities { get; set; } = new();
    public List<LocationCount> BookingCities { get; set; } = new();

    public List<PageVisitCount> TopPages { get; set; } = new();
    public List<DailyVisitCount> DailyTrend { get; set; } = new();
    public List<LocationCount> Sites { get; set; } = new();

    // Pre-2026-10 shape, kept so an older CMS Web build keeps rendering until it is redeployed.
    public int TotalVisits { get; set; }
    public int UniqueVisitors { get; set; }
    public List<RegionVisitCount> TopRegions { get; set; } = new();
}

public class LocationCount
{
    public string Name { get; set; } = string.Empty;
    /// <summary>State for a city row; null otherwise.</summary>
    public string? Parent { get; set; }
    public int Visits { get; set; }
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
    /// <summary>Page views (kept as Count for older clients).</summary>
    public int Count { get; set; }
    public int Visits { get; set; }
}

public class DailyVisitCount
{
    public string Date { get; set; } = string.Empty;   // IST day
    /// <summary>Visits that started that day.</summary>
    public int Count { get; set; }
    public int PageViews { get; set; }
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
    /// <summary>Display name from StatusMaster (falls back to the code).</summary>
    public string Status { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;

    public string? PatientName { get; set; }
    public string? PatientMobileMasked { get; set; }

    public string? DoctorName { get; set; }
    public string? HospitalName { get; set; }

    // True when a valid OTP-verified patient session was active at booking time (see
    // Appointments.BookedByMobile) — false means a guest booking.
    public bool IsLoggedIn { get; set; }
    public string? BookedByMobileMasked { get; set; }

    // No IpAddress here on purpose: the CMS never displays it, and every GET is cached in the admin's
    // browser, so returning a visitor's raw IP only spread it further.
    public string? ReferrerUrl { get; set; }
    public string? UtmCampaign { get; set; }
}
