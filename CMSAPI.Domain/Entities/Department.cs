using System;

namespace CMSAPI.Domain.Entities;

public class Department
{
    public Guid DepartmentID { get; set; }
    public Guid? HospitalID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? CreatedByUserID { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Hospital? Hospital { get; set; }

    // Navigation
    public System.Collections.Generic.List<Specialization>? Specializations { get; set; }
    public System.Collections.Generic.List<HospitalDepartmentMapping>? HospitalDepartmentMappings { get; set; }
    public System.Collections.Generic.List<DoctorDepartment>? DoctorDepartments { get; set; }
}
