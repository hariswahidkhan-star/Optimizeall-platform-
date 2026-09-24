using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.SocialMedia;

// ---------------------------------------------------------------- DTOs

public sealed class VariantInput
{
    [Required] public Guid? ProfileId { get; set; }
    [MaxLength(70_000)] public string Text { get; set; } = string.Empty;
    [MaxLength(200)] public string? Title { get; set; }
    [MaxLength(35)] public List<Guid> MediaIds { get; set; } = new();
    [MaxLength(35)] public List<string> AltTexts { get; set; } = new();
    [MaxLength(2000)] public string? Link { get; set; }
    [MaxLength(10_000)] public string? FirstComment { get; set; }
    [MaxLength(100)] public List<string> Hashtags { get; set; } = new();
    [MaxLength(100)] public List<string> Mentions { get; set; } = new();
}

public sealed class PostInput
{
    [Required] public Guid? ClientAccountId { get; set; }
    [Required, MinLength(1), MaxLength(200)] public string Title { get; set; } = string.Empty;

    /// <summary>Planned time (UTC). Scheduling happens through the schedule/queue actions.</summary>
    public DateTime? ScheduledAt { get; set; }
    public Guid? CampaignId { get; set; }
    public bool AutoAppendUtm { get; set; }
    public bool IsEvergreen { get; set; }
    [Range(1, 365)] public int EvergreenIntervalDays { get; set; } = 30;
    [Range(1, 52)] public int EvergreenMaxRepeats { get; set; } = 3;
    [Required, MinLength(1), MaxLength(8)] public List<VariantInput> Variants { get; set; } = new();
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class TransitionInput
{
    [MaxLength(4000)] public string? Comment { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class ScheduleInput
{
    /// <summary>UTC publish time; omit to keep the planned time.</summary>
    public DateTime? ScheduledAt { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class RescheduleInput
{
    [Required] public DateTime? ScheduledAt { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class MarkPublishedInput
{
    [Required] public Guid? VariantId { get; set; }
    [Required, MaxLength(1000)] public string Url { get; set; } = string.Empty;
    [MaxLength(200)] public string? ExternalPostId { get; set; }
}

public sealed class RetryInput
{
    /// <summary>Required when a variant's outcome is unknown: confirms it was checked and is not live.</summary>
    public bool ConfirmNotPublished { get; set; }
}

public sealed class CommentInput
{
    [Required, MinLength(1), MaxLength(4000)] public string Body { get; set; } = string.Empty;
    public bool Internal { get; set; }
}

public sealed class ValidateInput
{
    [Required] public Guid? ClientAccountId { get; set; }
    public Guid? CampaignId { get; set; }
    public bool AutoAppendUtm { get; set; }
    [Required, MinLength(1), MaxLength(8)] public List<VariantInput> Variants { get; set; } = new();
}

public sealed record VariantDto(
    Guid Id, Guid ProfileId, string ProfileHandle, string ProfileName, SocialNetwork Network, string Text, string? Title,
    IReadOnlyList<Guid> MediaIds, IReadOnlyList<string> AltTexts, string? Link, string? EffectiveLink, string? FirstComment,
    IReadOnlyList<string> Hashtags, IReadOnlyList<string> Mentions, VariantPublishStatus PublishStatus, int Attempts,
    DateTime? NextAttemptAt, PublishFailureKind FailureKind, string? FailureReason, string? ExternalPostId, string? PublishedUrl,
    DateTime? PublishedAt, bool PublishedManually, VariantValidation Validation);

public sealed record CommentDto(Guid Id, string AuthorName, bool IsClient, bool IsInternal, PostCommentKind Kind, string Body, DateTime CreatedAt,
    Guid? AuthorUserId = null, bool IsResolved = false, DateTime? ResolvedAt = null);

public sealed record PostDto(
    Guid Id, Guid ClientAccountId, string ClientName, string Title, SocialPostStatus Status, DateTime? ScheduledAt, Guid? CampaignId,
    bool AutoAppendUtm, bool IsEvergreen, int EvergreenIntervalDays, int EvergreenMaxRepeats, int EvergreenRepeatCount,
    Guid? RecycledFromPostId, int? RecycleNumber, DateTime? PublishedAt, string? FailureReason, bool RequiresClientApproval,
    bool IsValid, IReadOnlyList<string> AllowedActions, IReadOnlyList<VariantDto> Variants, IReadOnlyList<CommentDto> Comments,
    string CreatedByName, DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp);

/// <summary>Compact row for calendars and lists.</summary>
public sealed record PostSummaryDto(
    Guid Id, Guid ClientAccountId, string ClientName, string Title, SocialPostStatus Status, DateTime? ScheduledAt,
    IReadOnlyList<SocialNetwork> Networks, string Preview, bool IsEvergreen, DateTime? PublishedAt, string? FailureReason,
    Guid ConcurrencyStamp);

public sealed record ValidationResultDto(bool IsValid, IReadOnlyList<VariantValidation> Variants);

// ---------------------------------------------------------------- Service

/// <summary>Post authoring, validation, the approval workflow, scheduling, manual publishing and retries.</summary>
public sealed class SocialPostService(
    AppDbContext db,
    SocialAccess access,
    NetworkPresetProvider presets,
    ICurrentUser currentUser,
    IAuditLogger audit,
    INotificationService notifications,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------ validation

    public async Task<ValidationResultDto> ValidateAsync(ValidateInput input, CancellationToken ct)
    {
        var clientId = input.ClientAccountId!.Value;
        await access.ClientAsync(clientId, ct);
        var campaign = await CampaignAsync(clientId, input.CampaignId, ct);
        var settings = await access.SettingsAsync(clientId, ct);
        var profiles = await ProfilesAsync(clientId, input.Variants.Select(v => v.ProfileId!.Value), ct);
        var media = await MediaAsync(clientId, input.Variants.SelectMany(v => v.MediaIds), ct);
        var results = new List<VariantValidation>();
        foreach (var v in input.Variants)
        {
            var profile = profiles[v.ProfileId!.Value];
            var link = EffectiveLink(v.Link, input.AutoAppendUtm, profile.Network, campaign, settings, null);
            results.Add(PostValidator.Validate(Content(profile.Network, v.Text, v.Title, v.MediaIds, v.AltTexts, link, v.FirstComment,
                v.Hashtags, v.Mentions, media), await presets.ForAsync(profile.Network, ct)));
        }
        return new ValidationResultDto(results.All(r => r.IsValid), results);
    }

    // ------------------------------ create / update

    public async Task<SocialPost> CreateAsync(PostInput input, CancellationToken ct)
    {
        var clientId = input.ClientAccountId!.Value;
        await access.ClientAsync(clientId, ct);
        var post = new SocialPost
        {
            ClientAccountId = clientId,
            CreatedByUserId = currentUser.Id,
        };
        await ApplyAsync(post, input, ct);
        db.Set<SocialPost>().Add(post);
        audit.Record("social.post.created", nameof(SocialPost), post.Id, after: Snapshot(post));
        await db.SaveChangesAsync(ct);
        return post;
    }

    public async Task<SocialPost> UpdateAsync(Guid id, PostInput input, CancellationToken ct)
    {
        var post = await access.PostAsync(id, ct);
        if (input.ClientAccountId != post.ClientAccountId)
            throw new DomainException("social.client_mismatch", "A post cannot be moved to another client.");
        CheckStamp(post, input.ConcurrencyStamp, required: true);
        if (!PostWorkflow.Editable.Contains(post.Status))
            throw DomainException.Conflict("social.not_editable", $"A post that is {post.Status} cannot be edited.");

        var before = Snapshot(post);
        var previousStatus = post.Status;
        await ApplyAsync(post, input, ct);
        // Most edits change only the variants (text, media, hashtags): the post row must still be saved so its
        // ConcurrencyStamp advances and a concurrent editor still holding the old stamp gets 409 instead of overwriting.
        ConcurrencyGuard.Touch(db, post);
        if (previousStatus != SocialPostStatus.Draft)
        {
            // Content changed after review: the approvals no longer apply.
            post.Status = SocialPostStatus.Draft;
            post.ApprovedAt = null;
            post.ApprovedByUserId = null;
            post.NextAttemptAt = null;
            post.FailureReason = null;
            foreach (var v in post.Variants.Where(v => v.PublishStatus != VariantPublishStatus.Published))
            {
                v.PublishStatus = VariantPublishStatus.Pending;
                v.FailureKind = PublishFailureKind.None;
                v.FailureReason = null;
                v.NextAttemptAt = null;
            }
            AddComment(post, PostCommentKind.System, $"Edited while {previousStatus}; the post is back in Draft and needs review again.", isInternal: false);
        }
        audit.Record("social.post.updated", nameof(SocialPost), post.Id, before, Snapshot(post));
        await db.SaveChangesAsync(ct);
        return post;
    }

    private async Task ApplyAsync(SocialPost post, PostInput input, CancellationToken ct)
    {
        var clientId = post.ClientAccountId;
        if (input.CampaignId is not null && await CampaignAsync(clientId, input.CampaignId, ct) is { IsArchived: true } archived && post.CampaignId != archived.Id)
            throw new DomainException("social.campaign_archived", $"The campaign {archived.Name} is archived; restore it or choose another campaign.",
                errors: new Dictionary<string, string[]> { ["campaignId"] = new[] { "This campaign is archived." } });
        var profileIds = input.Variants.Select(v => v.ProfileId!.Value).ToList();
        if (profileIds.Distinct().Count() != profileIds.Count)
            throw new DomainException("social.duplicate_profile", "Each profile can appear only once in a post.");
        var profiles = await ProfilesAsync(clientId, profileIds, ct);
        await MediaAsync(clientId, input.Variants.SelectMany(v => v.MediaIds), ct);
        foreach (var v in input.Variants)
        {
            if (v.Link is { Length: > 0 } link && !PostValidator.IsHttpUrl(link))
                throw new DomainException("social.invalid_link", "Links must be absolute http(s) URLs.",
                    errors: new Dictionary<string, string[]> { ["link"] = new[] { "Enter an absolute http(s) URL." } });
            if (v.AltTexts.Any(a => a.Length > 5000))
                throw new DomainException("social.alt_text_too_long", "Alt text must be at most 5,000 characters.");
        }

        post.Title = input.Title.Trim();
        post.ScheduledAt = input.ScheduledAt is { } at ? Utc(at) : null;
        post.CampaignId = input.CampaignId;
        post.AutoAppendUtm = input.AutoAppendUtm;
        post.IsEvergreen = input.IsEvergreen;
        post.EvergreenIntervalDays = input.EvergreenIntervalDays;
        post.EvergreenMaxRepeats = input.EvergreenMaxRepeats;

        var keep = new HashSet<Guid>(profileIds);
        foreach (var removed in post.Variants.Where(v => !keep.Contains(v.ProfileId)).ToList())
        {
            if (removed.PublishStatus == VariantPublishStatus.Published)
                throw DomainException.Conflict("social.variant_published", "A published variant cannot be removed.");
            post.Variants.Remove(removed);
            db.Remove(removed);
        }
        foreach (var v in input.Variants)
        {
            var profile = profiles[v.ProfileId!.Value];
            var variant = post.Variants.FirstOrDefault(x => x.ProfileId == profile.Id);
            if (variant is null)
            {
                variant = new SocialPostVariant { PostId = post.Id, ClientAccountId = clientId, ProfileId = profile.Id };
                post.Variants.Add(variant);
                if (db.Entry(post).State != EntityState.Detached) db.Add(variant);
            }
            else if (variant.PublishStatus == VariantPublishStatus.Published)
            {
                continue; // already live: content is frozen
            }
            variant.Network = profile.Network;
            variant.Text = v.Text;
            variant.Title = string.IsNullOrWhiteSpace(v.Title) ? null : v.Title.Trim();
            variant.MediaIds = v.MediaIds.ToList();
            variant.AltTexts = v.AltTexts.Select(a => a.Trim()).ToList();
            variant.Link = string.IsNullOrWhiteSpace(v.Link) ? null : v.Link.Trim();
            variant.FirstComment = string.IsNullOrWhiteSpace(v.FirstComment) ? null : v.FirstComment;
            variant.Hashtags = v.Hashtags.Select(PostValidator.NormalizeHashtag).Where(h => h.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            variant.Mentions = v.Mentions.Select(m => m.Trim().TrimStart('@')).Where(m => m.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    // ------------------------------ workflow

    public async Task<SocialPost> TransitionAsync(Guid id, WorkflowAction action, TransitionInput input, CancellationToken ct)
    {
        var post = await access.PostAsync(id, ct);
        CheckStamp(post, input.ConcurrencyStamp, required: false);
        var settings = await access.SettingsAsync(post.ClientAccountId, ct);

        if (action is WorkflowAction.ApproveInternal or WorkflowAction.Schedule && !currentUser.HasPermission(Permissions.SocialPublish))
            throw DomainException.Forbidden("auth.forbidden", "Approving and scheduling posts requires social.publish.");
        if (action is WorkflowAction.RequestChanges or WorkflowAction.ClientRequestChanges && string.IsNullOrWhiteSpace(input.Comment))
            throw new DomainException("social.comment_required", "Say what needs to change.",
                errors: new Dictionary<string, string[]> { ["comment"] = new[] { "Say what needs to change." } });
        if (action == WorkflowAction.Submit || action == WorkflowAction.ApproveInternal)
            await EnsureValidAsync(post, settings, ct);

        var from = post.Status;
        var to = PostWorkflow.Next(from, action, settings.RequireClientApproval);
        post.Status = to;
        var actor = currentUser.Id;
        if (to == SocialPostStatus.Approved)
        {
            post.ApprovedAt = Now;
            post.ApprovedByUserId = actor;
        }
        var kind = action switch
        {
            WorkflowAction.Submit => PostCommentKind.Submitted,
            WorkflowAction.ApproveInternal or WorkflowAction.ClientApprove => PostCommentKind.Approved,
            WorkflowAction.RequestChanges or WorkflowAction.ClientRequestChanges => PostCommentKind.ChangesRequested,
            _ => PostCommentKind.System,
        };
        var isClient = action is WorkflowAction.ClientApprove or WorkflowAction.ClientRequestChanges;
        AddComment(post, kind, string.IsNullOrWhiteSpace(input.Comment) ? Describe(action, to) : input.Comment.Trim(),
            isInternal: !isClient && action is WorkflowAction.Submit or WorkflowAction.RequestChanges, isClient: isClient);
        audit.Record($"social.post.{action.ToString().ToLowerInvariant()}", nameof(SocialPost), post.Id,
            new { Status = from }, new { Status = to }, input.Comment);

        if (to == SocialPostStatus.ClientApproval) await NotifyClientApproversAsync(post, ct);
        if (kind is PostCommentKind.ChangesRequested or PostCommentKind.Approved && post.CreatedByUserId != actor)
            await notifications.StageAsync(new NotificationRequest(post.CreatedByUserId, "social.post_reviewed",
                kind == PostCommentKind.Approved ? $"Post approved: {post.Title}" : $"Changes requested: {post.Title}",
                input.Comment ?? Describe(action, to), SocialLinks.Post(post.Id)), ct);

        await db.SaveChangesAsync(ct);
        return post;
    }

    public async Task<SocialPost> ScheduleAsync(Guid id, ScheduleInput input, CancellationToken ct)
    {
        var post = await access.PostAsync(id, ct);
        CheckStamp(post, input.ConcurrencyStamp, required: false);
        var at = input.ScheduledAt is { } requested ? Utc(requested) : post.ScheduledAt;
        await ScheduleCoreAsync(post, at, ct);
        await db.SaveChangesAsync(ct);
        return post;
    }

    /// <summary>Schedules at the next free queue slot of the post's first profile.</summary>
    public async Task<SocialPost> QueueAsync(Guid id, CancellationToken ct)
    {
        var post = await access.PostAsync(id, ct);
        var client = await access.ClientAsync(post.ClientAccountId, ct);
        var profileIds = post.Variants.Select(v => v.ProfileId).ToList();
        var slots = await db.Set<SocialQueueSlot>().AsNoTracking().Where(s => profileIds.Contains(s.ProfileId))
            .Select(s => new { s.DayOfWeek, s.MinuteOfDay }).ToListAsync(ct);
        if (slots.Count == 0)
            throw DomainException.Conflict("social.no_queue_slots", "None of this post's profiles has queue slots. Add slots under Queue settings.");
        var horizon = Now.AddDays(60);
        var taken = await db.Set<SocialPost>().AsNoTracking()
            .Where(p => p.ClientAccountId == post.ClientAccountId && p.Id != post.Id && p.ScheduledAt > Now && p.ScheduledAt < horizon
                        && (p.Status == SocialPostStatus.Scheduled || p.Status == SocialPostStatus.Publishing))
            .Where(p => p.Variants.Any(v => profileIds.Contains(v.ProfileId)))
            .Select(p => p.ScheduledAt!.Value).ToListAsync(ct);
        var next = QueueScheduler.NextFreeSlot(slots.Select(s => (s.DayOfWeek, s.MinuteOfDay)).Distinct().ToList(),
            QueueScheduler.Zone(client.TimeZone), Now.AddMinutes(5), taken)
            ?? throw DomainException.Conflict("social.queue_full", "No free queue slot in the next eight weeks.");
        await ScheduleCoreAsync(post, next, ct);
        await db.SaveChangesAsync(ct);
        return post;
    }

    private async Task ScheduleCoreAsync(SocialPost post, DateTime? at, CancellationToken ct)
    {
        if (!currentUser.HasPermission(Permissions.SocialPublish))
            throw DomainException.Forbidden("auth.forbidden", "Scheduling posts requires social.publish.");
        if (at is null || at <= Now.AddMinutes(1))
            throw new DomainException("social.schedule_in_past", "Choose a publish time at least a minute in the future.",
                errors: new Dictionary<string, string[]> { ["scheduledAt"] = new[] { "Choose a time in the future." } });
        var settings = await access.SettingsAsync(post.ClientAccountId, ct);
        await EnsureValidAsync(post, settings, ct);
        var from = post.Status;
        post.Status = PostWorkflow.Next(from, WorkflowAction.Schedule, settings.RequireClientApproval);
        await EnsureClientApprovedAsync(post, settings, ct);
        post.ScheduledAt = at;
        post.NextAttemptAt = null;
        AddComment(post, PostCommentKind.Scheduled, $"Scheduled for {at:yyyy-MM-dd HH:mm} UTC.", isInternal: true);
        audit.Record("social.post.scheduled", nameof(SocialPost), post.Id, new { Status = from }, new { post.Status, post.ScheduledAt });
    }

    /// <summary>Moves the planned/scheduled time (calendar drag). Approvals are kept; content is unchanged.</summary>
    public async Task<SocialPost> RescheduleAsync(Guid id, RescheduleInput input, CancellationToken ct)
    {
        var post = await access.PostAsync(id, ct);
        CheckStamp(post, input.ConcurrencyStamp, required: false);
        if (!PostWorkflow.Reschedulable.Contains(post.Status))
            throw DomainException.Conflict("social.not_reschedulable", $"A post that is {post.Status} cannot be moved.");
        var at = Utc(input.ScheduledAt!.Value);
        if (post.Status is SocialPostStatus.Scheduled or SocialPostStatus.Failed)
        {
            if (!currentUser.HasPermission(Permissions.SocialPublish))
                throw DomainException.Forbidden("auth.forbidden", "Moving a scheduled post requires social.publish.");
            if (at <= Now.AddMinutes(1))
                throw new DomainException("social.schedule_in_past", "Choose a publish time in the future.");
        }
        var before = post.ScheduledAt;
        post.ScheduledAt = at;
        audit.Record("social.post.rescheduled", nameof(SocialPost), post.Id, new { ScheduledAt = before }, new { post.ScheduledAt });
        await db.SaveChangesAsync(ct);
        return post;
    }

    public async Task<SocialPost> MarkPublishedAsync(Guid id, MarkPublishedInput input, CancellationToken ct)
    {
        var post = await access.PostAsync(id, ct);
        if (post.Status is not (SocialPostStatus.Approved or SocialPostStatus.Scheduled or SocialPostStatus.Failed or SocialPostStatus.Published))
            throw DomainException.Conflict("social.not_publishable", $"A post that is {post.Status} cannot be marked as published; it must be approved first.");
        var variant = post.Variants.FirstOrDefault(v => v.Id == input.VariantId) ?? throw DomainException.NotFound("Variant");
        if (variant.PublishStatus == VariantPublishStatus.Published)
            throw DomainException.Conflict("social.already_published", "This variant is already published.");
        if (variant.PublishStatus == VariantPublishStatus.Publishing)
            throw DomainException.Conflict("social.publishing_in_progress", "This variant is being published right now; wait for the outcome.");
        var url = input.Url.Trim();
        if (!PostValidator.IsHttpUrl(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("social.invalid_url", "Enter the https link to the live post.",
                errors: new Dictionary<string, string[]> { ["url"] = new[] { "Enter the https link to the live post." } });

        // Conditional claim so a concurrent publishing run cannot also publish it.
        var now = Now;
        var claimed = await db.Set<SocialPostVariant>()
            .Where(v => v.Id == variant.Id && (v.PublishStatus == VariantPublishStatus.Pending || v.PublishStatus == VariantPublishStatus.Failed))
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.PublishStatus, VariantPublishStatus.Published)
                .SetProperty(v => v.PublishedManually, true)
                .SetProperty(v => v.PublishedUrl, url)
                .SetProperty(v => v.ExternalPostId, input.ExternalPostId)
                .SetProperty(v => v.PublishedAt, now)
                .SetProperty(v => v.MarkedPublishedByUserId, currentUser.Id)
                .SetProperty(v => v.FailureKind, PublishFailureKind.None)
                .SetProperty(v => v.FailureReason, (string?)null), ct);
        if (claimed == 0) throw DomainException.Conflict("social.publishing_in_progress", "This variant changed meanwhile; reload and try again.");
        await db.Entry(variant).ReloadAsync(ct);

        db.Set<SocialPublishAttempt>().Add(new SocialPublishAttempt
        {
            PostId = post.Id, VariantId = variant.Id, ClientAccountId = post.ClientAccountId, Network = variant.Network,
            AttemptNumber = variant.Attempts + 1, Outcome = "MarkedPublished", Message = $"Marked as published manually: {url}",
            ExternalPostId = input.ExternalPostId, StartedAt = now, FinishedAt = now, ActorUserId = currentUser.Id,
        });
        if (post.Variants.All(v => v.PublishStatus == VariantPublishStatus.Published))
        {
            post.Status = SocialPostStatus.Published;
            post.PublishedAt ??= now;
            post.FailureReason = null;
        }
        AddComment(post, PostCommentKind.Published, $"{PostValidator.Label(variant.Network)} marked as published: {url}", isInternal: false);
        audit.Record("social.post.marked_published", nameof(SocialPostVariant), variant.Id, null, new { url, input.ExternalPostId });
        await db.SaveChangesAsync(ct);
        return post;
    }

    public async Task<SocialPost> RetryAsync(Guid id, RetryInput input, CancellationToken ct)
    {
        var post = await access.PostAsync(id, ct);
        if (!currentUser.HasPermission(Permissions.SocialPublish))
            throw DomainException.Forbidden("auth.forbidden", "Retrying requires social.publish.");
        var failed = post.Variants.Where(v => v.PublishStatus == VariantPublishStatus.Failed).ToList();
        if (post.Status != SocialPostStatus.Failed || failed.Count == 0)
            throw DomainException.Conflict("social.nothing_to_retry", "Only failed posts can be retried.");
        if (failed.Any(v => v.FailureKind == PublishFailureKind.Unknown) && !input.ConfirmNotPublished)
            throw DomainException.Conflict("social.confirm_not_published",
                "The outcome of a previous attempt is unknown. Check the network first; retry only if the post is not live.");
        foreach (var v in failed)
        {
            v.PublishStatus = VariantPublishStatus.Pending;
            v.NextAttemptAt = null;
            v.FailureKind = PublishFailureKind.None;
            v.FailureReason = null;
        }
        var settings = await access.SettingsAsync(post.ClientAccountId, ct);
        await EnsureClientApprovedAsync(post, settings, ct);
        post.Status = PostWorkflow.Next(post.Status, WorkflowAction.Retry, settings.RequireClientApproval);
        if (post.ScheduledAt is null || post.ScheduledAt < Now) post.ScheduledAt = Now;
        post.NextAttemptAt = null;
        post.FailureReason = null;
        AddComment(post, PostCommentKind.System, "Publishing retried.", isInternal: true);
        audit.Record("social.post.retried", nameof(SocialPost), post.Id, null, new { failed = failed.Select(v => v.Id) });
        await db.SaveChangesAsync(ct);
        return post;
    }

    public async Task<SocialPost> CommentAsync(Guid id, CommentInput input, bool fromClient, CancellationToken ct)
    {
        var post = await access.PostAsync(id, ct);
        AddComment(post, PostCommentKind.Comment, input.Body.Trim(), isInternal: !fromClient && input.Internal, isClient: fromClient);
        await db.SaveChangesAsync(ct);
        return post;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var post = await access.PostAsync(id, ct);
        if (post.Status is SocialPostStatus.Publishing or SocialPostStatus.Published || post.Variants.Any(v => v.PublishStatus == VariantPublishStatus.Published))
            throw DomainException.Conflict("social.not_deletable", "Published posts are kept for reporting and cannot be deleted.");
        audit.Record("social.post.deleted", nameof(SocialPost), post.Id, Snapshot(post));
        db.Remove(post);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------ client approval gate

    /// <summary>
    /// True when the post's approval came from one of the client's Approvers/Owners. The approval workflow records the
    /// user of the last transition to Approved: a client approver after "client approve", a staff member after an internal
    /// approval while client approval was off.
    /// </summary>
    public static Task<bool> IsClientApprovedAsync(AppDbContext db, Guid clientId, Guid? approvedByUserId, CancellationToken ct) =>
        approvedByUserId is not { } approver
            ? Task.FromResult(false)
            : db.Set<ClientMember>().AsNoTracking().AnyAsync(m => m.ClientAccountId == clientId && m.UserId == approver
                && (m.Role == ClientMemberRole.Approver || m.Role == ClientMemberRole.Owner), ct);

    /// <summary>
    /// When the client requires approval, a post approved only internally (e.g. before the setting was switched on) must
    /// not be scheduled or published: 409 <c>social.client_approval_required</c>.
    /// </summary>
    private async Task EnsureClientApprovedAsync(SocialPost post, SocialClientSettings settings, CancellationToken ct)
    {
        if (!settings.RequireClientApproval || await IsClientApprovedAsync(db, post.ClientAccountId, post.ApprovedByUserId, ct)) return;
        throw DomainException.Conflict("social.client_approval_required",
            "This client requires client approval, and this post was only approved internally. Send it to the client for approval first.");
    }

    /// <summary>
    /// Called when a client's "require client approval" setting is switched on: approved or scheduled posts that the client
    /// has not approved go back to ClientApproval (conditional update, so a post the publishing job has just claimed is left
    /// alone) and the client's approvers are notified. Returns the number of posts reopened.
    /// </summary>
    public async Task<int> ReopenForClientApprovalAsync(Guid clientId, CancellationToken ct)
    {
        var candidates = await db.Set<SocialPost>().AsNoTracking()
            .Where(p => p.ClientAccountId == clientId && (p.Status == SocialPostStatus.Approved || p.Status == SocialPostStatus.Scheduled))
            .Select(p => new { p.Id, p.Status, p.ApprovedByUserId, p.Title }).ToListAsync(ct);
        var reopened = new List<(Guid Id, string Title)>();
        foreach (var c in candidates)
        {
            if (await IsClientApprovedAsync(db, clientId, c.ApprovedByUserId, ct)) continue;
            var changed = await db.Set<SocialPost>()
                .Where(p => p.Id == c.Id && p.Status == c.Status)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, SocialPostStatus.ClientApproval)
                    .SetProperty(p => p.ApprovedAt, (DateTime?)null)
                    .SetProperty(p => p.ApprovedByUserId, (Guid?)null)
                    .SetProperty(p => p.NextAttemptAt, (DateTime?)null)
                    .SetProperty(p => p.UpdatedAt, Now)
                    .SetProperty(p => p.ConcurrencyStamp, Guid.NewGuid()), ct);
            if (changed == 1) reopened.Add((c.Id, c.Title));
        }
        foreach (var (id, title) in reopened)
        {
            var post = new SocialPost { Id = id, ClientAccountId = clientId, Title = title };
            AddComment(post, PostCommentKind.System, "Client approval is now required; the post is waiting for the client's approval.", isInternal: false);
            audit.Record("social.post.reopened_for_client_approval", nameof(SocialPost), id);
            await NotifyClientApproversAsync(post, ct);
        }
        if (reopened.Count > 0) await db.SaveChangesAsync(ct);
        return reopened.Count;
    }

    // ------------------------------ mapping

    public async Task<PostDto> ToDtoAsync(SocialPost post, bool forClient, CancellationToken ct)
    {
        var client = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == post.ClientAccountId).Select(c => c.Name).FirstAsync(ct);
        var settings = await access.SettingsAsync(post.ClientAccountId, ct);
        var campaign = await CampaignAsync(post.ClientAccountId, post.CampaignId, ct, required: false);
        var profiles = await db.Set<BrandProfile>().AsNoTracking().Where(p => p.ClientAccountId == post.ClientAccountId)
            .ToDictionaryAsync(p => p.Id, ct);
        var media = await MediaAsync(post.ClientAccountId, post.Variants.SelectMany(v => v.MediaIds), ct, strict: false);
        var comments = await db.Set<SocialPostComment>().AsNoTracking().Where(c => c.PostId == post.Id)
            .Where(c => !forClient || !c.IsInternal).OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var creator = await db.Set<User>().AsNoTracking().Where(u => u.Id == post.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);

        var variants = new List<VariantDto>();
        foreach (var v in post.Variants.OrderBy(v => v.Network).ThenBy(v => v.Id))
        {
            var profile = profiles.GetValueOrDefault(v.ProfileId);
            var link = EffectiveLink(v.Link, post.AutoAppendUtm, v.Network, campaign, settings, post.Title);
            var validation = PostValidator.Validate(Content(v.Network, v.Text, v.Title, v.MediaIds, v.AltTexts, link, v.FirstComment,
                v.Hashtags, v.Mentions, media), await presets.ForAsync(v.Network, ct));
            // Clients see a failed variant as still pending (the agency is handling it), like their calendar and lists.
            var hideFailure = forClient && v.PublishStatus == VariantPublishStatus.Failed;
            variants.Add(new VariantDto(v.Id, v.ProfileId, profile?.Handle ?? string.Empty, profile?.DisplayName ?? string.Empty, v.Network,
                v.Text, v.Title, v.MediaIds, v.AltTexts, v.Link, link, v.FirstComment, v.Hashtags, v.Mentions,
                hideFailure ? VariantPublishStatus.Pending : v.PublishStatus, v.Attempts, v.NextAttemptAt,
                forClient ? PublishFailureKind.None : v.FailureKind, forClient ? null : v.FailureReason, v.ExternalPostId, v.PublishedUrl, v.PublishedAt,
                v.PublishedManually, validation));
        }
        var allowed = forClient
            ? (post.Status == SocialPostStatus.ClientApproval ? new[] { "clientApprove", "clientRequestChanges" } : Array.Empty<string>())
            : AllowedActions(post, settings.RequireClientApproval);
        if (!forClient && settings.RequireClientApproval && post.Status is SocialPostStatus.Approved or SocialPostStatus.Failed
            && !await IsClientApprovedAsync(db, post.ClientAccountId, post.ApprovedByUserId, ct))
            allowed = allowed.Where(a => a is not ("schedule" or "queue" or "retry")).ToList();
        var status = forClient && post.Status == SocialPostStatus.Failed ? SocialPostStatus.Scheduled : post.Status;
        return new PostDto(post.Id, post.ClientAccountId, client, post.Title, status, post.ScheduledAt, post.CampaignId, post.AutoAppendUtm,
            post.IsEvergreen, post.EvergreenIntervalDays, post.EvergreenMaxRepeats, post.EvergreenRepeatCount, post.RecycledFromPostId,
            post.RecycleNumber, post.PublishedAt, forClient ? null : post.FailureReason, settings.RequireClientApproval,
            variants.All(v => v.Validation.IsValid), allowed, variants,
            comments.Select(c => new CommentDto(c.Id, c.AuthorName, c.IsClient, c.IsInternal, c.Kind, c.Body, c.CreatedAt, c.AuthorUserId, c.IsResolved, c.ResolvedAt)).ToList(),
            creator ?? "Unknown", post.CreatedAt, post.UpdatedAt, post.ConcurrencyStamp);
    }

    private IReadOnlyList<string> AllowedActions(SocialPost post, bool requireClientApproval)
    {
        var canPublish = currentUser.HasPermission(Permissions.SocialPublish);
        var actions = new List<string>();
        void Add(WorkflowAction a, string name, bool needsPublish = false)
        {
            if ((!needsPublish || canPublish) && PostWorkflow.CanTransition(post.Status, a, requireClientApproval)) actions.Add(name);
        }
        Add(WorkflowAction.Submit, "submit");
        Add(WorkflowAction.ApproveInternal, "approve", needsPublish: true);
        Add(WorkflowAction.RequestChanges, "requestChanges");
        Add(WorkflowAction.Schedule, "schedule", needsPublish: true);
        if (canPublish && post.Status == SocialPostStatus.Approved) actions.Add("queue");
        Add(WorkflowAction.Unschedule, "unschedule", needsPublish: true);
        Add(WorkflowAction.Retry, "retry", needsPublish: true);
        if (canPublish && post.Status is SocialPostStatus.Approved or SocialPostStatus.Scheduled or SocialPostStatus.Failed)
            actions.Add("markPublished");
        if (PostWorkflow.Editable.Contains(post.Status)) actions.Add("edit");
        if (post.Status is not (SocialPostStatus.Publishing or SocialPostStatus.Published)) actions.Add("delete");
        return actions;
    }

    public static PostSummaryDto Summary(SocialPost p, string clientName) => new(
        p.Id, p.ClientAccountId, clientName, p.Title, p.Status, p.ScheduledAt, p.Variants.Select(v => v.Network).Distinct().OrderBy(n => n).ToList(),
        Preview(p.Variants.OrderBy(v => v.Network).Select(v => v.Text).FirstOrDefault() ?? string.Empty), p.IsEvergreen, p.PublishedAt,
        p.FailureReason, p.ConcurrencyStamp);

    private static string Preview(string text) => text.Length <= 140 ? text : text[..139] + "…";

    // ------------------------------ helpers

    public static string? EffectiveLink(string? link, bool autoAppendUtm, SocialNetwork network, SocialCampaign? campaign,
        SocialClientSettings settings, string? postTitle)
    {
        if (string.IsNullOrWhiteSpace(link) || !autoAppendUtm || !PostValidator.IsHttpUrl(link)) return link;
        var source = campaign?.UtmSource is { Length: > 0 } s ? s : network switch
        {
            SocialNetwork.GoogleBusiness => "google-business",
            _ => network.ToString().ToLowerInvariant(),
        };
        var medium = campaign?.UtmMedium is { Length: > 0 } m ? m : settings.DefaultUtmMedium;
        var name = campaign?.UtmCampaign is { Length: > 0 } c ? c : NamingConvention.Slug(postTitle ?? "social");
        return UtmBuilder.Build(link, new UtmParameters(source, medium, string.IsNullOrEmpty(name) ? "social" : name, campaign?.UtmTerm, campaign?.UtmContent));
    }

    private static VariantContent Content(SocialNetwork network, string text, string? title, IReadOnlyList<Guid> mediaIds,
        IReadOnlyList<string> altTexts, string? link, string? firstComment, IReadOnlyList<string> hashtags, IReadOnlyList<string> mentions,
        IReadOnlyDictionary<Guid, SocialMediaAsset> media) =>
        new(network, text, title,
            mediaIds.Where(media.ContainsKey).Select(id => media[id]).Select(m => new MediaInfo(m.Id, m.Kind, m.Width, m.Height, m.DurationSeconds, m.SizeBytes, m.IsPublic)).ToList(),
            altTexts, link, firstComment, hashtags.Select(PostValidator.NormalizeHashtag).ToList(), mentions);

    private async Task EnsureValidAsync(SocialPost post, SocialClientSettings settings, CancellationToken ct)
    {
        var campaign = await CampaignAsync(post.ClientAccountId, post.CampaignId, ct, required: false);
        var media = await MediaAsync(post.ClientAccountId, post.Variants.SelectMany(v => v.MediaIds), ct, strict: false);
        var errors = new List<string>();
        foreach (var v in post.Variants.Where(v => v.PublishStatus != VariantPublishStatus.Published))
        {
            var link = EffectiveLink(v.Link, post.AutoAppendUtm, v.Network, campaign, settings, post.Title);
            var result = PostValidator.Validate(Content(v.Network, v.Text, v.Title, v.MediaIds, v.AltTexts, link, v.FirstComment, v.Hashtags,
                v.Mentions, media), await presets.ForAsync(v.Network, ct));
            errors.AddRange(result.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Message));
        }
        if (post.Variants.Count == 0) errors.Add("Add at least one network.");
        if (errors.Count > 0)
            throw new DomainException("social.validation_failed", "Fix the validation errors first: " + string.Join(" ", errors.Take(5)),
                errors: new Dictionary<string, string[]> { ["variants"] = errors.ToArray() });
    }

    private async Task<SocialCampaign?> CampaignAsync(Guid clientId, Guid? campaignId, CancellationToken ct, bool required = true)
    {
        if (campaignId is null) return null;
        var campaign = await db.Set<SocialCampaign>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == campaignId && c.ClientAccountId == clientId, ct);
        if (campaign is null && required) throw DomainException.NotFound("Campaign");
        return campaign;
    }

    private async Task<Dictionary<Guid, BrandProfile>> ProfilesAsync(Guid clientId, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        var profiles = await db.Set<BrandProfile>().AsNoTracking()
            .Where(p => p.ClientAccountId == clientId && list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (profiles.Count != list.Count)
            throw new DomainException("social.profile_not_found", "Choose profiles that belong to this client.",
                errors: new Dictionary<string, string[]> { ["variants"] = new[] { "Choose profiles that belong to this client." } });
        if (profiles.Values.Any(p => !p.IsActive))
            throw new DomainException("social.profile_inactive", "One of the chosen profiles is archived.");
        return profiles;
    }

    private async Task<Dictionary<Guid, SocialMediaAsset>> MediaAsync(Guid clientId, IEnumerable<Guid> ids, CancellationToken ct, bool strict = true)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, SocialMediaAsset>();
        var media = await db.Set<SocialMediaAsset>().AsNoTracking()
            .Where(m => m.ClientAccountId == clientId && list.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        if (strict && media.Count != list.Count)
            throw new DomainException("social.media_not_found", "Choose media from this client's library.");
        return media;
    }

    private async Task NotifyClientApproversAsync(SocialPost post, CancellationToken ct)
    {
        var approvers = await db.Set<ClientMember>().AsNoTracking()
            .Where(m => m.ClientAccountId == post.ClientAccountId && (m.Role == ClientMemberRole.Approver || m.Role == ClientMemberRole.Owner))
            .Select(m => m.UserId).ToListAsync(ct);
        foreach (var userId in approvers)
            await notifications.StageAsync(new NotificationRequest(userId, "social.approval_requested",
                $"Post awaiting your approval: {post.Title}", "Review the post and approve it or request changes.",
                SocialLinks.ClientApprovals), ct);
    }

    private void AddComment(SocialPost post, PostCommentKind kind, string body, bool isInternal, bool isClient = false)
    {
        var name = currentUser.IsAuthenticated
            ? db.Set<User>().AsNoTracking().Where(u => u.Id == currentUser.Id).Select(u => u.DisplayName).FirstOrDefault() ?? "Someone"
            : "System";
        db.Set<SocialPostComment>().Add(new SocialPostComment
        {
            PostId = post.Id,
            ClientAccountId = post.ClientAccountId,
            AuthorUserId = currentUser.IdOrNull,
            AuthorName = name,
            IsClient = isClient,
            IsInternal = isInternal,
            Kind = kind,
            Body = body.Length > 4000 ? body[..4000] : body,
            CreatedAt = Now,
        });
    }

    private static string Describe(WorkflowAction action, SocialPostStatus to) => action switch
    {
        WorkflowAction.Submit => "Submitted for internal review.",
        WorkflowAction.ApproveInternal => to == SocialPostStatus.ClientApproval ? "Approved internally; sent to the client for approval." : "Approved.",
        WorkflowAction.ClientApprove => "Approved by the client.",
        WorkflowAction.Unschedule => "Unscheduled.",
        _ => $"Moved to {to}.",
    };

    private void CheckStamp(SocialPost post, Guid? stamp, bool required)
    {
        if (stamp is null)
        {
            if (required) throw new DomainException("concurrency.stamp_required", "Include the concurrencyStamp you last saw.");
            return;
        }
        if (stamp != post.ConcurrencyStamp)
            throw DomainException.Conflict("concurrency.conflict", "This post was changed by someone else. Reload and try again.");
        db.Entry(post).Property(p => p.ConcurrencyStamp).OriginalValue = stamp.Value;
    }

    private static object Snapshot(SocialPost p) => new
    {
        p.Title, p.Status, p.ScheduledAt, p.CampaignId, p.AutoAppendUtm, p.IsEvergreen,
        Variants = p.Variants.Select(v => new { v.Network, v.ProfileId, TextLength = v.Text.Length, Media = v.MediaIds.Count, v.Link }),
    };

    public static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
