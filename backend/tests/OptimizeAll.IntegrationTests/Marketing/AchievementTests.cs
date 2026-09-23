using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Marketing;

public sealed class AchievementTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Baseline_achievements_are_seeded_once()
    {
        var keys = await api.WithDbAsync(db => db.Set<Achievement>().Select(a => a.Key).ToListAsync());
        foreach (var key in new[] { "first-approved-post", "five-approved", "twenty-five-approved", "hundred-approved", "multi-platform", "first-100", "referral-star", "campaign-explorer" })
            Assert.Single(keys, key);

        // Re-running the seeder is a no-op.
        await api.WithDbAsync(db => new OptimizeAll.Api.Modules.Marketing.Achievements.AchievementSeeder().SeedAsync(db, default));
        Assert.Equal(keys.Count, await api.WithDbAsync(db => db.Set<Achievement>().CountAsync()));
    }

    [Fact]
    public async Task Achievements_are_awarded_once_and_shown_with_progress()
    {
        var manager = await api.CreateUserAsync(new[] { Role.CampaignManager });
        var campaign = await api.CreateCampaignAsync(manager.Id);
        var (user, client) = await api.CreateClientAsync();
        var instagram = await api.CreateSocialAccountAsync(user.Id);
        var tiktok = await api.CreateSocialAccountAsync(user.Id, SocialPlatform.TikTok);
        var first = await api.CreateSubmissionAsync(user.Id, campaign.Id, instagram, SubmissionStatus.Approved);
        await api.CreateSubmissionAsync(user.Id, campaign.Id, tiktok, SubmissionStatus.Pending);

        var approved = new SubmissionApproved(first.Id, user.Id, campaign.Id, true, api.Now());
        await api.PublishAsync(approved);
        await api.PublishAsync(approved);
        await Task.WhenAll(api.PublishAsync(approved), api.PublishAsync(approved));

        var awarded = await api.WithDbAsync(db => (
            from ua in db.Set<UserAchievement>()
            join a in db.Set<Achievement>() on ua.AchievementId equals a.Id
            where ua.UserId == user.Id
            select a.Key).ToListAsync());
        Assert.Equal(new[] { "first-approved-post" }, awarded);
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<Notification>().CountAsync(n => n.UserId == user.Id && n.Type == NotificationTypes.Achievement)));

        var mine = (await (await client.GetAsync("/api/v1/me/achievements")).ReadJsonAsync()).EnumerateArray().ToList();
        var firstPost = mine.Single(a => a.GetProperty("key").GetString() == "first-approved-post");
        Assert.NotEqual(JsonValueKind.Null, firstPost.GetProperty("awardedAt").ValueKind);
        Assert.Equal("badge-check", firstPost.GetProperty("icon").GetString());
        var five = mine.Single(a => a.GetProperty("key").GetString() == "five-approved");
        Assert.Equal(1m, five.GetProperty("progress").GetDecimal());
        Assert.Equal(5m, five.GetProperty("threshold").GetDecimal());
        Assert.Equal(JsonValueKind.Null, five.GetProperty("awardedAt").ValueKind);
    }

    [Fact]
    public async Task Marketing_manages_achievements_but_cannot_delete_awarded_ones()
    {
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var managerUser = await api.CreateUserAsync(new[] { Role.CampaignManager });
        var campaign = await api.CreateCampaignAsync(managerUser.Id);
        var user = await api.CreateUserAsync();
        var account = await api.CreateSocialAccountAsync(user.Id);
        var created = await manager.PostAsJsonAsync("/api/v1/marketing/achievements", new
        {
            key = "first-step", name = "First step", description = "Your first approved post", icon = "footprints",
            criterion = "ApprovedSubmissions", threshold = 1, sortOrder = 5,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();
        await (await manager.PostAsJsonAsync("/api/v1/marketing/achievements", new
        {
            key = "first-step", name = "Dup", description = "Dup", criterion = "ApprovedSubmissions", threshold = 1,
        })).ShouldFailAsync(409, "achievement.key_taken");

        var submission = await api.CreateSubmissionAsync(user.Id, campaign.Id, account, SubmissionStatus.Approved);
        await api.PublishAsync(new SubmissionApproved(submission.Id, user.Id, campaign.Id, true, api.Now()));

        var listed = (await (await manager.GetAsync("/api/v1/marketing/achievements")).ReadJsonAsync()).EnumerateArray()
            .Single(a => a.GetProperty("key").GetString() == "first-step");
        Assert.Equal(1, listed.GetProperty("awardedCount").GetInt32());
        await (await manager.DeleteAsync($"/api/v1/marketing/achievements/{id}")).ShouldFailAsync(409, "achievement.awarded");

        var deactivated = await (await manager.PutAsJsonAsync($"/api/v1/marketing/achievements/{id}", new
        {
            key = "first-step", name = "First step", description = "Retired", criterion = "ApprovedSubmissions", threshold = 1, isActive = false,
        })).ReadJsonAsync();
        Assert.False(deactivated.GetProperty("isActive").GetBoolean());

        var spare = await manager.PostJsonAsync("/api/v1/marketing/achievements", new
        {
            key = "never-awarded", name = "Never", description = "Never awarded", criterion = "QualifiedReferrals", threshold = 1000,
        });
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/v1/marketing/achievements/{spare.GetProperty("id").GetGuid()}")).StatusCode);
    }
}
