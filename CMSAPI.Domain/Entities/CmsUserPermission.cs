namespace CMSAPI.Domain.Entities;

/// <summary>Per-user override on top of role-granted permissions. Deny wins.</summary>
public class CmsUserPermission
{
    public Guid UserId { get; set; }
    public Guid PermissionId { get; set; }
    public string Effect { get; set; } = "Allow";   // 'Allow' | 'Deny'

    public CmsUser? User { get; set; }
    public CmsPermission? Permission { get; set; }
}
