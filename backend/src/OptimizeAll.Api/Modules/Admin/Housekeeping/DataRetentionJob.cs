using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Jobs;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Admin.Housekeeping;

/// <summary>
/// Retention periods for high-volume operational tables (configuration section <c>DataRetention</c>, see
/// docs/DATABASE.md § Retention). A value of 0 keeps the rows forever. Audit logs, ledger entries, submissions, invoices
/// and other business/legal records are never touched.
/// </summary>
public sealed class DataRetentionOptions
{
    public const string Section = "DataRetention";

    /// <summary>Master switch for <see cref="DataRetentionJob"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Background job run log (<c>job_runs</c>): runs that started more than this many days ago.</summary>
    public int JobRunDays { get; set; } = 30;

    /// <summary>In-app notifications the user has read, created more than this many days ago.</summary>
    public int ReadNotificationDays { get; set; } = 180;

    /// <summary>Any in-app notification (read or not) created more than this many days ago.</summary>
    public int NotificationDays { get; set; } = 365;

    /// <summary>Refresh tokens and email/password tokens that expired more than this many days ago.</summary>
    public int ExpiredTokenDays { get; set; } = 30;

    /// <summary>
    /// Raw tracking events (<c>tracking_clicks</c>, <c>landing_page_views</c>) older than this many days. Off by default:
    /// analytics for periods before the cutoff would count fewer clicks/views once they are deleted.
    /// </summary>
    public int TrackingEventDays { get; set; }

    /// <summary>Rows deleted per statement (short statements keep lock times and replication lag small).</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>Statements per table per run; the rest is picked up by the next (hourly) run.</summary>
    public int MaxBatchesPerTable { get; set; } = 50;
}

/// <summary>
/// Hourly housekeeping of append-only operational tables according to <see cref="DataRetentionOptions"/>. Deletes in
/// small batches (select a batch of ids, delete by id), oldest first, so no statement holds locks for long. Candidate
/// scans are primary-key ranges: ids are UUIDv7 (<see cref="IdGenerator"/>), so rows created before a cutoff have ids
/// below <see cref="IdGenerator.LowerBound"/> of it, and the exact time condition is applied on top. Idempotent and
/// safe to retry: a crash midway only leaves rows for the next run.
/// </summary>
public sealed class DataRetentionJob(AppDbContext db, IOptions<DataRetentionOptions> options, TimeProvider clock) : IJob
{
    public string Name => nameof(DataRetentionJob);

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (!o.Enabled) return "disabled";
        var now = clock.GetUtcNow().UtcDateTime;
        var summary = new List<string>();

        if (o.JobRunDays > 0)
        {
            var cutoff = now.AddDays(-o.JobRunDays);
            // Served by IX_job_runs_StartedAt.
            summary.Add($"job_runs={await DeleteAsync(o, db.Set<JobRun>(),
                q => q.Where(r => r.StartedAt < cutoff && r.Status != JobRunStatus.Running).OrderBy(r => r.StartedAt).Select(r => r.Id), ct)}");
        }

        if (o.ReadNotificationDays > 0 || o.NotificationDays > 0)
        {
            var byRead = o.ReadNotificationDays > 0;
            var byAge = o.NotificationDays > 0;
            var readCutoff = now.AddDays(-Math.Max(0, o.ReadNotificationDays));
            var allCutoff = now.AddDays(-Math.Max(0, o.NotificationDays));
            var bound = IdGenerator.LowerBound(byRead && (!byAge || readCutoff > allCutoff) ? readCutoff : allCutoff);
            var deliveries = db.Set<NotificationDelivery>();
            // Never delete a notification whose e-mail/WhatsApp copy is still queued or being sent (deliveries cascade).
            summary.Add($"notifications={await DeleteAsync(o, db.Set<Notification>(),
                q => q.Where(n => n.Id.CompareTo(bound) < 0 &&
                                  ((byRead && n.ReadAt != null && n.CreatedAt < readCutoff) || (byAge && n.CreatedAt < allCutoff)) &&
                                  !deliveries.Any(d => d.NotificationId == n.Id &&
                                                       (d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.Sending)))
                    .OrderBy(n => n.Id).Select(n => n.Id), ct)}");
        }

        if (o.ExpiredTokenDays > 0)
        {
            var cutoff = now.AddDays(-o.ExpiredTokenDays);
            var bound = IdGenerator.LowerBound(cutoff); // a token is created before it expires
            summary.Add($"refresh_tokens={await DeleteAsync(o, db.Set<RefreshToken>(),
                q => q.Where(t => t.Id.CompareTo(bound) < 0 && t.ExpiresAt < cutoff).OrderBy(t => t.Id).Select(t => t.Id), ct)}");
            summary.Add($"user_tokens={await DeleteAsync(o, db.Set<UserToken>(),
                q => q.Where(t => t.Id.CompareTo(bound) < 0 && t.ExpiresAt < cutoff).OrderBy(t => t.Id).Select(t => t.Id), ct)}");
        }

        if (o.TrackingEventDays > 0)
        {
            var cutoff = now.AddDays(-o.TrackingEventDays);
            var bound = IdGenerator.LowerBound(cutoff);
            summary.Add($"tracking_clicks={await DeleteAsync(o, db.Set<TrackingClick>(),
                q => q.Where(k => k.Id.CompareTo(bound) < 0 && k.ClickedAt < cutoff).OrderBy(k => k.Id).Select(k => k.Id), ct)}");
            summary.Add($"landing_page_views={await DeleteAsync(o, db.Set<LandingPageView>(),
                q => q.Where(v => v.Id.CompareTo(bound) < 0 && v.ViewedAt < cutoff).OrderBy(v => v.Id).Select(v => v.Id), ct)}");
        }

        return summary.Count == 0 ? "nothing configured" : "deleted " + string.Join(" ", summary);
    }

    /// <summary>Deletes the rows <paramref name="candidates"/> selects, <see cref="DataRetentionOptions.BatchSize"/> ids at a time.</summary>
    private static async Task<int> DeleteAsync<T>(DataRetentionOptions o, DbSet<T> set, Func<IQueryable<T>, IQueryable<Guid>> candidates,
        CancellationToken ct) where T : Entity
    {
        var batch = Math.Clamp(o.BatchSize, 1, 10_000);
        var deleted = 0;
        for (var i = 0; i < Math.Max(1, o.MaxBatchesPerTable); i++)
        {
            var ids = await candidates(set.AsNoTracking()).Take(batch).ToListAsync(ct);
            if (ids.Count == 0) break;
            deleted += await set.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync(ct);
            if (ids.Count < batch) break;
        }
        return deleted;
    }
}
