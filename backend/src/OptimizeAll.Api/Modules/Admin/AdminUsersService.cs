using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Auth;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Admin;

public sealed class AdminUsersService(
    AppDbContext db,
    ICurrentUser currentUser,
    IAuthService auth,
    IAuditLogger audit,
    INotificationService notifications,
    IPasswordHasher<User> hasher,
    AuditLogService auditLogs,
    IPermissionDirectory directory,
    Roles.AdminRolesService customRoles,
    TimeProvider clock)
{
    public const int MaxExportRows = 50_000;
    private const string ReferralAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly string[] StatusActions = { "admin.user_suspended", "admin.user_reactivated" };

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ---------- Read ----------

    private async Task<IQueryable<User>> FilterAsync(AdminUserQuery q, CancellationToken ct)
    {
        var users = db.Set<User>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Permission))
        {
            var permission = q.Permission.Trim();
            if (!Permissions.All.Contains(permission))
                throw FieldRules.FieldError("admin.invalid_permission", "permission", "Unknown permission.");
            users = await directory.UsersWithPermissionAsync(permission, ct);
        }
        if (q.Role is { } role) users = users.Where(u => u.Roles.Any(r => r.Role == role));
        if (q.Status is { } status) users = users.Where(u => u.Status == status);
        if (!string.IsNullOrWhiteSpace(q.Country)) { var c = q.Country.Trim().ToUpperInvariant(); users = users.Where(u => u.CountryCode == c); }
        if (q.Tier is { } tier) users = users.Where(u => u.Tier == tier);
        if (q.IsTestAccount is { } isTest) users = users.Where(u => u.IsTestAccount == isTest);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            users = users.Where(u => EF.Functions.Like(u.Email, p, "\\") || EF.Functions.Like(u.DisplayName, p, "\\"));
        }
        return users;
    }

    public async Task<PagedResult<AdminUserListItemDto>> ListAsync(AdminUserQuery query, CancellationToken ct)
    {
        var q = await FilterAsync(query, ct);
        q = query.Sort?.ToLowerInvariant() switch
        {
            "email" => query.Desc ? q.OrderByDescending(u => u.Email) : q.OrderBy(u => u.Email),
            "displayname" => query.Desc ? q.OrderByDescending(u => u.DisplayName) : q.OrderBy(u => u.DisplayName),
            "lastactiveat" => query.Desc ? q.OrderByDescending(u => u.LastActiveAt) : q.OrderBy(u => u.LastActiveAt),
            _ => query.Desc ? q.OrderByDescending(u => u.CreatedAt) : q.OrderBy(u => u.CreatedAt),
        };
        var total = await q.CountAsync(ct);
        var users = await q.Include(u => u.Roles).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<AdminUserListItemDto>(users.Select(ToListItem).ToList(), total, query.Page, query.PageSize);
    }

    public async Task<FileContentResult> ExportCsvAsync(AdminUserQuery query, CancellationToken ct)
    {
        var users = await (await FilterAsync(query, ct)).Include(u => u.Roles).OrderBy(u => u.CreatedAt).Take(MaxExportRows).ToListAsync(ct);
        return Csv.File($"users-{Now:yyyyMMdd-HHmmss}.csv",
            new[] { "id", "email", "displayName", "countryCode", "languageCode", "status", "tier", "roles", "emailVerified", "createdAt", "lastActiveAt", "isTestAccount" },
            users.Select(u => new object?[]
            {
                u.Id, u.Email, u.DisplayName, u.CountryCode, u.LanguageCode, u.Status, u.Tier,
                string.Join(';', u.Roles.Select(r => r.Role).OrderBy(r => r)), u.IsEmailVerified, u.CreatedAt, u.LastActiveAt,
                u.IsTestAccount,
            }));
    }

    public async Task<AdminUserDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Set<User>().AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id, ct)
                   ?? throw DomainException.NotFound("User");
        var now = Now;
        var idString = id.ToString();

        var history = await (from l in db.Set<AuditLog>().AsNoTracking()
                             where l.EntityType == nameof(User) && l.EntityId == idString && StatusActions.Contains(l.Action)
                             join a in db.Set<User>().AsNoTracking() on l.ActorUserId equals a.Id into actors
                             from a in actors.DefaultIfEmpty()
                             orderby l.Id descending
                             select new StatusHistoryDto(l.CreatedAt, l.Action, l.ActorUserId, a == null ? null : a.DisplayName, l.Reason))
            .Take(100).ToListAsync(ct);

        var accounts = (await db.Set<SocialAccount>().AsNoTracking().Where(a => a.UserId == id)
                .OrderByDescending(a => a.IsActive).ThenBy(a => a.Platform).ToListAsync(ct))
            .Select(a => new AdminSocialAccountDto(a.Id, a.Platform, a.Handle, a.ProfileUrl, a.AccountCreatedAt,
                Math.Max(a.AccountAgeDays(now), 0), a.FollowerCount, a.VerificationStatus, a.IsActive))
            .ToList();

        var counts = (await db.Set<Submission>().AsNoTracking().Where(s => s.UserId == id)
                .GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Count);
        int C(SubmissionStatus s) => counts.TryGetValue(s, out var c) ? c : 0;

        var earnings = (await db.Set<EarningEntry>().AsNoTracking().Where(e => e.UserId == id)
                .GroupBy(e => new { e.Status, e.SettlementCurrency })
                .Select(g => new { g.Key.Status, g.Key.SettlementCurrency, Amount = g.Sum(e => e.SettlementAmount), Count = g.Count() })
                .ToListAsync(ct))
            .OrderBy(e => e.Status).ThenBy(e => e.SettlementCurrency)
            .Select(e => new EarningTotalDto(e.Status, e.SettlementCurrency, Money.Round(e.Amount, e.SettlementCurrency), e.Count))
            .ToList();

        var holds = await db.Set<PayoutHold>().AsNoTracking().Where(h => h.UserId == id && h.ReleasedAt == null)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new PayoutHoldDto(h.Id, h.Reason, h.CreatedAt, h.CreatedByUserId)).ToListAsync(ct);

        var payout = await db.Set<PayoutProfile>().AsNoTracking().Where(p => p.UserId == id)
            .Select(p => new PayoutProfileSummaryDto(p.Method, p.MaskedDestination, p.PreferredCurrency, p.UpdatedAt)).FirstOrDefaultAsync(ct);

        // Actor-side entries and network details are audit-log data: only for callers who may read the audit log.
        var canViewAudit = currentUser.HasPermission(Permissions.AuditView);
        var recent = await auditLogs.RecentForUserAsync(id, 20, includeActorEntries: canViewAudit, includeNetworkDetails: canViewAudit, ct);

        return new AdminUserDetailDto(
            ToProfile(user),
            user.Roles.Select(r => r.Role).OrderBy(r => r).ToList(),
            history,
            accounts,
            new SubmissionCountsDto(counts.Values.Sum(), C(SubmissionStatus.Pending), C(SubmissionStatus.UnderReview),
                C(SubmissionStatus.Approved), C(SubmissionStatus.NeedsCorrection), C(SubmissionStatus.Rejected), C(SubmissionStatus.Reversed)),
            earnings,
            holds,
            payout,
            recent,
            user.ConcurrencyStamp,
            await customRoles.AssignedAsync(id, ct));
    }

    // ---------- Status ----------

    public async Task<AdminUserDetailDto> SuspendAsync(Guid id, SuspendUserRequest request, CancellationToken ct)
    {
        RequireConfirm(request.Confirm);
        if (id == currentUser.Id)
            throw DomainException.Forbidden("admin.cannot_suspend_self", "You can't suspend your own account.");
        // Serialize with other suspensions and role changes (same lock as SetRolesAsync) so two concurrent suspensions or
        // a suspension racing a demotion can't both pass the "last active admin" check. The lock is taken before the
        // transaction starts, so the reads below see the latest committed roles and statuses.
        await using var adminLock = await AcquireAdminLockAsync(ct);
        await using var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct);
        var activeAdminIds = await ActiveAdminIdsAsync(ct);

        var user = await db.LoadUserAsync(id, ct);
        RequireAdminForStaffTarget(user);
        if (user.Status == UserStatus.Suspended)
            throw DomainException.Conflict("admin.already_suspended", "This account is already suspended.");
        if (user.Roles.Any(r => r.Role == Role.Admin) && !activeAdminIds.Any(a => a != id))
            throw DomainException.Conflict("admin.last_admin", "At least one active administrator must remain.");

        var before = new { user.Status, user.StatusReason };
        user.Status = UserStatus.Suspended;
        user.StatusReason = request.Reason.Trim();
        user.StatusChangedAt = Now;
        // Bumps SecurityVersion: outstanding access tokens stop working on the next request; refresh tokens are revoked.
        await auth.RevokeAllSessionsAsync(user, "suspended", ct);
        audit.Record("admin.user_suspended", nameof(User), user.Id, before, new { user.Status, user.StatusReason }, user.StatusReason);
        await notifications.StageAsync(new NotificationRequest(user.Id, NotificationTypes.AccountStatusChanged,
            "Your Optimize All account has been suspended",
            $"Your account has been suspended. Reason: {user.StatusReason} If you believe this is a mistake, reply to this email or contact support.",
            null, new[] { NotificationChannel.Email }), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AdminUserDetailDto> ReactivateAsync(Guid id, ReactivateUserRequest request, CancellationToken ct)
    {
        var user = await db.LoadUserAsync(id, ct);
        RequireAdminForStaffTarget(user);
        if (user.Status == UserStatus.Active)
            throw DomainException.Conflict("admin.already_active", "This account is already active.");

        var before = new { user.Status, user.StatusReason };
        user.Status = UserStatus.Active;
        user.StatusReason = null;
        user.StatusChangedAt = Now;
        audit.Record("admin.user_reactivated", nameof(User), user.Id, before, new { user.Status }, request.Reason.Trim());
        await notifications.StageAsync(new NotificationRequest(user.Id, NotificationTypes.AccountStatusChanged,
            "Your Optimize All account is active again",
            "Your account has been reactivated. You can sign in and continue taking part in campaigns.",
            AppLinks.ParticipantHome, new[] { NotificationChannel.Email }), ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ---------- Roles / tier ----------

    public async Task<AdminUserDetailDto> SetRolesAsync(Guid id, SetRolesRequest request, CancellationToken ct)
    {
        RequireConfirm(request.Confirm);
        if (request.Roles.Any(r => !Enum.IsDefined(r)))
            throw FieldRules.FieldError("admin.invalid_role", "roles", "Unknown role.");
        var newRoles = request.Roles.Distinct().OrderBy(r => r).ToList();

        // Serialize with other role changes and suspensions so two concurrent demotions can't both pass the "last admin"
        // check (lock before the transaction, so the reads below see the latest committed state).
        await using var adminLock = await AcquireAdminLockAsync(ct);
        await using var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct);
        var adminIds = await db.Set<UserRole>().Where(r => r.Role == Role.Admin).Select(r => r.UserId).ToListAsync(ct);

        var user = await db.LoadUserAsync(id, ct);
        var oldRoles = user.Roles.Select(r => r.Role).OrderBy(r => r).ToList();
        var removingAdmin = oldRoles.Contains(Role.Admin) && !newRoles.Contains(Role.Admin);
        if (removingAdmin && id == currentUser.Id)
            throw DomainException.Forbidden("admin.cannot_remove_own_admin", "You can't remove your own Admin role.");
        if (removingAdmin)
        {
            var otherActiveAdmins = await db.Set<User>().CountAsync(u => adminIds.Contains(u.Id) && u.Id != id && u.Status == UserStatus.Active, ct);
            if (otherActiveAdmins == 0)
                throw DomainException.Conflict("admin.last_admin", "At least one active administrator must remain.");
        }

        if (!oldRoles.SequenceEqual(newRoles))
        {
            var now = Now;
            user.Roles.RemoveAll(r => !newRoles.Contains(r.Role));
            foreach (var role in newRoles.Where(r => !oldRoles.Contains(r)))
                user.Roles.Add(new UserRole { UserId = user.Id, Role = role, GrantedAt = now, GrantedByUserId = currentUser.Id });
            ConcurrencyGuard.Touch(db, user);
            // Permissions are carried in access tokens: force re-authentication so the new role set applies everywhere.
            await auth.RevokeAllSessionsAsync(user, "roles_changed", ct);
            audit.Record("admin.user_roles_changed", nameof(User), user.Id, new { roles = oldRoles }, new { roles = newRoles }, request.Reason.Trim());
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AdminUserDetailDto> SetTierAsync(Guid id, SetTierRequest request, CancellationToken ct)
    {
        var tier = request.Tier!.Value;
        if (!Enum.IsDefined(tier)) throw FieldRules.FieldError("admin.invalid_tier", "tier", "Unknown tier.");
        var user = await db.LoadUserAsync(id, ct);
        if (user.Tier != tier)
        {
            var before = user.Tier;
            user.Tier = tier;
            audit.Record("admin.user_tier_changed", nameof(User), user.Id, new { tier = before }, new { tier }, request.Reason.Trim());
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    // ---------- Staff creation ----------

    public async Task<AdminUserDetailDto> CreateStaffAsync(CreateStaffRequest request, CancellationToken ct)
    {
        var roles = request.Roles.Distinct().ToList();
        if (roles.Any(r => !Enum.IsDefined(r)))
            throw FieldRules.FieldError("admin.invalid_role", "roles", "Unknown role.");
        if (!roles.Any(r => r != Role.Participant))
            throw FieldRules.FieldError("admin.staff_role_required", "roles", "Choose at least one staff role (Reviewer, CampaignManager, Finance or Admin).");

        var email = request.Email.Trim();
        var normalized = Normalization.Email(email);
        if (await db.Set<User>().AnyAsync(u => u.NormalizedEmail == normalized, ct))
            throw DomainException.Conflict("admin.email_exists", "An account with this email already exists. Change its roles instead.");

        var now = Now;
        var user = new User
        {
            Email = email,
            NormalizedEmail = normalized,
            DisplayName = request.DisplayName.Trim(),
            CountryCode = request.CountryCode.Trim().ToUpperInvariant(),
            EmailVerifiedAt = now,
            ReferralCode = await GenerateReferralCodeAsync(ct),
        };
        // Unusable random password: the staff member sets their own through the reset link emailed below.
        user.PasswordHash = hasher.HashPassword(user, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        foreach (var role in roles.OrderBy(r => r))
            user.Roles.Add(new UserRole { UserId = user.Id, Role = role, GrantedAt = now, GrantedByUserId = currentUser.Id });
        db.Set<User>().Add(user);
        audit.Record("admin.staff_created", nameof(User), user.Id, after: new { user.Email, user.DisplayName, user.CountryCode, roles });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (Common.Errors.ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("admin.email_exists", "An account with this email already exists. Change its roles instead.");
        }

        await auth.ForgotPasswordAsync(user.Email, ct);
        return await GetAsync(user.Id, ct);
    }

    // ---------- Helpers ----------

    /// <summary>The "last active admin" lock shared by suspensions and role changes.</summary>
    private async Task<IAsyncDisposable> AcquireAdminLockAsync(CancellationToken ct)
    {
        try
        {
            return await db.Dialect().AcquireNamedLockAsync(db, "admin-roles", TimeSpan.FromSeconds(30), ct);
        }
        catch (TimeoutException)
        {
            throw DomainException.Conflict("admin.busy", "Another administrator change is in progress. Try again in a moment.");
        }
    }

    private Task<List<Guid>> ActiveAdminIdsAsync(CancellationToken ct) =>
        (from u in db.Set<User>()
         join r in db.Set<UserRole>() on u.Id equals r.UserId
         where r.Role == Role.Admin && u.Status == UserStatus.Active
         select u.Id).ToListAsync(ct);

    private void RequireAdminForStaffTarget(User target)
    {
        if (target.Roles.Any(r => r.Role != Role.Participant) && !currentUser.Roles.Contains(Role.Admin))
            throw DomainException.Forbidden("admin.staff_requires_admin", "Only administrators can change the status of staff accounts.");
    }

    private static void RequireConfirm(bool confirm)
    {
        if (!confirm)
            throw FieldRules.FieldError("admin.confirmation_required", "confirm", "Confirm this sensitive action by sending \"confirm\": true.");
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

    private static AdminUserListItemDto ToListItem(User u) => new(u.Id, u.Email, u.DisplayName, u.CountryCode, u.Status, u.Tier,
        u.Roles.Select(r => r.Role).OrderBy(r => r).ToList(), u.IsEmailVerified, u.CreatedAt, u.LastActiveAt, u.IsTestAccount);

    private static AdminUserProfileDto ToProfile(User u) => new(u.Id, u.Email, u.DisplayName, u.CountryCode, u.LanguageCode, u.TimeZone,
        u.Interests, u.Status, u.StatusReason, u.StatusChangedAt, u.Tier, u.ReferralCode, u.IsEmailVerified, u.EmailVerifiedAt,
        u.MarketingEmailOptIn, u.WhatsAppOptIn, u.WhatsAppNumber is null ? null : FieldRules.MaskTail(u.WhatsAppNumber),
        u.LastLoginAt, u.LastActiveAt, u.CreatedAt, u.IsTestAccount);
}
