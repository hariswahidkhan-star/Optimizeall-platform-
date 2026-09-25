using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.UnitTests.Foundation;

/// <summary>
/// The <c>InitialCreate</c> migrations are the permanent baselines of every deployed database (docs/DATABASE.md
/// § Migrations). Regenerating one gives it a new id, and every existing database would then see it as pending and fail
/// on its first CREATE TABLE. Schema changes are incremental migrations (scripts/regenerate-migrations.sh --add Name).
/// If this test fails, restore the baseline files from git instead of updating the ids.
/// </summary>
public class MigrationBaselineTests
{
    private const string MySqlBaseline = "20260925041843_InitialCreate";
    private const string SqliteBaseline = "20260925041851_InitialCreate";

    private static List<string> MigrationIds(Assembly assembly) => assembly.GetTypes()
        .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract)
        .Where(t => t.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(AppDbContext))
        .Select(t => t.GetCustomAttribute<MigrationAttribute>()?.Id ?? throw new InvalidOperationException($"{t} has no [Migration]"))
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToList();

    public static TheoryData<string, string> Providers => new()
    {
        { typeof(AppDbContext).Assembly.GetName().Name!, MySqlBaseline },
        { DatabaseProviders.SqliteMigrationsAssembly, SqliteBaseline },
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public void The_baseline_migration_is_frozen(string assemblyName, string baseline)
    {
        var ids = MigrationIds(Assembly.Load(assemblyName));
        Assert.NotEmpty(ids);
        Assert.Equal(baseline, ids[0]);
        // Exactly one baseline; everything after it is an incremental migration.
        Assert.Single(ids, id => id.EndsWith(BaselineUpgrade.InitialCreateSuffix, StringComparison.Ordinal));
    }

    [Fact]
    public void Both_providers_have_the_same_incremental_migrations()
    {
        static IEnumerable<string> Names(IEnumerable<string> ids) => ids.Skip(1).Select(id => id[(id.IndexOf('_') + 1)..]);
        Assert.Equal(
            Names(MigrationIds(typeof(AppDbContext).Assembly)),
            Names(MigrationIds(Assembly.Load(DatabaseProviders.SqliteMigrationsAssembly))));
    }

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { SqliteBaseline }, null)]
    [InlineData(new[] { SqliteBaseline, "20261001000000_AddThing" }, null)] // a newer release's migration: not a baseline
    [InlineData(new[] { "20260924231550_InitialCreate" }, "20260924231550_InitialCreate")]
    [InlineData(new[] { "20260924192026_InitialCreate", "20260924200000_Extra" }, "20260924192026_InitialCreate")]
    public void A_foreign_baseline_is_an_InitialCreate_this_build_does_not_have(string[] applied, string? expected)
    {
        var known = new[] { SqliteBaseline, "20260926000000_Next" };
        Assert.Equal(expected, BaselineUpgrade.FindForeignBaseline(applied, known));
    }

    [Theory]
    [InlineData(null, BaselineUpgradeMode.Auto)]
    [InlineData("", BaselineUpgradeMode.Auto)]
    [InlineData("auto", BaselineUpgradeMode.Auto)]
    [InlineData("Refuse", BaselineUpgradeMode.Refuse)]
    public void The_baseline_upgrade_mode_is_parsed(string? value, BaselineUpgradeMode expected) =>
        Assert.Equal(expected, BaselineUpgrade.ParseMode(value));

    [Fact]
    public void An_unknown_baseline_upgrade_mode_is_rejected() =>
        Assert.Throws<InvalidOperationException>(() => BaselineUpgrade.ParseMode("Sometimes"));
}
