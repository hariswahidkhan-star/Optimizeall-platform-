using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Hosting;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Auth;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Admin;

public sealed class CreateTestUserRequest
{
    [Required, MinLength(1), MaxLength(5)]
    public List<Role> Roles { get; set; } = new();

    /// <summary>Optional; defaults to "Test &lt;role&gt;".</summary>
    [MaxLength(80)]
    public string? DisplayName { get; set; }

    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter country code.")]
    public string? CountryCode { get; set; }

    /// <summary>Client role only: the client organization the test user belongs to.</summary>
    public Guid? ClientAccountId { get; set; }

    public ClientMemberRole? ClientMemberRole { get; set; }
}

/// <summary>The generated password is returned exactly once, in this response; it is never stored in clear.</summary>
public sealed record CreatedTestUserDto(Guid Id, string Email, string DisplayName, IReadOnlyList<Role> Roles, string Password,
    Guid? ClientAccountId);

/// <summary>
/// Test users (QA and demos): created only through this service with <see cref="User.IsTestAccount"/> set, verified,
/// with a generated password shown once. Never paid (payout batches exclude them), left out of analytics and marketing
/// KPIs, and labelled "TEST" in the admin UI. The flag can't be changed later by any endpoint.
/// </summary>
public sealed class TestUsersService(
    AppDbContext db,
    ICurrentUser currentUser,
    IAuthService auth,
    IAuditLogger audit,
    IPasswordHasher<User> hasher,
    IOptions<TestAccountOptions> options,
    TimeProvider clock)
{
    private const string ReferralAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<CreatedTestUserDto> CreateAsync(CreateTestUserRequest request, CancellationToken ct)
    {
        var roles = request.Roles.Distinct().OrderBy(r => r).ToList();
        if (roles.Any(r => !Enum.IsDefined(r)))
            throw FieldRules.FieldError("admin.invalid_role", "roles", "Unknown role.");
        // Staff roles are a privilege grant: same permission as creating staff.
        if (roles.Any(r => r is not (Role.Participant or Role.Client)) && !currentUser.HasPermission(Permissions.RolesAssign))
            throw DomainException.Forbidden("auth.forbidden", "Creating staff test users requires roles.assign.");
        // A test user is a real, usable account whose password the creator receives: the same guardrail as granting the
        // roles to anyone (hold every staff permission yourself; Admin only by an admin), and no client/staff mixing.
        Roles.CustomRoleGuardrails.EnsureCanGrant(currentUser.Permissions, currentUser.Roles.Contains(Role.Admin),
            RolePermissions.For(roles).Where(PermissionCatalog.IsStaffPermission));
        if (Roles.CustomRoleGuardrails.MixesClientAndStaff(RolePermissions.For(roles)))
            throw DomainException.Conflict("roles.client_staff_conflict",
                "A user can't hold the client portal permission together with staff permissions.");

        ClientAccount? client = null;
        if (request.ClientAccountId is { } clientId)
        {
            if (!roles.Contains(Role.Client))
                throw FieldRules.FieldError("admin.client_role_required", "clientAccountId", "Only Client test users can belong to a client organization.");
            client = await db.Set<ClientAccount>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId, ct)
                     ?? throw FieldRules.FieldError("admin.client_not_found", "clientAccountId", "Client organization not found.");
        }

        var label = string.IsNullOrWhiteSpace(request.DisplayName) ? roles[0].ToString() : request.DisplayName.Trim();
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? $"Test {roles[0]}" : request.DisplayName.Trim();
        var now = Now;
        User? user = null;
        string password = string.Empty;
        for (var attempt = 0; attempt < 5 && user is null; attempt++)
        {
            var email = TestAccounts.GenerateEmail(label, options.Value.EmailDomain);
            var normalized = Normalization.Email(email);
            if (await db.Set<User>().AnyAsync(u => u.NormalizedEmail == normalized, ct)) continue;
            password = TestAccounts.GeneratePassword();
            PasswordPolicy.Validate(password, email);
            user = new User
            {
                Email = email,
                NormalizedEmail = normalized,
                DisplayName = displayName,
                CountryCode = (request.CountryCode ?? "US").ToUpperInvariant(),
                EmailVerifiedAt = now,
                ReferralCode = await GenerateReferralCodeAsync(ct),
                IsTestAccount = true,
            };
        }
        if (user is null) throw DomainException.Conflict("admin.test_user_email_taken", "Could not allocate a unique test email. Try again.");

        user.PasswordHash = hasher.HashPassword(user, password);
        foreach (var role in roles)
            user.Roles.Add(new UserRole { UserId = user.Id, Role = role, GrantedAt = now, GrantedByUserId = currentUser.Id });
        db.Set<User>().Add(user);
        if (client is not null)
            db.Set<ClientMember>().Add(new ClientMember
            {
                ClientAccountId = client.Id, UserId = user.Id, Role = request.ClientMemberRole ?? ClientMemberRole.Viewer,
                AddedAt = now, AddedByUserId = currentUser.Id,
            });
        audit.Record("admin.test_user_created", nameof(User), user.Id,
            after: new { user.Email, user.DisplayName, roles, user.IsTestAccount, clientAccountId = client?.Id });
        await db.SaveChangesAsync(ct);
        return new CreatedTestUserDto(user.Id, user.Email, user.DisplayName, roles, password, client?.Id);
    }

    /// <summary>
    /// "Deletes" a test user: deactivates it and signs it out everywhere (rows it created stay for referential and audit
    /// integrity; test data is excluded from payouts and KPIs anyway). Real accounts are refused.
    /// </summary>
    public async Task DeleteAsync(Guid id, string reason, CancellationToken ct)
    {
        var user = await db.Set<User>().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id, ct)
                   ?? throw DomainException.NotFound("User");
        if (!user.IsTestAccount)
            throw DomainException.Conflict("admin.not_test_account", "Only test accounts can be deleted here. Suspend real accounts instead.");
        if (id == currentUser.Id)
            throw DomainException.Forbidden("admin.cannot_delete_self", "You can't delete your own account.");
        if (user.Status == UserStatus.Deactivated) return;

        var before = new { user.Status };
        user.Status = UserStatus.Deactivated;
        user.StatusReason = reason;
        user.StatusChangedAt = Now;
        await auth.RevokeAllSessionsAsync(user, "test_user_deleted", ct);
        await db.Set<ImpersonationSession>().Where(s => s.TargetUserId == id && s.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, Now).SetProperty(x => x.EndedReason, "target_deleted"), ct);
        audit.Record("admin.test_user_deleted", nameof(User), user.Id, before, new { user.Status }, reason);
        await db.SaveChangesAsync(ct);
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
}
