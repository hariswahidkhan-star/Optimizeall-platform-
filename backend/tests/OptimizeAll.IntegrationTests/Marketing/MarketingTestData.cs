using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Marketing;

/// <summary>
/// Arranges cross-module data directly in the database (campaigns, social accounts, submissions, earnings) and
/// publishes domain events exactly like the owning modules do after commit.
/// </summary>
public static class MarketingTestData
{
    public static DateTime Now(this ApiFactory api) => api.Clock.GetUtcNow().UtcDateTime;

    public static Task PublishAsync<TEvent>(this ApiFactory api, TEvent domainEvent) where TEvent : IDomainEvent =>
        api.Services.GetRequiredService<IEventPublisher>().PublishAsync(domainEvent);

    public static async Task SetSettingAsync<T>(this ApiFactory api, string key, T value)
    {
        using var scope = api.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        await settings.SetAsync(key, value, null);
        await scope.ServiceProvider.GetRequiredService<OptimizeAll.Infrastructure.Persistence.AppDbContext>().SaveChangesAsync();
    }

    public static async Task<Campaign> CreateCampaignAsync(
        this ApiFactory api,
        Guid createdBy,
        CampaignStatus status = CampaignStatus.Active,
        CampaignVisibility visibility = CampaignVisibility.Public,
        string? destination = null,
        string? utmCampaign = null,
        DateTime? publishedAt = null,
        SocialPlatform[]? platforms = null,
        string[]? topics = null,
        string? landingHeadline = null,
        string? landingBody = null,
        decimal baseRate = 2.5m,
        string title = "Spring Launch")
    {
        var now = api.Now();
        var slug = "c-" + Guid.NewGuid().ToString("N")[..12];
        var campaign = new Campaign
        {
            Slug = slug,
            Title = title,
            Summary = title + " summary",
            Description = "Description",
            Status = status,
            Visibility = visibility,
            StartsAt = now.AddDays(-1),
            EndsAt = now.AddDays(30),
            SubmissionDeadline = now.AddDays(32),
            PostingInstructions = "Share the image with the caption.",
            TrackingDestinationUrl = destination,
            UtmCampaign = utmCampaign,
            LandingHeadline = landingHeadline,
            LandingBody = landingBody,
            HeroImageUrl = "https://cdn.example.com/hero.jpg",
            CreatedByUserId = createdBy,
            PublishedAt = status is CampaignStatus.Active or CampaignStatus.Scheduled ? publishedAt ?? now.AddDays(-10) : null,
            Topics = (topics ?? Array.Empty<string>()).ToList(),
        };
        foreach (var p in platforms ?? new[] { SocialPlatform.Instagram })
            campaign.Platforms.Add(new CampaignPlatform { CampaignId = campaign.Id, Platform = p });
        campaign.Assets.Add(new CampaignAsset { CampaignId = campaign.Id, Type = CampaignAssetType.Image, Title = "Hero image", Url = "https://cdn.example.com/a.jpg", SortOrder = 1 });
        campaign.Assets.Add(new CampaignAsset { CampaignId = campaign.Id, Type = CampaignAssetType.Caption, Title = "Caption", Body = "Caption text", SortOrder = 2 });

        var ruleSet = new RewardRuleSet
        {
            CampaignId = campaign.Id, Version = 1, Currency = "USD", EffectiveFrom = now.AddDays(-2), CreatedAt = now,
            CreatedByUserId = createdBy, ChangeReason = "Initial",
            Rules = { new RewardRule { Type = RewardRuleType.BaseRate, Amount = baseRate } },
        };

        await api.WithDbAsync(async db =>
        {
            db.Set<Campaign>().Add(campaign);
            db.Set<RewardRuleSet>().Add(ruleSet);
            await db.SaveChangesAsync();
        });
        return campaign;
    }

    public static async Task<SocialAccount> CreateSocialAccountAsync(
        this ApiFactory api, Guid userId, SocialPlatform platform = SocialPlatform.Instagram, int followers = 1500, int ageDays = 400)
    {
        var handle = "h" + Guid.NewGuid().ToString("N")[..14];
        var account = new SocialAccount
        {
            UserId = userId, Platform = platform, Handle = handle, NormalizedHandle = handle,
            ProfileUrl = $"https://social.example.com/{handle}", AccountCreatedAt = api.Now().AddDays(-ageDays),
            FollowerCount = followers, VerificationStatus = SocialAccountVerificationStatus.Verified,
        };
        await api.WithDbAsync(async db => { db.Set<SocialAccount>().Add(account); await db.SaveChangesAsync(); });
        return account;
    }

    public static async Task<Submission> CreateSubmissionAsync(
        this ApiFactory api, Guid userId, Guid campaignId, SocialAccount account, SubmissionStatus status,
        DateTime? submittedAt = null, Guid? variantId = null)
    {
        var ruleSetId = await api.WithDbAsync(db => db.Set<RewardRuleSet>().Where(r => r.CampaignId == campaignId).Select(r => r.Id).FirstAsync());
        var now = api.Now();
        var url = $"https://social.example.com/p/{Guid.NewGuid():N}";
        var submission = new Submission
        {
            CampaignId = campaignId, UserId = userId, SocialAccountId = account.Id, Platform = account.Platform,
            PostUrl = url, NormalizedPostUrl = url, PostedAt = (submittedAt ?? now).AddMinutes(-5), Status = status,
            SubmittedAt = submittedAt ?? now.AddHours(-1),
            DecidedAt = status is SubmissionStatus.Approved or SubmissionStatus.Rejected or SubmissionStatus.Reversed or SubmissionStatus.NeedsCorrection
                ? now.AddMinutes(-10) : null,
            RewardRuleSetId = ruleSetId, RewardRuleSetVersion = 1, EstimatedRewardAmount = 2.5m, RewardCurrency = "USD",
            ExperimentVariantId = variantId,
        };
        await api.WithDbAsync(async db => { db.Set<Submission>().Add(submission); await db.SaveChangesAsync(); });
        return submission;
    }

    /// <summary>Inserts a ledger row directly (analytics fixtures). Settlement currency USD, rate 1.</summary>
    public static EarningEntry Earning(Guid userId, Submission? submission, EarningType type, decimal amount, EarningStatus status,
        DateTime now, Guid? reverses = null) => new()
    {
        UserId = userId, CampaignId = submission?.CampaignId, SubmissionId = submission?.Id, Type = type, Status = status,
        Amount = amount, Currency = "USD", ExchangeRate = 1m, SettlementAmount = amount, SettlementCurrency = "USD",
        IdempotencyKey = $"test:{Guid.NewGuid():N}", Description = type.ToString(), CreatedAt = now,
        Reason = type is EarningType.Reversal or EarningType.Adjustment ? "test reversal" : null, ReversesEntryId = reverses,
    };

    public static async Task<JsonElement> PostJsonAsync(this HttpClient client, string url, object body) =>
        await (await client.PostAsJsonAsync(url, body)).ReadJsonAsync();
}
