using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Auth;

public sealed record LoginResult(AuthResponse Response, string RefreshToken, DateTime RefreshExpiresAt);

public interface IAuthService
{
    Task RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task VerifyEmailAsync(string token, CancellationToken ct);
    Task ResendVerificationAsync(string email, CancellationToken ct);
    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<LoginResult> RefreshAsync(string? rawRefreshToken, CancellationToken ct);
    Task LogoutAsync(string? rawRefreshToken, CancellationToken ct);
    Task ForgotPasswordAsync(string email, CancellationToken ct);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct);
    Task<SessionUserDto> GetSessionUserAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Non-production quick sign-in (DevTools:TestLoginEnabled): a normal session for an active test or demo account,
    /// without its password. The caller (DevTestLoginController) enforces the environment and account guards.
    /// </summary>
    Task<LoginResult> SignInWithoutPasswordAsync(Guid userId, CancellationToken ct);

    /// <summary>Revokes every refresh token of a user and invalidates outstanding access tokens.</summary>
    Task RevokeAllSessionsAsync(User user, string reason, CancellationToken ct);

    /// <summary>
    /// Starts a session for a user already authenticated by an external identity provider (e.g. Google). The caller
    /// has verified the identity and loaded <paramref name="user"/> with its roles; inactive users are refused. Changes
    /// staged in the context (link bookkeeping, audit rows) are saved together with the new refresh token.
    /// </summary>
    Task<LoginResult> SignInExternalAsync(User user, CancellationToken ct);
}

public sealed class AuthService(
    AppDbContext db,
    ITokenService tokens,
    IPasswordHasher<User> hasher,
    Notifications.Templates.AccountEmails accountEmails,
    IEventPublisher events,
    IAuditLogger audit,
    ICurrentUser currentUser,
    IPermissionResolver permissionResolver,
    IImpersonationContext impersonation,
    IPrivacyHasher privacyHasher,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan RotationGracePeriod = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefreshLockTimeout = TimeSpan.FromSeconds(15);
    private const int MaxReplacementHops = 20;
    /// <summary>Revocation reason of a refresh token its client presented (and received a replacement for).</summary>
    private const string RotatedReason = "rotated";
    /// <summary>Revocation reason of a replacement nobody presented, superseded when its predecessor came back in the grace window.</summary>
    private const string SupersededReason = "superseded";
    public const string RefreshRaceCode = "auth.refresh_race";
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(48);
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);
    private const string ReferralAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        if (!request.AcceptTerms)
            throw new DomainException("auth.terms_required", "You must accept the terms to register.");

        var normalizedEmail = Normalization.Email(request.Email);
        PasswordPolicy.Validate(request.Password, request.Email);
        var timeZone = ValidateTimeZone(request.TimeZone);

        var existing = await db.Set<User>().FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);
        if (existing is not null)
        {
            // Same response as a new registration (prevents account enumeration); tell the owner instead,
            // at most once a day so registration cannot be used to flood a mailbox.
            var noticeSince = Now.AddDays(-1);
            var recentlyNotified = await db.Set<OptimizeAll.Domain.Audit.AuditLog>().AnyAsync(a =>
                a.Action == "auth.duplicate_registration_notice" && a.EntityId == existing.Id.ToString() && a.CreatedAt > noticeSince, ct);
            if (recentlyNotified) return;
            audit.Record("auth.duplicate_registration_notice", nameof(User), existing.Id);
            await db.SaveChangesAsync(ct);
            await accountEmails.SendDuplicateRegistrationAsync(existing, ct);
            return;
        }

        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            DisplayName = request.DisplayName.Trim(),
            CountryCode = request.CountryCode.ToUpperInvariant(),
            LanguageCode = string.IsNullOrWhiteSpace(request.LanguageCode) ? "en" : request.LanguageCode.Trim().ToLowerInvariant(),
            TimeZone = timeZone,
            MarketingEmailOptIn = request.MarketingEmailOptIn,
            ReferralCode = await GenerateReferralCodeAsync(ct),
            LastActiveAt = Now,
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        user.Roles.Add(new UserRole { UserId = user.Id, Role = Role.Participant, GrantedAt = Now });
        db.Set<User>().Add(user);

        var (raw, hash) = tokens.CreateOpaqueToken();
        db.Set<UserToken>().Add(new UserToken
        {
            UserId = user.Id, Purpose = UserTokenPurpose.EmailVerification, TokenHash = hash,
            CreatedAt = Now, ExpiresAt = Now.Add(VerificationLifetime),
        });
        audit.Record("auth.registered", nameof(User), user.Id, after: new { user.CountryCode, user.LanguageCode });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (Common.Errors.ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            // Concurrent registration with the same email: behave exactly like the "existing" branch.
            logger.LogInformation("Concurrent registration for an existing email ignored");
            return;
        }

        await SendVerificationEmailAsync(user, raw, ct);
        await events.PublishAsync(new UserRegistered(user.Id, request.ReferralCode?.Trim(), request.InviteCode?.Trim(),
            privacyHasher.Hash(currentUser.IpAddress), privacyHasher.Hash(request.DeviceId), Now), ct);
    }

    public async Task VerifyEmailAsync(string token, CancellationToken ct)
    {
        var record = await FindValidTokenAsync(token, UserTokenPurpose.EmailVerification, ct)
                     ?? throw new DomainException("auth.invalid_token", "This verification link is invalid or has expired.");
        var user = await db.Set<User>().FirstAsync(u => u.Id == record.UserId, ct);

        record.UsedAt = Now;
        var newlyVerified = user.EmailVerifiedAt is null;
        if (newlyVerified)
        {
            user.EmailVerifiedAt = Now;
            audit.Record("auth.email_verified", nameof(User), user.Id);
        }
        await db.SaveChangesAsync(ct);

        if (newlyVerified)
            await events.PublishAsync(new EmailVerified(user.Id, Now), ct);
    }

    public async Task ResendVerificationAsync(string emailAddress, CancellationToken ct)
    {
        var normalized = Normalization.Email(emailAddress);
        var user = await db.Set<User>().FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null || user.EmailVerifiedAt is not null) return;

        // Throttle: at most one verification email per 2 minutes per account.
        var recent = await db.Set<UserToken>().AnyAsync(t => t.UserId == user.Id &&
            t.Purpose == UserTokenPurpose.EmailVerification && t.CreatedAt > Now.AddMinutes(-2), ct);
        if (recent) return;

        var (raw, hash) = tokens.CreateOpaqueToken();
        db.Set<UserToken>().Add(new UserToken
        {
            UserId = user.Id, Purpose = UserTokenPurpose.EmailVerification, TokenHash = hash,
            CreatedAt = Now, ExpiresAt = Now.Add(VerificationLifetime),
        });
        await db.SaveChangesAsync(ct);
        await SendVerificationEmailAsync(user, raw, ct);
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var normalized = Normalization.Email(request.Email);
        var user = await db.Set<User>().Include(u => u.Roles).FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);

        if (user is null)
        {
            // Equalize timing with the found-user path to avoid user enumeration.
            hasher.VerifyHashedPassword(new User(), DummyHash.Value, request.Password);
            throw InvalidCredentials();
        }

        // A locked account answers exactly like a wrong password so lockout does not reveal which emails exist.
        if (user.LockoutEndsAt is { } lockedUntil && lockedUntil > Now)
        {
            hasher.VerifyHashedPassword(new User(), DummyHash.Value, request.Password);
            throw InvalidCredentials();
        }

        // Accounts created through an external provider (Google) have no password until they set one via reset:
        // password sign-in is impossible for them, and it answers like any wrong password.
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            hasher.VerifyHashedPassword(new User(), DummyHash.Value, request.Password);
            throw InvalidCredentials();
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            await RegisterFailedLoginAsync(user.Id, ct);
            throw InvalidCredentials();
        }

        if (user.Status != UserStatus.Active)
            throw DomainException.Forbidden("account.suspended",
                user.Status == UserStatus.Suspended
                    ? "Your account is suspended. Contact support if you believe this is a mistake."
                    : "This account has been deactivated.");

        // Login bookkeeping uses atomic statements rather than the tracked (concurrency-stamped) entity, so parallel
        // sign-ins from several devices never conflict with each other or with staff edits of the user.
        var rehash = verification == PasswordVerificationResult.SuccessRehashNeeded
            ? hasher.HashPassword(user, request.Password)
            : user.PasswordHash;
        await db.Set<User>().Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.FailedLoginCount, 0)
            .SetProperty(u => u.LockoutEndsAt, (DateTime?)null)
            .SetProperty(u => u.LastLoginAt, Now)
            .SetProperty(u => u.LastActiveAt, Now)
            .SetProperty(u => u.PasswordHash, rehash), ct);

        var result = IssueSession(user, familyId: IdGenerator.NewId());
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<LoginResult> SignInExternalAsync(User user, CancellationToken ct)
    {
        if (user.Status != UserStatus.Active)
            throw DomainException.Forbidden("account.suspended",
                user.Status == UserStatus.Suspended
                    ? "Your account is suspended. Contact support if you believe this is a mistake."
                    : "This account has been deactivated.");

        await db.Set<User>().Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.LastLoginAt, Now)
            .SetProperty(u => u.LastActiveAt, Now), ct);

        var result = IssueSession(user, familyId: IdGenerator.NewId());
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>
    /// Counts a failed attempt in a single atomic UPDATE so concurrent guesses are all counted, and starts the
    /// lockout when the threshold is reached (the counter resets for the next window).
    /// </summary>
    private async Task RegisterFailedLoginAsync(Guid userId, CancellationToken ct)
    {
        DateTime? lockUntil = Now.Add(LockoutDuration);
        // LockoutEndsAt is assigned first: MySQL evaluates SET assignments left to right (later ones see earlier
        // results), so it must read FailedLoginCount before that is changed. SQLite reads the old row for all of them.
        await db.Set<User>().Where(u => u.Id == userId).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.LockoutEndsAt, u => u.FailedLoginCount + 1 >= MaxFailedLogins ? lockUntil : u.LockoutEndsAt)
            .SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1 >= MaxFailedLogins ? 0 : u.FailedLoginCount + 1), ct);
        var locked = await db.Set<User>().AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.LockoutEndsAt == lockUntil, ct);
        if (locked)
        {
            audit.Record("auth.locked_out", nameof(User), userId);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<LoginResult> RefreshAsync(string? rawRefreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken)) throw SessionExpired();

        var hash = tokens.Hash(rawRefreshToken);
        var presented = await db.Set<RefreshToken>().AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (presented is null) throw SessionExpired();

        // Rotations of one session family are serialized, so the grace decision below always sees the latest chain.
        await using var familyLock = await db.Dialect().AcquireNamedLockAsync(db, $"refresh:{presented.FamilyId:N}", RefreshLockTimeout, ct);
        var token = await db.Set<RefreshToken>().AsNoTracking().FirstAsync(t => t.Id == presented.Id, ct);

        // The token whose rotation issues the new session: the presented one, or (lost response) the unused tip of its chain.
        var rotate = token;
        var rotateReason = RotatedReason;
        if (token.RevokedAt is not null)
        {
            var withinGrace = token.RevokedReason is RotatedReason or SupersededReason && token.RevokedAt >= Now.Subtract(RotationGracePeriod);
            var (tip, replacementUsed) = await ReplacementTipAsync(token, ct);
            if (withinGrace && tip is not null && !replacementUsed)
            {
                // The response that carried the replacement never reached the browser (a reload or navigation aborted it,
                // or another tab raced this one): nobody has presented the replacement yet, so it is superseded by a new
                // sibling in the same family instead of signing the user out.
                rotate = tip;
                rotateReason = SupersededReason;
            }
            else
            {
                if (token.ReplacedByTokenId is not null && (replacementUsed || !withinGrace))
                {
                    // A rotated token presented again after its replacement was used, or outside the grace window:
                    // likely theft. Revoke the whole family.
                    await db.Set<RefreshToken>()
                        .Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, Now).SetProperty(t => t.RevokedReason, "reuse_detected"), ct);
                    logger.LogWarning("Refresh token reuse detected for user {UserId}; session family revoked", token.UserId);
                }
                throw SessionExpired();
            }
        }
        if (rotate.ExpiresAt <= Now) throw SessionExpired();

        var user = await db.Set<User>().AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is null || user.Status != UserStatus.Active) throw SessionExpired();

        // Rotation is atomic and transactional: the old token is revoked and its replacement inserted together, and
        // only one concurrent refresh with the same token can win the conditional update.
        await using var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct);
        var result = IssueSession(user, token.FamilyId, out var newToken);
        await db.SaveChangesAsync(ct);
        var rotated = await db.Set<RefreshToken>()
            .Where(t => t.Id == rotate.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, Now)
                .SetProperty(t => t.RevokedReason, rotateReason)
                .SetProperty(t => t.ReplacedByTokenId, newToken.Id), ct);
        if (rotated == 0)
        {
            await tx.RollbackAsync(ct);
            throw RefreshRace();
        }
        await db.Set<User>().Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastActiveAt, Now), ct);
        await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>
    /// Follows the replacement chain of a revoked token through superseded links (replacements whose response was lost)
    /// to its tip. Returns the tip when it is still live, and whether any replacement on the way was actually presented
    /// by a client (rotated by use), which means the presented token is stale and possibly stolen.
    /// </summary>
    private async Task<(RefreshToken? LiveTip, bool ReplacementUsed)> ReplacementTipAsync(RefreshToken token, CancellationToken ct)
    {
        var nextId = token.ReplacedByTokenId;
        for (var hop = 0; nextId is { } id && hop < MaxReplacementHops; hop++)
        {
            var next = await db.Set<RefreshToken>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.FamilyId == token.FamilyId, ct);
            if (next is null) return (null, false);
            if (next.RevokedAt is null) return (next, false);
            if (next.RevokedReason == RotatedReason) return (null, true);
            if (next.RevokedReason != SupersededReason) return (null, false);
            nextId = next.ReplacedByTokenId;
        }
        return (null, false);
    }

    public async Task LogoutAsync(string? rawRefreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken)) return;
        var hash = tokens.Hash(rawRefreshToken);
        await db.Set<RefreshToken>()
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, Now).SetProperty(t => t.RevokedReason, "logout"), ct);
    }

    public async Task ForgotPasswordAsync(string emailAddress, CancellationToken ct)
    {
        var normalized = Normalization.Email(emailAddress);
        var user = await db.Set<User>().FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null || user.Status == UserStatus.Deactivated) return;

        var recent = await db.Set<UserToken>().AnyAsync(t => t.UserId == user.Id &&
            t.Purpose == UserTokenPurpose.PasswordReset && t.CreatedAt > Now.AddMinutes(-2), ct);
        if (recent) return;

        var (raw, hash) = tokens.CreateOpaqueToken();
        db.Set<UserToken>().Add(new UserToken
        {
            UserId = user.Id, Purpose = UserTokenPurpose.PasswordReset, TokenHash = hash,
            CreatedAt = Now, ExpiresAt = Now.Add(ResetLifetime),
        });
        await db.SaveChangesAsync(ct);

        await accountEmails.SendPasswordResetAsync(user, raw, ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var record = await FindValidTokenAsync(request.Token, UserTokenPurpose.PasswordReset, ct)
                     ?? throw new DomainException("auth.invalid_token", "This reset link is invalid or has expired.");
        var user = await db.Set<User>().FirstAsync(u => u.Id == record.UserId, ct);
        PasswordPolicy.Validate(request.NewPassword, user.Email);

        record.UsedAt = Now;
        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
        // Possession of the reset link proves control of the mailbox.
        user.EmailVerifiedAt ??= Now;
        audit.Record("auth.password_reset", nameof(User), user.Id);
        await RevokeAllSessionsAsync(user, "password_reset", ct);
        await InvalidatePasswordResetLinksAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.Set<User>().FirstAsync(u => u.Id == userId, ct);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            throw new DomainException("auth.invalid_password", "Your current password is incorrect.");
        PasswordPolicy.Validate(request.NewPassword, user.Email);
        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        audit.Record("auth.password_changed", nameof(User), user.Id);
        await RevokeAllSessionsAsync(user, "password_changed", ct);
        await InvalidatePasswordResetLinksAsync(user.Id, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Once the password was reset or changed, every other reset link still in someone's mailbox is spent too: an older
    /// link (a second request, or one requested by whoever briefly had the mailbox) must not change the password again.
    /// </summary>
    private Task InvalidatePasswordResetLinksAsync(Guid userId, CancellationToken ct) =>
        db.Set<UserToken>()
            .Where(t => t.UserId == userId && t.Purpose == UserTokenPurpose.PasswordReset && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, Now), ct);

    public async Task<SessionUserDto> GetSessionUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Set<User>().AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw DomainException.NotFound("User");
        var dto = ToDto(user, await permissionResolver.ForUserAsync(user.Id, user.Roles.Select(r => r.Role), ct)) with
        {
            IsTestAccount = user.IsTestAccount,
            CustomRoles = await CustomRoleNamesQuery(db, user.Id).ToListAsync(ct),
        };
        return impersonation.SessionId is { } sessionId
            ? dto with { ImpersonatedBy = await ImpersonatorAsync(db, sessionId, ct) }
            : dto;
    }

    /// <summary>The impersonator shown on the session of an impersonation token (null when the session is gone).</summary>
    public static Task<ImpersonatorDto?> ImpersonatorAsync(AppDbContext db, Guid sessionId, CancellationToken ct) =>
        (from s in db.Set<ImpersonationSession>().AsNoTracking()
         join u in db.Set<User>().AsNoTracking() on s.ImpersonatorUserId equals u.Id
         where s.Id == sessionId
         select new ImpersonatorDto(u.Id, u.DisplayName, u.Email, s.StartedAt, s.ExpiresAt)).FirstOrDefaultAsync(ct);

    public async Task<LoginResult> SignInWithoutPasswordAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Set<User>().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw DomainException.NotFound("User");
        if (user.Status != UserStatus.Active)
            throw DomainException.Forbidden("account.suspended", "This account is not active.");
        await db.Set<User>().Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.LastLoginAt, Now)
            .SetProperty(u => u.LastActiveAt, Now), ct);
        audit.Record("auth.test_login", nameof(User), user.Id, after: new { user.IsTestAccount });
        var result = IssueSession(user, familyId: IdGenerator.NewId());
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task RevokeAllSessionsAsync(User user, string reason, CancellationToken ct)
    {
        user.SecurityVersion++;
        await db.Set<RefreshToken>()
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, Now).SetProperty(t => t.RevokedReason, reason), ct);
    }

    /// <summary>Session user with effective permissions (built-in roles + custom roles, see <see cref="IPermissionResolver"/>).</summary>
    private SessionUserDto ToDto(User user) =>
        ToDto(user, permissionResolver.ForUser(user.Id, user.Roles.Select(r => r.Role))) with
        {
            CustomRoles = CustomRoleNamesQuery(db, user.Id).ToList(),
        };

    /// <summary>Names of the user's custom roles, sorted (shown as badges next to the built-in roles).</summary>
    public static IQueryable<string> CustomRoleNamesQuery(AppDbContext db, Guid userId) =>
        from a in db.Set<UserCustomRole>().AsNoTracking()
        join r in db.Set<CustomRole>().AsNoTracking() on a.CustomRoleId equals r.Id
        where a.UserId == userId
        orderby r.Name
        select r.Name;

    public static SessionUserDto ToDto(User user, IEnumerable<string> effectivePermissions)
    {
        var roles = user.Roles.Select(r => r.Role).ToArray();
        return new SessionUserDto(user.Id, user.Email, user.DisplayName, user.IsEmailVerified, user.CountryCode,
            user.LanguageCode, user.TimeZone, user.Status.ToString(), roles.Select(r => r.ToString()).ToArray(),
            effectivePermissions.Distinct().OrderBy(p => p).ToArray(), CustomRoles: Array.Empty<string>());
    }

    private LoginResult IssueSession(User user, Guid familyId) => IssueSession(user, familyId, out _);

    private LoginResult IssueSession(User user, Guid familyId, out RefreshToken refreshToken)
    {
        var access = tokens.CreateAccessToken(user);
        var (raw, hash) = tokens.CreateOpaqueToken();
        refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            FamilyId = familyId,
            CreatedAt = Now,
            ExpiresAt = Now.AddDays(jwtOptions.Value.RefreshTokenDays),
            CreatedByIp = currentUser.IpAddress,
            UserAgent = currentUser.UserAgent is { Length: > 300 } ua ? ua[..300] : currentUser.UserAgent,
        };
        db.Set<RefreshToken>().Add(refreshToken);
        return new LoginResult(new AuthResponse(access.Token, access.ExpiresAt, ToDto(user) with { IsTestAccount = user.IsTestAccount }),
            raw, refreshToken.ExpiresAt);
    }

    private async Task<UserToken?> FindValidTokenAsync(string raw, UserTokenPurpose purpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var hash = tokens.Hash(raw.Trim());
        var record = await db.Set<UserToken>().FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, ct);
        return record is null || record.UsedAt is not null || record.ExpiresAt <= Now ? null : record;
    }

    private async Task SendVerificationEmailAsync(User user, string rawToken, CancellationToken ct)
    {
        // Rendered from the editable "auth.verify_email" template (Admin → Content → Email templates).
        var result = await accountEmails.SendVerificationAsync(user, rawToken, ct);
        if (!result.Success)
            logger.LogWarning("Verification email for user {UserId} failed: {Error}", user.Id, result.Error);
    }

    private async Task<string> GenerateReferralCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = new string(Enumerable.Range(0, 8)
                .Select(_ => ReferralAlphabet[RandomNumberGenerator.GetInt32(ReferralAlphabet.Length)]).ToArray());
            if (!await db.Set<User>().AnyAsync(u => u.ReferralCode == code, ct)) return code;
        }
        throw new InvalidOperationException("Could not allocate a unique referral code.");
    }

    private static string ValidateTimeZone(string? tz)
    {
        if (string.IsNullOrWhiteSpace(tz)) return "UTC";
        if (TimeZoneInfo.TryFindSystemTimeZoneById(tz.Trim(), out _)) return tz.Trim();
        throw new DomainException("auth.invalid_timezone", "Unknown time zone.", DomainErrorKind.Validation,
            new Dictionary<string, string[]> { ["timeZone"] = new[] { "Use an IANA time zone such as Europe/London." } });
    }

    private static DomainException InvalidCredentials() =>
        new("auth.invalid_credentials",
            "The email or password is incorrect. After repeated failures, sign-in is paused for 15 minutes.", DomainErrorKind.Unauthorized);

    private static DomainException RefreshRace() =>
        new(RefreshRaceCode, "Your session was refreshed in another tab. Retry the request.", DomainErrorKind.Unauthorized);

    private static DomainException SessionExpired() =>
        new("auth.session_expired", "Your session has expired. Please sign in again.", DomainErrorKind.Unauthorized);

    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<User>().HashPassword(new User(), "timing-equalizer-password"));
}
