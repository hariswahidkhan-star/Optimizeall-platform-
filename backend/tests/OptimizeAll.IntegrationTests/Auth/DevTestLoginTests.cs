using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Auth;

public sealed class DevTestLoginTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private WebApplicationFactory<Program> With(bool enabled, string? environment = null) =>
        api.WithWebHostBuilder(b =>
        {
            if (environment is not null) b.UseEnvironment(environment);
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevTools:TestLoginEnabled"] = enabled ? "true" : "false",
                // Production insists on SMTP; the sender is never used here.
                ["Email:Mode"] = environment == "Production" ? "Smtp" : "File",
            }));
        });

    private static HttpClient Browser(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        return client;
    }

    private async Task<Guid> CreateTestUserAsync()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var created = await (await admin.PostAsJsonAsync("/api/v1/admin/test-users", new { roles = new[] { "Participant" } })).ReadJsonAsync();
        return created.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Disabled_by_default_the_endpoints_do_not_exist()
    {
        var testUserId = await CreateTestUserAsync();
        var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/dev/test-accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/dev/test-login", new { userId = testUserId })).StatusCode);
    }

    [Fact]
    public async Task Enabled_outside_production_it_lists_and_signs_in_test_and_demo_accounts_only()
    {
        var testUserId = await CreateTestUserAsync();
        var demo = await api.CreateUserAsync(email: $"qa-{Guid.NewGuid():N}@demo.optimizeall.app");
        var real = await api.CreateUserAsync();
        await using var factory = With(enabled: true);
        var browser = Browser(factory);

        var accounts = await (await browser.GetAsync("/api/v1/dev/test-accounts")).ReadJsonAsync();
        var ids = accounts.EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(testUserId, ids);
        Assert.Contains(demo.Id, ids);
        Assert.DoesNotContain(real.Id, ids);

        var session = await (await browser.PostAsJsonAsync("/api/v1/dev/test-login", new { userId = testUserId })).ReadJsonAsync();
        Assert.Equal(testUserId, session.GetProperty("user").GetProperty("id").GetGuid());
        Assert.True(session.GetProperty("user").GetProperty("isTestAccount").GetBoolean());
        // A normal session: the refresh cookie works.
        var refreshed = await (await browser.PostAsync("/api/v1/auth/refresh", null)).ReadJsonAsync();
        Assert.Equal(testUserId, refreshed.GetProperty("user").GetProperty("id").GetGuid());
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "auth.test_login" && a.EntityId == testUserId.ToString())));

        var demoSession = await (await browser.PostAsJsonAsync("/api/v1/dev/test-login", new { userId = demo.Id })).ReadJsonAsync();
        Assert.Equal(demo.Id, demoSession.GetProperty("user").GetProperty("id").GetGuid());

        // Real (non-test, non-demo) accounts can never be entered this way.
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsJsonAsync("/api/v1/dev/test-login", new { userId = real.Id })).StatusCode);
    }

    [Fact]
    public async Task Production_refuses_even_when_the_flag_is_set()
    {
        var testUserId = await CreateTestUserAsync();
        await using var factory = With(enabled: true, environment: "Production");
        var browser = Browser(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/api/v1/dev/test-accounts")).StatusCode);
        var response = await browser.PostAsJsonAsync("/api/v1/dev/test-login", new { userId = testUserId });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
    }
}
