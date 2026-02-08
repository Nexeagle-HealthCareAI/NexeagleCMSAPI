using System;

namespace CMSAPI.Domain.Entities;

public class DoctorSpecialization
{
    public Guid DoctorSpecializationID { get; set; }
    public Guid DoctorID { get; set; }
    public Guid SpecializationID { get; set; }

    // Navigation
    public Doctor? Doctor { get; set; }
    public Specialization? Specialization { get; set; }
}
