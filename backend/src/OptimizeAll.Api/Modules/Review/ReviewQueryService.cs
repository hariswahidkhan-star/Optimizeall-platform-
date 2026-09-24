using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Campaigns;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Review;

public interface IReviewQueryService
{
    Task<PagedResult<ReviewQueueItemDto>> QueueAsync(ReviewQueueQuery query, CancellationToken ct);
    Task<ReviewDetailDto> DetailAsync(Guid submissionId, CancellationToken ct);
    Task<PagedResult<LiveCheckItemDto>> LiveChecksAsync(LiveCheckQuery query, CancellationToken ct);
    Task<PagedResult<AppealListItemDto>> AppealsAsync(AppealQuery query, CancellationToken ct);
    Task<AppealDetailDto> AppealAsync(Guid appealId, CancellationToken ct);
    Task<IReadOnlyList<ReviewerDto>> ReviewersAsync(CancellationToken ct);
    Task<ReviewStatsDto> StatsAsync(CancellationToken ct);
    Task<IReadOnlyList<ReviewEarningDto>> EarningsAsync(Guid submissionId, CancellationToken ct);
}

public sealed class ReviewQueryService(
    AppDbContext db,
    ICurrentUser currentUser,
    IRewardQuoteService quotes,
    IPermissionDirectory directory,
    TimeProvider clock) : IReviewQueryService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<ReviewQueueItemDto>> QueueAsync(ReviewQueueQuery query, CancellationToken ct)
    {
        var me = currentUser.Id;
        var q = db.Set<Submission>().AsNoTracking().AsQueryable();
        q = query.Status is { } status
            ? q.Where(s => s.Status == status)
            : q.Where(s => s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.UnderReview);
        if (query.CampaignId is { } campaignId) q = q.Where(s => s.CampaignId == campaignId);
        if (query.Platform is { } platform) q = q.Where(s => s.Platform == platform);
        if (query.MinRisk is { } minRisk) q = q.Where(s => s.RiskScore >= minRisk);
        if (query.Flagged == true) q = q.Where(s => s.Flags.Any(f => f.ResolvedAt == null));
        if (query.Flagged == false) q = q.Where(s => !s.Flags.Any(f => f.ResolvedAt == null));
        if (query.AssignedToMe) q = q.Where(s => s.AssignedReviewerId == me);
        if (query.ClaimedByMe)
        {
            var claimNow = Now;
            q = q.Where(s => s.ClaimedByUserId == me && s.ClaimExpiresAt > claimNow);
        }

        q = (query.Sort ?? "oldest").ToLowerInvariant() == "risk"
            ? q.OrderByDescending(s => s.RiskScore).ThenBy(s => s.SubmittedAt)
            : q.OrderBy(s => s.SubmittedAt).ThenBy(s => s.Id);

        var joined = from s in q
                     join c in db.Set<Campaign>() on s.CampaignId equals c.Id
                     join u in db.Set<User>() on s.UserId equals u.Id
                     join a in db.Set<SocialAccount>() on s.SocialAccountId equals a.Id
                     select new { s, CampaignTitle = c.Title, u.DisplayName, u.CountryCode, a.Handle };

        var total = await q.CountAsync(ct);
        var rows = await joined.Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        var ids = rows.Select(r => r.s.Id).ToList();
        var flags = await db.Set<SubmissionFlag>().AsNoTracking().Where(f => ids.Contains(f.SubmissionId) && f.ResolvedAt == null)
            .Select(f => new { f.SubmissionId, f.Type }).ToListAsync(ct);
        var names = await NamesAsync(rows.SelectMany(r => new[] { r.s.ClaimedByUserId, r.s.AssignedReviewerId }), ct);
        var now = Now;

        var items = rows.Select(r => new ReviewQueueItemDto(
            r.s.Id, new QueueCampaignDto(r.s.CampaignId, r.CampaignTitle), new QueueParticipantDto(r.s.UserId, r.DisplayName, r.CountryCode),
            r.s.Platform, r.Handle, r.s.Status, r.s.SubmittedAt, r.s.RiskScore,
            flags.Where(f => f.SubmissionId == r.s.Id).Select(f => f.Type).Distinct().ToList(),
            r.s.ClaimExpiresAt > now ? Person(names, r.s.ClaimedByUserId) : null,
            r.s.ClaimExpiresAt > now ? r.s.ClaimExpiresAt : null,
            Person(names, r.s.AssignedReviewerId), r.s.CorrectionCount)).ToList();
        return new PagedResult<ReviewQueueItemDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<ReviewDetailDto> DetailAsync(Guid submissionId, CancellationToken ct)
    {
        var s = await db.Set<Submission>().AsNoTracking().AsSplitQuery().Include(x => x.Flags).Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == submissionId, ct) ?? throw DomainException.NotFound("Submission");
        var campaign = await db.Set<Campaign>().AsNoTracking().AsSplitQuery().Include(c => c.Platforms).Include(c => c.Disclosures)
            .FirstAsync(c => c.Id == s.CampaignId, ct);
        var user = await db.Set<User>().AsNoTracking().FirstAsync(u => u.Id == s.UserId, ct);
        var account = await db.Set<SocialAccount>().AsNoTracking().FirstAsync(a => a.Id == s.SocialAccountId, ct);
        var now = Now;

        var statusCounts = await db.Set<Submission>().Where(x => x.UserId == s.UserId)
            .GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var history = await (from x in db.Set<Submission>().AsNoTracking()
                             join c in db.Set<Campaign>() on x.CampaignId equals c.Id
                             where x.UserId == s.UserId && x.Id != s.Id
                             orderby x.SubmittedAt descending
                             select new HistoryItemDto(x.Id, new QueueCampaignDto(c.Id, c.Title), x.Status, x.SubmittedAt, x.PostUrl))
            .Take(50).ToListAsync(ct);

        var sha = s.ScreenshotSha256;
        var hash = s.ContentHash;
        var related = await (from x in db.Set<Submission>().AsNoTracking()
                             join c in db.Set<Campaign>() on x.CampaignId equals c.Id
                             join u in db.Set<User>() on x.UserId equals u.Id
                             where x.Id != s.Id && ((sha != null && x.ScreenshotSha256 == sha) || (hash != null && x.ContentHash == hash))
                             orderby x.SubmittedAt descending
                             select new { x.Id, x.ScreenshotSha256, x.ContentHash, CampaignId = c.Id, c.Title, UserId = u.Id, u.DisplayName, x.Status, x.SubmittedAt })
            .Take(50).ToListAsync(ct);

        var appeals = await db.Set<Appeal>().AsNoTracking().Where(a => a.SubmissionId == s.Id).OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
        var names = await NamesAsync(s.Events.Select(e => e.ActorUserId)
            .Concat(new[] { s.ClaimedByUserId, s.DecidedByUserId, s.AssignedReviewerId })
            .Concat(appeals.Select(a => a.ResolvedByUserId)), ct);

        RewardQuoteDto? quote = null;
        try
        {
            var priced = await quotes.QuoteSubmissionAsync(s, null, ct);
            quote = RewardQuoteDto.From(priced.Quote, priced.RuleSet.Id, priced.RuleSet.Version);
        }
        catch (DomainException)
        {
            // A rule set that can no longer be priced is shown without a quote rather than failing the whole page.
        }

        // The reviewer's quality-bonus ceiling comes from the rule set version recorded on the submission.
        var qualityBonusMax = await db.Set<RewardRule>().AsNoTracking()
            .Where(r => r.RuleSetId == s.RewardRuleSetId && r.Type == RewardRuleType.QualityBonus)
            .Select(r => (decimal?)r.Amount).FirstOrDefaultAsync(ct);

        var claimActive = s.ClaimedByUserId is not null && s.ClaimExpiresAt > now;
        return new ReviewDetailDto(
            new RequirementsDto(campaign.Id, campaign.Title, campaign.PostingInstructions, campaign.RequiredHashtags, campaign.RequiredMentions,
                CampaignText.ResolveDisclosure(campaign, s.Platform, user.CountryCode),
                campaign.Platforms.Select(p => p.Platform).OrderBy(p => p).ToList(), campaign.StartsAt, campaign.EndsAt,
                campaign.SubmissionDeadline, campaign.MinPostLiveHours, campaign.RequireScreenshot),
            new ReviewSubmissionDto(s.Id, s.PostUrl, s.NormalizedPostUrl, s.Platform, s.PostedAt, s.CaptionText,
                s.ScreenshotFileId is { } f ? FileUrls.For(f) : null, s.ScreenshotSha256, s.SubmittedAt, s.Status, s.CorrectionCount,
                s.RiskScore, s.RewardRuleSetVersion, s.EstimatedRewardAmount, s.RewardCurrency, s.DecidedAt, Person(names, s.DecidedByUserId),
                s.DecisionReason, s.LiveCheckStatus, s.LiveCheckDueAt, Person(names, s.AssignedReviewerId), s.ConcurrencyStamp,
                new ReviewClaimInfoDto(claimActive ? Person(names, s.ClaimedByUserId) : null, claimActive ? s.ClaimExpiresAt : null,
                    claimActive && s.ClaimedByUserId == currentUser.Id, claimActive)),
            new ReviewAccountDto(account.Id, account.Handle, account.ProfileUrl, account.Platform, account.AccountCreatedAt,
                Math.Max(0, account.AccountAgeDays(now)), account.FollowerCount, account.VerificationStatus, account.IsActive),
            new ReviewParticipantDto(user.Id, user.DisplayName, user.Email, user.CountryCode, user.Tier, user.CreatedAt,
                statusCounts.GetValueOrDefault(SubmissionStatus.Approved), statusCounts.GetValueOrDefault(SubmissionStatus.Rejected),
                statusCounts.GetValueOrDefault(SubmissionStatus.Reversed), user.Status),
            history,
            s.Flags.OrderByDescending(x => x.Weight).ThenBy(x => x.CreatedAt)
                .Select(x => new FlagDto(x.Id, x.Type, x.Detail, x.Weight, x.CreatedAt, x.ResolvedAt is not null, x.ResolvedAt, x.ResolutionNote)).ToList(),
            related.Select(r => new RelatedSubmissionDto(r.Id,
                sha != null && r.ScreenshotSha256 == sha ? (hash != null && r.ContentHash == hash ? "screenshot_and_content" : "screenshot") : "content",
                new QueueCampaignDto(r.CampaignId, r.Title), new PersonRefDto(r.UserId, r.DisplayName), r.Status, r.SubmittedAt)).ToList(),
            s.Events.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
                .Select(e => new ReviewEventDto(e.Action, e.FromStatus, e.ToStatus, e.Reason, Person(names, e.ActorUserId), e.CreatedAt)).ToList(),
            quote,
            await EarningsAsync(s.Id, ct),
            appeals.Select(a => ToAppealDto(a, names)).ToList(),
            qualityBonusMax);
    }

    public async Task<IReadOnlyList<ReviewEarningDto>> EarningsAsync(Guid submissionId, CancellationToken ct) =>
        await db.Set<EarningEntry>().AsNoTracking().Where(e => e.SubmissionId == submissionId)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Type)
            .Select(e => new ReviewEarningDto(e.Id, e.Type, e.Amount, e.Currency, e.Status, e.CreatedAt, e.RewardRuleSetVersion))
            .ToListAsync(ct);

    public async Task<PagedResult<LiveCheckItemDto>> LiveChecksAsync(LiveCheckQuery query, CancellationToken ct)
    {
        var now = Now;
        var q = from s in db.Set<Submission>().AsNoTracking()
                join c in db.Set<Campaign>() on s.CampaignId equals c.Id
                join u in db.Set<User>() on s.UserId equals u.Id
                where s.Status == SubmissionStatus.Approved && s.LiveCheckStatus == LiveCheckStatus.Pending
                select new { s, c.Title, u.DisplayName };
        if (query.Due) q = q.Where(x => x.s.LiveCheckDueAt <= now);
        return await q.OrderBy(x => x.s.LiveCheckDueAt)
            .Select(x => new LiveCheckItemDto(x.s.Id, new QueueCampaignDto(x.s.CampaignId, x.Title), new PersonRefDto(x.s.UserId, x.DisplayName),
                x.s.Platform, x.s.PostUrl, x.s.PostedAt, x.s.DecidedAt, x.s.LiveCheckDueAt, x.s.LiveCheckDueAt <= now))
            .ToPagedAsync(query, ct);
    }

    public async Task<PagedResult<AppealListItemDto>> AppealsAsync(AppealQuery query, CancellationToken ct)
    {
        var q = from a in db.Set<Appeal>().AsNoTracking()
                join s in db.Set<Submission>() on a.SubmissionId equals s.Id
                join c in db.Set<Campaign>() on s.CampaignId equals c.Id
                join u in db.Set<User>() on a.UserId equals u.Id
                select new { a, s.CampaignId, c.Title, u.DisplayName, s.DecidedByUserId };
        if (query.Status is { } status) q = q.Where(x => x.a.Status == status);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.a.CreatedAt).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.DecidedByUserId), ct);
        var items = rows.Select(r => new AppealListItemDto(r.a.Id, r.a.SubmissionId, new QueueCampaignDto(r.CampaignId, r.Title),
            new PersonRefDto(r.a.UserId, r.DisplayName), r.a.Status, r.a.DecisionAppealed, r.a.Reason, r.a.CreatedAt,
            Person(names, r.DecidedByUserId), r.a.ConcurrencyStamp)).ToList();
        return new PagedResult<AppealListItemDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<AppealDetailDto> AppealAsync(Guid appealId, CancellationToken ct)
    {
        var appeal = await db.Set<Appeal>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == appealId, ct) ?? throw DomainException.NotFound("Appeal");
        var decidedBy = await db.Set<Submission>().Where(s => s.Id == appeal.SubmissionId).Select(s => s.DecidedByUserId).FirstAsync(ct);
        var names = await NamesAsync(new[] { appeal.ResolvedByUserId, decidedBy }, ct);
        return new AppealDetailDto(ToAppealDto(appeal, names), Person(names, decidedBy), await DetailAsync(appeal.SubmissionId, ct));
    }

    public async Task<IReadOnlyList<ReviewerDto>> ReviewersAsync(CancellationToken ct)
    {
        // Built-in or custom-role holders of submissions.review.
        var reviewers = await (await directory.UsersWithPermissionAsync(Permissions.SubmissionsReview, ct))
            .Where(u => u.Status == UserStatus.Active)
            .OrderBy(u => u.DisplayName).Select(u => new { u.Id, u.DisplayName, u.Email }).ToListAsync(ct);
        var ids = reviewers.Select(r => r.Id).ToList();
        var assigned = await db.Set<Submission>()
            .Where(s => s.AssignedReviewerId != null && ids.Contains(s.AssignedReviewerId.Value) &&
                        (s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.UnderReview))
            .GroupBy(s => s.AssignedReviewerId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var dayStart = Now.Date;
        var decisions = await db.Set<SubmissionEvent>()
            .Where(e => e.ActorUserId != null && ids.Contains(e.ActorUserId.Value) && e.CreatedAt >= dayStart && ReviewService.DecisionActions.Contains(e.Action))
            .GroupBy(e => e.ActorUserId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return reviewers.Select(r => new ReviewerDto(r.Id, r.DisplayName, r.Email, assigned.GetValueOrDefault(r.Id), decisions.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<ReviewStatsDto> StatsAsync(CancellationToken ct)
    {
        var me = currentUser.Id;
        var now = Now;
        var dayStart = now.Date;
        var myDecisions = await db.Set<SubmissionEvent>()
            .CountAsync(e => e.ActorUserId == me && e.CreatedAt >= dayStart && ReviewService.DecisionActions.Contains(e.Action), ct);
        var byStatus = await db.Set<Submission>()
            .Where(s => s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.UnderReview || s.Status == SubmissionStatus.NeedsCorrection)
            .GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var queue = new Dictionary<string, int>
        {
            [nameof(SubmissionStatus.Pending)] = 0, [nameof(SubmissionStatus.UnderReview)] = 0, [nameof(SubmissionStatus.NeedsCorrection)] = 0,
        };
        foreach (var row in byStatus) queue[row.Key.ToString()] = row.Count;
        var oldest = await db.Set<Submission>().Where(s => s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.UnderReview)
            .MinAsync(s => (DateTime?)s.SubmittedAt, ct);
        var dueLive = await db.Set<Submission>().CountAsync(s =>
            s.Status == SubmissionStatus.Approved && s.LiveCheckStatus == LiveCheckStatus.Pending && s.LiveCheckDueAt <= now, ct);
        var openAppeals = await db.Set<Appeal>().CountAsync(a => a.Status == AppealStatus.Open, ct);
        var claimedByMe = await db.Set<Submission>().CountAsync(s =>
            s.ClaimedByUserId == me && s.ClaimExpiresAt > now && (s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.UnderReview), ct);
        return new ReviewStatsDto(myDecisions, queue, oldest, oldest is null ? null : Math.Round((now - oldest.Value).TotalHours, 2),
            dueLive, openAppeals, claimedByMe);
    }

    public static ReviewAppealDto ToAppealDto(Appeal a, IReadOnlyDictionary<Guid, string> names) =>
        new(a.Id, a.Status, a.DecisionAppealed, a.Reason, a.CreatedAt, Person(names, a.ResolvedByUserId), a.ResolutionNote, a.ResolvedAt,
            a.ConcurrencyStamp);

    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, string>();
        return await db.Set<User>().AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    private static PersonRefDto? Person(IReadOnlyDictionary<Guid, string> names, Guid? id) =>
        id is { } v ? new PersonRefDto(v, names.TryGetValue(v, out var n) ? n : "Unknown user") : null;
}
