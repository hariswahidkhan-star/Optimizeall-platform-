using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Persistence;

public enum DatabaseProvider
{
    MySql,
    Sqlite,
}

/// <summary>
/// Provider-specific concurrency primitives. Application code must use this instead of raw SQL, so every module
/// works on both MySQL and SQLite. All methods operate on the given context's current connection/transaction.
/// </summary>
public interface IDatabaseDialect
{
    DatabaseProvider Provider { get; }

    /// <summary>
    /// Starts a transaction that will write. On MySQL a normal transaction; on SQLite <c>BEGIN IMMEDIATE</c>, which takes
    /// the database write lock up front so later locking reads cannot deadlock or be overtaken.
    /// </summary>
    Task<IDbContextTransaction> BeginWriteTransactionAsync(AppDbContext db, CancellationToken ct);

    /// <summary>
    /// Locks one row for the rest of the current transaction (MySQL <c>SELECT … FOR UPDATE</c>). On SQLite the write
    /// transaction already serializes writers, so this only verifies the row exists. Returns false if the row does not exist.
    /// <paramref name="table"/> must be a trusted table-name constant, never user input.
    /// </summary>
    Task<bool> LockRowAsync(AppDbContext db, string table, Guid id, CancellationToken ct);

    /// <summary>
    /// Acquires an application-wide named lock (MySQL <c>GET_LOCK</c> on the context's connection, scoped to the current
    /// database; SQLite: an in-process lock plus the database write lock). Dispose to release. Throws on timeout.
    /// </summary>
    Task<IAsyncDisposable> AcquireNamedLockAsync(AppDbContext db, string name, TimeSpan timeout, CancellationToken ct);

    /// <summary>True when the exception is a unique-constraint violation.</summary>
    bool IsUniqueViolation(DbUpdateException ex);
}

public sealed class MySqlDialect : IDatabaseDialect
{
    public DatabaseProvider Provider => DatabaseProvider.MySql;

    public Task<IDbContextTransaction> BeginWriteTransactionAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.BeginTransactionAsync(ct);

    public async Task<bool> LockRowAsync(AppDbContext db, string table, Guid id, CancellationToken ct)
    {
        EnsureIdentifier(table);
#pragma warning disable EF1002 // table is a validated identifier constant; the id is a parameter.
        var rows = await db.Database
            .SqlQueryRaw<Guid>($"SELECT Id AS Value FROM `{table}` WHERE Id = {{0}} FOR UPDATE", id)
            .ToListAsync(ct);
#pragma warning restore EF1002
        return rows.Count > 0;
    }

    public async Task<IAsyncDisposable> AcquireNamedLockAsync(AppDbContext db, string name, TimeSpan timeout, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        var scoped = $"oa:{name}:{db.Database.GetDbConnection().Database}";
        if (scoped.Length > 64) scoped = scoped[..64];
        var acquired = await db.Database
            .SqlQuery<long?>($"SELECT GET_LOCK({scoped}, {(int)Math.Ceiling(timeout.TotalSeconds)}) AS Value")
            .SingleAsync(ct);
        if (acquired != 1)
        {
            await db.Database.CloseConnectionAsync();
            throw new TimeoutException($"Could not acquire lock '{name}' within {timeout.TotalSeconds:0}s.");
        }
        return new Release(async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT RELEASE_LOCK({scoped})");
            await db.Database.CloseConnectionAsync();
        });
    }

    public bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is MySqlConnector.MySqlException { ErrorCode: MySqlConnector.MySqlErrorCode.DuplicateKeyEntry };

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
