using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Errors;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Admin.Roles;

/// <summary>
/// Custom roles (dynamic RBAC): CRUD, assignment and the permission catalog. Every change is audited with before/after
/// snapshots and bumps the affected users' <see cref="User.PermissionVersion"/> in the same transaction, so the new
/// permission set applies to their next request without re-login (see <see cref="IPermissionResolver"/>).
/// </summary>
public sealed class AdminRolesService(
    AppDbContext db,
    ICurrentUser currentUser,
    IPermissionResolver resolver,
    CustomRolePermissionCache cache,
    IAuditLogger audit,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private bool ActorIsAdmin => currentUser.Roles.Contains(Role.Admin);
    private IReadOnlySet<string> ActorPermissions => currentUser.Permissions;

    // ------------------------------------------------------------------ read

    public async Task<RolesOverviewDto> OverviewAsync(CancellationToken ct)
    {
        var builtInCounts = await db.Set<UserRole>().AsNoTracking().GroupBy(r => r.Role)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var builtIn = Enum.GetValues<Role>()
            .Select(r => new BuiltInRoleDto(r.ToString(), CustomRoleGuardrails.Humanize(r.ToString()),
                RolePermissions.For(r).OrderBy(p => p, StringComparer.Ordinal).ToList(), builtInCounts.GetValueOrDefault(r)))
            .ToList();

        var roles = await db.Set<CustomRole>().AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        return new RolesOverviewDto(builtIn, await ToDtosAsync(roles, ct));
    }

    public PermissionCatalogDto Catalog()
    {
        var granted = ActorPermissions;
        var isAdmin = ActorIsAdmin;
        var areas = PermissionCatalog.Areas()
            .Select(a => new PermissionAreaDto(a.Area, a.Permissions
                .Select(p => new PermissionInfoDto(p.Key, p.Label, p.Description, p.Sensitive,
                    PermissionCatalog.AdminOnlyGrants.Contains(p.Key),
                    CustomRoleGuardrails.CanGrant(granted, isAdmin, new[] { p.Key })))
                .ToList()))
            .ToList();
        return new PermissionCatalogDto(areas, isAdmin);
    }

    public async Task<CustomRoleDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var role = await db.Set<CustomRole>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct)
                   ?? throw DomainException.NotFound("CustomRole");
        var holders = await (from a in db.Set<UserCustomRole>().AsNoTracking()
                             join u in db.Set<User>().AsNoTracking() on a.UserId equals u.Id
                             where a.CustomRoleId == id
                             orderby u.DisplayName
                             select new { u.Id, u.DisplayName, u.Email, u.Status, a.AssignedAt }).ToListAsync(ct);
        return new CustomRoleDetailDto((await ToDtosAsync(new[] { role }, ct))[0],
            holders.Select(h => new RoleHolderDto(h.Id, h.DisplayName, h.Email, h.Status.ToString(), h.AssignedAt)).ToList());
    }

    public async Task<UserCustomRolesDto> UserRolesAsync(Guid userId, CancellationToken ct)
    {
        var roles = await db.Set<UserRole>().AsNoTracking().Where(r => r.UserId == userId).Select(r => r.Role).ToListAsync(ct);
        if (roles.Count == 0 && !await db.Set<User>().AnyAsync(u => u.Id == userId, ct)) throw DomainException.NotFound("User");
        var assigned = await AssignedAsync(userId, ct);
        var effective = await resolver.ForUserAsync(userId, roles, ct);
        return new UserCustomRolesDto(userId, assigned, effective.OrderBy(p => p, StringComparer.Ordinal).ToList());
    }

    /// <summary>Custom roles assigned to a user (for the admin user detail page).</summary>
    public Task<List<AssignedCustomRoleDto>> AssignedAsync(Guid userId, CancellationToken ct) =>
        (from a in db.Set<UserCustomRole>().AsNoTracking()
         join r in db.Set<CustomRole>().AsNoTracking() on a.CustomRoleId equals r.Id
         where a.UserId == userId
         orderby r.Name
         select new AssignedCustomRoleDto(r.Id, r.Name, a.AssignedAt)).ToListAsync(ct);

    // ------------------------------------------------------------------ create / update / delete

    public async Task<CustomRoleDetailDto> CreateAsync(SaveCustomRoleRequest request, CancellationToken ct)
    {
        var name = CustomRoleGuardrails.ValidateName(request.Name);
        var permissions = CustomRoleGuardrails.NormalizePermissions(request.Permissions);
        CustomRoleGuardrails.EnsureCanGrant(ActorPermissions, ActorIsAdmin, permissions);

        var role = new CustomRole
        {
            Name = name,
            NormalizedName = CustomRole.Normalize(name),
            Description = Clean(request.Description),
            Permissions = permissions,
            CreatedByUserId = currentUser.Id,
            UpdatedByUserId = currentUser.Id,
        };
        if (await db.Set<CustomRole>().AnyAsync(r => r.NormalizedName == role.NormalizedName, ct)) throw NameTaken();
        db.Set<CustomRole>().Add(role);
        audit.Record("admin.custom_role_created", nameof(CustomRole), role.Id, after: Snapshot(role));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            throw NameTaken();
        }
        return await GetAsync(role.Id, ct);
    }

    public async Task<CustomRoleDetailDto> UpdateAsync(Guid id, SaveCustomRoleRequest request, CancellationToken ct)
    {
        if (request.ConcurrencyStamp is not { } stamp)
            throw FieldRules.FieldError("roles.stamp_required", "concurrencyStamp", "Send the concurrency stamp you last saw.");
        var name = CustomRoleGuardrails.ValidateName(request.Name);
        var permissions = CustomRoleGuardrails.NormalizePermissions(request.Permissions);

        List<Guid> holders;
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct))
        {
            var role = await db.Set<CustomRole>().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw DomainException.NotFound("CustomRole");
            EnsureEditable(role);
            ConcurrencyGuard.Apply(db, role, stamp);
            // Editing a role that holds permissions the actor lacks would let them change (or strip) someone else's grants.
            CustomRoleGuardrails.EnsureCanGrant(ActorPermissions, ActorIsAdmin, role.Permissions.Concat(permissions).Distinct());

            var normalized = CustomRole.Normalize(name);
            if (normalized != role.NormalizedName && await db.Set<CustomRole>().AnyAsync(r => r.NormalizedName == normalized && r.Id != id, ct))
                throw NameTaken();

            holders = await HolderIdsAsync(id, ct);
            await EnsureNoClientStaffMixAsync(holders, permissions, excludingRoleId: id, ct);

            var before = Snapshot(role);
            role.Name = name;
            role.NormalizedName = normalized;
            role.Description = Clean(request.Description);
            role.Permissions = permissions;
            role.UpdatedByUserId = currentUser.Id;
            audit.Record("admin.custom_role_updated", nameof(CustomRole), role.Id, before, Snapshot(role));
            if (!before.Permissions.SequenceEqual(permissions)) await BumpPermissionVersionAsync(holders, ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex))
            {
                throw NameTaken();
            }
            await tx.CommitAsync(ct);
        }
        cache.Invalidate(holders);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, DeleteCustomRoleQuery query, CancellationToken ct)
    {
        List<Guid> holders;
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct))
        {
            // Row lock: serializes with concurrent assignments of this role (they lock it too).
            if (!await db.Dialect().LockRowAsync(db, "custom_roles", id, ct)) throw DomainException.NotFound("CustomRole");
            var role = await db.Set<CustomRole>().FirstAsync(r => r.Id == id, ct);
            EnsureEditable(role);
            CustomRoleGuardrails.EnsureCanGrant(ActorPermissions, ActorIsAdmin, role.Permissions);

            holders = await HolderIdsAsync(id, ct);
            if (holders.Count > 0 && !query.Confirm)
                throw new DomainException("roles.in_use",
                    $"This role is assigned to {holders.Count} user(s). Confirm to remove it from them" +
                    (query.ReassignTo is null ? "" : " and move them to the other role") + ".", DomainErrorKind.Conflict,
                    new Dictionary<string, string[]> { ["holders"] = new[] { holders.Count.ToString() } });

            CustomRole? target = null;
            if (query.ReassignTo is { } targetId)
            {
                if (targetId == id) throw FieldRules.FieldError("roles.invalid_reassignment", "reassignTo", "Choose a different role.");
                if (!await db.Dialect().LockRowAsync(db, "custom_roles", targetId, ct))
                    throw FieldRules.FieldError("roles.invalid_reassignment", "reassignTo", "The replacement role does not exist.");
                target = await db.Set<CustomRole>().FirstAsync(r => r.Id == targetId, ct);
                CustomRoleGuardrails.EnsureCanGrant(ActorPermissions, ActorIsAdmin, target.Permissions);
                var alreadyHolding = await HolderIdsAsync(targetId, ct);
                var moving = holders.Except(alreadyHolding).ToList();
                await EnsureNoClientStaffMixAsync(moving, target.Permissions, excludingRoleId: id, ct);
                var now = Now;
                foreach (var userId in moving)
                    db.Set<UserCustomRole>().Add(new UserCustomRole { UserId = userId, CustomRoleId = targetId, AssignedAt = now, AssignedByUserId = currentUser.Id });
            }

            audit.Record("admin.custom_role_deleted", nameof(CustomRole), role.Id,
                new { role = Snapshot(role), holders },
                target is null ? null : new { reassignedTo = new { target.Id, target.Name }, holders });
            await BumpPermissionVersionAsync(holders, ct);
            await db.Set<UserCustomRole>().Where(a => a.CustomRoleId == id).ExecuteDeleteAsync(ct);
            db.Set<CustomRole>().Remove(role);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        cache.Invalidate(holders);
    }

    // ------------------------------------------------------------------ assignment

    public async Task<UserCustomRolesDto> AssignAsync(Guid id, Guid userId, CancellationToken ct)
    {
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct))
        {
            if (!await db.Dialect().LockRowAsync(db, "custom_roles", id, ct)) throw DomainException.NotFound("CustomRole");
            var role = await db.Set<CustomRole>().AsNoTracking().FirstAsync(r => r.Id == id, ct);
            if (!await db.Set<User>().AnyAsync(u => u.Id == userId, ct)) throw DomainException.NotFound("User");
            CustomRoleGuardrails.EnsureCanGrant(ActorPermissions, ActorIsAdmin, role.Permissions);

            if (!await db.Set<UserCustomRole>().AnyAsync(a => a.UserId == userId && a.CustomRoleId == id, ct))
            {
                await EnsureNoClientStaffMixAsync(new[] { userId }, role.Permissions, excludingRoleId: null, ct);
                var before = await AssignedNamesAsync(userId, ct);
                db.Set<UserCustomRole>().Add(new UserCustomRole { UserId = userId, CustomRoleId = id, AssignedAt = Now, AssignedByUserId = currentUser.Id });
                audit.Record("admin.custom_role_assigned", nameof(User), userId,
                    new { customRoles = before }, new { customRoles = before.Append(role.Name).OrderBy(n => n).ToList(), role = new { role.Id, role.Name } });
                await BumpPermissionVersionAsync(new[] { userId }, ct);
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        cache.Invalidate(new[] { userId });
        return await UserRolesAsync(userId, ct);
    }

    public async Task<UserCustomRolesDto> UnassignAsync(Guid id, Guid userId, CancellationToken ct)
    {
        await using (var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct))
        {
            if (!await db.Dialect().LockRowAsync(db, "custom_roles", id, ct)) throw DomainException.NotFound("CustomRole");
            var role = await db.Set<CustomRole>().AsNoTracking().FirstAsync(r => r.Id == id, ct);
            CustomRoleGuardrails.EnsureCanGrant(ActorPermissions, ActorIsAdmin, role.Permissions);

            var assignment = await db.Set<UserCustomRole>().FirstOrDefaultAsync(a => a.UserId == userId && a.CustomRoleId == id, ct);
            if (assignment is not null)
            {
                var before = await AssignedNamesAsync(userId, ct);
                db.Set<UserCustomRole>().Remove(assignment);
                audit.Record("admin.custom_role_unassigned", nameof(User), userId,
                    new { customRoles = before }, new { customRoles = before.Where(n => n != role.Name).ToList(), role = new { role.Id, role.Name } });
                await BumpPermissionVersionAsync(new[] { userId }, ct);
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        cache.Invalidate(new[] { userId });
        return await UserRolesAsync(userId, ct);
    }

    // ------------------------------------------------------------------ helpers

    private sealed record RoleSnapshot(string Name, string? Description, List<string> Permissions);

    private static RoleSnapshot Snapshot(CustomRole r) => new(r.Name, r.Description, r.Permissions.ToList());

    private static string? Clean(string? description) => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static DomainException NameTaken() =>
        new("roles.name_taken", "A role with this name already exists.", DomainErrorKind.Conflict,
            new Dictionary<string, string[]> { ["name"] = new[] { "A role with this name already exists." } });

    private static void EnsureEditable(CustomRole role)
    {
        if (role.IsSystem)
            throw DomainException.Forbidden("roles.system_role", "System roles can't be edited or deleted.");
    }

    private Task<List<Guid>> HolderIdsAsync(Guid roleId, CancellationToken ct) =>
        db.Set<UserCustomRole>().Where(a => a.CustomRoleId == roleId).Select(a => a.UserId).ToListAsync(ct);

    private Task<List<string>> AssignedNamesAsync(Guid userId, CancellationToken ct) =>
        (from a in db.Set<UserCustomRole>()
         join r in db.Set<CustomRole>() on a.CustomRoleId equals r.Id
         where a.UserId == userId
         orderby r.Name
         select r.Name).ToListAsync(ct);

    /// <summary>
    /// A user's effective permissions must never contain client.portal together with staff permissions (client users are
    /// tenant-scoped; staff see every client). Checks each user's built-in roles + other custom roles + the new set.
    /// </summary>
    private async Task EnsureNoClientStaffMixAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<string> added,
        Guid? excludingRoleId, CancellationToken ct)
    {
        if (userIds.Count == 0) return;
        var ids = userIds.ToList();
        var builtIn = await db.Set<UserRole>().AsNoTracking().Where(r => ids.Contains(r.UserId))
            .Select(r => new { r.UserId, r.Role }).ToListAsync(ct);
        var custom = await (from a in db.Set<UserCustomRole>().AsNoTracking()
                            join r in db.Set<CustomRole>().AsNoTracking() on a.CustomRoleId equals r.Id
                            where ids.Contains(a.UserId) && (excludingRoleId == null || a.CustomRoleId != excludingRoleId)
                            select new { a.UserId, r.Permissions }).ToListAsync(ct);
        foreach (var userId in ids)
        {
            var effective = new HashSet<string>(RolePermissions.For(builtIn.Where(b => b.UserId == userId).Select(b => b.Role)));
            foreach (var c in custom.Where(c => c.UserId == userId)) effective.UnionWith(c.Permissions);
            effective.UnionWith(added);
            if (CustomRoleGuardrails.MixesClientAndStaff(effective))
                throw DomainException.Conflict("roles.client_staff_conflict",
                    "A user can't hold the client portal permission together with staff permissions.");
        }
    }

    private async Task BumpPermissionVersionAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return;
        var ids = userIds.ToList();
        await db.Set<User>().Where(u => ids.Contains(u.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PermissionVersion, u => u.PermissionVersion + 1), ct);
    }

    private async Task<List<CustomRoleDto>> ToDtosAsync(IReadOnlyCollection<CustomRole> roles, CancellationToken ct)
    {
        var ids = roles.Select(r => r.Id).ToList();
        var counts = await db.Set<UserCustomRole>().AsNoTracking().Where(a => ids.Contains(a.CustomRoleId))
            .GroupBy(a => a.CustomRoleId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var creatorIds = roles.Where(r => r.CreatedByUserId != null).Select(r => r.CreatedByUserId!.Value).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => creatorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName }).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var granted = ActorPermissions;
        var isAdmin = ActorIsAdmin;
        return roles.Select(r => new CustomRoleDto(r.Id, r.Name, r.Description, r.Permissions, r.IsSystem, counts.GetValueOrDefault(r.Id),
                r.CreatedAt, r.CreatedByUserId, r.CreatedByUserId is { } c ? names.GetValueOrDefault(c) : null, r.UpdatedAt,
                r.ConcurrencyStamp, !r.IsSystem && CustomRoleGuardrails.CanGrant(granted, isAdmin, r.Permissions)))
            .ToList();
    }
}
