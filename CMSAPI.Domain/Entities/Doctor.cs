using System;

namespace CMSAPI.Domain.Entities;

public class Doctor
{
    public Guid DoctorID { get; set; }
    public Guid? UserID { get; set; }

    // Navigation
    public System.Collections.Generic.List<DoctorDepartment>? DoctorDepartments { get; set; }
    public System.Collections.Generic.List<DoctorSpecialization>? DoctorSpecializations { get; set; }
    public Guid? PrimaryDepartmentID { get; set; }
    public System.Collections.Generic.List<Appointment>? Appointments { get; set; }
}
