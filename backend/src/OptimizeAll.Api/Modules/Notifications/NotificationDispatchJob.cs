using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Notifications;

/// <summary>
/// Delivers the notification outbox (Email/WhatsApp) with retry and backoff.
///
/// Exactly-once claiming: each due delivery is claimed with a conditional UPDATE (Pending → Sending with a
/// short lock) and only the instance whose UPDATE affected the row sends it, so concurrent runs can never send
/// the same delivery twice. Sent deliveries are never picked up again. A delivery stuck in Sending (process
/// crashed mid-send) is marked Failed for manual retry instead of being re-sent automatically, because the
/// provider may already have delivered it.
/// </summary>
public sealed class NotificationDispatchJob(
    AppDbContext db,
    IEnumerable<INotificationChannelSender> senders,
    TimeProvider clock,
    ILogger<NotificationDispatchJob> logger) : IJob
{
    public const int BatchSize = 100;
    public const int MaxAttempts = 6;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);

    /// <summary>Delay before retry after the Nth failed attempt (1-based). After <see cref="MaxAttempts"/> the delivery fails permanently.</summary>
    public static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12),
    };

    public string Name => nameof(NotificationDispatchJob);

    /// <summary>Retry delay after <paramref name="attempts"/> failed attempts, or null when the delivery must fail permanently.</summary>
    public static TimeSpan? RetryDelay(int attempts) =>
        attempts >= MaxAttempts || attempts < 1 ? null : Backoff[Math.Min(attempts, Backoff.Length) - 1];

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var bySender = new Dictionary<NotificationChannel, INotificationChannelSender>();
        foreach (var sender in senders) bySender[sender.Channel] = sender; // last registration wins

        var now = clock.GetUtcNow().UtcDateTime;
        var recovered = await RecoverInterruptedAsync(now, ct);

        var candidates = await db.Set<NotificationDelivery>().AsNoTracking()
            .Where(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= now && (d.LockedUntil == null || d.LockedUntil < now))
            .OrderBy(d => d.NextAttemptAt).ThenBy(d => d.Id)
            .Select(d => d.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        int claimed = 0, sent = 0, skipped = 0, retrying = 0, failed = 0;
        foreach (var id in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var claimNow = clock.GetUtcNow().UtcDateTime;
            var lockUntil = claimNow.Add(LockDuration);
            var won = await db.Set<NotificationDelivery>()
                .Where(d => d.Id == id && d.Status == DeliveryStatus.Pending && (d.LockedUntil == null || d.LockedUntil < claimNow))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, DeliveryStatus.Sending)
                    .SetProperty(d => d.LockedUntil, lockUntil), ct);
            if (won != 1) continue; // another instance claimed it
            claimed++;

            var outcome = await SendOneAsync(id, bySender, ct);
            switch (outcome)
            {
                case Outcome.Sent: sent++; break;
                case Outcome.Skipped: skipped++; break;
                case Outcome.Retrying: retrying++; break;
                default: failed++; break;
            }
        }

        return $"Claimed {claimed} of {candidates.Count} due deliveries: {sent} sent, {skipped} skipped, {retrying} scheduled for retry, " +
               $"{failed} failed permanently; {recovered} interrupted deliveries marked failed.";
    }

    private enum Outcome { Sent, Skipped, Retrying, Failed }

    private async Task<Outcome> SendOneAsync(Guid id, IReadOnlyDictionary<NotificationChannel, INotificationChannelSender> bySender, CancellationToken ct)
    {
        var delivery = await db.Set<NotificationDelivery>().AsNoTracking().FirstAsync(d => d.Id == id, ct);
        var notification = await db.Set<Notification>().AsNoTracking().FirstOrDefaultAsync(n => n.Id == delivery.NotificationId, ct);
        var user = await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == delivery.UserId, ct);

        ChannelSendResult result;
        if (notification is null || user is null)
            result = ChannelSendResult.Skipped("The notification or its recipient no longer exists.");
        else if (!bySender.TryGetValue(delivery.Channel, out var sender))
            result = ChannelSendResult.Skipped($"No sender is registered for the {delivery.Channel} channel.");
        else
        {
            try
            {
                result = await sender.SendAsync(delivery, notification, user, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "{Channel} delivery {DeliveryId} threw", delivery.Channel, delivery.Id);
                result = ChannelSendResult.Failed($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var mine = db.Set<NotificationDelivery>().Where(d => d.Id == id && d.Status == DeliveryStatus.Sending);
        switch (result.Status)
        {
            case ChannelSendStatus.Sent:
                await mine.ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, DeliveryStatus.Sent)
                    .SetProperty(d => d.SentAt, now)
                    .SetProperty(d => d.Attempts, d => d.Attempts + 1)
                    .SetProperty(d => d.ProviderMessageId, Truncate(result.ProviderMessageId, 200))
                    .SetProperty(d => d.LastError, (string?)null)
                    .SetProperty(d => d.LockedUntil, (DateTime?)null), CancellationToken.None);
                return Outcome.Sent;

            case ChannelSendStatus.Skipped:
                await mine.ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, DeliveryStatus.Skipped)
                    .SetProperty(d => d.LastError, Truncate(result.Error ?? "Skipped", 1000)!)
                    .SetProperty(d => d.LockedUntil, (DateTime?)null), CancellationToken.None);
                return Outcome.Skipped;

            default:
                var attempts = delivery.Attempts + 1;
                var delay = RetryDelay(attempts);
                var error = Truncate(result.Error ?? "Unknown error", 1000)!;
                if (delay is { } d1)
                {
                    await mine.ExecuteUpdateAsync(s => s
                        .SetProperty(d => d.Status, DeliveryStatus.Pending)
                        .SetProperty(d => d.Attempts, attempts)
                        .SetProperty(d => d.NextAttemptAt, now.Add(d1))
                        .SetProperty(d => d.LastError, error)
                        .SetProperty(d => d.LockedUntil, (DateTime?)null), CancellationToken.None);
                    return Outcome.Retrying;
                }
                await mine.ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, DeliveryStatus.Failed)
                    .SetProperty(d => d.Attempts, attempts)
                    .SetProperty(d => d.LastError, error)
                    .SetProperty(d => d.LockedUntil, (DateTime?)null), CancellationToken.None);
                return Outcome.Failed;
        }
    }

    /// <summary>Deliveries left in Sending well past their lock (crash mid-send) are failed for manual review, never auto re-sent.</summary>
    private async Task<int> RecoverInterruptedAsync(DateTime now, CancellationToken ct)
    {
        var staleBefore = now.AddMinutes(-5);
        return await db.Set<NotificationDelivery>()
            .Where(d => d.Status == DeliveryStatus.Sending && d.LockedUntil != null && d.LockedUntil < staleBefore)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, DeliveryStatus.Failed)
                .SetProperty(d => d.Attempts, d => d.Attempts + 1)
                .SetProperty(d => d.LastError,
                    "The send was interrupted before its outcome was recorded. Not retried automatically to avoid a duplicate message; retry manually if it was not received.")
                .SetProperty(d => d.LockedUntil, (DateTime?)null), ct);
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
