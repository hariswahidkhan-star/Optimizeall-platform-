using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Clients;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Projects;

public sealed class TaskService(
    AppDbContext db,
    IClientScope scope,
    ICurrentUser currentUser,
    IAuditLogger audit,
    INotificationService notifications,
    ProjectService projects,
    TimeProvider clock)
{
    private const double Gap = 1000d;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(Now);

    private async Task<ProjectTask> LoadAsync(Guid taskId, CancellationToken ct, bool tracked = true)
    {
        var q = tracked ? db.Set<ProjectTask>() : db.Set<ProjectTask>().AsNoTracking();
        return await (await scope.ApplyAsync(q, t => t.ClientAccountId, ct)).FirstOrDefaultAsync(t => t.Id == taskId, ct)
               ?? throw DomainException.NotFound("Task");
    }

    // ------------------------------------------------------------------ queries

    public async Task<IReadOnlyList<TaskSummaryDto>> ListForProjectAsync(Guid projectId, TaskListQuery query, CancellationToken ct)
    {
        await projects.LoadAsync(projectId, ct);
        var q = db.Set<ProjectTask>().AsNoTracking().Where(t => t.ProjectId == projectId);
        if (query.Status is { } s) q = q.Where(t => t.Status == s);
        if (query.MilestoneId is { } m) q = q.Where(t => t.MilestoneId == m);
        if (query.AssigneeId is { } a) q = q.Where(t => db.Set<TaskAssignee>().Any(x => x.TaskId == t.Id && x.UserId == a));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = Common.Http.PagingExtensions.LikePattern(query.Search);
            q = q.Where(t => EF.Functions.Like(t.Title, p));
        }
        return await SummariesAsync(await q.OrderBy(t => t.Status).ThenBy(t => t.SortOrder).ToListAsync(ct), ct);
    }

    public async Task<IReadOnlyList<TaskSummaryDto>> MyTasksAsync(MyTasksQuery query, CancellationToken ct)
    {
        var me = currentUser.Id;
        var today = Today;
        var q = db.Set<ProjectTask>().AsNoTracking().Where(t => db.Set<TaskAssignee>().Any(a => a.TaskId == t.Id && a.UserId == me));
        q = query.Filter switch
        {
            "done" => q.Where(t => t.Status == ProjectTaskStatus.Done),
            "overdue" => q.Where(t => t.Status != ProjectTaskStatus.Done && t.DueDate != null && t.DueDate < today),
            "today" => q.Where(t => t.Status != ProjectTaskStatus.Done && t.DueDate != null && t.DueDate <= today),
            "week" => q.Where(t => t.Status != ProjectTaskStatus.Done && t.DueDate != null && t.DueDate <= today.AddDays(7)),
            _ => q.Where(t => t.Status != ProjectTaskStatus.Done),
        };
        var rows = await q.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenByDescending(t => t.Priority).Take(300).ToListAsync(ct);
        return await SummariesAsync(rows, ct);
    }

    internal async Task<List<TaskSummaryDto>> SummariesAsync(List<ProjectTask> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return new List<TaskSummaryDto>();
        var ids = rows.Select(r => r.Id).ToList();
        var assignees = await (from a in db.Set<TaskAssignee>().AsNoTracking()
                               join u in db.Set<User>() on a.UserId equals u.Id
                               where ids.Contains(a.TaskId)
                               select new { a.TaskId, Person = new PersonDto(u.Id, u.DisplayName, u.Email) }).ToListAsync(ct);
        var checklist = await db.Set<TaskChecklistItem>().AsNoTracking().Where(c => ids.Contains(c.TaskId))
            .GroupBy(c => c.TaskId).Select(g => new { g.Key, Total = g.Count(), Done = g.Count(c => c.IsDone) }).ToDictionaryAsync(x => x.Key, ct);
        var comments = await db.Set<TaskComment>().AsNoTracking().Where(c => ids.Contains(c.TaskId))
            .GroupBy(c => c.TaskId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var blocked = await (from d in db.Set<TaskDependency>().AsNoTracking()
                             join b in db.Set<ProjectTask>() on d.BlockedByTaskId equals b.Id
                             where ids.Contains(d.TaskId) && b.Status != ProjectTaskStatus.Done
                             select d.TaskId).Distinct().ToListAsync(ct);
        var projectIds = rows.Select(r => r.ProjectId).Distinct().ToList();
        var projectNames = await db.Set<Project>().AsNoTracking().Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var clientIds = rows.Select(r => r.ClientAccountId).Distinct().ToList();
        var clientNames = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var today = Today;
        return rows.Select(t => new TaskSummaryDto(t.Id, t.ProjectId, projectNames.GetValueOrDefault(t.ProjectId) ?? "", t.ClientAccountId,
            clientNames.GetValueOrDefault(t.ClientAccountId) ?? "", t.MilestoneId, t.Title, t.Status, t.Priority, t.DueDate, t.EstimateHours,
            t.Labels, t.ClientVisible, t.SortOrder,
            assignees.Where(a => a.TaskId == t.Id).Select(a => a.Person).OrderBy(p => p.DisplayName).ToList(),
            checklist.GetValueOrDefault(t.Id)?.Done ?? 0, checklist.GetValueOrDefault(t.Id)?.Total ?? 0, comments.GetValueOrDefault(t.Id),
            blocked.Contains(t.Id), t.Status != ProjectTaskStatus.Done && t.DueDate is { } due && due < today, t.ConcurrencyStamp)).ToList();
    }

    public async Task<TaskDetailDto> GetAsync(Guid taskId, CancellationToken ct)
    {
        var t = await LoadAsync(taskId, ct, tracked: false);
        var summary = (await SummariesAsync(new List<ProjectTask> { t }, ct)).Single();
        var checklist = await db.Set<TaskChecklistItem>().AsNoTracking().Where(c => c.TaskId == taskId).OrderBy(c => c.SortOrder)
            .Select(c => new ChecklistItemDto(c.Id, c.Text, c.IsDone, c.SortOrder)).ToListAsync(ct);
        var comments = await db.Set<TaskComment>().AsNoTracking().Where(c => c.TaskId == taskId).OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var watcherIds = await db.Set<TaskWatcher>().AsNoTracking().Where(w => w.TaskId == taskId).Select(w => w.UserId).ToListAsync(ct);
        var blockedBy = await (from d in db.Set<TaskDependency>().AsNoTracking()
                               join b in db.Set<ProjectTask>() on d.BlockedByTaskId equals b.Id
                               where d.TaskId == taskId
                               select new TaskRefDto(b.Id, b.Title, b.Status)).ToListAsync(ct);
        var blocking = await (from d in db.Set<TaskDependency>().AsNoTracking()
                              join b in db.Set<ProjectTask>() on d.TaskId equals b.Id
                              where d.BlockedByTaskId == taskId
                              select new TaskRefDto(b.Id, b.Title, b.Status)).ToListAsync(ct);
        var attachments = await (from a in db.Set<TaskAttachment>().AsNoTracking()
                                 join f in db.Set<DeliveryFile>() on a.FileId equals f.Id
                                 where a.TaskId == taskId
                                 orderby a.CreatedAt
                                 select new { a, f }).ToListAsync(ct);
        var peopleIds = comments.Select(c => c.AuthorUserId).Concat(comments.SelectMany(c => c.MentionedUserIds)).Concat(watcherIds)
            .Concat(attachments.Select(a => a.a.AddedByUserId)).Distinct().ToList();
        var people = await db.Set<User>().AsNoTracking().Where(u => peopleIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new PersonDto(u.Id, u.DisplayName, u.Email), ct);
        PersonDto P(Guid id) => people.TryGetValue(id, out var p) ? p : new PersonDto(id, "Former user", "");
        var minutes = await db.Set<TimeEntry>().AsNoTracking().Where(e => e.TaskId == taskId).SumAsync(e => (int?)e.Minutes, ct) ?? 0;
        return new TaskDetailDto(summary, t.Description, checklist,
            comments.Select(c => new TaskCommentDto(c.Id, P(c.AuthorUserId), c.Body, c.MentionedUserIds.Select(P).ToList(), c.CreatedAt, c.EditedAt)).ToList(),
            watcherIds.Select(P).ToList(), blockedBy, blocking,
            attachments.Select(x => new AttachmentDto(x.a.Id, DeliveryFileDto.From(x.f), P(x.a.AddedByUserId), x.a.CreatedAt)).ToList(),
            BudgetMath.Hours(minutes), t.CreatedAt, t.CompletedAt, watcherIds.Contains(currentUser.Id));
    }

    // ------------------------------------------------------------------ commands

    public async Task<TaskDetailDto> CreateAsync(Guid projectId, CreateTaskRequest r, CancellationToken ct)
    {
        var project = await projects.LoadAsync(projectId, ct);
        var task = new ProjectTask { ProjectId = projectId, ClientAccountId = project.ClientAccountId, CreatedByUserId = currentUser.Id };
        await ApplyAsync(task, r, ct);
        var max = await db.Set<ProjectTask>().Where(t => t.ProjectId == projectId && t.Status == task.Status).MaxAsync(t => (double?)t.SortOrder, ct) ?? 0;
        task.SortOrder = max + Gap;
        db.Set<ProjectTask>().Add(task);
        var sort = 0;
        foreach (var item in DeliveryRules.CleanList(r.Checklist, 50, 500, "checklist"))
            db.Set<TaskChecklistItem>().Add(new TaskChecklistItem { TaskId = task.Id, Text = item, SortOrder = sort++ });
        var assignees = await SetAssigneesAsync(task, r.AssigneeUserIds, ct);
        await SetDependenciesAsync(task, r.BlockedByTaskIds, ct);
        foreach (var w in assignees.Append(currentUser.Id).Distinct())
            db.Set<TaskWatcher>().Add(new TaskWatcher { TaskId = task.Id, UserId = w });
        await NotifyAssignedAsync(task, project.Name, assignees, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(task.Id, ct);
    }

    public async Task<TaskDetailDto> UpdateAsync(Guid taskId, UpdateTaskRequest r, CancellationToken ct)
    {
        var task = await LoadAsync(taskId, ct);
        DeliveryRules.EnsureStamp(task, r.ConcurrencyStamp, db);
        var previousStatus = task.Status;
        await ApplyAsync(task, r, ct);
        if (task.Status != previousStatus) await OnStatusChangeAsync(task, previousStatus, ct);
        var existing = await db.Set<TaskAssignee>().Where(a => a.TaskId == taskId).Select(a => a.UserId).ToListAsync(ct);
        var added = await SetAssigneesAsync(task, r.AssigneeUserIds, ct, existing);
        await SetDependenciesAsync(task, r.BlockedByTaskIds, ct, replace: true);
        var watchers = await db.Set<TaskWatcher>().Where(w => w.TaskId == taskId).Select(w => w.UserId).ToListAsync(ct);
        foreach (var w in added.Where(a => !watchers.Contains(a)))
            db.Set<TaskWatcher>().Add(new TaskWatcher { TaskId = taskId, UserId = w });
        var projectName = await db.Set<Project>().Where(p => p.Id == task.ProjectId).Select(p => p.Name).FirstAsync(ct);
        await NotifyAssignedAsync(task, projectName, added, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(taskId, ct);
    }

    /// <summary>Kanban move (drag and drop or keyboard): new status column and position after <c>AfterTaskId</c>.</summary>
    public async Task<TaskSummaryDto> MoveAsync(Guid taskId, MoveTaskRequest r, CancellationToken ct)
    {
        var task = await LoadAsync(taskId, ct);
        DeliveryRules.EnsureStamp(task, r.ConcurrencyStamp, db);
        var status = r.Status!.Value;
        if (!Enum.IsDefined(status)) throw DeliveryRules.Invalid("task.invalid_status", "status", "Choose a status.");
        var column = await db.Set<ProjectTask>().AsNoTracking()
            .Where(t => t.ProjectId == task.ProjectId && t.Status == status && t.Id != taskId)
            .OrderBy(t => t.SortOrder).Select(t => new { t.Id, t.SortOrder }).ToListAsync(ct);
        double sortOrder;
        if (r.AfterTaskId is null)
        {
            sortOrder = column.Count == 0 ? Gap : column[0].SortOrder - Gap;
        }
        else
        {
            var index = column.FindIndex(c => c.Id == r.AfterTaskId);
            if (index < 0) throw DeliveryRules.Invalid("task.invalid_position", "afterTaskId", "That task isn't in the target column.");
            sortOrder = index == column.Count - 1 ? column[index].SortOrder + Gap : (column[index].SortOrder + column[index + 1].SortOrder) / 2;
        }
        var previous = task.Status;
        task.SortOrder = sortOrder;
        task.Status = status;
        if (previous != status) await OnStatusChangeAsync(task, previous, ct);
        await db.SaveChangesAsync(ct);
        return (await SummariesAsync(new List<ProjectTask> { task }, ct)).Single();
    }

    /// <summary>The author edits their comment (mentions are kept and not notified again).</summary>
    public async Task<TaskDetailDto> EditCommentAsync(Guid taskId, Guid commentId, EditCommentRequest r, CancellationToken ct)
    {
        await LoadAsync(taskId, ct, tracked: false);
        var comment = await db.Set<TaskComment>().FirstOrDefaultAsync(c => c.Id == commentId && c.TaskId == taskId, ct)
                      ?? throw DomainException.NotFound("Comment");
        if (comment.AuthorUserId != currentUser.Id)
            throw DomainException.Forbidden("task.comment_not_author", "Only the author can edit a comment.");
        var before = comment.Body;
        comment.Body = r.Body.Trim();
        comment.EditedAt = Now;
        audit.Record("task.comment_edited", nameof(TaskComment), commentId, new { Body = before }, new { comment.Body });
        await db.SaveChangesAsync(ct);
        return await GetAsync(taskId, ct);
    }

    /// <summary>The author, or a project manager, deletes a comment (audited with its text).</summary>
    public async Task<TaskDetailDto> DeleteCommentAsync(Guid taskId, Guid commentId, CancellationToken ct)
    {
        await LoadAsync(taskId, ct, tracked: false);
        var comment = await db.Set<TaskComment>().FirstOrDefaultAsync(c => c.Id == commentId && c.TaskId == taskId, ct)
                      ?? throw DomainException.NotFound("Comment");
        if (comment.AuthorUserId != currentUser.Id && !currentUser.HasPermission(Permissions.ProjectsManage))
            throw DomainException.Forbidden("task.comment_not_author", "Only the author or a project manager can delete a comment.");
        db.Remove(comment);
        audit.Record("task.comment_deleted", nameof(TaskComment), commentId, before: new { comment.Body, comment.AuthorUserId });
        await db.SaveChangesAsync(ct);
        return await GetAsync(taskId, ct);
    }

    public async Task DeleteAsync(Guid taskId, CancellationToken ct)
    {
        var task = await LoadAsync(taskId, ct);
        if (await db.Set<TimeEntry>().AnyAsync(e => e.TaskId == taskId, ct))
            throw DomainException.Conflict("task.has_time", "Time has been logged on this task; mark it Done instead of deleting it.");
        await db.Set<TaskDependency>().Where(d => d.BlockedByTaskId == taskId).ExecuteDeleteAsync(ct);
        db.Remove(task);
        audit.Record("task.deleted", nameof(ProjectTask), taskId, before: new { task.Title, task.ProjectId, task.Status });
        await db.SaveChangesAsync(ct);
    }

    private async Task OnStatusChangeAsync(ProjectTask task, ProjectTaskStatus previous, CancellationToken ct)
    {
        if (task.Status == ProjectTaskStatus.Done)
        {
            var openBlockers = await (from d in db.Set<TaskDependency>()
                                      join b in db.Set<ProjectTask>() on d.BlockedByTaskId equals b.Id
                                      where d.TaskId == task.Id && b.Status != ProjectTaskStatus.Done
                                      select b.Title).ToListAsync(ct);
            if (openBlockers.Count > 0)
                throw DomainException.Conflict("task.blocked", $"Finish the blocking task first: {string.Join(", ", openBlockers)}.");
            task.CompletedAt = Now;
        }
        else if (previous == ProjectTaskStatus.Done)
        {
            task.CompletedAt = null;
        }
    }

    private async Task ApplyAsync(ProjectTask t, TaskRequestBase r, CancellationToken ct)
    {
        if (!Enum.IsDefined(r.Status) || !Enum.IsDefined(r.Priority))
            throw DeliveryRules.Invalid("task.invalid_status", "status", "Choose a status and priority.");
        if (r.MilestoneId is { } m && !await db.Set<Milestone>().AnyAsync(x => x.Id == m && x.ProjectId == t.ProjectId, ct))
            throw DeliveryRules.Invalid("task.invalid_milestone", "milestoneId", "That milestone isn't part of this project.");
        t.Title = r.Title.Trim();
        t.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description;
        t.Priority = r.Priority;
        t.DueDate = r.DueDate;
        t.EstimateHours = r.EstimateHours;
        t.Labels = DeliveryRules.CleanList(r.Labels.Select(l => l.ToLowerInvariant()), 10, 40, "labels");
        t.MilestoneId = r.MilestoneId;
        t.ClientVisible = r.ClientVisible;
        if (t.Status != r.Status && db.Entry(t).State == EntityState.Detached && r.Status == ProjectTaskStatus.Done) t.CompletedAt = Now;
        t.Status = r.Status;
    }

    /// <summary>Replaces assignees; returns the newly added ones.</summary>
    private async Task<List<Guid>> SetAssigneesAsync(ProjectTask task, List<Guid> requested, CancellationToken ct, List<Guid>? existing = null)
    {
        var ids = requested.Distinct().ToList();
        if (ids.Count > 10) throw DeliveryRules.Invalid("task.too_many_assignees", "assigneeUserIds", "At most 10 assignees.");
        var valid = await StaffDirectory.ValidStaffAsync(db, ids, ct);
        if (ids.Any(i => !valid.Contains(i))) throw DeliveryRules.Invalid("task.invalid_assignee", "assigneeUserIds", "Assignees must be active staff.");
        existing ??= new List<Guid>();
        if (existing.Count > 0)
        {
            var removed = existing.Except(ids).ToList();
            if (removed.Count > 0)
                db.RemoveRange(await db.Set<TaskAssignee>().Where(a => a.TaskId == task.Id && removed.Contains(a.UserId)).ToListAsync(ct));
        }
        var added = ids.Except(existing).ToList();
        foreach (var id in added) db.Set<TaskAssignee>().Add(new TaskAssignee { TaskId = task.Id, UserId = id });
        return added;
    }

    private async Task SetDependenciesAsync(ProjectTask task, List<Guid> blockedBy, CancellationToken ct, bool replace = false)
    {
        var ids = blockedBy.Distinct().ToList();
        if (ids.Contains(task.Id)) throw DeliveryRules.Invalid("task.self_dependency", "blockedByTaskIds", "A task can't block itself.");
        if (ids.Count > 0 && await db.Set<ProjectTask>().CountAsync(t => ids.Contains(t.Id) && t.ProjectId == task.ProjectId, ct) != ids.Count)
            throw DeliveryRules.Invalid("task.invalid_dependency", "blockedByTaskIds", "Blocking tasks must be in the same project.");
        // Reject cycles: none of the blockers may (transitively) be blocked by this task.
        var frontier = new Queue<Guid>(ids);
        var seen = new HashSet<Guid>();
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            if (!seen.Add(current)) continue;
            var next = await db.Set<TaskDependency>().AsNoTracking().Where(d => d.TaskId == current).Select(d => d.BlockedByTaskId).ToListAsync(ct);
            if (next.Contains(task.Id))
                throw DeliveryRules.Invalid("task.dependency_cycle", "blockedByTaskIds", "That would create a circular dependency.");
            foreach (var n in next) frontier.Enqueue(n);
        }
        var existing = replace
            ? await db.Set<TaskDependency>().Where(d => d.TaskId == task.Id).ToListAsync(ct)
            : new List<TaskDependency>();
        foreach (var stale in existing.Where(d => !ids.Contains(d.BlockedByTaskId))) db.Remove(stale);
        foreach (var id in ids.Where(i => existing.All(d => d.BlockedByTaskId != i)))
            db.Set<TaskDependency>().Add(new TaskDependency { TaskId = task.Id, BlockedByTaskId = id });
    }

    private async Task NotifyAssignedAsync(ProjectTask task, string projectName, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        foreach (var user in userIds.Where(u => u != currentUser.Id))
            await notifications.StageAsync(new NotificationRequest(user, DeliveryNotificationTypes.TaskAssigned,
                $"New task: {task.Title}", $"You were assigned a task on {projectName}{(task.DueDate is { } d ? $", due {d:yyyy-MM-dd}" : "")}.",
                DeliveryLinks.AgencyTask(task.ProjectId, task.Id), new[] { NotificationChannel.Email }), ct);
    }

    // ------------------------------------------------------------------ comments, mentions, watchers

    /// <summary>
    /// Adds a comment. Mentioned staff get a notification (in-app + email) once per comment; other watchers get an in-app
    /// notification. The author becomes a watcher.
    /// </summary>
    public async Task<TaskDetailDto> CommentAsync(Guid taskId, TaskCommentRequest r, CancellationToken ct)
    {
        var task = await LoadAsync(taskId, ct, tracked: false);
        var mentions = r.MentionUserIds.Distinct().Where(id => id != currentUser.Id).ToList();
        if (mentions.Count > 20) throw DeliveryRules.Invalid("task.too_many_mentions", "mentionUserIds", "At most 20 mentions.");
        var valid = await StaffDirectory.ValidStaffAsync(db, mentions, ct);
        if (mentions.Any(m => !valid.Contains(m))) throw DeliveryRules.Invalid("task.invalid_mention", "mentionUserIds", "You can only mention active staff.");
        var comment = new TaskComment
        {
            TaskId = taskId, ClientAccountId = task.ClientAccountId, AuthorUserId = currentUser.Id, Body = r.Body.Trim(),
            MentionedUserIds = mentions, CreatedAt = Now,
        };
        db.Set<TaskComment>().Add(comment);
        var watchers = await db.Set<TaskWatcher>().AsNoTracking().Where(w => w.TaskId == taskId).Select(w => w.UserId).ToListAsync(ct);
        if (!watchers.Contains(currentUser.Id)) db.Set<TaskWatcher>().Add(new TaskWatcher { TaskId = taskId, UserId = currentUser.Id });
        var author = await db.Set<User>().AsNoTracking().Where(u => u.Id == currentUser.Id).Select(u => u.DisplayName).FirstAsync(ct);
        var preview = comment.Body.Length > 180 ? comment.Body[..180] + "…" : comment.Body;
        var link = DeliveryLinks.AgencyTask(task.ProjectId, task.Id);
        foreach (var m in mentions)
            await notifications.StageAsync(new NotificationRequest(m, DeliveryNotificationTypes.TaskMention,
                $"{author} mentioned you on “{task.Title}”", preview, link, new[] { NotificationChannel.Email }), ct);
        foreach (var w in watchers.Where(w => w != currentUser.Id && !mentions.Contains(w)))
            await notifications.StageAsync(new NotificationRequest(w, DeliveryNotificationTypes.TaskComment,
                $"{author} commented on “{task.Title}”", preview, link), ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(taskId, ct);
    }

    public async Task<TaskDetailDto> WatchAsync(Guid taskId, bool watch, CancellationToken ct)
    {
        await LoadAsync(taskId, ct, tracked: false);
        var me = currentUser.Id;
        var row = await db.Set<TaskWatcher>().FirstOrDefaultAsync(w => w.TaskId == taskId && w.UserId == me, ct);
        if (watch && row is null) db.Set<TaskWatcher>().Add(new TaskWatcher { TaskId = taskId, UserId = me });
        if (!watch && row is not null) db.Remove(row);
        await db.SaveChangesAsync(ct);
        return await GetAsync(taskId, ct);
    }

    // ------------------------------------------------------------------ checklist & attachments

    public async Task<TaskDetailDto> AddChecklistItemAsync(Guid taskId, ChecklistItemRequest r, CancellationToken ct)
    {
        var task = await LoadAsync(taskId, ct);
        var count = await db.Set<TaskChecklistItem>().CountAsync(c => c.TaskId == taskId, ct);
        if (count >= 50) throw DeliveryRules.Invalid("task.checklist_full", "text", "A checklist holds at most 50 items.");
        db.Set<TaskChecklistItem>().Add(new TaskChecklistItem { TaskId = taskId, Text = r.Text.Trim(), IsDone = r.IsDone, SortOrder = count });
        ConcurrencyTouch(task);
        await db.SaveChangesAsync(ct);
        return await GetAsync(taskId, ct);
    }

    public async Task<TaskDetailDto> UpdateChecklistItemAsync(Guid taskId, Guid itemId, ChecklistItemRequest r, CancellationToken ct)
    {
        var task = await LoadAsync(taskId, ct);
        var item = await db.Set<TaskChecklistItem>().FirstOrDefaultAsync(c => c.Id == itemId && c.TaskId == taskId, ct)
                   ?? throw DomainException.NotFound("ChecklistItem");
        item.Text = r.Text.Trim();
        item.IsDone = r.IsDone;
        ConcurrencyTouch(task);
        await db.SaveChangesAsync(ct);
        return await GetAsync(taskId, ct);
    }

    public async Task<TaskDetailDto> DeleteChecklistItemAsync(Guid taskId, Guid itemId, CancellationToken ct)
    {
        var task = await LoadAsync(taskId, ct);
        var item = await db.Set<TaskChecklistItem>().FirstOrDefaultAsync(c => c.Id == itemId && c.TaskId == taskId, ct)
                   ?? throw DomainException.NotFound("ChecklistItem");
        db.Remove(item);
        ConcurrencyTouch(task);
        await db.SaveChangesAsync(ct);
        return await GetAsync(taskId, ct);
    }

    public async Task<TaskDetailDto> AttachAsync(Guid taskId, Guid fileId, CancellationToken ct)
    {
        var task = await LoadAsync(taskId, ct, tracked: false);
        if (!await db.Set<DeliveryFile>().AnyAsync(f => f.Id == fileId && f.ClientAccountId == task.ClientAccountId, ct))
            throw DeliveryRules.Invalid("file.invalid_attachment", "fileId", "Upload the file for this client first.");
        db.Set<TaskAttachment>().Add(new TaskAttachment { TaskId = taskId, FileId = fileId, AddedByUserId = currentUser.Id, CreatedAt = Now });
        await db.SaveChangesAsync(ct);
        return await GetAsync(taskId, ct);
    }

    public async Task<TaskDetailDto> DetachAsync(Guid taskId, Guid attachmentId, CancellationToken ct)
    {
        await LoadAsync(taskId, ct, tracked: false);
        var row = await db.Set<TaskAttachment>().FirstOrDefaultAsync(a => a.Id == attachmentId && a.TaskId == taskId, ct)
                  ?? throw DomainException.NotFound("Attachment");
        db.Remove(row);
        await db.SaveChangesAsync(ct);
        return await GetAsync(taskId, ct);
    }

    private void ConcurrencyTouch(ProjectTask task) => db.Entry(task).Property(x => x.UpdatedAt).IsModified = true;

    // ------------------------------------------------------------------ recurring rules

    public async Task<IReadOnlyList<RecurringRuleDto>> RecurringAsync(Guid projectId, CancellationToken ct)
    {
        await projects.LoadAsync(projectId, ct);
        var rules = await db.Set<RecurringTaskRule>().AsNoTracking().Where(r => r.ProjectId == projectId).OrderBy(r => r.DayOfMonth).ToListAsync(ct);
        var assigneeIds = rules.Where(r => r.AssigneeUserId != null).Select(r => r.AssigneeUserId!.Value).Distinct().ToList();
        var people = await db.Set<User>().AsNoTracking().Where(u => assigneeIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new PersonDto(u.Id, u.DisplayName, u.Email), ct);
        return rules.Select(r => new RecurringRuleDto(r.Id, r.ProjectId, r.Title, r.Description, r.DayOfMonth, r.DueInDays,
            r.AssigneeUserId is { } a && people.TryGetValue(a, out var p) ? p : null, r.EstimateHours, r.Labels, r.ClientVisible, r.IsActive)).ToList();
    }

    public async Task<IReadOnlyList<RecurringRuleDto>> SaveRecurringAsync(Guid projectId, Guid? ruleId, RecurringRuleRequest r, CancellationToken ct)
    {
        var project = await projects.LoadAsync(projectId, ct);
        if (r.AssigneeUserId is { } a && !(await StaffDirectory.ValidStaffAsync(db, new[] { a }, ct)).Contains(a))
            throw DeliveryRules.Invalid("task.invalid_assignee", "assigneeUserId", "Choose an active staff member.");
        RecurringTaskRule rule;
        if (ruleId is { } id)
            rule = await db.Set<RecurringTaskRule>().FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == projectId, ct) ?? throw DomainException.NotFound("RecurringTask");
        else
            db.Set<RecurringTaskRule>().Add(rule = new RecurringTaskRule { ProjectId = projectId, ClientAccountId = project.ClientAccountId });
        rule.Title = r.Title.Trim();
        rule.Description = r.Description?.Trim();
        rule.DayOfMonth = r.DayOfMonth;
        rule.DueInDays = r.DueInDays;
        rule.AssigneeUserId = r.AssigneeUserId;
        rule.EstimateHours = r.EstimateHours;
        rule.Labels = DeliveryRules.CleanList(r.Labels.Select(l => l.ToLowerInvariant()), 10, 40, "labels");
        rule.ClientVisible = r.ClientVisible;
        rule.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct);
        return await RecurringAsync(projectId, ct);
    }

    public async Task<IReadOnlyList<RecurringRuleDto>> DeleteRecurringAsync(Guid projectId, Guid ruleId, CancellationToken ct)
    {
        await projects.LoadAsync(projectId, ct);
        var rule = await db.Set<RecurringTaskRule>().FirstOrDefaultAsync(x => x.Id == ruleId && x.ProjectId == projectId, ct)
                   ?? throw DomainException.NotFound("RecurringTask");
        db.Remove(rule);
        await db.SaveChangesAsync(ct);
        return await RecurringAsync(projectId, ct);
    }
}
