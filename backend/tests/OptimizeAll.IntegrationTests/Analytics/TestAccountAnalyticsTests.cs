using System.Net.Http.Json;
using System.Text.Json;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Marketing;

namespace OptimizeAll.IntegrationTests.Analytics;

/// <summary>Owns its database: one real participant and one test participant with identical activity.</summary>
public sealed class TestAccountAnalyticsTests : IAsyncLifetime
{
    private readonly ApiFactory api = new();

    public Task InitializeAsync() => api.InitializeAsync();

    public Task DisposeAsync() => api.DisposeAsync();

    private static decimal? Value(JsonElement section, string key)
    {
        var metric = section.GetProperty("metrics").EnumerateArray().First(m => m.GetProperty("key").GetString() == key);
        var v = metric.GetProperty("value");
        return v.ValueKind == JsonValueKind.Null ? null : v.GetDecimal();
    }

    [Fact]
    public async Task Test_accounts_are_left_out_of_analytics_kpis_and_the_tracking_leaderboard()
    {
        var (managerUser, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var now = api.Now();
        var campaign = await api.CreateCampaignAsync(managerUser.Id, title: "Gamma");

        var real = await api.CreateUserAsync();
        var created = await (await admin.PostAsJsonAsync("/api/v1/admin/test-users", new { roles = new[] { "Participant" } })).ReadJsonAsync();
        var testUserId = created.GetProperty("id").GetGuid();

        foreach (var userId in new[] { real.Id, testUserId })
        {
            var account = await api.CreateSocialAccountAsync(userId, SocialPlatform.Instagram, followers: 1000, ageDays: 400);
            var submission = await api.CreateSubmissionAsync(userId, campaign.Id, account, SubmissionStatus.Approved);
            await api.WithDbAsync(async db =>
            {
                db.Add(MarketingTestData.Earning(userId, submission, EarningType.PostReward, 5m, EarningStatus.Approved, now));
                var link = new TrackingLink
                {
                    Code = "tst" + Guid.NewGuid().ToString("N")[..8], CampaignId = campaign.Id, UserId = userId,
                    DestinationUrl = "https://x.example.com", UtmSource = "optimizeall", UtmCampaign = "g", CreatedAt = now,
                };
                db.Add(link);
                db.Add(new TrackingClick { TrackingLinkId = link.Id, ClickedAt = now.AddMinutes(-30), VisitorHash = new string('b', 64), IsUnique = true });
                db.Add(new TrackingConversion
                {
                    TrackingLinkId = link.Id, ExternalReference = "o-" + Guid.NewGuid().ToString("N"), Value = 10m, Currency = "USD",
                    OccurredAt = now.AddMinutes(-20), ReceivedAt = now, VerifiedAt = now,
                });
                await db.SaveChangesAsync();
            });
        }

        var range = $"from={Uri.EscapeDataString(now.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(now.AddHours(1).ToString("O"))}";
        var o = await (await manager.GetAsync($"/api/v1/analytics/overview?{range}")).ReadJsonAsync();
        Assert.Equal(1m, Value(o.GetProperty("funnel"), "registrations"));
        Assert.Equal(1m, Value(o.GetProperty("funnel"), "participantsWithSubmission"));
        Assert.Equal(1m, Value(o.GetProperty("posts"), "postsSubmitted"));
        Assert.Equal(1m, Value(o.GetProperty("posts"), "postsApproved"));
        Assert.Equal(5m, Value(o.GetProperty("spend"), "spend"));
        Assert.Equal(1000m, Value(o.GetProperty("reach"), "estimatedReach"));
        Assert.Equal(1m, Value(o.GetProperty("traffic"), "trackedClicks"));
        Assert.Equal(1m, Value(o.GetProperty("conversions"), "verifiedConversions"));
        Assert.Equal(1, o.GetProperty("timeseries").EnumerateArray().Sum(p => p.GetProperty("approvals").GetInt32()));

        var summary = await (await manager.GetAsync($"/api/v1/marketing/tracking/summary?{range}")).ReadJsonAsync();
        Assert.Equal(1, summary.GetProperty("clicks").GetInt32());
        var top = summary.GetProperty("topParticipants").EnumerateArray().Select(r => r.GetProperty("userId").GetGuid()).ToList();
        Assert.Equal(new[] { real.Id }, top);
    }
}
