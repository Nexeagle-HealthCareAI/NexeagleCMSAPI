namespace CMSAPI.Domain.Entities;

/// <summary>One gateable page + action, e.g. Key="subscriptions.approve".</summary>
public class CmsPermission
{
    public Guid PermissionId { get; set; }
    public string Key { get; set; } = string.Empty;       // e.g. 'subscriptions.approve'
    public string PageKey { get; set; } = string.Empty;   // e.g. 'subscriptions'
    public string Action { get; set; } = string.Empty;    // e.g. 'view','manage','approve'
    public string DisplayName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public int SortOrder { get; set; }

    public ICollection<CmsRolePermission> RolePermissions { get; set; } = new List<CmsRolePermission>();
    public ICollection<CmsUserPermission> UserPermissions { get; set; } = new List<CmsUserPermission>();
}
