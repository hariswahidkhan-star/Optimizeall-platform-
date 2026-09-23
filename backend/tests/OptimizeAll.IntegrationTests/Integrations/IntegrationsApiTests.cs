using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Integrations;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Seo;

namespace OptimizeAll.IntegrationTests.Integrations;

/// <summary>Fake provider endpoint for verification calls (no internet). Answers per host.</summary>
public sealed class FakeProviderHandler : HttpMessageHandler
{
    public static ConcurrentDictionary<string, HttpStatusCode> StatusByHost { get; } = new();
    public static ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        var status = StatusByHost.GetValueOrDefault(request.RequestUri!.Host, HttpStatusCode.OK);
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(status == HttpStatusCode.OK ? "{\"status_code\":20000,\"tasks\":[]}" : "{}", Encoding.UTF8, "application/json"),
        });
    }
}

public sealed class IntegrationsFixture : IAsyncLifetime
{
    public ApiFactory Api { get; } = new();
    public WebApplicationFactory<Program> Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Api.InitializeAsync();
        Host = Api.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddHttpClient(IntegrationVerifier.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new FakeProviderHandler())));
        _ = Host.Services;
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        await Api.DisposeAsync();
    }
}

public sealed class IntegrationsApiTests(IntegrationsFixture fx) : IClassFixture<IntegrationsFixture>
{
    private const string Password = "super-secret-dfs-password-123";
    private const string Login = "api-login@agency.test";

    private async Task<HttpClient> AdminAsync() => await fx.Host.LoginAsync(await fx.Api.CreateUserAsync(new[] { Role.Admin }));

    private static async Task<string> RawAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        return text;
    }

    [Fact]
    public async Task Integration_management_requires_the_permission()
    {
        var anonymous = fx.Host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/agency/integrations/providers")).StatusCode);
        foreach (var role in new[] { Role.SeoSpecialist, Role.Designer, Role.AccountManager, Role.Client })
        {
            var user = await fx.Host.LoginAsync(await fx.Api.CreateUserAsync(new[] { role }));
            await (await user.GetAsync("/api/v1/agency/integrations/connections")).ShouldFailAsync(403);
            await (await user.PostAsJsonAsync("/api/v1/agency/integrations/connections", new { provider = "dataforseo", displayName = "x" })).ShouldFailAsync(403);
        }
        var providers = await (await (await AdminAsync()).GetAsync("/api/v1/agency/integrations/providers")).ReadJsonAsync();
        Assert.Equal(22, providers.GetArrayLength());
    }

    [Fact]
    public async Task Secrets_are_write_only_encrypted_and_kept_on_partial_updates()
    {
        var admin = await AdminAsync();
        var created = await admin.PostAsJsonAsync("/api/v1/agency/integrations/connections", new
        {
            provider = "dataforseo", displayName = "DataForSEO (agency)", secrets = new { login = Login, password = Password },
        });
        var createdJson = await RawAsync(created);
        Assert.DoesNotContain(Password, createdJson);
        Assert.DoesNotContain(Login, createdJson);
        var connection = System.Text.Json.JsonDocument.Parse(createdJson).RootElement;
        var id = connection.GetProperty("id").GetGuid();
        Assert.Equal("Unverified", connection.GetProperty("status").GetString());
        Assert.All(connection.GetProperty("secrets").EnumerateArray(), s => Assert.True(s.GetProperty("saved").GetBoolean()));

        Assert.DoesNotContain(Password, await RawAsync(await admin.GetAsync("/api/v1/agency/integrations/connections")));
        Assert.DoesNotContain(Password, await RawAsync(await admin.GetAsync($"/api/v1/agency/integrations/connections/{id}")));
        var stored = await fx.Api.WithDbAsync(db => db.Set<IntegrationConnection>().AsNoTracking().SingleAsync(c => c.Id == id));
        Assert.DoesNotContain(Password, stored.EncryptedSecrets);

        // Rename without secrets: saved secrets are kept.
        (await admin.PutAsJsonAsync($"/api/v1/agency/integrations/connections/{id}", new { displayName = "DataForSEO main", secrets = new { password = "" } }))
            .EnsureSuccessStatusCode();
        using (var scope = fx.Host.Services.CreateScope())
        {
            var creds = await scope.ServiceProvider.GetRequiredService<ICredentialVault>().GetAsync("dataforseo", null);
            Assert.Equal(Password, creds!.Secrets["password"]);
            Assert.Equal(Login, creds.Secrets["login"]);
        }

        // Replace one secret.
        (await admin.PutAsJsonAsync($"/api/v1/agency/integrations/connections/{id}", new { displayName = "DataForSEO main", secrets = new { password = "rotated-password-456" } }))
            .EnsureSuccessStatusCode();
        using (var scope = fx.Host.Services.CreateScope())
        {
            var creds = await scope.ServiceProvider.GetRequiredService<ICredentialVault>().GetAsync("dataforseo", null);
            Assert.Equal("rotated-password-456", creds!.Secrets["password"]);
            Assert.Equal(Login, creds.Secrets["login"]);
        }

        // Audit rows describe the changes but never contain secret values.
        var audits = await fx.Api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking().Where(a => a.EntityId == id.ToString()).ToListAsync());
        Assert.Contains(audits, a => a.Action == "integration.created");
        Assert.Contains(audits, a => a.Action == "integration.updated" && a.AfterJson!.Contains("password"));
        Assert.All(audits, a =>
        {
            Assert.DoesNotContain(Password, (a.BeforeJson ?? "") + a.AfterJson);
            Assert.DoesNotContain("rotated-password-456", (a.BeforeJson ?? "") + a.AfterJson);
            Assert.DoesNotContain(Login, (a.BeforeJson ?? "") + a.AfterJson);
        });

        // Only one live connection per provider and scope.
        await (await admin.PostAsJsonAsync("/api/v1/agency/integrations/connections", new
        {
            provider = "dataforseo", displayName = "Second", secrets = new { login = "a", password = "b" },
        })).ShouldFailAsync(409, "integrations.exists");

        // Disconnect wipes the credentials; adapters then see "not configured".
        var disconnected = await (await admin.PostAsync($"/api/v1/agency/integrations/connections/{id}/disconnect", null)).ReadJsonAsync();
        Assert.Equal("Disconnected", disconnected.GetProperty("status").GetString());
        Assert.All(disconnected.GetProperty("secrets").EnumerateArray(), s => Assert.False(s.GetProperty("saved").GetBoolean()));
        using (var scope = fx.Host.Services.CreateScope())
            Assert.Null(await scope.ServiceProvider.GetRequiredService<ICredentialVault>().GetAsync("dataforseo", null));
        (await admin.DeleteAsync($"/api/v1/agency/integrations/connections/{id}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Settings_and_secrets_are_validated_against_the_provider_descriptor()
    {
        var admin = await AdminAsync();
        var client = await fx.Api.CreateClientAccountAsync();
        var missing = await admin.PostAsJsonAsync("/api/v1/agency/integrations/connections", new
        {
            provider = "twilio", clientAccountId = client.Id, displayName = "SMS", settings = new { accountSid = "nope", shoeSize = "44" }, secrets = new { },
        });
        await missing.ShouldFailAsync(400, "integrations.invalid");
        var body = await missing.Content.ReadAsStringAsync();
        foreach (var key in new[] { "settings.accountSid", "settings.fromNumber", "settings.shoeSize", "secrets.authToken" }) Assert.Contains(key, body);

        await (await admin.PostAsJsonAsync("/api/v1/agency/integrations/connections", new { provider = "myspace", displayName = "Old" }))
            .ShouldFailAsync(400, "integrations.unknown_provider");
        await (await admin.PostAsJsonAsync("/api/v1/agency/integrations/connections", new
        {
            provider = "meta", displayName = "Meta", settings = new { pageId = "123" }, secrets = new { pageAccessToken = "t" },
        })).ShouldFailAsync(400, "integrations.client_only");
        await (await admin.PostAsJsonAsync("/api/v1/agency/integrations/connections", new
        {
            provider = "stripe", clientAccountId = client.Id, displayName = "Stripe", settings = new { publishableKey = "pk_test_abc" }, secrets = new { secretKey = "sk_test_abc" },
        })).ShouldFailAsync(400, "integrations.agency_only");
        await (await admin.PostAsJsonAsync("/api/v1/agency/integrations/connections", new
        {
            provider = "meta", clientAccountId = Guid.NewGuid(), displayName = "Meta", settings = new { pageId = "123" }, secrets = new { pageAccessToken = "t" },
        })).ShouldFailAsync(404);

        var ok = await (await admin.PostAsJsonAsync("/api/v1/agency/integrations/connections", new
        {
            provider = "twilio", clientAccountId = client.Id, displayName = "SMS",
            settings = new { accountSid = "AC" + new string('b', 32), fromNumber = "+14155550100" }, secrets = new { authToken = "twilio-token" },
        })).ReadJsonAsync();
        Assert.Equal(client.Id, ok.GetProperty("clientAccountId").GetGuid());
        var scoped = await (await admin.GetAsync($"/api/v1/agency/integrations/connections?clientId={client.Id}")).ReadJsonAsync();
        Assert.Single(scoped.EnumerateArray());
    }

    [Fact]
    public async Task Test_connection_uses_the_provider_verifier_or_marks_unverified()
    {
        var admin = await AdminAsync();
        var client = await fx.Api.CreateClientAccountAsync();
        var sendgrid = await (await admin.PostAsJsonAsync("/api/v1/agency/integrations/connections", new
        {
            provider = "sendgrid", clientAccountId = client.Id, displayName = "SendGrid", settings = new { fromEmail = "news@client.test" },
            secrets = new { apiKey = "SG.abcdefghijklmnopqrstuvwxyz0123456789" },
        })).ReadJsonAsync();
        var sendgridId = sendgrid.GetProperty("id").GetGuid();

        FakeProviderHandler.StatusByHost["api.sendgrid.com"] = HttpStatusCode.OK;
        var connected = await (await admin.PostAsync($"/api/v1/agency/integrations/connections/{sendgridId}/test", null)).ReadJsonAsync();
        Assert.Equal("Connected", connected.GetProperty("status").GetString());
        Assert.Contains(FakeProviderHandler.Requests, r => r.RequestUri!.AbsoluteUri == "https://api.sendgrid.com/v3/scopes"
                                                          && r.Headers.Authorization!.Parameter == "SG.abcdefghijklmnopqrstuvwxyz0123456789");

        FakeProviderHandler.StatusByHost["api.sendgrid.com"] = HttpStatusCode.Unauthorized;
        var rejected = await (await admin.PostAsync($"/api/v1/agency/integrations/connections/{sendgridId}/test", null)).ReadJsonAsync();
        Assert.Equal("Error", rejected.GetProperty("status").GetString());
        Assert.Contains("rejected", rejected.GetProperty("statusMessage").GetString());

        var meta = await (await admin.PostAsJsonAsync("/api/v1/agency/integrations/connections", new
        {
            provider = "meta", clientAccountId = client.Id, displayName = "Meta page", settings = new { pageId = "1234567890" }, secrets = new { pageAccessToken = "EAAB-token" },
        })).ReadJsonAsync();
        var unverified = await (await admin.PostAsync($"/api/v1/agency/integrations/connections/{meta.GetProperty("id").GetGuid()}/test", null)).ReadJsonAsync();
        Assert.Equal("Unverified", unverified.GetProperty("status").GetString());
        Assert.Contains("not available", unverified.GetProperty("statusMessage").GetString());
        Assert.DoesNotContain(FakeProviderHandler.Requests, r => r.RequestUri!.Host.Contains("facebook", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Expiry_job_warns_once_and_marks_expired_tokens()
    {
        var admin = await AdminAsync();
        var client = await fx.Api.CreateClientAccountAsync();
        var now = fx.Api.Clock.GetUtcNow().UtcDateTime;
        var expiring = await (await admin.PostAsJsonAsync("/api/v1/agency/integrations/connections", new
        {
            provider = "linkedin", clientAccountId = client.Id, displayName = "LinkedIn page", settings = new { organizationId = "123456" },
            secrets = new { accessToken = "li-token" }, expiresAt = now.AddDays(5),
        })).ReadJsonAsync();
        Assert.True(expiring.GetProperty("expiringSoon").GetBoolean());
        var expired = await (await admin.PostAsJsonAsync("/api/v1/agency/integrations/connections", new
        {
            provider = "tiktok", clientAccountId = client.Id, displayName = "TikTok", settings = new { openId = "open-1" },
            secrets = new { accessToken = "tt-token" }, expiresAt = now.AddDays(-1),
        })).ReadJsonAsync();

        var runner = fx.Host.Services.GetRequiredService<JobRunner>();
        await runner.RunAsync<IntegrationExpiryJob>();
        await runner.RunAsync<IntegrationExpiryJob>();

        var id = expiring.GetProperty("id").GetGuid().ToString();
        Assert.Equal(1, await fx.Api.WithDbAsync(db => db.Set<AuditLog>().CountAsync(a => a.Action == IntegrationExpiryJob.WarningAction && a.EntityId == id)));
        Assert.True(await fx.Api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.Type == "integrations.expiring")));
        var expiredRow = await (await admin.GetAsync($"/api/v1/agency/integrations/connections/{expired.GetProperty("id").GetGuid()}")).ReadJsonAsync();
        Assert.Equal("Error", expiredRow.GetProperty("status").GetString());
        Assert.Contains("expired", expiredRow.GetProperty("statusMessage").GetString());
    }
}
