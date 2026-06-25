namespace CMSAPI.Domain.Entities;

/// <summary>A named bundle of permissions assignable to CMS users.</summary>
public class CmsRole
{
    public Guid RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemDefined { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public ICollection<CmsUserRole> UserRoles { get; set; } = new List<CmsUserRole>();
    public ICollection<CmsRolePermission> RolePermissions { get; set; } = new List<CmsRolePermission>();
}
