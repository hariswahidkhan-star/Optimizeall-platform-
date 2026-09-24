using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySqlConnector;

namespace OptimizeAll.DbBench;

public sealed record StatementResult(string Sql, string Plan);

public sealed record CaseResult(string Name, string Serves, double MedianMs, double MinMs, long RowsRead, List<StatementResult> Statements, string? Error);

/// <summary>
/// Runs each case once to warm up, then <c>runs</c> times, and reports the median wall time, the rows InnoDB read
/// (sum of the session's Handler_read_* counters for one run: a load-independent measure of the work done) and, for
/// every SQL statement the case sent, <c>EXPLAIN ANALYZE</c> (the plan with actual rows and timings).
/// </summary>
public sealed class BenchRunner(string connection, int runs)
{
    public async Task<List<CaseResult>> RunAsync(IReadOnlyList<BenchCase> cases, string? only)
    {
        var capture = new CaptureInterceptor();
        await using var db = Db.Create(connection, capture);
        await db.Database.OpenConnectionAsync();
        var conn = (MySqlConnection)db.Database.GetDbConnection();
        await Exec(conn, "ANALYZE TABLE audit_logs, notifications, notification_deliveries, job_runs, email_campaign_recipients, email_events, " +
                         "email_subscribers, submissions, submission_events, form_submissions, landing_page_views, tracking_clicks, time_entries, " +
                         "thread_messages, crm_contacts, ads_daily_metrics, refresh_tokens, user_tokens");
        var context = await BenchContext.ResolveAsync(db);
        var statusOverhead = await RowsReadAsync(conn, () => Task.CompletedTask);

        var results = new List<CaseResult>();
        foreach (var c in cases.Where(c => only is null || c.Name.StartsWith(only, StringComparison.Ordinal)))
        {
            try
            {
                db.ChangeTracker.Clear();
                capture.Commands.Clear();
                capture.Enabled = true;
                await c.Run(db, context); // warm-up; captures the SQL
                capture.Enabled = false;
                var statements = capture.Commands.ToList();

                var rowsRead = await RowsReadAsync(conn, () => c.Run(db, context)) - statusOverhead;
                var times = new List<double>();
                for (var i = 0; i < runs; i++)
                {
                    var sw = Stopwatch.StartNew();
                    await c.Run(db, context);
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
                times.Sort();

                var explained = new List<StatementResult>();
                foreach (var s in statements) explained.Add(new StatementResult(s.Sql, await ExplainAsync(conn, s)));
                results.Add(new CaseResult(c.Name, c.Serves, Math.Round(times[times.Count / 2], 2), Math.Round(times[0], 2), rowsRead, explained, null));
                Console.WriteLine($"{c.Name,-36} {times[times.Count / 2],9:F2} ms  {rowsRead,10:N0} rows read");
            }
            catch (Exception ex)
            {
                capture.Enabled = false;
                results.Add(new CaseResult(c.Name, c.Serves, 0, 0, 0, new(), ex.Message));
                Console.WriteLine($"{c.Name,-36} ERROR {ex.Message}");
            }
        }
        return results;
    }

    private static async Task<long> RowsReadAsync(MySqlConnection conn, Func<Task> action)
    {
        var before = await HandlerReadsAsync(conn);
        await action();
        return await HandlerReadsAsync(conn) - before;
    }

    private static async Task<long> HandlerReadsAsync(MySqlConnection conn)
    {
        await using var cmd = new MySqlCommand("SHOW SESSION STATUS LIKE 'Handler_read%'", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        long total = 0;
        while (await reader.ReadAsync()) total += long.Parse(reader.GetString(1));
        return total;
    }

    private static async Task Exec(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) { }
    }

    private static async Task<string> ExplainAsync(MySqlConnection conn, CapturedCommand s)
    {
        try
        {
            await using var cmd = new MySqlCommand("EXPLAIN ANALYZE " + s.Sql, conn);
            foreach (var p in s.Parameters) cmd.Parameters.Add(new MySqlParameter(p.Name, p.Value ?? DBNull.Value));
            var sb = new StringBuilder();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) sb.AppendLine(reader.GetString(0));
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return "EXPLAIN failed: " + ex.Message;
        }
    }
}

public sealed record CapturedParameter(string Name, object? Value);

public sealed record CapturedCommand(string Sql, List<CapturedParameter> Parameters);

/// <summary>Records the SQL (and parameter values) EF sends while <see cref="Enabled"/>.</summary>
public sealed class CaptureInterceptor : DbCommandInterceptor
{
    public bool Enabled { get; set; }
    public List<CapturedCommand> Commands { get; } = new();

    private void Capture(DbCommand command)
    {
        if (!Enabled) return;
        Commands.Add(new CapturedCommand(command.CommandText,
            command.Parameters.Cast<DbParameter>().Select(p => new CapturedParameter(p.ParameterName, p.Value)).ToList()));
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Capture(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Capture(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}

public static class BenchReport
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Task SaveAsync(List<CaseResult> results, string path) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(results, Json));

    public static async Task<List<CaseResult>> LoadAsync(string path) =>
        JsonSerializer.Deserialize<List<CaseResult>>(await File.ReadAllTextAsync(path), Json) ?? new();

    /// <summary>Before/after table plus the plans of every case whose plan changed.</summary>
    public static string Markdown(List<CaseResult> before, List<CaseResult> after)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Case | Serves | Before ms | After ms | Before rows read | After rows read |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|");
        foreach (var a in after)
        {
            var b = before.FirstOrDefault(x => x.Name == a.Name);
            sb.AppendLine($"| `{a.Name}` | {a.Serves} | {(b is null ? "–" : b.MedianMs.ToString("F2"))} | {a.MedianMs:F2} | " +
                          $"{(b is null ? "–" : b.RowsRead.ToString("N0"))} | {a.RowsRead:N0} |");
        }
        sb.AppendLine();
        foreach (var a in after)
        {
            var b = before.FirstOrDefault(x => x.Name == a.Name);
            sb.AppendLine($"### `{a.Name}`").AppendLine();
            for (var i = 0; i < a.Statements.Count; i++)
            {
                sb.AppendLine("```sql").AppendLine(a.Statements[i].Sql.Trim()).AppendLine("```");
                if (b is not null && i < b.Statements.Count)
                    sb.AppendLine("Before:").AppendLine("```").AppendLine(b.Statements[i].Plan).AppendLine("```");
                sb.AppendLine("After:").AppendLine("```").AppendLine(a.Statements[i].Plan).AppendLine("```");
            }
        }
        return sb.ToString();
    }
}
