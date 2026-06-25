namespace CMSAPI.Domain.Entities;

public class CmsUserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }

    public CmsUser? User { get; set; }
    public CmsRole? Role { get; set; }
}
