using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Review;

/// <summary>
/// Hourly: when approved posts are due for their "still live?" check, stages at most one in-app reminder per reviewer
/// per UTC day (deduplicated against existing ReviewLiveCheckDue notifications created today).
/// </summary>
public sealed class LiveCheckReminderJob(AppDbContext db, INotificationService notifications, TimeProvider clock) : IJob
{
    public string Name => "review-live-check-reminder";

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.Set<Submission>().CountAsync(s =>
            s.Status == SubmissionStatus.Approved && s.LiveCheckStatus == LiveCheckStatus.Pending && s.LiveCheckDueAt <= now, ct);
        if (due == 0) return "no live checks due";

        var roles = ReviewRoles.Reviewers.ToList();
        var reviewers = await db.Set<User>().Where(u => u.Status == UserStatus.Active && u.Roles.Any(r => roles.Contains(r.Role)))
            .Select(u => u.Id).ToListAsync(ct);
        var dayStart = now.Date;
        var alreadyNotified = await db.Set<Notification>()
            .Where(n => n.Type == NotificationTypes.ReviewLiveCheckDue && n.CreatedAt >= dayStart && reviewers.Contains(n.UserId))
            .Select(n => n.UserId).Distinct().ToListAsync(ct);

        var staged = 0;
        foreach (var reviewer in reviewers.Except(alreadyNotified))
        {
            await notifications.StageAsync(new NotificationRequest(reviewer, NotificationTypes.ReviewLiveCheckDue,
                "Live checks due",
                $"{due} approved post{(due == 1 ? " is" : "s are")} due for a check that {(due == 1 ? "it is" : "they are")} still live.",
                "/review/live-checks"), ct);
            staged++;
        }
        await db.SaveChangesAsync(ct);
        return $"{due} due, {staged} reviewer(s) notified";
    }
}
