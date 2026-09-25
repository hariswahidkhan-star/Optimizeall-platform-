using System.Text.Json;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Projects;
using OptimizeAll.IntegrationTests.Clients;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Projects;

/// <summary>
/// Message threads page through every conversation of a client (the list used to stop silently at 200): stable order
/// (most recent activity first, ties by id, so no thread repeats or goes missing across pages), a total, search, the
/// client's view without internal threads, and the original <c>GET threads</c> (first 200 + <c>X-Total-Count</c>).
/// </summary>
public sealed class ThreadPagingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private sealed record Page(List<Guid> Ids, int Total, int TotalPages);

    private static async Task<Page> ReadPageAsync(HttpResponseMessage response)
    {
        var body = await response.ReadJsonAsync();
        return new Page(body.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToList(),
            body.GetProperty("total").GetInt32(), body.GetProperty("totalPages").GetInt32());
    }

    /// <summary>Creates threads directly: many share the same last-activity time, so only the id orders them.</summary>
    private async Task<List<MessageThread>> SeedThreadsAsync(Guid clientId, Guid authorId, int count, int internalCount)
    {
        var at = DateTime.UtcNow.AddDays(-1);
        var threads = Enumerable.Range(0, count).Select(i => new MessageThread
        {
            ClientAccountId = clientId, Subject = $"Thread {i:D3}", CreatedByUserId = authorId, LastAuthorUserId = authorId,
            LastMessageAt = at.AddMinutes(i / 10), MessageCount = 1, LastMessagePreview = i % 50 == 0 ? "about the invoice" : "hello",
            IsInternal = i < internalCount,
        }).ToList();
        await api.WithDbAsync(async db =>
        {
            db.Set<MessageThread>().AddRange(threads);
            await db.SaveChangesAsync();
        });
        return threads;
    }

    [Fact]
    public async Task More_than_200_threads_page_completely_in_a_stable_order_with_a_total()
    {
        var am = await api.StaffAsync();
        var org = await api.CreateOrgAsync(am.Client);
        const int count = 230, internalCount = 7;
        var threads = await SeedThreadsAsync(org, am.User.Id, count, internalCount);

        // Staff: every thread, internal included, in pages of 50.
        var seen = new List<Guid>();
        Page page;
        var n = 1;
        do
        {
            page = await ReadPageAsync(await am.Client.GetAsync($"/api/v1/agency/clients/{org}/threads/paged?page={n}&pageSize=50"));
            Assert.Equal(count, page.Total);
            Assert.Equal(5, page.TotalPages);
            seen.AddRange(page.Ids);
            n++;
        } while (page.Ids.Count > 0);
        Assert.Equal(count, seen.Count);
        Assert.Equal(count, seen.Distinct().Count());
        Assert.Equal(threads.Select(t => t.Id).ToHashSet(), seen.ToHashSet());
        // Most recent activity first.
        var byId = threads.ToDictionary(t => t.Id);
        Assert.True(seen.Zip(seen.Skip(1)).All(p => byId[p.First].LastMessageAt >= byId[p.Second].LastMessageAt));

        // The same order on every read (ties broken by id): a second pass gives the same pages.
        var again = new List<Guid>();
        for (var p = 1; p <= 5; p++)
            again.AddRange((await ReadPageAsync(await am.Client.GetAsync($"/api/v1/agency/clients/{org}/threads/paged?page={p}&pageSize=50"))).Ids);
        Assert.Equal(seen, again);

        // Far pages are empty, not an overflow; a page size over 200 is refused.
        Assert.Empty((await ReadPageAsync(await am.Client.GetAsync($"/api/v1/agency/clients/{org}/threads/paged?page=85899347&pageSize=50"))).Ids);
        await (await am.Client.GetAsync($"/api/v1/agency/clients/{org}/threads/paged?pageSize=201")).ShouldFailAsync(400);

        // Search matches the subject or the last message.
        var found = await ReadPageAsync(await am.Client.GetAsync($"/api/v1/agency/clients/{org}/threads/paged?search=invoice&pageSize=200"));
        Assert.Equal(threads.Count(t => t.LastMessagePreview == "about the invoice"), found.Total);

        // The original list keeps its shape (first 200) and now tells the caller how many there are.
        var legacy = await am.Client.GetAsync($"/api/v1/agency/clients/{org}/threads");
        Assert.Equal(count.ToString(), Assert.Single(legacy.Headers.GetValues("X-Total-Count")));
        var legacyItems = (await legacy.ReadJsonAsync()).EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(200, legacyItems.Count);
        Assert.Equal(seen.Take(200), legacyItems);

        // A client user pages through the non-internal threads only.
        var owner = await api.ClientUserAsync(org, ClientMemberRole.Owner);
        var clientSeen = new List<Guid>();
        for (var p = 1; p <= 2; p++)
        {
            var clientPage = await ReadPageAsync(await owner.Client.GetAsync($"/api/v1/client/orgs/{org}/threads/paged?page={p}&pageSize=200"));
            Assert.Equal(count - internalCount, clientPage.Total);
            clientSeen.AddRange(clientPage.Ids);
        }
        Assert.Equal(count - internalCount, clientSeen.Distinct().Count());
        Assert.DoesNotContain(clientSeen, id => byId[id].IsInternal);
        var clientLegacy = await owner.Client.GetAsync($"/api/v1/client/orgs/{org}/threads");
        Assert.Equal((count - internalCount).ToString(), Assert.Single(clientLegacy.Headers.GetValues("X-Total-Count")));

        // Another organisation's threads are out of reach.
        var otherOrg = await api.CreateOrgAsync(am.Client);
        var foreign = await owner.Client.GetAsync($"/api/v1/client/orgs/{otherOrg}/threads/paged");
        Assert.Contains((int)foreign.StatusCode, new[] { 403, 404 });
    }
}
