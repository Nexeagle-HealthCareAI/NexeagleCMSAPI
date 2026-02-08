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
    public DateTime CreatedAt { get; set; }

    // Navigation
    public System.Collections.Generic.List<DoctorDepartment>? DoctorDepartments { get; set; }
    public System.Collections.Generic.List<DoctorSpecialization>? DoctorSpecializations { get; set; }
    public System.Collections.Generic.List<Appointment>? Appointments { get; set; }
}
