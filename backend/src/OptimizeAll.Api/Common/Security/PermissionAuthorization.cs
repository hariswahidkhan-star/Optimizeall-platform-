using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace OptimizeAll.Api.Common.Security;

/// <summary>Requires the caller to hold a permission. Usage: <c>[HasPermission(Permissions.PayoutsFinalize)]</c>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public HasPermissionAttribute(string permission) : base(PolicyPrefix + permission)
    {
    }
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>Permissions are resolved from the role claims on every request so role changes apply immediately on refresh.</summary>
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            RolePermissions.For(ClaimsHelper.GetRoles(context.User)).Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[HasPermissionAttribute.PolicyPrefix.Length..]))
                .Build();
        }
        return await base.GetPolicyAsync(policyName);
    }
}
