using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Accounts;

/// <summary>Arrange helpers shared by the accounts/social/content/notifications/support/admin integration tests.</summary>
public static class B1TestData
{
    public static JsonSerializerOptions Json => ApiFactory.Json;

    public static async Task<SocialAccount> AddSocialAccountAsync(
        this ApiFactory api, Guid userId, int ageDays, int followers = 1000,
        SocialAccountVerificationStatus status = SocialAccountVerificationStatus.Unverified,
        SocialPlatform platform = SocialPlatform.Instagram, bool isActive = true)
    {
        var handle = "h" + Guid.NewGuid().ToString("N")[..12];
        var account = new SocialAccount
        {
            UserId = userId,
            Platform = platform,
            Handle = handle,
            NormalizedHandle = handle,
            ProfileUrl = $"https://www.instagram.com/{handle}",
            AccountCreatedAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(-ageDays),
            FollowerCount = followers,
            VerificationStatus = status,
            IsActive = isActive,
        };
        await api.WithDbAsync(async db =>
        {
            db.Set<SocialAccount>().Add(account);
            await db.SaveChangesAsync();
        });
        return account;
    }

    /// <summary>Creates a minimal campaign + reward rule set + submission row for read-side tests.</summary>
    public static async Task<Guid> AddSubmissionAsync(this ApiFactory api, Guid userId, Guid socialAccountId, SubmissionStatus status)
    {
        var now = api.Clock.GetUtcNow().UtcDateTime;
        var campaign = new Campaign
        {
            Slug = "c-" + Guid.NewGuid().ToString("N")[..16],
            Title = "Test campaign",
            Summary = "Summary",
            Description = "Description",
            Status = CampaignStatus.Active,
            StartsAt = now.AddDays(-5),
            EndsAt = now.AddDays(30),
            SubmissionDeadline = now.AddDays(31),
            PostingInstructions = "Share it",
            CreatedByUserId = userId,
        };
        var ruleSet = new RewardRuleSet
        {
            CampaignId = campaign.Id, Version = 1, Currency = "USD", EffectiveFrom = now.AddDays(-5), CreatedAt = now,
            CreatedByUserId = userId, ChangeReason = "initial",
        };
        var url = $"https://www.instagram.com/p/{Guid.NewGuid():N}";
        var submission = new Submission
        {
            CampaignId = campaign.Id, UserId = userId, SocialAccountId = socialAccountId, Platform = SocialPlatform.Instagram,
            PostUrl = url, NormalizedPostUrl = Normalization.PostUrl(url)!, PostedAt = now.AddHours(-2), Status = status,
            SubmittedAt = now.AddHours(-1), RewardRuleSetId = ruleSet.Id, RewardRuleSetVersion = 1, EstimatedRewardAmount = 5m,
        };
        await api.WithDbAsync(async db =>
        {
            db.Set<Campaign>().Add(campaign);
            db.Set<RewardRuleSet>().Add(ruleSet);
            db.Set<Submission>().Add(submission);
            await db.SaveChangesAsync();
        });
        return submission.Id;
    }

    public static async Task<(TestUser User, HttpClient Client)> AdminAsync(this ApiFactory api) => await api.CreateClientAsync(Role.Admin);

    /// <summary>Changes a platform setting through the admin API (as a fresh admin).</summary>
    public static async Task SetSettingAsync(this ApiFactory api, string key, object value)
    {
        var (_, admin) = await api.AdminAsync();
        var response = await admin.PutAsJsonAsync($"/api/v1/admin/settings/{key}", new { value, reason = "test setup", confirm = true });
        await response.ReadJsonAsync();
    }

    public static async Task<User> ReloadUserAsync(this ApiFactory api, Guid id) =>
        await api.WithDbAsync(db => db.Set<User>().AsNoTracking().Include(u => u.Roles).FirstAsync(u => u.Id == id));
}
