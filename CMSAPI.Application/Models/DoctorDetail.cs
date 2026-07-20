using System;
using System.Collections.Generic;

namespace CMSAPI.Application.Models;

// Full "A to Z" doctor profile for the CMS admin detail view — a superset of DoctorListItem,
// including every hospital a doctor is affiliated with (not just the one deterministically picked
// for the list row) since a doctor is a platform-wide identity that can work at more than one.
public class DoctorDetail
{
    public Guid DoctorId { get; set; }
    public Guid UserId { get; set; }
    public string? FullName { get; set; }
    public string? MobileNumber { get; set; }
    public string? Email { get; set; }
    public string? PhotoUrl { get; set; }

    public string LicenseNumber { get; set; } = string.Empty;
    public string? MedicalCouncil { get; set; }
    public int? RegistrationYear { get; set; }
    public string? Qualification { get; set; }
    public int? ExperienceYears { get; set; }
    public string? Bio { get; set; }
    public int ProfileCompletionPercent { get; set; }

    public List<string> Specializations { get; set; } = new();
    public List<string> Languages { get; set; } = new();
    public string? PublicContactEmail { get; set; }
    public string? PublicContactPhone { get; set; }

    public bool IsPubliclyListed { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsDelistedByAdmin { get; set; }
    public decimal? DiscountPercent { get; set; }
    public DateTime? DiscountStartAt { get; set; }
    public DateTime? DiscountEndAt { get; set; }

    public DateTime CreatedAt { get; set; }
    // From UserAuth.LastLoginTime (UTC) — null if the doctor has never logged in.
    public DateTime? LastLoginTime { get; set; }

    public List<DoctorHospitalAffiliation> Hospitals { get; set; } = new();
}

public class DoctorHospitalAffiliation
{
    public Guid HospitalId { get; set; }
    public string? HospitalName { get; set; }
    public string? HospitalAddress { get; set; }
    public string? DepartmentName { get; set; }
    // Same dbo.DoctorFees rows easyHMSWeb's Configuration > Doctor Fees edits, hospital-scoped.
    public decimal? OpdConsultFee { get; set; }
    public decimal? IpdVisitFee { get; set; }
    public decimal? EmergencyFee { get; set; }
}
