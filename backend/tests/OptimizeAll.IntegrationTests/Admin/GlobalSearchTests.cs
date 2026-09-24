using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Projects;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Admin;

/// <summary>GET /api/v1/search — the staff command palette.</summary>
public sealed class GlobalSearchTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    /// <summary>Creates one record of every searchable type whose name contains <paramref name="token"/>.</summary>
    private async Task<Guid> SeedAsync(string token)
    {
        var clientId = Guid.Empty;
        await api.WithDbAsync(async db =>
        {
            var client = new ClientAccount { Name = $"Acme {token}", Slug = $"acme-{token}".ToLowerInvariant(), Industry = "Retail" };
            db.Add(client);
            clientId = client.Id;
            db.Add(new CrmContact { FirstName = "Dana", LastName = token, Email = $"dana.{token}@example.test".ToLowerInvariant() });
            var stage = await db.Set<PipelineStage>().OrderBy(s => s.Position).FirstOrDefaultAsync();
            if (stage is null)
            {
                stage = new PipelineStage { Name = "New", Position = 0 };
                db.Add(stage);
            }
            db.Add(new CrmDeal { Title = $"Website rebuild {token}", StageId = stage.Id, StageChangedAt = DateTime.UtcNow });
            db.Add(new Project { ClientAccountId = client.Id, Name = $"Launch {token}" });
            db.Add(new Invoice { ClientAccountId = client.Id, Number = $"INV-{token}" });
            await db.SaveChangesAsync();
        });
        await api.CreateUserAsync(email: $"{token}@example.test".ToLowerInvariant());
        return clientId;
    }

    private static string Token() => "Zq" + Guid.NewGuid().ToString("N")[..10];

    private static Dictionary<string, List<JsonElement>> Groups(JsonElement result) =>
        result.GetProperty("groups").EnumerateArray().ToDictionary(g => g.GetProperty("type").GetString()!,
            g => g.GetProperty("items").EnumerateArray().ToList());

    [Fact]
    public async Task Admin_finds_every_type_with_links_to_the_portal_pages()
    {
        var token = Token();
        var clientId = await SeedAsync(token);
        var (_, admin) = await api.CreateClientAsync(Role.Admin);

        var result = await (await admin.GetAsync($"/api/v1/search?q={token.ToLowerInvariant()}")).ReadJsonAsync();
        var groups = Groups(result);
        Assert.Equal(new[] { "clients", "contacts", "deals", "projects", "invoices", "users" }.Order(), groups.Keys.Order());
        var client = Assert.Single(groups["clients"]);
        Assert.Equal($"/agency/clients/{clientId}", client.GetProperty("url").GetString());
        Assert.StartsWith("/agency/crm/deals/", Assert.Single(groups["deals"]).GetProperty("url").GetString());
        Assert.StartsWith("/admin/users/", Assert.Single(groups["users"]).GetProperty("url").GetString());
        Assert.Contains($"Acme {token}", Assert.Single(groups["invoices"]).GetProperty("subtitle").GetString());
    }

    [Fact]
    public async Task Results_are_limited_to_the_types_the_caller_may_open()
    {
        var token = Token();
        await SeedAsync(token);
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep); // crm.view, clients.view, billing.view
        var groups = Groups(await (await sales.GetAsync($"/api/v1/search?q={token}")).ReadJsonAsync());
        Assert.Contains("contacts", groups.Keys);
        Assert.Contains("invoices", groups.Keys);
        Assert.DoesNotContain("projects", groups.Keys);
        Assert.DoesNotContain("users", groups.Keys);

        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer); // users.view, campaigns.view only
        var reviewerGroups = Groups(await (await reviewer.GetAsync($"/api/v1/search?q={token}")).ReadJsonAsync());
        Assert.Equal(new[] { "users" }, reviewerGroups.Keys);

        var (_, participant) = await api.CreateClientAsync();
        await (await participant.GetAsync($"/api/v1/search?q={token}")).ShouldFailAsync(403, "search.forbidden");
        var (_, clientUser) = await api.CreateClientAsync(Role.Client);
        await (await clientUser.GetAsync($"/api/v1/search?q={token}")).ShouldFailAsync(403, "search.forbidden");
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"/api/v1/search?q={token}")).StatusCode);
    }

    [Fact]
    public async Task Short_queries_are_rejected_and_each_type_is_capped()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        await (await admin.GetAsync("/api/v1/search?q=%20a%20")).ShouldFailAsync(400, "search.query_too_short");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/search?q=abc&limit=11")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/v1/search?q={new string('x', 101)}")).StatusCode);

        var token = Token();
        await api.WithDbAsync(async db =>
        {
            for (var i = 0; i < 7; i++) db.Add(new ClientAccount { Name = $"Many {token} {i}", Slug = $"many-{token}-{i}".ToLowerInvariant() });
            await db.SaveChangesAsync();
        });
        Assert.Equal(5, Groups(await (await admin.GetAsync($"/api/v1/search?q={token}")).ReadJsonAsync())["clients"].Count);
        Assert.Equal(7, Groups(await (await admin.GetAsync($"/api/v1/search?q={token}&limit=10")).ReadJsonAsync())["clients"].Count);

        // LIKE wildcards in the input are literal.
        Assert.Empty((await (await admin.GetAsync("/api/v1/search?q=%25%25_")).ReadJsonAsync()).GetProperty("groups").EnumerateArray());
    }

    [Fact]
    public async Task Search_is_rate_limited_per_user()
    {
        await using var limited = api.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Enabled"] = "true", ["RateLimiting:SearchPerMinute"] = "3" })));
        await limited.StartAsync();
        var user = await api.CreateUserAsync(new[] { Role.Admin });
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password });
        var token = (await login.ReadJsonAsync()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++) statuses.Add((await client.GetAsync("/api/v1/search?q=acme")).StatusCode);
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests }, statuses);
    }
}
