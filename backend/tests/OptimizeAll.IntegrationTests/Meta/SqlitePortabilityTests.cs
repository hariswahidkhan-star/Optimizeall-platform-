using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Jobs;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.IntegrationTests.Meta;

/// <summary>
/// The SQLite-specific persistence pieces (dialect, pragmas, decimal support, portable model), exercised on a
/// temporary SQLite file migrated with the SQLite migrations. Runs whatever OPTIMIZEALL_TEST_PROVIDER is.
/// </summary>
public sealed class SqlitePortabilityTests : IAsyncLifetime
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"oa-portability-{Guid.NewGuid():N}.db");
    private readonly IDatabaseDialect _dialect = DatabaseDialects.Sqlite;
    private DbContextOptions<AppDbContext> _options = null!;

    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = _file }.ConnectionString;

    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:SqlitePath"] = _file,
        }).Build();
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseConnection.Configure(builder, config);
        _options = builder.Options;
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        using (var pooled = new SqliteConnection(ConnectionString)) SqliteConnection.ClearPool(pooled);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_file + suffix); } catch (IOException) { }
        }
        return Task.CompletedTask;
    }

    private AppDbContext Db() => new(_options, TimeProvider.System);

    [Fact]
    public async Task Every_connection_uses_wal_a_busy_timeout_and_foreign_keys()
    {
        await using var db = Db();
        Assert.False(db.IsMySql);
        Assert.Equal("wal", await ScalarAsync<string>(db, "PRAGMA journal_mode"));
        Assert.Equal(SqlitePragmaInterceptor.BusyTimeoutMilliseconds, await ScalarAsync<long>(db, "PRAGMA busy_timeout"));
        Assert.Equal(1L, await ScalarAsync<long>(db, "PRAGMA foreign_keys"));
    }

    [Fact]
    public async Task Write_transaction_takes_the_database_write_lock_up_front()
    {
        await using var holder = Db();
        await using var tx = await _dialect.BeginWriteTransactionAsync(holder, CancellationToken.None);

        // Before the holder has written anything, another writer cannot even start an immediate transaction.
        await using (var other = new SqliteConnection(ConnectionString))
        {
            await other.OpenAsync();
            await using var noWait = other.CreateCommand();
            noWait.CommandText = "PRAGMA busy_timeout=0";
            await noWait.ExecuteNonQueryAsync();
            var ex = await Assert.ThrowsAsync<SqliteException>(() => Task.Run(() => other.BeginTransaction(deferred: false)));
            Assert.Equal(5, ex.SqliteErrorCode); // SQLITE_BUSY
        }

        await tx.CommitAsync();
        await using var after = new SqliteConnection(ConnectionString);
        await after.OpenAsync();
        using var ok = after.BeginTransaction(deferred: false);
        ok.Rollback();
    }

    [Fact]
    public async Task Concurrent_writers_wait_for_each_other_instead_of_failing()
    {
        var name = "portability-" + Guid.NewGuid().ToString("N")[..8];
        await using (var seed = Db())
        {
            seed.Set<JobLease>().Add(new JobLease { Name = name, Holder = "0", LeasedUntil = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }

        // 20 read-modify-write increments in parallel write transactions: none is lost, none fails with "locked".
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            await using var db = Db();
            await using var tx = await _dialect.BeginWriteTransactionAsync(db, CancellationToken.None);
            var lease = await db.Set<JobLease>().SingleAsync(l => l.Name == name);
            await Task.Delay(5);
            lease.Holder = (int.Parse(lease.Holder) + 1).ToString();
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        })));

        await using var check = Db();
        Assert.Equal("20", (await check.Set<JobLease>().SingleAsync(l => l.Name == name)).Holder);
    }

    [Fact]
    public async Task Row_locks_need_a_write_transaction_and_report_missing_rows()
    {
        var rate = await AddRateAsync(1.5m);
        await using var db = Db();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _dialect.LockRowAsync(db, "exchange_rates", rate.Id, CancellationToken.None));

        await using var tx = await _dialect.BeginWriteTransactionAsync(db, CancellationToken.None);
        Assert.True(await _dialect.LockRowAsync(db, "exchange_rates", rate.Id, CancellationToken.None));
        Assert.False(await _dialect.LockRowAsync(db, "exchange_rates", Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => _dialect.LockRowAsync(db, "exchange_rates; DROP", rate.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Named_locks_are_exclusive_and_must_be_taken_before_the_transaction()
    {
        await using var a = Db();
        await using var b = Db();
        await using (await _dialect.AcquireNamedLockAsync(a, "portability", TimeSpan.FromSeconds(5), CancellationToken.None))
        {
            await Assert.ThrowsAsync<TimeoutException>(() =>
                _dialect.AcquireNamedLockAsync(b, "portability", TimeSpan.FromMilliseconds(100), CancellationToken.None));
        }
        await using (await _dialect.AcquireNamedLockAsync(b, "portability", TimeSpan.FromSeconds(1), CancellationToken.None))
        {
            Assert.True(SqliteDialect.NamedLocks.Contains(SqliteDialect.NamedLockKey(b, "portability")));
        }
        // Released locks leave no entry behind (per-entity lock names must not accumulate).
        Assert.False(SqliteDialect.NamedLocks.Contains(SqliteDialect.NamedLockKey(b, "portability")));

        await using var tx = await _dialect.BeginWriteTransactionAsync(a, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _dialect.AcquireNamedLockAsync(a, "portability", TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public async Task Unique_violations_are_recognized()
    {
        var name = "dup-" + Guid.NewGuid().ToString("N")[..8];
        await using (var db = Db())
        {
            db.Set<JobLease>().Add(new JobLease { Name = name, Holder = "", LeasedUntil = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await using var again = Db();
        again.Set<JobLease>().Add(new JobLease { Name = name, Holder = "", LeasedUntil = DateTime.UtcNow });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => again.SaveChangesAsync());
        Assert.True(_dialect.IsUniqueViolation(ex));
        Assert.True(DatabaseErrors.IsUniqueViolation(ex));
        Assert.True(OptimizeAll.Api.Common.Errors.ProblemExceptionHandler.IsUniqueViolation(ex));
    }

    [Fact]
    public async Task Decimal_aggregates_ordering_and_scale_are_exact()
    {
        // Values whose text order differs from numeric order, and sums that floating point gets wrong.
        var source = "P" + Guid.NewGuid().ToString("N")[..8];
        foreach (var value in new[] { 0.1m, 0.2m, 10.5m, 9.25m, 1234567890.12345678m, 0.00000001m })
            await AddRateAsync(value, source);

        await using var db = Db();
        var rates = db.Set<ExchangeRate>().Where(r => r.Source == source);
        Assert.Equal(1234567910.17345679m, await rates.SumAsync(r => r.Rate));
        Assert.Equal(0.00000001m, await rates.MinAsync(r => r.Rate));
        Assert.Equal(1234567890.12345678m, await rates.MaxAsync(r => r.Rate));
        Assert.Equal(1234567910.17345679m / 6, await rates.AverageAsync(r => r.Rate));
        Assert.Equal(0.3m, await rates.Where(r => r.Rate < 1m && r.Rate > 0.01m).SumAsync(r => r.Rate));
        Assert.Equal(0m, await rates.Where(r => r.Rate > 1e12m).SumAsync(r => r.Rate));

        var grouped = await rates.GroupBy(r => r.Source).Select(g => g.Sum(r => r.Rate)).SingleAsync();
        Assert.Equal(1234567910.17345679m, grouped);

        var ascending = await rates.OrderBy(r => r.Rate).Select(r => r.Rate).ToListAsync();
        Assert.Equal(new[] { 0.00000001m, 0.1m, 0.2m, 9.25m, 10.5m, 1234567890.12345678m }, ascending);
        var descending = await rates.OrderByDescending(r => r.Rate).ThenBy(r => r.Id).Select(r => r.Rate).FirstAsync();
        Assert.Equal(1234567890.12345678m, descending);

        // Read back with the column's scale (8), like MySQL DECIMAL(18,8).
        var tenAndAHalf = await rates.Where(r => r.Rate == 10.5m).Select(r => r.Rate).SingleAsync();
        Assert.Equal("10.50000000", tenAndAHalf.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Check_constraints_compare_decimals_numerically()
    {
        await using var db = Db();
        db.Set<ExchangeRate>().Add(NewRate(0m, "zero"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task NewGuid_in_bulk_updates_creates_a_distinct_guid_per_row()
    {
        var source = "G" + Guid.NewGuid().ToString("N")[..8];
        var first = await AddRateAsync(2m, source);
        var second = await AddRateAsync(3m, source);

        await using var db = Db();
        var updated = await db.Set<ExchangeRate>().Where(r => r.Source == source)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedByUserId, r => Guid.NewGuid()));
        Assert.Equal(2, updated);

        var stamps = await db.Set<ExchangeRate>().AsNoTracking().Where(r => r.Source == source)
            .ToDictionaryAsync(r => r.Id, r => r.CreatedByUserId!.Value);
        Assert.NotEqual(stamps[first.Id], stamps[second.Id]);
        // Stored in EF's SQLite Guid format, so they can be queried by value.
        var stamp = stamps[first.Id];
        Assert.Equal(first.Id, await db.Set<ExchangeRate>().Where(r => r.CreatedByUserId == stamp).Select(r => r.Id).SingleAsync());
    }

    private static ExchangeRate NewRate(decimal rate, string source) => new()
    {
        BaseCurrency = "USD",
        QuoteCurrency = "PKR",
        Rate = rate,
        EffectiveAt = DateTime.UtcNow.AddTicks(Random.Shared.Next()),
        Source = source,
        CreatedAt = DateTime.UtcNow,
    };

    private async Task<ExchangeRate> AddRateAsync(decimal rate, string source = "test")
    {
        await using var db = Db();
        var entity = NewRate(rate, source);
        db.Set<ExchangeRate>().Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    private static async Task<T> ScalarAsync<T>(AppDbContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            return (T)(await cmd.ExecuteScalarAsync())!;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
