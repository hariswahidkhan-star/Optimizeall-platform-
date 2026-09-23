using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Notifications;

/// <summary>A notification to stage. <c>Channels</c> are external channels attempted in addition to the in-app notification center.</summary>
public sealed record NotificationRequest(
    Guid UserId,
    string Type,
    string Title,
    string Body,
    string? LinkUrl = null,
    NotificationChannel[]? Channels = null);

public interface INotificationService
{
    /// <summary>
    /// Stages an in-app notification plus outbox rows for each requested external channel the user has not
    /// muted. Persisted by the caller's SaveChanges (same transaction), then delivered by the dispatch job.
    /// </summary>
    Task<Notification> StageAsync(NotificationRequest request, CancellationToken ct = default);
}

public sealed class NotificationService(AppDbContext db, TimeProvider clock) : INotificationService
{
    public async Task<Notification> StageAsync(NotificationRequest request, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var notification = new Notification
        {
            UserId = request.UserId,
            Type = request.Type,
            Title = Truncate(request.Title, 200),
            Body = Truncate(request.Body, 2000),
            LinkUrl = request.LinkUrl,
            CreatedAt = now,
        };
        db.Set<Notification>().Add(notification);

        var channels = (request.Channels ?? Array.Empty<NotificationChannel>())
            .Where(c => c != NotificationChannel.InApp).Distinct().ToArray();
        if (channels.Length == 0) return notification;

        var essential = NotificationTypes.Essential.Contains(request.Type);
        var muted = essential
            ? new HashSet<NotificationChannel>()
            : (await db.Set<NotificationPreference>().AsNoTracking()
                .Where(p => p.UserId == request.UserId && p.Type == request.Type && !p.Enabled)
                .Select(p => p.Channel).ToListAsync(ct)).ToHashSet();

        var user = await db.Set<User>().AsNoTracking()
            .Where(u => u.Id == request.UserId)
            .Select(u => new { u.WhatsAppOptIn, u.MarketingEmailOptIn })
            .FirstOrDefaultAsync(ct);
        if (user is null) return notification;

        var isMarketing = NotificationTypes.Marketing.Contains(request.Type);
        foreach (var channel in channels)
        {
            if (muted.Contains(channel)) continue;
            if (channel == NotificationChannel.WhatsApp && !user.WhatsAppOptIn) continue;
            if (isMarketing && channel == NotificationChannel.Email && !user.MarketingEmailOptIn) continue;

            db.Set<NotificationDelivery>().Add(new NotificationDelivery
            {
                NotificationId = notification.Id,
                UserId = request.UserId,
                Channel = channel,
                Status = DeliveryStatus.Pending,
                NextAttemptAt = now,
                CreatedAt = now,
            });
        }
        return notification;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
