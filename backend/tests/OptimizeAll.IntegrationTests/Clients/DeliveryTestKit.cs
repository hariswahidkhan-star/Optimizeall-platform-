using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Clients;

public sealed record StaffSession(TestUser User, HttpClient Client);

public sealed record ClientSession(TestUser User, HttpClient Client, Guid ClientId);

/// <summary>Records DeliverableApproved events (registered through <see cref="DeliveryTestKit.WithEventRecorder"/>).</summary>
public sealed class ApprovedEventRecorder : IEventHandler<DeliverableApproved>
{
    public static readonly ConcurrentBag<DeliverableApproved> Events = new();

    public Task HandleAsync(DeliverableApproved domainEvent, CancellationToken cancellationToken)
    {
        Events.Add(domainEvent);
        return Task.CompletedTask;
    }
}

/// <summary>Arrange helpers for the delivery (clients/projects) tests.</summary>
public static class DeliveryTestKit
{
    public static readonly JsonSerializerOptions Json = ApiFactory.Json;

    /// <summary>A host on the same database with <see cref="ApprovedEventRecorder"/> registered.</summary>
    public static WebApplicationFactory<Program> WithEventRecorder(this ApiFactory api) =>
        api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<IEventHandler<DeliverableApproved>, ApprovedEventRecorder>()));

    public static async Task<HttpClient> LoginOnAsync(this WebApplicationFactory<Program> host, TestUser user)
    {
        await host.StartAsync();
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }

    public static async Task<StaffSession> StaffAsync(this ApiFactory api, Role role = Role.AccountManager)
    {
        var (user, client) = await api.CreateClientAsync(role);
        return new StaffSession(user, client);
    }

    /// <summary>Creates a client account through the API (as an account manager) and returns its id.</summary>
    public static async Task<Guid> CreateOrgAsync(this ApiFactory api, HttpClient accountManager, string? name = null, string currency = "USD")
    {
        name ??= "Org " + Guid.NewGuid().ToString("N")[..8];
        var created = await (await accountManager.PostAsJsonAsync("/api/v1/agency/clients", new
        {
            name, countryCode = "US", timeZone = "America/New_York", currency, industry = "Testing", status = "Active",
        })).ReadJsonAsync();
        return created.GetProperty("id").GetGuid();
    }

    /// <summary>Creates a Client-role user who is a member of the organization with the duty, signed in.</summary>
    public static async Task<ClientSession> ClientUserAsync(this ApiFactory api, Guid clientId, ClientMemberRole duty)
    {
        var user = await api.CreateUserAsync(new[] { Role.Client }, countryCode: "US");
        await api.WithDbAsync(async db =>
        {
            db.Set<ClientMember>().Add(new ClientMember { ClientAccountId = clientId, UserId = user.Id, Role = duty, AddedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        return new ClientSession(user, await api.LoginAsync(user), clientId);
    }

    public static async Task<JsonElement> CreateProjectAsync(this HttpClient staff, Guid clientId, string? templateKey = null, string name = "Test project")
    {
        return await (await staff.PostAsJsonAsync("/api/v1/agency/projects", new
        {
            clientId, name, type = templateKey is null ? "OneOffCampaign" : "SeoProgram", templateKey, status = "Active",
            budgetHours = 10, budgetAmount = 1000, defaultHourlyRate = 100,
        })).ReadJsonAsync();
    }

    public static async Task<JsonElement> CreateDeliverableAsync(this HttpClient staff, Guid projectId, string title = "Hero banner")
    {
        return await (await staff.PostAsJsonAsync("/api/v1/agency/deliverables", new { projectId, title, type = "Design" })).ReadJsonAsync();
    }

    public static async Task<JsonElement> AddLinkVersionAsync(this HttpClient staff, Guid deliverableId, string link = "https://example.com/v")
    {
        using var form = new MultipartFormDataContent { { new StringContent(link), "linkUrl" }, { new StringContent("notes"), "notes" } };
        return await (await staff.PostAsync($"/api/v1/agency/deliverables/{deliverableId}/versions", form)).ReadJsonAsync();
    }

    public static int Version(this JsonElement detail) => detail.GetProperty("deliverable").GetProperty("currentVersion").GetInt32();

    public static string Status(this JsonElement detail) => detail.GetProperty("deliverable").GetProperty("status").GetString()!;

    /// <summary>Draft with one version → internal review → sent to the client (ClientReview).</summary>
    public static async Task<Guid> DeliverableAwaitingClientAsync(this HttpClient staff, Guid projectId, string title = "For review")
    {
        var d = await staff.CreateDeliverableAsync(projectId, title);
        var id = d.GetProperty("deliverable").GetProperty("id").GetGuid();
        var v = await staff.AddLinkVersionAsync(id);
        (await staff.PostAsJsonAsync($"/api/v1/agency/deliverables/{id}/submit", new { version = v.Version() })).EnsureSuccessStatusCode();
        var sent = await (await staff.PostAsJsonAsync($"/api/v1/agency/deliverables/{id}/internal-approve", new { version = v.Version() })).ReadJsonAsync();
        Assert.Equal("ClientReview", sent.Status());
        return id;
    }
}
