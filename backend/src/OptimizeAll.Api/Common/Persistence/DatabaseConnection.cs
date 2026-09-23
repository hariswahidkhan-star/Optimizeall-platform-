using MySqlConnector;

namespace OptimizeAll.Api.Common.Persistence;

/// <summary>
/// Resolves the MySQL connection string. <c>ConnectionStrings:Default</c> wins when set; otherwise it is built from
/// discrete settings (<c>Database:Host</c>, <c>Database:Port</c>, <c>Database:Name</c>, <c>Database:User</c>,
/// <c>Database:Password</c>). The discrete form suits platforms that inject a host and a generated password as
/// separate variables (e.g. Render Blueprints) and avoids hand-escaping passwords into a connection string.
/// </summary>
public static class DatabaseConnection
{
    public static string Resolve(IConfiguration config)
    {
        var explicitConnection = config.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(explicitConnection)) return explicitConnection;

        var host = config["Database:Host"];
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("Set ConnectionStrings:Default or Database:Host (with Database:Name/User/Password).");

        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = uint.TryParse(config["Database:Port"], out var port) ? port : 3306,
            Database = Required(config, "Database:Name"),
            UserID = Required(config, "Database:User"),
            Password = Required(config, "Database:Password"),
            // Private-network MySQL typically uses the server's self-signed certificate.
            SslMode = MySqlSslMode.Preferred,
            AllowPublicKeyRetrieval = true,
        };
        return builder.ConnectionString;
    }

    private static string Required(IConfiguration config, string key) =>
        config[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"{key} is required when Database:Host is used.");
}
