using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Persistence;

public enum DatabaseProvider
{
    MySql,
    Sqlite,
}

/// <summary>How <see cref="IDatabaseDialect.LockRowAsync"/> locks the row.</summary>
public enum RowLockMode
{
    /// <summary>Exclusive lock (MySQL <c>FOR UPDATE</c>).</summary>
    Update,

    /// <summary>Shared lock (MySQL <c>FOR SHARE</c>): blocks concurrent writers of the row, not other readers.</summary>
    Share,
}

/// <summary>
/// Provider-specific concurrency primitives. Application code must use this instead of raw SQL, so every module
/// works on both MySQL and SQLite. All methods operate on the given context's current connection/transaction.
/// Resolve it from DI or, in static helpers, with <c>db.Dialect()</c>.
/// </summary>
public interface IDatabaseDialect
{
    DatabaseProvider Provider { get; }

    /// <summary>
    /// Starts a transaction that will write. On MySQL a normal transaction (with <paramref name="isolationLevel"/> when
    /// given); on SQLite <c>BEGIN IMMEDIATE</c>, which takes the database write lock up front (waiting up to the busy
    /// timeout) so later reads and writes of the transaction cannot deadlock or be overtaken. SQLite ignores the
    /// isolation level: its transactions are always serializable.
    /// </summary>
    Task<IDbContextTransaction> BeginWriteTransactionAsync(AppDbContext db, CancellationToken ct,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified);

    /// <summary>
    /// Locks one row for the rest of the current transaction (MySQL <c>SELECT … FOR UPDATE</c> / <c>FOR SHARE</c>). On
    /// SQLite the write transaction already serializes writers, so this only verifies the row exists (and throws
    /// <see cref="InvalidOperationException"/> outside a transaction). Returns false if the row does not exist.
    /// <paramref name="table"/> must be a trusted table-name constant, never user input.
    /// </summary>
    Task<bool> LockRowAsync(AppDbContext db, string table, Guid id, CancellationToken ct, RowLockMode mode = RowLockMode.Update);

    /// <summary>
    /// Acquires an application-wide named lock (MySQL <c>GET_LOCK</c> on the context's connection, scoped to the current
    /// database; SQLite: an in-process lock per database file, which is sufficient because SQLite deployments run a
    /// single API instance). Acquire it <b>before</b> <see cref="BeginWriteTransactionAsync"/> and dispose it after
    /// the transaction. Dispose to release. Throws <see cref="TimeoutException"/> on timeout.
    /// </summary>
    Task<IAsyncDisposable> AcquireNamedLockAsync(AppDbContext db, string name, TimeSpan timeout, CancellationToken ct);

    /// <summary>True when the exception is a unique-constraint violation.</summary>
    bool IsUniqueViolation(DbUpdateException ex);
}

/// <summary>Provider-agnostic database error classification.</summary>
public static class DatabaseErrors
{
    /// <summary>SQLite primary result code SQLITE_CONSTRAINT.</summary>
    private const int SqliteConstraint = 19;
    /// <summary>SQLITE_CONSTRAINT_PRIMARYKEY (1555) and SQLITE_CONSTRAINT_UNIQUE (2067).</summary>
    private const int SqliteConstraintPrimaryKey = 1555, SqliteConstraintUnique = 2067;

    /// <summary>True when the exception is a unique/primary-key violation on MySQL (ER_DUP_ENTRY) or SQLite.</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) => IsUniqueViolation(ex.InnerException);

    public static bool IsUniqueViolation(Exception? ex) => ex switch
    {
        MySqlConnector.MySqlException mysql => mysql.ErrorCode == MySqlConnector.MySqlErrorCode.DuplicateKeyEntry,
        SqliteException sqlite => sqlite.SqliteErrorCode == SqliteConstraint &&
                                  sqlite.SqliteExtendedErrorCode is SqliteConstraintUnique or SqliteConstraintPrimaryKey,
        _ => false,
    };
}

public static class DatabaseDialects
{
    public static readonly IDatabaseDialect MySql = new MySqlDialect();
    public static readonly IDatabaseDialect Sqlite = new SqliteDialect();

    public static IDatabaseDialect For(DatabaseProvider provider) => provider == DatabaseProvider.Sqlite ? Sqlite : MySql;

    /// <summary>The dialect of the provider this context runs on (for static helpers without DI).</summary>
    public static IDatabaseDialect Dialect(this AppDbContext db) =>
        DatabaseProviders.IsSqlite(db.Database.ProviderName) ? Sqlite : MySql;
}

public sealed class MySqlDialect : IDatabaseDialect
{
    public DatabaseProvider Provider => DatabaseProvider.MySql;

    public Task<IDbContextTransaction> BeginWriteTransactionAsync(AppDbContext db, CancellationToken ct,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified) =>
        isolationLevel == IsolationLevel.Unspecified
            ? db.Database.BeginTransactionAsync(ct)
            : db.Database.BeginTransactionAsync(isolationLevel, ct);

    public async Task<bool> LockRowAsync(AppDbContext db, string table, Guid id, CancellationToken ct, RowLockMode mode = RowLockMode.Update)
    {
        EnsureIdentifier(table);
        var clause = mode == RowLockMode.Share ? "FOR SHARE" : "FOR UPDATE";
#pragma warning disable EF1002 // table is a validated identifier constant; the id is a parameter.
        var rows = await db.Database
            .SqlQueryRaw<Guid>($"SELECT `Id` AS `Value` FROM `{table}` WHERE `Id` = {{0}} {clause}", id)
            .ToListAsync(ct);
#pragma warning restore EF1002
        return rows.Count > 0;
    }

    public async Task<IAsyncDisposable> AcquireNamedLockAsync(AppDbContext db, string name, TimeSpan timeout, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var scoped = $"oa:{name}:{db.Database.GetDbConnection().Database}";
            if (scoped.Length > 64) scoped = scoped[..64];
            var acquired = await db.Database
                .SqlQuery<long?>($"SELECT GET_LOCK({scoped}, {(int)Math.Ceiling(timeout.TotalSeconds)}) AS Value")
                .SingleAsync(ct);
            if (acquired != 1)
                throw new TimeoutException($"Could not acquire lock '{name}' within {timeout.TotalSeconds:0}s.");
            return new Release(async () =>
            {
                try
                {
                    await db.Database.SqlQuery<long?>($"SELECT RELEASE_LOCK({scoped}) AS Value").SingleAsync(CancellationToken.None);
                }
                finally
                {
                    await db.Database.CloseConnectionAsync();
                }
            });
        }
        catch
        {
            await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    public bool IsUniqueViolation(DbUpdateException ex) => DatabaseErrors.IsUniqueViolation(ex);

    internal static void EnsureIdentifier(string table)
    {
        if (string.IsNullOrEmpty(table) || !table.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException("Invalid table name.", nameof(table));
    }

    internal sealed class Release(Func<Task> release) : IAsyncDisposable
    {
        private int _released;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) await release();
        }
    }
}

/// <summary>
/// SQLite dialect. SQLite has a single database-wide write lock, so every write transaction is <c>BEGIN IMMEDIATE</c>
/// and row locks reduce to existence checks. Named locks are in-process: <b>SQLite deployments must run exactly one
/// API instance</b> (which a single SQLite file on one disk implies anyway).
/// </summary>
public sealed class SqliteDialect : IDatabaseDialect
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> NamedLocks = new(StringComparer.Ordinal);

    public DatabaseProvider Provider => DatabaseProvider.Sqlite;

    public async Task<IDbContextTransaction> BeginWriteTransactionAsync(AppDbContext db, CancellationToken ct,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified)
    {
        // EF opens the connection and calls SqliteConnection.BeginTransaction(Serializable), which Microsoft.Data.Sqlite
        // runs as "BEGIN IMMEDIATE" (only ReadUncommitted is deferred): the write lock is taken here, waiting for other
        // writers up to the busy timeout. The transaction is owned by EF, so disposing it rolls back and closes the
        // connection as usual. (Verified by SqliteDialectTests.)
        return await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    }

    public async Task<bool> LockRowAsync(AppDbContext db, string table, Guid id, CancellationToken ct, RowLockMode mode = RowLockMode.Update)
    {
        MySqlDialect.EnsureIdentifier(table);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException($"LockRowAsync({table}) must run inside a write transaction (BeginWriteTransactionAsync).");
#pragma warning disable EF1002 // table is a validated identifier constant; the id is a parameter.
        var rows = await db.Database
            // EF stores Guids on SQLite as upper-case TEXT.
            .SqlQueryRaw<int>($"SELECT COUNT(*) AS \"Value\" FROM \"{table}\" WHERE \"Id\" = {{0}}", id.ToString().ToUpperInvariant())
            .ToListAsync(ct);
#pragma warning restore EF1002
        return rows.Count > 0 && rows[0] > 0;
    }

    public async Task<IAsyncDisposable> AcquireNamedLockAsync(AppDbContext db, string name, TimeSpan timeout, CancellationToken ct)
    {
        // Waiting for the in-process lock while holding the database write lock would block the lock holder's own
        // write transaction until the busy timeout; enforce the acquire-before-transaction order.
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException($"Acquire the named lock '{name}' before beginning the transaction.");
        var key = $"{db.Database.GetConnectionString()}|{name}";
        var semaphore = NamedLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (!await semaphore.WaitAsync(timeout, ct))
            throw new TimeoutException($"Could not acquire lock '{name}' within {timeout.TotalSeconds:0}s.");
        return new MySqlDialect.Release(() =>
        {
            semaphore.Release();
            return Task.CompletedTask;
        });
    }

    public bool IsUniqueViolation(DbUpdateException ex) => DatabaseErrors.IsUniqueViolation(ex);
}
