using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Clients;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Projects;

/// <summary>Creates a template's milestones, tasks and recurring rules for a project (used by the API and the seeders).</summary>
public static class ProjectTemplateApplier
{
    public static List<ProjectTask> Apply(AppDbContext db, Project project, ProjectTemplate template, DateOnly start, Guid? createdBy)
    {
        var milestones = new Dictionary<string, Milestone>();
        var order = 0;
        foreach (var m in template.Milestones)
        {
            var milestone = new Milestone
            {
                ProjectId = project.Id, ClientAccountId = project.ClientAccountId, Title = m.Title,
                DueDate = start.AddDays(m.OffsetDays), SortOrder = order++, ClientVisible = true,
            };
            milestones[m.Key] = milestone;
            db.Set<Milestone>().Add(milestone);
        }
        var tasks = new List<ProjectTask>();
        var sort = 1000d;
        foreach (var t in template.Tasks)
        {
            var task = new ProjectTask
            {
                ProjectId = project.Id, ClientAccountId = project.ClientAccountId, Title = t.Title, Description = t.Description,
                MilestoneId = t.MilestoneKey is { } k && milestones.TryGetValue(k, out var ms) ? ms.Id : null,
                DueDate = start.AddDays(t.OffsetDays), EstimateHours = t.EstimateHours, Labels = t.Labels.ToList(),
                ClientVisible = t.ClientVisible, SortOrder = sort, CreatedByUserId = createdBy,
            };
            sort += 1000;
            tasks.Add(task);
            db.Set<ProjectTask>().Add(task);
        }
        foreach (var r in template.Recurring)
        {
            db.Set<RecurringTaskRule>().Add(new RecurringTaskRule
            {
                ProjectId = project.Id, ClientAccountId = project.ClientAccountId, Title = r.Title, Description = r.Description,
                DayOfMonth = Math.Clamp(r.DayOfMonth, 1, 28), DueInDays = r.DueInDays, EstimateHours = r.EstimateHours,
                Labels = r.Labels.ToList(), AssigneeUserId = project.OwnerUserId,
            });
        }
        return tasks;
    }
}

public sealed class ProjectService(
    AppDbContext db,
    IClientScope scope,
    ICurrentUser currentUser,
    IAuditLogger audit,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(Now);

    public async Task<Project> LoadAsync(Guid id, CancellationToken ct, bool tracked = false)
    {
        var q = tracked ? db.Set<Project>() : db.Set<Project>().AsNoTracking();
        var project = await (await scope.ApplyAsync(q, p => p.ClientAccountId, ct)).FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw DomainException.NotFound("Project");
        return project;
    }

    public async Task<PagedResult<ProjectSummaryDto>> ListAsync(ProjectListQuery query, CancellationToken ct)
    {
        var q = await scope.ApplyAsync(db.Set<Project>().AsNoTracking(), p => p.ClientAccountId, ct);
        if (query.ClientId is { } cid) q = q.Where(p => p.ClientAccountId == cid);
        if (query.Status is { } status) q = q.Where(p => p.Status == status);
        if (query.Member == "me")
        {
            var me = currentUser.Id;
            q = q.Where(p => p.OwnerUserId == me || db.Set<ProjectMember>().Any(m => m.ProjectId == p.Id && m.UserId == me));
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = PagingExtensions.LikePattern(query.Search);
            q = q.Where(p => EF.Functions.Like(p.Name, pattern));
        }
        q = query.Sort switch
        {
            "name" => q.OrderBy(p => p.Name),
            "endDate" => query.Desc ? q.OrderByDescending(p => p.EndDate) : q.OrderBy(p => p.EndDate),
            _ => q.OrderBy(p => p.Status).ThenByDescending(p => p.CreatedAt),
        };
        var total = await q.CountAsync(ct);
        var rows = await q.Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<ProjectSummaryDto>(await SummariesAsync(rows, ct), total, query.Page, query.PageSize);
    }

    internal async Task<List<ProjectSummaryDto>> SummariesAsync(List<Project> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var today = Today;
        var tasks = await db.Set<ProjectTask>().AsNoTracking().Where(t => ids.Contains(t.ProjectId))
            .GroupBy(t => t.ProjectId)
            .Select(g => new
            {
                g.Key,
                Total = g.Count(),
                Done = g.Count(t => t.Status == ProjectTaskStatus.Done),
                Overdue = g.Count(t => t.Status != ProjectTaskStatus.Done && t.DueDate != null && t.DueDate < today),
            }).ToDictionaryAsync(x => x.Key, ct);
        var minutes = await db.Set<TimeEntry>().AsNoTracking().Where(e => ids.Contains(e.ProjectId))
            .GroupBy(e => e.ProjectId).Select(g => new { g.Key, Minutes = g.Sum(e => e.Minutes) }).ToDictionaryAsync(x => x.Key, x => x.Minutes, ct);
        var clientIds = rows.Select(r => r.ClientAccountId).Distinct().ToList();
        var clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var ownerIds = rows.Where(r => r.OwnerUserId != null).Select(r => r.OwnerUserId!.Value).Distinct().ToList();
        var owners = await db.Set<User>().AsNoTracking().Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new PersonDto(u.Id, u.DisplayName, u.Email), ct);
        return rows.Select(p =>
        {
            var t = tasks.GetValueOrDefault(p.Id);
            var hours = BudgetMath.Hours(minutes.GetValueOrDefault(p.Id));
            var atRisk = p.Status is ProjectStatus.Active or ProjectStatus.Planning &&
                         ((t?.Overdue ?? 0) > 0 || (p.BudgetHours is > 0 && hours >= p.BudgetHours * 0.9m) ||
                          (p.EndDate is { } end && end < today));
            return new ProjectSummaryDto(p.Id, p.ClientAccountId, clients.GetValueOrDefault(p.ClientAccountId) ?? "", p.Name, p.Type, p.Status,
                p.ServiceLines, p.StartDate, p.EndDate, p.OwnerUserId is { } o && owners.TryGetValue(o, out var person) ? person : null,
                (t?.Total ?? 0) - (t?.Done ?? 0), t?.Overdue ?? 0, t?.Total ?? 0, t?.Done ?? 0, p.BudgetHours, hours, atRisk);
        }).ToList();
    }

    public async Task<ProjectDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await LoadAsync(id, ct);
        var client = await db.Set<ClientAccount>().AsNoTracking().FirstAsync(c => c.Id == p.ClientAccountId, ct);
        var memberIds = await db.Set<ProjectMember>().AsNoTracking().Where(m => m.ProjectId == id).Select(m => m.UserId).ToListAsync(ct);
        var people = await db.Set<User>().AsNoTracking().Where(u => memberIds.Contains(u.Id) || u.Id == p.OwnerUserId)
            .ToDictionaryAsync(u => u.Id, u => new PersonDto(u.Id, u.DisplayName, u.Email), ct);
        return new ProjectDetailDto(p.Id, p.ClientAccountId, client.Name, client.Currency, p.Name, p.Description, p.Type, p.Status,
            p.ServiceLines, p.StartDate, p.EndDate, p.BudgetHours, p.BudgetAmount, p.Currency, p.DefaultHourlyRate,
            p.OwnerUserId is { } o && people.TryGetValue(o, out var owner) ? owner : null,
            memberIds.Where(people.ContainsKey).Select(m => people[m]).OrderBy(m => m.DisplayName).ToList(),
            await MilestonesAsync(id, ct), p.TemplateKey, await BudgetAsync(p, ct), p.CreatedAt, p.ConcurrencyStamp);
    }

    public async Task<IReadOnlyList<MilestoneDto>> MilestonesAsync(Guid projectId, CancellationToken ct, bool clientVisibleOnly = false)
    {
        var q = db.Set<Milestone>().AsNoTracking().Where(m => m.ProjectId == projectId);
        if (clientVisibleOnly) q = q.Where(m => m.ClientVisible);
        var milestones = await q.OrderBy(m => m.SortOrder).ThenBy(m => m.DueDate).ToListAsync(ct);
        var tq = db.Set<ProjectTask>().AsNoTracking().Where(t => t.ProjectId == projectId && t.MilestoneId != null);
        if (clientVisibleOnly) tq = tq.Where(t => t.ClientVisible);
        var counts = await tq.GroupBy(t => t.MilestoneId!.Value)
            .Select(g => new { g.Key, Total = g.Count(), Done = g.Count(t => t.Status == ProjectTaskStatus.Done) }).ToDictionaryAsync(x => x.Key, ct);
        return milestones.Select(m => new MilestoneDto(m.Id, m.Title, m.DueDate, m.Status, m.SortOrder, m.ClientVisible,
            counts.GetValueOrDefault(m.Id)?.Total ?? 0, counts.GetValueOrDefault(m.Id)?.Done ?? 0)).ToList();
    }

    /// <summary>Budget burn: hours logged vs budget, and billable amount at the user → role → project default rate.</summary>
    public async Task<BudgetBurn> BudgetAsync(Project p, CancellationToken ct)
    {
        var entries = await db.Set<TimeEntry>().AsNoTracking().Where(e => e.ProjectId == p.Id && e.RunningUserId == null)
            .Select(e => new { e.UserId, e.Minutes, e.Billable }).ToListAsync(ct);
        var userIds = entries.Select(e => e.UserId).Distinct().ToList();
        var roles = await db.Set<UserRole>().AsNoTracking().Where(r => userIds.Contains(r.UserId)).ToListAsync(ct);
        var rates = await db.Set<HourlyRate>().AsNoTracking().Select(r => new RateCard(r.UserId, r.Role, r.Rate, r.Currency)).ToListAsync(ct);
        return BudgetMath.Compute(
            entries.Select(e => new BurnEntry(e.UserId, e.Minutes, e.Billable, roles.Where(r => r.UserId == e.UserId).Select(r => r.Role).ToList())),
            rates, p.BudgetHours, p.BudgetAmount, p.Currency, p.DefaultHourlyRate);
    }

    public async Task<ProjectDetailDto> CreateAsync(CreateProjectRequest r, CancellationToken ct)
    {
        var clientId = r.ClientId!.Value;
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var client = await db.Set<ClientAccount>().AsNoTracking().FirstAsync(c => c.Id == clientId, ct);
        ProjectTemplate? template = null;
        if (!string.IsNullOrWhiteSpace(r.TemplateKey))
            template = await db.Set<ProjectTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.Key == r.TemplateKey && t.IsActive, ct)
                       ?? throw DeliveryRules.Invalid("project.invalid_template", "templateKey", "That template doesn't exist.");
        var project = new Project
        {
            ClientAccountId = clientId, Currency = client.Currency, Status = r.Status, TemplateKey = template?.Key,
            CreatedByUserId = currentUser.Id,
        };
        await ApplyAsync(project, r, template, ct);
        db.Set<Project>().Add(project);
        await SetMembersAsync(project.Id, r.MemberUserIds.Append(currentUser.Id).Concat(project.OwnerUserId is { } o ? new[] { o } : Array.Empty<Guid>()), ct);
        if (template is not null)
            ProjectTemplateApplier.Apply(db, project, template, project.StartDate ?? Today, currentUser.Id);
        audit.Record("project.created", nameof(Project), project.Id, after: new { project.Name, project.ClientAccountId, project.Type, project.TemplateKey });
        await db.SaveChangesAsync(ct);
        return await GetAsync(project.Id, ct);
    }

    public async Task<ProjectDetailDto> UpdateAsync(Guid id, UpdateProjectRequest r, CancellationToken ct)
    {
        var project = await LoadAsync(id, ct, tracked: true);
        DeliveryRules.EnsureStamp(project, r.ConcurrencyStamp, db);
        var before = new { project.Name, project.Type, project.BudgetHours, project.BudgetAmount, project.OwnerUserId, project.EndDate };
        await ApplyAsync(project, r, null, ct);
        await SetMembersAsync(id, r.MemberUserIds.Concat(project.OwnerUserId is { } o ? new[] { o } : Array.Empty<Guid>()), ct, replace: true);
        audit.Record("project.updated", nameof(Project), id, before,
            new { project.Name, project.Type, project.BudgetHours, project.BudgetAmount, project.OwnerUserId, project.EndDate });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ProjectDetailDto> ChangeStatusAsync(Guid id, ChangeProjectStatusRequest r, CancellationToken ct)
    {
        var project = await LoadAsync(id, ct, tracked: true);
        DeliveryRules.EnsureStamp(project, r.ConcurrencyStamp, db);
        var status = r.Status!.Value;
        if (!Enum.IsDefined(status)) throw DeliveryRules.Invalid("project.invalid_status", "status", "Choose a status.");
        if (project.Status != status)
        {
            audit.Record("project.status_changed", nameof(Project), id, new { project.Status }, new { Status = status });
            project.Status = status;
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    private async Task ApplyAsync(Project p, ProjectRequestBase r, ProjectTemplate? template, CancellationToken ct)
    {
        var type = r.Type!.Value;
        if (!Enum.IsDefined(type)) throw DeliveryRules.Invalid("project.invalid_type", "type", "Choose a project type.");
        if (r.StartDate is { } s && r.EndDate is { } e && e < s)
            throw DeliveryRules.Invalid("project.invalid_dates", "endDate", "The end date must be on or after the start date.");
        var lines = DeliveryRules.CleanList(r.ServiceLines.Select(x => x.ToLowerInvariant()), 10, 32, "serviceLines");
        if (lines.Any(l => !DeliveryRules.ServiceLines.Contains(l)))
            throw DeliveryRules.Invalid("project.invalid_service_line", "serviceLines", "Unknown service line.");
        if (r.OwnerUserId is { } owner && !(await StaffDirectory.ValidStaffAsync(db, new[] { owner }, ct)).Contains(owner))
            throw DeliveryRules.Invalid("project.invalid_owner", "ownerUserId", "Choose an active staff member.");
        p.Name = r.Name.Trim();
        p.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        p.Type = type;
        p.ServiceLines = lines.Count == 0 && template is not null ? template.ServiceLines.ToList() : lines;
        p.StartDate = r.StartDate ?? (template is not null ? Today : null);
        p.EndDate = r.EndDate ?? (template?.DurationDays is { } d && p.StartDate is { } start ? start.AddDays(d) : null);
        p.BudgetHours = r.BudgetHours ?? template?.DefaultBudgetHours;
        p.BudgetAmount = r.BudgetAmount is { } amount ? Money.Round(amount, p.Currency) : null;
        p.DefaultHourlyRate = r.DefaultHourlyRate;
        p.OwnerUserId = r.OwnerUserId;
    }

    private async Task SetMembersAsync(Guid projectId, IEnumerable<Guid> userIds, CancellationToken ct, bool replace = false)
    {
        var ids = userIds.Distinct().ToList();
        var valid = await StaffDirectory.ValidStaffAsync(db, ids, ct);
        if (ids.Any(i => !valid.Contains(i) && i != currentUser.Id))
            throw DeliveryRules.Invalid("project.invalid_member", "memberUserIds", "Project members must be active staff.");
        var existing = replace
            ? await db.Set<ProjectMember>().Where(m => m.ProjectId == projectId).ToListAsync(ct)
            : new List<ProjectMember>();
        foreach (var stale in existing.Where(m => !ids.Contains(m.UserId))) db.Remove(stale);
        foreach (var id in ids.Where(valid.Contains).Where(i => existing.All(m => m.UserId != i)))
            db.Set<ProjectMember>().Add(new ProjectMember { ProjectId = projectId, UserId = id, AddedAt = Now });
    }

    /// <summary>Files of the project: deliverable versions and task attachments, newest first.</summary>
    public async Task<IReadOnlyList<ProjectFileDto>> FilesAsync(Guid projectId, CancellationToken ct)
    {
        await LoadAsync(projectId, ct);
        var fromVersions = await (from v in db.Set<DeliverableVersion>().AsNoTracking()
                                  join d in db.Set<Deliverable>() on v.DeliverableId equals d.Id
                                  join f in db.Set<DeliveryFile>() on v.FileId equals f.Id
                                  where d.ProjectId == projectId
                                  select new { f, Source = d.Title + " · v" + v.Number, DeliverableId = (Guid?)d.Id, TaskId = (Guid?)null }).ToListAsync(ct);
        var fromTasks = await (from a in db.Set<TaskAttachment>().AsNoTracking()
                               join t in db.Set<ProjectTask>() on a.TaskId equals t.Id
                               join f in db.Set<DeliveryFile>() on a.FileId equals f.Id
                               where t.ProjectId == projectId
                               select new { f, Source = t.Title, DeliverableId = (Guid?)null, TaskId = (Guid?)t.Id }).ToListAsync(ct);
        return fromVersions.Concat(fromTasks).OrderByDescending(x => x.f.CreatedAt)
            .Select(x => new ProjectFileDto(DeliveryFileDto.From(x.f), x.Source, x.DeliverableId, x.TaskId)).ToList();
    }

    // ------------------------------------------------------------------ milestones

    public async Task<IReadOnlyList<MilestoneDto>> AddMilestoneAsync(Guid projectId, MilestoneRequest r, CancellationToken ct)
    {
        var p = await LoadAsync(projectId, ct);
        var max = await db.Set<Milestone>().Where(m => m.ProjectId == projectId).MaxAsync(m => (int?)m.SortOrder, ct) ?? -1;
        db.Set<Milestone>().Add(new Milestone
        {
            ProjectId = projectId, ClientAccountId = p.ClientAccountId, Title = r.Title.Trim(), DueDate = r.DueDate, Status = r.Status,
            ClientVisible = r.ClientVisible, SortOrder = max + 1, CompletedAt = r.Status == MilestoneStatus.Done ? Now : null,
        });
        await db.SaveChangesAsync(ct);
        return await MilestonesAsync(projectId, ct);
    }

    public async Task<IReadOnlyList<MilestoneDto>> UpdateMilestoneAsync(Guid projectId, Guid milestoneId, MilestoneRequest r, CancellationToken ct)
    {
        await LoadAsync(projectId, ct);
        var m = await db.Set<Milestone>().FirstOrDefaultAsync(x => x.Id == milestoneId && x.ProjectId == projectId, ct)
                ?? throw DomainException.NotFound("Milestone");
        m.Title = r.Title.Trim();
        m.DueDate = r.DueDate;
        m.ClientVisible = r.ClientVisible;
        if (m.Status != r.Status) m.CompletedAt = r.Status == MilestoneStatus.Done ? Now : null;
        m.Status = r.Status;
        await db.SaveChangesAsync(ct);
        return await MilestonesAsync(projectId, ct);
    }

    public async Task<IReadOnlyList<MilestoneDto>> DeleteMilestoneAsync(Guid projectId, Guid milestoneId, CancellationToken ct)
    {
        await LoadAsync(projectId, ct);
        var m = await db.Set<Milestone>().FirstOrDefaultAsync(x => x.Id == milestoneId && x.ProjectId == projectId, ct)
                ?? throw DomainException.NotFound("Milestone");
        await db.Set<ProjectTask>().Where(t => t.MilestoneId == milestoneId).ExecuteUpdateAsync(s => s.SetProperty(t => t.MilestoneId, (Guid?)null), ct);
        db.Remove(m);
        audit.Record("project.milestone_deleted", nameof(Milestone), milestoneId, before: new { m.Title, m.ProjectId, m.Status });
        await db.SaveChangesAsync(ct);
        return await MilestonesAsync(projectId, ct);
    }

    // ------------------------------------------------------------------ templates

    public async Task<IReadOnlyList<ProjectTemplateDto>> TemplatesAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.Set<ProjectTemplate>().AsNoTracking();
        if (!includeInactive) q = q.Where(t => t.IsActive);
        return (await q.OrderBy(t => t.Name).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<ProjectTemplateDto> SaveTemplateAsync(Guid? id, ProjectTemplateRequest r, CancellationToken ct)
    {
        var type = r.ProjectType!.Value;
        if (!Enum.IsDefined(type)) throw DeliveryRules.Invalid("template.invalid_type", "projectType", "Choose a project type.");
        if (r.Tasks.Count > 200 || r.Milestones.Count > 50 || r.Recurring.Count > 20)
            throw DeliveryRules.Invalid("template.too_large", "tasks", "Templates hold at most 200 tasks, 50 milestones and 20 recurring tasks.");
        var keys = r.Milestones.Select(m => m.Key).ToHashSet();
        if (r.Milestones.Any(m => string.IsNullOrWhiteSpace(m.Key) || string.IsNullOrWhiteSpace(m.Title) || m.Title.Length > 200) ||
            keys.Count != r.Milestones.Count)
            throw DeliveryRules.Invalid("template.invalid_milestones", "milestones", "Each milestone needs a unique key and a title.");
        if (r.Tasks.Any(t => string.IsNullOrWhiteSpace(t.Title) || t.Title.Length > 300 || (t.MilestoneKey is { } k && !keys.Contains(k))))
            throw DeliveryRules.Invalid("template.invalid_tasks", "tasks", "Each task needs a title and an existing milestone key.");
        if (r.Recurring.Any(x => string.IsNullOrWhiteSpace(x.Title) || x.DayOfMonth is < 1 or > 28))
            throw DeliveryRules.Invalid("template.invalid_recurring", "recurring", "Recurring tasks need a title and a day between 1 and 28.");

        ProjectTemplate t;
        if (id is { } existing)
        {
            t = await db.Set<ProjectTemplate>().FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw DomainException.NotFound("ProjectTemplate");
            DeliveryRules.EnsureStamp(t, r.ConcurrencyStamp ?? Guid.Empty, db);
        }
        else
        {
            if (await db.Set<ProjectTemplate>().AnyAsync(x => x.Key == r.Key, ct))
                throw DeliveryRules.Invalid("template.key_taken", "key", "Another template uses this key.");
            t = new ProjectTemplate { Key = r.Key };
            db.Set<ProjectTemplate>().Add(t);
        }
        t.Name = r.Name.Trim();
        t.Description = r.Description?.Trim();
        t.ProjectType = type;
        t.ServiceLines = DeliveryRules.CleanList(r.ServiceLines.Select(x => x.ToLowerInvariant()), 10, 32, "serviceLines");
        t.DefaultBudgetHours = r.DefaultBudgetHours;
        t.DurationDays = r.DurationDays;
        t.Milestones = r.Milestones.Select(m => m with { Title = m.Title.Trim() }).ToList();
        t.Tasks = r.Tasks.Select(x => x with { Title = x.Title.Trim(), Labels = x.Labels ?? new List<string>() }).ToList();
        t.Recurring = r.Recurring.Select(x => x with { Labels = x.Labels ?? new List<string>() }).ToList();
        t.IsActive = r.IsActive;
        audit.Record(id is null ? "project_template.created" : "project_template.updated", nameof(ProjectTemplate), t.Id,
            after: new { t.Key, t.Name, Tasks = t.Tasks.Count, t.IsActive });
        await db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    private static ProjectTemplateDto ToDto(ProjectTemplate t) => new(t.Id, t.Key, t.Name, t.Description, t.ProjectType, t.ServiceLines,
        t.DefaultBudgetHours, t.DurationDays, t.Milestones, t.Tasks, t.Recurring, t.IsActive, t.ConcurrencyStamp,
        DeliveryTemplateService.IsBuiltInProject(t.Key));

    public async Task<IReadOnlyList<BriefTemplateDto>> BriefTemplatesAsync(CancellationToken ct, bool includeInactive = false) =>
        (await db.Set<BriefTemplate>().AsNoTracking().Where(t => includeInactive || t.IsActive).OrderBy(t => t.Name).ToListAsync(ct))
        .Select(DeliveryTemplateService.ToDto).ToList();

    public async Task<IReadOnlyList<ReportTemplateDto>> ReportTemplatesAsync(CancellationToken ct, bool includeInactive = false) =>
        (await db.Set<ReportTemplate>().AsNoTracking().Where(t => includeInactive || t.IsActive).OrderBy(t => t.Name).ToListAsync(ct))
        .Select(DeliveryTemplateService.ToDto).ToList();
}
