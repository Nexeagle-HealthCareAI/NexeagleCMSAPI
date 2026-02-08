using System;

namespace CMSAPI.Domain.Entities;

public class HospitalUser
{
    public Guid HospitalUserID { get; set; }
    public Guid HospitalID { get; set; }
    public Guid UserID { get; set; }
    public bool IsPrimary { get; set; }
    public string? EmployeeID { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Hospital? Hospital { get; set; }
}
