using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Auth;

/// <summary>The impersonation access token plus the raw session token for the impersonation cookie.</summary>
public sealed record ImpersonationStart(AuthResponse Response, string SessionToken, DateTime SessionExpiresAt);

/// <summary>
/// "Log in as user". Design:
/// <list type="bullet">
/// <item>The staff member's own session is untouched: their refresh cookie (<c>oa_refresh</c>) stays as it is and is
/// not rotated while impersonating.</item>
/// <item>Starting creates an <see cref="ImpersonationSession"/> (hard lifetime <see cref="ImpersonationOptions.SessionMinutes"/>)
/// whose opaque token goes into a separate HttpOnly cookie (<c>oa_impersonation</c>, same flags and path as the refresh
/// cookie). Access tokens for the target carry <see cref="ImpersonationClaims"/> and never outlive the session.</item>
/// <item><c>/auth/refresh</c> first resumes a live impersonation session (only together with a live refresh token of the
/// impersonator), otherwise drops the impersonation cookie and refreshes the staff member's own session. So exiting,
/// expiry, or the impersonator being signed out all land back on the staff member's session (or the sign-in page).</item>
/// <item>Every request made with such a token is validated against the session (ended/expired/impersonator suspended
/// or signed out ⇒ 401), audited with the impersonator id, and high-risk actions are refused
/// (<see cref="DeniedWhileImpersonatingAttribute"/>).</item>
/// </list>
/// </summary>
public sealed class ImpersonationService(
    AppDbContext db,
    ITokenService tokens,
    ICurrentUser currentUser,
    IImpersonationContext impersonation,
    IAuditLogger audit,
    IOptions<ImpersonationOptions> options,
    IPermissionResolver permissions,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Whether a user with these roles may never be impersonated (admins and other impersonators).</summary>
    public static bool IsProtectedTarget(IEnumerable<Role> roles)
    {
        var list = roles.ToList();
        return list.Contains(Role.Admin) || RolePermissions.For(list).Contains(Permissions.UsersImpersonate);
    }

    /// <summary>Like <see cref="IsProtectedTarget(IEnumerable{Role})"/>, including permissions granted by custom roles.</summary>
    private async Task<bool> IsProtectedTargetAsync(User target, CancellationToken ct)
    {
        var roles = target.Roles.Select(r => r.Role).ToList();
        return IsProtectedTarget(roles) || Impersonation.IsProtectedTarget(roles, await permissions.ForUserAsync(target.Id, roles, ct));
    }

    public async Task<ImpersonationStart> StartAsync(Guid targetId, ImpersonateRequest request, CancellationToken ct)
    {
        if (impersonation.IsImpersonating) throw Impersonation.ForbiddenAction();
        if (!request.Confirm)
            throw FieldRules.FieldError("admin.confirmation_required", "confirm", "Confirm this sensitive action by sending \"confirm\": true.");
        var reason = request.Reason.Trim();
        if (reason.Length < 5)
            throw FieldRules.FieldError("admin.reason_required", "reason", "Explain why you need to view this account.");

        var impersonatorId = currentUser.Id;
        if (targetId == impersonatorId)
            throw DomainException.Forbidden("admin.impersonation_self", "You can't impersonate yourself.");

        var target = await db.Set<User>().AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == targetId, ct)
                     ?? throw DomainException.NotFound("User");
        if (await IsProtectedTargetAsync(target, ct))
            throw DomainException.Forbidden("admin.impersonation_target_forbidden",
                "Administrators and staff who can impersonate others can't be impersonated.");
        if (target.Status != UserStatus.Active)
            throw DomainException.Conflict("admin.impersonation_target_inactive",
                "Suspended or deactivated accounts can't be impersonated. Reactivate the account first.");

        var impersonator = await db.Set<User>().AsNoTracking().FirstAsync(u => u.Id == impersonatorId, ct);

        // One impersonation at a time per staff member: starting a new one ends any other (audited like any ending).
        var previous = await db.Set<ImpersonationSession>().AsNoTracking()
            .Where(s => s.ImpersonatorUserId == impersonatorId && s.EndedAt == null).ToListAsync(ct);
        foreach (var old in previous) await EndAndAuditAsync(old, "replaced", ct);

        var (raw, hash) = tokens.CreateOpaqueToken();
        var session = new ImpersonationSession
        {
            ImpersonatorUserId = impersonatorId,
            TargetUserId = target.Id,
            Reason = reason,
            TokenHash = hash,
            ImpersonatorSecurityVersion = impersonator.SecurityVersion,
            StartedAt = Now,
            ExpiresAt = Now.AddMinutes(Math.Clamp(options.Value.SessionMinutes, 1, 60)),
            IpAddress = currentUser.IpAddress,
        };
        db.Set<ImpersonationSession>().Add(session);
        audit.Record("admin.impersonation_started", nameof(User), target.Id, after: new
        {
            sessionId = session.Id,
            impersonatorUserId = impersonatorId,
            impersonatorName = impersonator.DisplayName,
            targetName = target.DisplayName,
            target.IsTestAccount,
            session.ExpiresAt,
        }, reason: reason);
        await db.SaveChangesAsync(ct);

        return new ImpersonationStart(Issue(target, impersonator, session), raw, session.ExpiresAt);
    }

    /// <summary>
    /// Re-issues an access token for a live impersonation session (page reload, token about to expire). Returns null
    /// when the session is over or is not backed by a live refresh token of the impersonator; the caller then falls
    /// back to the impersonator's own session.
    /// </summary>
    public async Task<AuthResponse?> ResumeAsync(string rawSessionToken, string? rawRefreshToken, CancellationToken ct)
    {
        var hash = tokens.Hash(rawSessionToken);
        var session = await db.Set<ImpersonationSession>().AsNoTracking().FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is null || session.EndedAt is not null) return null;
        if (session.ExpiresAt <= Now)
        {
            await EndAndAuditAsync(session, "expired", ct, save: true);
            return null;
        }

        var impersonator = await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == session.ImpersonatorUserId, ct);
        var target = await db.Set<User>().AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == session.TargetUserId, ct);
        var refreshHash = string.IsNullOrWhiteSpace(rawRefreshToken) ? null : tokens.Hash(rawRefreshToken);
        var adminSessionLive = refreshHash is not null && await db.Set<RefreshToken>().AsNoTracking().AnyAsync(t =>
            t.TokenHash == refreshHash && t.UserId == session.ImpersonatorUserId && t.RevokedAt == null && t.ExpiresAt > Now, ct);
        if (impersonator is null || impersonator.Status != UserStatus.Active ||
            impersonator.SecurityVersion != session.ImpersonatorSecurityVersion || !adminSessionLive ||
            target is null || target.Status != UserStatus.Active)
        {
            await EndAndAuditAsync(session, "admin_session_ended", ct, save: true);
            return null;
        }
        // Custom-role changes don't bump security versions: re-check that the impersonator still may impersonate and the
        // target is still impersonable (not an admin or impersonator by now).
        if (!await Impersonation.PermissionsStillAllowAsync(impersonator.Id, target.Id, db, permissions, ct))
        {
            await EndAndAuditAsync(session, "permissions_changed", ct, save: true);
            return null;
        }
        return Issue(target, impersonator, session);
    }

    /// <summary>
    /// Ends the caller's impersonation session (from the access token) and/or the one in the cookie. Idempotent.
    /// Returns true when a live session was ended.
    /// </summary>
    public async Task<bool> EndAsync(string? rawSessionToken, string reason, CancellationToken ct)
    {
        var ids = new List<Guid>();
        if (impersonation.SessionId is { } fromToken) ids.Add(fromToken);
        if (!string.IsNullOrWhiteSpace(rawSessionToken))
        {
            var hash = tokens.Hash(rawSessionToken);
            var fromCookie = await db.Set<ImpersonationSession>().AsNoTracking().Where(s => s.TokenHash == hash)
                .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            if (fromCookie is { } id && !ids.Contains(id)) ids.Add(id);
        }

        var ended = false;
        foreach (var id in ids)
        {
            var session = await db.Set<ImpersonationSession>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
            if (session is null || session.EndedAt is not null) continue;
            if (await EndAndAuditAsync(session, reason, ct)) ended = true;
        }
        if (ended) await db.SaveChangesAsync(ct);
        return ended;
    }

    /// <summary>
    /// Ends a live session (conditional update, so concurrent enders record it once) and stages the
    /// <c>admin.impersonation_ended</c> audit row with the reason (exit, logout, expired, replaced, admin_session_ended,
    /// permissions_changed). With <paramref name="save"/> the audit row is saved at once.
    /// </summary>
    private async Task<bool> EndAndAuditAsync(ImpersonationSession session, string reason, CancellationToken ct, bool save = false)
    {
        var updated = await db.Set<ImpersonationSession>().Where(s => s.Id == session.Id && s.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, Now).SetProperty(x => x.EndedReason, reason), ct);
        if (updated == 0) return false;
        audit.Record("admin.impersonation_ended", nameof(User), session.TargetUserId,
            after: new { sessionId = session.Id, impersonatorUserId = session.ImpersonatorUserId, endedReason = reason });
        if (save) await db.SaveChangesAsync(ct);
        return true;
    }

    private AuthResponse Issue(User target, User impersonator, ImpersonationSession session)
    {
        var access = tokens.CreateAccessToken(target,
            new ImpersonationGrant(session.Id, impersonator.Id, impersonator.DisplayName, session.ExpiresAt));
        var user = AuthService.ToDto(target, permissions.ForUser(target.Id, target.Roles.Select(r => r.Role))) with
        {
            IsTestAccount = target.IsTestAccount,
            CustomRoles = AuthService.CustomRoleNamesQuery(db, target.Id).ToList(),
            ImpersonatedBy = new ImpersonatorDto(impersonator.Id, impersonator.DisplayName, impersonator.Email, session.StartedAt, session.ExpiresAt),
        };
        return new AuthResponse(access.Token, access.ExpiresAt, user);
    }
}
