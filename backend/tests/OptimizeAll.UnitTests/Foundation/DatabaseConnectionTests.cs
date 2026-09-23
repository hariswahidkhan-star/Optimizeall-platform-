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
}
