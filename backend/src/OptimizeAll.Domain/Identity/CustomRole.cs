using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Identity;

/// <summary>
/// An admin-defined bundle of permissions (dynamic RBAC). A user's effective permissions are the union of their built-in
/// <see cref="Role"/> permission sets and the permissions of every custom role assigned to them (<see cref="UserCustomRole"/>).
/// </summary>
public class CustomRole : AuditedEntity, IConcurrencyStamped
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Upper-cased, trimmed <see cref="Name"/>; unique (names are compared case-insensitively on every provider).</summary>
    public string NormalizedName { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Permission keys (see Api/Common/Security/Permissions), distinct and sorted.</summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>Reserved for platform-defined roles; system roles cannot be edited or deleted through the admin API.</summary>
    public bool IsSystem { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
}

/// <summary>Assignment of a <see cref="CustomRole"/> to a user.</summary>
public class UserCustomRole
{
    public Guid UserId { get; set; }
    public Guid CustomRoleId { get; set; }
    public DateTime AssignedAt { get; set; }
    public Guid? AssignedByUserId { get; set; }
}
