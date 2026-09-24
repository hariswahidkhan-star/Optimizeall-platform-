using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Security;

/// <summary>
/// "Who holds permission X": the only way to select recipients, assignees or owners by permission. Includes users who
/// hold it through a built-in role or through a custom role. Never query <c>u.Roles</c> for this.
/// </summary>
public interface IPermissionDirectory
{
    /// <summary>
    /// Users (any status; add <c>u.Status == UserStatus.Active</c> as needed) whose built-in or custom roles grant the
    /// permission. The result is a composable, untracked query.
    /// </summary>
    Task<IQueryable<User>> UsersWithPermissionAsync(string permission, CancellationToken ct = default);

    /// <summary>Users holding at least one of the permissions.</summary>
    Task<IQueryable<User>> UsersWithAnyPermissionAsync(IReadOnlyCollection<string> permissions, CancellationToken ct = default);

    /// <summary>True when the user (any status) holds the permission through a built-in or custom role.</summary>
    Task<bool> UserHasPermissionAsync(Guid userId, string permission, CancellationToken ct = default);
}

public sealed class PermissionDirectory(AppDbContext db) : IPermissionDirectory
{
    public Task<IQueryable<User>> UsersWithPermissionAsync(string permission, CancellationToken ct = default) =>
        UsersWithAnyPermissionAsync(new[] { permission }, ct);

    public async Task<IQueryable<User>> UsersWithAnyPermissionAsync(IReadOnlyCollection<string> permissions, CancellationToken ct = default)
    {
        var roles = BuiltInRolesWithAny(permissions);
        var customRoleIds = await CustomRolesWithAnyAsync(permissions, ct);
        var users = db.Set<User>().AsNoTracking();
        if (customRoleIds.Count == 0)
            return users.Where(u => u.Roles.Any(r => roles.Contains(r.Role)));
        var assignments = db.Set<UserCustomRole>();
        return users.Where(u => u.Roles.Any(r => roles.Contains(r.Role)) ||
                                assignments.Any(a => a.UserId == u.Id && customRoleIds.Contains(a.CustomRoleId)));
    }

    public async Task<bool> UserHasPermissionAsync(Guid userId, string permission, CancellationToken ct = default) =>
        await (await UsersWithPermissionAsync(permission, ct)).AnyAsync(u => u.Id == userId, ct);

    public static List<Role> BuiltInRolesWithAny(IReadOnlyCollection<string> permissions) =>
        Enum.GetValues<Role>().Where(r => RolePermissions.For(r).Any(permissions.Contains)).ToList();

    private async Task<List<Guid>> CustomRolesWithAnyAsync(IReadOnlyCollection<string> permissions, CancellationToken ct)
    {
        // The custom_roles table is small and permission lists are JSON columns: filter in memory (portable).
        var all = await db.Set<CustomRole>().AsNoTracking().Select(r => new { r.Id, r.Permissions }).ToListAsync(ct);
        return all.Where(r => r.Permissions.Any(permissions.Contains)).Select(r => r.Id).ToList();
    }
}
