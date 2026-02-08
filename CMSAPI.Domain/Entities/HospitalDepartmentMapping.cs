using System;

namespace CMSAPI.Domain.Entities;

public class HospitalDepartmentMapping
{
    public Guid MappingID { get; set; }
    public Guid HospitalID { get; set; }
    public Guid DepartmentID { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime MappedAt { get; set; }

    // Navigation
    public Hospital? Hospital { get; set; }
    public Department? Department { get; set; }
}
