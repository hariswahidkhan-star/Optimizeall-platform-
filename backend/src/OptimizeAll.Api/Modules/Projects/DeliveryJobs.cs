using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Projects;

/// <summary>
/// Generates this month's task for every active recurring rule of an active project once its day of month has come.
/// Idempotent: the task's RecurrenceKey "{ruleId}:{yyyy-MM}" is unique, so re-runs and concurrent runs create it once.
/// </summary>
public sealed class RecurringTaskJob(AppDbContext db, IDatabaseDialect dialect, INotificationService notifications, TimeProvider clock) : IJob
{
    public string Name => nameof(RecurringTaskJob);

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var month = $"{today:yyyy-MM}";
        var rules = await (from r in db.Set<RecurringTaskRule>().AsNoTracking()
                           join p in db.Set<Project>() on r.ProjectId equals p.Id
                           where r.IsActive && p.Status == ProjectStatus.Active && r.DayOfMonth <= today.Day
                           select new { Rule = r, ProjectName = p.Name }).ToListAsync(ct);
        var created = 0;
        foreach (var item in rules)
        {
            var rule = item.Rule;
            var key = $"{rule.Id}:{month}";
            if (await db.Set<ProjectTask>().AnyAsync(t => t.RecurrenceKey == key, ct)) continue;
            var dueBase = new DateOnly(today.Year, today.Month, rule.DayOfMonth);
            var sort = (await db.Set<ProjectTask>().Where(t => t.ProjectId == rule.ProjectId && t.Status == ProjectTaskStatus.Todo)
                .MaxAsync(t => (double?)t.SortOrder, ct) ?? 0) + 1000;
            var task = new ProjectTask
            {
                ProjectId = rule.ProjectId, ClientAccountId = rule.ClientAccountId, Title = $"{rule.Title} — {dueBase:MMMM yyyy}",
                Description = rule.Description, DueDate = dueBase.AddDays(rule.DueInDays), EstimateHours = rule.EstimateHours,
                Labels = rule.Labels.Append("recurring").Distinct().ToList(), ClientVisible = rule.ClientVisible, SortOrder = sort,
                RecurrenceKey = key,
            };
            db.Set<ProjectTask>().Add(task);
            if (rule.AssigneeUserId is { } a)
            {
                db.Set<TaskAssignee>().Add(new TaskAssignee { TaskId = task.Id, UserId = a });
                db.Set<TaskWatcher>().Add(new TaskWatcher { TaskId = task.Id, UserId = a });
                await notifications.StageAsync(new NotificationRequest(a, DeliveryNotificationTypes.TaskAssigned, $"New task: {task.Title}",
                    $"Recurring task on {item.ProjectName}, due {task.DueDate:yyyy-MM-dd}.", DeliveryLinks.AgencyTask(task.ProjectId, task.Id)), ct);
            }
            try
            {
                await db.SaveChangesAsync(ct);
                created++;
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                db.ChangeTracker.Clear();
            }
        }
        return $"{created} recurring task(s) created for {month}";
    }
}

/// <summary>Creates last month's report drafts per active retainer client (idempotent per client and month).</summary>
public sealed class MonthlyReportDraftJob(ReportService reports) : IJob
{
    public string Name => nameof(MonthlyReportDraftJob);

    public async Task<string> ExecuteAsync(CancellationToken ct) => $"{await reports.GenerateMonthlyDraftsAsync(ct)} report draft(s) created";
}

/// <summary>
/// Client-feedback SLA: reminds the client's approvers when a deliverable review is due within 24 hours and again when it
/// is overdue (each reminder once per version, via dispatch keys), and — only for clients that enabled it — auto-approves
/// deliverables waiting longer than their auto-approve window (audited).
/// </summary>
public sealed class DeliverableSlaJob(
    AppDbContext db,
    DeliverableService deliverables,
    DeliveryLookup lookup,
    INotificationService notifications,
    TimeProvider clock) : IJob
{
    public string Name => nameof(DeliverableSlaJob);

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var soon = now.AddHours(24);
        var waiting = await (from d in db.Set<Deliverable>().AsNoTracking()
                             join c in db.Set<ClientAccount>() on d.ClientAccountId equals c.Id
                             where d.Status == DeliverableStatus.ClientReview
                             select new { d.Id, d.Title, d.ClientAccountId, d.CurrentVersion, d.ClientDueAt, d.SentToClientAt, c.AutoApproveAfterDays })
            .ToListAsync(ct);
        int reminders = 0, autoApproved = 0;
        foreach (var d in waiting)
        {
            if (d.AutoApproveAfterDays is { } days && d.SentToClientAt is { } sent && sent.AddDays(days) <= now)
            {
                if (await deliverables.AutoApproveAsync(d.Id, d.CurrentVersion, days, ct)) autoApproved++;
                continue;
            }
            if (d.ClientDueAt is not { } due) continue;
            var kind = due <= now ? "overdue" : due <= soon ? "due-soon" : null;
            if (kind is null) continue;
            if (!await lookup.TryClaimAsync($"deliverable-sla:{d.Id}:v{d.CurrentVersion}:{kind}", ct)) continue;
            var approvers = await lookup.ClientUsersAsync(d.ClientAccountId, ct, ClientMemberRole.Approver);
            foreach (var u in approvers)
                await notifications.StageAsync(new NotificationRequest(u, DeliveryNotificationTypes.DeliverableReminder,
                    kind == "overdue" ? $"Feedback overdue: {d.Title}" : $"Feedback due tomorrow: {d.Title}",
                    kind == "overdue"
                        ? "Your team is waiting for your review to keep the schedule on track."
                        : $"Please approve or request changes by {due:ddd d MMM HH:mm} UTC.",
                    DeliveryLinks.ClientDeliverable(d.ClientAccountId, d.Id), new[] { NotificationChannel.Email }), ct);
            if (kind == "overdue")
                foreach (var u in await lookup.ClientTeamAsync(d.ClientAccountId, ct))
                    await notifications.StageAsync(new NotificationRequest(u, DeliveryNotificationTypes.DeliverableReminder,
                        $"Client feedback overdue: {d.Title}", "The client's review deadline has passed.", DeliveryLinks.AgencyDeliverable(d.Id)), ct);
            await db.SaveChangesAsync(ct);
            reminders++;
        }
        return $"{reminders} reminder(s), {autoApproved} auto-approval(s)";
    }
}
