using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Marketing.Experiments;
using OptimizeAll.Api.Modules.Marketing.Invitations;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Marketing.Landing;

public sealed record RewardTeaserDto(string Currency, decimal BaseAmount);

public sealed record LandingCategoryDto(string Name, string Slug);

public sealed record LandingCampaignDto(
    string Slug, string Title, string Summary, IReadOnlyList<SocialPlatform> Platforms, RewardTeaserDto? Reward,
    DateTime StartsAt, DateTime EndsAt, LandingCategoryDto? Category);

public sealed record UtmDto(string? Source, string? Medium, string? Campaign);

public sealed record LandingExperimentDto(Guid ExperimentId, Guid VariantId, string Key);

public sealed record InvitationLandingDto(
    string Code, string Type, string Headline, string Body, string? HeroImageUrl, LandingCampaignDto? Campaign, UtmDto Utm,
    LandingExperimentDto? Experiment);

public sealed record LandingAssetDto(Guid Id, string Title, string Url);

public sealed record PublicCampaignLandingDto(
    string Slug, string Title, string Summary, string Headline, string Body, string? HeroImageUrl,
    IReadOnlyList<SocialPlatform> Platforms, RewardTeaserDto? Reward, DateTime StartsAt, DateTime EndsAt,
    DateTime SubmissionDeadline, LandingCategoryDto? Category, IReadOnlyList<LandingAssetDto> Assets, string Disclosure,
    LandingExperimentDto? Experiment);

/// <summary>Anonymous landing pages for invitation links and public campaigns.</summary>
[ApiController]
[Route("api/v1/public")]
[EnableRateLimiting(RateLimitPolicies.Public)]
public sealed class PublicLandingController(
    AppDbContext db, ExperimentAssignmentService experiments, IPrivacyHasher hasher, TimeProvider clock) : ControllerBase
{
    public const string VisitorHeader = "X-Visitor-Id";
    public const string PlatformHeadline = "Get paid to share brands you already love";
    public const string PlatformBody =
        "Join Optimize All, share company-approved posts from your established social accounts, submit proof and get paid for every approved post.";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Invitation landing payload. 404 when the link is inactive, expired or used up. Counts the visit.</summary>
    [HttpGet("invitations/{code}")]
    public async Task<InvitationLandingDto> Invitation(string code, CancellationToken ct)
    {
        var now = Now;
        var link = await db.Set<InvitationLink>().AsNoTracking().FirstOrDefaultAsync(i => i.Code == code, ct);
        if (link is null || !InvitationsController.IsUsable(link, now))
            throw DomainException.NotFound("Invitation");

        Campaign? campaign = null;
        if (link.CampaignId is { } campaignId)
        {
            campaign = await LoadCampaignAsync(c => c.Id == campaignId, ct);
            if (campaign is null || campaign.Status is not (CampaignStatus.Scheduled or CampaignStatus.Active))
                throw DomainException.NotFound("Invitation");
        }

        await db.Set<InvitationLink>().Where(i => i.Id == link.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.VisitCount, i => i.VisitCount + 1), ct);

        var utm = new UtmDto(link.UtmSource, link.UtmMedium, link.UtmCampaign);
        if (campaign is null)
            return new InvitationLandingDto(link.Code, "platform", PlatformHeadline, PlatformBody, null, null, utm, null);

        var (headline, body, experiment) = await LandingContentAsync(campaign, ct);
        return new InvitationLandingDto(link.Code, "campaign", headline, body, campaign.HeroImageUrl,
            await ToLandingCampaignAsync(campaign, ct), utm, experiment);
    }

    /// <summary>Public landing page of an Active or Scheduled public campaign.</summary>
    [HttpGet("campaigns/{slug}")]
    public async Task<PublicCampaignLandingDto> Campaign(string slug, CancellationToken ct)
    {
        var campaign = await LoadCampaignAsync(c => c.Slug == slug, ct);
        if (campaign is null || campaign.Visibility != CampaignVisibility.Public ||
            campaign.Status is not (CampaignStatus.Scheduled or CampaignStatus.Active))
            throw DomainException.NotFound("Campaign");

        var (headline, body, experiment) = await LandingContentAsync(campaign, ct);
        var teaser = await RewardTeaserAsync(campaign.Id, ct);
        return new PublicCampaignLandingDto(
            campaign.Slug, campaign.Title, campaign.Summary, headline, body, campaign.HeroImageUrl,
            campaign.Platforms.Select(p => p.Platform).OrderBy(p => p).ToList(), teaser,
            campaign.StartsAt, campaign.EndsAt, campaign.SubmissionDeadline,
            campaign.Category is { } cat ? new LandingCategoryDto(cat.Name, cat.Slug) : null,
            campaign.Assets.Where(a => a.Type == CampaignAssetType.Image && !string.IsNullOrWhiteSpace(a.Url))
                .OrderBy(a => a.SortOrder).Select(a => new LandingAssetDto(a.Id, a.Title, a.Url!)).ToList(),
            campaign.DefaultDisclosureText,
            experiment);
    }

    private Task<Campaign?> LoadCampaignAsync(System.Linq.Expressions.Expression<Func<Campaign, bool>> predicate, CancellationToken ct) =>
        db.Set<Campaign>().AsNoTracking().AsSplitQuery()
            .Include(c => c.Platforms).Include(c => c.Assets).Include(c => c.Category)
            .FirstOrDefaultAsync(predicate, ct);

    /// <summary>
    /// Landing headline/body with a running LandingPage experiment applied for identified anonymous visitors
    /// (X-Visitor-Id, hashed). Without a visitor id no assignment is made and the base content is shown.
    /// </summary>
    private async Task<(string Headline, string Body, LandingExperimentDto? Experiment)> LandingContentAsync(Campaign campaign, CancellationToken ct)
    {
        var headline = string.IsNullOrWhiteSpace(campaign.LandingHeadline) ? campaign.Title : campaign.LandingHeadline;
        var body = string.IsNullOrWhiteSpace(campaign.LandingBody) ? campaign.Summary : campaign.LandingBody;

        var visitorId = Request.Headers[VisitorHeader].ToString();
        if (string.IsNullOrWhiteSpace(visitorId) || visitorId.Length > 200) return (headline, body, null);

        var running = await experiments.RunningAsync(campaign.Id, ExperimentElement.LandingPage, ct);
        var experiment = running.FirstOrDefault();
        if (experiment is null || experiment.Variants.Count == 0) return (headline, body, null);

        var variant = await experiments.AssignAsync(experiment, VariantAssigner.VisitorSubject(hasher.Hash(visitorId)!), ct);
        return (
            string.IsNullOrWhiteSpace(variant.LandingHeadline) ? headline : variant.LandingHeadline,
            string.IsNullOrWhiteSpace(variant.LandingBody) ? body : variant.LandingBody,
            new LandingExperimentDto(experiment.Id, variant.Id, variant.Key));
    }

    private async Task<LandingCampaignDto> ToLandingCampaignAsync(Campaign c, CancellationToken ct) => new(
        c.Slug, c.Title, c.Summary, c.Platforms.Select(p => p.Platform).OrderBy(p => p).ToList(),
        await RewardTeaserAsync(c.Id, ct), c.StartsAt, c.EndsAt,
        c.Category is { } cat ? new LandingCategoryDto(cat.Name, cat.Slug) : null);

    /// <summary>Base rate of the reward rule version currently in force (a teaser; actual amounts depend on the rules).</summary>
    private async Task<RewardTeaserDto?> RewardTeaserAsync(Guid campaignId, CancellationToken ct)
    {
        var now = Now;
        var set = await db.Set<RewardRuleSet>().AsNoTracking().Include(s => s.Rules)
            .Where(s => s.CampaignId == campaignId && s.EffectiveFrom <= now)
            .OrderByDescending(s => s.Version).FirstOrDefaultAsync(ct);
        var baseRule = set?.Rules.FirstOrDefault(r => r.Type == RewardRuleType.BaseRate);
        return set is null || baseRule is null ? null : new RewardTeaserDto(set.Currency, baseRule.Amount);
    }
}
