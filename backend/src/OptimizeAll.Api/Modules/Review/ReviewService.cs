using System.Data;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Campaigns;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Review;

public interface IReviewService
{
    Task<ClaimDto> ClaimAsync(Guid submissionId, CancellationToken ct);
    Task ReleaseAsync(Guid submissionId, CancellationToken ct);
    Task<DecisionResultDto> DecideAsync(Guid submissionId, DecisionRequest request, CancellationToken ct);
    Task<LiveCheckResultDto> LiveCheckAsync(Guid submissionId, LiveCheckRequest request, CancellationToken ct);
    Task<ReverseResultDto> ReverseAsync(Guid submissionId, ReverseRequest request, CancellationToken ct);
    Task<AppealResolutionDto> ResolveAppealAsync(Guid appealId, ResolveAppealRequest request, CancellationToken ct);
    Task<AssignResultDto> AssignAsync(AssignRequest request, CancellationToken ct);
}

/// <summary>
/// Reviewer commands. Every state transition is a conditional update on the expected current state (and, for
/// decisions, the concurrency stamp the reviewer saw), so two reviewers can never both decide the same submission.
/// Reward-affecting transitions first lock the campaign row (SELECT ... FOR UPDATE) so caps, budget and the
/// first-post bonus are evaluated serially per campaign.
/// </summary>
public sealed class ReviewService(
    AppDbContext db,
    ICurrentUser currentUser,
    IRewardQuoteService quotes,
    ILedgerWriter ledger,
    IAuditLogger audit,
    INotificationService notifications,
    IEventPublisher events,
    ISettingsService settings,
    IReviewQueryService queries,
    TimeProvider clock) : IReviewService
{
    public static readonly string[] DecisionActions = { "approved", "correction_requested", "rejected" };

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ claims

    public async Task<ClaimDto> ClaimAsync(Guid submissionId, CancellationToken ct)
    {
        var me = currentUser.Id;
        var now = Now;
        var until = now.AddMinutes(await settings.GetAsync(SettingKeys.ReviewClaimMinutes, 15, ct));

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var moved = await db.Set<Submission>()
            .Where(s => s.Id == submissionId && s.Status == SubmissionStatus.Pending &&
                        (s.ClaimedByUserId == null || s.ClaimExpiresAt < now || s.ClaimedByUserId == me))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SubmissionStatus.UnderReview)
                .SetProperty(s => s.ClaimedByUserId, me).SetProperty(s => s.ClaimExpiresAt, until).SetProperty(s => s.UpdatedAt, now), ct);
        if (moved == 1)
        {
            db.Set<SubmissionEvent>().Add(new SubmissionEvent
            {
                SubmissionId = submissionId, FromStatus = SubmissionStatus.Pending, ToStatus = SubmissionStatus.UnderReview,
                Action = "claimed", ActorUserId = me, CreatedAt = now,
            });
            await db.SaveChangesAsync(ct);
        }
        else
        {
            moved = await db.Set<Submission>()
                .Where(s => s.Id == submissionId && s.Status == SubmissionStatus.UnderReview &&
                            (s.ClaimedByUserId == null || s.ClaimExpiresAt < now || s.ClaimedByUserId == me))
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.ClaimedByUserId, me).SetProperty(s => s.ClaimExpiresAt, until), ct);
        }
        await tx.CommitAsync(ct);

        var s = await db.Set<Submission>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == submissionId, ct)
            ?? throw DomainException.NotFound("Submission");
        if (moved == 0) throw await ClaimConflictAsync(s, now, ct);

        var name = await db.Set<User>().Where(u => u.Id == me).Select(u => u.DisplayName).FirstAsync(ct);
        return new ClaimDto(s.Id, s.Status, new PersonRefDto(me, name), until, s.ConcurrencyStamp);
    }

    private async Task<DomainException> ClaimConflictAsync(Submission s, DateTime now, CancellationToken ct)
    {
        if (!s.IsOpenForReview)
            return DomainException.Conflict("review.already_decided", $"This submission was already decided ({s.Status}).");
        if (s.ClaimedByUserId is { } other && other != currentUser.Id && s.ClaimExpiresAt >= now)
        {
            var name = await db.Set<User>().Where(u => u.Id == other).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "another reviewer";
            return new DomainException("review.claimed_by_other",
                $"{name} is reviewing this submission until {s.ClaimExpiresAt:yyyy-MM-dd HH:mm} UTC.", DomainErrorKind.Conflict,
                new Dictionary<string, string[]>
                {
                    ["claimedBy"] = new[] { name },
                    ["claimedByUserId"] = new[] { other.ToString() },
                    ["claimExpiresAt"] = new[] { s.ClaimExpiresAt!.Value.ToString("O") },
                });
        }
        return DomainException.Conflict("review.already_decided", "This submission changed while you were reviewing it. Reload and try again.");
    }

    public async Task ReleaseAsync(Guid submissionId, CancellationToken ct)
    {
        var me = currentUser.Id;
        var released = await db.Set<Submission>().Where(s => s.Id == submissionId && s.ClaimedByUserId == me)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.ClaimedByUserId, (Guid?)null).SetProperty(s => s.ClaimExpiresAt, (DateTime?)null), ct);
        if (released == 0)
        {
            if (!await db.Set<Submission>().AnyAsync(s => s.Id == submissionId, ct)) throw DomainException.NotFound("Submission");
            throw DomainException.Conflict("review.not_claimed", "You don't hold a claim on this submission.");
        }
    }

    // ------------------------------------------------------------------ decisions

    public async Task<DecisionResultDto> DecideAsync(Guid submissionId, DecisionRequest request, CancellationToken ct)
    {
        var me = currentUser.Id;
        var now = Now;
        var decision = request.Decision!.Value;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (decision != ReviewDecision.Approve && (reason is null || reason.Length < 5))
            throw new DomainException("review.reason_required", "Explain the decision to the participant (at least 5 characters).");
        if (decision != ReviewDecision.Approve && request.QualityBonusAmount is > 0)
            throw new DomainException("review.quality_bonus_requires_approval", "A quality bonus can only be awarded when approving.");

        var existing = await db.Set<Submission>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw DomainException.NotFound("Submission");

        var target = decision switch
        {
            ReviewDecision.Approve => SubmissionStatus.Approved,
            ReviewDecision.RequestCorrection => SubmissionStatus.NeedsCorrection,
            _ => SubmissionStatus.Rejected,
        };
        var action = decision switch
        {
            ReviewDecision.Approve => "approved",
            ReviewDecision.RequestCorrection => "correction_requested",
            _ => "rejected",
        };

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await CampaignLock.LockAsync(db, existing.CampaignId, ct);
        var campaign = await db.Set<Campaign>().AsNoTracking().FirstAsync(c => c.Id == existing.CampaignId, ct);
        var before = await db.Set<Submission>().AsNoTracking().FirstAsync(s => s.Id == submissionId, ct);

        var (liveStatus, liveDue) = LiveCheckFor(campaign, before.PostedAt, target);
        var stamp = request.ConcurrencyStamp!.Value;
        var newStamp = Guid.NewGuid();
        var updated = await db.Set<Submission>()
            .Where(s => s.Id == submissionId && (s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.UnderReview) &&
                        s.ConcurrencyStamp == stamp &&
                        (s.ClaimedByUserId == null || s.ClaimExpiresAt < now || s.ClaimedByUserId == me))
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, target).SetProperty(s => s.DecidedAt, now).SetProperty(s => s.DecidedByUserId, me)
                .SetProperty(s => s.DecisionReason, reason).SetProperty(s => s.ClaimedByUserId, (Guid?)null)
                .SetProperty(s => s.ClaimExpiresAt, (DateTime?)null).SetProperty(s => s.ConcurrencyStamp, newStamp)
                .SetProperty(s => s.LiveCheckStatus, liveStatus).SetProperty(s => s.LiveCheckDueAt, liveDue)
                .SetProperty(s => s.UpdatedAt, now), ct);
        if (updated == 0)
        {
            if (before.IsOpenForReview && before.ConcurrencyStamp == stamp)
                throw await ClaimConflictAsync(before, now, ct);
            throw DomainException.Conflict("review.already_decided",
                before.IsOpenForReview
                    ? "This submission changed while you were reviewing it. Reload and try again."
                    : $"This submission was already decided ({before.Status}).");
        }

        RewardQuoteDto? rewardDto = null;
        var eventReason = reason;
        var firstForUser = false;
        if (target == SubmissionStatus.Approved)
        {
            var approved = await RecordApprovalEarningsAsync(before, campaign, request.QualityBonusAmount, keySuffix: string.Empty, me, ct);
            rewardDto = RewardQuoteDto.From(approved.Quote, approved.RuleSet.Id, approved.RuleSet.Version);
            if (approved.Quote.AppliedCaps.Count > 0)
                eventReason = $"{(reason is null ? "Approved" : reason)} (caps applied: {string.Join(", ", approved.Quote.AppliedCaps)})";
            firstForUser = !await db.Set<Submission>().AnyAsync(s =>
                s.UserId == before.UserId && s.Id != submissionId && s.Status == SubmissionStatus.Approved, ct);
        }

        await ResolveFlagsAsync(submissionId, action, me, now, ct);
        db.Set<SubmissionEvent>().Add(new SubmissionEvent
        {
            SubmissionId = submissionId, FromStatus = before.Status, ToStatus = target, Action = action, ActorUserId = me,
            Reason = eventReason, CreatedAt = now,
        });
        audit.Record($"submission.{action}", nameof(Submission), submissionId,
            before: new { Status = before.Status.ToString() },
            after: new { Status = target.ToString(), RewardTotal = rewardDto?.Total, rewardDto?.Currency, AppliedCaps = rewardDto?.AppliedCaps, RuleSetVersion = before.RewardRuleSetVersion },
            reason: eventReason);
        await notifications.StageAsync(DecisionNotification(before, campaign, target, reason, rewardDto), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (target == SubmissionStatus.Approved)
            await events.PublishAsync(new SubmissionApproved(submissionId, before.UserId, before.CampaignId, firstForUser, now), ct);

        return new DecisionResultDto(submissionId, target, now, reason, newStamp, rewardDto, await queries.EarningsAsync(submissionId, ct),
            liveStatus, liveDue);
    }

    private static (LiveCheckStatus Status, DateTime? DueAt) LiveCheckFor(Campaign campaign, DateTime postedAt, SubmissionStatus target) =>
        target == SubmissionStatus.Approved && campaign.MinPostLiveHours > 0
            ? (LiveCheckStatus.Pending, postedAt.AddHours(campaign.MinPostLiveHours))
            : (LiveCheckStatus.NotRequired, null);

    /// <summary>
    /// Prices the submission from its recorded rule set version and records one ledger entry per reward line.
    /// Must run inside the caller's transaction after the campaign row is locked.
    /// </summary>
    private async Task<PricedReward> RecordApprovalEarningsAsync(Submission submission, Campaign campaign, decimal? qualityBonus,
        string keySuffix, Guid actor, CancellationToken ct)
    {
        var priced = await quotes.QuoteSubmissionAsync(submission, qualityBonus, ct);
        var requiresLiveCheck = campaign.MinPostLiveHours > 0;
        foreach (var line in priced.Quote.Lines)
        {
            var key = line.Type switch
            {
                EarningType.FirstPostBonus => RewardQuoteService.FirstPostKey(campaign.Id, submission.UserId),
                EarningType.TimeLimitedBonus => $"submission:{submission.Id}:{line.Type}:{line.RuleId}{keySuffix}",
                _ => $"submission:{submission.Id}:{line.Type}{keySuffix}",
            };
            await ledger.RecordAsync(new NewEarning(
                submission.UserId, line.Type, line.Amount, priced.Quote.Currency, key,
                $"{line.Label} — {campaign.Title}", line.RequiresApproval || requiresLiveCheck,
                CampaignId: campaign.Id, SubmissionId: submission.Id, RewardRuleSetId: priced.RuleSet.Id,
                RewardRuleSetVersion: priced.RuleSet.Version, RewardRuleId: line.RuleId, CreatedByUserId: actor), ct);
        }
        return priced;
    }

    private async Task ResolveFlagsAsync(Guid submissionId, string note, Guid me, DateTime now, CancellationToken ct) =>
        await db.Set<SubmissionFlag>().Where(f => f.SubmissionId == submissionId && f.ResolvedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(f => f.ResolvedAt, now).SetProperty(f => f.ResolvedByUserId, me)
                .SetProperty(f => f.ResolutionNote, note), ct);

    private static NotificationRequest DecisionNotification(Submission s, Campaign campaign, SubmissionStatus target, string? reason, RewardQuoteDto? reward)
    {
        var (title, body) = target switch
        {
            SubmissionStatus.Approved => ("Submission approved",
                reward is { Total: > 0 }
                    ? $"Your post for \"{campaign.Title}\" was approved. You earned {reward.Total} {reward.Currency}."
                    : $"Your post for \"{campaign.Title}\" was approved."),
            SubmissionStatus.NeedsCorrection => ("Correction needed", $"Your post for \"{campaign.Title}\" needs a correction: {reason}"),
            _ => ("Submission not approved", $"Your post for \"{campaign.Title}\" was not approved: {reason}"),
        };
        return new NotificationRequest(s.UserId, NotificationTypes.SubmissionDecision, title, body, $"/submissions/{s.Id}",
            new[] { NotificationChannel.Email });
    }

    // ------------------------------------------------------------------ live checks

    public async Task<LiveCheckResultDto> LiveCheckAsync(Guid submissionId, LiveCheckRequest request, CancellationToken ct)
    {
        var me = currentUser.Id;
        var now = Now;
        var result = request.Result!.Value;
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (result == LiveCheckResult.Removed && (note is null || note.Length < 5))
            throw new DomainException("review.reason_required", "Describe what you found (at least 5 characters).");

        var s = await db.Set<Submission>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == submissionId, ct)
            ?? throw DomainException.NotFound("Submission");
        if (result == LiveCheckResult.ConfirmedLive && s.LiveCheckDueAt > now)
            throw DomainException.Conflict("review.live_check_not_due",
                $"The post must stay live until {s.LiveCheckDueAt:yyyy-MM-dd HH:mm} UTC before it can be confirmed.");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await CampaignLock.LockAsync(db, s.CampaignId, ct);
        var target = result == LiveCheckResult.ConfirmedLive ? LiveCheckStatus.ConfirmedLive : LiveCheckStatus.Removed;
        var updated = await db.Set<Submission>()
            .Where(x => x.Id == submissionId && x.Status == SubmissionStatus.Approved && x.LiveCheckStatus == LiveCheckStatus.Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.LiveCheckStatus, target).SetProperty(x => x.LiveCheckedAt, now)
                .SetProperty(x => x.LiveCheckedByUserId, me).SetProperty(x => x.UpdatedAt, now), ct);
        if (updated == 0)
            throw DomainException.Conflict("review.live_check_not_pending", "This submission has no pending live check.");

        SubmissionStatus status;
        if (result == LiveCheckResult.ConfirmedLive)
        {
            var manualRuleIds = await db.Set<RewardRule>()
                .Where(r => r.RuleSetId == s.RewardRuleSetId && r.ApprovalMode == BonusApprovalMode.ManualApproval)
                .Select(r => r.Id).ToListAsync(ct);
            var pending = await db.Set<EarningEntry>()
                .Where(e => e.SubmissionId == submissionId && e.Status == EarningStatus.PendingApproval).ToListAsync(ct);
            foreach (var entry in pending.Where(e => e.RewardRuleId is null || !manualRuleIds.Contains(e.RewardRuleId.Value)))
                await ledger.ApproveAsync(entry, me, ct);
            db.Set<SubmissionEvent>().Add(new SubmissionEvent
            {
                SubmissionId = submissionId, FromStatus = SubmissionStatus.Approved, ToStatus = SubmissionStatus.Approved,
                Action = "live_check_confirmed", ActorUserId = me, Reason = note, CreatedAt = now,
            });
            audit.Record("submission.live_check_confirmed", nameof(Submission), submissionId, after: new { LiveCheckStatus = target.ToString() }, reason: note);
            status = SubmissionStatus.Approved;
        }
        else
        {
            await ReverseCoreAsync(s, $"Post removed before the minimum live duration: {note}", me, now, "live_check_removed", ct);
            status = SubmissionStatus.Reversed;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (status == SubmissionStatus.Reversed)
            await events.PublishAsync(new SubmissionReversed(submissionId, s.UserId, s.CampaignId, note!, now), ct);
        return new LiveCheckResultDto(submissionId, status, target, await queries.EarningsAsync(submissionId, ct));
    }

    // ------------------------------------------------------------------ reversal

    public async Task<ReverseResultDto> ReverseAsync(Guid submissionId, ReverseRequest request, CancellationToken ct)
    {
        if (!request.Confirm)
            throw new DomainException("confirmation.required", "Confirm the reversal by sending \"confirm\": true.");
        var me = currentUser.Id;
        var now = Now;
        var reason = request.Reason.Trim();
        var s = await db.Set<Submission>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == submissionId, ct)
            ?? throw DomainException.NotFound("Submission");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await CampaignLock.LockAsync(db, s.CampaignId, ct);
        await ReverseCoreAsync(s, reason, me, now, "reversed", ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await events.PublishAsync(new SubmissionReversed(submissionId, s.UserId, s.CampaignId, reason, now), ct);
        return new ReverseResultDto(submissionId, SubmissionStatus.Reversed, await queries.EarningsAsync(submissionId, ct));
    }

    /// <summary>Approved → Reversed and every live earning of the submission reversed in the ledger (same transaction).</summary>
    private async Task ReverseCoreAsync(Submission s, string reason, Guid me, DateTime now, string action, CancellationToken ct)
    {
        var updated = await db.Set<Submission>().Where(x => x.Id == s.Id && x.Status == SubmissionStatus.Approved)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, SubmissionStatus.Reversed).SetProperty(x => x.DecidedAt, now)
                .SetProperty(x => x.DecidedByUserId, me).SetProperty(x => x.DecisionReason, reason)
                .SetProperty(x => x.ConcurrencyStamp, Guid.NewGuid()).SetProperty(x => x.UpdatedAt, now), ct);
        if (updated == 0)
            throw DomainException.Conflict("review.not_approved", "Only approved submissions can be reversed.");

        var earnings = await db.Set<EarningEntry>()
            .Where(e => e.SubmissionId == s.Id && e.Type != EarningType.Reversal && e.ReversedByEntryId == null &&
                        e.Status != EarningStatus.Reversed && e.Status != EarningStatus.Declined)
            .ToListAsync(ct);
        foreach (var entry in earnings)
            await ledger.ReverseAsync(entry, reason, me, ct);

        db.Set<SubmissionEvent>().Add(new SubmissionEvent
        {
            SubmissionId = s.Id, FromStatus = SubmissionStatus.Approved, ToStatus = SubmissionStatus.Reversed, Action = action,
            ActorUserId = me, Reason = reason, CreatedAt = now,
        });
        audit.Record("submission.reversed", nameof(Submission), s.Id, new { Status = "Approved" },
            new { Status = "Reversed", ReversedEarnings = earnings.Select(e => new { e.Id, e.Type, e.Amount, e.Currency }) }, reason);
        var title = await db.Set<Campaign>().Where(c => c.Id == s.CampaignId).Select(c => c.Title).FirstAsync(ct);
        await notifications.StageAsync(new NotificationRequest(s.UserId, NotificationTypes.SubmissionReversed, "Approval reversed",
            $"The approval of your post for \"{title}\" was reversed: {reason}", $"/submissions/{s.Id}",
            new[] { NotificationChannel.Email }), ct);
    }

    // ------------------------------------------------------------------ appeals

    public async Task<AppealResolutionDto> ResolveAppealAsync(Guid appealId, ResolveAppealRequest request, CancellationToken ct)
    {
        var me = currentUser.Id;
        var now = Now;
        var note = request.Note.Trim();
        var outcome = request.Outcome!.Value;
        var appeal = await db.Set<Appeal>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == appealId, ct) ?? throw DomainException.NotFound("Appeal");
        var s = await db.Set<Submission>().AsNoTracking().FirstAsync(x => x.Id == appeal.SubmissionId, ct);
        if (appeal.Status != AppealStatus.Open)
            throw DomainException.Conflict("appeal.already_resolved", "This appeal has already been resolved.");
        if (s.DecidedByUserId == me && !currentUser.Roles.Contains(Role.Admin))
            throw DomainException.Forbidden("appeal.same_reviewer", "Appeals must be resolved by someone other than the reviewer who made the decision.");

        var status = outcome == AppealOutcome.Overturned ? AppealStatus.Overturned : AppealStatus.Upheld;
        var stamp = request.ConcurrencyStamp!.Value;
        var firstForUser = false;
        SubmissionStatus submissionStatus = s.Status;

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await CampaignLock.LockAsync(db, s.CampaignId, ct);
        var resolved = await db.Set<Appeal>().Where(a => a.Id == appealId && a.Status == AppealStatus.Open && a.ConcurrencyStamp == stamp)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, status).SetProperty(a => a.ResolvedByUserId, me)
                .SetProperty(a => a.ResolvedAt, now).SetProperty(a => a.ResolutionNote, note)
                .SetProperty(a => a.ConcurrencyStamp, Guid.NewGuid()).SetProperty(a => a.UpdatedAt, now), ct);
        if (resolved == 0)
            throw DomainException.Conflict("appeal.already_resolved", "This appeal was changed by someone else. Reload and try again.");

        var campaign = await db.Set<Campaign>().AsNoTracking().FirstAsync(c => c.Id == s.CampaignId, ct);
        if (status == AppealStatus.Overturned)
        {
            var from = appeal.DecisionAppealed;
            var fresh = await db.Set<Submission>().AsNoTracking().FirstAsync(x => x.Id == s.Id, ct);
            var (liveStatus, liveDue) = LiveCheckFor(campaign, fresh.PostedAt, SubmissionStatus.Approved);
            var reason = $"Appeal overturned: {note}";
            var moved = await db.Set<Submission>().Where(x => x.Id == s.Id && x.Status == from)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, SubmissionStatus.Approved).SetProperty(x => x.DecidedAt, now)
                    .SetProperty(x => x.DecidedByUserId, me).SetProperty(x => x.DecisionReason, reason)
                    .SetProperty(x => x.ConcurrencyStamp, Guid.NewGuid()).SetProperty(x => x.LiveCheckStatus, liveStatus)
                    .SetProperty(x => x.LiveCheckDueAt, liveDue).SetProperty(x => x.LiveCheckedAt, (DateTime?)null)
                    .SetProperty(x => x.LiveCheckedByUserId, (Guid?)null).SetProperty(x => x.UpdatedAt, now), ct);
            if (moved == 0)
                throw DomainException.Conflict("appeal.submission_changed", "The submission's status changed since the appeal was filed.");

            await RecordApprovalEarningsAsync(fresh, campaign, null, $":appeal:{appealId}", me, ct);
            firstForUser = !await db.Set<Submission>().AnyAsync(x =>
                x.UserId == s.UserId && x.Id != s.Id && x.Status == SubmissionStatus.Approved, ct);
            db.Set<SubmissionEvent>().Add(new SubmissionEvent
            {
                SubmissionId = s.Id, FromStatus = from, ToStatus = SubmissionStatus.Approved, Action = "appeal_overturned",
                ActorUserId = me, Reason = note, CreatedAt = now,
            });
            submissionStatus = SubmissionStatus.Approved;
        }
        else
        {
            db.Set<SubmissionEvent>().Add(new SubmissionEvent
            {
                SubmissionId = s.Id, FromStatus = s.Status, ToStatus = s.Status, Action = "appeal_upheld", ActorUserId = me,
                Reason = note, CreatedAt = now,
            });
        }

        audit.Record("appeal.resolved", nameof(Appeal), appealId, new { Status = "Open" },
            new { Status = status.ToString(), SubmissionId = s.Id, SubmissionStatus = submissionStatus.ToString() }, note);
        await notifications.StageAsync(new NotificationRequest(s.UserId, NotificationTypes.AppealResolved,
            status == AppealStatus.Overturned ? "Appeal accepted" : "Appeal reviewed",
            status == AppealStatus.Overturned
                ? $"Your appeal for \"{campaign.Title}\" was accepted and your post is now approved. {note}"
                : $"Your appeal for \"{campaign.Title}\" was reviewed and the original decision stands. {note}",
            $"/submissions/{s.Id}", new[] { NotificationChannel.Email }), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (status == AppealStatus.Overturned)
            await events.PublishAsync(new SubmissionApproved(s.Id, s.UserId, s.CampaignId, firstForUser, now), ct);

        var updatedAppeal = await db.Set<Appeal>().AsNoTracking().FirstAsync(a => a.Id == appealId, ct);
        var myName = await db.Set<User>().Where(u => u.Id == me).Select(u => u.DisplayName).FirstAsync(ct);
        return new AppealResolutionDto(ReviewQueryService.ToAppealDto(updatedAppeal, new Dictionary<Guid, string> { [me] = myName }),
            submissionStatus, await queries.EarningsAsync(s.Id, ct));
    }

    // ------------------------------------------------------------------ assignment

    public async Task<AssignResultDto> AssignAsync(AssignRequest request, CancellationToken ct)
    {
        var reviewerId = request.ReviewerId!.Value;
        var roles = ReviewRoles.Reviewers.ToList();
        var isReviewer = await db.Set<User>().AnyAsync(u => u.Id == reviewerId && u.Status == UserStatus.Active &&
                                                            u.Roles.Any(r => roles.Contains(r.Role)), ct);
        if (!isReviewer)
            throw new DomainException("review.not_a_reviewer", "The selected user can't review submissions.");

        var ids = request.SubmissionIds.Distinct().ToList();
        var open = await db.Set<Submission>().Where(s => ids.Contains(s.Id) &&
                                                         (s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.UnderReview))
            .Select(s => new { s.Id, s.AssignedReviewerId }).ToListAsync(ct);
        var openIds = open.Select(o => o.Id).ToList();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var updated = await db.Set<Submission>()
            .Where(s => openIds.Contains(s.Id) && (s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.UnderReview))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.AssignedReviewerId, reviewerId), ct);
        foreach (var o in open)
            audit.Record("submission.assigned", nameof(Submission), o.Id, new { AssignedReviewerId = o.AssignedReviewerId }, new { AssignedReviewerId = reviewerId });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new AssignResultDto(updated, ids.Except(openIds).ToList());
    }
}
