using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Crm;

/// <summary>
/// Every 5 minutes: sends due reminders (tasks and meetings) and tells assignees about overdue tasks — each at most once,
/// claimed with a conditional update (<c>ReminderSentAt</c>/<c>OverdueNotifiedAt</c> still null) so retries and parallel
/// instances never notify twice. Also marks unanswered proposals past their validity date as Expired.
/// </summary>
public sealed class CrmTaskReminderJob(
    AppDbContext db, IDatabaseDialect dialect, INotificationService notifications, IAuditLogger audit, TimeProvider clock) : IJob
{
    public string Name => "crm.task-reminders";

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var reminders = await db.Set<CrmActivity>().AsNoTracking()
            .Where(a => a.RemindAt != null && a.RemindAt <= now && a.ReminderSentAt == null && a.CompletedAt == null && a.AssigneeUserId != null)
            .OrderBy(a => a.RemindAt).Take(500).ToListAsync(ct);
        var sentReminders = 0;
        foreach (var a in reminders)
        {
            await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
            var claimed = await db.Set<CrmActivity>().Where(x => x.Id == a.Id && x.ReminderSentAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReminderSentAt, now), ct);
            if (claimed == 0) continue;
            var when = a.Type == ActivityType.Meeting ? a.OccursAt : a.DueAt;
            await notifications.StageAsync(new NotificationRequest(a.AssigneeUserId!.Value, BillingNotificationTypes.TaskReminder,
                $"Reminder: {a.Subject}", when is { } w ? $"{a.Type} scheduled for {w:yyyy-MM-dd HH:mm} UTC." : $"{a.Type} reminder.",
                a.DealId is { } d ? BillingLinks.AgencyDeal(d) : BillingLinks.AgencyTasks, new[] { NotificationChannel.Email }), ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            sentReminders++;
        }

        var overdue = await db.Set<CrmActivity>().AsNoTracking()
            .Where(a => a.Type == ActivityType.Task && a.CompletedAt == null && a.DueAt != null && a.DueAt < now && a.OverdueNotifiedAt == null &&
                        a.AssigneeUserId != null)
            .OrderBy(a => a.DueAt).Take(500).ToListAsync(ct);
        var sentOverdue = 0;
        foreach (var a in overdue)
        {
            await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
            var claimed = await db.Set<CrmActivity>().Where(x => x.Id == a.Id && x.OverdueNotifiedAt == null && x.CompletedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.OverdueNotifiedAt, now), ct);
            if (claimed == 0) continue;
            await notifications.StageAsync(new NotificationRequest(a.AssigneeUserId!.Value, BillingNotificationTypes.TaskOverdue,
                $"Overdue task: {a.Subject}", $"This task was due {a.DueAt:yyyy-MM-dd HH:mm} UTC.",
                a.DealId is { } d ? BillingLinks.AgencyDeal(d) : BillingLinks.AgencyTasks, new[] { NotificationChannel.Email }), ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            sentOverdue++;
        }

        var today = DateOnly.FromDateTime(now);
        var expiring = await (from p in db.Set<Proposal>().AsNoTracking()
                              join v in db.Set<ProposalVersion>().AsNoTracking() on new { p.Id, V = p.SentVersion ?? 0 } equals new { Id = v.ProposalId, V = v.VersionNumber }
                              where (p.Status == ProposalStatus.Sent || p.Status == ProposalStatus.Viewed) && v.ValidUntil < today
                              select p.Id).ToListAsync(ct);
        var expired = 0;
        foreach (var id in expiring)
        {
            if (await db.Set<Proposal>().Where(p => p.Id == id && (p.Status == ProposalStatus.Sent || p.Status == ProposalStatus.Viewed))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, ProposalStatus.Expired).SetProperty(p => p.ConcurrencyStamp, Guid.NewGuid())
                        .SetProperty(p => p.UpdatedAt, now), ct) == 1)
            {
                audit.RecordSystem("crm.proposal_expired", nameof(Proposal), id);
                expired++;
            }
        }
        if (expired > 0) await db.SaveChangesAsync(ct);
        return $"Sent {sentReminders} reminder(s) and {sentOverdue} overdue notice(s); expired {expired} proposal(s).";
    }
}
