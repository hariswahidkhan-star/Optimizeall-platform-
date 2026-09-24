using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Security;

/// <summary>
/// Effective permissions = built-in role permissions (<see cref="RolePermissions"/>) ∪ permissions of the user's custom
/// roles. The single source for authorization (<see cref="PermissionHandler"/>), <see cref="ICurrentUser.HasPermission"/>
/// (and therefore <see cref="IClientScope.IsStaff"/>) and the session DTO sent to the web app.
/// </summary>
/// <remarks>
/// Built-in roles come from the access token (changing them bumps <c>SecurityVersion</c> and ends sessions). Custom-role
/// permissions are read per request: the JWT validation step loads the user's <c>PermissionVersion</c> together with the
/// security version, and the custom-role permission set is cached per instance keyed by (user id, permission version).
/// Every custom-role change bumps the affected users' version in the same transaction, so a change applies on the
/// next request on every instance without re-login.
/// </remarks>
public interface IPermissionResolver
{
    /// <summary>Effective permissions of an authenticated principal (empty when anonymous). Memoized per request.</summary>
    ValueTask<IReadOnlySet<string>> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>Synchronous variant for <see cref="ICurrentUser"/>; normally served from the per-request memo.</summary>
    IReadOnlySet<string> Resolve(ClaimsPrincipal principal);

    /// <summary>Effective permissions of any user from their built-in roles plus their custom roles in the database.</summary>
    Task<IReadOnlySet<string>> ForUserAsync(Guid userId, IEnumerable<Role> builtInRoles, CancellationToken ct = default);

    /// <summary>Synchronous variant of <see cref="ForUserAsync"/> (session DTOs built in synchronous code).</summary>
    IReadOnlySet<string> ForUser(Guid userId, IEnumerable<Role> builtInRoles);
}

/// <summary>Per-instance cache of custom-role permissions keyed by user id and <c>User.PermissionVersion</c>.</summary>
public sealed class CustomRolePermissionCache(TimeProvider clock)
{
    /// <summary>Entries are also dropped after this age so the cache stays small (correctness comes from the version key).</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);
    private const int MaxEntries = 20_000;

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    private sealed record Entry(int Version, IReadOnlySet<string> Permissions, DateTimeOffset LoadedAt);

    public bool TryGet(Guid userId, int version, out IReadOnlySet<string> permissions)
    {
        if (_entries.TryGetValue(userId, out var e) && e.Version == version && clock.GetUtcNow() - e.LoadedAt < Ttl)
        {
            permissions = e.Permissions;
            return true;
        }
        permissions = EmptySet;
        return false;
    }

    public void Set(Guid userId, int version, IReadOnlySet<string> permissions)
    {
        if (_entries.Count >= MaxEntries) _entries.Clear();
        _entries[userId] = new Entry(version, permissions, clock.GetUtcNow());
    }

    public void Invalidate(IEnumerable<Guid> userIds)
    {
        foreach (var id in userIds) _entries.TryRemove(id, out _);
    }

    internal static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();
}

public sealed class PermissionResolver(AppDbContext db, CustomRolePermissionCache cache, IHttpContextAccessor accessor) : IPermissionResolver
{
    /// <summary>HttpContext item holding the caller's <c>User.PermissionVersion</c> (set during JWT validation).</summary>
    public const string PermissionVersionItem = "oa.permission_version";

    private readonly Dictionary<string, IReadOnlySet<string>> _memo = new(StringComparer.Ordinal);

    public async ValueTask<IReadOnlySet<string>> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        if (!TryIdentify(principal, out var userId, out var roles, out var key)) return CustomRolePermissionCache.EmptySet;
        if (_memo.TryGetValue(key, out var memo)) return memo;
        var custom = await CustomPermissionsAsync(userId, KnownVersion(userId), ct);
        return _memo[key] = Union(roles, custom);
    }

    public IReadOnlySet<string> Resolve(ClaimsPrincipal principal)
    {
        if (!TryIdentify(principal, out var userId, out var roles, out var key)) return CustomRolePermissionCache.EmptySet;
        if (_memo.TryGetValue(key, out var memo)) return memo;
        var custom = CustomPermissions(userId, KnownVersion(userId));
        return _memo[key] = Union(roles, custom);
    }

    public async Task<IReadOnlySet<string>> ForUserAsync(Guid userId, IEnumerable<Role> builtInRoles, CancellationToken ct = default) =>
        Union(builtInRoles, await CustomPermissionsAsync(userId, null, ct));

    public IReadOnlySet<string> ForUser(Guid userId, IEnumerable<Role> builtInRoles) =>
        Union(builtInRoles, CustomPermissions(userId, null));

    private static IReadOnlySet<string> Union(IEnumerable<Role> roles, IEnumerable<string> custom)
    {
        var set = new HashSet<string>(RolePermissions.For(roles), StringComparer.Ordinal);
        set.UnionWith(custom);
        return set;
    }

    private static bool TryIdentify(ClaimsPrincipal principal, out Guid userId, out Role[] roles, out string key)
    {
        userId = Guid.Empty;
        roles = Array.Empty<Role>();
        key = string.Empty;
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirst(AppClaims.UserId)?.Value, out userId)) return false;
        roles = ClaimsHelper.GetRoles(principal).Distinct().OrderBy(r => r).ToArray();
        key = userId.ToString("N") + ":" + string.Join(',', roles);
        return true;
    }

    /// <summary>The caller's permission version read during JWT validation of this request (only for that user).</summary>
    private int? KnownVersion(Guid userId)
    {
        var items = accessor.HttpContext?.Items;
        return items is not null && items.TryGetValue(PermissionVersionItem, out var v) && v is (Guid id, int version) && id == userId
            ? version
            : null;
    }

    private async Task<IReadOnlySet<string>> CustomPermissionsAsync(Guid userId, int? knownVersion, CancellationToken ct)
    {
        if (knownVersion is { } kv && cache.TryGet(userId, kv, out var cached)) return cached;
        // Read the version before the permissions: a change committed in between is cached under the older version at
        // worst, which the next request (seeing the new version) reloads.
        var version = knownVersion ?? await db.Set<User>().AsNoTracking().Where(u => u.Id == userId)
            .Select(u => (int?)u.PermissionVersion).FirstOrDefaultAsync(ct);
        if (version is null) return CustomRolePermissionCache.EmptySet;
        if (cache.TryGet(userId, version.Value, out cached)) return cached;
        var lists = await CustomRoleListsQuery(userId).ToListAsync(ct);
        return Store(userId, version.Value, lists);
    }

    private IReadOnlySet<string> CustomPermissions(Guid userId, int? knownVersion)
    {
        if (knownVersion is { } kv && cache.TryGet(userId, kv, out var cached)) return cached;
        var version = knownVersion ?? db.Set<User>().AsNoTracking().Where(u => u.Id == userId)
            .Select(u => (int?)u.PermissionVersion).FirstOrDefault();
        if (version is null) return CustomRolePermissionCache.EmptySet;
        if (cache.TryGet(userId, version.Value, out cached)) return cached;
        return Store(userId, version.Value, CustomRoleListsQuery(userId).ToList());
    }

    private IQueryable<List<string>> CustomRoleListsQuery(Guid userId) =>
        from a in db.Set<UserCustomRole>().AsNoTracking()
        join r in db.Set<CustomRole>().AsNoTracking() on a.CustomRoleId equals r.Id
        where a.UserId == userId
        select r.Permissions;

    private IReadOnlySet<string> Store(Guid userId, int version, IEnumerable<List<string>> lists)
    {
        // Only permissions that exist are honoured (a permission removed from the code stops applying).
        var known = KnownPermissions;
        var set = lists.SelectMany(l => l).Where(known.Contains).ToHashSet(StringComparer.Ordinal);
        cache.Set(userId, version, set);
        return set;
    }

    private static readonly HashSet<string> KnownPermissions = Permissions.All.ToHashSet(StringComparer.Ordinal);
}
