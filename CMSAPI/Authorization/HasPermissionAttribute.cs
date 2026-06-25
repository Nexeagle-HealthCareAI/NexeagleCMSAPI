using Microsoft.AspNetCore.Authorization;

namespace CMSAPI.Authorization;

/// <summary>
/// Requires the authenticated user to carry a specific permission, e.g.
/// [HasPermission("subscriptions.approve")]. Resolved by PermissionPolicyProvider.
/// </summary>
public class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission)
        : base($"{PermissionConstants.PolicyPrefix}{permission}")
    {
    }
}
