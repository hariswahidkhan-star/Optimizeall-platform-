using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Agency;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Clients;

/// <summary>
/// Client health "last activity" reflects exchanges with the client: the team talking among themselves in an internal
/// (staff-only) thread must not make a client that has gone quiet look active.
/// </summary>
public sealed class ClientHealthActivityTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static IEnumerable<string> Codes(JsonElement health) =>
        health.GetProperty("reasons").EnumerateArray().Select(r => r.GetProperty("code").GetString()!);

    [Fact]
    public async Task Internal_thread_messages_do_not_count_as_client_activity()
    {
        var am = await api.StaffAsync();
        var orgId = await api.CreateOrgAsync(am.Client, "Quiet Client");
        // An established client (past the new-client grace period) with no delivery activity at all.
        await api.WithDbAsync(db => db.Set<ClientAccount>().Where(c => c.Id == orgId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, c => c.CreatedAt.AddDays(-60))));
        var health = await (await am.Client.GetAsync($"/api/v1/agency/clients/{orgId}/health")).ReadJsonAsync();
        Assert.Contains("no_activity", Codes(health));

        // The team discusses the client internally: still no activity with the client.
        var created = await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{orgId}/threads",
            new { subject = "Renewal risk", body = "They have not replied in weeks.", isInternal = true });
        created.EnsureSuccessStatusCode();
        var internalId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();
        (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{orgId}/threads/{internalId}/messages", new { body = "Escalating to the director." }))
            .EnsureSuccessStatusCode();
        health = await (await am.Client.GetAsync($"/api/v1/agency/clients/{orgId}/health")).ReadJsonAsync();
        Assert.Contains("no_activity", Codes(health));
        var board = await (await am.Client.GetAsync("/api/v1/agency/clients/health")).ReadJsonAsync();
        Assert.Contains("no_activity", Codes(board.EnumerateArray().Single(b => b.GetProperty("clientId").GetGuid() == orgId)));

        // A message in a client-visible thread is activity.
        (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{orgId}/threads", new { subject = "Checking in", body = "How is everything going?" }))
            .EnsureSuccessStatusCode();
        health = await (await am.Client.GetAsync($"/api/v1/agency/clients/{orgId}/health")).ReadJsonAsync();
        Assert.DoesNotContain(Codes(health), c => c is "no_activity" or "quiet" or "inactive");
    }
}
