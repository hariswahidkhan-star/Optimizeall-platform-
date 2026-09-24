using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Common.Security;

/// <summary>
/// Endpoint filter for "any of these permissions" (stacked <c>[HasPermission]</c> attributes mean all of them). Runs with the
/// authorization filters, i.e. before model validation, so a caller without access always gets 403 (never a 400 that
/// reveals the request shape). Anonymous callers are left to the authentication challenge (401).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireAnyPermissionAttribute(params string[] permissions) : Attribute, Microsoft.AspNetCore.Mvc.Filters.IAuthorizationFilter
{
    public IReadOnlyList<string> Permissions { get; } = permissions;

    public void OnAuthorization(Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext context)
    {
        var principal = context.HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true) return;
        // Effective permissions (built-in + custom roles).
        var granted = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>().Permissions;
        if (!Permissions.Any(granted.Contains))
            throw DomainException.Forbidden("auth.forbidden", "You do not have permission to perform this action.");
    }
}
