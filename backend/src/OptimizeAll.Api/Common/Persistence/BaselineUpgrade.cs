using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Persistence;

/// <summary>
/// Upgrades a database created from an older, since-replaced <c>InitialCreate</c> migration ("foreign baseline") to the
/// current schema without losing data (docs/DATABASE.md § Baseline upgrade).
/// <para>Until the baseline was frozen, every schema change regenerated the single <c>InitialCreate</c> migration, so its
/// id changed with each release. A database created by an earlier release records that earlier id in
/// <c>__EFMigrationsHistory</c>; <c>MigrateAsync</c> then treats the current <c>InitialCreate</c> as pending and fails on its
/// first <c>CREATE TABLE</c>. This class detects that state and, on SQLite, rebuilds the database: current schema in a new
/// file, rows of every common table copied over column by column, verified, then swapped in (the original is kept as a
/// backup). On MySQL it refuses with instructions (scripts/upgrade-baseline-mysql.sh).</para>
/// </summary>
public static class BaselineUpgrade
{
    public const string InitialCreateSuffix = "_InitialCreate";

    /// <summary>
    /// The applied migration id that is an <c>InitialCreate</c> this build does not contain, or null when the database is
    /// empty, current, or only behind by migrations this build knows.
    /// </summary>
    public static string? FindForeignBaseline(IEnumerable<string> applied, IEnumerable<string> known)
    {
        var knownSet = new HashSet<string>(known, StringComparer.Ordinal);
        return applied
            .Where(id => id.EndsWith(InitialCreateSuffix, StringComparison.Ordinal) && !knownSet.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .LastOrDefault();
    }

    /// <summary>
    /// Runs before <c>MigrateAsync</c>. Returns the upgrade summary when an upgrade ran, null when none was needed.
    /// Throws <see cref="BaselineUpgradeException"/> when an upgrade is needed but not allowed or not possible.
    /// </summary>
    public static async Task<BaselineUpgradeSummary?> RunIfNeededAsync(
        IServiceProvider sp, AppDbContext db, DatabaseOptions options, ILogger logger, CancellationToken ct)
    {
        var mode = ParseMode(options.BaselineUpgrade); // validated even when no upgrade is needed
        var known = db.Database.GetMigrations().ToList();
        var current = known.FirstOrDefault(id => id.EndsWith(InitialCreateSuffix, StringComparison.Ordinal))
                      ?? throw new InvalidOperationException("This build contains no InitialCreate migration.");
        var foreign = FindForeignBaseline(await db.Database.GetAppliedMigrationsAsync(ct), known);
        if (foreign is null) return null;

        if (db.IsMySql)
            throw new BaselineUpgradeException(
                $"The MySQL database was created by an earlier release (baseline migration '{foreign}'); this release's " +
                $"baseline is '{current}', so its schema cannot be migrated in place and the API will not start. Automatic " +
                "baseline upgrade is supported on SQLite only. Back up the database, then run " +
                "scripts/upgrade-baseline-mysql.sh (docs/DATABASE.md § Baseline upgrade), which copies the data into the " +
                "current schema and keeps the old tables in a backup database; or redeploy the release that created it.");
        if (mode == BaselineUpgradeMode.Refuse)
            throw new BaselineUpgradeException(
                $"The SQLite database was created by an earlier release (baseline migration '{foreign}'); this release's " +
                $"baseline is '{current}'. Database:BaselineUpgrade is 'Refuse', so the API will not start. Set " +
                "Database:BaselineUpgrade=Auto (the default) to back up the file and copy its data into the current schema " +
                "at startup (docs/DATABASE.md § Baseline upgrade), or redeploy the release that created it.");

        var dialect = sp.GetRequiredService<IDatabaseDialect>();
        await using var _ = await dialect.AcquireNamedLockAsync(db, "database-baseline-upgrade", TimeSpan.FromMinutes(5), ct);
        // Another caller in this process may have upgraded the file while this one waited for the lock.
        db.ChangeTracker.Clear();
        foreign = FindForeignBaseline(await db.Database.GetAppliedMigrationsAsync(ct), known);
        if (foreign is null) return null;

        logger.LogWarning(
            "Database baseline {Foreign} is not this release's baseline {Current}: upgrading the SQLite database by copying " +
            "its data into the current schema (Database:BaselineUpgrade=Auto)", foreign, current);
        var summary = await new SqliteBaselineUpgrader(sp, db, logger).UpgradeAsync(foreign, ct);
        summary.Log(logger);
        return summary;
    }

    public static BaselineUpgradeMode ParseMode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? BaselineUpgradeMode.Auto
        : Enum.TryParse<BaselineUpgradeMode>(value.Trim(), ignoreCase: true, out var mode) && Enum.IsDefined(mode) ? mode
        : throw new InvalidOperationException($"Unknown Database:BaselineUpgrade '{value}'. Use Auto or Refuse.");
}

public enum BaselineUpgradeMode
{
    /// <summary>SQLite: back up the file and copy its data into the current schema at startup. MySQL: refuse.</summary>
    Auto,

    /// <summary>Refuse to start when the database has a foreign baseline.</summary>
    Refuse,
}

/// <summary>The database cannot be brought to the current schema automatically; the message says what to do.</summary>
public sealed class BaselineUpgradeException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

public sealed record BaselineTableCopy(string Table, long Rows, IReadOnlyList<string> NewColumns, IReadOnlyList<string> DroppedColumns);

public sealed record BaselineUpgradeSummary(
    string FromBaseline,
    string BackupPath,
    IReadOnlyList<BaselineTableCopy> Copied,
    IReadOnlyList<string> NewTables,
    IReadOnlyList<string> DroppedTables,
    TimeSpan Elapsed)
{
    public void Log(ILogger logger)
    {
        logger.LogWarning(
            "Baseline upgrade from {From} finished in {Seconds:0.0}s: {Tables} tables copied ({Rows} rows), {NewTables} new " +
            "tables [{NewList}], {Dropped} tables no longer in the schema [{DroppedList}] (their rows stay in the backup). " +
            "Backup of the original database: {Backup}",
            FromBaseline, Elapsed.TotalSeconds, Copied.Count, Copied.Sum(c => c.Rows), NewTables.Count,
            string.Join(", ", NewTables), DroppedTables.Count, string.Join(", ", DroppedTables), BackupPath);
        foreach (var table in Copied)
        {
            var changed = table.NewColumns.Count > 0 || table.DroppedColumns.Count > 0;
            logger.Log(changed ? LogLevel.Information : LogLevel.Debug,
                "Baseline upgrade: {Table}: {Rows} rows copied; new columns [{New}]; dropped columns [{Dropped}]",
                table.Table, table.Rows, string.Join(", ", table.NewColumns), string.Join(", ", table.DroppedColumns));
        }
    }
}

/// <summary>
/// SQLite baseline upgrade. <b>Deliberate, documented exception to the no-raw-SQL rule</b> (docs/ARCHITECTURE.md): copying
/// arbitrary tables between two schemas cannot be expressed through the EF model. Every identifier comes from
/// <c>sqlite_master</c> / <c>pragma_table_xinfo</c> of the two database files and every default value from the EF model;
/// nothing comes from user input, and identifiers are always quoted.
/// <para>Steps: (1) online backup of the live file to <c>&lt;dir&gt;/backups/&lt;name&gt;-&lt;oldBaseline&gt;-&lt;utc&gt;.db</c>;
/// (2) the current schema is created by <c>MigrateAsync</c> in a temporary file next to the live one; (3) the backup is
/// attached and, with foreign keys off, each table present in both gets its rows copied through the intersection of
/// their columns (new NOT NULL columns without a database default get the EF model's default, or the CLR default);
/// (4) row counts, <c>foreign_key_check</c> and <c>integrity_check</c> are verified; (5) the temporary file is renamed over
/// the live one (atomic on the same file system). Any failure before (5) leaves the live database untouched.</para>
/// </summary>
internal sealed class SqliteBaselineUpgrader(IServiceProvider sp, AppDbContext db, ILogger logger)
{
    private const string HistoryTable = "__EFMigrationsHistory";

    public async Task<BaselineUpgradeSummary> UpgradeAsync(string foreignBaseline, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var livePath = Path.GetFullPath(new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource);
        var directory = Path.GetDirectoryName(livePath)!;
        var name = Path.GetFileNameWithoutExtension(livePath);
        var tempPath = livePath + ".baseline-upgrade.tmp";
        var backupDirectory = Path.Combine(directory, "backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory,
            $"{name}-{foreignBaseline}-{DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}.db");

        await db.Database.CloseConnectionAsync();
        SqliteConnection.ClearAllPools();
        DeleteDatabaseFiles(tempPath); // left over from an interrupted attempt

        // (1) Backup: checkpoint, then SQLite's online backup (a consistent single-file copy including any WAL content).
        await using (var live = await OpenAsync(livePath, ct))
        {
            await CheckpointAsync(live, livePath, ct);
            await using var backup = await OpenAsync(backupPath, ct);
            live.BackupDatabase(backup);
        }
        logger.LogWarning("Baseline upgrade: backed up {Live} to {Backup}", livePath, backupPath);

        BaselineUpgradeSummary summary;
        try
        {
            // (2) Current schema in a new file, through the same migrations a fresh database gets.
            using (var scope = sp.CreateScope())
            {
                var fresh = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                fresh.Database.SetConnectionString(ConnectionString(tempPath));
                await fresh.Database.MigrateAsync(ct);
                await fresh.Database.CloseConnectionAsync();
            }

            // (3) + (4) Copy and verify.
            await using (var target = await OpenAsync(tempPath, ct))
            {
                summary = await CopyAsync(target, backupPath, foreignBaseline, ct);
                await CheckpointAsync(target, tempPath, ct);
            }
        }
        catch (Exception ex)
        {
            SqliteConnection.ClearAllPools();
            DeleteDatabaseFiles(tempPath);
            throw new BaselineUpgradeException(
                $"Baseline upgrade from '{foreignBaseline}' failed; the database {livePath} was left unchanged (a backup is at " +
                $"{backupPath}). Cause: {ex.Message} Fix the data or set Database:BaselineUpgrade=Refuse and upgrade manually " +
                "(docs/DATABASE.md § Baseline upgrade).", ex);
        }

        // (5) Swap. No connection is open (no pooling on the upgrade connections, pools cleared); the live file was fully
        // checkpointed, so its -wal/-shm hold nothing and must not be paired with the new file.
        SqliteConnection.ClearAllPools();
        await using (var live = await OpenAsync(livePath, ct))
            await CheckpointAsync(live, livePath, ct);
        SqliteConnection.ClearAllPools();
        DeleteSidecars(tempPath);
        DeleteSidecars(livePath);
        File.Move(tempPath, livePath, overwrite: true);
        SqliteConnection.ClearAllPools();
        return summary with { BackupPath = backupPath, Elapsed = stopwatch.Elapsed };
    }

    private async Task<BaselineUpgradeSummary> CopyAsync(SqliteConnection target, string oldPath, string foreignBaseline, CancellationToken ct)
    {
        // Foreign keys must be switched off outside a transaction; the copy order then doesn't matter.
        await ExecAsync(target, "PRAGMA foreign_keys=OFF;", ct);
        await using (var attach = target.CreateCommand())
        {
            attach.CommandText = "ATTACH DATABASE $path AS old;";
            attach.Parameters.AddWithValue("$path", oldPath);
            await attach.ExecuteNonQueryAsync(ct);
        }

        var newTables = await TablesAsync(target, "main", ct);
        var oldTables = (await TablesAsync(target, "old", ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaults = ModelDefaults();
        var copied = new List<BaselineTableCopy>();
        var created = new List<string>();

        await using (var tx = (SqliteTransaction)await target.BeginTransactionAsync(ct))
        {
            foreach (var table in newTables)
            {
                if (!oldTables.Contains(table)) { created.Add(table); continue; }
                var newColumns = await ColumnsAsync(target, "main", table, ct);
                var oldColumns = (await ColumnsAsync(target, "old", table, ct)).ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

                var insert = new List<string>();
                var select = new List<string>();
                var added = new List<string>();
                foreach (var column in newColumns)
                {
                    var fallback = column.NotNull && column.PrimaryKey == 0
                        ? column.Default ?? DefaultLiteral(defaults, table, column)
                        : null;
                    if (oldColumns.TryGetValue(column.Name, out var old))
                    {
                        insert.Add(Quote(column.Name));
                        // A column that was nullable and is now NOT NULL keeps its values and fills its gaps.
                        select.Add(fallback is not null && !old.NotNull ? $"COALESCE({Quote(old.Name)}, {fallback})" : Quote(old.Name));
                    }
                    else
                    {
                        added.Add(column.Name);
                        if (column.NotNull && column.Default is null && column.PrimaryKey == 0)
                        {
                            insert.Add(Quote(column.Name));
                            select.Add(fallback!);
                        }
                    }
                }
                var dropped = oldColumns.Keys.Where(c => !newColumns.Any(n => string.Equals(n.Name, c, StringComparison.OrdinalIgnoreCase))).ToList();

                await ExecAsync(target, $"DELETE FROM main.{Quote(table)};", ct, tx);
                await ExecAsync(target,
                    $"INSERT INTO main.{Quote(table)} ({string.Join(", ", insert)}) SELECT {string.Join(", ", select)} FROM old.{Quote(table)};",
                    ct, tx);
                var oldCount = await ScalarAsync(target, $"SELECT COUNT(*) FROM old.{Quote(table)};", ct, tx);
                var newCount = await ScalarAsync(target, $"SELECT COUNT(*) FROM main.{Quote(table)};", ct, tx);
                if (oldCount != newCount)
                    throw new InvalidOperationException($"Table {table}: copied {newCount} of {oldCount} rows.");
                copied.Add(new BaselineTableCopy(table, newCount, added, dropped));
            }

            var violations = await ForeignKeyViolationsAsync(target, tx, ct);
            if (violations.Count > 0)
                throw new InvalidOperationException(
                    $"foreign_key_check found {violations.Count} violation(s) after the copy, e.g. {string.Join("; ", violations.Take(10))}.");
            await tx.CommitAsync(ct);
        }

        await ExecAsync(target, "DETACH DATABASE old;", ct);
        await ExecAsync(target, "PRAGMA foreign_keys=ON;", ct);
        await using (var check = target.CreateCommand())
        {
            check.CommandText = "PRAGMA integrity_check;";
            var result = Convert.ToString(await check.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            if (!string.Equals(result, "ok", StringComparison.Ordinal))
                throw new InvalidOperationException($"integrity_check of the upgraded database failed: {result}");
        }

        var droppedTables = oldTables
            .Where(t => !newTables.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Where(t => !string.Equals(t, HistoryTable, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t, StringComparer.Ordinal).ToList();
        return new BaselineUpgradeSummary(foreignBaseline, oldPath, copied, created, droppedTables, TimeSpan.Zero);
    }

    /// <summary>EF model properties by (table, column), for default values of new NOT NULL columns.</summary>
    private Dictionary<(string Table, string Column), IProperty> ModelDefaults()
    {
        var map = new Dictionary<(string, string), IProperty>(TableColumnComparer.Instance);
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null) continue;
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName(store);
                if (column is not null) map.TryAdd((table, column), property);
            }
        }
        return map;
    }

    /// <summary>SQL literal of the model default (or CLR default) of a NOT NULL column, rendered by EF's type mapping.</summary>
    private static string DefaultLiteral(Dictionary<(string, string), IProperty> model, string table, Column column)
    {
        if (model.TryGetValue((table, column.Name), out var property))
        {
            var value = property.GetDefaultValue() ?? ClrDefault(property);
            if (value is not null)
            {
                var mapping = property.GetRelationalTypeMapping();
                return mapping.GenerateSqlLiteral(value);
            }
        }
        // By storage class (a column EF doesn't map, e.g. a shadow column dropped from the model).
        var type = column.Type.ToUpperInvariant();
        return type.Contains("INT") ? "0"
            : type.Contains("REAL") || type.Contains("FLOA") || type.Contains("DOUB") ? "0.0"
            : type.Contains("BLOB") ? "X''"
            : "''";
    }

    private static object? ClrDefault(IProperty property)
    {
        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (type == typeof(string)) return string.Empty;
        if (type == typeof(byte[])) return Array.Empty<byte>();
        if (type.IsValueType) return Activator.CreateInstance(type);
        // Reference types stored through a converter (e.g. a list serialized to JSON) default to an empty instance.
        return type.GetConstructor(Type.EmptyTypes) is not null ? Activator.CreateInstance(type) : null;
    }

    private static async Task<List<string>> TablesAsync(SqliteConnection connection, string schema, CancellationToken ct)
    {
        var tables = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT name FROM {schema}.sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite\\_%' ESCAPE '\\' " +
            $"AND name <> '{HistoryTable}' ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) tables.Add(reader.GetString(0));
        return tables;
    }

    private sealed record Column(string Name, string Type, bool NotNull, string? Default, int PrimaryKey);

    private static async Task<List<Column>> ColumnsAsync(SqliteConnection connection, string schema, string table, CancellationToken ct)
    {
        var columns = new List<Column>();
        await using var command = connection.CreateCommand();
        // hidden: 0 = normal column; generated and hidden virtual-table columns can't be inserted.
        command.CommandText = "SELECT name, type, \"notnull\", dflt_value, pk FROM pragma_table_xinfo($table, $schema) WHERE hidden = 0 ORDER BY cid;";
        command.Parameters.AddWithValue("$table", table);
        command.Parameters.AddWithValue("$schema", schema);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            columns.Add(new Column(reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1), reader.GetInt64(2) != 0,
                reader.IsDBNull(3) ? null : reader.GetString(3), (int)reader.GetInt64(4)));
        return columns;
    }

    private static async Task<List<string>> ForeignKeyViolationsAsync(SqliteConnection connection, SqliteTransaction tx, CancellationToken ct)
    {
        var violations = new List<string>();
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "PRAGMA main.foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            violations.Add($"{reader.GetString(0)} row {(reader.IsDBNull(1) ? "?" : reader.GetValue(1))} → {reader.GetString(2)}");
        return violations;
    }

    private static async Task CheckpointAsync(SqliteConnection connection, string path, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct) && reader.GetInt64(0) != 0)
            throw new InvalidOperationException($"Could not checkpoint {path}: another connection is using the database.");
    }

    private static async Task ExecAsync(SqliteConnection connection, string sql, CancellationToken ct, SqliteTransaction? tx = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql, CancellationToken ct, SqliteTransaction tx)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    /// <summary>Unpooled, so closing the connection really closes the file (the files are renamed afterwards).</summary>
    private static string ConnectionString(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = 300 }.ConnectionString;

    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken ct)
    {
        var connection = new SqliteConnection(ConnectionString(path));
        await connection.OpenAsync(ct);
        await ExecAsync(connection, $"PRAGMA busy_timeout={SqlitePragmaInterceptor.BusyTimeoutMilliseconds};", ct);
        return connection;
    }

    /// <summary>Quotes an identifier taken from sqlite_master / pragma_table_xinfo.</summary>
    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static void DeleteSidecars(string path)
    {
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }

    private static void DeleteDatabaseFiles(string path)
    {
        DeleteSidecars(path);
        if (File.Exists(path)) File.Delete(path);
    }

    private sealed class TableColumnComparer : IEqualityComparer<(string Table, string Column)>
    {
        public static readonly TableColumnComparer Instance = new();
        public bool Equals((string Table, string Column) x, (string Table, string Column) y) =>
            string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Table, string Column) obj) =>
            HashCode.Combine(obj.Table.ToUpperInvariant(), obj.Column.ToUpperInvariant());
    }
}
