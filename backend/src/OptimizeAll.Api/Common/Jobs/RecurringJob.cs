using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Jobs;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Jobs;

public sealed class JobOptions
{
    public const string Section = "Jobs";

    /// <summary>Master switch; disabled in integration tests, which trigger jobs explicitly.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>A unit of background work. Implementations are resolved in a fresh DI scope per run.</summary>
public interface IJob
{
    /// <summary>Stable job name, used for leases and the job-run log.</summary>
    string Name { get; }

    /// <summary>
    /// Runs one pass. Must be safe to retry: use idempotency keys / conditional updates so a crash midway and a
    /// re-run cannot duplicate effects. Returns a short summary for the run log.
    /// </summary>
    Task<string> ExecuteAsync(CancellationToken ct);
}

/// <summary>
/// Runs jobs with a DB lease (one instance at a time across a cluster), records each run in job_runs with its
/// outcome and error, and retries failed runs on the next tick. Also used by admin "run now" endpoints and tests.
/// </summary>
public sealed class JobRunner(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<JobRunner> logger)
{
    private static readonly string InstanceId = $"{Environment.MachineName}:{Environment.ProcessId}";

    public async Task<JobRun?> RunAsync<TJob>(CancellationToken ct = default) where TJob : IJob =>
        await RunAsync(typeof(TJob), ct);

    public async Task<JobRun?> RunAsync(Type jobType, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = (IJob)scope.ServiceProvider.GetRequiredService(jobType);
        var leaseFor = TimeSpan.FromMinutes(10);

        if (!await TryAcquireLeaseAsync(db, job.Name, leaseFor, ct))
        {
            logger.LogDebug("Job {Job} skipped: lease held by another instance", job.Name);
            return null;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var run = new JobRun
        {
            JobName = job.Name,
            RunKey = now.ToString("yyyyMMddHHmmssfff"),
            Status = JobRunStatus.Running,
            Attempt = 1,
            StartedAt = now,
            InstanceId = InstanceId,
        };
        db.Set<JobRun>().Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            run.Summary = Truncate(await job.ExecuteAsync(ct), 2000);
            run.Status = JobRunStatus.Succeeded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Job {Job} failed", job.Name);
            run.Status = JobRunStatus.Failed;
            run.Error = Truncate(ex.ToString(), 60000);
        }
        finally
        {
            run.FinishedAt = clock.GetUtcNow().UtcDateTime;
            db.ChangeTracker.Clear();
            db.Set<JobRun>().Attach(run);
            db.Entry(run).Property(r => r.Status).IsModified = true;
            db.Entry(run).Property(r => r.Summary).IsModified = true;
            db.Entry(run).Property(r => r.Error).IsModified = true;
            db.Entry(run).Property(r => r.FinishedAt).IsModified = true;
            await db.SaveChangesAsync(CancellationToken.None);
            await ReleaseLeaseAsync(db, job.Name);
        }
        return run;
    }

    private async Task<bool> TryAcquireLeaseAsync(AppDbContext db, string name, TimeSpan duration, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var until = now.Add(duration);

        // Insert-if-absent, then conditional takeover of an expired lease; both are atomic statements.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT IGNORE INTO job_leases (Name, Holder, LeasedUntil) VALUES ({name}, {""}, {now.AddSeconds(-1)})", ct);
        var taken = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE job_leases SET Holder = {InstanceId}, LeasedUntil = {until} WHERE Name = {name} AND (LeasedUntil < {now} OR Holder = {InstanceId})", ct);
        return taken == 1;
    }

    private async Task ReleaseLeaseAsync(AppDbContext db, string name)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE job_leases SET LeasedUntil = {clock.GetUtcNow().UtcDateTime.AddSeconds(-1)} WHERE Name = {name} AND Holder = {InstanceId}");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>Hosted loop that runs a job every <paramref name="interval"/>.</summary>
public sealed class RecurringJobService<TJob>(
    JobRunner runner,
    Microsoft.Extensions.Options.IOptions<JobOptions> options,
    ILogger<RecurringJobService<TJob>> logger,
    TimeSpan interval) : BackgroundService where TJob : IJob
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;

        // Stagger start so instances don't all hit the database at boot.
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(5, 20)), stoppingToken);
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await runner.RunAsync<TJob>(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Recurring job {Job} loop error", typeof(TJob).Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public static class JobRegistration
{
    /// <summary>Registers a job for DI and schedules it to run every <paramref name="interval"/>.</summary>
    public static IServiceCollection AddRecurringJob<TJob>(this IServiceCollection services, TimeSpan interval)
        where TJob : class, IJob
    {
        services.AddScoped<TJob>();
        services.AddHostedService(sp => new RecurringJobService<TJob>(
            sp.GetRequiredService<JobRunner>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JobOptions>>(),
            sp.GetRequiredService<ILogger<RecurringJobService<TJob>>>(),
            interval));
        return services;
    }
}
