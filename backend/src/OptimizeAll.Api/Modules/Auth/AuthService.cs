using System.Net;
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

    /// <summary>Revokes every refresh token of a user and invalidates outstanding access tokens.</summary>
    Task RevokeAllSessionsAsync(User user, string reason, CancellationToken ct);
}

public sealed class AuthService(
    AppDbContext db,
    ITokenService tokens,
    IPasswordHasher<User> hasher,
    IEmailSender email,
    IEventPublisher events,
    IAuditLogger audit,
    ICurrentUser currentUser,
    IPrivacyHasher privacyHasher,
    IOptions<JwtOptions> jwtOptions,
    IOptions<EmailOptions> emailOptions,
    TimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan RotationGracePeriod = TimeSpan.FromSeconds(30);
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
            await email.SendAsync(new EmailMessage(existing.Email, existing.DisplayName, "Someone tried to register with your email",
                $"Hi {existing.DisplayName},\n\nSomeone tried to create an Optimize All account with this email address. " +
                $"If it was you, sign in or reset your password at {emailOptions.Value.AppBaseUrl}/forgot-password.\n\n" +
                "If it wasn't you, you can ignore this message."), ct);
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

    /// <summary>
    /// Counts a failed attempt in a single atomic UPDATE so concurrent guesses are all counted, and starts the
    /// lockout when the threshold is reached (the counter resets for the next window).
    /// </summary>
    private async Task RegisterFailedLoginAsync(Guid userId, CancellationToken ct)
    {
        var lockUntil = Now.Add(LockoutDuration);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE users
               SET LockoutEndsAt = CASE WHEN FailedLoginCount + 1 >= {MaxFailedLogins} THEN {lockUntil} ELSE LockoutEndsAt END,
                   FailedLoginCount = CASE WHEN FailedLoginCount + 1 >= {MaxFailedLogins} THEN 0 ELSE FailedLoginCount + 1 END
             WHERE Id = {userId}", ct);
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
        var token = await db.Set<RefreshToken>().AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) throw SessionExpired();

        if (token.RevokedAt is not null)
        {
            // Two tabs refreshing with the same cookie: the loser arrives just after rotation. Within the grace window
            // this is a benign race, not theft, so the session family is kept and the winner's new cookie stays valid.
            if (token.RevokedReason == "rotated" && token.RevokedAt >= Now.Subtract(RotationGracePeriod))
                throw RefreshRace();

            if (token.ReplacedByTokenId is not null)
            {
                // A rotated token presented again outside the grace window: likely theft. Revoke the whole family.
                await db.Set<RefreshToken>()
                    .Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, Now).SetProperty(t => t.RevokedReason, "reuse_detected"), ct);
                logger.LogWarning("Refresh token reuse detected for user {UserId}; session family revoked", token.UserId);
            }
            throw SessionExpired();
        }
        if (token.ExpiresAt <= Now) throw SessionExpired();

        var user = await db.Set<User>().AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is null || user.Status != UserStatus.Active) throw SessionExpired();

        // Rotation is atomic and transactional: the old token is revoked and its replacement inserted together, and
        // only one concurrent refresh with the same token can win the conditional update.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = IssueSession(user, token.FamilyId, out var newToken);
        await db.SaveChangesAsync(ct);
        var rotated = await db.Set<RefreshToken>()
            .Where(t => t.Id == token.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, Now)
                .SetProperty(t => t.RevokedReason, "rotated")
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

        var link = $"{emailOptions.Value.AppBaseUrl}/reset-password?token={WebUtility.UrlEncode(raw)}";
        await email.SendAsync(new EmailMessage(user.Email, user.DisplayName, "Reset your Optimize All password",
            $"Hi {user.DisplayName},\n\nUse this link within one hour to choose a new password:\n{link}\n\n" +
            "If you didn't ask for this, you can ignore this email; your password won't change."), ct);
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
        await db.SaveChangesAsync(ct);
    }

    public async Task<SessionUserDto> GetSessionUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Set<User>().AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw DomainException.NotFound("User");
        return ToDto(user);
    }

    public async Task RevokeAllSessionsAsync(User user, string reason, CancellationToken ct)
    {
        user.SecurityVersion++;
        await db.Set<RefreshToken>()
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, Now).SetProperty(t => t.RevokedReason, reason), ct);
    }

    public static SessionUserDto ToDto(User user)
    {
        var roles = user.Roles.Select(r => r.Role).ToArray();
        return new SessionUserDto(user.Id, user.Email, user.DisplayName, user.IsEmailVerified, user.CountryCode,
            user.LanguageCode, user.TimeZone, user.Status.ToString(), roles.Select(r => r.ToString()).ToArray(),
            RolePermissions.For(roles).OrderBy(p => p).ToArray());
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
        return new LoginResult(new AuthResponse(access.Token, access.ExpiresAt, ToDto(user)), raw, refreshToken.ExpiresAt);
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
        var link = $"{emailOptions.Value.AppBaseUrl}/verify-email?token={WebUtility.UrlEncode(rawToken)}";
        var result = await email.SendAsync(new EmailMessage(user.Email, user.Email, "Verify your Optimize All email",
            // The display name is attacker-controlled until the address is verified, so it is not echoed here.
            $"Welcome to Optimize All!\n\nConfirm your email address to start joining paid campaigns:\n{link}\n\n" +
            "This link expires in 48 hours."), ct);
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
