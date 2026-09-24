using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Api.Modules.Submissions;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Eligibility;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;

namespace OptimizeAll.Api.Modules.Seed;

internal enum StepKind
{
    Claim,
    Approve,
    RequestCorrection,
    Resubmit,
    Reject,
    Reverse,
    LiveConfirm,
    LiveRemove,
    Appeal,
    AppealUphold,
    AppealOverturn,
    BonusApprove,
    BonusDecline,
    Withdraw,
}

internal sealed record Step(StepKind Kind, DateTime At, DemoPerson Actor, string? Reason = null, decimal? Bonus = null);

/// <summary>A submission's whole planned life: created at <see cref="SubmittedAt"/>, then each step in time order.</summary>
internal sealed class SubPlan
{
    public required DemoPerson Who { get; init; }
    public required DemoCampaign Campaign { get; init; }
    public required SocialAccount Account { get; init; }
    public required DateTime SubmittedAt { get; init; }
    public required DateTime PostedAt { get; init; }
    public required string PostUrl { get; init; }
    public string? Caption { get; set; }
    public byte[]? Png { get; set; }
    public List<Step> Steps { get; } = new();

    public Guid Id { get; set; }
    public bool Created { get; set; }
    public Guid? AppealId { get; set; }
    public DemoPerson? DecidedBy { get; set; }

    public SubPlan Then(StepKind kind, DateTime at, DemoPerson actor, string? reason = null, decimal? bonus = null)
    {
        Steps.Add(new Step(kind, at, actor, reason, bonus));
        return this;
    }
}

internal sealed partial class DemoRun
{
    private readonly List<SubPlan> _plans = new();
    private readonly HashSet<string> _normalizedUrls = new(StringComparer.Ordinal);
    private int _screenshotVariant;

    private static readonly string[] RejectReasons =
    {
        "The post is missing the required paid-partnership disclosure.",
        "The link points to a private account, so we can't verify the post is public.",
        "The screenshot doesn't match the post at the submitted link.",
        "The post uses visuals that aren't part of the approved campaign assets.",
        "The caption changes the approved product claims (e.g. 'guaranteed results').",
    };

    private static readonly string[] CorrectionReasons =
    {
        "Please add the required disclosure for your platform to the caption and resubmit.",
        "The screenshot is cropped — please upload one that shows the whole post including the caption.",
        "The link goes to your profile, not the post. Please submit the direct link to the post.",
        "Please add the campaign hashtags to the caption and resubmit.",
    };

    private static readonly string[] ReversalReasons =
    {
        "The post was deleted within the 30-day minimum period.",
        "Most engagement on this post came from inauthentic accounts.",
        "The disclosure was removed from the caption after approval.",
    };

    // ------------------------------------------------------------------ planning

    private SubPlan Plan(DemoPerson who, DemoCampaign campaign, SocialPlatform platform, DateTime submittedAt,
        TimeSpan? postedBefore = null, byte[]? png = null)
    {
        var account = who.Accounts.First(a => a.Platform == platform);
        var plan = new SubPlan
        {
            Who = who,
            Campaign = campaign,
            Account = account,
            SubmittedAt = submittedAt,
            PostedAt = submittedAt - (postedBefore ?? _rng.Hours(0.5, 20)),
            PostUrl = NewPostUrl(platform, account.Handle),
            Caption = CaptionFor(campaign, platform, who.User.CountryCode),
            Png = png ?? DemoPng.Screenshot(_screenshotVariant++),
        };
        _plans.Add(plan);
        return plan;
    }

    private DemoPerson OtherReviewer(DemoPerson reviewer) => reviewer.Id == Reviewer1.Id ? Reviewer2 : Reviewer1;
    private DemoPerson AnyReviewer() => _rng.Chance(0.5) ? Reviewer1 : Reviewer2;

    private string NewPostUrl(SocialPlatform platform, string handle)
    {
        for (var attempt = 0; ; attempt++)
        {
            var url = platform switch
            {
                SocialPlatform.Instagram => $"https://www.instagram.com/p/{_rng.Chars("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-", 11)}/?igsh={_rng.Chars("abcdefghijklmnopqrstuvwxyz0123456789", 12)}",
                SocialPlatform.TikTok => $"https://www.tiktok.com/@{handle}/video/{_rng.Digits(19)}?is_from_webapp=1",
                SocialPlatform.X => $"https://x.com/{handle}/status/{_rng.Digits(19)}",
                SocialPlatform.YouTube => $"https://www.youtube.com/watch?v={_rng.Chars("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-", 11)}",
                SocialPlatform.LinkedIn => $"https://www.linkedin.com/posts/{handle}_activity-{_rng.Digits(19)}-{_rng.Chars("abcdefghijklmnopqrstuvwxyz", 4)}",
                SocialPlatform.Facebook => $"https://www.facebook.com/{handle}/posts/{_rng.Digits(16)}",
                _ => throw new InvalidOperationException($"No demo URL format for {platform}."),
            };
            // Unique by canonical post key, the same key SubmissionService stores and de-duplicates on.
            var key = PlatformUrlRules.CanonicalKey(platform, url);
            if (key is not null && _normalizedUrls.Add(key))
                return url;
            if (attempt > 20) throw new InvalidOperationException("Could not generate a unique demo post URL.");
        }
    }

    private string CaptionFor(DemoCampaign campaign, SocialPlatform platform, string country)
    {
        var c = campaign.Campaign;
        var disclosure = c.Disclosures
            .Where(d => (d.Platform is null || d.Platform == platform) && (d.CountryCode is null || d.CountryCode == country))
            .OrderByDescending(d => (d.Platform.HasValue ? 1 : 0) + (d.CountryCode is null ? 0 : 1))
            .Select(d => d.Text).FirstOrDefault() ?? c.DefaultDisclosureText;
        var opener = campaign.Openers.Length == 0 ? string.Empty : _rng.Pick(campaign.Openers) + " ";
        var extra = _rng.Pick(new[] { "", " Link in bio.", " Who's joining me?", " Highly recommend.", " Thoughts?", " Saving this for later." });
        return $"{opener}{campaign.Caption}{extra} {c.RequiredHashtags} {disclosure}".Trim();
    }

    /// <summary>Random submissions for every campaign with a <see cref="DemoCampaign.Generated"/> count.</summary>
    private void PlanGeneratedSubmissions()
    {
        var pool = _participants.Where(p => !p.Scripted).ToList();
        var perPair = _plans.GroupBy(p => (p.Who.Id, p.Campaign.Id)).ToDictionary(g => g.Key, g => g.Count());
        // Participants without payout details only earn after the draft's cutoff: a Held item in a batch that is later
        // finalized keeps its amount while its earnings are released, which reconciliation reports as a mismatch.
        var noProfileFrom = _p0.CutoffUtc.AddDays(-2);

        foreach (var campaign in _campaigns.Values.Where(c => c.Generated > 0))
        {
            var c = campaign.Campaign;
            var from = c.StartsAt.AddHours(2);
            var until = campaign.SubmissionsUntil(_now).AddMinutes(-30);
            var made = 0;
            for (var attempt = 0; made < campaign.Generated && attempt < campaign.Generated * 40; attempt++)
            {
                // A fifth of the posts on open campaigns arrive in the last two days (the live review queue).
                var recent = c.Status == CampaignStatus.Active && _rng.Chance(0.2);
                var t = recent ? _rng.Between(_now.AddHours(-44), until) : _rng.Between(from, until);
                var who = _rng.Pick(pool);
                if (!who.HasPayoutProfile && t < noProfileFrom) continue;
                var pairLimit = Math.Min(c.MaxSubmissionsPerParticipant, 3);
                if (perPair.GetValueOrDefault((who.Id, campaign.Id)) >= pairLimit) continue;
                var eligible = EligibleAccounts(who, campaign, t);
                if (eligible.Count == 0) continue;
                if (_plans.Any(p => p.Who.Id == who.Id && Math.Abs((p.SubmittedAt - t).TotalHours) < 3)) continue;

                var account = _rng.Pick(eligible);
                var plan = Plan(who, campaign, account.Platform, t);
                perPair[(who.Id, campaign.Id)] = perPair.GetValueOrDefault((who.Id, campaign.Id)) + 1;
                AssignRandomOutcome(plan);
                made++;
            }
            Count($"generated submissions ({campaign.Key})", made);
        }
    }

    private void AssignRandomOutcome(SubPlan plan)
    {
        var reviewer = AnyReviewer();
        var decideAt = plan.SubmittedAt + _rng.Hours(2, 30);
        if (decideAt > _now.AddMinutes(-20)) return; // still waiting in the queue

        var hasQuality = plan.Campaign.RuleSets.Last().Rules.Any(r => r.Type == RewardRuleType.QualityBonus);
        var roll = _rng.NextDouble();
        if (roll < 0.60)
        {
            var bonus = hasQuality && _rng.Chance(0.18) ? Math.Round((decimal)_rng.Next(2, 6), 2) : (decimal?)null;
            plan.Then(StepKind.Approve, decideAt, reviewer, bonus: bonus);
            if (bonus is not null)
            {
                var bonusAt = decideAt + _rng.Hours(10, 60);
                var fate = _rng.NextDouble();
                if (fate < 0.55) plan.Then(StepKind.BonusApprove, bonusAt, Manager);
                else if (fate < 0.8)
                    plan.Then(StepKind.BonusDecline, bonusAt, Manager,
                        "Good post, but it doesn't meet the standout criteria (original footage and above-average engagement).");
            }
            AddLiveCheck(plan, decideAt, removeChance: 0.07);
        }
        else if (roll < 0.74)
        {
            plan.Then(StepKind.Reject, decideAt, reviewer, _rng.Pick(RejectReasons));
        }
        else if (roll < 0.86)
        {
            plan.Then(StepKind.RequestCorrection, decideAt, reviewer, _rng.Pick(CorrectionReasons));
            var fixAt = decideAt + _rng.Hours(4, 48);
            var until = plan.Campaign.SubmissionsUntil(_now).AddMinutes(-30);
            if (fixAt < until && _rng.Chance(0.7))
            {
                plan.Then(StepKind.Resubmit, fixAt, plan.Who);
                var second = fixAt + _rng.Hours(2, 24);
                if (second < _now.AddMinutes(-20))
                {
                    plan.Then(StepKind.Approve, second, OtherReviewer(reviewer), "Correction received — disclosure now present.");
                    AddLiveCheck(plan, second, removeChance: 0);
                }
            }
        }
        else if (roll < 0.93)
        {
            plan.Then(StepKind.Approve, decideAt, reviewer);
            var reverseAt = decideAt + _rng.Hours(30, 200);
            if (reverseAt < _now.AddHours(-1))
                plan.Then(StepKind.Reverse, reverseAt, OtherReviewer(reviewer), _rng.Pick(ReversalReasons));
        }
        else
        {
            plan.Then(StepKind.Approve, decideAt, reviewer, "Great post — clear disclosure and strong visuals.");
            AddLiveCheck(plan, decideAt, removeChance: 0);
        }
    }

    /// <summary>For campaigns with a minimum live duration: a reviewer confirms (or finds the post removed) after it is due.</summary>
    private void AddLiveCheck(SubPlan plan, DateTime approvedAt, double removeChance)
    {
        var hours = plan.Campaign.Campaign.MinPostLiveHours;
        if (hours <= 0) return;
        var due = plan.PostedAt.AddHours(hours);
        var checkAt = (due > approvedAt ? due : approvedAt) + _rng.Hours(2, 30);
        if (checkAt > _now.AddMinutes(-20)) return; // still pending (or due) — shows up in the live-check queue
        if (_rng.Chance(removeChance))
            plan.Then(StepKind.LiveRemove, checkAt, AnyReviewer(), "The post returns 'content unavailable' — deleted by the author.");
        else
            plan.Then(StepKind.LiveConfirm, checkAt, AnyReviewer());
    }

    // ------------------------------------------------------------------ eligibility at a point in time

    private List<SocialAccount> EligibleAccounts(DemoPerson who, DemoCampaign campaign, DateTime at)
    {
        var user = who.User;
        if (user.CreatedAt > at.AddHours(-1) || user.EmailVerifiedAt is null || user.EmailVerifiedAt > at) return new();
        if (who.SuspendedAt is { } suspended && suspended <= at) return new();

        var accounts = who.Accounts.Where(a => a.CreatedAt <= at).Select(a => AccountAt(a, at)).ToList();
        var criteria = EligibilityCriteria.ForCampaign(campaign.Campaign, _minAccountAgeDays, 0);
        var profile = new ParticipantProfile(user.Id, UserStatus.Active, true, user.CountryCode, user.LanguageCode, user.Tier, user.Interests);
        var result = EligibilityEvaluator.Evaluate(criteria, profile, accounts, at);
        if (!result.IsEligible) return new();
        var ids = result.EligibleAccounts.Select(a => a.SocialAccountId).ToHashSet();
        return who.Accounts.Where(a => ids.Contains(a.Id)).ToList();
    }

    /// <summary>The profile as it looked at <paramref name="at"/>: not yet verified before its verification decision.</summary>
    private static SocialAccount AccountAt(SocialAccount a, DateTime at) => new()
    {
        Id = a.Id, UserId = a.UserId, Platform = a.Platform, Handle = a.Handle, NormalizedHandle = a.NormalizedHandle,
        AccountCreatedAt = a.AccountCreatedAt, FollowerCount = a.FollowerCount, PrimaryLanguage = a.PrimaryLanguage,
        AudienceCountryCode = a.AudienceCountryCode, IsActive = a.IsActive,
        VerificationStatus = a.VerifiedAt is { } v && v > at ? SocialAccountVerificationStatus.PendingReview : a.VerificationStatus,
    };

    // ------------------------------------------------------------------ queue wiring

    private void EnqueuePlans()
    {
        foreach (var plan in _plans)
        {
            var p = plan;
            At(p.SubmittedAt, async () =>
            {
                if (!await CreateSubmissionAsync(p)) return;
                foreach (var step in p.Steps.OrderBy(s => s.At))
                {
                    var s = step;
                    At(s.At, () => RunStepAsync(p, s));
                }
            });
        }
    }

    // ------------------------------------------------------------------ create (SubmissionService.CreateAsync)

    private async Task<bool> CreateSubmissionAsync(SubPlan plan)
    {
        var campaign = plan.Campaign.Campaign;
        var eligible = EligibleAccounts(plan.Who, plan.Campaign, Now);
        if (eligible.All(a => a.Id != plan.Account.Id) || !campaign.IsOpenForSubmissionsAt(Now, plan.Campaign))
        {
            _logger.LogWarning("Demo seed: skipped a planned submission by {User} to {Campaign} (not eligible/open at {At:u})",
                plan.Who.User.Email, campaign.Slug, Now);
            Count("skipped planned submissions");
            return false;
        }
        var ruleSet = await _quotes.GetCurrentRuleSetAsync(campaign.Id, Now)
                      ?? throw new InvalidOperationException($"Campaign {campaign.Slug} has no reward rules at {Now:u}.");
        var normalizedUrl = PlatformUrlRules.CanonicalKey(plan.Account.Platform, plan.PostUrl)
                            ?? throw new InvalidOperationException($"Invalid demo post URL {plan.PostUrl}.");
        if (await _db.Set<Submission>().AnyAsync(s => s.NormalizedPostUrl == normalizedUrl))
            throw new InvalidOperationException("Duplicate demo post URL.");

        var screenshot = plan.Png is null ? null
            : await StoreScreenshotAsync(plan.Who.Id, plan.Png, $"{plan.Account.Platform.ToString().ToLowerInvariant()}-post-{Now:yyyyMMdd-HHmm}.png");

        var submission = new Submission
        {
            Id = IdGenerator.NewId(Now),
            CampaignId = campaign.Id,
            UserId = plan.Who.Id,
            SocialAccountId = plan.Account.Id,
            Platform = plan.Account.Platform,
            PostUrl = plan.PostUrl,
            NormalizedPostUrl = normalizedUrl,
            PostedAt = plan.PostedAt,
            CaptionText = plan.Caption,
            ContentHash = Normalization.ContentHash(plan.Caption),
            ScreenshotFileId = screenshot?.Id,
            ScreenshotSha256 = screenshot?.Sha256,
            Status = SubmissionStatus.Pending,
            SubmittedAt = Now,
            RewardRuleSetId = ruleSet.Id,
            RewardRuleSetVersion = ruleSet.Version,
            RewardCurrency = ruleSet.Currency,
            ExperimentVariantId = await VariantForAsync(plan),
            CreatedAt = Now,
        };
        plan.Id = submission.Id;
        plan.Created = true;

        var context = await _quotes.BuildContextAsync(campaign, ruleSet.Currency, plan.Who.Id, submission.Platform, submission.PostedAt,
            submission.SubmittedAt, null, submission.Id);
        submission.EstimatedRewardAmount = RewardEngine.Quote(ruleSet, context).Total;
        await ApplyRiskFlagsAsync(submission, campaign, AccountAt(plan.Account, Now), plan.Who);
        submission.Events.Add(new SubmissionEvent
        {
            SubmissionId = submission.Id, ToStatus = SubmissionStatus.Pending, Action = "submitted", ActorUserId = plan.Who.Id, CreatedAt = Now,
        });
        _db.Set<Submission>().Add(submission);
        _audit.As(plan.Who.Id, Role.Participant).Record("submission.created", nameof(Submission), submission.Id, after: new
        {
            submission.CampaignId, submission.Platform, submission.NormalizedPostUrl, submission.RewardRuleSetVersion,
            submission.EstimatedRewardAmount, submission.RewardCurrency, submission.RiskScore,
        });
        await NotifyAsync(plan.Who.Id, NotificationTypes.SubmissionReceived, "Submission received",
            $"We received your post for \"{campaign.Title}\". A reviewer will check it soon.", AppLinks.Submission(submission.Id));
        Count("submissions");
        return true;
    }

    /// <summary>Same heuristics and inputs as SubmissionService.ApplyRiskFlagsAsync.</summary>
    private async Task ApplyRiskFlagsAsync(Submission submission, Campaign campaign, SocialAccount account, DemoPerson who)
    {
        var id = submission.Id;
        var sha = submission.ScreenshotSha256;
        var hash = submission.ContentHash;
        var dayAgo = Now.AddHours(-24);
        var signals = new RiskSignals
        {
            SameScreenshotCount = sha is null ? 0 : await _db.Set<Submission>().CountAsync(s => s.ScreenshotSha256 == sha && s.Id != id),
            RepeatedContentCount = hash is null ? 0 : await _db.Set<Submission>().CountAsync(s =>
                s.ContentHash == hash && s.Id != id && (s.UserId == submission.UserId || s.CampaignId == campaign.Id)),
            PostedAtUtc = submission.PostedAt,
            CampaignStartsAtUtc = campaign.StartsAt,
            CampaignEndsAtUtc = campaign.EndsAt,
            AccountVerified = account.VerificationStatus == SocialAccountVerificationStatus.Verified,
            CampaignRequiresVerifiedAccount = campaign.Eligibility.RequireVerifiedAccount,
            SubmissionsInLast24Hours = await _db.Set<Submission>().CountAsync(s => s.UserId == submission.UserId && s.Id != id && s.SubmittedAt >= dayAgo),
            VelocityLimitPer24Hours = await _settings.GetAsync(SettingKeys.SubmissionVelocityLimit, 10),
            ParticipantCreatedAtUtc = who.User.CreatedAt,
            NowUtc = Now,
        };
        var flags = RiskRules.Evaluate(signals);
        foreach (var flag in flags)
        {
            var row = new SubmissionFlag { SubmissionId = id, Type = flag.Type, Detail = flag.Detail, Weight = flag.Weight, CreatedAt = Now };
            if (_db.Entry(submission).State == EntityState.Detached) submission.Flags.Add(row);
            else _db.Set<SubmissionFlag>().Add(row);
            Count($"risk flags ({flag.Type})");
        }
        submission.RiskScore = RiskRules.Score(flags);
    }

    // ------------------------------------------------------------------ steps

    private async Task RunStepAsync(SubPlan plan, Step step)
    {
        var s = await _db.Set<Submission>().Include(x => x.Flags).FirstAsync(x => x.Id == plan.Id);
        var campaign = plan.Campaign;
        switch (step.Kind)
        {
            case StepKind.Claim:
                if (s.Status != SubmissionStatus.Pending) return;
                var minutes = await _settings.GetAsync(SettingKeys.ReviewClaimMinutes, 15);
                s.Status = SubmissionStatus.UnderReview;
                s.ClaimedByUserId = step.Actor.Id;
                s.ClaimExpiresAt = Now.AddMinutes(minutes);
                AddEvent(s, SubmissionStatus.Pending, SubmissionStatus.UnderReview, "claimed", step.Actor.Id, null);
                break;

            case StepKind.Approve:
                await DecideAsync(plan, s, SubmissionStatus.Approved, step.Actor, step.Reason, step.Bonus);
                break;
            case StepKind.RequestCorrection:
                await DecideAsync(plan, s, SubmissionStatus.NeedsCorrection, step.Actor, step.Reason, null);
                break;
            case StepKind.Reject:
                await DecideAsync(plan, s, SubmissionStatus.Rejected, step.Actor, step.Reason, null);
                break;

            case StepKind.Resubmit:
                await ResubmitAsync(plan, s);
                break;

            case StepKind.Withdraw:
                Withdraw(s, step.Reason);
                break;

            case StepKind.Reverse:
                if (s.Status != SubmissionStatus.Approved) return;
                await ReverseCoreAsync(plan, s, step.Reason!, step.Actor, "reversed");
                break;

            case StepKind.LiveConfirm:
            {
                if (s.Status != SubmissionStatus.Approved || s.LiveCheckStatus != LiveCheckStatus.Pending || s.LiveCheckDueAt > Now) return;
                s.LiveCheckStatus = LiveCheckStatus.ConfirmedLive;
                s.LiveCheckedAt = Now;
                s.LiveCheckedByUserId = step.Actor.Id;
                var manualRuleIds = await _db.Set<RewardRule>()
                    .Where(r => r.RuleSetId == s.RewardRuleSetId && r.ApprovalMode == BonusApprovalMode.ManualApproval)
                    .Select(r => r.Id).ToListAsync();
                var pending = await _db.Set<EarningEntry>()
                    .Where(e => e.SubmissionId == s.Id && e.Status == EarningStatus.PendingApproval).ToListAsync();
                _audit.As(step.Actor.Id, Role.Reviewer);
                foreach (var entry in pending.Where(e => e.RewardRuleId is null || !manualRuleIds.Contains(e.RewardRuleId.Value)))
                    await _ledger.ApproveAsync(entry, step.Actor.Id);
                AddEvent(s, SubmissionStatus.Approved, SubmissionStatus.Approved, "live_check_confirmed", step.Actor.Id, "Post still public.");
                _audit.Record("submission.live_check_confirmed", nameof(Submission), s.Id,
                    after: new { LiveCheckStatus = LiveCheckStatus.ConfirmedLive.ToString() }, reason: "Post still public.");
                Count("live checks confirmed");
                break;
            }
            case StepKind.LiveRemove:
                if (s.Status != SubmissionStatus.Approved || s.LiveCheckStatus != LiveCheckStatus.Pending) return;
                if (!await ReverseCoreAsync(plan, s, $"Post removed before the minimum live duration: {step.Reason}", step.Actor, "live_check_removed"))
                    return;
                s.LiveCheckStatus = LiveCheckStatus.Removed;
                s.LiveCheckedAt = Now;
                s.LiveCheckedByUserId = step.Actor.Id;
                break;

            case StepKind.Appeal:
            {
                if (s.Status is not (SubmissionStatus.Rejected or SubmissionStatus.Reversed) || s.DecidedAt is null) return;
                var window = await _settings.GetAsync(SettingKeys.AppealWindowDays, 14);
                if (Now > s.DecidedAt.Value.AddDays(window)) return;
                var appeal = new Appeal
                {
                    SubmissionId = s.Id, UserId = s.UserId, DecisionAppealed = s.Status, Reason = step.Reason!, Status = AppealStatus.Open,
                    CreatedAt = Now,
                };
                _db.Set<Appeal>().Add(appeal);
                plan.AppealId = appeal.Id;
                AddEvent(s, s.Status, s.Status, "appealed", s.UserId, step.Reason);
                _audit.As(s.UserId, Role.Participant).Record("submission.appealed", nameof(Submission), s.Id,
                    after: new { AppealId = appeal.Id, DecisionAppealed = s.Status.ToString() }, reason: step.Reason);
                Count("appeals");
                break;
            }
            case StepKind.AppealUphold:
            case StepKind.AppealOverturn:
                await ResolveAppealAsync(plan, s, step);
                break;

            case StepKind.BonusApprove:
            case StepKind.BonusDecline:
            {
                var bonus = await _db.Set<EarningEntry>().FirstOrDefaultAsync(e =>
                    e.SubmissionId == s.Id && e.Type == EarningType.QualityBonus && e.Status == EarningStatus.PendingApproval);
                if (bonus is null) return;
                _audit.As(step.Actor.Id, step.Actor.Role);
                if (step.Kind == StepKind.BonusApprove)
                {
                    await _ledger.ApproveAsync(bonus, step.Actor.Id);
                    await NotifyAsync(s.UserId, NotificationTypes.EarningApproved, "Bonus approved",
                        $"Your quality bonus of {Money2(bonus.Amount, bonus.Currency)} for \"{campaign.Title}\" was approved.", AppLinks.Earnings);
                    Count("quality bonuses approved");
                }
                else
                {
                    _ledger.Decline(bonus, step.Reason!, step.Actor.Id);
                    Count("quality bonuses declined");
                }
                break;
            }
        }
    }

    private void AddEvent(Submission s, SubmissionStatus? from, SubmissionStatus to, string action, Guid? actor, string? reason) =>
        _db.Set<SubmissionEvent>().Add(new SubmissionEvent
        {
            SubmissionId = s.Id, FromStatus = from, ToStatus = to, Action = action, ActorUserId = actor, Reason = reason, CreatedAt = Now,
        });

    /// <summary>ReviewService.DecideAsync: status transition, earnings from the recorded rule version, flags resolved, history.</summary>
    private async Task DecideAsync(SubPlan plan, Submission s, SubmissionStatus target, DemoPerson reviewer, string? reason, decimal? qualityBonus)
    {
        if (!s.IsOpenForReview) return;
        var campaign = plan.Campaign.Campaign;
        var before = s.Status;
        var action = target switch
        {
            SubmissionStatus.Approved => "approved",
            SubmissionStatus.NeedsCorrection => "correction_requested",
            _ => "rejected",
        };
        _audit.As(reviewer.Id, Role.Reviewer);

        s.Status = target;
        s.DecidedAt = Now;
        s.DecidedByUserId = reviewer.Id;
        s.DecisionReason = reason;
        s.ClaimedByUserId = null;
        s.ClaimExpiresAt = null;
        (s.LiveCheckStatus, s.LiveCheckDueAt) = target == SubmissionStatus.Approved && campaign.MinPostLiveHours > 0
            ? (LiveCheckStatus.Pending, s.PostedAt.AddHours(campaign.MinPostLiveHours))
            : (LiveCheckStatus.NotRequired, (DateTime?)null);
        plan.DecidedBy = reviewer;

        var eventReason = reason;
        PricedReward? priced = null;
        if (target == SubmissionStatus.Approved)
        {
            priced = await RecordApprovalEarningsAsync(s, plan.Campaign, qualityBonus, string.Empty, reviewer);
            if (priced.Quote.AppliedCaps.Count > 0)
            {
                eventReason = $"{reason ?? "Approved"} (caps applied: {string.Join(", ", priced.Quote.AppliedCaps)})";
                Count("approvals with caps applied");
            }
        }

        ResolveFlags(s, action, reviewer.Id);
        _db.Set<SubmissionEvent>().Add(new SubmissionEvent
        {
            SubmissionId = s.Id, FromStatus = before, ToStatus = target, Action = action, ActorUserId = reviewer.Id, Reason = eventReason, CreatedAt = Now,
        });
        _audit.Record($"submission.{action}", nameof(Submission), s.Id,
            before: new { Status = before.ToString() },
            after: new { Status = target.ToString(), RewardTotal = priced?.Quote.Total, priced?.Quote.Currency, AppliedCaps = priced?.Quote.AppliedCaps, RuleSetVersion = s.RewardRuleSetVersion },
            reason: eventReason);

        var (title, body) = target switch
        {
            SubmissionStatus.Approved => ("Submission approved", priced is { Quote.Total: > 0 }
                ? $"Your post for \"{campaign.Title}\" was approved. You earned {priced.Quote.Total} {priced.Quote.Currency}."
                : $"Your post for \"{campaign.Title}\" was approved."),
            SubmissionStatus.NeedsCorrection => ("Correction needed", $"Your post for \"{campaign.Title}\" needs a correction: {reason}"),
            _ => ("Submission not approved", $"Your post for \"{campaign.Title}\" was not approved: {reason}"),
        };
        await NotifyAsync(s.UserId, NotificationTypes.SubmissionDecision, title, body, AppLinks.Submission(s.Id));
        Count($"decisions ({action})");

        if (target == SubmissionStatus.Approved) await QualifyReferralAsync(s.UserId);
    }

    /// <summary>ReviewService.RecordApprovalEarningsAsync: one ledger entry per reward line, with the production key conventions.</summary>
    private async Task<PricedReward> RecordApprovalEarningsAsync(Submission s, DemoCampaign campaign, decimal? qualityBonus, string keySuffix, DemoPerson actor)
    {
        var priced = await _quotes.QuoteSubmissionAsync(s, qualityBonus);
        var requiresLiveCheck = campaign.Campaign.MinPostLiveHours > 0;
        foreach (var line in priced.Quote.Lines)
        {
            var key = line.Type switch
            {
                EarningType.FirstPostBonus => RewardQuoteService.FirstPostKey(campaign.Id, s.UserId),
                EarningType.TimeLimitedBonus => $"submission:{s.Id}:{line.Type}:{line.RuleId}{keySuffix}",
                _ => $"submission:{s.Id}:{line.Type}{keySuffix}",
            };
            await _ledger.RecordAsync(new NewEarning(
                s.UserId, line.Type, line.Amount, priced.Quote.Currency, key, $"{line.Label} — {campaign.Title}",
                line.RequiresApproval || requiresLiveCheck,
                CampaignId: campaign.Id, SubmissionId: s.Id, RewardRuleSetId: priced.RuleSet.Id,
                RewardRuleSetVersion: priced.RuleSet.Version, RewardRuleId: line.RuleId, CreatedByUserId: actor.Id));
            Count("earnings from approvals");
        }
        return priced;
    }

    private void ResolveFlags(Submission s, string note, Guid actor)
    {
        foreach (var flag in s.Flags.Where(f => f.ResolvedAt is null))
        {
            flag.ResolvedAt = Now;
            flag.ResolvedByUserId = actor;
            flag.ResolutionNote = note;
        }
    }

    /// <summary>SubmissionService.WithdrawAsync (participant withdraws an undecided submission; the post link is released).</summary>
    private void Withdraw(Submission s, string? reason)
    {
        if (!s.CanWithdraw) return;
        var from = s.Status;
        var key = s.NormalizedPostUrl;
        s.NormalizedPostUrl = SubmissionService.WithdrawnKey(s.Id, key);
        s.Status = SubmissionStatus.Withdrawn;
        s.ClaimedByUserId = null;
        s.ClaimExpiresAt = null;
        foreach (var flag in s.Flags.Where(f => f.ResolvedAt is null))
        {
            flag.ResolvedAt = Now;
            flag.ResolvedByUserId = s.UserId;
            flag.ResolutionNote = "withdrawn by participant";
        }
        AddEvent(s, from, SubmissionStatus.Withdrawn, "withdrawn", s.UserId, reason);
        _audit.As(s.UserId, Role.Participant).Record("submission.withdrawn", nameof(Submission), s.Id,
            before: new { Status = from.ToString(), NormalizedPostUrl = key },
            after: new { Status = nameof(SubmissionStatus.Withdrawn), s.EstimatedRewardAmount, s.RewardCurrency }, reason: reason);
        Count("submissions withdrawn");
    }

    /// <summary>SubmissionService.ResubmitAsync (participant fixes a NeedsCorrection submission).</summary>
    private async Task ResubmitAsync(SubPlan plan, Submission s)
    {
        if (s.Status != SubmissionStatus.NeedsCorrection) return;
        var campaign = plan.Campaign.Campaign;
        if (!campaign.IsOpenForSubmissionsAt(Now, plan.Campaign)) return;

        var before = new { s.PostUrl, s.PostedAt, s.CaptionText, s.ScreenshotSha256 };
        var png = DemoPng.Screenshot(_screenshotVariant++);
        var screenshot = await StoreScreenshotAsync(s.UserId, png, $"{s.Platform.ToString().ToLowerInvariant()}-post-corrected.png");
        s.ScreenshotFileId = screenshot.Id;
        s.ScreenshotSha256 = screenshot.Sha256;
        var caption = CaptionFor(plan.Campaign, s.Platform, plan.Who.User.CountryCode);
        s.CaptionText = caption;
        s.ContentHash = Normalization.ContentHash(caption);

        var ruleSet = await _quotes.LoadRuleSetAsync(s.RewardRuleSetId);
        var context = await _quotes.BuildContextAsync(campaign, ruleSet.Currency, s.UserId, s.Platform, s.PostedAt, s.SubmittedAt, null, s.Id);
        s.EstimatedRewardAmount = RewardEngine.Quote(ruleSet, context).Total;

        _db.RemoveRange(s.Flags.Where(f => f.ResolvedAt is null).ToList());
        await ApplyRiskFlagsAsync(s, campaign, AccountAt(plan.Account, Now), plan.Who);
        s.Status = SubmissionStatus.Pending;
        s.CorrectionCount++;
        s.ClaimedByUserId = null;
        s.ClaimExpiresAt = null;
        _db.Set<SubmissionEvent>().Add(new SubmissionEvent
        {
            SubmissionId = s.Id, FromStatus = SubmissionStatus.NeedsCorrection, ToStatus = SubmissionStatus.Pending, Action = "resubmitted",
            ActorUserId = s.UserId, CreatedAt = Now,
        });
        _audit.As(s.UserId, Role.Participant).Record("submission.resubmitted", nameof(Submission), s.Id, before,
            new { s.PostUrl, s.PostedAt, s.CaptionText, s.ScreenshotSha256, s.CorrectionCount });
        Count("resubmissions");
    }

    /// <summary>
    /// ReviewService.ReverseCoreAsync: Approved → Reversed and every live earning reversed through the ledger writer
    /// (unpaid → cancelled, paid → clawback). Returns false (and changes nothing) while an earning sits in a payout batch,
    /// exactly as the real service refuses it.
    /// </summary>
    private async Task<bool> ReverseCoreAsync(SubPlan plan, Submission s, string reason, DemoPerson actor, string action)
    {
        if (s.Status != SubmissionStatus.Approved) return false;
        var earnings = await _db.Set<EarningEntry>()
            .Where(e => e.SubmissionId == s.Id && e.Type != EarningType.Reversal && e.ReversedByEntryId == null &&
                        e.Status != EarningStatus.Reversed && e.Status != EarningStatus.Declined)
            .ToListAsync();
        if (earnings.Any(e => e.Status == EarningStatus.Scheduled))
        {
            Count("reversals skipped (earning in a payout batch)");
            return false;
        }

        _audit.As(actor.Id, Role.Reviewer);
        s.Status = SubmissionStatus.Reversed;
        s.DecidedAt = Now;
        s.DecidedByUserId = actor.Id;
        s.DecisionReason = reason;
        plan.DecidedBy = actor;
        foreach (var entry in earnings)
        {
            var wasPaid = entry.Status == EarningStatus.Paid;
            await _ledger.ReverseAsync(entry, reason, actor.Id);
            Count(wasPaid ? "clawbacks (paid earnings reversed)" : "unpaid earnings reversed");
        }
        _db.Set<SubmissionEvent>().Add(new SubmissionEvent
        {
            SubmissionId = s.Id, FromStatus = SubmissionStatus.Approved, ToStatus = SubmissionStatus.Reversed, Action = action,
            ActorUserId = actor.Id, Reason = reason, CreatedAt = Now,
        });
        _audit.Record("submission.reversed", nameof(Submission), s.Id, new { Status = "Approved" },
            new { Status = "Reversed", ReversedEarnings = earnings.Select(e => new { e.Id, e.Type, e.Amount, e.Currency }) }, reason);
        await NotifyAsync(s.UserId, NotificationTypes.SubmissionReversed, "Approval reversed",
            $"The approval of your post for \"{plan.Campaign.Title}\" was reversed: {reason}", AppLinks.Submission(s.Id));
        Count("submissions reversed");
        return true;
    }

    /// <summary>ReviewService.ResolveAppealAsync (resolved by someone other than the original decider).</summary>
    private async Task ResolveAppealAsync(SubPlan plan, Submission s, Step step)
    {
        if (plan.AppealId is not { } appealId) return;
        var appeal = await _db.Set<Appeal>().FirstAsync(a => a.Id == appealId);
        if (appeal.Status != AppealStatus.Open) return;
        var resolver = step.Actor.Id == s.DecidedByUserId ? OtherReviewer(step.Actor) : step.Actor;
        var note = step.Reason!;
        var overturn = step.Kind == StepKind.AppealOverturn;
        if (overturn && s.Status != appeal.DecisionAppealed) return;
        _audit.As(resolver.Id, Role.Reviewer);

        appeal.Status = overturn ? AppealStatus.Overturned : AppealStatus.Upheld;
        appeal.ResolvedByUserId = resolver.Id;
        appeal.ResolvedAt = Now;
        appeal.ResolutionNote = note;
        var campaign = plan.Campaign.Campaign;
        if (overturn)
        {
            var from = appeal.DecisionAppealed;
            s.Status = SubmissionStatus.Approved;
            s.DecidedAt = Now;
            s.DecidedByUserId = resolver.Id;
            s.DecisionReason = $"Appeal overturned: {note}";
            (s.LiveCheckStatus, s.LiveCheckDueAt) = campaign.MinPostLiveHours > 0
                ? (LiveCheckStatus.Pending, s.PostedAt.AddHours(campaign.MinPostLiveHours))
                : (LiveCheckStatus.NotRequired, (DateTime?)null);
            s.LiveCheckedAt = null;
            s.LiveCheckedByUserId = null;
            await RecordApprovalEarningsAsync(s, plan.Campaign, null, $":appeal:{appealId}", resolver);
            _db.Set<SubmissionEvent>().Add(new SubmissionEvent
            {
                SubmissionId = s.Id, FromStatus = from, ToStatus = SubmissionStatus.Approved, Action = "appeal_overturned",
                ActorUserId = resolver.Id, Reason = note, CreatedAt = Now,
            });
            await QualifyReferralAsync(s.UserId);
        }
        else
        {
            _db.Set<SubmissionEvent>().Add(new SubmissionEvent
            {
                SubmissionId = s.Id, FromStatus = s.Status, ToStatus = s.Status, Action = "appeal_upheld", ActorUserId = resolver.Id,
                Reason = note, CreatedAt = Now,
            });
        }
        _audit.Record("appeal.resolved", nameof(Appeal), appealId, new { Status = "Open" },
            new { Status = appeal.Status.ToString(), SubmissionId = s.Id, SubmissionStatus = s.Status.ToString() }, note);
        await NotifyAsync(s.UserId, NotificationTypes.AppealResolved, overturn ? "Appeal accepted" : "Appeal reviewed",
            overturn
                ? $"Your appeal for \"{campaign.Title}\" was accepted and your post is now approved. {note}"
                : $"Your appeal for \"{campaign.Title}\" was reviewed and the original decision stands. {note}",
            AppLinks.Submission(s.Id));
        Count(overturn ? "appeals overturned" : "appeals upheld");
    }

    // ------------------------------------------------------------------ experiments (assignment at first view)

    private readonly HashSet<string> _assignedSubjects = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, (Experiment Experiment, List<ExperimentVariant> Variants)> _experimentsByCampaign = new();

    private async Task<Guid?> VariantForAsync(SubPlan plan)
    {
        if (!_experimentsByCampaign.TryGetValue(plan.Campaign.Id, out var x)) return null;
        var (experiment, variants) = x;
        if (experiment.StartedAt is not { } started || Now < started || (experiment.EndedAt is { } ended && Now > ended)) return null;
        var subject = VariantAssigner.UserSubject(plan.Who.Id);
        var key = VariantAssigner.Assign(experiment.Id, subject, variants.Select(v => new WeightedVariant(v.Key, v.Weight)).ToList());
        var variant = variants.First(v => v.Key == key);
        if (_assignedSubjects.Add($"{experiment.Id}:{subject}"))
        {
            _db.Set<ExperimentAssignment>().Add(new ExperimentAssignment
            {
                ExperimentId = experiment.Id, VariantId = variant.Id, SubjectKey = subject, AssignedAt = Now.AddMinutes(-_rng.Next(20, 600)),
            });
            Count("experiment assignments");
        }
        await Task.CompletedTask;
        return variant.Id;
    }
}

internal static class DemoCampaignExtensions
{
    /// <summary>Campaign.IsOpenForSubmissions evaluated against the campaign's status at that moment of the timeline.</summary>
    public static bool IsOpenForSubmissionsAt(this Campaign campaign, DateTime at, DemoCampaign demo) =>
        campaign.PublishedAt is { } published && published <= at && at >= campaign.StartsAt && at <= campaign.SubmissionDeadline &&
        (demo.PausedAt is null || at < demo.PausedAt);
}
