using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Eligibility;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Campaigns;

public interface ICampaignCatalogService
{
    Task<IReadOnlyList<CategoryDto>> ActiveCategoriesAsync(CancellationToken ct);
    Task<PagedResult<CampaignCardDto>> BrowseAsync(Guid userId, CampaignBrowseQuery query, CancellationToken ct);
    Task<IReadOnlyList<RecommendedCampaignDto>> RecommendedAsync(Guid userId, int limit, CancellationToken ct);
    Task<CampaignDetailDto> DetailAsync(Guid userId, string slug, CancellationToken ct);
}

/// <summary>
/// Participant-facing campaign reads. Browse/recommended list only Public campaigns (InviteOnly campaigns are
/// unlisted but reachable by slug). Filters that depend on computed values (reward, eligibility, topics) are applied
/// in memory after a database pre-filter on status, visibility, platform, category, deadline and search.
/// </summary>
public sealed class CampaignCatalogService(AppDbContext db, IParticipantEligibility eligibility, Rates.IPersonRatesService personRates,
    TimeProvider clock) : ICampaignCatalogService
{
    private sealed record Row(Campaign Campaign, RewardRuleSet? RuleSet, EligibilityResult Eligibility, int MyCount, int MyActive);

    public async Task<IReadOnlyList<CategoryDto>> ActiveCategoriesAsync(CancellationToken ct) =>
        (await db.Set<CampaignCategory>().AsNoTracking().Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct))
        .Select(CategoryDto.From).ToList();

    public async Task<PagedResult<CampaignCardDto>> BrowseAsync(Guid userId, CampaignBrowseQuery query, CancellationToken ct)
    {
        var q = ListedCampaigns(includeScheduled: true);
        if (query.Platform is { } platform) q = q.Where(c => c.Platforms.Any(p => p.Platform == platform));
        if (query.CategoryId is { } categoryId) q = q.Where(c => c.CategoryId == categoryId);
        if (query.DeadlineBefore is { } before)
        {
            var b = Rewards.RewardRuleSetFactory.Utc(before);
            q = q.Where(c => c.SubmissionDeadline <= b);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = PagingExtensions.LikePattern(query.Search);
            q = q.Where(c => EF.Functions.Like(c.Title, pattern, "\\") || EF.Functions.Like(c.Summary, pattern, "\\"));
        }

        var participant = await eligibility.LoadAsync(userId, ct);
        var rows = await BuildRowsAsync(await q.ToListAsync(ct), participant, ct);

        var topic = query.Topic?.Trim().ToLowerInvariant();
        var filtered = rows.Where(r =>
            (string.IsNullOrEmpty(topic) || r.Campaign.Topics.Contains(topic)) &&
            (query.MinReward is null || (BaseAmount(r) ?? 0m) >= query.MinReward) &&
            (!query.EligibleOnly || r.Eligibility.IsEligible));

        filtered = (query.Sort ?? "deadline").ToLowerInvariant() switch
        {
            "reward" => filtered.OrderByDescending(r => BaseAmount(r) ?? 0m).ThenBy(r => r.Campaign.SubmissionDeadline),
            "newest" => filtered.OrderByDescending(r => r.Campaign.PublishedAt ?? r.Campaign.CreatedAt),
            _ => filtered.OrderBy(r => r.Campaign.SubmissionDeadline).ThenBy(r => r.Campaign.Title),
        };

        var list = filtered.ToList();
        var now = clock.GetUtcNow().UtcDateTime;
        var pageRows = list.Skip(query.Skip).Take(query.PageSize).ToList();
        var yours = await personRates.YourRatesAsync(userId, pageRows.Select(r => (r.Campaign, r.RuleSet)).ToList(), ct);
        var page = pageRows.Select(r => ToCard(r, now, yours.GetValueOrDefault(r.Campaign.Id))).ToList();
        return new PagedResult<CampaignCardDto>(page, list.Count, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<RecommendedCampaignDto>> RecommendedAsync(Guid userId, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 24);
        var participant = await eligibility.LoadAsync(userId, ct);
        var rows = await BuildRowsAsync(await ListedCampaigns(includeScheduled: false).ToListAsync(ct), participant, ct);
        var now = participant.NowUtc;
        var candidates = rows.Where(r => r.Eligibility.IsEligible && Remaining(r) > 0 && r.Campaign.IsOpenForSubmissions(now)).ToList();
        if (candidates.Count == 0) return Array.Empty<RecommendedCampaignDto>();

        var interests = participant.User.Interests.Select(i => i.ToLowerInvariant()).ToHashSet();
        var maxBase = candidates.Max(r => BaseAmount(r) ?? 0m);
        var yours = await personRates.YourRatesAsync(userId, candidates.Select(r => (r.Campaign, r.RuleSet)).ToList(), ct);
        var scored = new List<RecommendedCampaignDto>();
        foreach (var r in candidates)
        {
            var c = r.Campaign;
            var matchedInterests = c.Topics.Concat(c.Eligibility.Interests).Select(t => t.ToLowerInvariant())
                .Where(interests.Contains).Distinct().ToList();
            var eligiblePlatforms = r.Eligibility.EligibleAccounts.Select(a => a.Platform).Distinct().ToList();
            var daysLeft = (c.SubmissionDeadline - now).TotalDays;

            var score = matchedInterests.Count * 3.0
                        + (eligiblePlatforms.Count > 0 ? 2.0 : 0)
                        + (maxBase > 0 ? (double)((BaseAmount(r) ?? 0m) / maxBase) * 2.0 : 0)
                        + (daysLeft <= 3 ? 1.5 : daysLeft <= 7 ? 0.75 : 0);

            string reason;
            if (matchedInterests.Count > 0) reason = $"Matches your interest in {matchedInterests[0]}";
            else if (daysLeft <= 3)
            {
                var days = Math.Max(0, (int)Math.Ceiling(daysLeft));
                reason = days <= 1 ? "Ends within a day" : $"Ends in {days} days";
            }
            else if (eligiblePlatforms.Count > 0) reason = $"Share it from your {eligiblePlatforms[0]} account";
            else reason = "You're eligible for this campaign";

            scored.Add(new RecommendedCampaignDto(ToCard(r, now, yours.GetValueOrDefault(c.Id)), Math.Round(score, 3), reason));
        }
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Campaign.SubmissionDeadline).Take(limit).ToList();
    }

    public async Task<CampaignDetailDto> DetailAsync(Guid userId, string slug, CancellationToken ct)
    {
        var campaign = await db.Set<Campaign>().AsNoTracking().AsSplitQuery()
            .Include(c => c.Category).Include(c => c.Platforms).Include(c => c.Assets).Include(c => c.Disclosures)
            .FirstOrDefaultAsync(c => c.Slug == slug, ct);
        if (campaign is null || campaign.Status is CampaignStatus.Draft or CampaignStatus.Archived)
            throw DomainException.NotFound("Campaign");

        var participant = await eligibility.LoadAsync(userId, ct);
        var row = (await BuildRowsAsync(new List<Campaign> { campaign }, participant, ct))[0];
        var now = participant.NowUtc;
        var yourRate = (await personRates.YourRatesAsync(userId, new[] { (campaign, row.RuleSet) }, ct)).GetValueOrDefault(campaign.Id);
        var card = ToCard(row, now, yourRate);

        var mySubmissions = await db.Set<Submission>().AsNoTracking()
            .Where(s => s.UserId == userId && s.CampaignId == campaign.Id).OrderByDescending(s => s.SubmittedAt)
            .Select(s => new MySubmissionRefDto(s.Id, s.Status, s.SubmittedAt)).ToListAsync(ct);

        var platforms = campaign.Platforms.Select(p => p.Platform).OrderBy(p => p).ToList();
        var disclosures = platforms
            .Select(p => new ResolvedDisclosureDto(p, CampaignText.ResolveDisclosure(campaign, p, participant.User.CountryCode))).ToList();

        var eligibilityDto = new DetailEligibilityDto(row.Eligibility.IsEligible, Reasons(row.Eligibility.ParticipantReasons),
            row.Eligibility.Accounts.Select(a => new AccountEligibilityDto(a.SocialAccountId, a.Platform, a.Handle, a.IsEligible,
                a.EligibleFrom, Reasons(a.Reasons))).ToList());

        return new CampaignDetailDto(
            campaign.Id, campaign.Slug, campaign.Title, campaign.Summary, card.Category, campaign.Topics, platforms,
            campaign.Status, campaign.Visibility, campaign.Status == CampaignStatus.Scheduled, campaign.IsOpenForSubmissions(now),
            campaign.StartsAt, campaign.EndsAt, campaign.SubmissionDeadline, campaign.TimeZone, campaign.HeroImageUrl,
            campaign.LandingHeadline, campaign.LandingBody, card.Reward, campaign.Description, campaign.PostingInstructions,
            campaign.RequiredHashtags, campaign.RequiredMentions,
            campaign.Assets.OrderBy(a => a.SortOrder).ThenBy(a => a.CreatedAt).Select(CampaignAssetDto.From).ToList(),
            disclosures, Terms(campaign, row.RuleSet), eligibilityDto, mySubmissions, row.MyCount, Remaining(row),
            TrackingUrl.IsValidDestination(campaign.TrackingDestinationUrl), yourRate);
    }

    private IQueryable<Campaign> ListedCampaigns(bool includeScheduled) =>
        db.Set<Campaign>().AsNoTracking().AsSplitQuery().Include(c => c.Category).Include(c => c.Platforms)
            .Where(c => c.Visibility == CampaignVisibility.Public &&
                        (c.Status == CampaignStatus.Active || (includeScheduled && c.Status == CampaignStatus.Scheduled)));

    private async Task<List<Row>> BuildRowsAsync(List<Campaign> campaigns, ParticipantSnapshot participant, CancellationToken ct)
    {
        if (campaigns.Count == 0) return new List<Row>();
        var ids = campaigns.Select(c => c.Id).ToList();
        var now = participant.NowUtc;

        var sets = await db.Set<RewardRuleSet>().AsNoTracking().Include(r => r.Rules)
            .Where(r => ids.Contains(r.CampaignId) && r.EffectiveFrom <= now &&
                        r.Version == db.Set<RewardRuleSet>()
                            .Where(x => x.CampaignId == r.CampaignId && x.EffectiveFrom <= now).Max(x => x.Version))
            .ToListAsync(ct);
        var setByCampaign = sets.ToDictionary(s => s.CampaignId);

        var mine = await db.Set<Submission>().AsNoTracking()
            .Where(s => s.UserId == participant.User.Id && ids.Contains(s.CampaignId))
            .Select(s => new { s.CampaignId, s.Status }).ToListAsync(ct);

        return campaigns.Select(c => new Row(
            c, setByCampaign.GetValueOrDefault(c.Id), eligibility.Evaluate(participant, c),
            mine.Count(m => m.CampaignId == c.Id),
            mine.Count(m => m.CampaignId == c.Id && m.Status != SubmissionStatus.Rejected && m.Status != SubmissionStatus.Withdrawn))).ToList();
    }

    private static decimal? BaseAmount(Row r) => r.RuleSet?.Rules.FirstOrDefault(x => x.Type == RewardRuleType.BaseRate)?.Amount;

    private static int Remaining(Row r) => Math.Max(0, r.Campaign.MaxSubmissionsPerParticipant - r.MyActive);

    private static IReadOnlyList<ReasonDto> Reasons(IEnumerable<EligibilityReason> reasons) =>
        reasons.Select(x => new ReasonDto(x.Code, x.Message)).ToList();

    private static CampaignCardDto ToCard(Row r, DateTime now, Rates.YourRateDto? yourRate)
    {
        var c = r.Campaign;
        return new CampaignCardDto(
            c.Id, c.Slug, c.Title, c.Summary,
            c.Category is null ? null : new CategoryRefDto(c.Category.Id, c.Category.Name, c.Category.Slug),
            c.Topics, c.Platforms.Select(p => p.Platform).OrderBy(p => p).ToList(), c.Status, c.Status == CampaignStatus.Scheduled,
            c.StartsAt, c.EndsAt, c.SubmissionDeadline, c.HeroImageUrl, CampaignText.Reward(r.RuleSet, now),
            new CardEligibilityDto(r.Eligibility.IsEligible, Reasons(r.Eligibility.ParticipantReasons)),
            r.MyCount, Remaining(r), yourRate);
    }

    private static RewardTermsDto? Terms(Campaign c, RewardRuleSet? set)
    {
        var baseRate = set?.Rules.FirstOrDefault(r => r.Type == RewardRuleType.BaseRate);
        if (set is null || baseRate is null) return null;
        return new RewardTermsDto(
            set.Currency, baseRate.Amount,
            set.Rules.Where(r => r.Type == RewardRuleType.RateOverride)
                .OrderByDescending(RewardEngine.Specificity).ThenByDescending(r => r.Priority)
                .Select(r => new RewardOverrideTermDto(r.Platform, r.CountryCode, r.Tier, r.Amount, r.ValidFrom, r.ValidTo, r.Label)).ToList(),
            set.Rules.Where(r => r.Type is RewardRuleType.TimeLimitedBonus or RewardRuleType.FirstPostBonus or RewardRuleType.QualityBonus)
                .OrderBy(r => r.Type).ThenBy(r => r.ValidFrom)
                .Select(r => new RewardBonusTermDto(r.Type.ToString(), r.Amount, r.ApprovalMode.ToString(), r.ValidFrom, r.ValidTo, r.Label)).ToList(),
            set.DailyCapPerParticipant, set.WeeklyCapPerParticipant, set.CampaignCapPerParticipant,
            c.MinPostLiveHours, c.MaxSubmissionsPerParticipant, c.RequireScreenshot, set.Version);
    }
}
