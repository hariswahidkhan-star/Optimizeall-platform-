using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.IntegrationTests.Clients;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Edge;

/// <summary>
/// Paginated lists at volume: every row exactly once when many rows tie on the sort key (MySQL returns ties in a
/// different order for each LIMIT/OFFSET unless a unique key ends the ORDER BY), far-out pages, page-size limits on
/// every binding, and LIKE search with wildcard characters on both providers.
/// </summary>
public sealed class ListEdgeCaseTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const int PageSize = 7;

    /// <summary>Walks every page and asserts each row appears exactly once and the total matches.</summary>
    private static async Task<List<Guid>> WalkAsync(HttpClient client, string url, int expected)
    {
        var seen = new List<Guid>();
        var sep = url.Contains('?') ? '&' : '?';
        for (var page = 1; page <= expected / PageSize + 2; page++)
        {
            var json = await (await client.GetAsync($"{url}{sep}page={page}&pageSize={PageSize}")).ReadJsonAsync();
            Assert.Equal(expected, json.GetProperty("total").GetInt32());
            seen.AddRange(json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        }
        var duplicates = seen.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, $"{url}: {duplicates.Count} rows were served on more than one page");
        Assert.True(seen.Count == expected, $"{url}: {expected - seen.Count} rows never appeared on any page");
        return seen;
    }

    private static async Task ImportContactsAsync(HttpClient sales, string tag, int count)
    {
        var csv = new StringBuilder("first_name,last_name,email,company,company_domain,lifecycle_stage,tags\r\n");
        for (var i = 0; i < count; i++)
            csv.Append($"Tie,Same,{tag}.{i}@volume.example,{tag} Co {i % 5},{tag}-{i % 5}.example,lead,{tag}\r\n");
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv.ToString()));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "contacts.csv");
        var result = await (await sales.PostAsync("/api/v1/agency/crm/contacts/import?dryRun=false", form)).ReadJsonAsync();
        Assert.Equal(count, result.GetProperty("created").GetInt32());
    }

    [Fact]
    public async Task Paging_through_tied_rows_serves_every_row_exactly_once()
    {
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var tag = "vol" + Guid.NewGuid().ToString("N")[..8];
        const int contacts = 120;
        // The test clock does not move: every contact shares CreatedAt/UpdatedAt, name, stage and score.
        await ImportContactsAsync(sales, tag, contacts);
        foreach (var sort in new[] { "", "&sort=name", "&sort=score", "&sort=lifecycle", "&sort=updated", "&sort=created&desc=false" })
            await WalkAsync(sales, $"/api/v1/agency/crm/contacts?tag={tag}{sort}", contacts);

        // Companies created by the import tie on CreatedAt too.
        await WalkAsync(sales, $"/api/v1/agency/crm/companies?search={tag}", 5);

        // Deals: same title, same timestamps.
        for (var i = 0; i < 30; i++)
            (await sales.PostAsJsonAsync("/api/v1/agency/crm/deals", new { title = $"{tag} deal", value = 1000m, currency = "USD" })).EnsureSuccessStatusCode();
        foreach (var sort in new[] { "", "&sort=title", "&sort=value", "&sort=stageChanged" })
            await WalkAsync(sales, $"/api/v1/agency/crm/deals?search={tag}{sort}", 30);

        // Admin users: identical display names and sign-up times.
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var domain = $"{tag}.example";
        for (var i = 0; i < 40; i++)
            await api.CreateUserAsync(email: $"u{i}@{domain}");
        foreach (var sort in new[] { "", "&sort=displayName", "&sort=lastActiveAt", "&sort=createdAt&desc=false" })
            await WalkAsync(admin, $"/api/v1/admin/users?search={domain}{sort}", 40);

        // Clients: same name prefix and creation time, sorted by name and status.
        var am = await api.StaffAsync();
        for (var i = 0; i < 20; i++)
            await api.CreateOrgAsync(am.Client, $"{tag} Client");
        foreach (var sort in new[] { "", "&sort=status", "&sort=createdAt" })
            await WalkAsync(am.Client, $"/api/v1/agency/clients?search={tag}{sort}", 20);

        // Projects: same name, status and creation time.
        var org = await api.CreateOrgAsync(am.Client, $"{tag} Projects");
        for (var i = 0; i < 20; i++)
            await am.Client.CreateProjectAsync(org, name: $"{tag} project");
        foreach (var sort in new[] { "", "&sort=name", "&sort=endDate" })
            await WalkAsync(am.Client, $"/api/v1/agency/projects?search={tag}{sort}", 20);
    }

    [Fact]
    public async Task A_far_out_page_is_empty_instead_of_wrapping_around_to_the_first_page()
    {
        var (adminUser, admin) = await api.CreateClientAsync(Role.Admin);
        var domain = $"far{Guid.NewGuid():N}"[..14] + ".example";
        for (var i = 0; i < 3; i++)
            await api.CreateUserAsync(email: $"u{i}@{domain}");
        await api.WithDbAsync(async db =>
        {
            for (var i = 0; i < 3; i++)
                db.Set<Notification>().Add(new Notification { UserId = adminUser.Id, Type = "test", Title = $"N{i}", Body = "b", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        // (page - 1) * pageSize overflows int for these: 85,899,346 * 25 = 2^31 + 4 → a negative OFFSET.
        foreach (var (url, total) in new[]
                 {
                     ($"/api/v1/admin/users?search={domain}&page=85899347&pageSize=25", 3),
                     ($"/api/v1/admin/users?search={domain}&page=2147483647&pageSize=200", 3),
                     ("/api/v1/me/notifications?page=21474837&pageSize=100", 3),
                     ("/api/v1/me/notifications?page=2147483647&pageSize=100", 3),
                 })
        {
            var json = await (await admin.GetAsync(url)).ReadJsonAsync();
            Assert.Equal(total, json.GetProperty("total").GetInt32());
            Assert.Equal(0, json.GetProperty("items").GetArrayLength());
        }
    }

    [Fact]
    public async Task My_crm_tasks_honour_page_and_page_size_limits()
    {
        var (salesUser, sales) = await api.CreateClientAsync(Role.SalesRep);
        var contact = await (await sales.PostAsJsonAsync("/api/v1/agency/crm/contacts", new { firstName = "Task", email = $"t{Guid.NewGuid():N}@tasks.example" }))
            .ReadJsonAsync();
        for (var i = 0; i < 12; i++)
        {
            (await sales.PostAsJsonAsync("/api/v1/agency/crm/activities", new
            {
                type = "Task", subject = $"Call back {i}", dueAt = DateTime.UtcNow.AddDays(1), assigneeUserId = salesUser.Id,
                contactId = contact.GetProperty("id").GetGuid(),
            })).EnsureSuccessStatusCode();
        }
        var first = await (await sales.GetAsync("/api/v1/agency/crm/tasks/mine?page=1&pageSize=5")).ReadJsonAsync();
        var second = await (await sales.GetAsync("/api/v1/agency/crm/tasks/mine?page=2&pageSize=5")).ReadJsonAsync();
        Assert.Equal(12, first.GetProperty("total").GetInt32());
        Assert.Equal(5, first.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, second.GetProperty("page").GetInt32());
        Assert.Equal(5, second.GetProperty("items").GetArrayLength());
        Assert.Empty(first.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())
            .Intersect(second.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())));
        await WalkAsync(sales, "/api/v1/agency/crm/tasks/mine", 12);

        // Out-of-range paging is refused like on every other list, not silently replaced by the defaults.
        await (await sales.GetAsync("/api/v1/agency/crm/tasks/mine?pageSize=100000")).ShouldFailAsync(400);
        await (await sales.GetAsync("/api/v1/agency/crm/tasks/mine?page=0")).ShouldFailAsync(400);
    }

    [Fact]
    public async Task Search_treats_wildcards_and_special_characters_literally()
    {
        var am = await api.StaffAsync();
        var tag = "q" + Guid.NewGuid().ToString("N")[..6];
        var names = new[] { $"{tag} 100% growth", $"{tag} 100 growth", $"{tag} a_b", $"{tag} axb", $"{tag} O'Brien \"Q\"", $"{tag} back\\slash", $"{tag} café 😀" };
        foreach (var name in names)
            await api.CreateOrgAsync(am.Client, name);
        var org = await api.CreateOrgAsync(am.Client, $"{tag} projects");
        foreach (var name in names)
            await am.Client.CreateProjectAsync(org, name: name);

        async Task<List<string>> Search(string list, string term)
        {
            var json = await (await am.Client.GetAsync($"/api/v1/agency/{list}?pageSize=50&search={Uri.EscapeDataString(term)}")).ReadJsonAsync();
            return json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).Where(n => n.StartsWith(tag)).ToList();
        }

        foreach (var list in new[] { "clients", "projects" })
        {
            Assert.Equal(new[] { $"{tag} 100% growth" }, await Search(list, $"{tag} 100%"));
            Assert.Equal(new[] { $"{tag} a_b" }, await Search(list, "a_b"));
            Assert.Equal(new[] { $"{tag} O'Brien \"Q\"" }, await Search(list, "O'Brien \"Q"));
            Assert.Equal(new[] { $"{tag} back\\slash" }, await Search(list, "back\\s"));
            Assert.Equal(new[] { $"{tag} café 😀" }, await Search(list, "café 😀"));
            Assert.Empty(await Search(list, $"{tag} %%"));
        }
    }
}
