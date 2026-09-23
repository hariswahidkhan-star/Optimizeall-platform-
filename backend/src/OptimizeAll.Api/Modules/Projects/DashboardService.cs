using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Clients;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Projects;

public sealed record TaskCountsDto(int Open, int Overdue, int DueToday);

public sealed record ClientOverdueDto(Guid ClientId, string ClientName, int OverdueTasks, int OverdueApprovals);

public sealed record AccountManagerPanelDto(IReadOnlyList<ClientHealthDto> HealthBoard, IReadOnlyList<ClientOverdueDto> OverdueByClient, UtilizationDto Utilization);

public sealed record AdminSnapshotDto(int ActiveClients, int OnboardingClients, int ActiveProjects, IReadOnlyList<ProjectSummaryDto> ProjectsAtRisk,
    int MinutesThisWeek, int BillableMinutesThisWeek, int DeliverablesAwaitingClients);

/// <summary>The agency home dashboard. Sections for account managers (clients.manage) and admins (settings.manage) are null otherwise.</summary>
public sealed record AgencyDashboardDto(
    IReadOnlyList<TaskSummaryDto> MyTasks, TaskCountsDto MyTaskCounts, IReadOnlyList<DeliverableSummaryDto> ReviewQueue,
    IReadOnlyList<DeliverableSummaryDto> PendingClientApprovals, IReadOnlyList<MeetingDto> TodaysMeetings, TimeEntryDto? Timer,
    int MyMinutesThisWeek, AccountManagerPanelDto? AccountManager, AdminSnapshotDto? Admin);

public sealed record ClientProjectSummaryDto(
    Guid Id, string Name, ProjectType Type, ProjectStatus Status, DateOnly? StartDate, DateOnly? EndDate, int VisibleTasks, int DoneTasks,
    int ProgressPercent, MilestoneDto? NextMilestone);

public sealed record ClientTaskDto(Guid Id, string Title, ProjectTaskStatus Status, DateOnly? DueDate, Guid? MilestoneId, DateTime? CompletedAt);

public sealed record ClientProjectDetailDto(ClientProjectSummaryDto Project, string? Description, IReadOnlyList<MilestoneDto> Milestones, IReadOnlyList<ClientTaskDto> Tasks);

public sealed record ClientHomeDto(
    MyOrganizationDto Organization, OnboardingDto Onboarding, IReadOnlyList<DeliverableSummaryDto> AwaitingApproval,
    IReadOnlyList<DeliverableSummaryDto> RecentDeliverables, ReportSummaryDto? LatestReport, IReadOnlyList<MeetingDto> UpcomingMeetings,
    IReadOnlyList<ThreadSummaryDto> Threads, IReadOnlyList<AccountTeamMemberDto> Team, IReadOnlyList<ClientProjectSummaryDto> Projects,
    NpsStatusDto Nps, bool CanApprove);

public sealed class DashboardService(
    AppDbContext db,
    ICurrentUser currentUser,
    TaskService tasks,
    DeliverableService deliverables,
    TimeService time,
    ProjectService projects,
    CommunicationService communication,
    ClientHealthService health,
    TimeProvider clock)
{
    public async Task<AgencyDashboardDto> AgencyAsync(CancellationToken ct)
    {
        var me = currentUser.Id;
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var week = BudgetMath.WeekStart(today);

        var mine = await tasks.MyTasksAsync(new MyTasksQuery { Filter = "open" }, ct);
        var counts = new TaskCountsDto(mine.Count, mine.Count(t => t.IsOverdue), mine.Count(t => t.DueDate is { } d && d <= today));
        var upcoming = mine.Where(t => t.DueDate is { } d && d <= today.AddDays(7)).Take(10).ToList();

        var review = (await deliverables.ListAsync(new DeliverableListQuery { View = "review", PageSize = 10 }, ct)).Items;

        // Clients I work with: account manager, account team or project member (admins: all).
        var isAdmin = currentUser.HasPermission(Permissions.SettingsManage);
        var myClients = isAdmin ? null : await MyClientIdsAsync(me, ct);
        var pendingQuery = db.Set<Deliverable>().AsNoTracking().Where(d => d.Status == DeliverableStatus.ClientReview);
        if (myClients is not null) pendingQuery = pendingQuery.Where(d => myClients.Contains(d.ClientAccountId));
        var pending = await deliverables.SummariesAsync(await pendingQuery.OrderBy(d => d.ClientDueAt).Take(10).ToListAsync(ct), ct);

        var dayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var meetingsQuery = db.Set<Meeting>().AsNoTracking()
            .Where(m => m.StartsAt >= dayStart && m.StartsAt < dayStart.AddDays(1) && m.Status != MeetingStatus.Cancelled);
        var meetingRows = await meetingsQuery.OrderBy(m => m.StartsAt).Take(50).ToListAsync(ct);
        meetingRows = meetingRows.Where(m => isAdmin || m.AttendeeUserIds.Contains(me) || m.CreatedByUserId == me ||
                                             (myClients?.Contains(m.ClientAccountId) ?? true)).Take(10).ToList();
        var meetings = await communication.MeetingDtosAsync(meetingRows, ct);

        var timer = await time.RunningAsync(ct);
        var myWeekEnd = week.AddDays(6);
        var weekMinutes = await db.Set<TimeEntry>().AsNoTracking().Where(e => e.UserId == me && e.Date >= week && e.Date <= myWeekEnd)
            .SumAsync(e => (int?)e.Minutes, ct) ?? 0;

        AccountManagerPanelDto? am = null;
        if (currentUser.HasPermission(Permissions.ClientsManage))
        {
            var board = await health.BoardAsync(isAdmin ? null : me, ct);
            if (!isAdmin && board.Count == 0) board = await health.BoardAsync(null, ct);
            var ids = board.Select(b => b.ClientId).ToList();
            var overdueApprovals = await db.Set<Deliverable>().AsNoTracking()
                .Where(d => ids.Contains(d.ClientAccountId) && d.Status == DeliverableStatus.ClientReview && d.ClientDueAt < now)
                .GroupBy(d => d.ClientAccountId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var overdue = board.Select(b => new ClientOverdueDto(b.ClientId, b.ClientName, b.OverdueTasks, overdueApprovals.GetValueOrDefault(b.ClientId)))
                .Where(o => o.OverdueTasks + o.OverdueApprovals > 0).OrderByDescending(o => o.OverdueTasks + o.OverdueApprovals).ToList();
            am = new AccountManagerPanelDto(board, overdue, await time.UtilizationAsync(week, week.AddDays(6), ct));
        }

        AdminSnapshotDto? admin = null;
        if (isAdmin)
        {
            var activeClients = await db.Set<ClientAccount>().CountAsync(c => c.Status == ClientAccountStatus.Active, ct);
            var onboarding = await db.Set<ClientAccount>().CountAsync(c => c.Status == ClientAccountStatus.Onboarding, ct);
            var activeProjects = await db.Set<Project>().AsNoTracking()
                .Where(p => p.Status == ProjectStatus.Active || p.Status == ProjectStatus.Planning).ToListAsync(ct);
            var summaries = await projects.SummariesAsync(activeProjects, ct);
            var weekEnd = week.AddDays(6);
            var weekEntries = db.Set<TimeEntry>().AsNoTracking().Where(e => e.Date >= week && e.Date <= weekEnd);
            var weekTotal = await weekEntries.SumAsync(e => (int?)e.Minutes, ct) ?? 0;
            var weekBillable = await weekEntries.Where(e => e.Billable).SumAsync(e => (int?)e.Minutes, ct) ?? 0;
            admin = new AdminSnapshotDto(activeClients, onboarding, activeProjects.Count,
                summaries.Where(s => s.AtRisk).OrderByDescending(s => s.OverdueTasks).Take(10).ToList(),
                weekTotal, weekBillable,
                await db.Set<Deliverable>().CountAsync(d => d.Status == DeliverableStatus.ClientReview, ct));
        }

        return new AgencyDashboardDto(upcoming, counts, review, pending, meetings, timer, weekMinutes, am, admin);
    }

    private async Task<List<Guid>> MyClientIdsAsync(Guid me, CancellationToken ct)
    {
        var managed = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.AccountManagerUserId == me).Select(c => c.Id).ToListAsync(ct);
        var team = await db.Set<ClientTeamAssignment>().AsNoTracking().Where(a => a.UserId == me).Select(a => a.ClientAccountId).ToListAsync(ct);
        var projectClients = await (from m in db.Set<ProjectMember>().AsNoTracking()
                                    join p in db.Set<Project>() on m.ProjectId equals p.Id
                                    where m.UserId == me
                                    select p.ClientAccountId).ToListAsync(ct);
        return managed.Concat(team).Concat(projectClients).Distinct().ToList();
    }
}

/// <summary>Client-portal read models: home, projects (client-visible tasks only).</summary>
public sealed class ClientPortalService(
    AppDbContext db,
    IClientScope scope,
    ClientService clients,
    ClientRelationshipService relationship,
    DeliverableService deliverables,
    ReportService reports,
    CommunicationService communication,
    ProjectService projects,
    TimeProvider clock)
{
    public async Task<ClientHomeDto> HomeAsync(Guid clientId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var org = (await clients.MyOrganizationsAsync(ct)).FirstOrDefault(o => o.ClientId == clientId) ?? throw DomainException.NotFound("Client");
        var all = await deliverables.ClientListAsync(clientId, null, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var meetings = await communication.MeetingsAsync(new MeetingQuery { ClientId = clientId, From = now.AddHours(-2), To = now.AddDays(45) }, ct);
        return new ClientHomeDto(org, await relationship.OnboardingAsync(clientId, ct),
            all.Where(d => d.Status == DeliverableStatus.ClientReview).Take(10).ToList(),
            all.Where(d => d.Status != DeliverableStatus.ClientReview).Take(6).ToList(),
            (await reports.ClientListAsync(clientId, ct)).FirstOrDefault(),
            meetings.Where(m => m.Status == MeetingStatus.Scheduled).Take(5).ToList(),
            (await communication.ThreadsAsync(clientId, ct)).Take(5).ToList(),
            await clients.AccountTeamAsync(clientId, ct),
            await ProjectsAsync(clientId, ct),
            await relationship.NpsStatusAsync(clientId, ct),
            org.Role is ClientMemberRole.Owner or ClientMemberRole.Approver);
    }

    public async Task<IReadOnlyList<ClientProjectSummaryDto>> ProjectsAsync(Guid clientId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var rows = await db.Set<Project>().AsNoTracking()
            .Where(p => p.ClientAccountId == clientId && p.Status != ProjectStatus.Cancelled)
            .OrderBy(p => p.Status).ThenByDescending(p => p.StartDate).ToListAsync(ct);
        var result = new List<ClientProjectSummaryDto>();
        foreach (var p in rows) result.Add(await SummaryAsync(p, ct));
        return result;
    }

    private async Task<ClientProjectSummaryDto> SummaryAsync(Project p, CancellationToken ct)
    {
        var visible = db.Set<ProjectTask>().AsNoTracking().Where(t => t.ProjectId == p.Id && t.ClientVisible);
        var total = await visible.CountAsync(ct);
        var done = await visible.CountAsync(t => t.Status == ProjectTaskStatus.Done, ct);
        var milestones = await projects.MilestonesAsync(p.Id, ct, clientVisibleOnly: true);
        return new ClientProjectSummaryDto(p.Id, p.Name, p.Type, p.Status, p.StartDate, p.EndDate, total, done,
            total == 0 ? (p.Status == ProjectStatus.Completed ? 100 : 0) : (int)Math.Round(done * 100.0 / total),
            milestones.Where(m => m.Status == MilestoneStatus.Open).OrderBy(m => m.DueDate).FirstOrDefault());
    }

    public async Task<ClientProjectDetailDto> ProjectAsync(Guid clientId, Guid projectId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var p = await db.Set<Project>().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == projectId && x.ClientAccountId == clientId && x.Status != ProjectStatus.Cancelled, ct)
                ?? throw DomainException.NotFound("Project");
        var tasks = await db.Set<ProjectTask>().AsNoTracking().Where(t => t.ProjectId == projectId && t.ClientVisible)
            .OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenBy(t => t.SortOrder)
            .Select(t => new ClientTaskDto(t.Id, t.Title, t.Status, t.DueDate, t.MilestoneId, t.CompletedAt)).ToListAsync(ct);
        return new ClientProjectDetailDto(await SummaryAsync(p, ct), p.Description, await projects.MilestonesAsync(projectId, ct, clientVisibleOnly: true), tasks);
    }
}
