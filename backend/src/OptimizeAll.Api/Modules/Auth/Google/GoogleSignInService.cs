using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Auth.Google;

public sealed record GoogleStartResult(string AuthorizationUrl, string FlowCookie, DateTime FlowExpiresAt);

public sealed record GoogleCallbackResult(GoogleCallbackResponse Response, LoginResult? Session);

/// <summary>
/// "Sign in with Google": starts the authorization-code flow (signed state, nonce, PKCE), finishes it (code exchange,
/// ID-token validation) and applies the account rules:
/// <list type="number">
/// <item>A known Google identity (provider + <c>sub</c>) signs in its user.</item>
/// <item>Otherwise a user with the same email is linked automatically only when that user's email is verified and the
/// user holds no staff role, and Google is authoritative for the address (Gmail or Workspace, see
/// <see cref="IsGoogleAuthoritative"/>); else the caller must sign in with their password and link from the profile.</item>
/// <item>Otherwise the caller must accept the terms (<see cref="CompleteAsync"/>) and a Participant account with a
/// verified email and no password is created. Staff roles are never granted here.</item>
/// </list>
/// Suspended, deactivated and locked-out users cannot sign in this way either. Every link and sign-in is audited.
/// </summary>
public sealed class GoogleSignInService(
    AppDbContext db,
    IAuthService auth,
    GoogleOidcClient oidc,
    GoogleIdTokenValidator validator,
    GoogleFlowProtector protector,
    IAuditLogger audit,
    ICurrentUser currentUser,
    IEventPublisher events,
    IPrivacyHasher privacyHasher,
    IOptions<GoogleAuthOptions> options,
    TimeProvider clock)
{
    private const string Provider = ExternalLoginProviders.Google;
    public static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(15);
    private const string ReferralAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Roles whose accounts may be linked to Google by a matching verified email (never staff).</summary>
    private static readonly HashSet<Role> AutoLinkableRoles = new() { Role.Participant, Role.Client };

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public bool Enabled => options.Value.Enabled;

    public GoogleStartResult Start(string mode, Guid? userId, string? returnTo)
    {
        RequireEnabled();
        var expiresAt = Now.Add(FlowLifetime);
        var stateId = GoogleFlowProtector.RandomToken();
        var nonce = GoogleFlowProtector.RandomToken();
        var verifier = GoogleOidcClient.NewCodeVerifier();
        var flow = new GoogleFlow(stateId, nonce, verifier, mode, userId, SafeReturnTo(returnTo), expiresAt);
        var url = oidc.BuildAuthorizationUrl(protector.ProtectState(stateId, expiresAt), nonce, verifier);
        return new GoogleStartResult(url, protector.ProtectFlow(flow), expiresAt);
    }

    public async Task<GoogleCallbackResult> CallbackAsync(GoogleCallbackRequest request, string? flowCookie, CancellationToken ct)
    {
        RequireEnabled();
        var state = protector.UnprotectState(request.State);
        var flow = protector.UnprotectFlow(flowCookie);
        if (state is null || flow is null || !FixedTimeEquals(state.StateId, flow.StateId) ||
            state.ExpiresAt <= Now || flow.ExpiresAt <= Now)
            throw StateInvalid();
        if (flow.Mode == GoogleFlowProtector.FlowModeLink &&
            (flow.UserId is null || !currentUser.IsAuthenticated || currentUser.Id != flow.UserId))
            throw StateInvalid();

        var idToken = await oidc.ExchangeCodeAsync(request.Code.Trim(), flow.CodeVerifier, ct);
        var identity = await validator.ValidateAsync(idToken, flow.Nonce, ct);

        return flow.Mode == GoogleFlowProtector.FlowModeLink
            ? await LinkToSignedInUserAsync(flow.UserId!.Value, identity, flow.ReturnTo, ct)
            : await SignInAsync(identity, flow.ReturnTo, ct);
    }

    private async Task<GoogleCallbackResult> SignInAsync(GoogleIdentity identity, string? returnTo, CancellationToken ct, bool retried = false)
    {
        var login = await db.Set<ExternalLogin>()
            .FirstOrDefaultAsync(l => l.Provider == Provider && l.Subject == identity.Subject, ct);
        if (login is not null)
        {
            var user = await LoadUserAsync(login.UserId, ct);
            EnsureCanSignIn(user);
            login.LastUsedAt = Now;
            audit.Record("auth.google_sign_in", nameof(User), user.Id, after: new { provider = Provider });
            var session = await auth.SignInExternalAsync(user, ct);
            return new GoogleCallbackResult(new GoogleCallbackResponse(GoogleCallbackStatus.SignedIn, session.Response, ReturnTo: returnTo), session);
        }

        var normalized = Normalization.Email(identity.Email);
        var existing = await db.Set<User>().Include(u => u.Roles).FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (existing is not null)
        {
            EnsureCanSignIn(existing);
            if (!existing.IsEmailVerified)
                throw DomainException.Conflict("auth.google_link_unverified",
                    "An account with this email already exists, but its email address hasn't been verified. Sign in with " +
                    "your password, then connect Google from your profile's security settings.");
            // Staff accounts, and addresses Google is not the authority for (a consumer Google account registered with
            // a non-Gmail address may have been verified by a previous owner of that mailbox or domain), are never
            // linked by email: the owner signs in with their password and connects Google from the profile.
            if (existing.Roles.Any(r => !AutoLinkableRoles.Contains(r.Role)) || !IsGoogleAuthoritative(identity))
                throw DomainException.Conflict("auth.google_link_requires_sign_in",
                    "An account with this email already exists. Sign in with your password, then connect Google from " +
                    "your profile's security settings.");
            if (await db.Set<ExternalLogin>().AnyAsync(l => l.UserId == existing.Id && l.Provider == Provider, ct))
                throw OtherGoogleAccount();

            AddLink(existing.Id, identity);
            audit.Record("auth.external_login_linked", nameof(User), existing.Id,
                after: new { provider = Provider, method = "verified_email_match" });
            audit.Record("auth.google_sign_in", nameof(User), existing.Id, after: new { provider = Provider });
            LoginResult session;
            try
            {
                session = await auth.SignInExternalAsync(existing, ct);
            }
            catch (DbUpdateException ex) when (!retried && Common.Errors.ProblemExceptionHandler.IsUniqueViolation(ex))
            {
                // A concurrent sign-in (another tab) linked this Google account first, or linked another one to this
                // user: re-evaluate against the database instead of answering a bare "duplicate record".
                db.ChangeTracker.Clear();
                return await SignInAsync(identity, returnTo, ct, retried: true);
            }
            return new GoogleCallbackResult(new GoogleCallbackResponse(GoogleCallbackStatus.SignedIn, session.Response, ReturnTo: returnTo), session);
        }

        // New user: nothing is created until they accept the terms.
        var ticket = protector.ProtectTicket(new GoogleSignUpTicket(identity.Subject, identity.Email, identity.Name, Now.Add(TicketLifetime)));
        return new GoogleCallbackResult(new GoogleCallbackResponse(GoogleCallbackStatus.NeedsTerms, Ticket: ticket,
            Email: identity.Email, DisplayName: SuggestedDisplayName(identity.Name, identity.Email), ReturnTo: returnTo), null);
    }

    private async Task<GoogleCallbackResult> LinkToSignedInUserAsync(Guid userId, GoogleIdentity identity, string? returnTo,
        CancellationToken ct, bool retried = false)
    {
        var user = await LoadUserAsync(userId, ct);
        EnsureCanSignIn(user);
        var bySubject = await db.Set<ExternalLogin>()
            .FirstOrDefaultAsync(l => l.Provider == Provider && l.Subject == identity.Subject, ct);
        if (bySubject is not null)
        {
            if (bySubject.UserId != userId)
                throw DomainException.Conflict("auth.google_already_linked",
                    "This Google account is already connected to another Optimize All account.");
            return new GoogleCallbackResult(new GoogleCallbackResponse(GoogleCallbackStatus.Linked, ReturnTo: returnTo), null);
        }
        if (await db.Set<ExternalLogin>().AnyAsync(l => l.UserId == userId && l.Provider == Provider, ct))
            throw OtherGoogleAccount();

        AddLink(userId, identity);
        audit.Record("auth.external_login_linked", nameof(User), userId, after: new { provider = Provider, method = "profile" });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (!retried && Common.Errors.ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            // A concurrent link won the unique index (provider + sub, or user + provider): re-evaluate against the
            // database so the caller gets the same answer as in the sequential case.
            db.ChangeTracker.Clear();
            return await LinkToSignedInUserAsync(userId, identity, returnTo, ct, retried: true);
        }
        return new GoogleCallbackResult(new GoogleCallbackResponse(GoogleCallbackStatus.Linked, ReturnTo: returnTo), null);
    }

    /// <summary>Creates the Participant account of a new Google user after they accepted the terms, and signs them in.</summary>
    public async Task<LoginResult> CompleteAsync(GoogleCompleteRequest request, CancellationToken ct)
    {
        RequireEnabled();
        var ticket = protector.UnprotectTicket(request.Ticket);
        if (ticket is null || ticket.ExpiresAt <= Now)
            throw new DomainException("auth.google_ticket_invalid", "This sign-up step has expired. Continue with Google again.");
        if (!request.AcceptTerms)
            throw new DomainException("auth.terms_required", "You must accept the terms to register.");

        // A ticket creates at most one account; it never signs in an existing one.
        if (await db.Set<ExternalLogin>().AnyAsync(l => l.Provider == Provider && l.Subject == ticket.Subject, ct))
            throw AccountExists();
        var normalizedEmail = Normalization.Email(ticket.Email);
        if (await db.Set<User>().AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct))
            throw AccountExists();

        var displayName = (request.DisplayName?.Trim() is { Length: >= 2 } chosen ? chosen : SuggestedDisplayName(ticket.Name, ticket.Email));
        if (displayName.Length < 2)
            throw new DomainException("auth.invalid_display_name", "Enter a display name of at least 2 characters.", DomainErrorKind.Validation,
                new Dictionary<string, string[]> { ["displayName"] = new[] { "Enter a display name of at least 2 characters." } });

        var user = new User
        {
            Email = ticket.Email,
            NormalizedEmail = normalizedEmail,
            // No password: password sign-in is impossible until one is set through "forgot password".
            PasswordHash = string.Empty,
            DisplayName = displayName,
            CountryCode = request.CountryCode.ToUpperInvariant(),
            LanguageCode = string.IsNullOrWhiteSpace(request.LanguageCode) ? "en" : request.LanguageCode.Trim().ToLowerInvariant(),
            TimeZone = ValidateTimeZone(request.TimeZone),
            MarketingEmailOptIn = request.MarketingEmailOptIn,
            ReferralCode = await GenerateReferralCodeAsync(ct),
            // Google verified the address (email_verified was required).
            EmailVerifiedAt = Now,
            LastActiveAt = Now,
        };
        user.Roles.Add(new UserRole { UserId = user.Id, Role = Role.Participant, GrantedAt = Now });
        db.Set<User>().Add(user);
        AddLink(user.Id, new GoogleIdentity(ticket.Subject, ticket.Email, true, ticket.Name, null));
        audit.Record("auth.registered", nameof(User), user.Id,
            after: new { user.CountryCode, user.LanguageCode, provider = Provider, termsAccepted = true });
        audit.Record("auth.external_login_linked", nameof(User), user.Id, after: new { provider = Provider, method = "sign_up" });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (Common.Errors.ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            throw AccountExists(); // a concurrent completion or registration won
        }

        await events.PublishAsync(new UserRegistered(user.Id, request.ReferralCode?.Trim(), request.InviteCode?.Trim(),
            privacyHasher.Hash(currentUser.IpAddress), privacyHasher.Hash(request.DeviceId), Now), ct);
        await events.PublishAsync(new EmailVerified(user.Id, Now), ct);

        audit.Record("auth.google_sign_in", nameof(User), user.Id, after: new { provider = Provider });
        return await auth.SignInExternalAsync(user, ct);
    }

    public async Task<SignInMethodsResponse> GetSignInMethodsAsync(Guid userId, CancellationToken ct)
    {
        var passwordHash = await db.Set<User>().Where(u => u.Id == userId).Select(u => u.PasswordHash).FirstAsync(ct);
        var logins = await db.Set<ExternalLogin>().AsNoTracking().Where(l => l.UserId == userId)
            .OrderBy(l => l.Provider)
            .Select(l => new ExternalLoginDto(l.Provider, l.Email, l.CreatedAt, l.LastUsedAt))
            .ToListAsync(ct);
        return new SignInMethodsResponse(HasPassword(passwordHash), Enabled, logins);
    }

    /// <summary>Disconnects Google, unless it is the account's only way to sign in (no password, no other provider).</summary>
    public async Task UnlinkAsync(Guid userId, CancellationToken ct)
    {
        var login = await db.Set<ExternalLogin>().FirstOrDefaultAsync(l => l.UserId == userId && l.Provider == Provider, ct)
                    ?? throw new DomainException("auth.google_not_linked", "Google isn't connected to this account.", DomainErrorKind.NotFound);
        var passwordHash = await db.Set<User>().Where(u => u.Id == userId).Select(u => u.PasswordHash).FirstAsync(ct);
        var otherLogins = await db.Set<ExternalLogin>().CountAsync(l => l.UserId == userId && l.Id != login.Id, ct);
        if (!HasPassword(passwordHash) && otherLogins == 0)
            throw DomainException.Conflict("auth.google_unlink_last_method",
                "Google is the only way to sign in to this account. Set a password first (use \"Forgot password?\" on " +
                "the sign-in page), then disconnect Google.");

        db.Set<ExternalLogin>().Remove(login);
        audit.Record("auth.external_login_unlinked", nameof(User), userId, before: new { provider = Provider, email = login.Email });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Whether Google is the authority for the token's email address (Google's guidance): a Gmail address, or an account
    /// managed by a Google Workspace organization (<c>hd</c> claim). For any other address <c>email_verified</c> only
    /// says the address was verified once, possibly by a previous owner of the mailbox or domain.
    /// </summary>
    public static bool IsGoogleAuthoritative(GoogleIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(identity.HostedDomain)) return true;
        var at = identity.Email.LastIndexOf('@');
        if (at < 0) return false;
        var domain = identity.Email[(at + 1)..].Trim();
        return domain.Equals("gmail.com", StringComparison.OrdinalIgnoreCase) ||
               domain.Equals("googlemail.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A usable password exists (accounts created through Google have none until they set one).</summary>
    public static bool HasPassword(string? passwordHash) => !string.IsNullOrEmpty(passwordHash);

    /// <summary>Only same-origin app paths survive (open-redirect defence); anything else is dropped.</summary>
    public static string? SafeReturnTo(string? value)
    {
        var path = value?.Trim();
        if (string.IsNullOrEmpty(path) || path.Length > 500 || path[0] != '/' || path.StartsWith("//", StringComparison.Ordinal))
            return null;
        return path.Any(c => c == '\\' || char.IsControl(c)) ? null : path;
    }

    private void AddLink(Guid userId, GoogleIdentity identity) =>
        db.Set<ExternalLogin>().Add(new ExternalLogin
        {
            UserId = userId,
            Provider = Provider,
            Subject = identity.Subject,
            Email = identity.Email.Length > 254 ? identity.Email[..254] : identity.Email,
            CreatedAt = Now,
            LastUsedAt = Now,
        });

    private async Task<User> LoadUserAsync(Guid userId, CancellationToken ct) =>
        await db.Set<User>().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw StateInvalid();

    private void EnsureCanSignIn(User user)
    {
        if (user.Status != UserStatus.Active)
            throw DomainException.Forbidden("account.suspended",
                user.Status == UserStatus.Suspended
                    ? "Your account is suspended. Contact support if you believe this is a mistake."
                    : "This account has been deactivated.");
        if (user.LockoutEndsAt is { } until && until > Now)
            throw DomainException.Forbidden("auth.locked_out",
                "Sign-in is paused for this account after repeated failed attempts. Try again in 15 minutes.");
    }

    private void RequireEnabled()
    {
        if (!Enabled) throw Disabled();
    }

    public static DomainException Disabled() =>
        new("auth.google_disabled", "Sign-in with Google isn't available.", DomainErrorKind.NotFound);

    private static string SuggestedDisplayName(string? name, string email)
    {
        var candidate = !string.IsNullOrWhiteSpace(name) ? name.Trim() : email.Split('@')[0];
        return candidate.Length > 100 ? candidate[..100] : candidate;
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

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static DomainException StateInvalid() =>
        new("auth.google_state_invalid",
            "This Google sign-in link has expired or was started in another browser. Please try again.");

    private static DomainException OtherGoogleAccount() =>
        DomainException.Conflict("auth.google_other_account", "This account is already connected to a different Google account.");

    private static DomainException AccountExists() =>
        DomainException.Conflict("auth.google_account_exists",
            "An account for this Google address already exists. Continue with Google again to sign in.");
}
