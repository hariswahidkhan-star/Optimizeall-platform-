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
    INotificationService notifications,
    IAuditLogger audit,
    AchievementEvaluator achievements,
    TimeProvider clock,
    ILogger<ReferralService> logger)
{
    public const string QualifyingSubmissionReversedReason = "Qualifying submission reversed";

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
    /// When the referred participant's only approved submission is reversed, the referral is rejected and its unpaid
    /// reward declined (pending) or reversed (approved). Paid or scheduled rewards are left untouched.
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
        if (entry is { Status: EarningStatus.Paid or EarningStatus.Scheduled })
        {
            logger.LogInformation("Referral {ReferralId}: qualifying submission reversed but reward {EntryId} is already {Status}",
                referral.Id, entry.Id, entry.Status);
            return;
        }

        if (entry is { Status: EarningStatus.PendingApproval })
            ledger.Decline(entry, QualifyingSubmissionReversedReason, null);
        else if (entry is { Status: EarningStatus.Approved, ReversedByEntryId: null })
            await ledger.ReverseAsync(entry, QualifyingSubmissionReversedReason, null, ct);

        referral.Status = ReferralStatus.Rejected;
        referral.RejectionReason = QualifyingSubmissionReversedReason;
        audit.RecordSystem("referral.rejected", nameof(Referral), referral.Id,
            after: new { referral.Status, RewardStatus = entry?.Status }, reason: QualifyingSubmissionReversedReason);
        try
        {
            await db.SaveChangesAsync(ct);
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

    /// <summary>Marketing rejection: declines (pending) or reverses (approved, unpaid) the reward; audited.</summary>
    public async Task<ReferralRejectResult> RejectAsync(Guid referralId, string reason, Guid actorId, CancellationToken ct)
    {
        var referral = await db.Set<Referral>().FirstOrDefaultAsync(r => r.Id == referralId, ct)
                       ?? throw DomainException.NotFound("Referral");
        if (referral.Status == ReferralStatus.Rejected)
            throw DomainException.Conflict("referral.already_rejected", "This referral has already been rejected.");

        var before = new { referral.Status, referral.EarningEntryId };
        var rewardAction = "none";
        if (referral.EarningEntryId is { } entryId)
        {
            var entry = await db.Set<EarningEntry>().FirstAsync(x => x.Id == entryId, ct);
            switch (entry.Status)
            {
                case EarningStatus.PendingApproval:
                    ledger.Decline(entry, reason, actorId);
                    rewardAction = "declined";
                    break;
                case EarningStatus.Approved when entry.ReversedByEntryId is null:
                    await ledger.ReverseAsync(entry, reason, actorId, ct);
                    rewardAction = "reversed";
                    break;
                case EarningStatus.Paid:
                case EarningStatus.Scheduled:
                    rewardAction = "unchanged_" + entry.Status.ToString().ToLowerInvariant();
                    break;
            }
        }

        referral.Status = ReferralStatus.Rejected;
        referral.RejectionReason = reason.Trim();
        audit.Record("referral.rejected", nameof(Referral), referral.Id, before,
            new { referral.Status, rewardAction }, reason);
        await db.SaveChangesAsync(ct);
        return new ReferralRejectResult(referral.Id, referral.Status, rewardAction);
    }
}

public sealed record ReferralRejectResult(Guid Id, ReferralStatus Status, string RewardAction);
