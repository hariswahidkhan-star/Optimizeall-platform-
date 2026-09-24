using System.Globalization;
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

/// <summary>Creative briefs, client message threads (with read receipts) and client meetings.</summary>
public sealed class CommunicationService(
    AppDbContext db,
    IClientScope scope,
    ICurrentUser currentUser,
    IAuditLogger audit,
    INotificationService notifications,
    DeliveryFileService files,
    DeliveryLookup lookup,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ briefs

    public async Task<IReadOnlyList<BriefDto>> BriefsAsync(Guid? clientId, BriefStatus? status, CancellationToken ct)
    {
        var q = await scope.ApplyAsync(db.Set<Brief>().AsNoTracking(), b => b.ClientAccountId, ct);
        if (clientId is { } cid)
        {
            await scope.EnsureAccessAsync(cid, ct: ct);
            q = q.Where(b => b.ClientAccountId == cid);
        }
        if (status is { } s) q = q.Where(b => b.Status == s);
        return await BriefDtosAsync(await q.OrderByDescending(b => b.CreatedAt).Take(200).ToListAsync(ct), ct);
    }

    public async Task<BriefDto> BriefAsync(Guid id, Guid? clientId, CancellationToken ct)
    {
        var q = await scope.ApplyAsync(db.Set<Brief>().AsNoTracking(), b => b.ClientAccountId, ct);
        if (clientId is { } cid) q = q.Where(b => b.ClientAccountId == cid);
        var brief = await q.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw DomainException.NotFound("Brief");
        return (await BriefDtosAsync(new List<Brief> { brief }, ct)).Single();
    }

    private async Task<List<BriefDto>> BriefDtosAsync(List<Brief> rows, CancellationToken ct)
    {
        var clientIds = rows.Select(r => r.ClientAccountId).Distinct().ToList();
        var clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var projectIds = rows.Where(r => r.ProjectId != null).Select(r => r.ProjectId!.Value).Distinct().ToList();
        var projectNames = await db.Set<Project>().AsNoTracking().Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var templateKeys = rows.Select(r => r.TemplateKey).Distinct().ToList();
        var templates = await db.Set<BriefTemplate>().AsNoTracking().Where(t => templateKeys.Contains(t.Key)).ToDictionaryAsync(t => t.Key, t => t.Name, ct);
        var people = await lookup.PeopleAsync(rows.Select(r => r.SubmittedByUserId), ct);
        return rows.Select(b => new BriefDto(b.Id, b.ClientAccountId, clients.GetValueOrDefault(b.ClientAccountId) ?? "", b.ProjectId,
            b.ProjectId is { } p ? projectNames.GetValueOrDefault(p) : null, b.TemplateKey, templates.GetValueOrDefault(b.TemplateKey) ?? b.TemplateKey,
            b.Title, b.Status, b.Answers, b.Deadline,
            people.TryGetValue(b.SubmittedByUserId, out var person) ? new PersonDto(person.Id, person.DisplayName, person.Email) : new PersonDto(b.SubmittedByUserId, "Former user", ""),
            b.SubmittedByClient, b.CreatedAt, b.ConvertedAt, b.StaffNote, b.ConcurrencyStamp)).ToList();
    }

    /// <summary>Submits a brief (client Approver/Owner in the portal, or staff for a client). Answers are validated against the template.</summary>
    public async Task<BriefDto> SubmitBriefAsync(Guid clientId, SubmitBriefRequest r, bool fromClient, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, fromClient ? ClientMemberRole.Approver : ClientMemberRole.Viewer, ct);
        var template = await db.Set<BriefTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.Key == r.TemplateKey && t.IsActive, ct)
                       ?? throw DeliveryRules.Invalid("brief.invalid_template", "templateKey", "Choose a brief type.");
        if (r.ProjectId is { } pid && !await db.Set<Project>().AnyAsync(p => p.Id == pid && p.ClientAccountId == clientId, ct))
            throw DeliveryRules.Invalid("brief.invalid_project", "projectId", "That project wasn't found.");
        var answers = new List<BriefAnswer>();
        var errors = new Dictionary<string, string[]>();
        foreach (var field in template.Fields)
        {
            var value = r.Answers.TryGetValue(field.Key, out var v) ? v?.Trim() ?? "" : "";
            if (value.Length == 0)
            {
                if (field.Required) errors[$"answers.{field.Key}"] = new[] { $"{field.Label} is required." };
                continue;
            }
            var max = field.Type == BriefFieldType.LongText ? 5000 : field.Type == BriefFieldType.List ? 3000 : 500;
            if (value.Length > max) errors[$"answers.{field.Key}"] = new[] { $"{field.Label} is too long (max {max} characters)." };
            else if (field.Type == BriefFieldType.Url && value.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(u => !DeliveryRules.IsHttpUrl(u.Trim())))
                errors[$"answers.{field.Key}"] = new[] { $"{field.Label} must be full http(s) links, one per line." };
            else if (field.Type == BriefFieldType.Date && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                errors[$"answers.{field.Key}"] = new[] { $"{field.Label} must be a date." };
            else if (field.Type == BriefFieldType.Select && !field.Options.Contains(value))
                errors[$"answers.{field.Key}"] = new[] { $"Choose one of the options for {field.Label}." };
            answers.Add(new BriefAnswer(field.Key, field.Label, value));
        }
        if (errors.Count > 0) throw new DomainException("brief.invalid_answers", "Some answers need attention.", DomainErrorKind.Validation, errors);
        var brief = new Brief
        {
            ClientAccountId = clientId, ProjectId = r.ProjectId, TemplateKey = template.Key, Title = r.Title.Trim(), Answers = answers,
            Deadline = r.Deadline, SubmittedByUserId = currentUser.Id, SubmittedByClient = fromClient,
        };
        db.Set<Brief>().Add(brief);
        if (fromClient)
            foreach (var u in await lookup.ClientTeamAsync(clientId, ct))
                await notifications.StageAsync(new NotificationRequest(u, DeliveryNotificationTypes.BriefSubmitted, $"New brief: {brief.Title}",
                    $"A {template.Name} brief was submitted in the client portal.", DeliveryLinks.AgencyBrief(clientId, brief.Id),
                    new[] { NotificationChannel.Email }), ct);
        audit.Record("brief.submitted", nameof(Brief), brief.Id, after: new { brief.ClientAccountId, brief.TemplateKey, brief.Title, fromClient });
        await db.SaveChangesAsync(ct);
        return await BriefAsync(brief.Id, clientId, ct);
    }

    public async Task<BriefDto> SetBriefStatusAsync(Guid id, BriefStatusRequest r, CancellationToken ct)
    {
        var brief = await (await scope.ApplyAsync(db.Set<Brief>(), b => b.ClientAccountId, ct)).FirstOrDefaultAsync(b => b.Id == id, ct)
                    ?? throw DomainException.NotFound("Brief");
        DeliveryRules.EnsureStamp(brief, r.ConcurrencyStamp, db);
        var status = r.Status!.Value;
        if (status == BriefStatus.Converted) throw DeliveryRules.Invalid("brief.use_convert", "status", "Use Convert to turn a brief into work.");
        var before = brief.Status;
        brief.Status = status;
        if (!string.IsNullOrWhiteSpace(r.StaffNote)) brief.StaffNote = r.StaffNote.Trim();
        audit.Record("brief.status_changed", nameof(Brief), id, new { Status = before }, new { brief.Status }, brief.StaffNote);
        await db.SaveChangesAsync(ct);
        return await BriefAsync(id, null, ct);
    }

    /// <summary>Turns a brief into tasks and/or deliverables on a project of the same client.</summary>
    public async Task<BriefDto> ConvertBriefAsync(Guid id, ConvertBriefRequest r, CancellationToken ct)
    {
        var brief = await (await scope.ApplyAsync(db.Set<Brief>(), b => b.ClientAccountId, ct)).FirstOrDefaultAsync(b => b.Id == id, ct)
                    ?? throw DomainException.NotFound("Brief");
        DeliveryRules.EnsureStamp(brief, r.ConcurrencyStamp, db);
        if (brief.Status == BriefStatus.Converted) throw DomainException.Conflict("brief.already_converted", "This brief was already converted.");
        var project = await db.Set<Project>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == r.ProjectId && p.ClientAccountId == brief.ClientAccountId, ct)
                      ?? throw DeliveryRules.Invalid("brief.invalid_project", "projectId", "Choose a project of this client.");
        if (r.Tasks.Count + r.Deliverables.Count == 0)
            throw DeliveryRules.Invalid("brief.nothing_to_create", "tasks", "Add at least one task or deliverable.");
        if (r.Tasks.Count > 50 || r.Deliverables.Count > 20)
            throw DeliveryRules.Invalid("brief.too_many", "tasks", "At most 50 tasks and 20 deliverables per conversion.");
        var assignees = r.Tasks.Where(t => t.AssigneeUserId != null).Select(t => t.AssigneeUserId!.Value).Distinct().ToList();
        var valid = await StaffDirectory.ValidStaffAsync(db, assignees, ct);
        if (assignees.Any(a => !valid.Contains(a))) throw DeliveryRules.Invalid("brief.invalid_assignee", "tasks", "Assignees must be active staff.");
        var description = "From brief: " + brief.Title + "\n\n" + string.Join("\n", brief.Answers.Select(a => $"**{a.Label}:** {a.Value}"));
        var sort = (await db.Set<ProjectTask>().Where(t => t.ProjectId == project.Id && t.Status == ProjectTaskStatus.Todo).MaxAsync(t => (double?)t.SortOrder, ct) ?? 0) + 1000;
        foreach (var t in r.Tasks)
        {
            if (string.IsNullOrWhiteSpace(t.Title) || t.Title.Length > 300) throw DeliveryRules.Invalid("brief.invalid_task", "tasks", "Each task needs a title.");
            var task = new ProjectTask
            {
                ProjectId = project.Id, ClientAccountId = project.ClientAccountId, Title = t.Title.Trim(), Description = description,
                DueDate = t.DueDate ?? brief.Deadline, SortOrder = sort, BriefId = brief.Id, CreatedByUserId = currentUser.Id,
            };
            sort += 1000;
            db.Set<ProjectTask>().Add(task);
            if (t.AssigneeUserId is { } a)
            {
                db.Set<TaskAssignee>().Add(new TaskAssignee { TaskId = task.Id, UserId = a });
                db.Set<TaskWatcher>().Add(new TaskWatcher { TaskId = task.Id, UserId = a });
            }
        }
        foreach (var d in r.Deliverables)
        {
            if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Length > 300 || !Enum.IsDefined(d.Type))
                throw DeliveryRules.Invalid("brief.invalid_deliverable", "deliverables", "Each deliverable needs a title and type.");
            db.Set<Deliverable>().Add(new Deliverable
            {
                ClientAccountId = project.ClientAccountId, ProjectId = project.Id, Title = d.Title.Trim(), Type = d.Type,
                Description = $"From brief: {brief.Title}", OwnerUserId = currentUser.Id, CreatedByUserId = currentUser.Id,
            });
        }
        brief.Status = BriefStatus.Converted;
        brief.ConvertedAt = Now;
        brief.ProjectId = project.Id;
        if (!string.IsNullOrWhiteSpace(r.StaffNote)) brief.StaffNote = r.StaffNote.Trim();
        audit.Record("brief.converted", nameof(Brief), brief.Id, after: new { project.Id, Tasks = r.Tasks.Count, Deliverables = r.Deliverables.Count });
        await db.SaveChangesAsync(ct);
        return await BriefAsync(id, null, ct);
    }

    // ------------------------------------------------------------------ messages

    /// <summary>
    /// Who may post in the client's threads: staff, and client users with the Approver or Owner duty. Viewer and Billing
    /// members read only (docs/CLIENT_DELIVERY.md, "Who does what"); there are no billing-typed threads.
    /// </summary>
    private const ClientMemberRole ClientWriterDuty = ClientMemberRole.Approver;

    /// <summary>Threads of a client the caller may see: client users never get internal (staff-only) threads.</summary>
    private IQueryable<MessageThread> VisibleThreads(Guid clientId)
    {
        var q = db.Set<MessageThread>().Where(t => t.ClientAccountId == clientId);
        return scope.IsStaff ? q : q.Where(t => !t.IsInternal);
    }

    private async Task<bool> CanPostAsync(Guid clientId, CancellationToken ct)
    {
        if (scope.IsStaff) return true;
        var role = await scope.MemberRoleAsync(clientId, ct);
        return role is ClientMemberRole.Owner or ClientWriterDuty;
    }

    public async Task<IReadOnlyList<ThreadSummaryDto>> ThreadsAsync(Guid clientId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var me = currentUser.Id;
        var threads = await VisibleThreads(clientId).AsNoTracking()
            .OrderByDescending(t => t.LastMessageAt).Take(200).ToListAsync(ct);
        var ids = threads.Select(t => t.Id).ToList();
        var unread = await (from m in db.Set<ThreadMessage>().AsNoTracking()
                            join r in db.Set<ThreadReadState>().Where(x => x.UserId == me) on m.ThreadId equals r.ThreadId into rs
                            from r in rs.DefaultIfEmpty()
                            where ids.Contains(m.ThreadId) && m.AuthorUserId != me && (r == null || m.CreatedAt > r.LastReadAt)
                            group m by m.ThreadId into g
                            select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var people = await lookup.PeopleAsync(threads.Select(t => t.LastAuthorUserId), ct);
        return threads.Select(t => new ThreadSummaryDto(t.Id, t.ClientAccountId, t.Subject, t.ProjectId, t.LastMessageAt, t.MessageCount,
            unread.GetValueOrDefault(t.Id), t.LastMessagePreview,
            t.LastAuthorUserId is { } a && people.TryGetValue(a, out var p) ? p.DisplayName : null, t.IsInternal)).ToList();
    }

    /// <summary>Renames a thread (staff only; the controller is agency-side, scope enforces the client).</summary>
    public async Task<ThreadDto> RenameThreadAsync(Guid clientId, Guid threadId, RenameThreadRequest r, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var thread = await VisibleThreads(clientId).FirstOrDefaultAsync(t => t.Id == threadId, ct)
                     ?? throw DomainException.NotFound("Thread");
        var before = thread.Subject;
        thread.Subject = r.Subject.Trim();
        audit.Record("message.thread_renamed", nameof(MessageThread), threadId, new { Subject = before }, new { thread.Subject });
        await db.SaveChangesAsync(ct);
        return await ThreadAsync(clientId, threadId, ct);
    }

    /// <summary>Loads a thread and marks it read for the caller (read receipt). An internal thread is a 404 for client users.</summary>
    public async Task<ThreadDto> ThreadAsync(Guid clientId, Guid threadId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var thread = await VisibleThreads(clientId).AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId, ct)
                     ?? throw DomainException.NotFound("Thread");
        await MarkReadAsync(threadId, ct);
        var messages = await db.Set<ThreadMessage>().AsNoTracking().Where(m => m.ThreadId == threadId).OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var reads = await db.Set<ThreadReadState>().AsNoTracking().Where(r => r.ThreadId == threadId).ToListAsync(ct);
        var fileIds = messages.SelectMany(m => m.AttachmentFileIds).Distinct().ToList();
        var fileRows = await db.Set<DeliveryFile>().AsNoTracking().Where(f => fileIds.Contains(f.Id) && f.ClientAccountId == clientId)
            .ToDictionaryAsync(f => f.Id, ct);
        var people = await lookup.PeopleAsync(messages.Select(m => m.AuthorUserId).Concat(reads.Select(r => r.UserId)), ct);
        var hideEmails = !scope.IsStaff;
        PersonDto P(Guid id) => people.TryGetValue(id, out var p) ? new PersonDto(p.Id, p.DisplayName, hideEmails ? "" : p.Email) : new PersonDto(id, "Former user", "");
        return new ThreadDto(thread.Id, thread.ClientAccountId, thread.Subject, thread.ProjectId,
            messages.Select(m => new MessageDto(m.Id, P(m.AuthorUserId), m.FromClient, m.Body,
                m.AttachmentFileIds.Where(fileRows.ContainsKey).Select(f => DeliveryFileDto.From(fileRows[f])).ToList(), m.CreatedAt,
                reads.Where(r => r.UserId != m.AuthorUserId && r.LastReadAt >= m.CreatedAt).Select(r => P(r.UserId).DisplayName).OrderBy(n => n).ToList()))
                .ToList(),
            messages.Select(m => m.AuthorUserId).Distinct().Select(P).ToList(),
            thread.IsInternal, await CanPostAsync(clientId, ct));
    }

    private async Task MarkReadAsync(Guid threadId, CancellationToken ct)
    {
        var me = currentUser.Id;
        var state = await db.Set<ThreadReadState>().FirstOrDefaultAsync(r => r.ThreadId == threadId && r.UserId == me, ct);
        if (state is null) db.Set<ThreadReadState>().Add(new ThreadReadState { ThreadId = threadId, UserId = me, LastReadAt = Now });
        else state.LastReadAt = Now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent first read inserted the row; the receipt is already recorded.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Starts a thread. Client users need the Approver or Owner duty (403 <c>client.insufficient_role</c>) and can't
    /// start an internal thread (400 <c>message.internal_not_allowed</c>).
    /// </summary>
    public async Task<ThreadDto> NewThreadAsync(Guid clientId, NewThreadRequest r, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, scope.IsStaff ? ClientMemberRole.Viewer : ClientWriterDuty, ct);
        if (r.IsInternal && !scope.IsStaff)
            throw DeliveryRules.Invalid("message.internal_not_allowed", "isInternal", "Only your agency team can start internal conversations.");
        if (r.ProjectId is { } pid && !await db.Set<Project>().AnyAsync(p => p.Id == pid && p.ClientAccountId == clientId, ct))
            throw DeliveryRules.Invalid("message.invalid_project", "projectId", "That project wasn't found.");
        var thread = new MessageThread
        {
            ClientAccountId = clientId, ProjectId = r.ProjectId, Subject = r.Subject.Trim(), CreatedByUserId = currentUser.Id, LastMessageAt = Now,
            IsInternal = r.IsInternal,
        };
        db.Set<MessageThread>().Add(thread);
        if (thread.IsInternal)
            audit.Record("message.internal_thread_created", nameof(MessageThread), thread.Id, after: new { thread.ClientAccountId, thread.ProjectId, thread.Subject });
        await AddMessageAsync(thread, r.Body, r.AttachmentFileIds, ct);
        await db.SaveChangesAsync(ct);
        return await ThreadAsync(clientId, thread.Id, ct);
    }

    /// <summary>Replies in a thread. Client users need the Approver or Owner duty; an internal thread is a 404 for them.</summary>
    public async Task<ThreadDto> ReplyAsync(Guid clientId, Guid threadId, NewMessageRequest r, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var thread = await VisibleThreads(clientId).FirstOrDefaultAsync(t => t.Id == threadId, ct)
                     ?? throw DomainException.NotFound("Thread");
        // Duty check after the visibility check, so an internal thread stays a 404 (not a 403) for every client user.
        await scope.EnsureAccessAsync(clientId, scope.IsStaff ? ClientMemberRole.Viewer : ClientWriterDuty, ct);
        await AddMessageAsync(thread, r.Body, r.AttachmentFileIds, ct);
        await db.SaveChangesAsync(ct);
        return await ThreadAsync(clientId, threadId, ct);
    }

    /// <summary>
    /// Adds a message and notifies the other side: client messages go to the account team, staff messages to the client's
    /// users (in-app + email via the outbox). Messages in an internal thread notify only the account team, never the
    /// client's users. Attachments must be files of the same client.
    /// </summary>
    private async Task AddMessageAsync(MessageThread thread, string body, List<Guid> attachments, CancellationToken ct)
    {
        var ids = attachments.Distinct().ToList();
        if (ids.Count > 10) throw DeliveryRules.Invalid("message.too_many_attachments", "attachmentFileIds", "At most 10 attachments.");
        await files.EnsureFilesOfClientAsync(thread.ClientAccountId, ids, ct);
        var fromClient = !scope.IsStaff;
        var now = Now;
        db.Set<ThreadMessage>().Add(new ThreadMessage
        {
            ThreadId = thread.Id, ClientAccountId = thread.ClientAccountId, AuthorUserId = currentUser.Id, FromClient = fromClient,
            Body = body.Trim(), AttachmentFileIds = ids, CreatedAt = now,
        });
        thread.LastMessageAt = now;
        thread.MessageCount++;
        thread.LastAuthorUserId = currentUser.Id;
        thread.LastMessagePreview = body.Trim().Length > 140 ? body.Trim()[..140] + "…" : body.Trim();
        var author = await db.Set<User>().AsNoTracking().Where(u => u.Id == currentUser.Id).Select(u => u.DisplayName).FirstAsync(ct);
        var preview = body.Trim().Length > 200 ? body.Trim()[..200] + "…" : body.Trim();
        var toAgency = fromClient || thread.IsInternal;
        var recipients = toAgency
            ? await lookup.ClientTeamAsync(thread.ClientAccountId, ct)
            : await db.Set<ClientMember>().AsNoTracking().Where(m => m.ClientAccountId == thread.ClientAccountId).Select(m => m.UserId).ToListAsync(ct);
        var title = thread.IsInternal ? $"{author} (internal): {thread.Subject}" : $"{author}: {thread.Subject}";
        foreach (var u in recipients.Where(u => u != currentUser.Id))
            await notifications.StageAsync(new NotificationRequest(u, DeliveryNotificationTypes.Message, title, preview,
                toAgency ? DeliveryLinks.AgencyThread(thread.ClientAccountId, thread.Id) : DeliveryLinks.ClientThread(thread.ClientAccountId, thread.Id),
                new[] { NotificationChannel.Email }), ct);
        // The author has read everything up to their own message.
        var state = await db.Set<ThreadReadState>().FirstOrDefaultAsync(r => r.ThreadId == thread.Id && r.UserId == currentUser.Id, ct);
        if (state is null) db.Set<ThreadReadState>().Add(new ThreadReadState { ThreadId = thread.Id, UserId = currentUser.Id, LastReadAt = now });
        else state.LastReadAt = now;
    }

    // ------------------------------------------------------------------ meetings

    public async Task<IReadOnlyList<MeetingDto>> MeetingsAsync(MeetingQuery query, CancellationToken ct)
    {
        var q = await scope.ApplyAsync(db.Set<Meeting>().AsNoTracking(), m => m.ClientAccountId, ct);
        if (query.ClientId is { } cid)
        {
            await scope.EnsureAccessAsync(cid, ct: ct);
            q = q.Where(m => m.ClientAccountId == cid);
        }
        if (query.From is { } from) q = q.Where(m => m.StartsAt >= from);
        if (query.To is { } to) q = q.Where(m => m.StartsAt < to);
        return await MeetingDtosAsync(await q.OrderBy(m => m.StartsAt).Take(300).ToListAsync(ct), ct);
    }

    internal async Task<List<MeetingDto>> MeetingDtosAsync(List<Meeting> rows, CancellationToken ct)
    {
        var clientIds = rows.Select(r => r.ClientAccountId).Distinct().ToList();
        var clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var people = await lookup.PeopleAsync(rows.SelectMany(r => r.AttendeeUserIds), ct);
        var hideEmails = !scope.IsStaff;
        return rows.Select(m => new MeetingDto(m.Id, m.ClientAccountId, clients.GetValueOrDefault(m.ClientAccountId) ?? "", m.ProjectId, m.Title,
            m.Kind, m.StartsAt, m.DurationMinutes, m.Location, m.Agenda, hideEmails ? null : m.Notes, m.Status,
            m.AttendeeUserIds.Where(people.ContainsKey).Select(a => new PersonDto(a, people[a].DisplayName, hideEmails ? "" : people[a].Email)).ToList(),
            hideEmails ? Array.Empty<MeetingActionItem>() : m.ActionItems, m.ConcurrencyStamp)).ToList();
    }

    public async Task<MeetingDto> SaveMeetingAsync(Guid? id, MeetingRequest r, CancellationToken ct)
    {
        var clientId = r.ClientId!.Value;
        await scope.EnsureAccessAsync(clientId, ct: ct);
        if (r.ProjectId is { } pid && !await db.Set<Project>().AnyAsync(p => p.Id == pid && p.ClientAccountId == clientId, ct))
            throw DeliveryRules.Invalid("meeting.invalid_project", "projectId", "That project belongs to another client.");
        var attendees = r.AttendeeUserIds.Distinct().ToList();
        if (attendees.Count > 50) throw DeliveryRules.Invalid("meeting.too_many_attendees", "attendeeUserIds", "At most 50 attendees.");
        var staff = await StaffDirectory.ValidStaffAsync(db, attendees, ct);
        var members = await db.Set<ClientMember>().AsNoTracking().Where(m => m.ClientAccountId == clientId && attendees.Contains(m.UserId)).Select(m => m.UserId).ToListAsync(ct);
        if (attendees.Any(a => !staff.Contains(a) && !members.Contains(a)))
            throw DeliveryRules.Invalid("meeting.invalid_attendee", "attendeeUserIds", "Attendees must be staff or users of this client.");
        if (r.ActionItems.Count > 50 || r.ActionItems.Any(a => string.IsNullOrWhiteSpace(a.Text) || a.Text.Length > 500))
            throw DeliveryRules.Invalid("meeting.invalid_action_items", "actionItems", "Each action item needs text (max 500 characters).");

        Meeting meeting;
        if (id is { } existing)
        {
            meeting = await db.Set<Meeting>().FirstOrDefaultAsync(m => m.Id == existing && m.ClientAccountId == clientId, ct) ?? throw DomainException.NotFound("Meeting");
            DeliveryRules.EnsureStamp(meeting, r.ConcurrencyStamp ?? Guid.Empty, db);
        }
        else
        {
            db.Set<Meeting>().Add(meeting = new Meeting { ClientAccountId = clientId, CreatedByUserId = currentUser.Id });
        }
        var previous = meeting.ActionItems.ToDictionary(a => a.Id);
        meeting.ProjectId = r.ProjectId;
        meeting.Title = r.Title.Trim();
        meeting.Kind = r.Kind;
        meeting.StartsAt = DateTime.SpecifyKind(r.StartsAt!.Value.ToUniversalTime(), DateTimeKind.Utc);
        meeting.DurationMinutes = r.DurationMinutes;
        meeting.Location = r.Location?.Trim();
        meeting.Agenda = r.Agenda?.Trim();
        meeting.Notes = r.Notes?.Trim();
        meeting.Status = r.Status;
        meeting.AttendeeUserIds = attendees;
        meeting.ActionItems = r.ActionItems.Select(a => new MeetingActionItem(a.Id ?? Guid.NewGuid(), a.Text.Trim(), a.AssigneeUserId, a.DueDate,
            a.Id is { } aid && previous.TryGetValue(aid, out var p) ? p.TaskId : null)).ToList();
        audit.Record(id is null ? "meeting.created" : "meeting.updated", nameof(Meeting), meeting.Id,
            after: new { meeting.Title, meeting.Status, meeting.StartsAt, ActionItems = meeting.ActionItems.Count });
        await db.SaveChangesAsync(ct);
        return (await MeetingDtosAsync(new List<Meeting> { meeting }, ct)).Single();
    }

    /// <summary>Turns a meeting action item into a task on a project of the same client (once).</summary>
    public async Task<MeetingDto> ConvertActionItemAsync(Guid meetingId, Guid itemId, ConvertActionItemRequest r, CancellationToken ct)
    {
        var meeting = await (await scope.ApplyAsync(db.Set<Meeting>(), m => m.ClientAccountId, ct)).FirstOrDefaultAsync(m => m.Id == meetingId, ct)
                      ?? throw DomainException.NotFound("Meeting");
        var item = meeting.ActionItems.FirstOrDefault(a => a.Id == itemId) ?? throw DomainException.NotFound("ActionItem");
        if (item.TaskId is not null) throw DomainException.Conflict("meeting.already_converted", "This action item already has a task.");
        var project = await db.Set<Project>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == r.ProjectId && p.ClientAccountId == meeting.ClientAccountId, ct)
                      ?? throw DeliveryRules.Invalid("meeting.invalid_project", "projectId", "Choose a project of this client.");
        var sort = (await db.Set<ProjectTask>().Where(t => t.ProjectId == project.Id && t.Status == ProjectTaskStatus.Todo).MaxAsync(t => (double?)t.SortOrder, ct) ?? 0) + 1000;
        var task = new ProjectTask
        {
            ProjectId = project.Id, ClientAccountId = project.ClientAccountId, Title = item.Text.Length > 300 ? item.Text[..300] : item.Text,
            Description = $"Action item from meeting “{meeting.Title}” on {meeting.StartsAt:yyyy-MM-dd}.", DueDate = item.DueDate, SortOrder = sort,
            MeetingId = meeting.Id, CreatedByUserId = currentUser.Id,
        };
        db.Set<ProjectTask>().Add(task);
        if (item.AssigneeUserId is { } a && (await StaffDirectory.ValidStaffAsync(db, new[] { a }, ct)).Contains(a))
        {
            db.Set<TaskAssignee>().Add(new TaskAssignee { TaskId = task.Id, UserId = a });
            db.Set<TaskWatcher>().Add(new TaskWatcher { TaskId = task.Id, UserId = a });
        }
        meeting.ActionItems = meeting.ActionItems.Select(x => x.Id == itemId ? x with { TaskId = task.Id } : x).ToList();
        await db.SaveChangesAsync(ct);
        return (await MeetingDtosAsync(new List<Meeting> { meeting }, ct)).Single();
    }
}
