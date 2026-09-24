using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Security;

/// <summary>Access-token claims carried only by impersonation tokens (see <see cref="ImpersonationGrant"/>).</summary>
public static class ImpersonationClaims
{
    /// <summary>Id of the <see cref="ImpersonationSession"/>.</summary>
    public const string SessionId = "imp_sid";

    /// <summary>The acting party (RFC 8693 "act" semantics, flattened): the impersonator's user id.</summary>
    public const string ActorId = "act_sub";

    /// <summary>The impersonator's display name at the start of the session.</summary>
    public const string ActorName = "act_name";
}

public sealed class ImpersonationOptions
{
    public const string Section = "Impersonation";

    /// <summary>Hard lifetime of an impersonation session. Never extended by refreshes.</summary>
    public int SessionMinutes { get; set; } = 60;
}

/// <summary>Added to an access token so it represents "<paramref name="ImpersonatorName"/> as the subject".</summary>
public sealed record ImpersonationGrant(Guid SessionId, Guid ImpersonatorId, string ImpersonatorName, DateTime SessionExpiresAt);

/// <summary>The impersonation state of the current request (from the validated access token).</summary>
public interface IImpersonationContext
{
    bool IsImpersonating { get; }
    Guid? SessionId { get; }
    Guid? ImpersonatorId { get; }
    string? ImpersonatorName { get; }
}

public sealed class HttpImpersonationContext(IHttpContextAccessor accessor) : IImpersonationContext
{
    private ClaimsPrincipal? Principal =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;

    public Guid? SessionId => Impersonation.SessionIdOf(Principal);
    public Guid? ImpersonatorId => Impersonation.ImpersonatorIdOf(Principal);
    public string? ImpersonatorName => Principal?.FindFirst(ImpersonationClaims.ActorName)?.Value;
    public bool IsImpersonating => SessionId.HasValue && ImpersonatorId.HasValue;
}

public static class Impersonation
{
    public const string ForbiddenActionCode = "auth.impersonation_forbidden_action";

    public static Guid? SessionIdOf(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirst(ImpersonationClaims.SessionId)?.Value, out var id) ? id : null;

    public static Guid? ImpersonatorIdOf(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirst(ImpersonationClaims.ActorId)?.Value, out var id) ? id : null;

    public static DomainException ForbiddenAction() =>
        DomainException.Forbidden(ForbiddenActionCode,
            "This action is not available while you are viewing as another user. Exit the impersonation session first.");

    /// <summary>
    /// Whether a user may never be impersonated: administrators and anyone whose effective permissions (built-in or
    /// custom roles) include <c>users.impersonate</c>.
    /// </summary>
    public static bool IsProtectedTarget(IEnumerable<Role> builtInRoles, IReadOnlySet<string> effectivePermissions) =>
        builtInRoles.Contains(Role.Admin) || effectivePermissions.Contains(Permissions.UsersImpersonate);

    /// <summary>
    /// Token validation for impersonation tokens (called for every authenticated request): the session must exist for
    /// this subject and impersonator, not be ended or expired, and the impersonator must still be active with the
    /// security version captured at the start. Permissions are re-checked on every request as well, because custom-role
    /// changes do not bump the security version: the impersonator must still hold <c>users.impersonate</c> (built-in or
    /// custom role) and the target must not have become a protected target (admin or impersonator) meanwhile.
    /// Ordinary tokens pass unchanged.
    /// </summary>
    public static async Task<bool> IsTokenStillValidAsync(ClaimsPrincipal principal, Guid subjectId, AppDbContext db,
        IPermissionResolver permissions, DateTime now, CancellationToken ct)
    {
        var sessionClaim = principal.FindFirst(ImpersonationClaims.SessionId);
        var actorClaim = principal.FindFirst(ImpersonationClaims.ActorId);
        if (sessionClaim is null && actorClaim is null) return true;
        if (!Guid.TryParse(sessionClaim?.Value, out var sessionId) || !Guid.TryParse(actorClaim?.Value, out var actorId)) return false;

        var live = await (from s in db.Set<ImpersonationSession>().AsNoTracking()
                          join u in db.Set<User>().AsNoTracking() on s.ImpersonatorUserId equals u.Id
                          where s.Id == sessionId && s.TargetUserId == subjectId && s.ImpersonatorUserId == actorId &&
                                s.EndedAt == null && s.ExpiresAt > now &&
                                u.Status == UserStatus.Active && u.SecurityVersion == s.ImpersonatorSecurityVersion
                          select s.Id).AnyAsync(ct);
        return live && await PermissionsStillAllowAsync(actorId, subjectId, db, permissions, ct);
    }

    /// <summary>
    /// The impersonator still holds <c>users.impersonate</c> and the target is still impersonable (effective permissions,
    /// custom roles included).
    /// </summary>
    public static async Task<bool> PermissionsStillAllowAsync(Guid impersonatorId, Guid targetId, AppDbContext db,
        IPermissionResolver permissions, CancellationToken ct)
    {
        var ids = new[] { impersonatorId, targetId };
        var roles = await db.Set<UserRole>().AsNoTracking().Where(r => ids.Contains(r.UserId))
            .Select(r => new { r.UserId, r.Role }).ToListAsync(ct);
        var actorRoles = roles.Where(r => r.UserId == impersonatorId).Select(r => r.Role).ToList();
        if (!(await permissions.ForUserAsync(impersonatorId, actorRoles, ct)).Contains(Permissions.UsersImpersonate)) return false;
        var targetRoles = roles.Where(r => r.UserId == targetId).Select(r => r.Role).ToList();
        return !IsProtectedTarget(targetRoles, await permissions.ForUserAsync(targetId, targetRoles, ct));
    }
}

/// <summary>
/// Refuses the action (403 <c>auth.impersonation_forbidden_action</c>) when the caller is impersonating another user:
/// changing credentials or payout destinations, payment/payout approvals and recording, role and account changes,
/// API/integration credentials and starting another impersonation. With <see cref="WritesOnly"/> (typical on a
/// controller) GET/HEAD requests are still allowed, so the impersonator can look but not act.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class DeniedWhileImpersonatingAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>Only block state-changing requests (not GET/HEAD).</summary>
    public bool WritesOnly { get; init; }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (Impersonation.SessionIdOf(context.HttpContext.User) is null) return;
        var method = context.HttpContext.Request.Method;
        if (WritesOnly && (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))) return;
        throw Impersonation.ForbiddenAction();
    }
}

/// <summary>
/// Records every request made with an impersonation token on the impersonated user ("impersonator as user": method,
/// path and query, status), including refused ones, in its own unit of work after the request finished:
/// state-changing requests as <c>impersonation.request</c>, reads (GET/HEAD) as <c>impersonation.read</c> — so what the
/// impersonator looked at (profiles, earnings, exports, documents) is on record too. Business audit rows written by the
/// handlers themselves carry <see cref="AuditLog.ImpersonatorUserId"/> as well (see AuditLogger).
/// </summary>
public sealed class ImpersonationAuditMiddleware(RequestDelegate next, ILogger<ImpersonationAuditMiddleware> logger)
{
    public const string WriteAction = "impersonation.request";
    public const string ReadAction = "impersonation.read";

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var sessionId = Impersonation.SessionIdOf(context.User);
        var impersonatorId = Impersonation.ImpersonatorIdOf(context.User);
        if (sessionId is null || impersonatorId is null || HttpMethods.IsOptions(method))
        {
            await next(context);
            return;
        }

        int status;
        try
        {
            await next(context);
            status = context.Response.StatusCode;
        }
        catch (Exception ex)
        {
            // The exception handler (outermost) turns this into the response; record the status it will write.
            status = ex is DomainException domain ? Errors.ProblemExceptionHandler.StatusFor(domain.Kind) : StatusCodes.Status500InternalServerError;
            await RecordAsync(context, sessionId.Value, impersonatorId.Value, status);
            throw;
        }
        await RecordAsync(context, sessionId.Value, impersonatorId.Value, status);
    }

    private async Task RecordAsync(HttpContext context, Guid sessionId, Guid impersonatorId, int status)
    {
        try
        {
            if (!Guid.TryParse(context.User.FindFirst(AppClaims.UserId)?.Value, out var userId)) return;
            await using var scope = context.RequestServices.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
            var path = context.Request.Path.Value ?? string.Empty;
            var method = context.Request.Method;
            var isRead = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
            var query = context.Request.QueryString.Value ?? string.Empty;
            db.Set<AuditLog>().Add(new AuditLog
            {
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                ActorUserId = userId,
                ImpersonatorUserId = impersonatorId,
                ActorType = "impersonation",
                Action = isRead ? ReadAction : WriteAction,
                EntityType = nameof(User),
                EntityId = userId.ToString(),
                AfterJson = JsonSerializer.Serialize(new
                {
                    sessionId,
                    method,
                    path = path.Length > 300 ? path[..300] : path,
                    query = query.Length > 300 ? query[..300] : query,
                    status,
                }),
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                CorrelationId = context.TraceIdentifier,
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record the impersonation request audit row for session {SessionId}", sessionId);
        }
    }
}
