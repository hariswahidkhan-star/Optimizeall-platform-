using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Projects;

/// <summary>
/// Time tracking: one running timer per user (unique index on RunningUserId), manual entries, weekly timesheets
/// (submit → approve/reject by projects.manage; entries of submitted/approved weeks are locked), utilization and CSV export.
/// </summary>
public sealed class TimeService(
    AppDbContext db,
    IClientScope scope,
    ICurrentUser currentUser,
    IAuditLogger audit,
    INotificationService notifications,
    IDatabaseDialect dialect,
    TimeProvider clock)
{
    /// <summary>Capacity used for utilization: 8 hours per weekday.</summary>
    public const int DailyCapacityMinutes = 8 * 60;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<DateOnly> LocalTodayAsync(CancellationToken ct)
    {
        var tzId = await db.Set<User>().AsNoTracking().Where(u => u.Id == currentUser.Id).Select(u => u.TimeZone).FirstOrDefaultAsync(ct);
        var tz = TimeZoneInfo.TryFindSystemTimeZoneById(tzId ?? "UTC", out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, tz));
    }

    // ------------------------------------------------------------------ timer

    public async Task<TimeEntryDto?> RunningAsync(CancellationToken ct)
    {
        var me = currentUser.Id;
        var entry = await db.Set<TimeEntry>().AsNoTracking().FirstOrDefaultAsync(e => e.RunningUserId == me, ct);
        return entry is null ? null : (await ToDtosAsync(new List<TimeEntry> { entry }, ct)).Single();
    }

    /// <summary>Starts a timer. A second running timer is rejected (409) — also under a race, by the unique index.</summary>
    public async Task<TimeEntryDto> StartAsync(StartTimerRequest r, CancellationToken ct)
    {
        var me = currentUser.Id;
        var (project, taskId) = await ValidateTargetAsync(r.ProjectId!.Value, r.TaskId, ct);
        await using var userLock = await LockUserTimeAsync(me, ct);
        if (await db.Set<TimeEntry>().AnyAsync(e => e.RunningUserId == me, ct))
            throw DomainException.Conflict("time.timer_running", "You already have a timer running. Stop it before starting another.");
        var entry = new TimeEntry
        {
            UserId = me, ClientAccountId = project.ClientAccountId, ProjectId = project.Id, TaskId = taskId,
            Date = await LocalTodayAsync(ct), Billable = r.Billable, Note = r.Note?.Trim(), StartedAt = Now, RunningUserId = me,
        };
        await EnsureWeekOpenAsync(me, entry.Date, ct);
        db.Set<TimeEntry>().Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("time.timer_running", "You already have a timer running. Stop it before starting another.");
        }
        return (await ToDtosAsync(new List<TimeEntry> { entry }, ct)).Single();
    }

    /// <summary>Stops my running timer (conditional: a double stop cannot double-count).</summary>
    public async Task<TimeEntryDto> StopAsync(CancellationToken ct)
    {
        var me = currentUser.Id;
        var entry = await db.Set<TimeEntry>().FirstOrDefaultAsync(e => e.RunningUserId == me, ct)
                    ?? throw DomainException.Conflict("time.no_timer", "No timer is running.");
        var now = Now;
        var minutes = Math.Max(1, (int)Math.Round((now - entry.StartedAt!.Value).TotalMinutes));
        minutes = Math.Min(minutes, 24 * 60);
        var updated = await db.Set<TimeEntry>().Where(e => e.Id == entry.Id && e.RunningUserId == me)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.RunningUserId, (Guid?)null).SetProperty(e => e.EndedAt, now)
                .SetProperty(e => e.Minutes, minutes).SetProperty(e => e.UpdatedAt, now).SetProperty(e => e.ConcurrencyStamp, Guid.NewGuid()), ct);
        if (updated == 0) throw DomainException.Conflict("time.no_timer", "No timer is running.");
        db.ChangeTracker.Clear();
        var stopped = await db.Set<TimeEntry>().AsNoTracking().FirstAsync(e => e.Id == entry.Id, ct);
        return (await ToDtosAsync(new List<TimeEntry> { stopped }, ct)).Single();
    }

    // ------------------------------------------------------------------ entries

    public async Task<IReadOnlyList<TimeEntryDto>> ListAsync(TimeEntryQuery q, CancellationToken ct)
    {
        var userId = q.UserId ?? currentUser.Id;
        if (userId != currentUser.Id || q.ProjectId is not null || q.ClientId is not null)
        {
            if (!currentUser.HasPermission(Permissions.TimeViewAll) && userId != currentUser.Id)
                throw DomainException.Forbidden("time.view_all_required", "You can only see your own time.");
        }
        var query = await scope.ApplyAsync(db.Set<TimeEntry>().AsNoTracking(), e => e.ClientAccountId, ct);
        var seeAll = currentUser.HasPermission(Permissions.TimeViewAll);
        if (q.UserId is not null || !seeAll || (q.ProjectId is null && q.ClientId is null)) query = query.Where(e => e.UserId == userId);
        if (q.ProjectId is { } pid) query = query.Where(e => e.ProjectId == pid);
        if (q.ClientId is { } cid) query = query.Where(e => e.ClientAccountId == cid);
        var from = q.From ?? BudgetMath.WeekStart(await LocalTodayAsync(ct));
        var to = q.To ?? from.AddDays(6);
        if (to.DayNumber - from.DayNumber > 366) throw DeliveryRules.Invalid("time.range_too_large", "to", "Choose at most one year.");
        query = query.Where(e => e.Date >= from && e.Date <= to);
        var rows = await query.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).Take(2000).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<TimeEntryDto> CreateAsync(TimeEntryRequest r, CancellationToken ct)
    {
        var me = currentUser.Id;
        var (project, taskId) = await ValidateTargetAsync(r.ProjectId!.Value, r.TaskId, ct);
        var date = r.Date!.Value;
        ValidateDate(date);
        await using var userLock = await LockUserTimeAsync(me, ct);
        await EnsureWeekOpenAsync(me, date, ct);
        var entry = new TimeEntry
        {
            UserId = me, ClientAccountId = project.ClientAccountId, ProjectId = project.Id, TaskId = taskId, Date = date,
            Minutes = r.Minutes!.Value, Billable = r.Billable, Note = r.Note?.Trim(),
        };
        db.Set<TimeEntry>().Add(entry);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync(new List<TimeEntry> { entry }, ct)).Single();
    }

    public async Task<TimeEntryDto> UpdateAsync(Guid id, TimeEntryRequest r, CancellationToken ct)
    {
        var me = currentUser.Id;
        await using var userLock = await LockUserTimeAsync(me, ct);
        var entry = await db.Set<TimeEntry>().FirstOrDefaultAsync(e => e.Id == id && e.UserId == me, ct) ?? throw DomainException.NotFound("TimeEntry");
        if (entry.IsRunning) throw DomainException.Conflict("time.timer_running", "Stop the timer before editing this entry.");
        DeliveryRules.EnsureStamp(entry, r.ConcurrencyStamp ?? Guid.Empty, db);
        await EnsureWeekOpenAsync(me, entry.Date, ct);
        var (project, taskId) = await ValidateTargetAsync(r.ProjectId!.Value, r.TaskId, ct);
        ValidateDate(r.Date!.Value);
        await EnsureWeekOpenAsync(me, r.Date.Value, ct);
        entry.ProjectId = project.Id;
        entry.ClientAccountId = project.ClientAccountId;
        entry.TaskId = taskId;
        entry.Date = r.Date.Value;
        entry.Minutes = r.Minutes!.Value;
        entry.Billable = r.Billable;
        entry.Note = r.Note?.Trim();
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync(new List<TimeEntry> { entry }, ct)).Single();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var me = currentUser.Id;
        await using var userLock = await LockUserTimeAsync(me, ct);
        var entry = await db.Set<TimeEntry>().FirstOrDefaultAsync(e => e.Id == id && e.UserId == me, ct) ?? throw DomainException.NotFound("TimeEntry");
        await EnsureWeekOpenAsync(me, entry.Date, ct);
        db.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    private void ValidateDate(DateOnly date)
    {
        var today = DateOnly.FromDateTime(Now);
        if (date > today.AddDays(1) || date < today.AddDays(-400))
            throw DeliveryRules.Invalid("time.invalid_date", "date", "Log time for today or a past date.");
    }

    private async Task<(Project Project, Guid? TaskId)> ValidateTargetAsync(Guid projectId, Guid? taskId, CancellationToken ct)
    {
        var project = await (await scope.ApplyAsync(db.Set<Project>().AsNoTracking(), p => p.ClientAccountId, ct))
                          .FirstOrDefaultAsync(p => p.Id == projectId, ct)
                      ?? throw DeliveryRules.Invalid("time.invalid_project", "projectId", "Choose a project.");
        if (project.Status is ProjectStatus.Cancelled)
            throw DeliveryRules.Invalid("time.project_closed", "projectId", "This project is cancelled.");
        if (taskId is { } t && !await db.Set<ProjectTask>().AnyAsync(x => x.Id == t && x.ProjectId == projectId, ct))
            throw DeliveryRules.Invalid("time.invalid_task", "taskId", "That task isn't part of the project.");
        return (project, taskId);
    }

    private async Task EnsureWeekOpenAsync(Guid userId, DateOnly date, CancellationToken ct)
    {
        var week = BudgetMath.WeekStart(date);
        if (await db.Set<Timesheet>().AnyAsync(t => t.UserId == userId && t.WeekStart == week &&
                                                   (t.Status == TimesheetStatus.Submitted || t.Status == TimesheetStatus.Approved), ct))
            throw DomainException.Conflict("time.week_locked", "That week's timesheet is submitted or approved; its entries are locked.");
    }

    /// <summary>
    /// Serializes one user's time writes (entries, timer start, week submission): the week-lock check and the write it guards
    /// must not interleave with a concurrent submit, or an entry could land in a week that was just submitted.
    /// </summary>
    private Task<IAsyncDisposable> LockUserTimeAsync(Guid userId, CancellationToken ct) =>
        dialect.AcquireNamedLockAsync(db, $"time:{userId:N}", TimeSpan.FromSeconds(15), ct);

    internal async Task<List<TimeEntryDto>> ToDtosAsync(List<TimeEntry> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return new List<TimeEntryDto>();
        var projectIds = rows.Select(r => r.ProjectId).Distinct().ToList();
        var projects = await db.Set<Project>().AsNoTracking().Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var clientIds = rows.Select(r => r.ClientAccountId).Distinct().ToList();
        var clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var taskIds = rows.Where(r => r.TaskId != null).Select(r => r.TaskId!.Value).Distinct().ToList();
        var tasks = await db.Set<ProjectTask>().AsNoTracking().Where(t => taskIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Title, ct);
        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var minWeek = rows.Min(r => BudgetMath.WeekStart(r.Date));
        var maxWeek = rows.Max(r => BudgetMath.WeekStart(r.Date));
        var locked = (await db.Set<Timesheet>().AsNoTracking()
                .Where(t => userIds.Contains(t.UserId) && t.WeekStart >= minWeek && t.WeekStart <= maxWeek &&
                            (t.Status == TimesheetStatus.Submitted || t.Status == TimesheetStatus.Approved))
                .Select(t => new { t.UserId, t.WeekStart }).ToListAsync(ct))
            .Select(x => (x.UserId, x.WeekStart)).ToHashSet();
        return rows.Select(e => new TimeEntryDto(e.Id, e.UserId, users.GetValueOrDefault(e.UserId) ?? "", e.ClientAccountId,
            clients.GetValueOrDefault(e.ClientAccountId) ?? "", e.ProjectId, projects.GetValueOrDefault(e.ProjectId) ?? "", e.TaskId,
            e.TaskId is { } t && tasks.TryGetValue(t, out var title) ? title : null, e.Date, e.Minutes, e.Billable, e.Note, e.StartedAt,
            e.IsRunning, locked.Contains((e.UserId, BudgetMath.WeekStart(e.Date))), e.ConcurrencyStamp)).ToList();
    }

    // ------------------------------------------------------------------ timesheets

    public async Task<TimesheetDto> WeekAsync(Guid? userId, DateOnly anyDay, CancellationToken ct)
    {
        var uid = userId ?? currentUser.Id;
        if (uid != currentUser.Id && !currentUser.HasPermission(Permissions.TimeViewAll) && !currentUser.HasPermission(Permissions.ProjectsManage))
            throw DomainException.Forbidden("time.view_all_required", "You can only see your own timesheet.");
        var week = BudgetMath.WeekStart(anyDay);
        var end = week.AddDays(6);
        var sheet = await db.Set<Timesheet>().AsNoTracking().FirstOrDefaultAsync(t => t.UserId == uid && t.WeekStart == week, ct);
        var entries = await db.Set<TimeEntry>().AsNoTracking().Where(e => e.UserId == uid && e.Date >= week && e.Date <= end)
            .OrderBy(e => e.Date).ThenBy(e => e.CreatedAt).ToListAsync(ct);
        var name = await db.Set<User>().AsNoTracking().Where(u => u.Id == uid).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "";
        var decidedBy = sheet?.DecidedByUserId is { } d
            ? await db.Set<User>().AsNoTracking().Where(u => u.Id == d).Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
            : null;
        var days = Enumerable.Range(0, 7).Select(i => week.AddDays(i))
            .Select(day => new TimesheetDayDto(day, entries.Where(e => e.Date == day).Sum(e => e.Minutes))).ToList();
        return new TimesheetDto(sheet?.Id, uid, name, week, sheet?.Status ?? TimesheetStatus.Open, entries.Sum(e => e.Minutes),
            entries.Where(e => e.Billable).Sum(e => e.Minutes), days, await ToDtosAsync(entries, ct), sheet?.SubmittedAt, sheet?.DecidedAt,
            decidedBy, sheet?.DecisionComment, sheet?.ConcurrencyStamp);
    }

    public async Task<TimesheetDto> SubmitAsync(DateOnly anyDay, CancellationToken ct)
    {
        var me = currentUser.Id;
        var week = BudgetMath.WeekStart(anyDay);
        var end = week.AddDays(6);
        await using var userLock = await LockUserTimeAsync(me, ct);
        if (await db.Set<TimeEntry>().AnyAsync(e => e.RunningUserId == me && e.Date >= week && e.Date <= end, ct))
            throw DomainException.Conflict("time.timer_running", "Stop your running timer before submitting the week.");
        var total = await db.Set<TimeEntry>().Where(e => e.UserId == me && e.Date >= week && e.Date <= end).SumAsync(e => (int?)e.Minutes, ct) ?? 0;
        if (total == 0) throw DomainException.Conflict("time.empty_week", "There's no time logged in this week.");
        var sheet = await db.Set<Timesheet>().FirstOrDefaultAsync(t => t.UserId == me && t.WeekStart == week, ct);
        if (sheet is null)
        {
            sheet = new Timesheet { UserId = me, WeekStart = week };
            db.Set<Timesheet>().Add(sheet);
        }
        else if (sheet.Status is TimesheetStatus.Submitted or TimesheetStatus.Approved)
        {
            throw DomainException.Conflict("time.already_submitted", $"This week is already {sheet.Status.ToString().ToLowerInvariant()}.");
        }
        sheet.Status = TimesheetStatus.Submitted;
        sheet.SubmittedAt = Now;
        sheet.DecidedAt = null;
        sheet.DecidedByUserId = null;
        sheet.DecisionComment = null;
        sheet.TotalMinutes = total;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("time.already_submitted", "This week was submitted already.");
        }
        return await WeekAsync(me, week, ct);
    }

    public async Task<IReadOnlyList<TimesheetDto>> PendingAsync(CancellationToken ct)
    {
        var sheets = await db.Set<Timesheet>().AsNoTracking().Where(t => t.Status == TimesheetStatus.Submitted)
            .OrderBy(t => t.WeekStart).Take(100).ToListAsync(ct);
        var result = new List<TimesheetDto>();
        foreach (var s in sheets) result.Add(await WeekAsync(s.UserId, s.WeekStart, ct));
        return result;
    }

    /// <summary>Approve or reject a submitted week (projects.manage; not your own). Conditional on the stamp; audited.</summary>
    public async Task<TimesheetDto> DecideAsync(Guid sheetId, bool approve, TimesheetDecisionRequest r, CancellationToken ct)
    {
        var sheet = await db.Set<Timesheet>().FirstOrDefaultAsync(t => t.Id == sheetId, ct) ?? throw DomainException.NotFound("Timesheet");
        // Never your own, whatever your permissions (admins included): approval needs a second person.
        if (sheet.UserId == currentUser.Id)
            throw DomainException.Forbidden("time.own_timesheet", "Someone else must approve your timesheet.");
        DeliveryRules.EnsureStamp(sheet, r.ConcurrencyStamp, db);
        if (sheet.Status != TimesheetStatus.Submitted)
            throw DomainException.Conflict("time.not_submitted", "Only submitted timesheets can be approved or rejected.");
        if (!approve && string.IsNullOrWhiteSpace(r.Comment))
            throw DeliveryRules.Invalid("time.comment_required", "comment", "Say why the timesheet is rejected.");
        sheet.Status = approve ? TimesheetStatus.Approved : TimesheetStatus.Rejected;
        sheet.DecidedAt = Now;
        sheet.DecidedByUserId = currentUser.Id;
        sheet.DecisionComment = r.Comment?.Trim();
        audit.Record(approve ? "time.timesheet_approved" : "time.timesheet_rejected", nameof(Timesheet), sheet.Id,
            new { Status = TimesheetStatus.Submitted }, new { sheet.Status, sheet.UserId, sheet.WeekStart, sheet.TotalMinutes }, sheet.DecisionComment);
        await notifications.StageAsync(new NotificationRequest(sheet.UserId, DeliveryNotificationTypes.TimesheetDecision,
            approve ? $"Timesheet approved (week of {sheet.WeekStart:d MMM})" : $"Timesheet returned (week of {sheet.WeekStart:d MMM})",
            approve ? "Your timesheet was approved." : $"Please review and resubmit: {sheet.DecisionComment}", DeliveryLinks.AgencyTimesheets,
            approve ? null : new[] { NotificationChannel.Email }), ct);
        await db.SaveChangesAsync(ct);
        return await WeekAsync(sheet.UserId, sheet.WeekStart, ct);
    }

    // ------------------------------------------------------------------ reports

    public async Task<UtilizationDto> UtilizationAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366) throw DeliveryRules.Invalid("time.invalid_range", "to", "Choose a range of at most one year.");
        var entries = await db.Set<TimeEntry>().AsNoTracking().Where(e => e.Date >= from && e.Date <= to && e.RunningUserId == null)
            .GroupBy(e => e.UserId)
            .Select(g => new { UserId = g.Key, Total = g.Sum(e => e.Minutes), Billable = g.Sum(e => e.Billable ? e.Minutes : 0) })
            .ToListAsync(ct);
        var staffIds = await db.Set<UserRole>().AsNoTracking().Where(r => StaffDirectory.DeliveryRoles.Contains(r.Role) && r.Role != Role.SalesRep)
            .Select(r => r.UserId).Distinct().ToListAsync(ct);
        var ids = staffIds.Union(entries.Select(e => e.UserId)).ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id) && u.Status == UserStatus.Active)
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var weekdays = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) weekdays++;
        var capacity = weekdays * DailyCapacityMinutes;
        var rows = users.Select(u =>
        {
            var e = entries.FirstOrDefault(x => x.UserId == u.Key);
            var total = e?.Total ?? 0;
            var billable = e?.Billable ?? 0;
            return new UtilizationRowDto(u.Key, u.Value, total, billable, capacity,
                capacity == 0 ? 0 : Math.Round(total * 100m / capacity, 1), total == 0 ? 0 : Math.Round(billable * 100m / total, 1));
        }).OrderByDescending(r => r.UtilizationPercent).ThenBy(r => r.UserName).ToList();
        return new UtilizationDto(from, to, rows, rows.Sum(r => r.TotalMinutes), rows.Sum(r => r.BillableMinutes));
    }

    public async Task<FileContentResult> ExportCsvAsync(TimeEntryQuery q, CancellationToken ct)
    {
        var rows = await ListAsync(q, ct);
        return Csv.File($"time-{q.From:yyyyMMdd}-{q.To:yyyyMMdd}.csv",
            new[] { "Date", "User", "Client", "Project", "Task", "Hours", "Minutes", "Billable", "Note" },
            rows.OrderBy(r => r.Date).Select(r => new object?[]
            {
                r.Date, r.UserName, r.ClientName, r.ProjectName, r.TaskTitle, BudgetMath.Hours(r.Minutes), r.Minutes,
                r.Billable ? "yes" : "no", r.Note,
            }));
    }

    // ------------------------------------------------------------------ rates

    public async Task<IReadOnlyList<HourlyRateDto>> RatesAsync(CancellationToken ct)
    {
        var rates = await db.Set<HourlyRate>().AsNoTracking().OrderBy(r => r.UserId == null).ThenBy(r => r.Role).ToListAsync(ct);
        var ids = rates.Where(r => r.UserId != null).Select(r => r.UserId!.Value).ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return rates.Select(r => new HourlyRateDto(r.Id, r.UserId, r.UserId is { } u ? names.GetValueOrDefault(u) : null, r.Role, r.Rate, r.Currency)).ToList();
    }

    public async Task<IReadOnlyList<HourlyRateDto>> SaveRateAsync(HourlyRateRequest r, CancellationToken ct)
    {
        if ((r.UserId is null) == (r.Role is null))
            throw DeliveryRules.Invalid("time.rate_scope", "userId", "Set the rate for either one user or one role.");
        var currency = Money.Normalize(r.Currency);
        if (!Money.IsSupported(currency)) throw DeliveryRules.Invalid("time.invalid_currency", "currency", "Choose a supported currency.");
        if (r.UserId is { } uid && !(await StaffDirectory.ValidStaffAsync(db, new[] { uid }, ct)).Contains(uid))
            throw DeliveryRules.Invalid("time.invalid_user", "userId", "Choose an active staff member.");
        var existing = await db.Set<HourlyRate>().FirstOrDefaultAsync(x => x.UserId == r.UserId && x.Role == r.Role && x.Currency == currency, ct);
        var before = existing?.Rate;
        if (existing is null) db.Set<HourlyRate>().Add(existing = new HourlyRate { UserId = r.UserId, Role = r.Role, Currency = currency });
        existing.Rate = r.Rate!.Value;
        audit.Record("time.rate_saved", nameof(HourlyRate), existing.Id, new { Rate = before }, new { existing.UserId, existing.Role, existing.Rate, existing.Currency });
        await db.SaveChangesAsync(ct);
        return await RatesAsync(ct);
    }

    public async Task<IReadOnlyList<HourlyRateDto>> DeleteRateAsync(Guid id, CancellationToken ct)
    {
        var rate = await db.Set<HourlyRate>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("HourlyRate");
        db.Remove(rate);
        audit.Record("time.rate_deleted", nameof(HourlyRate), id, before: new { rate.UserId, rate.Role, rate.Rate, rate.Currency });
        await db.SaveChangesAsync(ct);
        return await RatesAsync(ct);
    }
}
