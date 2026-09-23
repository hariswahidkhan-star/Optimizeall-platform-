using Microsoft.Extensions.Configuration;
using MySqlConnector;
using OptimizeAll.Api.Common.Persistence;
using Xunit;

namespace OptimizeAll.UnitTests.Foundation;

public sealed class DatabaseConnectionTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void Explicit_connection_string_wins()
    {
        var cs = DatabaseConnection.Resolve(Config(("ConnectionStrings:Default", "Server=a;Database=b;"), ("Database:Host", "ignored")));
        Assert.Equal("Server=a;Database=b;", cs);
    }

    [Fact]
    public void Builds_from_discrete_settings_and_escapes_the_password()
    {
        var cs = DatabaseConnection.Resolve(Config(
            ("Database:Host", "optimizeall-mysql"), ("Database:Port", "3307"), ("Database:Name", "optimizeall"),
            ("Database:User", "app"), ("Database:Password", "p;w=\"d'")));
        var parsed = new MySqlConnectionStringBuilder(cs);
        Assert.Equal("optimizeall-mysql", parsed.Server);
        Assert.Equal(3307u, parsed.Port);
        Assert.Equal("optimizeall", parsed.Database);
        Assert.Equal("app", parsed.UserID);
        Assert.Equal("p;w=\"d'", parsed.Password);
    }

    [Fact]
    public void Missing_settings_fail_fast()
    {
        Assert.Throws<InvalidOperationException>(() => DatabaseConnection.Resolve(Config()));
        Assert.Throws<InvalidOperationException>(() => DatabaseConnection.Resolve(Config(("Database:Host", "h"), ("Database:Name", "n"))));
    }

    [Fact]
    public void Provider_defaults_to_mysql_and_parses_case_insensitively()
    {
        Assert.Equal(DatabaseProvider.MySql, DatabaseConnection.Provider(Config()));
        Assert.Equal(DatabaseProvider.Sqlite, DatabaseConnection.Provider(Config(("Database:Provider", "sqlite"))));
        Assert.Equal(DatabaseProvider.MySql, DatabaseConnection.Provider(Config(("Database:Provider", "MySql"))));
        Assert.Throws<InvalidOperationException>(() => DatabaseConnection.Provider(Config(("Database:Provider", "Postgres"))));
    }

    [Fact]
    public void Sqlite_path_wins_and_its_directory_is_created()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oa-unit-" + Guid.NewGuid().ToString("N"), "db");
        try
        {
            var file = Path.Combine(dir, "optimizeall.db");
            var cs = DatabaseConnection.ResolveSqlite(Config(
                ("Database:SqlitePath", file), ("ConnectionStrings:Default", "Server=127.0.0.1;Database=ignored;")));
            Assert.Equal(file, new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(cs).DataSource);
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public void Sqlite_uses_the_connection_string_otherwise_and_rejects_mysql_strings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oa-unit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cs = DatabaseConnection.ResolveSqlite(Config(("ConnectionStrings:Default", $"Data Source={dir}/x.db")));
            Assert.Equal($"{dir}/x.db", new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(cs).DataSource);
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        Assert.Throws<InvalidOperationException>(() => DatabaseConnection.ResolveSqlite(Config(("ConnectionStrings:Default", "Server=a;Database=b;"))));
        Assert.Throws<InvalidOperationException>(() => DatabaseConnection.ResolveSqlite(Config()));
    }

    [Fact]
    public void Check_constraint_sql_is_rewritten_for_sqlite()
    {
        var decimals = new HashSet<string> { "Amount" };
        Assert.Equal("CAST(\"Amount\" AS REAL) > 0 AND \"Type\" <> 'X' OR LENGTH(\"Reason\") > 0",
            OptimizeAll.Infrastructure.Persistence.PortableModel.RewriteCheckSql("`Amount` > 0 AND `Type` <> 'X' OR CHAR_LENGTH(`Reason`) > 0", decimals));
    }
}
