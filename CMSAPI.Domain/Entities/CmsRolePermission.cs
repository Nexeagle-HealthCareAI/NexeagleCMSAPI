namespace CMSAPI.Domain.Entities;

public class CmsRolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }

    public CmsRole? Role { get; set; }
    public CmsPermission? Permission { get; set; }
}
