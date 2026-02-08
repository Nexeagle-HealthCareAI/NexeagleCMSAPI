using System;

namespace CMSAPI.Domain.Entities;

public class Specialization
{
    public Guid SpecializationID { get; set; }
    public Guid DepartmentID { get; set; }
    public Guid? HospitalID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? CreatedByUserID { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Department? Department { get; set; }
    public Hospital? Hospital { get; set; }
    public System.Collections.Generic.List<DoctorSpecialization>? DoctorSpecializations { get; set; }
}
