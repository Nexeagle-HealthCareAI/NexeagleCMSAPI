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
public class BookingFunnelStats
{
    public int SearchCount { get; set; }
    public int ProfileViewCount { get; set; }
    // Search-to-View Rate = ProfileViewCount / SearchCount * 100
    public double SearchToViewRatePct { get; set; }

    // Booking funnel step counts — "visit" (date/time), "details" (Confirm Details), "done"
    // (Success). No separate payment step exists in this product (pay-at-hospital-counter).
    public int VisitStepCount { get; set; }
    public int DetailsStepCount { get; set; }
    public int DoneStepCount { get; set; }

    public List<SpecialtyDemandItem> SpecialtyDemand { get; set; } = new();
}

public class SpecialtyDemandItem
{
    public string SpecialtyId { get; set; } = string.Empty;
    public int SearchCount { get; set; }
    public int ProfileViewCount { get; set; }
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
