using System.Reflection;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.IntegrationTests.ApiContract;

/// <summary>
/// Ids of existing rows by entity name (from the EF model: every entity with a single Guid key), optionally only the rows
/// of one client organization (<c>ClientAccountId</c>) and of its members (<c>UserId</c>), and the mapping from a route
/// parameter to the entity it names: <c>{projectId}</c> → Project, <c>invoices/{id}</c> → Invoice (by name containment).
/// </summary>
public sealed class IdIndex
{
    public Dictionary<string, List<Guid>> Ids { get; } = new();
    public Guid? ClientId { get; private init; }
    public List<Guid> MemberUserIds { get; private init; } = new();

    public IReadOnlySet<Guid> AllIds => _all ??= Ids.Values.SelectMany(v => v).Concat(ClientId is { } c ? new[] { c } : Array.Empty<Guid>()).ToHashSet();
    private HashSet<Guid>? _all;

    public static async Task<IdIndex> LoadAsync(AppDbContext db, Guid? clientId = null, int perType = 5)
    {
        var members = clientId is { } cid
            ? await db.Set<ClientMember>().Where(m => m.ClientAccountId == cid).Select(m => m.UserId).ToListAsync()
            : new List<Guid>();
        var index = new IdIndex { ClientId = clientId, MemberUserIds = members };
        foreach (var type in db.Model.GetEntityTypes().Where(t => !t.IsOwned() && t.FindPrimaryKey() is { Properties.Count: 1 } k && k.Properties[0].ClrType == typeof(Guid)))
        {
            var key = type.FindPrimaryKey()!.Properties[0].Name;
            var found = new List<Guid>();
            if (clientId is null)
            {
                found.AddRange(await QueryAsync(db, type, null, false, Guid.Empty, key, perType));
            }
            else
            {
                if (type.FindProperty("ClientAccountId") is { } fk && (fk.ClrType == typeof(Guid) || fk.ClrType == typeof(Guid?)))
                    found.AddRange(await QueryAsync(db, type, fk.Name, fk.ClrType == typeof(Guid?), clientId.Value, key, perType));
                if (type.FindProperty("UserId") is { } uk && (uk.ClrType == typeof(Guid) || uk.ClrType == typeof(Guid?)) && type.ClrType != typeof(ClientMember))
                    foreach (var member in members)
                        found.AddRange(await QueryAsync(db, type, uk.Name, uk.ClrType == typeof(Guid?), member, key, perType));
            }
            if (found.Count > 0) index.Ids[type.ClrType.Name] = found.Distinct().ToList();
        }
        return index;
    }

    public static bool IsClientParam(string name) =>
        name.Equals("clientId", StringComparison.OrdinalIgnoreCase) || name.Equals("clientAccountId", StringComparison.OrdinalIgnoreCase);

    /// <summary>Rows a parameter named <paramref name="name"/> (after the literal segment <paramref name="previous"/>) may refer to.</summary>
    public IReadOnlyList<Guid> Candidates(string name, string? previous, bool fallbackToAll)
    {
        if (IsClientParam(name))
            return ClientId is { } c ? new[] { c } : Ids.GetValueOrDefault(nameof(ClientAccount)) ?? new List<Guid>();
        if (name.Equals("userId", StringComparison.OrdinalIgnoreCase) && MemberUserIds.Count > 0) return MemberUserIds;
        var token = (name.EndsWith("Id", StringComparison.Ordinal) && name.Length > 2 ? name[..^2] : Singular(previous ?? name)).ToLowerInvariant();
        var exact = Ids.Where(kv => kv.Key.Equals(token, StringComparison.OrdinalIgnoreCase)).SelectMany(kv => kv.Value.Take(3));
        var contained = Ids.Where(kv => kv.Key.ToLowerInvariant().Contains(token)).OrderBy(kv => kv.Key.Length).SelectMany(kv => kv.Value.Take(3));
        var matched = exact.Concat(contained).Distinct().Take(12).ToList();
        return matched.Count > 0 || !fallbackToAll ? matched : Ids.Values.Select(v => v[0]).Take(40).ToList();
    }

    /// <summary>The literal route segment just before parameter <paramref name="name"/> ("invoices" for invoices/{id}).</summary>
    public static string? PreviousLiteral(ApiEndpoint e, string name)
    {
        var segments = e.Endpoint.RoutePattern.PathSegments.ToList();
        var index = segments.FindIndex(s => s.Parts.OfType<RoutePatternParameterPart>().Any(p => p.Name == name));
        return index > 0 ? string.Concat(segments[index - 1].Parts.OfType<RoutePatternLiteralPart>().Select(l => l.Content)) : null;
    }

    /// <summary>Existing ids for every Guid route parameter of <paramref name="e"/>, or null when one cannot be matched.</summary>
    public Dictionary<string, string>? RealRoute(ApiEndpoint e)
    {
        var guidParams = e.RouteParameters.Where(p => p.ParameterPolicies.Any(pp => pp.Content == "guid")).ToList();
        if (guidParams.Count == 0) return null;
        var route = new Dictionary<string, string>();
        foreach (var p in guidParams)
        {
            var candidates = Candidates(p.Name, PreviousLiteral(e, p.Name), fallbackToAll: false);
            if (candidates.Count == 0) return null;
            route[p.Name] = candidates[0].ToString();
        }
        return route;
    }

    private static string Singular(string word) =>
        word.EndsWith("ies") ? word[..^3] + "y" : word.EndsWith("ses") ? word[..^2] : word.EndsWith('s') ? word[..^1] : word;

    private static Task<List<Guid>> QueryAsync(AppDbContext db, IEntityType type, string? column, bool nullable, Guid value, string key, int take) =>
        (Task<List<Guid>>)typeof(IdIndex).GetMethod(nameof(IdsAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type.ClrType).Invoke(null, new object?[] { db, column, nullable, value, key, take })!;

    private static async Task<List<Guid>> IdsAsync<T>(AppDbContext db, string? column, bool nullable, Guid value, string key, int take) where T : class
    {
        IQueryable<T> set = db.Set<T>().AsNoTracking().IgnoreQueryFilters();
        if (column is not null)
            set = nullable ? set.Where(e => EF.Property<Guid?>(e, column) == value) : set.Where(e => EF.Property<Guid>(e, column) == value);
        return await set.OrderBy(e => EF.Property<Guid>(e, key)).Select(e => EF.Property<Guid>(e, key)).Take(take).ToListAsync();
    }
}
