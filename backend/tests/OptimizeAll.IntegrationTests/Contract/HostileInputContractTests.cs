using System.Diagnostics;
using Xunit.Abstractions;

namespace OptimizeAll.IntegrationTests.ApiContract;

/// <summary>
/// Every endpoint, called by a caller its metadata allows, answers every input of the hostile matrix (<see cref="HostileCases"/>)
/// with a success or a 4xx RFC 7807 problem carrying <c>code</c> and <c>traceId</c>: never a 5xx, never a stack trace,
/// unknown ids are 404, malformed JSON/types/enums/dates are 400, unsupported media types 415, oversized pages clamped or 400.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class HostileInputContractTests(ContractFixture fx, ITestOutputHelper output)
{
    [Fact]
    public async Task Every_endpoint_rejects_hostile_input_with_a_problem_and_never_fails_with_a_500()
    {
        var findings = new Findings();
        var stopwatch = Stopwatch.StartNew();
        var work = fx.Endpoints
            .Select(e => (Endpoint: e, Caller: fx.AuthorizedCallerFor(e)))
            .SelectMany(x => HostileCases.For(x.Endpoint).Select(c => (x.Endpoint, x.Caller, Case: c)))
            .ToList();

        await Findings.ForEachAsync(work, ContractFixture.Parallelism, async item =>
        {
            var (endpoint, caller, @case) = item;
            Outcome outcome;
            try
            {
                outcome = await Outcome.ReadAsync(await fx.SendAsync(caller, @case.Build));
            }
            catch (Exception ex)
            {
                findings.Add($"{endpoint.Key} [{@case.Name}] as {caller?.Name ?? "anonymous"}: [client] {ex.GetType().Name}: {ex.Message}");
                return;
            }
            var violation = outcome.ContractViolation() ?? @case.Expect?.Invoke(outcome);
            if (outcome.IsChallenge && caller is not null) violation ??= $"[401] signed-in caller got {outcome.Short}";
            if (violation is not null)
                findings.Add($"{endpoint.Key} [{@case.Name}] as {caller?.Name ?? "anonymous"}: {violation}");
        });

        output.WriteLine($"{fx.Endpoints.Count} endpoints, {work.Count} hostile requests in {stopwatch.Elapsed.TotalSeconds:F0}s");
        findings.AssertEmpty("hostile-input", work.Count);
    }
}
