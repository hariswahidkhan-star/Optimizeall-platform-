using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using OptimizeAll.Api.Modules.Clients;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace OptimizeAll.IntegrationTests.ApiContract;

/// <summary>
/// Client tenancy: a user of client A who names client B's records (route or query ids) gets 404 (or a rejection), never
/// B's data, and no response to a client-A user ever contains an identifier or the name of client B. Ids come from the demo
/// dataset: every row of every entity with a <c>ClientAccountId</c> of the other client, plus rows owned by the other
/// client's users. Checked in both directions (A's owner against B, B's owner against A).
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class TenancyContractTests(ContractFixture fx, ITestOutputHelper output)
{
    /// <summary>Ids of one tenant's rows by entity name, plus strings that identify the tenant in a response body.</summary>
    private sealed record Tenant(Guid ClientId, string Name, Dictionary<string, List<Guid>> Ids, List<Guid> MemberUserIds)
    {
        public IReadOnlySet<Guid> AllIds { get; } = Ids.Values.SelectMany(v => v).Append(ClientId).ToHashSet();

        public IReadOnlyList<string> Markers { get; } = Ids.Values.SelectMany(v => v).Append(ClientId).Select(i => i.ToString()).Append(Name).Distinct().ToList();
    }

    [Fact]
    public async Task Client_users_never_reach_another_clients_records()
    {
        var a = await LoadTenantAsync(fx.ClientA);
        var b = await LoadTenantAsync(fx.ClientB);
        Assert.True(b.Ids.Count > 10, $"client B has rows in only {b.Ids.Count} entity types; the demo dataset is missing");

        var findings = new Findings();
        var stopwatch = Stopwatch.StartNew();
        var endpoints = fx.Endpoints.Where(e => !e.AllowAnonymous && e.Allows(fx.ClientOwnerA.Permissions)).ToList();
        var work = new List<(ApiEndpoint Endpoint, Caller Caller, Tenant Other, string Label, Func<HttpRequestMessage> Build, bool NamesOther)>();
        var positive = new List<(ApiEndpoint Endpoint, Func<HttpRequestMessage> Build)>();

        foreach (var (caller, own, other) in new[] { (fx.ClientOwnerA, a, b), (fx.ClientOwnerB, b, a) })
        {
            foreach (var e in endpoints)
            {
                var routeParams = e.RouteParameters.Select(p => p.Name).ToList();
                var queryIds = e.Query.Where(q => Samples.KindOf(q.Type) == ValueKind.Guid).ToList();

                // Plain call: whatever it lists must be the caller's own tenant only.
                work.Add((e, caller, other, "own", () => HostileCases.BaseRequest(e, route: OwnRoute(e, own)), false));

                // Route ids: every combination of (own|other client) x other tenant's candidate records.
                foreach (var combo in RouteCombos(e, routeParams, own, other).Take(40))
                    work.Add((e, caller, other, string.Join(",", combo.Select(kv => $"{kv.Key}={Label(kv.Value, own, other)}")),
                        () => HostileCases.BaseRequest(e, route: combo), true));

                // Query ids (clientId=, clientAccountId=, postId=, …): other tenant's records.
                foreach (var q in queryIds)
                {
                    foreach (var id in Candidates(q.Name, null, other).Take(6))
                    {
                        var query = Samples.BaseQuery(e).Where(x => !x.Name.Equals(q.Name, StringComparison.OrdinalIgnoreCase))
                            .Append((q.Name, id.ToString())).ToList();
                        work.Add((e, caller, other, $"?{q.Name}={Label(id.ToString(), own, other)}",
                            () => HostileCases.BaseRequest(e, route: OwnRoute(e, own), query: query), false));
                    }
                }

                if (caller == fx.ClientOwnerA && routeParams.Count > 0)
                    foreach (var combo in RouteCombos(e, routeParams, own, own).Take(3))
                        positive.Add((e, () => HostileCases.BaseRequest(e, route: combo)));
            }
        }

        await Findings.ForEachAsync(work, ContractFixture.Parallelism, async item =>
        {
            var outcome = await Outcome.ReadAsync(await fx.SendAsync(item.Caller, item.Build));
            var where = $"{item.Endpoint.Key} [{item.Label}] as {item.Caller}";
            if (outcome.Status >= 500) findings.Add($"{where}: [5xx] {outcome.Short}");
            else if (item.NamesOther && outcome.IsSuccess) findings.Add($"[leak] {where}: other tenant's record answered {outcome.Short}");
            else if (outcome.IsSuccess && item.Other.Markers.FirstOrDefault(m => outcome.Body.Contains(m, StringComparison.OrdinalIgnoreCase)) is { } marker)
                findings.Add($"[leak] {where}: response contains other tenant's '{marker}'");
        });

        var reached = 0;
        await Findings.ForEachAsync(positive, ContractFixture.Parallelism, async item =>
        {
            var outcome = await Outcome.ReadAsync(await fx.SendAsync(fx.ClientOwnerA, item.Build));
            if (outcome.IsSuccess) Interlocked.Increment(ref reached);
        });

        output.WriteLine($"{endpoints.Count} client-reachable endpoints, {work.Count} cross-tenant requests, " +
                         $"{reached}/{positive.Count} own-tenant controls answered 2xx, {stopwatch.Elapsed.TotalSeconds:F0}s");
        // The cross-tenant requests only prove something if the same requests with the caller's own ids reach real data.
        Assert.True(reached >= 20, $"only {reached} own-tenant control requests succeeded; the fixture ids do not reach the handlers");
        findings.AssertEmpty("tenancy", work.Count);
    }

    private static string Label(string value, Tenant own, Tenant other) =>
        other.AllIds.Any(i => i.ToString() == value) ? "other" : own.AllIds.Any(i => i.ToString() == value) ? "own" : value;

    private static Dictionary<string, string> OwnRoute(ApiEndpoint e, Tenant own) =>
        e.RouteParameters.Where(p => IsClientParam(p.Name)).ToDictionary(p => p.Name, _ => own.ClientId.ToString());

    private static bool IsClientParam(string name) => name.Equals("clientId", StringComparison.OrdinalIgnoreCase) ||
                                                      name.Equals("clientAccountId", StringComparison.OrdinalIgnoreCase);

    /// <summary>Route value combinations naming <paramref name="target"/>'s records (client ids: own and target).</summary>
    private static IEnumerable<Dictionary<string, string>> RouteCombos(ApiEndpoint e, List<string> routeParams, Tenant own, Tenant target)
    {
        if (routeParams.Count == 0) yield break;
        var literals = e.Endpoint.RoutePattern.PathSegments.ToList();
        var options = new List<(string Name, List<string> Values)>();
        foreach (var name in routeParams)
        {
            if (IsClientParam(name))
            {
                options.Add((name, new[] { own.ClientId, target.ClientId }.Distinct().Select(g => g.ToString()).ToList()));
                continue;
            }
            // The literal segment just before the parameter ("invoices/{id}") names the entity for a bare {id}.
            var index = literals.FindIndex(s => s.Parts.OfType<Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart>().Any(p => p.Name == name));
            var previous = index > 0 ? string.Concat(literals[index - 1].Parts.OfType<Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart>().Select(l => l.Content)) : null;
            var candidates = Candidates(name, previous, target).Select(g => g.ToString()).ToList();
            if (candidates.Count == 0) candidates.Add(Guid.NewGuid().ToString());
            options.Add((name, candidates));
        }
        IEnumerable<Dictionary<string, string>> Expand(int i, Dictionary<string, string> acc)
        {
            if (i == options.Count)
            {
                // At least one value must belong to the target tenant (a pure own-tenant combination is not cross-tenant).
                yield return new Dictionary<string, string>(acc);
                yield break;
            }
            foreach (var v in options[i].Values)
            {
                acc[options[i].Name] = v;
                foreach (var r in Expand(i + 1, acc)) yield return r;
            }
        }
        foreach (var combo in Expand(0, new Dictionary<string, string>()))
        {
            if (own != target && combo.Values.All(v => own.AllIds.Any(i => i.ToString() == v))) continue;
            yield return combo;
        }
    }

    /// <summary>Records of <paramref name="tenant"/> a parameter named <paramref name="name"/> (after segment <paramref name="previous"/>) may refer to.</summary>
    private static IEnumerable<Guid> Candidates(string name, string? previous, Tenant tenant)
    {
        if (IsClientParam(name)) return new[] { tenant.ClientId };
        if (name.Equals("userId", StringComparison.OrdinalIgnoreCase)) return tenant.MemberUserIds;
        var token = name.EndsWith("Id", StringComparison.Ordinal) && name.Length > 2 ? name[..^2] : Singular(previous ?? name);
        token = token.ToLowerInvariant();
        var matched = tenant.Ids.Where(kv => kv.Key.ToLowerInvariant().Contains(token)).SelectMany(kv => kv.Value.Take(3)).ToList();
        return matched.Count > 0 ? matched.Take(12) : tenant.Ids.Values.Select(v => v[0]).Take(40);
    }

    private static string Singular(string word) =>
        word.EndsWith("ies") ? word[..^3] + "y" : word.EndsWith("ses") ? word[..^2] : word.EndsWith('s') ? word[..^1] : word;

    private async Task<Tenant> LoadTenantAsync(Guid clientId)
    {
        return await fx.WithDbAsync(async db =>
        {
            var name = await db.Set<ClientAccount>().Where(c => c.Id == clientId).Select(c => c.Name).SingleAsync();
            var members = await db.Set<ClientMember>().Where(m => m.ClientAccountId == clientId).Select(m => m.UserId).ToListAsync();
            var ids = new Dictionary<string, List<Guid>>();
            foreach (var type in db.Model.GetEntityTypes().Where(t => !t.IsOwned() && t.FindPrimaryKey() is { Properties.Count: 1 } k && k.Properties[0].ClrType == typeof(Guid)))
            {
                var key = type.FindPrimaryKey()!.Properties[0].Name;
                var found = new List<Guid>();
                if (type.FindProperty("ClientAccountId") is { } fk && (fk.ClrType == typeof(Guid) || fk.ClrType == typeof(Guid?)))
                    found.AddRange(await QueryIdsAsync(db, type, fk.Name, fk.ClrType, clientId, key));
                if (type.FindProperty("UserId") is { } uk && (uk.ClrType == typeof(Guid) || uk.ClrType == typeof(Guid?)) && type.ClrType != typeof(ClientMember))
                    foreach (var member in members)
                        found.AddRange(await QueryIdsAsync(db, type, uk.Name, uk.ClrType, member, key));
                if (found.Count > 0) ids[type.ClrType.Name] = found.Distinct().ToList();
            }
            return new Tenant(clientId, name, ids, members);
        });
    }

    private static Task<List<Guid>> QueryIdsAsync(AppDbContext db, IEntityType type, string column, Type columnType, Guid value, string key) =>
        (Task<List<Guid>>)typeof(TenancyContractTests).GetMethod(nameof(IdsAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type.ClrType).Invoke(null, new object[] { db, column, columnType == typeof(Guid?), value, key })!;

    private static async Task<List<Guid>> IdsAsync<T>(AppDbContext db, string column, bool nullable, Guid value, string key) where T : class
    {
        var set = db.Set<T>().AsNoTracking().IgnoreQueryFilters();
        var filtered = nullable ? set.Where(e => EF.Property<Guid?>(e, column) == value) : set.Where(e => EF.Property<Guid>(e, column) == value);
        return await filtered.OrderBy(e => EF.Property<Guid>(e, key)).Select(e => EF.Property<Guid>(e, key)).Take(5).ToListAsync();
    }
}
