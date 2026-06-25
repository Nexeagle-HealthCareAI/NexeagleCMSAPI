using Microsoft.AspNetCore.Authorization;

namespace CMSAPI.Authorization;

public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}
