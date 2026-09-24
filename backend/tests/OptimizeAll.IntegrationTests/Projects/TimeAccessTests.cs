using System.Net;
using System.Net.Http.Json;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Projects;

/// <summary>
/// time.view_all alone (Finance) reads time entries, exports, timesheets, utilization and rates but cannot track time;
/// time.track (delivery staff) tracks and reads their own time; everyone else is refused.
/// </summary>
public sealed class TimeAccessTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Range = "from=2026-09-01&to=2026-09-07";

    [Fact]
    public async Task Finance_reads_time_without_being_able_to_track_it()
    {
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync($"/api/v1/agency/time/entries?{Range}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync($"/api/v1/agency/time/entries/export.csv?{Range}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync("/api/v1/agency/time/timesheets/week?date=2026-09-02")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync($"/api/v1/agency/time/utilization?{Range}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync("/api/v1/agency/time/rates")).StatusCode);

        await (await finance.GetAsync("/api/v1/agency/time/timer")).ShouldFailAsync(403);
        await (await finance.PostAsJsonAsync("/api/v1/agency/time/timer/start", new { })).ShouldFailAsync(403);
        await (await finance.PostAsJsonAsync("/api/v1/agency/time/entries", new { })).ShouldFailAsync(403);
        await (await finance.PostAsync("/api/v1/agency/time/timesheets/submit?date=2026-09-02", null)).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Trackers_read_their_own_time_and_others_are_refused()
    {
        var (_, designer) = await api.CreateClientAsync(Role.Designer);
        Assert.Equal(HttpStatusCode.OK, (await designer.GetAsync($"/api/v1/agency/time/entries?{Range}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await designer.GetAsync("/api/v1/agency/time/timer")).StatusCode); // no timer running
        await (await designer.GetAsync($"/api/v1/agency/time/utilization?{Range}")).ShouldFailAsync(403);

        foreach (var role in new[] { Role.Participant, Role.Client, Role.SalesRep })
        {
            var (_, client) = await api.CreateClientAsync(role);
            await (await client.GetAsync($"/api/v1/agency/time/entries?{Range}")).ShouldFailAsync(403);
            await (await client.GetAsync("/api/v1/agency/time/timesheets/week?date=2026-09-02")).ShouldFailAsync(403);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"/api/v1/agency/time/entries?{Range}")).StatusCode);
    }
}
