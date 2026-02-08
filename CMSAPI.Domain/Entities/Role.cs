using System;

namespace CMSAPI.Domain.Entities;

public class Role
{
    public Guid RoleID { get; set; }
    public Guid? HospitalID { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemDefined { get; set; }
    public bool IsActive { get; set; }
    public Guid? CreatedByUserID { get; set; }
    public DateTime CreatedAt { get; set; }
}
