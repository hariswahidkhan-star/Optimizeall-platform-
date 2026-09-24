using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Clients;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.ApiContract;

/// <summary>A signed-in caller of the contract suite: one built-in role (a client user also has a member duty).</summary>
public sealed class Caller
{
    public required string Name { get; init; }
    public required Role Role { get; init; }
    public ClientMemberRole? Duty { get; init; }
    public Guid? ClientAccountId { get; init; }

    /// <summary>Effective permissions, derived from <see cref="RolePermissions"/> (the source of truth for built-in roles).</summary>
    public IReadOnlySet<string> Permissions => RolePermissions.For(new[] { Role });

    internal Guid UserId;
    internal string Token = string.Empty;
    internal int Generation;

    public override string ToString() => Name;
}

/// <summary>
/// One host for the whole contract suite: a fresh database seeded with Baseline + Demo (four demo client organizations with
/// projects, invoices, proposals, reports, social/email/SEO data), a signed-in caller per built-in role and per client duty
/// (members of client A = Nimbus), and the endpoint catalog. Tokens are minted directly (no password round trip); a caller
/// whose session is revoked by the endpoint under test (sign-out everywhere, password change, …) is replaced by a fresh user.
/// </summary>
public sealed class ContractFixture : IAsyncLifetime
{
    public ApiFactory Api { get; } = new();
    public WebApplicationFactory<Program> Host { get; private set; } = null!;
    public HttpClient Http { get; private set; } = null!;
    public IReadOnlyList<ApiEndpoint> Endpoints { get; private set; } = Array.Empty<ApiEndpoint>();

    public Guid ClientA { get; private set; }
    public Guid ClientB { get; private set; }

    /// <summary>Staff callers (one per built-in role except Client) and client callers (one per duty, members of client A).</summary>
    public IReadOnlyList<Caller> Callers { get; private set; } = Array.Empty<Caller>();

    public Caller Admin => Callers.Single(c => c.Role == Role.Admin);
    public Caller ClientOwnerA => Callers.Single(c => c.Role == Role.Client && c.Duty == ClientMemberRole.Owner);

    /// <summary>Owner of client B (for the reverse tenancy direction).</summary>
    public Caller ClientOwnerB { get; private set; } = null!;

    private string _passwordHash = string.Empty;
    private readonly SemaphoreSlim _mint = new(1, 1);

    public async Task InitializeAsync()
    {
        await Api.InitializeAsync();
        Host = Api.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Seed:1"] = "Demo" })));
        await Host.StartAsync(); // migrations (already applied) + Baseline + Demo
        Http = Host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        Http.Timeout = TimeSpan.FromMinutes(3);
        Http.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        Endpoints = EndpointCatalog.Load(Host.Services);

        (ClientA, ClientB) = await WithDbAsync(async db =>
        {
            var a = await db.Set<ClientAccount>().Where(c => c.Slug == DeliveryDemoData.Nimbus.Slug).Select(c => c.Id).SingleAsync();
            var b = await db.Set<ClientAccount>().Where(c => c.Slug == DeliveryDemoData.Aurora.Slug).Select(c => c.Id).SingleAsync();
            return (a, b);
        });
        _passwordHash = new PasswordHasher<User>().HashPassword(new User(), "Contract-Suite-Pass-1");

        var callers = new List<Caller>();
        foreach (var role in Enum.GetValues<Role>().Where(r => r != Role.Client))
            callers.Add(new Caller { Name = role.ToString(), Role = role });
        foreach (var duty in Enum.GetValues<ClientMemberRole>())
            callers.Add(new Caller { Name = $"Client({duty})", Role = Role.Client, Duty = duty, ClientAccountId = ClientA });
        ClientOwnerB = new Caller { Name = "ClientB(Owner)", Role = Role.Client, Duty = ClientMemberRole.Owner, ClientAccountId = ClientB };
        foreach (var caller in callers.Append(ClientOwnerB)) await ProvisionAsync(caller);
        Callers = callers;
    }

    public async Task DisposeAsync()
    {
        Http?.Dispose();
        if (Host is not null) await Host.DisposeAsync();
        await Api.DisposeAsync();
    }

    /// <summary>Requests in flight at once (CONTRACT_PARALLELISM, default 4).</summary>
    public static int Parallelism =>
        int.TryParse(Environment.GetEnvironmentVariable("CONTRACT_PARALLELISM"), out var p) && p > 0 ? p : 4;

    /// <summary>
    /// The caller the hostile-input matrix uses for an endpoint: anonymous for public endpoints, otherwise the admin, or, when
    /// the metadata excludes the admin (client portal), the first caller it allows (client owner first).
    /// </summary>
    public Caller? AuthorizedCallerFor(ApiEndpoint e)
    {
        if (e.AllowAnonymous) return null;
        if (e.Allows(Admin.Permissions)) return Admin;
        if (e.Allows(ClientOwnerA.Permissions)) return ClientOwnerA;
        return Callers.FirstOrDefault(c => e.Allows(c.Permissions));
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Host.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Creates a fresh user for the caller (role, client membership) and mints its access token.</summary>
    private async Task ProvisionAsync(Caller caller)
    {
        var now = Api.Clock.GetUtcNow().UtcDateTime;
        var email = $"contract-{caller.Name.ToLowerInvariant().Replace("(", "-").Replace(")", "")}-{Guid.NewGuid():N}"[..40] + "@example.test";
        var user = new User
        {
            Email = email, NormalizedEmail = Normalization.Email(email), DisplayName = "Contract " + caller.Name, CountryCode = "PK",
            LanguageCode = "en", EmailVerifiedAt = now, ReferralCode = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            PasswordHash = _passwordHash,
        };
        user.Roles.Add(new UserRole { UserId = user.Id, Role = caller.Role, GrantedAt = now });
        await WithDbAsync(async db =>
        {
            db.Add(user);
            if (caller.ClientAccountId is { } clientId)
                db.Add(new ClientMember { ClientAccountId = clientId, UserId = user.Id, Role = caller.Duty!.Value, AddedAt = now });
            await db.SaveChangesAsync();
            return true;
        });
        caller.UserId = user.Id;
        caller.Token = Host.Services.GetRequiredService<ITokenService>().CreateAccessToken(user).Token;
        caller.Generation++;
    }

    /// <summary>
    /// Sends a request as <paramref name="caller"/> (anonymous when null). A 401 for a signed-in caller means the endpoint
    /// under test (or a concurrent one) revoked the caller's session: the caller gets a fresh user and the request is retried
    /// once with a rebuilt message.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(Caller? caller, Func<HttpRequestMessage> build)
    {
        for (var attempt = 0; ; attempt++)
        {
            var generation = caller?.Generation ?? 0;
            var request = build();
            if (caller is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", caller.Token);
            var response = await Http.SendAsync(request);
            if (caller is null || response.StatusCode != HttpStatusCode.Unauthorized || attempt > 0) return response;
            // Only a rejected token (the authentication challenge) means the session was revoked; a business 401
            // (e.g. auth.session_expired without a refresh cookie) is the endpoint's answer.
            await response.Content.LoadIntoBufferAsync();
            if (!(await response.Content.ReadAsStringAsync()).Contains("\"http_401\"", StringComparison.Ordinal)) return response;
            response.Dispose();
            await _mint.WaitAsync();
            try
            {
                if (caller.Generation == generation) await ProvisionAsync(caller);
            }
            finally
            {
                _mint.Release();
            }
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ContractCollection : ICollectionFixture<ContractFixture>
{
    public const string Name = "Contract";
}

/// <summary>Runs checks over many endpoints concurrently and collects findings instead of stopping at the first one.</summary>
public sealed class Findings
{
    private readonly ConcurrentBag<string> _items = new();
    public void Add(string finding) => _items.Add(finding);
    public int Count => _items.Count;

    public void AssertEmpty(string what, int checkedCount)
    {
        var sorted = _items.OrderBy(x => x, StringComparer.Ordinal).ToList();
        // CONTRACT_REPORT=<directory>: every finding of the run, one per line (the assertion message shows the first 400).
        if (Environment.GetEnvironmentVariable("CONTRACT_REPORT") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, what + ".txt"), sorted);
        }
        if (sorted.Count == 0) return;
        throw new Xunit.Sdk.XunitException(
            $"{sorted.Count} {what} finding(s) over {checkedCount} checks:\n" + string.Join('\n', sorted.Take(400)));
    }

    public static async Task ForEachAsync<T>(IEnumerable<T> items, int parallelism, Func<T, Task> body)
    {
        using var gate = new SemaphoreSlim(parallelism);
        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync();
            try { await body(item); }
            finally { gate.Release(); }
        }).ToList();
        await Task.WhenAll(tasks);
    }
}
