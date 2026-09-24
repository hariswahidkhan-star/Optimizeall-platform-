using System.Security.Claims;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;

namespace OptimizeAll.Api.Common.Security;

public static class AppClaims
{
    public const string UserId = "sub";
    public const string Role = "role";
    public const string SecurityVersion = "sv";
    public const string EmailVerified = "ev";
}

public static class ClaimsHelper
{
    public static IEnumerable<Role> GetRoles(ClaimsPrincipal principal) =>
        principal.FindAll(AppClaims.Role)
            .Select(c => Enum.TryParse<Role>(c.Value, out var r) ? (Role?)r : null)
            .Where(r => r.HasValue)
            .Select(r => r!.Value);
}

/// <summary>The authenticated caller for the current request.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The caller's id. Throws when unauthenticated; only use on authorized endpoints.</summary>
    Guid Id { get; }
    Guid? IdOrNull { get; }
    IReadOnlyCollection<Role> Roles { get; }
    IReadOnlySet<string> Permissions { get; }
    bool HasPermission(string permission);
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }

    /// <summary>Throws a 403 DomainException unless the caller holds the permission.</summary>
    void Require(string permission);
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor, IPermissionResolver resolver) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? IdOrNull =>
        IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(AppClaims.UserId), out var id) ? id : null;

    public Guid Id => IdOrNull ?? throw new DomainException("auth.required", "Authentication required.", DomainErrorKind.Unauthorized);

    public IReadOnlyCollection<Role> Roles => Principal is null ? Array.Empty<Role>() : ClaimsHelper.GetRoles(Principal).ToArray();

    /// <summary>Effective permissions: built-in roles plus custom roles (see <see cref="IPermissionResolver"/>).</summary>
    public IReadOnlySet<string> Permissions => Principal is null ? RolePermissions.For(Array.Empty<Role>()) : resolver.Resolve(Principal);

    public bool HasPermission(string permission) => Permissions.Contains(permission);

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString();

    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;

    public void Require(string permission)
    {
        if (!HasPermission(permission))
            throw DomainException.Forbidden("auth.forbidden", "You do not have permission to perform this action.");
    }
}
