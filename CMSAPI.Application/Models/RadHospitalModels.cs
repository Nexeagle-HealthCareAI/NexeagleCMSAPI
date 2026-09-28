using System;
using System.Collections.Generic;

namespace CMSAPI.Application.Models;

// Mirrors 1RadAPI's AdminStaffDto/AdminHospitalDto (Features/Hospitals/Queries/GetAllHospitalsForAdmin)
// -- shapes are kept in sync by hand since these are two separate services over HTTP, not a shared DB.
public class RadStaffItem
{
    public Guid StaffId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public string? Designation { get; set; }
    public string? Specialization { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class RadHospitalItem
{
    public Guid HospitalId { get; set; }
    public string HospitalName { get; set; } = string.Empty;
    public string HospitalAddress { get; set; } = string.Empty;
    public string? GSTIN { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? NABHNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string SubscriptionStatus { get; set; } = string.Empty;
    public string BillingCycle { get; set; } = string.Empty;
    public string Modules { get; set; } = string.Empty;
    public List<RadStaffItem> Staff { get; set; } = new();
}
