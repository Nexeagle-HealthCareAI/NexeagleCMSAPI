using System;

namespace CMSAPI.Domain.Entities;

public class DoctorDepartment
{
    public Guid DoctorDepartmentID { get; set; }
    public Guid HospitalID { get; set; }
    public Guid DoctorID { get; set; }
    public Guid DepartmentID { get; set; }
    public DateTime AssignedAt { get; set; }

    // Navigation
    public Department? Department { get; set; }
    public Doctor? Doctor { get; set; }
    public Hospital? Hospital { get; set; }
}
