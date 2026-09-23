using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Seo;

/// <summary>Arranges agency data (clients, client members) and helper calls shared by the SEO, pages and integrations tests.</summary>
public static class AgencyTestData
{
    public static async Task<ClientAccount> CreateClientAccountAsync(this ApiFactory api, string? name = null)
    {
        var slug = "client-" + Guid.NewGuid().ToString("N")[..10];
        var client = new ClientAccount
        {
            Name = name ?? "Client " + slug[^6..], Slug = slug, CountryCode = "GB", Currency = "GBP", TimeZone = "Europe/London", Status = ClientAccountStatus.Active,
        };
        await api.WithDbAsync(async db =>
        {
            db.Add(client);
            await db.SaveChangesAsync();
        });
        return client;
    }

    /// <summary>Creates a Client-role user who is a member of <paramref name="clientId"/> and returns a signed-in HttpClient.</summary>
    public static async Task<(TestUser User, HttpClient Client)> CreateClientUserAsync(this ApiFactory api, Guid clientId,
        ClientMemberRole role = ClientMemberRole.Viewer)
    {
        var user = await api.CreateUserAsync(new[] { Role.Client });
        await api.WithDbAsync(async db =>
        {
            db.Add(new ClientMember { ClientAccountId = clientId, UserId = user.Id, Role = role, AddedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        return (user, await api.LoginAsync(user));
    }

    /// <summary>Overrides configuration at runtime (in-memory provider) and reloads, so IOptionsMonitor sees the change.</summary>
    public static void SetConfig(this WebApplicationFactory<Program> factory, params (string Key, string? Value)[] values)
    {
        var root = (IConfigurationRoot)factory.Services.GetRequiredService<IConfiguration>();
        foreach (var (key, value) in values) root[key] = value;
        root.Reload();
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) => await response.ReadJsonAsync();

    public static StringContent Json(object value) => new(JsonSerializer.Serialize(value, ApiFactory.Json), System.Text.Encoding.UTF8, "application/json");

    public static MultipartFormDataContent CsvUpload(string csv, string fileName = "import.csv", string? date = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", fileName);
        if (date is not null) content.Add(new StringContent(date), "date");
        return content;
    }
}

/// <summary>
/// Test-only middleware that sets the connection's remote IP from the X-Test-Ip header (TestServer leaves it empty), so
/// per-IP rules (rate limits, IP hashes) can be exercised.
/// </summary>
public sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (ctx, nextMiddleware) =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.TryParse(ctx.Request.Headers[Header].ToString(), out var ip) ? ip : IPAddress.Parse("203.0.113.10");
            await nextMiddleware(ctx);
        });
        next(app);
    };
}

public static class DerivedFactoryAuth
{
    /// <summary>Signs a user in against any factory (e.g. one derived with WithWebHostBuilder).</summary>
    public static async Task<HttpClient> LoginAsync(this WebApplicationFactory<Program> factory, TestUser user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }
}
