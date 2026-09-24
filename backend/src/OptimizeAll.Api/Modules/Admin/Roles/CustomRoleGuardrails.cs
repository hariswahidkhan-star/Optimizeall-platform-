using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;

namespace OptimizeAll.Api.Modules.Admin.Roles;

/// <summary>
/// Pure rules for custom roles:
/// <list type="bullet">
/// <item>a role holds known permissions only, at least one;</item>
/// <item><c>client.portal</c> cannot be combined with staff permissions (in one role, or in one user's effective set);</item>
/// <item>nobody can grant (or edit, assign, unassign, delete a role holding) a permission they don't hold themselves;</item>
/// <item><see cref="PermissionCatalog.AdminOnlyGrants"/> (roles.manage, settings.manage, users.impersonate) require the
/// built-in Admin role.</item>
/// </list>
/// </summary>
public static class CustomRoleGuardrails
{
    public const int MaxNameLength = 80;

    public static string ValidateName(string? raw)
    {
        var name = (raw ?? string.Empty).Trim();
        if (name.Length < 2 || name.Length > MaxNameLength)
            throw FieldRules.FieldError("roles.invalid_name", "name", $"Use 2 to {MaxNameLength} characters.");
        if (Enum.GetNames<Role>().Any(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(Humanize(r), name, StringComparison.OrdinalIgnoreCase)))
            throw FieldRules.FieldError("roles.name_reserved", "name", "This name belongs to a built-in role.");
        return name;
    }

    /// <summary>Distinct, sorted, known permissions; rejects unknown ones, an empty set and client/staff mixing.</summary>
    public static List<string> NormalizePermissions(IEnumerable<string>? permissions)
    {
        var list = (permissions ?? Array.Empty<string>()).Select(p => (p ?? string.Empty).Trim()).Distinct(StringComparer.Ordinal).ToList();
        var known = Permissions.All.ToHashSet(StringComparer.Ordinal);
        var unknown = list.Where(p => !known.Contains(p)).ToList();
        if (unknown.Count > 0)
            throw FieldRules.FieldError("roles.unknown_permission", "permissions", $"Unknown permission(s): {string.Join(", ", unknown)}.");
        if (list.Count == 0)
            throw FieldRules.FieldError("roles.permissions_required", "permissions", "Choose at least one permission.");
        if (MixesClientAndStaff(list))
            throw FieldRules.FieldError("roles.client_portal_mixed", "permissions",
                "The client portal permission cannot be combined with staff permissions in one role.");
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    public static bool MixesClientAndStaff(IEnumerable<string> permissions)
    {
        var set = permissions as IReadOnlyCollection<string> ?? permissions.ToList();
        return set.Contains(Permissions.ClientPortal) && set.Any(PermissionCatalog.IsStaffPermission);
    }

    /// <summary>
    /// Throws 403 unless the actor may grant every permission: they must hold each one, and admin-only permissions
    /// additionally need the built-in Admin role.
    /// </summary>
    public static void EnsureCanGrant(IReadOnlySet<string> actorPermissions, bool actorIsAdmin, IEnumerable<string> permissions)
    {
        var list = permissions.ToList();
        var adminOnly = list.Where(PermissionCatalog.AdminOnlyGrants.Contains).ToList();
        if (adminOnly.Count > 0 && !actorIsAdmin)
            throw DomainException.Forbidden("roles.admin_only_permission",
                $"Only administrators can grant {string.Join(", ", adminOnly)}.");
        var missing = list.Where(p => !actorPermissions.Contains(p)).ToList();
        if (missing.Count > 0)
            throw DomainException.Forbidden("roles.cannot_grant_unheld",
                $"You can't grant permissions you don't hold yourself: {string.Join(", ", missing)}.");
    }

    public static bool CanGrant(IReadOnlySet<string> actorPermissions, bool actorIsAdmin, IEnumerable<string> permissions) =>
        permissions.All(p => actorPermissions.Contains(p) && (actorIsAdmin || !PermissionCatalog.AdminOnlyGrants.Contains(p)));

    public static string Humanize(string roleName) =>
        string.Concat(roleName.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
}
