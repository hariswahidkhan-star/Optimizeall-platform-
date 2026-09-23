using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Marketing.Achievements;
using OptimizeAll.Api.Modules.Marketing.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Marketing.Referrals;

/// <summary>
/// Referral lifecycle: created on registration (with fraud signals), qualified by the configured action, rewarded
/// through the ledger (idempotent key "referral:{id}"), reversed when the qualifying submission is reversed, expired
/// by <see cref="ReferralExpiryJob"/>. Every transition is a conditional update or guarded by a unique index, so
/// duplicate or concurrent events cannot double-reward.
/// </summary>
public sealed class ReferralService(
    AppDbContext db,
    ISettingsService settings,
    ILedgerWriter ledger,
    IPayoutReversalCoordinator payoutReversals,
    INotificationService notifications,
    IAuditLogger audit,
    AchievementEvaluator achievements,
    TimeProvider clock,
    ILogger<ReferralService> logger)
{
    public const string QualifyingSubmissionReversedReason = "Qualifying submission reversed";
    public const string RewardReversalHoldReason = "Referral reward reversal pending";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<ReferralProgramSettings> GetProgramAsync(CancellationToken ct) =>
        settings.GetAsync(SettingKeys.ReferralProgram, new ReferralProgramSettings(), ct);

    public static ReferralQualifyingAction ParseAction(string? value) =>
        Enum.TryParse<ReferralQualifyingAction>(value, ignoreCase: true, out var action) && Enum.IsDefined(action)
            ? action
            : ReferralQualifyingAction.FirstApprovedSubmission;

    /// <summary>Creates the referral for a new registration that used another participant's referral code.</summary>
    public async Task CreateFromRegistrationAsync(UserRegistered e, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.ReferralCode)) return;
        var program = await GetProgramAsync(ct);
        if (!program.Enabled) return;

        var code = e.ReferralCode.Trim().ToUpperInvariant();
        var referrer = await db.Set<User>().AsNoTracking()
            .Where(u => u.ReferralCode == code)
            .Select(u => new { u.Id, u.Email, u.Status })
            .FirstOrDefaultAsync(ct);
        // Self-referral is impossible: the code must belong to a different user (also a DB check constraint).
        if (referrer is null || referrer.Id == e.UserId) return;

        var referred = await db.Set<User>().AsNoTracking()
            .Where(u => u.Id == e.UserId).Select(u => new { u.Id, u.Email }).FirstOrDefaultAsync(ct);
        if (referred is null) return;
        if (await db.Set<Referral>().AnyAsync(r => r.ReferredUserId == e.UserId, ct)) return;

        var now = Now;
        var ipWindowStart = now - ReferralFraudRules.SharedIpWindow;
        var device = e.DeviceHash;
        var prior = await db.Set<Referral>().AsNoTracking()
            .Where(r => r.ReferrerUserId == referrer.Id &&
                        (r.CreatedAt >= ipWindowStart || (device != null && r.DeviceHash == device)))
            .Select(r => new PriorReferral(r.RegistrationIpHash, r.DeviceHash, r.CreatedAt))
            .ToListAsync(ct);
        // The referrer's own registration signals (when they were referred themselves).
        var own = await db.Set<Referral>().AsNoTracking()
            .Where(r => r.ReferredUserId == referrer.Id)
            .Select(r => new { r.RegistrationIpHash, r.DeviceHash })
            .FirstOrDefaultAsync(ct);

        var signals = ReferralFraudRules.Evaluate(new ReferralFraudContext(
            referrer.Email, referred.Email, e.IpHash, e.DeviceHash, prior, now, own?.RegistrationIpHash, own?.DeviceHash));

        var referral = new Referral
        {
            ReferrerUserId = referrer.Id,
            ReferredUserId = e.UserId,
            CodeUsed = code.Length > 32 ? code[..32] : code,
            Status = ReferralStatus.Registered,
            QualifyingAction = ParseAction(program.QualifyingAction),
            QualifyBy = now.AddDays(Math.Max(1, program.QualifyWithinDays)),
            RegistrationIpHash = e.IpHash,
            DeviceHash = e.DeviceHash,
            FraudSignals = ReferralFraudRules.Join(signals),
        };
        db.Set<Referral>().Add(referral);
        audit.RecordSystem("referral.created", nameof(Referral), referral.Id,
            after: new { referral.ReferrerUserId, referral.ReferredUserId, referral.FraudSignals });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Duplicate event: the referred user already has a referral.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Qualifies the referral of <paramref name="referredUserId"/> when <paramref name="action"/> is its qualifying
    /// action, it is still Registered and within its window. Creates the referrer's reward in the same transaction.
    /// </summary>
    public async Task QualifyAsync(Guid referredUserId, ReferralQualifyingAction action, CancellationToken ct)
    {
        var referral = await db.Set<Referral>().AsNoTracking().FirstOrDefaultAsync(r => r.ReferredUserId == referredUserId, ct);
        if (referral is null || referral.Status != ReferralStatus.Registered || referral.QualifyingAction != action) return;

        var now = Now;
        if (now > referral.QualifyBy) return; // left for the expiry job

        var program = await GetProgramAsync(ct);
        var qualified = false;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // Serialize reward decisions per referrer so the reward cap cannot be exceeded by concurrent events.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT Id FROM users WHERE Id = {referral.ReferrerUserId.ToString()} FOR UPDATE", ct);

            var won = await db.Set<Referral>()
                .Where(r => r.Id == referral.Id && r.Status == ReferralStatus.Registered && r.QualifyBy >= now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, ReferralStatus.Qualified)
                    .SetProperty(r => r.QualifiedAt, now)
                    .SetProperty(r => r.UpdatedAt, now)
                    .SetProperty(r => r.ConcurrencyStamp, Guid.NewGuid()), ct);
            if (won == 0) return; // another handler already qualified (or rejected/expired) it

            var tracked = await db.Set<Referral>().FirstAsync(r => r.Id == referral.Id, ct);
            var rewarded = await db.Set<Referral>()
                .CountAsync(r => r.ReferrerUserId == referral.ReferrerUserId && r.EarningEntryId != null, ct);

            EarningEntry? entry = null;
            if (!program.Enabled)
            {
                tracked.RejectionReason = "No reward: the referral program was disabled when this referral qualified.";
            }
            else if (rewarded >= program.MaxRewardedReferralsPerUser)
            {
                tracked.RejectionReason =
                    $"cap reached: at most {program.MaxRewardedReferralsPerUser} referrals are rewarded per participant.";
            }
            else if (program.ReferrerRewardAmount > 0)
            {
                var signals = ReferralFraudRules.Split(tracked.FraudSignals);
                entry = await ledger.RecordAsync(new NewEarning(
                    tracked.ReferrerUserId, EarningType.ReferralReward, program.ReferrerRewardAmount, program.Currency,
                    $"referral:{tracked.Id}", "Referral reward",
                    RequiresApproval: program.RequireManualApproval || signals.Count > 0,
                    ReferralId: tracked.Id), ct);
                tracked.EarningEntryId = entry.Id;
            }

            audit.RecordSystem("referral.qualified", nameof(Referral), tracked.Id,
                after: new { tracked.QualifyingAction, tracked.EarningEntryId, tracked.RejectionReason });

            await notifications.StageAsync(new NotificationRequest(
                tracked.ReferrerUserId,
                NotificationTypes.ReferralQualified,
                "Your referral qualified",
                entry is not null
                    ? $"Someone you invited completed their qualifying step. A referral reward of {entry.Amount:0.00} {entry.Currency} " +
                      (entry.Status == EarningStatus.PendingApproval ? "is pending approval." : "has been added to your earnings.")
                    : "Someone you invited completed their qualifying step. " +
                      (tracked.RejectionReason ?? "No reward applies to this referral."),
                "/referrals",
                new[] { NotificationChannel.InApp, NotificationChannel.Email }), ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            qualified = true;
        }

        if (qualified)
            await achievements.EvaluateAsync(referral.ReferrerUserId, ct);
    }

    /// <summary>Qualification for "FirstApprovedSubmission": accepts the event flag or an approved submission on record.</summary>
    public async Task OnSubmissionApprovedAsync(SubmissionApproved e, CancellationToken ct)
    {
        if (!e.IsFirstApprovedSubmissionForUser &&
            !await db.Set<Submission>().AnyAsync(s => s.UserId == e.UserId && s.Status == SubmissionStatus.Approved, ct))
            return;
        await QualifyAsync(e.UserId, ReferralQualifyingAction.FirstApprovedSubmission, ct);
    }

    /// <summary>
    /// When the referred participant's only approved submission is reversed, the referral is rejected and its reward
    /// undone through <see cref="UndoRewardAsync"/> (declined, reversed, clawed back, or held for reversal).
    /// </summary>
    public async Task OnSubmissionReversedAsync(SubmissionReversed e, CancellationToken ct)
    {
        var referral = await db.Set<Referral>().FirstOrDefaultAsync(r => r.ReferredUserId == e.UserId, ct);
        if (referral is null || referral.Status != ReferralStatus.Qualified ||
            referral.QualifyingAction != ReferralQualifyingAction.FirstApprovedSubmission) return;

        var stillApproved = await db.Set<Submission>().AnyAsync(s =>
            s.UserId == e.UserId && s.Id != e.SubmissionId && s.Status == SubmissionStatus.Approved, ct);
        if (stillApproved) return;

        EarningEntry? entry = referral.EarningEntryId is { } entryId
            ? await db.Set<EarningEntry>().FirstOrDefaultAsync(x => x.Id == entryId, ct)
            : null;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rewardAction = entry is null ? "none" : await UndoRewardAsync(referral, entry, QualifyingSubmissionReversedReason, null, ct);

        referral.Status = ReferralStatus.Rejected;
        referral.RejectionReason = QualifyingSubmissionReversedReason;
        audit.RecordSystem("referral.rejected", nameof(Referral), referral.Id,
            after: new { referral.Status, RewardStatus = entry?.Status, rewardAction }, reason: QualifyingSubmissionReversedReason);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent duplicate event already processed this referral.
            db.ChangeTracker.Clear();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Marks Registered referrals past their qualifying window as Expired. Returns the number expired.</summary>
    public Task<int> ExpireAsync(CancellationToken ct)
    {
        var now = Now;
        return db.Set<Referral>()
            .Where(r => r.Status == ReferralStatus.Registered && r.QualifyBy < now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ReferralStatus.Expired)
                .SetProperty(r => r.UpdatedAt, now)
                .SetProperty(r => r.ConcurrencyStamp, Guid.NewGuid()), ct);
    }

    /// <summary>
    /// Undoes the reward of a referral being rejected, in the caller's transaction. Returns the action taken:
    /// <list type="bullet">
    /// <item><c>declined</c> — PendingApproval → Declined.</item>
    /// <item><c>reversed</c> — Approved (unpaid) → Reversed.</item>
    /// <item><c>clawback</c> — Paid: <see cref="ILedgerWriter.ReverseAsync"/> creates a negative entry netted against
    /// the referrer's next payout.</item>
    /// <item><c>held_and_reversed</c> — Scheduled in a Draft batch: the referrer's item is held
    /// ("Referral reward reversal pending"), which releases the reward back to Approved, and it is then reversed.</item>
    /// <item><c>pending_reversal</c> — Scheduled in a Finalized batch: nothing can change in the frozen batch, so a
    /// <c>referral.reward_pending_reversal</c> (PendingReversal) audit note is recorded and the item is flagged by the
    /// reconciliation report (<c>pending_reversal</c> warning) until finance reverses the reward.</item>
    /// </list>
    /// </summary>
    private async Task<string> UndoRewardAsync(Referral referral, EarningEntry entry, string reason, Guid? actor, CancellationToken ct)
    {
        if (entry.ReversedByEntryId is not null || entry.Status is EarningStatus.Reversed) return "already_reversed";
        switch (entry.Status)
        {
            case EarningStatus.PendingApproval:
                ledger.Decline(entry, reason, actor);
                return "declined";
            case EarningStatus.Approved:
                await ledger.ReverseAsync(entry, reason, actor, ct);
                return "reversed";
            case EarningStatus.Paid:
                await ledger.ReverseAsync(entry, reason, actor, ct);
                return "clawback";
            case EarningStatus.Scheduled:
                var scheduled = (await payoutReversals.FindScheduledAsync(new[] { entry.Id }, ct)).SingleOrDefault()
                                ?? throw DomainException.Conflict("payout.earnings_changed", "The reward's payout item changed; try again.");
                if (scheduled.BatchStatus == PayoutBatchStatus.Draft)
                {
                    await payoutReversals.HoldForReversalAsync(scheduled, RewardReversalHoldReason, ct);
                    await db.Entry(entry).ReloadAsync(ct); // now Approved and unlinked
                    await ledger.ReverseAsync(entry, reason, actor, ct);
                    return "held_and_reversed";
                }
                var note = new
                {
                    PendingReversal = true, ReferralId = referral.Id, EarningId = entry.Id, scheduled.ItemId, scheduled.BatchId,
                    scheduled.BatchReference, scheduled.ItemStatus,
                };
                if (actor is null)
                    audit.RecordSystem("referral.reward_pending_reversal", nameof(EarningEntry), entry.Id, after: note, reason: reason);
                else
                    audit.Record("referral.reward_pending_reversal", nameof(EarningEntry), entry.Id, after: note, reason: reason);
                logger.LogWarning("Referral {ReferralId}: reward {EntryId} is in finalized batch {Batch}; flagged PendingReversal",
                    referral.Id, entry.Id, scheduled.BatchReference);
                return "pending_reversal";
            default:
                return "none";
        }
    }

    /// <summary>Marketing rejection: undoes the reward (see <see cref="UndoRewardAsync"/>); audited.</summary>
    public async Task<ReferralRejectResult> RejectAsync(Guid referralId, string reason, Guid actorId, CancellationToken ct)
    {
        var referral = await db.Set<Referral>().FirstOrDefaultAsync(r => r.Id == referralId, ct)
                       ?? throw DomainException.NotFound("Referral");
        if (referral.Status == ReferralStatus.Rejected)
            throw DomainException.Conflict("referral.already_rejected", "This referral has already been rejected.");

        var before = new { referral.Status, referral.EarningEntryId };
        var rewardAction = "none";
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (referral.EarningEntryId is { } entryId)
        {
            var entry = await db.Set<EarningEntry>().FirstAsync(x => x.Id == entryId, ct);
            rewardAction = await UndoRewardAsync(referral, entry, reason.Trim(), actorId, ct);
        }

        referral.Status = ReferralStatus.Rejected;
        referral.RejectionReason = reason.Trim();
        audit.Record("referral.rejected", nameof(Referral), referral.Id, before,
            new { referral.Status, rewardAction }, reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ReferralRejectResult(referral.Id, referral.Status, rewardAction);
    }
}

public sealed record ReferralRejectResult(Guid Id, ReferralStatus Status, string RewardAction);
