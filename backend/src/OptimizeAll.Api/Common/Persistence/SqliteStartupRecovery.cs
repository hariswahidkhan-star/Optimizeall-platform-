using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Persistence;

/// <summary>What startup finds in the SQLite file before migrating (part of <see cref="BaselineUpgrade"/>).</summary>
/// <param name="Path">Full path of the database file.</param>
/// <param name="ForeignBaseline">
/// The earlier <c>InitialCreate</c> the file was created from, <see cref="BaselineUpgrade.NoHistoryBaseline"/> when it has
/// tables but no migrations history, or null when it is missing, empty or on this build's baseline.
/// </param>
/// <param name="Unreadable">Why the file cannot be used (not a database, corrupt), or null.</param>
internal sealed record SqliteProbeResult(string Path, string? ForeignBaseline, string? Unreadable);

/// <summary>
/// Reads the SQLite file directly (own unpooled connection, no EF interceptors) so a corrupt or foreign file is classified
/// instead of failing somewhere inside <c>MigrateAsync</c>. Deliberate raw SQL like <see cref="SqliteBaselineUpgrader"/>:
/// only fixed catalog queries.
/// </summary>
internal static class SqliteDatabaseProbe
{
    private const string HistoryTable = "__EFMigrationsHistory";

    public static async Task<SqliteProbeResult> InspectAsync(AppDbContext db, IReadOnlyCollection<string> known, bool verifyIntegrity, CancellationToken ct)
    {
        var path = System.IO.Path.GetFullPath(new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource);
        await db.Database.CloseConnectionAsync();
        if (!File.Exists(path) || new FileInfo(path).Length == 0) return new SqliteProbeResult(path, null, null);

        try
        {
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, Mode = SqliteOpenMode.ReadWrite }.ConnectionString);
            await connection.OpenAsync(ct);
            var tables = await ListAsync(connection,
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite\\_%' ESCAPE '\\';", ct);
            if (verifyIntegrity)
            {
                // quick_check: structural check of every page, without the (slower) index/row cross-check of integrity_check.
                var check = await ListAsync(connection, "PRAGMA quick_check(5);", ct);
                if (check is not ["ok"])
                    return new SqliteProbeResult(path, null, "quick_check: " + string.Join("; ", check));
            }

            var hasHistory = tables.Contains(HistoryTable, StringComparer.OrdinalIgnoreCase);
            var applied = hasHistory ? await ListAsync(connection, $"SELECT MigrationId FROM \"{HistoryTable}\";", ct) : new List<string>();
            var otherTables = tables.Count(t => !string.Equals(t, HistoryTable, StringComparison.OrdinalIgnoreCase));
            var foreign = BaselineUpgrade.FindForeignBaseline(applied, known)
                          ?? (applied.Count == 0 && otherTables > 0 ? BaselineUpgrade.NoHistoryBaseline : null);
            return new SqliteProbeResult(path, foreign, null);
        }
        catch (SqliteException ex)
        {
            return new SqliteProbeResult(path, null, $"SQLite error {ex.SqliteErrorCode}: {ex.Message}");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    private static async Task<List<string>> ListAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(ct)) rows.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? "");
        return rows;
    }
}

/// <summary>
/// <see cref="BaselineUpgradeMode.AutoOrFresh"/>: moves the unusable SQLite file (with its -wal/-shm, so the copy stays
/// consistent) unchanged to <c>&lt;db dir&gt;/backups/&lt;name&gt;-&lt;label&gt;-&lt;utc&gt;-unmigrated.db</c>, so the caller's
/// <c>MigrateAsync</c> creates a fresh database and the seeders fill it. Demo/staging only.
/// </summary>
internal static class SqliteFreshStart
{
    public static async Task<BaselineUpgradeSummary> SetAsideAsync(
        AppDbContext db, string livePath, string label, string reason, ILogger logger, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await db.Database.CloseConnectionAsync();
        SqliteConnection.ClearAllPools();

        var directory = Path.GetDirectoryName(livePath)!;
        var backups = Path.Combine(directory, "backups");
        Directory.CreateDirectory(backups);
        var safeLabel = string.Concat(label.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '-'));
        var target = Path.Combine(backups,
            $"{Path.GetFileNameWithoutExtension(livePath)}-{safeLabel}-{DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}-unmigrated.db");

        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            if (File.Exists(livePath + suffix)) File.Move(livePath + suffix, target + suffix, overwrite: true);
        File.Move(livePath, target, overwrite: true);
        foreach (var leftover in new[] { livePath + ".baseline-upgrade.tmp", livePath + ".baseline-upgrade.tmp-wal", livePath + ".baseline-upgrade.tmp-shm" })
            TryDelete(leftover, logger);

        logger.LogCritical(
            "DATABASE RESET (Database:BaselineUpgrade=AutoOrFresh): the SQLite database could not be upgraded or read, so the " +
            "API starts with a FRESH, EMPTY database at {Live} (migrations + seed profiles). The old data was NOT deleted: the " +
            "original file was moved unchanged to {Backup}. Reason: {Reason}. To recover it, stop the API, fix the file and " +
            "move it back (docs/DATABASE.md § Baseline upgrade, docs/RENDER.md). AutoOrFresh is for demo/staging only.",
            livePath, target, reason);
        return new BaselineUpgradeSummary(label, target, Array.Empty<BaselineTableCopy>(), Array.Empty<string>(),
            Array.Empty<string>(), TimeSpan.Zero) { StartedFresh = true };
    }

    public static void TryDelete(string path, ILogger logger)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not delete {Path}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Could not delete {Path}", path);
        }
    }
}
