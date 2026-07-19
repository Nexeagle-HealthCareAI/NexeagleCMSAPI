using System;
using System.Collections.Generic;

namespace CMSAPI.Application.Models;

public class HospitalDetails
{
    public Guid Id { get; set; }
    public string PartnerName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string HospitalType { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int TotalPatients { get; set; }
    public DateTime RegisteredOn { get; set; }
    public string Status { get; set; } = "Active";

    // Subscription — same summary as the list, plus the full timeline and payment ledger for
    // this specific hospital.
    public string? SubscriptionPlanName { get; set; }
    public string? SubscriptionStatus { get; set; }
    public int? SubscriptionDaysRemaining { get; set; }
    public bool SubscriptionIsEnterprise { get; set; }
    public DateTime? TrialStartDate { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public DateTime? SubscriptionStartDate { get; set; }
    public DateTime? SubscriptionEndDate { get; set; }
    public List<HospitalPaymentHistoryItem> PaymentHistory { get; set; } = new();

    public List<HospitalUserInfo> Users { get; set; } = new();
    public List<DoctorInfo> Doctors { get; set; } = new();
    public HospitalStats Stats { get; set; } = new();
}

public class HospitalPaymentHistoryItem
{
    public string PlanName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string? PaymentMode { get; set; }
    public string Status { get; set; } = string.Empty; // PendingApproval, Approved, Rejected
    public DateTime SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
}

public class HospitalUserInfo
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime? LastLoginTime { get; set; }
    public string LoginMethod { get; set; } = string.Empty;
}

public class DoctorInfo
{
    public string Name { get; set; } = string.Empty;
    public List<string> Departments { get; set; } = new();
    public string Speciality { get; set; } = string.Empty;
    public string Degree { get; set; } = string.Empty;
    public string RegistrationNumber { get; set; } = string.Empty;
    public DateTime RegisteredOn { get; set; }
    public AppointmentCounts Appointments { get; set; } = new();
    public AppointmentCounts UniquePatients { get; set; } = new();
}

public class AppointmentCounts
{
    public int Daily { get; set; }
    public int Weekly { get; set; }
    public int Monthly { get; set; }
    public int Yearly { get; set; }
}

public class HospitalStats
{
    public StatDetails UniquePatients { get; set; } = new();
    public StatDetails Appointments { get; set; } = new();
}

public class StatDetails
{
    public List<StatPoint> Daily { get; set; } = new();
    public List<StatPoint> Weekly { get; set; } = new();
    public List<StatPoint> Monthly { get; set; } = new();
    public List<StatPoint> Yearly { get; set; } = new();
}

public class StatPoint
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}
