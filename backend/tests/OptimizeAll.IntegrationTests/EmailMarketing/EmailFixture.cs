using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.EmailMarketing;

[CollectionDefinition(Name)]
public sealed class EmailCollection : ICollectionFixture<EmailFixture>
{
    public const string Name = "email-marketing";
}

/// <summary>Records every marketing email instead of sending it; its outcome can be switched per test.</summary>
public sealed class CapturingEmailProvider : IEmailMarketingProvider
{
    public ConcurrentQueue<OutboundEmail> Sent { get; } = new();
    public ProviderOutcome NextOutcome { get; set; } = ProviderOutcome.Accepted;
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
    /// <summary>Delivers the message, then throws (a bug or lost connection after the provider accepted it).</summary>
    public bool ThrowAfterSending { get; set; }

    public string Key => "smtp";

    public async Task<ProviderResult> SendAsync(OutboundEmail email, CancellationToken ct)
    {
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        switch (NextOutcome)
        {
            case ProviderOutcome.NotConfigured: return ProviderResult.NotConfigured("Test provider not configured.");
            case ProviderOutcome.TransientFailure: return ProviderResult.Transient("Test transient failure.");
            case ProviderOutcome.PermanentFailure: return ProviderResult.Permanent("Test rejection.");
            case ProviderOutcome.Unknown:
                Sent.Enqueue(email); // the provider did deliver it, but did not confirm
                return ProviderResult.Unknown("Test: accepted without a message id.");
        }
        Sent.Enqueue(email);
        if (ThrowAfterSending) throw new InvalidOperationException("Test: connection lost after the message was accepted.");
        return ProviderResult.Accepted("<" + Guid.NewGuid().ToString("N") + "@test>");
    }

    public IReadOnlyList<OutboundEmail> For(Guid? clientId) => Sent.Where(e => e.ClientAccountId == clientId).ToList();
}

public sealed class CapturingSmsProvider : ISmsProvider
{
    public ConcurrentQueue<(Guid? ClientId, string To, string Body)> Sent { get; } = new();
    public string Key => "twilio";

    public Task<ProviderResult> SendAsync(Guid? clientAccountId, string toE164, string body, string? statusCallbackUrl, CancellationToken ct)
    {
        Sent.Enqueue((clientAccountId, toE164, body));
        return Task.FromResult(ProviderResult.Accepted("SM" + Guid.NewGuid().ToString("N")));
    }
}

/// <summary>
/// One database for all email-marketing tests (a collection fixture keeps test-DB usage modest). Tests isolate
/// themselves by creating their own client workspace. Providers are replaced with capturing fakes.
/// </summary>
public sealed class EmailFixture : IAsyncLifetime
{
    public const string SigningSecret = "email-test-postback-secret";

    public ApiFactory Api { get; } = new();
    public WebApplicationFactory<Program> App { get; private set; } = null!;
    public CapturingEmailProvider Email { get; } = new();
    public CapturingSmsProvider Sms { get; } = new();
    private readonly Dictionary<Role, (TestUser User, HttpClient Client)> _staff = new();
    private readonly SemaphoreSlim _staffLock = new(1, 1);

    public async Task InitializeAsync()
    {
        await Api.InitializeAsync();
        App = Api.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tracking:PostbackSecret"] = SigningSecret,
                ["EmailMarketing:PublicBaseUrl"] = "http://app.test",
                // Tests move the clock forward by hours/days (A/B waits, journeys); keep sessions valid meanwhile.
                ["Jwt:AccessTokenMinutes"] = "525600",
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(Email);
                services.AddScoped<IEmailMarketingProvider>(sp => sp.GetRequiredService<CapturingEmailProvider>());
                services.AddSingleton<ISmsProvider>(Sms);
            });
        });
        await App.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await App.DisposeAsync();
        await Api.DisposeAsync();
    }

    public DateTime Now => Api.Clock.GetUtcNow().UtcDateTime;

    public async Task<T> Db<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = App.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task Db(Func<AppDbContext, Task> action) => Db(async db => { await action(db); return true; });

    public Task RunJobAsync<TJob>() where TJob : IJob => App.Services.GetRequiredService<JobRunner>().RunAsync<TJob>();

    public HttpClient Anonymous() => App.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task<HttpClient> LoginAsync(TestUser user)
    {
        var client = App.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>A signed-in staff member with the role (cached per role).</summary>
    public async Task<HttpClient> StaffAsync(Role role = Role.Admin)
    {
        await _staffLock.WaitAsync();
        try
        {
            if (_staff.TryGetValue(role, out var existing)) return existing.Client;
            var user = await Api.CreateUserAsync(new[] { role });
            var client = await LoginAsync(user);
            _staff[role] = (user, client);
            return client;
        }
        finally { _staffLock.Release(); }
    }

    public async Task<TestUser> StaffUserAsync(Role role = Role.Admin)
    {
        await StaffAsync(role);
        return _staff[role].User;
    }

    // ---------- Arrange helpers ----------

    public sealed record Workspace(Guid ClientId, Guid SenderId, Guid ListId);

    /// <summary>A client with email settings (address), a verified sender and a list without double opt-in.</summary>
    public async Task<Workspace> CreateWorkspaceAsync(bool requireApproval = false, bool doubleOptIn = false, string timeZone = "UTC")
    {
        var slug = "c-" + Guid.NewGuid().ToString("N")[..12];
        var client = new ClientAccount { Name = "Client " + slug, Slug = slug, CountryCode = "US", Currency = "USD", TimeZone = timeZone, Status = ClientAccountStatus.Active };
        var key = OptimizeAll.Domain.EmailMarketing.Workspace.Key(client.Id);
        var sender = new SenderProfile { ClientAccountId = client.Id, ScopeKey = key, FromName = "Brand", FromEmail = $"news@{slug}.example.com", IsDefault = true, VerifiedAt = Now };
        var list = new EmailList { ClientAccountId = client.Id, ScopeKey = key, Name = "Newsletter", DoubleOptIn = doubleOptIn, PublicKey = Guid.NewGuid().ToString("N")[..16] };
        await Db(async db =>
        {
            db.Add(client);
            db.Add(new EmailWorkspaceSettings
            {
                ClientAccountId = client.Id, ScopeKey = key, OrganizationName = "Brand Inc", PhysicalAddress = "1 Main Street, Springfield",
                RequireClientApproval = requireApproval, DefaultTimeZone = timeZone,
            });
            db.Add(sender);
            db.Add(list);
            await db.SaveChangesAsync();
        });
        return new Workspace(client.Id, sender.Id, list.Id);
    }

    /// <summary>Adds consented, subscribed contacts to the list directly. Returns their ids.</summary>
    public async Task<List<Guid>> AddSubscribersAsync(Workspace ws, int count, Action<Subscriber, int>? configure = null)
    {
        var ids = new List<Guid>();
        var key = OptimizeAll.Domain.EmailMarketing.Workspace.Key(ws.ClientId);
        await Db(async db =>
        {
            for (var i = 0; i < count; i++)
            {
                var email = $"s{i}-{Guid.NewGuid():N}"[..20] + "@example.com";
                var s = new Subscriber
                {
                    ClientAccountId = ws.ClientId, ScopeKey = key, Email = email, NormalizedEmail = email, FirstName = "Sub" + i, Source = "test",
                    EmailConsent = ConsentStatus.Granted, EmailConsentAt = Now, Status = SubscriberStatus.Subscribed, CountryCode = "US",
                };
                configure?.Invoke(s, i);
                db.Add(s);
                db.Add(new ListMembership { ListId = ws.ListId, SubscriberId = s.Id, Status = MembershipStatus.Subscribed, CreatedAt = Now, SubscribedAt = Now });
                ids.Add(s.Id);
            }
            await db.SaveChangesAsync();
        });
        return ids;
    }

    public static object Design(string body = "<p>Hello {{first_name|there}}</p>", bool footer = true, string link = "https://shop.example.com/offer")
    {
        var blocks = new List<object>
        {
            new { type = "header", title = "News" },
            new { type = "text", html = body },
            new { type = "button", text = "Shop now", href = link },
        };
        if (footer) blocks.Add(new { type = "footer" });
        return new { blocks };
    }

    public async Task<JsonElement> CreateCampaignAsync(HttpClient staff, Workspace ws, string? name = null, object? design = null, object? extra = null,
        string path = "/api/v1/agency/email/campaigns", string channel = "Email")
    {
        var body = new Dictionary<string, object?>
        {
            ["clientAccountId"] = ws.ClientId,
            ["name"] = name ?? "Campaign " + Guid.NewGuid().ToString("N")[..8],
            ["channel"] = channel,
            ["listId"] = ws.ListId,
            ["senderProfileId"] = ws.SenderId,
            ["subject"] = "Hello {{first_name|there}}",
            ["design"] = design ?? Design(),
        };
        if (extra is not null)
            foreach (var p in JsonSerializer.SerializeToElement(extra, ApiFactory.Json).EnumerateObject())
                body[p.Name] = p.Value;
        return await (await staff.PostAsJsonAsync(path, body, ApiFactory.Json)).ReadJsonAsync();
    }

    public static async Task<JsonElement> ConfirmSendAsync(HttpClient staff, JsonElement campaign, string path = "/api/v1/agency/email/campaigns")
    {
        var id = campaign.GetProperty("id").GetString();
        var response = await staff.PostAsJsonAsync($"{path}/{id}/send", new
        {
            confirm = true,
            confirmName = campaign.GetProperty("name").GetString(),
            concurrencyStamp = campaign.GetProperty("concurrencyStamp").GetString(),
        });
        return await response.ReadJsonAsync();
    }

    public Task<List<CampaignRecipient>> RecipientsAsync(Guid campaignId) =>
        Db(db => db.Set<CampaignRecipient>().AsNoTracking().Where(r => r.CampaignId == campaignId).ToListAsync());

    public Task<EmailCampaign> CampaignAsync(Guid campaignId) => Db(db => db.Set<EmailCampaign>().AsNoTracking().FirstAsync(c => c.Id == campaignId));

    /// <summary>Stores integration credentials (encrypted) for a workspace, as the Integrations module would.</summary>
    public async Task AddIntegrationAsync(Guid? clientId, string provider, Dictionary<string, string> settings, Dictionary<string, string> secrets)
    {
        using var scope = App.Services.CreateScope();
        var vault = scope.ServiceProvider.GetRequiredService<ICredentialVault>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Add(new IntegrationConnection
        {
            Provider = provider, ClientAccountId = clientId, DisplayName = provider, SettingsJson = JsonSerializer.Serialize(settings),
            EncryptedSecrets = vault.Protect(secrets), Status = IntegrationStatus.Connected,
        });
        await db.SaveChangesAsync();
    }

    public async Task<(TestUser User, HttpClient Client)> ClientUserAsync(Guid clientId, ClientMemberRole role)
    {
        var user = await Api.CreateUserAsync(new[] { Role.Client });
        await Db(async db =>
        {
            db.Add(new ClientMember { ClientAccountId = clientId, UserId = user.Id, Role = role, AddedAt = Now });
            await db.SaveChangesAsync();
        });
        return (user, await LoginAsync(user));
    }
}
