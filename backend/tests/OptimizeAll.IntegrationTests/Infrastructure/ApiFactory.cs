using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API against a fresh database (created from migrations) with a controllable clock: a uniquely named
/// MySQL database, or (OPTIMIZEALL_TEST_PROVIDER=Sqlite) a temporary SQLite file of its own (a real file, not
/// in-memory, so WAL, the busy timeout and write-lock contention behave as in production); either is deleted on
/// dispose. Background jobs are disabled; tests run them explicitly through <see cref="RunJobAsync{TJob}"/>.
///
/// Environment variables:
///   OPTIMIZEALL_TEST_PROVIDER "MySql" (default) or "Sqlite"
///   OPTIMIZEALL_TEST_MYSQL   server connection string without database
///                            (default "Server=127.0.0.1;Port=3306;User=optimizeall;Password=optimizeall_dev;")
///   OPTIMIZEALL_TEST_DB_INIT "Migrate" (default) or "EnsureCreated"
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The provider the integration tests run on (OPTIMIZEALL_TEST_PROVIDER).</summary>
    public static readonly DatabaseProvider Provider =
        Enum.TryParse<DatabaseProvider>(Environment.GetEnvironmentVariable("OPTIMIZEALL_TEST_PROVIDER"), ignoreCase: true, out var p)
            ? p
            : DatabaseProvider.MySql;

    public static bool IsSqlite => Provider == DatabaseProvider.Sqlite;

    private readonly string _databaseName = $"oa_test_{Guid.NewGuid():N}"[..30];
    private readonly string _sqliteFile = Path.Combine(Path.GetTempPath(), $"oa-test-{Guid.NewGuid():N}.db");
    private readonly string _serverConnection =
        Environment.GetEnvironmentVariable("OPTIMIZEALL_TEST_MYSQL")
        ?? "Server=127.0.0.1;Port=3306;User=optimizeall;Password=optimizeall_dev;";

    public string MailDirectory { get; } = Path.Combine(Path.GetTempPath(), "oa-mail-" + Guid.NewGuid().ToString("N"));
    public string StorageDirectory { get; } = Path.Combine(Path.GetTempPath(), "oa-files-" + Guid.NewGuid().ToString("N"));

    /// <summary>Test clock. Starts at the real current time; advance it to cross cutoffs, holds and expiries.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    // Each test host gets a small pool so many parallel test classes stay under MySQL's max_connections.
    public string ConnectionString => IsSqlite
        ? new SqliteConnectionStringBuilder { DataSource = _sqliteFile, Pooling = true }.ConnectionString
        : $"{_serverConnection.TrimEnd(';')};Database={_databaseName};Maximum Pool Size=20;";

    /// <summary>For CREATE/DROP DATABASE only: unpooled, so these one-off admin connections never linger idle.</summary>
    /// <summary>A generous command timeout: on a busy shared server DROP DATABASE can exceed the 30 s default (class cleanup failures).</summary>
    private string AdminConnectionString => $"{_serverConnection.TrimEnd(';')};Pooling=false;Default Command Timeout=300;";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = Provider.ToString(),
                ["Database:SqlitePath"] = null,
                ["ConnectionStrings:Default"] = ConnectionString,
                ["Database:InitializationMode"] = Environment.GetEnvironmentVariable("OPTIMIZEALL_TEST_DB_INIT") ?? "Migrate",
                ["Database:InitializeOnStartup"] = "true",
                ["Database:Seed:0"] = "Baseline",
                ["Jwt:SigningKey"] = "integration-test-signing-key-0123456789abcdef0123",
                ["Security:HashSalt"] = "integration-test-salt",
                ["Security:SecureCookies"] = "false",
                ["Email:Mode"] = "File",
                ["Email:PickupDirectory"] = MailDirectory,
                ["Email:AppBaseUrl"] = "http://app.test",
                ["Storage:RootPath"] = StorageDirectory,
                ["Jobs:Enabled"] = "false",
                ["RateLimiting:Enabled"] = "false",
                ["DevTools:MailboxEnabled"] = "true",
                ["Bootstrap:AdminEmail"] = null,
                ["Bootstrap:AdminPassword"] = null,
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public async Task InitializeAsync()
    {
        if (IsSqlite)
        {
            await this.StartAsync(); // boot the host (creates the file, runs migrations + baseline seed); see HostStartup
            return;
        }
        await using var conn = new MySqlConnection(AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE `{_databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci";
        await cmd.ExecuteNonQueryAsync();
        await this.StartAsync(); // boot the host (runs migrations + baseline seed); see HostStartup
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        TryDelete(MailDirectory);
        TryDelete(StorageDirectory);
        if (IsSqlite)
        {
            await using (var pooled = new SqliteConnection(ConnectionString))
                SqliteConnection.ClearPool(pooled);
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(_sqliteFile + suffix); } catch (IOException) { }
            }
            TryDelete(Path.ChangeExtension(_sqliteFile, null) + "-keys"); // Data Protection key ring
            return;
        }
        // Close this host's idle pooled connections now instead of after the pool's idle timeout, so the many
        // short-lived test hosts stay well under the server's max_connections.
        await using (var pooled = new MySqlConnection(ConnectionString))
            await MySqlConnection.ClearPoolAsync(pooled);
        await using var conn = new MySqlConnection(AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS `{_databaseName}`";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Runs an action with a scoped DbContext (for arranging data or asserting on the database).</summary>
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task WithDbAsync(Func<AppDbContext, Task> action) =>
        WithDbAsync(async db => { await action(db); return true; });

    /// <summary>Runs a background job once through the real JobRunner (lease + run log).</summary>
    public Task<OptimizeAll.Domain.Jobs.JobRun?> RunJobAsync<TJob>() where TJob : IJob =>
        Services.GetRequiredService<JobRunner>().RunAsync<TJob>();

    /// <summary>Creates a user directly in the database. Defaults: verified participant in PK.</summary>
    public async Task<TestUser> CreateUserAsync(
        Role[]? roles = null, bool emailVerified = true, string? email = null, string countryCode = "PK",
        string languageCode = "en", ParticipantTier tier = ParticipantTier.Standard, IEnumerable<string>? interests = null)
    {
        const string password = "Integration-Test-Pass-1";
        email ??= $"user-{Guid.NewGuid():N}@example.test";
        var now = Clock.GetUtcNow().UtcDateTime;
        var user = new User
        {
            Email = email,
            NormalizedEmail = Normalization.Email(email),
            DisplayName = "Test " + email.Split('@')[0][..Math.Min(12, email.Split('@')[0].Length)],
            CountryCode = countryCode,
            LanguageCode = languageCode,
            Tier = tier,
            Interests = interests?.ToList() ?? new List<string>(),
            EmailVerifiedAt = emailVerified ? now : null,
            ReferralCode = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        foreach (var role in roles ?? new[] { Role.Participant })
            user.Roles.Add(new UserRole { UserId = user.Id, Role = role, GrantedAt = now });

        await WithDbAsync(async db =>
        {
            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
        });
        return new TestUser(user.Id, email, password);
    }

    /// <summary>Creates a user and returns an HttpClient authenticated as them.</summary>
    public async Task<(TestUser User, HttpClient Client)> CreateClientAsync(params Role[] roles)
    {
        var user = await CreateUserAsync(roles.Length == 0 ? null : roles);
        return (user, await LoginAsync(user));
    }

    public async Task<HttpClient> LoginAsync(TestUser user)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }
}

public sealed record TestUser(Guid Id, string Email, string Password);

/// <summary>Share one database/host across the tests of a class: <c>public class X(ApiFactory api) : IClassFixture&lt;ApiFactory&gt;</c>.</summary>
public static class HttpAssertions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"Expected success but got {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, ApiFactory.Json)!;
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.ReadAsync<JsonElement>();

    /// <summary>Asserts a problem response with the given status and (optionally) error code.</summary>
    public static async Task ShouldFailAsync(this HttpResponseMessage response, int status, string? code = null)
    {
        var text = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != status)
            throw new Xunit.Sdk.XunitException($"Expected {status} but got {(int)response.StatusCode}: {text}");
        if (code is not null)
        {
            var json = JsonSerializer.Deserialize<JsonElement>(text);
            var actual = json.TryGetProperty("code", out var c) ? c.GetString() : null;
            if (actual != code)
                throw new Xunit.Sdk.XunitException($"Expected error code '{code}' but got '{actual}': {text}");
        }
    }
}
