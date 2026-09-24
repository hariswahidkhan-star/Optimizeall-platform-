using System.ComponentModel.DataAnnotations;

namespace OptimizeAll.Api.Modules.Admin.Roles;

public sealed record BuiltInRoleDto(string Name, string Label, IReadOnlyList<string> Permissions, int UserCount);

/// <param name="CanManage">True when the caller may edit, delete, assign and unassign this role (see the guardrails).</param>
public sealed record CustomRoleDto(
    Guid Id, string Name, string? Description, IReadOnlyList<string> Permissions, bool IsSystem, int UserCount,
    DateTime CreatedAt, Guid? CreatedByUserId, string? CreatedByName, DateTime UpdatedAt, Guid ConcurrencyStamp, bool CanManage);

public sealed record RolesOverviewDto(IReadOnlyList<BuiltInRoleDto> BuiltIn, IReadOnlyList<CustomRoleDto> Custom);

public sealed record RoleHolderDto(Guid UserId, string DisplayName, string Email, string Status, DateTime AssignedAt);

public sealed record CustomRoleDetailDto(CustomRoleDto Role, IReadOnlyList<RoleHolderDto> Holders);

public sealed record PermissionInfoDto(string Key, string Label, string Description, bool Sensitive, bool AdminOnly, bool Granted);

public sealed record PermissionAreaDto(string Area, IReadOnlyList<PermissionInfoDto> Permissions);

/// <param name="CallerIsAdmin">Whether the caller holds the built-in Admin role (needed to grant admin-only permissions).</param>
public sealed record PermissionCatalogDto(IReadOnlyList<PermissionAreaDto> Areas, bool CallerIsAdmin);

public sealed record AssignedCustomRoleDto(Guid Id, string Name, DateTime AssignedAt);

public sealed record UserCustomRolesDto(Guid UserId, IReadOnlyList<AssignedCustomRoleDto> Roles, IReadOnlyList<string> EffectivePermissions);

public sealed class SaveCustomRoleRequest
{
    [Required, MinLength(2), MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required, MinLength(1), MaxLength(200)]
    public List<string> Permissions { get; set; } = new();

    /// <summary>Required on update: the stamp the client last saw (stale writes answer 409).</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class DeleteCustomRoleQuery
{
    /// <summary>Required when the role is still assigned: removes (or moves) every assignment.</summary>
    public bool Confirm { get; set; }

    /// <summary>Optional: move the current holders to this custom role before deleting.</summary>
    public Guid? ReassignTo { get; set; }
}
