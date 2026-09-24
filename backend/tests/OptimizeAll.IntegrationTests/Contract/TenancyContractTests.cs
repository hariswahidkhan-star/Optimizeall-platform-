using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Agency;
using Xunit.Abstractions;

namespace OptimizeAll.IntegrationTests.ApiContract;

/// <summary>
/// Client tenancy: a user of client A who names client B's records (route or query ids) gets 404 (or another rejection),
/// never B's data, and no response to a client-A user contains an identifier or the name of client B. Ids come from the
/// demo dataset: every row of every entity with a <c>ClientAccountId</c> of the other client, plus rows owned by the other
/// client's users. Checked in both directions (A's owner against B, B's owner against A), with own-tenant control
/// requests proving the same requests reach real data.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class TenancyContractTests(ContractFixture fx, ITestOutputHelper output)
{
    private sealed record Tenant(IdIndex Index, string Name)
    {
        public Guid ClientId => Index.ClientId!.Value;
        public IReadOnlyList<string> Markers { get; } = Index.AllIds.Select(i => i.ToString()).Append(Name).ToList();
    }

    private sealed record Probe(ApiEndpoint Endpoint, Caller Caller, Tenant Other, string Label, Func<HttpRequestMessage> Build, bool NamesOther);

    [Fact]
    public async Task Client_users_never_reach_another_clients_records()
    {
        var a = await LoadAsync(fx.ClientA);
        var b = await LoadAsync(fx.ClientB);
        Assert.True(b.Index.Ids.Count > 10, $"client B has rows in only {b.Index.Ids.Count} entity types; the demo dataset is missing");

        var stopwatch = Stopwatch.StartNew();
        var endpoints = fx.Endpoints.Where(e => !e.AllowAnonymous && e.Allows(fx.ClientOwnerA.Permissions)).ToList();
        var probes = new List<Probe>();
        var controls = new List<Func<HttpRequestMessage>>();

        foreach (var (caller, own, other) in new[] { (fx.ClientOwnerA, a, b), (fx.ClientOwnerB, b, a) })
        {
            foreach (var e in endpoints)
            {
                var ownClient = OwnClientRoute(e, own);
                // Plain call: whatever it lists belongs to the caller's own organization only.
                probes.Add(new Probe(e, caller, other, "own", () => HostileCases.BaseRequest(e, route: ownClient), false));

                // Route ids: (own | other client) x the other tenant's candidate records; at least one names the other tenant.
                foreach (var combo in RouteCombos(e, own, other).Take(40))
                    probes.Add(new Probe(e, caller, other, string.Join(",", combo.Select(kv => $"{kv.Key}={Label(kv.Value, own, other)}")),
                        () => HostileCases.BaseRequest(e, route: combo), true));

                // Query ids (clientId=, clientAccountId=, postId=, …) naming the other tenant's records.
                foreach (var q in e.Query.Where(q => Samples.KindOf(q.Type) == ValueKind.Guid))
                {
                    foreach (var id in other.Index.Candidates(q.Name, null, fallbackToAll: true).Take(6))
                    {
                        var query = Samples.BaseQuery(e).Where(x => !x.Name.Equals(q.Name, StringComparison.OrdinalIgnoreCase))
                            .Append((q.Name, id.ToString())).ToList();
                        probes.Add(new Probe(e, caller, other, $"?{q.Name}=other", () => HostileCases.BaseRequest(e, route: ownClient, query: query), false));
                    }
                }

                if (caller == fx.ClientOwnerA && e.RouteParameters.Any())
                    foreach (var combo in RouteCombos(e, own, own).Take(3))
                        controls.Add(() => HostileCases.BaseRequest(e, route: combo));
            }
        }

        var findings = new Findings();
        await Findings.ForEachAsync(probes, ContractFixture.Parallelism, async p =>
        {
            var outcome = await Outcome.ReadAsync(await fx.SendAsync(p.Caller, p.Build));
            var where = $"{p.Endpoint.Key} [{p.Label}] as {p.Caller}";
            if (outcome.Status >= 500) findings.Add($"{where}: [5xx] {outcome.Short}");
            else if (p.NamesOther && outcome.IsSuccess) findings.Add($"[leak] {where}: other tenant's record answered {outcome.Short}");
            else if (outcome.IsSuccess && p.Other.Markers.FirstOrDefault(m => outcome.Body.Contains(m, StringComparison.OrdinalIgnoreCase)) is { } marker)
                findings.Add($"[leak] {where}: response contains other tenant's '{marker}'");
        });

        var reached = 0;
        await Findings.ForEachAsync(controls, ContractFixture.Parallelism, async build =>
        {
            if ((await Outcome.ReadAsync(await fx.SendAsync(fx.ClientOwnerA, build))).IsSuccess) Interlocked.Increment(ref reached);
        });

        output.WriteLine($"{endpoints.Count} client-reachable endpoints, {probes.Count} cross-tenant requests, " +
                         $"{reached}/{controls.Count} own-tenant controls answered 2xx, {stopwatch.Elapsed.TotalSeconds:F0}s");
        // The cross-tenant requests only prove something if the same requests with the caller's own ids reach real data.
        Assert.True(reached >= 20, $"only {reached} own-tenant control requests succeeded; the fixture ids do not reach the handlers");
        findings.AssertEmpty("tenancy", probes.Count);
    }

    private async Task<Tenant> LoadAsync(Guid clientId) => await fx.WithDbAsync(async db =>
        new Tenant(await IdIndex.LoadAsync(db, clientId), await db.Set<ClientAccount>().Where(c => c.Id == clientId).Select(c => c.Name).SingleAsync()));

    private static string Label(string value, Tenant own, Tenant other) =>
        Guid.TryParse(value, out var id) ? other.Index.AllIds.Contains(id) ? "other" : own.Index.AllIds.Contains(id) ? "own" : value : value;

    private static Dictionary<string, string> OwnClientRoute(ApiEndpoint e, Tenant own) =>
        e.RouteParameters.Where(p => IdIndex.IsClientParam(p.Name)).ToDictionary(p => p.Name, _ => own.ClientId.ToString());

    /// <summary>Route value combinations naming <paramref name="target"/>'s records (client ids: own and target).</summary>
    private static IEnumerable<Dictionary<string, string>> RouteCombos(ApiEndpoint e, Tenant own, Tenant target)
    {
        var options = new List<(string Name, List<string> Values)>();
        foreach (var p in e.RouteParameters)
        {
            if (Samples.RegexChoice(p) is { } choice) { options.Add((p.Name, new List<string> { choice })); continue; }
            if (IdIndex.IsClientParam(p.Name))
            {
                options.Add((p.Name, new[] { own.ClientId, target.ClientId }.Distinct().Select(g => g.ToString()).ToList()));
                continue;
            }
            var candidates = target.Index.Candidates(p.Name, IdIndex.PreviousLiteral(e, p.Name), fallbackToAll: true).Select(g => g.ToString()).ToList();
            options.Add((p.Name, candidates.Count > 0 ? candidates : new List<string> { Guid.NewGuid().ToString() }));
        }
        if (options.Count == 0) yield break;

        IEnumerable<Dictionary<string, string>> Expand(int i, Dictionary<string, string> acc)
        {
            if (i == options.Count) { yield return new Dictionary<string, string>(acc); yield break; }
            foreach (var v in options[i].Values)
            {
                acc[options[i].Name] = v;
                foreach (var r in Expand(i + 1, acc)) yield return r;
            }
        }
        foreach (var combo in Expand(0, new Dictionary<string, string>()))
        {
            // A combination of only the caller's own ids is not a cross-tenant request.
            if (own != target && combo.Values.All(v => !Guid.TryParse(v, out var g) || own.Index.AllIds.Contains(g))) continue;
            yield return combo;
        }
    }
}
