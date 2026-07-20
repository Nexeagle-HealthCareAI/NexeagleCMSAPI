using System;

namespace CMSAPI.Domain.Entities;

public class Doctor
{
    public Guid DoctorID { get; set; }
    public Guid HospitalID { get; set; }
    public Guid UserID { get; set; }
    public string LicenseNumber { get; set; } = string.Empty;
    public string? Qualification { get; set; }
    public int? ExperienceYears { get; set; }
    public string? MedicalCouncil { get; set; }
    public int? RegistrationYear { get; set; }
    public string? Bio { get; set; }
    public int ProfileCompletionPercent { get; set; }
    public string? ObjectURL { get; set; }
    public Guid? PrimaryDepartmentID { get; set; }
    public bool IsPubliclyListed { get; set; }
    // Public-facing contact + languages — same columns easyHMSAPI's Doctor.cs maps, added here
    // for the CMS admin's full doctor-detail view.
    public string? PublicContactEmail { get; set; }
    public string? PublicContactPhone { get; set; }
    public string? LanguagesJson { get; set; }
    // CMS-controlled Doctor Dekho marketing/moderation fields — see easyHMSAPI's Doctor.cs for
    // the full semantics (IsDelistedByAdmin is deliberately separate from IsPubliclyListed).
    public decimal? DiscountPercent { get; set; }
    public DateTime? DiscountStartAt { get; set; }
    public DateTime? DiscountEndAt { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsDelistedByAdmin { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public System.Collections.Generic.List<DoctorDepartment>? DoctorDepartments { get; set; }
    public System.Collections.Generic.List<DoctorSpecialization>? DoctorSpecializations { get; set; }
    public System.Collections.Generic.List<Appointment>? Appointments { get; set; }
}
