using System;

namespace CMSAPI.Domain.Entities;

public class UserRole
{
    public Guid UserID { get; set; }
    public Guid RoleID { get; set; }
    public Guid? HospitalID { get; set; }
}
