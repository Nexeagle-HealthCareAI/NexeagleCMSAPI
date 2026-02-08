using System;
using System.Collections.Generic;

namespace CMSAPI.Domain.Entities;

public class Hospital
{
    public Guid HospitalID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Contact { get; set; } = string.Empty;
    public string? AlternateContact { get; set; }
    public string? Website { get; set; }
    public string Location { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Pincode { get; set; } = string.Empty;
    public string? TimeZone { get; set; }
    public string RegistrationNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Guid CreatedByUserID { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public ICollection<HospitalUser> HospitalUsers { get; set; } = new List<HospitalUser>();
    public ICollection<PatientRegistration> PatientRegistrations { get; set; } = new List<PatientRegistration>();
    public HospitalProfileStatus? HospitalProfileStatus { get; set; }
    public System.Collections.Generic.List<HospitalDepartmentMapping>? HospitalDepartmentMappings { get; set; }
    public System.Collections.Generic.List<DoctorDepartment>? DoctorDepartments { get; set; }
    public System.Collections.Generic.List<Appointment>? Appointments { get; set; }
}
