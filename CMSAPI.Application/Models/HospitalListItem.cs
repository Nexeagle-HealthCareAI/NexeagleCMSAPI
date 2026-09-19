using System;

namespace CMSAPI.Application.Models;

public class HospitalListItem
{
    public Guid Id { get; set; }
    public string PartnerName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int TotalPatients { get; set; }
    public int TotalDoctors { get; set; }
    public int TotalNonDoctorUsers { get; set; }
    public DateTime RegisteredOn { get; set; }
    public string Status { get; set; } = "Active"; // Active | Pending

    // Soft-delete — separate concept from Status above (which is IsActive-driven onboarding
    // status). Excluded from GetHospitalsAsync by default; see includeArchived.
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    // Subscription summary — null when the hospital has no HospitalSubscription row at all
    // (shouldn't normally happen; every hospital gets a Trial row on registration).
    public string? SubscriptionPlanName { get; set; }
    public string? SubscriptionStatus { get; set; } // Trial, Active, Expired, Blocked, Rejected, Pending, PendingApproval
    // Only meaningful for an Active (paid) plan's real billing-cycle end date -- Trial has no
    // calendar expiry any more (see HospitalSubscription.GetEffectiveStatus), so this is null
    // for Trial/Blocked/Rejected.
    public int? SubscriptionDaysRemaining { get; set; }
    public bool SubscriptionIsEnterprise { get; set; }
    // Current-month free-tier usage -- only set when SubscriptionStatus == "Trial" (null for a
    // paid plan, which has no cap at all).
    public int? FreeTierUsedCount { get; set; }
    public int? FreeTierLimit { get; set; }
}
