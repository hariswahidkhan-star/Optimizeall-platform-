using System.Data;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Errors;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Campaigns;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Submissions;

public interface ISubmissionService
{
    Task<MySubmissionDetailDto> CreateAsync(CreateSubmissionForm form, CancellationToken ct);
    Task<MySubmissionDetailDto> ResubmitAsync(Guid id, UpdateSubmissionForm form, CancellationToken ct);
    Task<PagedResult<MySubmissionListItemDto>> ListMineAsync(MySubmissionsQuery query, CancellationToken ct);
    Task<MySubmissionDetailDto> GetMineAsync(Guid id, CancellationToken ct);
    Task<MySubmissionDetailDto> AppealAsync(Guid id, AppealRequest request, CancellationToken ct);
    Task<MySubmissionDetailDto> WithdrawAsync(Guid id, WithdrawSubmissionRequest request, CancellationToken ct);
}

/// <summary>Participant proof submission, correction and appeal.</summary>
public sealed class SubmissionService(
    AppDbContext db,
    ICurrentUser currentUser,
    IParticipantEligibility eligibility,
    IRewardQuoteService quotes,
    Rates.IPersonalRateService personalRates,
    IFileService files,
    ISettingsService settings,
    IAuditLogger audit,
    INotificationService notifications,
    IEventPublisher events,
    TimeProvider clock) : ISubmissionService
{
    public static readonly TimeSpan MaxPostedAtSkew = SubmissionTiming.MaxFutureSkew;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ create

    public async Task<MySubmissionDetailDto> CreateAsync(CreateSubmissionForm form, CancellationToken ct)
    {
        var me = currentUser.Id;
        var now = Now;
        var campaign = await LoadCampaignAsync(form.CampaignId!.Value, ct);
        EnsureOpen(campaign, now);

        var platform = form.Platform!.Value;
        var postedAt = form.PostedAt!.Value.UtcDateTime;
        var account = await ValidateParticipantAsync(campaign, me, form.SocialAccountId!.Value, platform, ct);
        var (normalizedUrl, isShortLink) = ValidateUrl(platform, form.PostUrl);
        var format = ContentFormats.Resolve(platform, form.PostUrl, form.Format);
        ValidatePostedAt(postedAt, now, submittedAt: now);
        if (await db.Set<Submission>().AnyAsync(s => s.NormalizedPostUrl == normalizedUrl, ct))
            throw DuplicateUrl();
        if (campaign.RequireScreenshot && form.Screenshot is null)
            throw new DomainException("submission.screenshot_required", "This campaign requires a screenshot of your post.");

        Guid? variantId = null;
        if (form.ExperimentVariantId is { } candidate)
        {
            var valid = await (from v in db.Set<ExperimentVariant>()
                               join e in db.Set<Experiment>() on v.ExperimentId equals e.Id
                               where v.Id == candidate && e.CampaignId == campaign.Id && e.Status == ExperimentStatus.Running
                               select v.Id).AnyAsync(ct);
            variantId = valid ? candidate : null;
        }

        StoredFile? screenshot = null;
        Submission submission;
        try
        {
            await using var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted);
            // Serialize this participant's submissions so the per-campaign limit cannot be exceeded by parallel requests.
            await db.Dialect().LockRowAsync(db, "users", me, ct);

            var active = await db.Set<Submission>().CountAsync(s =>
                s.UserId == me && s.CampaignId == campaign.Id && s.Status != SubmissionStatus.Rejected &&
                s.Status != SubmissionStatus.Withdrawn, ct);
            if (active >= campaign.MaxSubmissionsPerParticipant)
                throw DomainException.Conflict("submission.limit_reached",
                    $"You have reached the limit of {campaign.MaxSubmissionsPerParticipant} submission{(campaign.MaxSubmissionsPerParticipant == 1 ? "" : "s")} for this campaign.");

            var ruleSet = await quotes.GetCurrentRuleSetAsync(campaign.Id, now, ct)
                ?? throw DomainException.Conflict("campaign.no_reward_rules", "This campaign has no reward rules yet.");

            if (form.Screenshot is not null)
                screenshot = await files.SaveImageAsync(form.Screenshot, FilePurpose.SubmissionScreenshot, me, isPublic: false, ct);

            submission = new Submission
            {
                CampaignId = campaign.Id,
                UserId = me,
                SocialAccountId = account.Id,
                Platform = platform,
                Format = format,
                PostUrl = form.PostUrl.Trim(),
                NormalizedPostUrl = normalizedUrl,
                PostedAt = postedAt,
                CaptionText = Blank(form.CaptionText),
                ContentHash = Normalization.ContentHash(form.CaptionText),
                ScreenshotFileId = screenshot?.Id,
                ScreenshotSha256 = screenshot?.Sha256,
                Status = SubmissionStatus.Pending,
                SubmittedAt = now,
                RewardRuleSetId = ruleSet.Id,
                RewardRuleSetVersion = ruleSet.Version,
                RewardCurrency = ruleSet.Currency,
                ExperimentVariantId = variantId,
            };

            var context = await quotes.BuildContextAsync(campaign, ruleSet.Currency, me, platform, postedAt, submission.SubmittedAt,
                null, submission.Id, ct);
            // The person-level rate is resolved once, now, and locked on the submission (like the rule set version).
            var personalRate = await personalRates.ResolveForSubmissionAsync(submission, ruleSet, ct);
            if (personalRate is not null)
            {
                db.Set<SubmissionRate>().Add(personalRate);
                context = context with { PersonalRate = Rates.PersonalRateService.ToInput(personalRate) };
            }
            submission.EstimatedRewardAmount = RewardEngine.Quote(ruleSet, context).Total;

            await ApplyRiskFlagsAsync(submission, campaign, account, isShortLink, ct);
            submission.Events.Add(new SubmissionEvent
            {
                SubmissionId = submission.Id, FromStatus = null, ToStatus = SubmissionStatus.Pending, Action = "submitted",
                ActorUserId = me, CreatedAt = now,
            });
            db.Set<Submission>().Add(submission);
            audit.Record("submission.created", nameof(Submission), submission.Id, after: new
            {
                submission.CampaignId, submission.Platform, submission.Format, submission.NormalizedPostUrl, submission.RewardRuleSetVersion,
                submission.EstimatedRewardAmount, submission.RewardCurrency, submission.RiskScore,
                RateSource = personalRate?.Level.ToString() ?? nameof(RateSourceLevel.CampaignRules),
                RateSourceLabel = personalRate?.SourceLabel, PersonalRate = personalRate?.Amount,
            });
            await notifications.StageAsync(new NotificationRequest(me, NotificationTypes.SubmissionReceived,
                "Submission received", $"We received your post for \"{campaign.Title}\". A reviewer will check it soon.",
                AppLinks.Submission(submission.Id)), ct);

            await SaveMappingDuplicateUrlAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            if (screenshot is not null) files.Discard(screenshot);
            throw;
        }

        await events.PublishAsync(new SubmissionCreated(submission.Id, me, campaign.Id, now), ct);
        return await GetMineAsync(submission.Id, ct);
    }

    // ------------------------------------------------------------------ resubmit

    public async Task<MySubmissionDetailDto> ResubmitAsync(Guid id, UpdateSubmissionForm form, CancellationToken ct)
    {
        var me = currentUser.Id;
        var now = Now;
        var submission = await db.Set<Submission>().Include(s => s.Flags)
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == me, ct) ?? throw DomainException.NotFound("Submission");
        if (submission.Status != SubmissionStatus.NeedsCorrection)
            throw DomainException.Conflict("submission.not_editable", "Only submissions that need a correction can be edited.");

        var campaign = await LoadCampaignAsync(submission.CampaignId, ct);
        EnsureOpen(campaign, now);
        var account = await ValidateParticipantAsync(campaign, me, submission.SocialAccountId, submission.Platform, ct);

        var postUrl = string.IsNullOrWhiteSpace(form.PostUrl) ? submission.PostUrl : form.PostUrl.Trim();
        var (normalizedUrl, isShortLink) = ValidateUrl(submission.Platform, postUrl);
        var postedAt = form.PostedAt?.UtcDateTime ?? submission.PostedAt;
        // Re-runs every PostedAt rule against the original SubmittedAt (which a correction does not change).
        ValidatePostedAt(postedAt, now, submission.SubmittedAt);
        if (await db.Set<Submission>().AnyAsync(s => s.NormalizedPostUrl == normalizedUrl && s.Id != id, ct))
            throw DuplicateUrl();
        if (campaign.RequireScreenshot && form.Screenshot is null && submission.ScreenshotFileId is null)
            throw new DomainException("submission.screenshot_required", "This campaign requires a screenshot of your post.");

        StoredFile? screenshot = null;
        try
        {
            await using var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted);
            if (form.Screenshot is not null)
            {
                screenshot = await files.SaveImageAsync(form.Screenshot, FilePurpose.SubmissionScreenshot, me, isPublic: false, ct);
                submission.ScreenshotFileId = screenshot.Id;
                submission.ScreenshotSha256 = screenshot.Sha256;
            }

            var before = new { submission.PostUrl, submission.PostedAt, submission.CaptionText, submission.ScreenshotSha256 };
            submission.PostUrl = postUrl;
            submission.NormalizedPostUrl = normalizedUrl;
            submission.PostedAt = postedAt;
            if (form.CaptionText is not null)
            {
                submission.CaptionText = Blank(form.CaptionText);
                submission.ContentHash = Normalization.ContentHash(form.CaptionText);
            }

            // Estimated reward is re-priced with the ORIGINAL captured rule set version.
            var ruleSet = await quotes.LoadRuleSetAsync(submission.RewardRuleSetId, ct);
            var context = await quotes.BuildContextAsync(campaign, ruleSet.Currency, me, submission.Platform, postedAt,
                submission.SubmittedAt, null, submission.Id, ct);
            // ... and with the person-level rate locked at creation (never re-resolved on a correction).
            var lockedRate = await db.Set<SubmissionRate>().AsNoTracking().FirstOrDefaultAsync(r => r.SubmissionId == submission.Id, ct);
            if (lockedRate is not null) context = context with { PersonalRate = Rates.PersonalRateService.ToInput(lockedRate) };
            submission.EstimatedRewardAmount = RewardEngine.Quote(ruleSet, context).Total;

            db.RemoveRange(submission.Flags.Where(f => f.ResolvedAt is null).ToList());
            await ApplyRiskFlagsAsync(submission, campaign, account, isShortLink, ct);

            submission.Status = SubmissionStatus.Pending;
            submission.CorrectionCount++;
            submission.ClaimedByUserId = null;
            submission.ClaimExpiresAt = null;
            db.Set<SubmissionEvent>().Add(new SubmissionEvent
            {
                SubmissionId = submission.Id, FromStatus = SubmissionStatus.NeedsCorrection, ToStatus = SubmissionStatus.Pending,
                Action = "resubmitted", ActorUserId = me, CreatedAt = now,
            });
            audit.Record("submission.resubmitted", nameof(Submission), submission.Id, before,
                new { submission.PostUrl, submission.PostedAt, submission.CaptionText, submission.ScreenshotSha256, submission.CorrectionCount });

            await SaveMappingDuplicateUrlAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            if (screenshot is not null) files.Discard(screenshot);
            throw;
        }
        return await GetMineAsync(id, ct);
    }

    // ------------------------------------------------------------------ reads

    public async Task<PagedResult<MySubmissionListItemDto>> ListMineAsync(MySubmissionsQuery query, CancellationToken ct)
    {
        var me = currentUser.Id;
        var q = from s in db.Set<Submission>().AsNoTracking()
                join c in db.Set<Campaign>() on s.CampaignId equals c.Id
                where s.UserId == me
                select new { s, c };
        if (query.Status is { } status) q = q.Where(x => x.s.Status == status);
        if (query.CampaignId is { } campaignId) q = q.Where(x => x.s.CampaignId == campaignId);
        return await q.OrderByDescending(x => x.s.SubmittedAt).ThenByDescending(x => x.s.Id)
            .Select(x => new MySubmissionListItemDto(x.s.Id, new CampaignRefDto(x.c.Id, x.c.Slug, x.c.Title), x.s.Platform, x.s.PostUrl,
                x.s.Status, x.s.SubmittedAt, x.s.EstimatedRewardAmount, x.s.RewardCurrency, x.s.DecisionReason))
            .ToPagedAsync(query, ct);
    }

    public async Task<MySubmissionDetailDto> GetMineAsync(Guid id, CancellationToken ct)
    {
        var me = currentUser.Id;
        var s = await db.Set<Submission>().AsNoTracking().Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == me, ct) ?? throw DomainException.NotFound("Submission");
        var campaign = await db.Set<Campaign>().AsNoTracking().Where(c => c.Id == s.CampaignId)
            .Select(c => new CampaignRefDto(c.Id, c.Slug, c.Title)).FirstAsync(ct);
        var account = await db.Set<SocialAccount>().AsNoTracking().Where(a => a.Id == s.SocialAccountId)
            .Select(a => new SocialAccountRefDto(a.Id, a.Platform, a.Handle)).FirstAsync(ct);
        var earnings = await db.Set<EarningEntry>().AsNoTracking()
            .Where(e => e.SubmissionId == id && e.UserId == me)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Type)
            .Select(e => new SubmissionEarningDto(e.Id, e.Type, e.Amount, e.Currency, e.Status, e.CreatedAt)).ToListAsync(ct);
        var appeals = await db.Set<Appeal>().AsNoTracking().Where(a => a.SubmissionId == id).OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
        var latest = appeals.FirstOrDefault();
        var rateLevel = await db.Set<SubmissionRate>().AsNoTracking().Where(r => r.SubmissionId == id)
            .Select(r => (RateSourceLevel?)r.Level).FirstOrDefaultAsync(ct);

        var (canAppeal, deadline) = await AppealEligibilityAsync(s, appeals, ct);
        var timeline = s.Events.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new TimelineEntryDto(e.Action, e.FromStatus, e.ToStatus, e.Reason,
                e.ActorUserId is null ? "System" : e.ActorUserId == me ? "You" : "Reviewer", e.CreatedAt)).ToList();

        return new MySubmissionDetailDto(
            s.Id, campaign, s.Platform, account, s.PostUrl, s.PostedAt, s.CaptionText,
            s.ScreenshotFileId is { } f ? FileUrls.For(f) : null, s.Status, s.SubmittedAt, s.DecidedAt, s.DecisionReason,
            s.CorrectionCount, s.EstimatedRewardAmount, s.RewardCurrency, s.RewardRuleSetVersion,
            new LiveCheckDto(s.LiveCheckStatus, s.LiveCheckDueAt, s.LiveCheckedAt), timeline, earnings,
            latest is null ? null : new AppealSummaryDto(latest.Id, latest.Status, latest.DecisionAppealed, latest.Reason,
                latest.ResolutionNote, latest.CreatedAt, latest.ResolvedAt),
            s.Status == SubmissionStatus.NeedsCorrection, canAppeal, deadline, s.CanWithdraw, s.Format,
            // Participants learn whether their own deal priced the post, never which card or group it was.
            rateLevel is null ? null : RateSources.IsPersonal(rateLevel.Value) ? "Personal" : "Special");
    }

    private async Task<(bool CanAppeal, DateTime? Deadline)> AppealEligibilityAsync(Submission s, IReadOnlyList<Appeal> appeals, CancellationToken ct)
    {
        if (s.Status is not (SubmissionStatus.Rejected or SubmissionStatus.Reversed) || s.DecidedAt is null) return (false, null);
        var days = await settings.GetAsync(SettingKeys.AppealWindowDays, 14, ct);
        var deadline = s.DecidedAt.Value.AddDays(days);
        var can = Now <= deadline &&
                  appeals.All(a => a.Status != AppealStatus.Open) &&
                  appeals.All(a => a.CreatedAt < s.DecidedAt.Value);
        return (can, deadline);
    }

    // ------------------------------------------------------------------ appeal

    public async Task<MySubmissionDetailDto> AppealAsync(Guid id, AppealRequest request, CancellationToken ct)
    {
        var me = currentUser.Id;
        var now = Now;
        var reason = request.Reason.Trim();
        if (reason.Length < 20)
            throw new DomainException("appeal.reason_too_short", "Explain your appeal in at least 20 characters.");

        await using var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted);
        await db.Dialect().LockRowAsync(db, "submissions", id, ct);
        var s = await db.Set<Submission>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == me, ct)
            ?? throw DomainException.NotFound("Submission");
        var appeals = await db.Set<Appeal>().AsNoTracking().Where(a => a.SubmissionId == id).ToListAsync(ct);
        var (canAppeal, _) = await AppealEligibilityAsync(s, appeals, ct);
        if (!canAppeal)
            throw DomainException.Conflict("appeal.not_allowed",
                "This decision can't be appealed (only rejections and reversals, once, within the appeal window).");

        var appeal = new Appeal { SubmissionId = id, UserId = me, DecisionAppealed = s.Status, Reason = reason, Status = AppealStatus.Open };
        db.Set<Appeal>().Add(appeal);
        // The appeal keeps the full text (2000); the timeline and audit copies are cut to their 1000-char columns.
        db.Set<SubmissionEvent>().Add(new SubmissionEvent
        {
            SubmissionId = id, FromStatus = s.Status, ToStatus = s.Status, Action = "appealed", ActorUserId = me,
            Reason = ReasonText.Fit(reason), CreatedAt = now,
        });
        audit.Record("submission.appealed", nameof(Submission), id, after: new { AppealId = appeal.Id, DecisionAppealed = s.Status.ToString() },
            reason: ReasonText.Fit(reason));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetMineAsync(id, ct);
    }

    // ------------------------------------------------------------------ withdraw

    /// <summary>
    /// Pending / UnderReview / NeedsCorrection → Withdrawn. A single conditional update on the expected status (and the
    /// post key read just before), so it races safely with a reviewer's decision, which is itself conditional on
    /// Pending/UnderReview and the concurrency stamp that this update replaces: exactly one of the two wins. No earnings
    /// exist before a decision, so nothing is reversed; the post key is released so the post can be submitted again.
    /// </summary>
    public async Task<MySubmissionDetailDto> WithdrawAsync(Guid id, WithdrawSubmissionRequest request, CancellationToken ct)
    {
        if (!request.Confirm)
            throw new DomainException("confirmation.required", "Confirm the withdrawal by sending \"confirm\": true.");
        var me = currentUser.Id;
        var now = Now;
        var reason = ReasonText.Fit(Blank(request.Reason));
        // A reviewer may claim (Pending → UnderReview) between the read and the update; the status is then re-read and the
        // withdrawal retried. A decision (Approved / Rejected) is final and ends the loop with 409.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var s = await db.Set<Submission>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == me, ct)
                ?? throw DomainException.NotFound("Submission");
            if (!s.CanWithdraw) throw NotWithdrawable(s.Status);
            var from = s.Status;
            var key = s.NormalizedPostUrl;
            var releasedKey = WithdrawnKey(s.Id, key);

            await using var tx = await db.Dialect().BeginWriteTransactionAsync(db, ct, IsolationLevel.ReadCommitted);
            // Same lock order as a reviewer's decision (campaign row first, then the submission), so a withdrawal racing a
            // decision waits for it instead of deadlocking on MySQL; the loser then gets the 409 below.
            await CampaignLock.LockAsync(db, s.CampaignId, ct);
            var updated = await db.Set<Submission>()
                .Where(x => x.Id == id && x.UserId == me && x.Status == from && x.NormalizedPostUrl == key)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(x => x.Status, SubmissionStatus.Withdrawn)
                    .SetProperty(x => x.NormalizedPostUrl, releasedKey)
                    .SetProperty(x => x.ClaimedByUserId, (Guid?)null).SetProperty(x => x.ClaimExpiresAt, (DateTime?)null)
                    .SetProperty(x => x.ConcurrencyStamp, Guid.NewGuid()).SetProperty(x => x.UpdatedAt, now), ct);
            if (updated == 0)
            {
                await tx.RollbackAsync(ct);
                continue;
            }

            // Open risk flags no longer need a reviewer's attention.
            await db.Set<SubmissionFlag>().Where(f => f.SubmissionId == id && f.ResolvedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(f => f.ResolvedAt, now).SetProperty(f => f.ResolvedByUserId, me)
                    .SetProperty(f => f.ResolutionNote, "withdrawn by participant"), ct);
            db.Set<SubmissionEvent>().Add(new SubmissionEvent
            {
                SubmissionId = id, FromStatus = from, ToStatus = SubmissionStatus.Withdrawn, Action = "withdrawn", ActorUserId = me,
                Reason = reason, CreatedAt = now,
            });
            audit.Record("submission.withdrawn", nameof(Submission), id,
                before: new { Status = from.ToString(), NormalizedPostUrl = key },
                after: new { Status = nameof(SubmissionStatus.Withdrawn), s.EstimatedRewardAmount, s.RewardCurrency }, reason: reason);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await GetMineAsync(id, ct);
        }
        throw DomainException.Conflict("submission.changed", "This submission changed a moment ago. Reload and try again.");
    }

    /// <summary>The released post key of a withdrawn submission: unique per submission and never a valid post key.</summary>
    public static string WithdrawnKey(Guid submissionId, string key)
    {
        var value = $"{Submission.WithdrawnKeyPrefix}{submissionId:N}:{key}";
        return value.Length <= 768 ? value : value[..768];
    }

    private static DomainException NotWithdrawable(SubmissionStatus status) =>
        DomainException.Conflict("submission.not_withdrawable", status == SubmissionStatus.Withdrawn
            ? "This submission was already withdrawn."
            : $"This submission can no longer be withdrawn because it was already decided ({status}).");

    // ------------------------------------------------------------------ helpers

    private async Task<Campaign> LoadCampaignAsync(Guid campaignId, CancellationToken ct)
    {
        var campaign = await db.Set<Campaign>().AsNoTracking().Include(c => c.Platforms).FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null || campaign.Status is CampaignStatus.Draft or CampaignStatus.Archived)
            throw DomainException.NotFound("Campaign");
        return campaign;
    }

    private static void EnsureOpen(Campaign campaign, DateTime now)
    {
        if (!campaign.IsOpenForSubmissions(now))
            throw DomainException.Conflict("submission.campaign_closed",
                campaign.Status == CampaignStatus.Active && now > campaign.SubmissionDeadline
                    ? "The submission deadline for this campaign has passed."
                    : "This campaign is not accepting submissions right now.");
    }

    private async Task<SocialAccount> ValidateParticipantAsync(Campaign campaign, Guid me, Guid socialAccountId, SocialPlatform platform, CancellationToken ct)
    {
        var participant = await eligibility.LoadAsync(me, ct);
        var account = participant.Accounts.FirstOrDefault(a => a.Id == socialAccountId)
            ?? throw new DomainException("submission.social_account_invalid", "Choose one of your own social profiles.");
        if (campaign.Platforms.All(p => p.Platform != platform))
            throw new DomainException("submission.platform_not_allowed", $"{platform} is not part of this campaign.");
        if (account.Platform != platform)
            throw new DomainException("submission.platform_mismatch", $"The selected profile is a {account.Platform} profile, not {platform}.");
        if (!account.IsActive)
            throw new DomainException("submission.social_account_inactive", "The selected profile is deactivated.");

        var result = eligibility.Evaluate(participant, campaign);
        var accountResult = result.Accounts.First(a => a.SocialAccountId == account.Id);
        if (!accountResult.IsEligible)
            throw new DomainException("submission.account_ineligible", "The selected profile doesn't meet this campaign's requirements.",
                DomainErrorKind.Conflict, new Dictionary<string, string[]> { ["reasons"] = accountResult.Reasons.Select(r => $"{r.Code}: {r.Message}").ToArray() });
        if (!result.IsEligible)
            throw new DomainException("submission.not_eligible", "You are not eligible for this campaign.", DomainErrorKind.Conflict,
                new Dictionary<string, string[]> { ["reasons"] = result.ParticipantReasons.Select(r => $"{r.Code}: {r.Message}").ToArray() });
        return account;
    }

    /// <summary>Validates the link for the platform and returns its canonical post key (stored in NormalizedPostUrl).</summary>
    private static (string Key, bool IsShortLink) ValidateUrl(SocialPlatform platform, string raw)
    {
        var parsed = PlatformUrlRules.Parse(platform, raw);
        return parsed.Error switch
        {
            PostUrlError.None => (parsed.CanonicalKey!, parsed.IsShortLink),
            PostUrlError.PlatformMismatch =>
                throw new DomainException("submission.url_platform_mismatch", $"That link is not a {platform} post link."),
            _ => throw new DomainException("submission.invalid_url", "Enter the full public link to your post (https://...)."),
        };
    }

    private static void ValidatePostedAt(DateTime postedAt, DateTime now, DateTime submittedAt)
    {
        if (SubmissionTiming.IsPostedAtInFuture(postedAt, now))
            throw new DomainException("submission.posted_at_in_future", "The post time can't be in the future.");
        if (SubmissionTiming.IsPostedAtTooOld(postedAt, submittedAt))
            throw new DomainException("submission.posted_at_too_old",
                $"Posts must be submitted within {SubmissionTiming.MaxPostAgeAtSubmission.TotalDays:0} days of going live.");
    }

    private async Task ApplyRiskFlagsAsync(Submission submission, Campaign campaign, SocialAccount account, bool isShortLink,
        CancellationToken ct)
    {
        var now = Now;
        var id = submission.Id;
        var sha = submission.ScreenshotSha256;
        var hash = submission.ContentHash;
        var user = await db.Set<User>().AsNoTracking().Where(u => u.Id == submission.UserId).Select(u => new { u.CreatedAt }).FirstAsync(ct);
        var dayAgo = now.AddHours(-24);

        var signals = new RiskSignals
        {
            SameScreenshotCount = sha is null ? 0 : await db.Set<Submission>().CountAsync(s => s.ScreenshotSha256 == sha && s.Id != id, ct),
            RepeatedContentCount = hash is null ? 0 : await db.Set<Submission>().CountAsync(s =>
                s.ContentHash == hash && s.Id != id && (s.UserId == submission.UserId || s.CampaignId == campaign.Id), ct),
            PostedAtUtc = submission.PostedAt,
            SubmittedAtUtc = submission.SubmittedAt,
            IsShortLink = isShortLink,
            CampaignStartsAtUtc = campaign.StartsAt,
            CampaignEndsAtUtc = campaign.EndsAt,
            AccountVerified = account.VerificationStatus == SocialAccountVerificationStatus.Verified,
            CampaignRequiresVerifiedAccount = campaign.Eligibility.RequireVerifiedAccount,
            SubmissionsInLast24Hours = await db.Set<Submission>().CountAsync(s => s.UserId == submission.UserId && s.Id != id && s.SubmittedAt >= dayAgo, ct),
            VelocityLimitPer24Hours = await settings.GetAsync(SettingKeys.SubmissionVelocityLimit, 10, ct),
            ParticipantCreatedAtUtc = user.CreatedAt,
            NowUtc = now,
        };

        var flags = RiskRules.Evaluate(signals);
        foreach (var flag in flags)
        {
            var row = new SubmissionFlag { SubmissionId = id, Type = flag.Type, Detail = flag.Detail, Weight = flag.Weight, CreatedAt = now };
            if (db.Entry(submission).State == EntityState.Detached) submission.Flags.Add(row);
            else db.Set<SubmissionFlag>().Add(row);
        }
        submission.RiskScore = RiskRules.Score(flags);
    }

    private async Task SaveMappingDuplicateUrlAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex) &&
                                           ex.InnerException!.Message.Contains("NormalizedPostUrl", StringComparison.OrdinalIgnoreCase))
        {
            throw DuplicateUrl();
        }
    }

    private static DomainException DuplicateUrl() =>
        DomainException.Conflict("submission.duplicate_url", "This post has already been submitted. Each post can only be claimed once.");

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
