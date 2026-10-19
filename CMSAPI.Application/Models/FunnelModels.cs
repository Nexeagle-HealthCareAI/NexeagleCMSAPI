using System;
using System.Collections.Generic;

namespace CMSAPI.Application.Models;

// ── Auth Funnel (WhatsApp Login) ────────────────────────────────────────────
// Grouped by SessionId — one browser session = one login journey (open form -> send OTP ->
// verify), which is what makes Time-to-Authenticate and per-attempt outcome meaningful.
public class AuthFunnelStats
{
    public int TotalVisitorSessions { get; set; }
    public int LoginInitiatedSessions { get; set; }
    public int OtpSentSessions { get; set; }
    public int OtpVerifiedSessions { get; set; }

    // Login Initiation Rate = LoginInitiatedSessions / TotalVisitorSessions * 100
    public double LoginInitiationRatePct { get; set; }
    // OTP / Auth Completion Rate = OtpVerifiedSessions / OtpSentSessions * 100
    public double AuthCompletionRatePct { get; set; }
    public double? AvgTimeToAuthenticateSeconds { get; set; }
}

public class AuthFunnelAttemptItem
{
    public string MobileMasked { get; set; } = string.Empty;
    public DateTime? OtpSentAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    // "Verified" | "Bounced" (OTP sent, never verified) | "Abandoned" (opened the form, never sent an OTP)
    public string Outcome { get; set; } = string.Empty;
    public double? TimeToAuthenticateSeconds { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
}

// ── Booking & Search Funnel ─────────────────────────────────────────────────
// People only (bot traffic excluded) and de-duplicated: a search is one distinct query per visit, a
// profile view is one doctor per visit, and a booking step is one doctor per visit, however many
// times the page re-rendered or the visitor went back and forth. Days are India time.
public class BookingFunnelStats
{
    /// <summary>Distinct searches (visit + query + specialty).</summary>
    public int SearchCount { get; set; }
    /// <summary>Visits that searched at least once.</summary>
    public int SearchSessionCount { get; set; }
    /// <summary>Distinct doctor profiles opened (visit + doctor).</summary>
    public int ProfileViewCount { get; set; }
    /// <summary>% of searching visits that went on to open a doctor profile.</summary>
    public double SearchToViewRatePct { get; set; }

    // Booking widget steps, per visit + doctor. "visit" = the booking form was shown (it is on every
    // profile page), "details" = date and time chosen, moved on to patient details, "done" = booked.
    public int VisitStepCount { get; set; }
    public int DetailsStepCount { get; set; }
    public int DoneStepCount { get; set; }

    /// <summary>Doctor Dekho appointments actually saved in the database for the same days (ground truth;
    /// DoneStepCount can be lower when ad blockers stop the tracking call).</summary>
    public int BookingsRecorded { get; set; }

    public List<SpecialtyDemandItem> SpecialtyDemand { get; set; } = new();
}

public class SpecialtyDemandItem
{
    public string SpecialtyId { get; set; } = string.Empty;
    /// <summary>Visits that searched this specialty.</summary>
    public int SearchCount { get; set; }
    /// <summary>Distinct profiles of this specialty opened (visit + doctor).</summary>
    public int ProfileViewCount { get; set; }
    /// <summary>Bookings completed in the booking widget for this specialty.</summary>
    public int CompletedBookingCount { get; set; }
}

// ── All Searches (raw log) ──────────────────────────────────────────────────
public class SearchLogItem
{
    public DateTime OccurredAt { get; set; }
    public string? Query { get; set; }
    public string? SpecialtyId { get; set; }
    public int? ResultsCount { get; set; }
    public bool AiUsed { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
}
