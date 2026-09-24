using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySqlConnector;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Persistence;

/// <summary>
/// Resolves the database provider and connection string.
/// <para><c>Database:Provider</c> = <c>MySql</c> (default) | <c>Sqlite</c>.</para>
/// <para>MySQL: <c>ConnectionStrings:Default</c> wins when set; otherwise it is built from discrete settings
/// (<c>Database:Host</c>, <c>Database:Port</c>, <c>Database:Name</c>, <c>Database:User</c>, <c>Database:Password</c>).
/// The discrete form suits platforms that inject a host and a generated password as separate variables (e.g. Render
/// Blueprints) and avoids hand-escaping passwords into a connection string.</para>
/// <para>SQLite: <c>Database:SqlitePath</c> (a file path) wins when set; otherwise <c>ConnectionStrings:Default</c> must be
/// a SQLite connection string (e.g. <c>Data Source=/app/storage/db/optimizeall.db</c>). The file's directory is created
/// if missing.</para>
/// </summary>
public static class DatabaseConnection
{
    public static DatabaseProvider Provider(IConfiguration config)
    {
        var value = config["Database:Provider"];
        if (string.IsNullOrWhiteSpace(value)) return DatabaseProvider.MySql;
        return Enum.TryParse<DatabaseProvider>(value.Trim(), ignoreCase: true, out var provider)
            ? provider
            : throw new InvalidOperationException($"Unknown Database:Provider '{value}'. Use MySql or Sqlite.");
    }

    /// <summary>The MySQL connection string.</summary>
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

    /// <summary>
    /// The SQLite connection string (private cache and pooling; shared-cache mode is deliberately not used because it
    /// replaces WAL's reader/writer concurrency with table locks whose SQLITE_LOCKED errors the busy timeout doesn't cover).
    /// </summary>
    public static string ResolveSqlite(IConfiguration config)
    {
        var path = config["Database:SqlitePath"];
        var connection = !string.IsNullOrWhiteSpace(path)
            ? new SqliteConnectionStringBuilder { DataSource = path.Trim() }.ConnectionString
            : config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Database:Provider=Sqlite needs Database:SqlitePath or a SQLite ConnectionStrings:Default.");

        SqliteConnectionStringBuilder builder;
        try
        {
            builder = new SqliteConnectionStringBuilder(connection);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default is not a SQLite connection string (expected e.g. \"Data Source=/app/storage/db/optimizeall.db\"). " +
                "Set Database:SqlitePath or fix the connection string.", ex);
        }
        EnsureDirectory(builder.DataSource);
        return builder.ConnectionString;
    }

    /// <summary>
    /// SQLite only: the Data Protection key ring directory, next to the database file
    /// (<c>/app/storage/db/optimizeall.db</c> → <c>/app/storage/db/optimizeall-keys</c>). Back it up with the database.
    /// </summary>
    public static string SqliteKeyDirectory(IConfiguration config)
    {
        var file = Path.GetFullPath(new SqliteConnectionStringBuilder(ResolveSqlite(config)).DataSource);
        return Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + "-keys");
    }

    private static void EnsureDirectory(string dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:" || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return;
        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    }

    /// <summary>Configures the AppDbContext for the configured provider (MySQL or SQLite).</summary>
    public static void Configure(DbContextOptionsBuilder options, IConfiguration config)
    {
        switch (Provider(config))
        {
            case DatabaseProvider.Sqlite:
                options.UseSqlite(ResolveSqlite(config), sqlite =>
                {
                    sqlite.MigrationsAssembly(DatabaseProviders.SqliteMigrationsAssembly);
                    sqlite.CommandTimeout(60);
                });
                options.AddInterceptors(SqlitePragmaInterceptor.Instance);
                options.UseSqliteQuerySupport();
                break;
            default:
                options.UseMySql(
                    Resolve(config),
                    new MySqlServerVersion(new Version(8, 0, 36)),
                    mysql =>
                    {
                        mysql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                        mysql.CommandTimeout(60);
                    });
                break;
        }
    }

    /// <summary>
    /// Development and Testing only: turns query warnings that mean nondeterministic results into exceptions, so an
    /// unordered <c>Skip</c>/<c>Take</c> (EF warning 10102) fails in tests instead of silently returning arbitrary rows.
    /// Production and Staging keep EF's default (a logged warning).
    /// </summary>
    public static void ConfigureStrictQueryWarnings(DbContextOptionsBuilder options, IHostEnvironment environment)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            options.ConfigureWarnings(w => w.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning));
    }

    private static string Required(IConfiguration config, string key) =>
        config[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"{key} is required when Database:Host is used.");
}

/// <summary>
/// Applies the SQLite pragmas every connection needs: WAL (readers don't block the writer and vice versa), a busy
/// timeout so a writer waits for the write lock instead of failing with "database is locked", and enforced foreign
/// keys. <c>synchronous=NORMAL</c> is the recommended durability level for WAL.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public static readonly SqlitePragmaInterceptor Instance = new();

    public const int BusyTimeoutMilliseconds = 10_000;

    private const string Pragmas =
        "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=10000; PRAGMA foreign_keys=ON; PRAGMA synchronous=NORMAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
