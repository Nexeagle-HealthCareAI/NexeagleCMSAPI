using System;

namespace CMSAPI.Domain.Entities;

public class PatientRegistration
{
    public Guid RegistrationId { get; set; }
    public Guid HospitalID { get; set; }
    public string PatientID { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    public Guid? RegisteredBy { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public short? AgeYears { get; set; }
    public string? Sex { get; set; }
    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? Pincode { get; set; }
    public string? InsuranceId { get; set; }

    // Navigation
    public Hospital? Hospital { get; set; }
}
