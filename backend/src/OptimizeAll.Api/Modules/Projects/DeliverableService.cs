using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Clients;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Projects;

/// <summary>
/// Deliverables and their approval workflow: Draft → InternalReview → ClientReview → ChangesRequested/Approved → Published.
/// Client decisions are conditional updates on (status = ClientReview AND current version = the version the client saw), so
/// an outdated version can never be approved and of two concurrent decisions exactly one wins; the winner alone publishes
/// <see cref="DeliverableApproved"/>.
/// </summary>
public sealed class DeliverableService(
    AppDbContext db,
    IClientScope scope,
    ICurrentUser currentUser,
    IAuditLogger audit,
    INotificationService notifications,
    IEventPublisher events,
    IDatabaseDialect dialect,
    DeliveryFileService files,
    ProjectService projects,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<Deliverable> LoadAsync(Guid id, CancellationToken ct, bool tracked = true, Guid? clientId = null)
    {
        var q = tracked ? db.Set<Deliverable>() : db.Set<Deliverable>().AsNoTracking();
        q = await scope.ApplyAsync(q, d => d.ClientAccountId, ct);
        if (clientId is { } cid) q = q.Where(d => d.ClientAccountId == cid && d.LastSentVersion > 0);
        return await q.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw DomainException.NotFound("Deliverable");
    }

    // ------------------------------------------------------------------ queries

    public async Task<PagedResult<DeliverableSummaryDto>> ListAsync(DeliverableListQuery query, CancellationToken ct)
    {
        var q = await scope.ApplyAsync(db.Set<Deliverable>().AsNoTracking(), d => d.ClientAccountId, ct);
        if (query.ClientId is { } cid) q = q.Where(d => d.ClientAccountId == cid);
        if (query.ProjectId is { } pid) q = q.Where(d => d.ProjectId == pid);
        if (query.Status is { } s) q = q.Where(d => d.Status == s);
        var me = currentUser.Id;
        q = query.View switch
        {
            "review" => currentUser.HasPermission(Permissions.ProjectsManage)
                ? q.Where(d => d.Status == DeliverableStatus.InternalReview && (d.ReviewerUserId == null || d.ReviewerUserId == me))
                : q.Where(d => d.Status == DeliverableStatus.InternalReview && d.ReviewerUserId == me),
            "client" => q.Where(d => d.Status == DeliverableStatus.ClientReview),
            "mine" => q.Where(d => d.OwnerUserId == me),
            _ => q,
        };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(d => EF.Functions.Like(d.Title, p, "\\"));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(d => d.UpdatedAt).ThenByDescending(d => d.Id).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<DeliverableSummaryDto>(await SummariesAsync(rows, ct), total, query.Page, query.PageSize);
    }

    internal async Task<List<DeliverableSummaryDto>> SummariesAsync(List<Deliverable> rows, CancellationToken ct)
    {
        var projectIds = rows.Select(r => r.ProjectId).Distinct().ToList();
        var projectNames = await db.Set<Project>().AsNoTracking().Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var clientIds = rows.Select(r => r.ClientAccountId).Distinct().ToList();
        var clientNames = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var peopleIds = rows.SelectMany(r => new[] { r.OwnerUserId, r.ReviewerUserId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        var people = await db.Set<User>().AsNoTracking().Where(u => peopleIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new PersonDto(u.Id, u.DisplayName, u.Email), ct);
        var now = Now;
        return rows.Select(d => new DeliverableSummaryDto(d.Id, d.ClientAccountId, clientNames.GetValueOrDefault(d.ClientAccountId) ?? "",
            d.ProjectId, projectNames.GetValueOrDefault(d.ProjectId) ?? "", d.TaskId, d.Title, d.Type, d.Status, d.CurrentVersion,
            d.OwnerUserId is { } o && people.TryGetValue(o, out var owner) ? owner : null,
            d.ReviewerUserId is { } r && people.TryGetValue(r, out var reviewer) ? reviewer : null,
            d.SentToClientAt, d.ClientDueAt, d.Status == DeliverableStatus.ClientReview && d.ClientDueAt < now, d.ApprovedAt, d.UpdatedAt,
            d.ConcurrencyStamp)).ToList();
    }

    public async Task<DeliverableDetailDto> GetAsync(Guid id, CancellationToken ct) =>
        await DetailAsync(await LoadAsync(id, ct, tracked: false), forClient: false, ct);

    private async Task<DeliverableDetailDto> DetailAsync(Deliverable d, bool forClient, CancellationToken ct)
    {
        var summary = (await SummariesAsync(new List<Deliverable> { d }, ct)).Single();
        var vq = db.Set<DeliverableVersion>().AsNoTracking().Where(v => v.DeliverableId == d.Id);
        if (forClient) vq = vq.Where(v => v.Number <= d.LastSentVersion);
        var versions = await vq.OrderByDescending(v => v.Number).ToListAsync(ct);
        var fileIds = versions.Where(v => v.FileId != null).Select(v => v.FileId!.Value).ToList();
        var fileRows = await db.Set<DeliveryFile>().AsNoTracking().Where(f => fileIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var cq = db.Set<DeliverableComment>().AsNoTracking().Where(c => c.DeliverableId == d.Id);
        // Internal comments never leave the database for a client request.
        if (forClient) cq = cq.Where(c => !c.IsInternal && c.VersionNumber <= d.LastSentVersion);
        var comments = await cq.OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var hq = db.Set<DeliverableReview>().AsNoTracking().Where(r => r.DeliverableId == d.Id);
        if (forClient) hq = hq.Where(r => r.Stage != ReviewStage.Internal);
        var history = await hq.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        var ids = versions.Select(v => v.CreatedByUserId).Concat(comments.Select(c => c.AuthorUserId))
            .Concat(d.ApprovedByUserId is { } a ? new[] { a } : Array.Empty<Guid>()).Distinct().ToList();
        var people = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new PersonDto(u.Id, u.DisplayName, forClient ? string.Empty : u.Email), ct);
        PersonDto P(Guid uid) => people.TryGetValue(uid, out var p) ? p : new PersonDto(uid, "Former user", "");

        var dto = new DeliverableDetailDto(
            forClient ? summary with { Owner = null, Reviewer = null } : summary, d.Description,
            versions.Select(v => new DeliverableVersionDto(v.Id, v.Number,
                v.FileId is { } f && fileRows.TryGetValue(f, out var file) ? DeliveryFileDto.From(file) : null,
                v.LinkUrl, v.Body, v.Notes, P(v.CreatedByUserId), v.CreatedAt)).ToList(),
            comments.Select(c => new DeliverableCommentDto(c.Id, c.VersionNumber, P(c.AuthorUserId), c.FromClient, c.IsInternal, c.Body, c.CreatedAt)).ToList(),
            history.Select(h => new DeliverableReviewDto(h.Id, h.VersionNumber, h.Stage, h.Decision, h.UserName, h.Comment, h.CreatedAt)).ToList(),
            d.ApprovedVersion, d.ApprovedByUserId is { } approver ? P(approver).DisplayName : null, d.AutoApproved,
            forClient ? await ClientActionsAsync(d, ct) : StaffActions(d));
        return dto;
    }

    private List<string> StaffActions(Deliverable d)
    {
        var actions = new List<string> { "comment" };
        var canManage = currentUser.HasPermission(Permissions.ProjectsManage);
        var canSubmit = currentUser.HasPermission(Permissions.DeliverablesSubmit);
        if (canSubmit && DeliverableWorkflow.CanAddVersion(d.Status)) actions.Add("addVersion");
        if (canSubmit && DeliverableWorkflow.CanSubmitForInternalReview(d.Status, d.CurrentVersion)) actions.Add("submit");
        if (d.Status == DeliverableStatus.InternalReview && (canManage || d.ReviewerUserId == currentUser.Id))
        {
            actions.Add("internalApprove");
            actions.Add("internalRequestChanges");
        }
        if (d.Status == DeliverableStatus.Approved && canManage) actions.Add("publish");
        return actions;
    }

    private async Task<List<string>> ClientActionsAsync(Deliverable d, CancellationToken ct)
    {
        var role = await scope.MemberRoleAsync(d.ClientAccountId, ct);
        var canAct = role is ClientMemberRole.Owner or ClientMemberRole.Approver;
        var actions = new List<string>();
        if (!canAct) return actions;
        actions.Add("comment");
        if (d.Status == DeliverableStatus.ClientReview)
        {
            actions.Add("approve");
            actions.Add("requestChanges");
        }
        if (d.Status is DeliverableStatus.Approved or DeliverableStatus.Published &&
            !await db.Set<ClientFeedback>().AnyAsync(f => f.DedupeKey == $"csat:{d.Id}:{currentUser.Id}", ct))
            actions.Add("rate");
        return actions;
    }

    // ------------------------------------------------------------------ staff commands

    public async Task<DeliverableDetailDto> CreateAsync(CreateDeliverableRequest r, CancellationToken ct)
    {
        var project = await projects.LoadAsync(r.ProjectId!.Value, ct);
        var type = r.Type!.Value;
        if (!Enum.IsDefined(type)) throw DeliveryRules.Invalid("deliverable.invalid_type", "type", "Choose a deliverable type.");
        if (r.TaskId is { } tid && !await db.Set<ProjectTask>().AnyAsync(t => t.Id == tid && t.ProjectId == project.Id, ct))
            throw DeliveryRules.Invalid("deliverable.invalid_task", "taskId", "That task isn't part of this project.");
        await ValidatePeopleAsync(r.OwnerUserId, r.ReviewerUserId, ct);
        var d = new Deliverable
        {
            ClientAccountId = project.ClientAccountId, ProjectId = project.Id, TaskId = r.TaskId, Title = r.Title.Trim(),
            Description = r.Description?.Trim(), Type = type, OwnerUserId = r.OwnerUserId ?? currentUser.Id, ReviewerUserId = r.ReviewerUserId,
            CreatedByUserId = currentUser.Id,
        };
        db.Set<Deliverable>().Add(d);
        audit.Record("deliverable.created", nameof(Deliverable), d.Id, after: new { d.Title, d.Type, d.ProjectId });
        await db.SaveChangesAsync(ct);
        return await GetAsync(d.Id, ct);
    }

    public async Task<DeliverableDetailDto> UpdateAsync(Guid id, UpdateDeliverableRequest r, CancellationToken ct)
    {
        var d = await LoadAsync(id, ct);
        DeliveryRules.EnsureStamp(d, r.ConcurrencyStamp, db);
        await ValidatePeopleAsync(r.OwnerUserId, r.ReviewerUserId, ct);
        var before = new { d.Title, d.OwnerUserId, d.ReviewerUserId };
        d.Title = r.Title.Trim();
        d.Description = r.Description?.Trim();
        d.OwnerUserId = r.OwnerUserId;
        d.ReviewerUserId = r.ReviewerUserId;
        audit.Record("deliverable.updated", nameof(Deliverable), id, before, new { d.Title, d.OwnerUserId, d.ReviewerUserId });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Deletes a deliverable that was never sent to the client (its versions, internal comments and review log go with it).
    /// Once the client has seen a version, the deliverable is part of the approval record and can't be deleted.
    /// </summary>
    public async Task DeleteAsync(Guid id, Guid? stamp, CancellationToken ct)
    {
        var d = await LoadAsync(id, ct);
        if (stamp is null) throw DeliveryRules.Invalid("concurrency.stamp_required", "concurrencyStamp", "Reload the deliverable and try again.");
        DeliveryRules.EnsureStamp(d, stamp, db);
        if (d.LastSentVersion > 0 || !DeliverableWorkflow.IsOpen(d.Status))
            throw DomainException.Conflict("deliverable.not_deletable",
                "The client has already seen this deliverable, so it stays in the approval record. Add a new version instead.");
        db.Remove(d);
        audit.Record("deliverable.deleted", nameof(Deliverable), id, before: new { d.Title, d.ProjectId, d.Status, d.CurrentVersion });
        await db.SaveChangesAsync(ct);
    }

    private async Task ValidatePeopleAsync(Guid? owner, Guid? reviewer, CancellationToken ct)
    {
        var ids = new[] { owner, reviewer }.Where(x => x != null).Select(x => x!.Value).ToList();
        var valid = await StaffDirectory.ValidStaffAsync(db, ids, ct);
        if (ids.Any(i => !valid.Contains(i))) throw DeliveryRules.Invalid("deliverable.invalid_person", "ownerUserId", "Owner and reviewer must be active staff.");
    }

    /// <summary>Adds a version (file, link and/or text). After changes were requested, the deliverable returns to Draft.</summary>
    public async Task<DeliverableDetailDto> AddVersionAsync(Guid id, DeliverableVersionForm form, CancellationToken ct)
    {
        var d = await LoadAsync(id, ct);
        if (!DeliverableWorkflow.CanAddVersion(d.Status))
            throw DomainException.Conflict("deliverable.locked", $"A {d.Status} deliverable can't take new versions.");
        var link = string.IsNullOrWhiteSpace(form.LinkUrl) ? null : form.LinkUrl.Trim();
        if (link is not null && !DeliveryRules.IsHttpUrl(link))
            throw DeliveryRules.Invalid("deliverable.invalid_link", "linkUrl", "Enter a full http(s) link.");
        var body = string.IsNullOrWhiteSpace(form.Body) ? null : form.Body;
        if (form.File is null && link is null && body is null)
            throw DeliveryRules.Invalid("deliverable.empty_version", "file", "Upload a file, add a link or paste the content.");
        DeliveryFile? file = form.File is null ? null : await files.SaveAsync(d.ClientAccountId, form.File, ct);
        try
        {
            d.CurrentVersion++;
            db.Set<DeliverableVersion>().Add(new DeliverableVersion
            {
                DeliverableId = d.Id, ClientAccountId = d.ClientAccountId, Number = d.CurrentVersion, FileId = file?.Id, LinkUrl = link,
                Body = body, Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim(), CreatedByUserId = currentUser.Id, CreatedAt = Now,
            });
            if (d.Status == DeliverableStatus.ChangesRequested) d.Status = DeliverableStatus.Draft;
            audit.Record("deliverable.version_added", nameof(Deliverable), d.Id, after: new { Version = d.CurrentVersion, file?.ContentType, link });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            if (file is not null) files.Discard(file);
            throw;
        }
        return await GetAsync(id, ct);
    }

    public async Task<DeliverableDetailDto> SubmitAsync(Guid id, DeliverableActionRequest r, CancellationToken ct)
    {
        var d = await LoadAsync(id, ct);
        EnsureVersion(d, r.Version!.Value);
        var next = DeliverableWorkflow.CanSubmitForInternalReview(d.Status, d.CurrentVersion) ? DeliverableWorkflow.Next(d.Status, DeliverableAction.Submit) : null;
        d.Status = next ?? throw InvalidTransition(d);
        await RecordAsync(d, ReviewStage.Internal, ReviewDecision.Submitted, r.Comment, ct);
        var reviewers = d.ReviewerUserId is { } rev ? new List<Guid> { rev } : await ManagersOnTeamAsync(d, ct);
        foreach (var u in reviewers.Where(u => u != currentUser.Id))
            await notifications.StageAsync(new NotificationRequest(u, DeliveryNotificationTypes.DeliverableInternalReview,
                $"Review needed: {d.Title}", $"Version {d.CurrentVersion} is ready for internal review.", DeliveryLinks.AgencyDeliverable(d.Id),
                new[] { NotificationChannel.Email }), ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Internal approval sends the version to the client (ClientReview) with the client's feedback SLA.</summary>
    public async Task<DeliverableDetailDto> InternalApproveAsync(Guid id, DeliverableActionRequest r, CancellationToken ct)
    {
        var d = await LoadAsync(id, ct);
        EnsureReviewer(d);
        EnsureVersion(d, r.Version!.Value);
        d.Status = DeliverableWorkflow.Next(d.Status, DeliverableAction.InternalApprove) ?? throw InvalidTransition(d);
        var client = await db.Set<ClientAccount>().AsNoTracking().FirstAsync(c => c.Id == d.ClientAccountId, ct);
        d.SentToClientAt = Now;
        d.ClientDueAt = DeliverableWorkflow.DueAt(Now, client.ApprovalSlaDays);
        d.LastSentVersion = d.CurrentVersion;
        await RecordAsync(d, ReviewStage.Internal, ReviewDecision.InternalApproved, r.Comment, ct);
        audit.Record("deliverable.sent_to_client", nameof(Deliverable), d.Id, after: new { Version = d.CurrentVersion, d.ClientDueAt });
        var approvers = await db.Set<ClientMember>().AsNoTracking()
            .Where(m => m.ClientAccountId == d.ClientAccountId && (m.Role == ClientMemberRole.Approver || m.Role == ClientMemberRole.Owner))
            .Select(m => m.UserId).ToListAsync(ct);
        foreach (var u in approvers)
            await notifications.StageAsync(new NotificationRequest(u, DeliveryNotificationTypes.DeliverableAwaitingClient,
                $"Ready for your review: {d.Title}",
                $"Version {d.CurrentVersion} is ready for your approval. Please review it by {d.ClientDueAt:ddd d MMM}.",
                DeliveryLinks.ClientDeliverable(d.ClientAccountId, d.Id), new[] { NotificationChannel.Email }), ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<DeliverableDetailDto> InternalRequestChangesAsync(Guid id, DeliverableActionRequest r, CancellationToken ct)
    {
        var d = await LoadAsync(id, ct);
        EnsureReviewer(d);
        EnsureVersion(d, r.Version!.Value);
        if (string.IsNullOrWhiteSpace(r.Comment)) throw DeliveryRules.Invalid("deliverable.comment_required", "comment", "Say what needs to change.");
        d.Status = DeliverableWorkflow.Next(d.Status, DeliverableAction.InternalRequestChanges) ?? throw InvalidTransition(d);
        await RecordAsync(d, ReviewStage.Internal, ReviewDecision.InternalChangesRequested, r.Comment, ct);
        db.Set<DeliverableComment>().Add(new DeliverableComment
        {
            DeliverableId = d.Id, ClientAccountId = d.ClientAccountId, VersionNumber = d.CurrentVersion, AuthorUserId = currentUser.Id,
            IsInternal = true, Body = r.Comment.Trim(), CreatedAt = Now,
        });
        if (d.OwnerUserId is { } owner && owner != currentUser.Id)
            await notifications.StageAsync(new NotificationRequest(owner, DeliveryNotificationTypes.DeliverableInternalReview,
                $"Changes requested internally: {d.Title}", r.Comment.Trim(), DeliveryLinks.AgencyDeliverable(d.Id)), ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<DeliverableDetailDto> PublishAsync(Guid id, DeliverableActionRequest r, CancellationToken ct)
    {
        var d = await LoadAsync(id, ct);
        EnsureVersion(d, r.Version!.Value);
        d.Status = DeliverableWorkflow.Next(d.Status, DeliverableAction.Publish) ?? throw InvalidTransition(d);
        d.PublishedAt = Now;
        await RecordAsync(d, ReviewStage.Internal, ReviewDecision.Published, r.Comment, ct);
        audit.Record("deliverable.published", nameof(Deliverable), d.Id, after: new { Version = d.CurrentVersion });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<DeliverableDetailDto> StaffCommentAsync(Guid id, DeliverableCommentRequest r, CancellationToken ct)
    {
        var d = await LoadAsync(id, ct, tracked: false);
        if (r.Version!.Value > d.CurrentVersion) throw DeliveryRules.Invalid("deliverable.invalid_version", "version", "That version doesn't exist.");
        db.Set<DeliverableComment>().Add(new DeliverableComment
        {
            DeliverableId = d.Id, ClientAccountId = d.ClientAccountId, VersionNumber = r.Version.Value, AuthorUserId = currentUser.Id,
            IsInternal = r.IsInternal || r.Version.Value > d.LastSentVersion, Body = r.Body.Trim(), CreatedAt = Now,
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private void EnsureReviewer(Deliverable d)
    {
        if (!currentUser.HasPermission(Permissions.ProjectsManage) && d.ReviewerUserId != currentUser.Id)
            throw DomainException.Forbidden("deliverable.not_reviewer", "Only the assigned reviewer or a project manager can review this deliverable.");
    }

    private static void EnsureVersion(Deliverable d, int version)
    {
        if (version != d.CurrentVersion)
            throw DomainException.Conflict("deliverable.stale_version", $"Version {version} is outdated; the latest version is {d.CurrentVersion}. Reload to review it.");
    }

    private static DomainException InvalidTransition(Deliverable d) =>
        DomainException.Conflict("deliverable.invalid_transition", $"That action isn't possible while the deliverable is {d.Status}.");

    private async Task<List<Guid>> ManagersOnTeamAsync(Deliverable d, CancellationToken ct)
    {
        var project = await db.Set<Project>().AsNoTracking().FirstAsync(p => p.Id == d.ProjectId, ct);
        var am = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == d.ClientAccountId).Select(c => c.AccountManagerUserId).FirstAsync(ct);
        return new[] { project.OwnerUserId, am }.Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
    }

    private async Task RecordAsync(Deliverable d, ReviewStage stage, ReviewDecision decision, string? comment, CancellationToken ct)
    {
        var name = await db.Set<User>().AsNoTracking().Where(u => u.Id == currentUser.Id).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        db.Set<DeliverableReview>().Add(new DeliverableReview
        {
            DeliverableId = d.Id, ClientAccountId = d.ClientAccountId, VersionNumber = d.CurrentVersion, Stage = stage, Decision = decision,
            UserId = currentUser.Id, UserName = name, Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), CreatedAt = Now,
        });
    }

    // ------------------------------------------------------------------ client portal

    public async Task<IReadOnlyList<DeliverableSummaryDto>> ClientListAsync(Guid clientId, string? view, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var q = db.Set<Deliverable>().AsNoTracking().Where(d => d.ClientAccountId == clientId && d.LastSentVersion > 0);
        q = view switch
        {
            "awaiting" => q.Where(d => d.Status == DeliverableStatus.ClientReview),
            "approved" => q.Where(d => d.Status == DeliverableStatus.Approved || d.Status == DeliverableStatus.Published),
            _ => q,
        };
        var rows = await q.OrderByDescending(d => d.Status == DeliverableStatus.ClientReview).ThenByDescending(d => d.UpdatedAt).Take(200).ToListAsync(ct);
        return (await SummariesAsync(rows, ct)).Select(s => s with { Owner = null, Reviewer = null }).ToList();
    }

    public async Task<DeliverableDetailDto> ClientGetAsync(Guid clientId, Guid id, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        return await DetailAsync(await LoadAsync(id, ct, tracked: false, clientId: clientId), forClient: true, ct);
    }

    public async Task<DeliverableDetailDto> ClientCommentAsync(Guid clientId, Guid id, DeliverableCommentRequest r, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ClientMemberRole.Approver, ct);
        var d = await LoadAsync(id, ct, tracked: false, clientId: clientId);
        if (r.Version!.Value > d.LastSentVersion) throw DeliveryRules.Invalid("deliverable.invalid_version", "version", "That version doesn't exist.");
        db.Set<DeliverableComment>().Add(new DeliverableComment
        {
            DeliverableId = d.Id, ClientAccountId = d.ClientAccountId, VersionNumber = r.Version.Value, AuthorUserId = currentUser.Id,
            FromClient = true, Body = r.Body.Trim(), CreatedAt = Now,
        });
        await NotifyTeamAsync(d, $"New comment on {d.Title}", r.Body.Trim(), ct);
        await db.SaveChangesAsync(ct);
        return await ClientGetAsync(clientId, id, ct);
    }

    /// <summary>Approver/Owner approves the version they reviewed (must be the current version).</summary>
    public Task<DeliverableDetailDto> ClientApproveAsync(Guid clientId, Guid id, DeliverableActionRequest r, CancellationToken ct) =>
        ClientDecideAsync(clientId, id, r, DeliverableAction.ClientApprove, ct);

    public Task<DeliverableDetailDto> ClientRequestChangesAsync(Guid clientId, Guid id, DeliverableActionRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Comment)) throw DeliveryRules.Invalid("deliverable.comment_required", "comment", "Tell the team what to change.");
        return ClientDecideAsync(clientId, id, r, DeliverableAction.ClientRequestChanges, ct);
    }

    private async Task<DeliverableDetailDto> ClientDecideAsync(Guid clientId, Guid id, DeliverableActionRequest r, DeliverableAction action, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ClientMemberRole.Approver, ct);
        var d = await LoadAsync(id, ct, tracked: false, clientId: clientId);
        var version = r.Version!.Value;
        var approve = action == DeliverableAction.ClientApprove;
        var next = DeliverableWorkflow.Next(DeliverableStatus.ClientReview, action)!.Value;
        var now = Now;
        var me = currentUser.Id;
        var name = await db.Set<User>().AsNoTracking().Where(u => u.Id == me).Select(u => u.DisplayName).FirstAsync(ct);

        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            // The decision only lands if the deliverable is still awaiting the client on exactly this version.
            var updated = approve
                ? await db.Set<Deliverable>()
                    .Where(x => x.Id == id && x.ClientAccountId == clientId && x.Status == DeliverableStatus.ClientReview && x.CurrentVersion == version)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, next).SetProperty(x => x.ApprovedAt, now)
                        .SetProperty(x => x.ApprovedVersion, version).SetProperty(x => x.ApprovedByUserId, me)
                        .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.ConcurrencyStamp, Guid.NewGuid()), ct)
                : await db.Set<Deliverable>()
                    .Where(x => x.Id == id && x.ClientAccountId == clientId && x.Status == DeliverableStatus.ClientReview && x.CurrentVersion == version)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, next).SetProperty(x => x.ClientDueAt, (DateTime?)null)
                        .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.ConcurrencyStamp, Guid.NewGuid()), ct);
            if (updated == 0)
            {
                var current = await db.Set<Deliverable>().AsNoTracking().Where(x => x.Id == id)
                    .Select(x => new { x.Status, x.CurrentVersion, x.LastSentVersion }).FirstAsync(ct);
                if (current.Status == DeliverableStatus.ClientReview && current.CurrentVersion != version)
                    throw DomainException.Conflict("deliverable.stale_version",
                        $"Version {version} is outdated; the latest version is {current.CurrentVersion}. Reload to review it.");
                throw DomainException.Conflict("deliverable.not_awaiting_approval",
                    current.Status is DeliverableStatus.Approved or DeliverableStatus.Published
                        ? "This deliverable was already approved."
                        : "This deliverable isn't waiting for your review any more. Reload to see its status.");
            }

            db.Set<DeliverableReview>().Add(new DeliverableReview
            {
                DeliverableId = id, ClientAccountId = clientId, VersionNumber = version, Stage = ReviewStage.Client,
                Decision = approve ? ReviewDecision.Approved : ReviewDecision.ChangesRequested, UserId = me, UserName = name,
                Comment = string.IsNullOrWhiteSpace(r.Comment) ? null : r.Comment.Trim(), CreatedAt = now,
            });
            if (!string.IsNullOrWhiteSpace(r.Comment))
                db.Set<DeliverableComment>().Add(new DeliverableComment
                {
                    DeliverableId = id, ClientAccountId = clientId, VersionNumber = version, AuthorUserId = me, FromClient = true,
                    Body = r.Comment.Trim(), CreatedAt = now,
                });
            audit.Record(approve ? "deliverable.client_approved" : "deliverable.client_changes_requested", nameof(Deliverable), id,
                new { Status = DeliverableStatus.ClientReview, Version = version }, new { Status = next, Version = version }, r.Comment?.Trim());
            await NotifyTeamAsync(d, approve ? $"Approved by {name}: {d.Title}" : $"Changes requested by {name}: {d.Title}",
                approve ? $"Version {version} was approved." : r.Comment!.Trim(), ct, DeliveryNotificationTypes.DeliverableDecision, email: true);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        if (approve) await events.PublishAsync(new DeliverableApproved(id, d.ProjectId, clientId, now), ct);
        return await ClientGetAsync(clientId, id, ct);
    }

    private async Task NotifyTeamAsync(Deliverable d, string title, string body, CancellationToken ct,
        string type = DeliveryNotificationTypes.DeliverableDecision, bool email = false)
    {
        var project = await db.Set<Project>().AsNoTracking().FirstAsync(p => p.Id == d.ProjectId, ct);
        var am = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == d.ClientAccountId).Select(c => c.AccountManagerUserId).FirstAsync(ct);
        var recipients = new[] { d.OwnerUserId, project.OwnerUserId, am }.Where(x => x != null).Select(x => x!.Value).Distinct();
        foreach (var u in recipients)
            await notifications.StageAsync(new NotificationRequest(u, type, title, body.Length > 500 ? body[..500] + "…" : body,
                DeliveryLinks.AgencyDeliverable(d.Id), email ? new[] { NotificationChannel.Email } : null), ct);
    }

    // ------------------------------------------------------------------ jobs

    /// <summary>
    /// Auto-approves one deliverable waiting longer than the client's opt-in window. Conditional (still ClientReview on the
    /// same version), audited as a system action; returns true when this call approved it.
    /// </summary>
    public async Task<bool> AutoApproveAsync(Guid id, int version, int afterDays, CancellationToken ct)
    {
        var now = Now;
        Guid projectId, clientId;
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            var updated = await db.Set<Deliverable>()
                .Where(x => x.Id == id && x.Status == DeliverableStatus.ClientReview && x.CurrentVersion == version)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, DeliverableStatus.Approved).SetProperty(x => x.ApprovedAt, now)
                    .SetProperty(x => x.ApprovedVersion, version).SetProperty(x => x.AutoApproved, true)
                    .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.ConcurrencyStamp, Guid.NewGuid()), ct);
            if (updated == 0) return false;
            var d = await db.Set<Deliverable>().AsNoTracking().FirstAsync(x => x.Id == id, ct);
            projectId = d.ProjectId;
            clientId = d.ClientAccountId;
            db.Set<DeliverableReview>().Add(new DeliverableReview
            {
                DeliverableId = id, ClientAccountId = clientId, VersionNumber = version, Stage = ReviewStage.System,
                Decision = ReviewDecision.AutoApproved, UserName = "Automatic approval",
                Comment = $"No client feedback within {afterDays} days (auto-approval is enabled for this client).", CreatedAt = now,
            });
            audit.RecordSystem("deliverable.auto_approved", nameof(Deliverable), id, new { Version = version, AfterDays = afterDays },
                "Client auto-approval window elapsed");
            await NotifyTeamAsync(d, $"Auto-approved: {d.Title}", $"Version {version} was approved automatically after {afterDays} days without client feedback.", ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        await events.PublishAsync(new DeliverableApproved(id, projectId, clientId, now), ct);
        return true;
    }
}
