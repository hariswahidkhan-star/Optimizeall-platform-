using System.Diagnostics;
using OptimizeAll.Domain.Identity;
using Xunit.Abstractions;

namespace OptimizeAll.IntegrationTests.ApiContract;

/// <summary>
/// Every protected endpoint, called by every built-in role (and a client user with each member duty), answers as its
/// permission metadata says: callers whose effective permissions (<c>RolePermissions</c>) satisfy the endpoint's
/// <c>[HasPermission]</c>/<c>[RequireAnyPermission]</c> reach the handler (any 2xx or business 4xx, never a permission 403),
/// callers who don't get 403 before anything else runs. Endpoints without permission metadata are open to every signed-in
/// user, so a permission 403 there means a check hidden in the handler instead of on the endpoint.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class PermissionContractTests(ContractFixture fx, ITestOutputHelper output)
{
    [Fact]
    public async Task Every_role_is_allowed_or_denied_exactly_as_the_permission_metadata_says()
    {
        var findings = new Findings();
        var stopwatch = Stopwatch.StartNew();
        var work = fx.Endpoints.Where(e => !e.AllowAnonymous)
            .SelectMany(e => fx.Callers
                // Member duties only matter to endpoints client users can reach; elsewhere one client caller stands for all.
                .Where(c => c.Role != Role.Client || c.Duty == Domain.Agency.ClientMemberRole.Viewer || e.Allows(c.Permissions))
                .Select(c => (Endpoint: e, Caller: c)))
            .ToList();

        await Findings.ForEachAsync(work, ContractFixture.Parallelism, async item =>
        {
            var (endpoint, caller) = item;
            var expected = endpoint.Allows(caller.Permissions);
            var outcome = await Outcome.ReadAsync(await fx.SendAsync(caller, () => HostileCases.BaseRequest(endpoint)));
            var required = endpoint.HasPermissionMetadata
                ? string.Join(" + ", endpoint.AllOf.Concat(endpoint.AnyOf.Select(s => "any(" + string.Join("|", s) + ")")))
                : "signed-in";
            if (outcome.Status >= 500)
                findings.Add($"{endpoint.Key} as {caller}: [5xx] {outcome.Short}");
            else if (outcome.IsChallenge)
                findings.Add($"{endpoint.Key} as {caller}: [401] {outcome.Short}");
            else if (expected && outcome.IsPermissionDenial)
                findings.Add($"{endpoint.Key} as {caller}: [perm] holds {required} but got {outcome.Short}");
            else if (!expected && outcome.Status != 403)
                findings.Add($"{endpoint.Key} as {caller}: [perm] lacks {required} but got {outcome.Short}");
        });

        output.WriteLine($"{work.Count} role x endpoint checks over {fx.Endpoints.Count(e => !e.AllowAnonymous)} protected endpoints in {stopwatch.Elapsed.TotalSeconds:F0}s");
        findings.AssertEmpty("permission", work.Count);
    }

    [Fact]
    public void Every_permission_named_in_endpoint_metadata_exists()
    {
        var known = OptimizeAll.Api.Common.Security.Permissions.All.ToHashSet();
        var unknown = fx.Endpoints.SelectMany(e => e.AllOf.Concat(e.AnyOf.SelectMany(s => s)).Where(p => !known.Contains(p)).Select(p => $"{e.Key}: {p}"))
            .Distinct().ToList();
        Assert.Empty(unknown);
    }
}
