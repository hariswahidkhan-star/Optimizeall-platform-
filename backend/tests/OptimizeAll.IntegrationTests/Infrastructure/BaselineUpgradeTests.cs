using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Seed;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.IntegrationTests.Infrastructure;

/// <summary>
/// Databases created by earlier releases, whose <c>InitialCreate</c> baseline had a different id, are upgraded at startup
/// without losing data (SQLite; <see cref="BaselineUpgrade"/>, docs/DATABASE.md § Baseline upgrade). Runs on SQLite
/// whatever OPTIMIZEALL_TEST_PROVIDER says.
/// <para>Fixtures (Fixtures/Baselines/sqlite-&lt;migration id&gt;.db.br, Brotli): the SQLite file an earlier release creates
/// on its first Render start, i.e. that commit's API started once with Database:InitializationMode=Migrate and the
/// Baseline+Demo seed, then VACUUMed. 20260924192026 is commit b70926b, 20260924231550 is commit a730b95. To add one:
/// <c>git archive &lt;commit&gt; backend global.json | tar -x -C /tmp/old</c>, build it, start it once as in render.yaml
/// (Database__Provider=Sqlite, Database__SqlitePath=…, Seed Baseline+Demo), stop it, VACUUM and Brotli-compress the file.</para>
/// </summary>
public sealed class BaselineUpgradeTests : IDisposable
{
    private const string CurrentSqliteBaseline = "20260925041851_InitialCreate";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "oa-baseline-" + Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(_directory, "optimizeall.db");
    private string BackupDirectory => Path.Combine(_directory, "backups");

    public BaselineUpgradeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    /// <summary>A host on the test's SQLite file, configured like render.yaml's API (Migrate + seed profiles).</summary>
    private sealed class Host(string dbPath, string storage, string mode, params string[] seeds) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "Sqlite",
                    ["Database:SqlitePath"] = dbPath,
                    ["ConnectionStrings:Default"] = null,
                    ["Database:InitializationMode"] = "Migrate",
                    ["Database:InitializeOnStartup"] = "true",
                    ["Database:BaselineUpgrade"] = mode,
                    ["Jwt:SigningKey"] = "integration-test-signing-key-0123456789abcdef0123",
                    ["Security:HashSalt"] = "integration-test-salt",
                    ["Security:SecureCookies"] = "false",
                    ["Email:Mode"] = "File",
                    ["Email:PickupDirectory"] = Path.Combine(storage, "mail"),
                    ["Email:AppBaseUrl"] = "http://app.test",
                    ["Storage:RootPath"] = Path.Combine(storage, "files"),
                    ["Jobs:Enabled"] = "false",
                    ["RateLimiting:Enabled"] = "false",
                    ["Bootstrap:AdminEmail"] = null,
                    ["Bootstrap:AdminPassword"] = null,
                    ["Website:PartnerContent:Enabled"] = "false",
                };
                // Replace the appsettings seed list entirely (empty = no seeding).
                for (var i = 0; i < 4; i++) settings[$"Database:Seed:{i}"] = i < seeds.Length ? seeds[i] : null;
                config.AddInMemoryCollection(settings);
            });
        }
    }

    private Host Start(string mode, params string[] seeds) => new(DbPath, _directory, mode, seeds);

    private void ExtractFixture(string migrationId)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Baselines", $"sqlite-{migrationId}.db.br");
        using var input = File.OpenRead(fixture);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = File.Create(DbPath);
        brotli.CopyTo(output);
    }

    public static TheoryData<string> Fixtures => new() { "20260924192026_InitialCreate", "20260924231550_InitialCreate" };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task A_database_from_an_earlier_baseline_is_upgraded_in_place_keeping_all_rows(string oldBaseline)
    {
        ExtractFixture(oldBaseline);
        Assert.Equal(new[] { oldBaseline }, Query(DbPath, "SELECT MigrationId FROM __EFMigrationsHistory"));
        var before = RowCounts(DbPath);
        var usersBefore = Query(DbPath, "SELECT Id || ' ' || NormalizedEmail || ' ' || PasswordHash FROM users ORDER BY Id");
        var ledgerBefore = Query(DbPath, "SELECT Id || ' ' || Amount FROM earning_entries ORDER BY Id");
        Assert.True(before["users"] > 5 && before["campaigns"] > 0 && before["submissions"] > 0 && before["earning_entries"] > 0
                    && before["website_pages"] > 0 && before["website_blog_posts"] > 0, "fixture should hold demo data");

        // First start: no seeding, so every copied table must hold exactly the rows it had.
        await using (var host = Start("Auto"))
        {
            await host.StartAsync();
            using var client = host.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        }
        SqliteConnection.ClearAllPools();

        Assert.Equal(new[] { CurrentSqliteBaseline }, Query(DbPath, "SELECT MigrationId FROM __EFMigrationsHistory"));
        var after = RowCounts(DbPath);
        var common = before.Keys.Where(after.ContainsKey).ToList();
        Assert.Equal(before.Count, common.Count); // no table of the old schema was dropped
        foreach (var table in common)
            Assert.True(before[table] == after[table], $"{table}: {before[table]} rows before, {after[table]} after");
        Assert.Equal(usersBefore, Query(DbPath, "SELECT Id || ' ' || NormalizedEmail || ' ' || PasswordHash FROM users ORDER BY Id"));
        Assert.Equal(ledgerBefore, Query(DbPath, "SELECT Id || ' ' || Amount FROM earning_entries ORDER BY Id"));
        Assert.Equal(new[] { "ok" }, Query(DbPath, "PRAGMA integrity_check"));
        Assert.Empty(Query(DbPath, "SELECT \"table\" FROM pragma_foreign_key_check"));

        // The original file is kept, unchanged, as a backup.
        var backup = Assert.Single(Directory.GetFiles(BackupDirectory));
        Assert.StartsWith($"optimizeall-{oldBaseline}-", Path.GetFileName(backup));
        Assert.Equal(before, RowCounts(backup));
        Assert.Equal(new[] { oldBaseline }, Query(backup, "SELECT MigrationId FROM __EFMigrationsHistory"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp*"));

        // Second start, as on Render (Baseline + Demo seed): no second upgrade, seeders are idempotent on the upgraded data,
        // and the demo accounts still sign in.
        await using (var host = Start("Auto", "Baseline", "Demo"))
        {
            await host.StartAsync();
            using var client = host.CreateClient();
            client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/public/site")).StatusCode);
            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = DemoAccounts.Admin, password = DemoAccounts.Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }
        SqliteConnection.ClearAllPools();
        Assert.Single(Directory.GetFiles(BackupDirectory));
        Assert.Equal(new[] { CurrentSqliteBaseline }, Query(DbPath, "SELECT MigrationId FROM __EFMigrationsHistory"));
        var reseeded = RowCounts(DbPath);
        foreach (var table in common)
            Assert.True(reseeded[table] >= before[table], $"{table}: {before[table]} rows before, {reseeded[table]} after seeding");
        Assert.Equal(before["users"], reseeded["users"]); // the demo seed found its accounts instead of adding them again
        Assert.Equal(new[] { "ok" }, Query(DbPath, "PRAGMA integrity_check"));
    }

    [Fact]
    public async Task Refuse_mode_does_not_start_and_leaves_the_database_untouched()
    {
        ExtractFixture("20260924231550_InitialCreate");
        var hash = Hash(DbPath);

        await using var host = Start("Refuse");
        var error = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());
        var refusal = Find<BaselineUpgradeException>(error);
        Assert.Contains("20260924231550_InitialCreate", refusal.Message);
        Assert.Contains(CurrentSqliteBaseline, refusal.Message);
        Assert.Contains("Database:BaselineUpgrade", refusal.Message);

        SqliteConnection.ClearAllPools();
        Assert.Equal(hash, Hash(DbPath));
        Assert.False(Directory.Exists(BackupDirectory));
    }

    [Fact]
    public async Task New_not_null_columns_get_the_model_defaults()
    {
        var userId = await CreateCurrentDatabaseWithUserAsync();
        // Simulate an older baseline that did not have these NOT NULL columns yet.
        Exec(DbPath,
            "UPDATE __EFMigrationsHistory SET MigrationId = '20200101000000_InitialCreate';" +
            "ALTER TABLE users DROP COLUMN FailedLoginCount; ALTER TABLE users DROP COLUMN Interests; ALTER TABLE users DROP COLUMN Tier;");

        await using (var host = Start("Auto"))
            await host.StartAsync();
        SqliteConnection.ClearAllPools();

        Assert.Equal(new[] { $"0|[]|{nameof(ParticipantTier.Standard)}" },
            Query(DbPath, $"SELECT FailedLoginCount || '|' || Interests || '|' || Tier FROM users WHERE Id = '{userId.ToString().ToUpperInvariant()}'"));
        await using (var host = Start("Auto"))
        {
            await host.StartAsync();
            using var scope = host.Services.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<User>().SingleAsync(u => u.Id == userId);
            Assert.Empty(user.Interests);
            Assert.Equal(ParticipantTier.Standard, user.Tier);
        }
    }

    [Fact]
    public async Task Foreign_key_violations_fail_the_upgrade_and_keep_the_old_database()
    {
        await CreateCurrentDatabaseWithUserAsync();
        Exec(DbPath,
            "PRAGMA foreign_keys=OFF;" +
            "UPDATE __EFMigrationsHistory SET MigrationId = '20200101000000_InitialCreate';" +
            "INSERT INTO user_roles (UserId, Role, GrantedAt) VALUES ('00000000-0000-0000-0000-00000000DEAD', 'Admin', '2026-01-01 00:00:00');");
        var hash = Hash(DbPath);

        await using var host = Start("Auto");
        var error = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());
        var failure = Find<BaselineUpgradeException>(error);
        Assert.Contains("foreign_key_check", failure.Message);
        Assert.Contains("user_roles", failure.Message);

        SqliteConnection.ClearAllPools();
        Assert.Equal(hash, Hash(DbPath));
        Assert.Equal(new[] { "20200101000000_InitialCreate" }, Query(DbPath, "SELECT MigrationId FROM __EFMigrationsHistory"));
        Assert.Single(Directory.GetFiles(BackupDirectory));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp*"));
    }

    private async Task<Guid> CreateCurrentDatabaseWithUserAsync()
    {
        var user = new User
        {
            Email = "upgrade@example.test",
            NormalizedEmail = Normalization.Email("upgrade@example.test"),
            DisplayName = "Upgrade Test",
            CountryCode = "PK",
            Tier = ParticipantTier.Gold,
            Interests = ["fashion"],
            FailedLoginCount = 2,
            ReferralCode = "UPGRADE01",
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Upgrade-Test-Pass-1");
        await using (var host = Start("Auto", "Baseline"))
        {
            await host.StartAsync();
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
        }
        SqliteConnection.ClearAllPools();
        Exec(DbPath, "PRAGMA wal_checkpoint(TRUNCATE);");
        return user.Id;
    }

    private static T Find<T>(Exception error) where T : Exception
    {
        for (Exception? e = error; e is not null; e = e.InnerException)
        {
            if (e is T match) return match;
            if (e is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions)
                    if (inner is T found) return found;
        }
        throw new Xunit.Sdk.XunitException($"Expected a {typeof(T).Name} but got: {error}");
    }

    private static Dictionary<string, long> RowCounts(string path)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        using var connection = Open(path);
        foreach (var table in Query(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory'"))
            counts[table] = Convert.ToInt64(Query(connection, $"SELECT COUNT(*) FROM \"{table}\"")[0]);
        return counts;
    }

    private static List<string> Query(string path, string sql)
    {
        using var connection = Open(path);
        return Query(connection, sql);
    }

    private static List<string> Query(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read()) rows.Add(Convert.ToString(reader.GetValue(0))!);
        return rows;
    }

    private static void Exec(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
