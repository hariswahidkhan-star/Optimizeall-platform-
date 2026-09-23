using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Retention;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Jobs;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Settings;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Marketing;

namespace OptimizeAll.IntegrationTests.Retention;

public sealed class RetentionTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private async Task<TestUser> UserAsync(bool verified, TimeSpan age, TimeSpan? lastActiveAgo = null, string[]? interests = null)
    {
        var user = await api.CreateUserAsync(emailVerified: verified, interests: interests);
        var created = api.Now() - age;
        DateTime? lastActive = lastActiveAgo is { } ago ? api.Now() - ago : null;
        await api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.CreatedAt, created).SetProperty(u => u.LastActiveAt, lastActive)));
        return user;
    }

    private Task<List<(string Kind, string Key)>> LogsAsync(Guid userId) =>
        api.WithDbAsync(async db => (await db.Set<RetentionMessageLog>().Where(l => l.UserId == userId).ToListAsync())
            .Select(l => (l.Kind, l.DedupKey)).OrderBy(x => x.Kind).ThenBy(x => x.DedupKey).ToList());

    /// <summary>JobRunner keys runs by the clock's millisecond; step the frozen test clock so consecutive runs get distinct keys.</summary>
    private Task<JobRun?> RunAsync()
    {
        api.Clock.Advance(TimeSpan.FromSeconds(1));
        return api.RunJobAsync<RetentionJob>();
    }

    private Task<int> NotificationsAsync(Guid userId, string type) =>
        api.WithDbAsync(db => db.Set<Notification>().CountAsync(n => n.UserId == userId && n.Type == type));

    [Fact]
    public async Task Each_automation_sends_exactly_once_across_runs_and_respects_the_switch()
    {
        await api.SetSettingAsync(SettingKeys.RetentionEnabled, true);
        var manager = await api.CreateUserAsync(new[] { Role.CampaignManager });
        var platformCampaign = await api.CreateCampaignAsync(manager.Id, publishedAt: api.Now().AddHours(-1), title: "Instagram launch");
        var topicCampaign = await api.CreateCampaignAsync(manager.Id, publishedAt: api.Now().AddHours(-2),
            platforms: Array.Empty<SocialPlatform>(), topics: new[] { "fitness" }, title: "Fitness push");
        var oldCampaign = await api.CreateCampaignAsync(manager.Id, publishedAt: api.Now().AddDays(-4), title: "Old news");

        var unverified = await UserAsync(false, TimeSpan.FromHours(25));
        var tooFresh = await UserAsync(false, TimeSpan.FromHours(2));
        var noSocial = await UserAsync(true, TimeSpan.FromDays(4));
        var noSubmission = await UserAsync(true, TimeSpan.FromDays(8));
        await api.CreateSocialAccountAsync(noSubmission.Id);
        var ineligible = await UserAsync(true, TimeSpan.FromDays(8));
        await api.CreateSocialAccountAsync(ineligible.Id, ageDays: 10);
        var newcomer = await UserAsync(true, TimeSpan.FromDays(1));
        await api.CreateSocialAccountAsync(newcomer.Id);
        var inactive = await UserAsync(true, TimeSpan.FromDays(100), lastActiveAgo: TimeSpan.FromDays(40));
        await api.CreateSocialAccountAsync(inactive.Id);
        var fitness = await UserAsync(true, TimeSpan.FromHours(1), interests: new[] { "fitness" });
        await api.CreateSocialAccountAsync(fitness.Id);

        var run1 = await RunAsync();
        Assert.Equal(JobRunStatus.Succeeded, run1!.Status);
        var run2 = await RunAsync();
        Assert.Equal(JobRunStatus.Succeeded, run2!.Status);
        Assert.Contains("onboarding.verify_email=0", run2.Summary);
        Assert.Contains("campaign.alert=0", run2.Summary);
        Assert.Contains("reactivation=0", run2.Summary);

        var alert = platformCampaign.Id.ToString();
        var reactivationKey = RetentionJob.ReactivationKey(api.Now());
        Assert.Equal(new[] { (RetentionKinds.VerifyEmail, "d1") }, await LogsAsync(unverified.Id));
        Assert.Empty(await LogsAsync(tooFresh.Id));
        Assert.Equal(new[] { (RetentionKinds.AddSocial, "d3") }, await LogsAsync(noSocial.Id));
        Assert.Equal(new[] { (RetentionKinds.CampaignAlert, alert), (RetentionKinds.FirstSubmission, "d7") }, await LogsAsync(noSubmission.Id));
        Assert.Empty(await LogsAsync(ineligible.Id));
        Assert.Equal(new[] { (RetentionKinds.CampaignAlert, alert) }, await LogsAsync(newcomer.Id));
        Assert.Equal(new[] { (RetentionKinds.CampaignAlert, alert), (RetentionKinds.Reactivation, reactivationKey) }, await LogsAsync(inactive.Id));
        // One alert per day: the topic campaign (published first) wins; the platform campaign waits.
        Assert.Equal(new[] { (RetentionKinds.CampaignAlert, topicCampaign.Id.ToString()) }, await LogsAsync(fitness.Id));
        Assert.False(await api.WithDbAsync(db => db.Set<RetentionMessageLog>().AnyAsync(l => l.DedupKey == oldCampaign.Id.ToString())));

        // Exactly one notification per message, with the right channels and wording.
        Assert.Equal(1, await NotificationsAsync(unverified.Id, NotificationTypes.OnboardingReminder));
        var verifyMail = await api.WithDbAsync(db => db.Set<Notification>().SingleAsync(n => n.UserId == unverified.Id));
        Assert.Contains("Resend verification", verifyMail.Body);
        Assert.DoesNotContain("token", verifyMail.Body, StringComparison.OrdinalIgnoreCase);
        Assert.True(await api.WithDbAsync(db => db.Set<NotificationDelivery>().AnyAsync(d => d.NotificationId == verifyMail.Id && d.Channel == NotificationChannel.Email)));
        Assert.Equal(2, await NotificationsAsync(noSubmission.Id, NotificationTypes.OnboardingReminder) + await NotificationsAsync(noSubmission.Id, NotificationTypes.CampaignAlert));
        Assert.Equal(1, await NotificationsAsync(inactive.Id, NotificationTypes.Reactivation));
        // Campaign alerts are marketing: no email without marketing consent.
        var newcomerAlert = await api.WithDbAsync(db => db.Set<Notification>().SingleAsync(n => n.UserId == newcomer.Id && n.Type == NotificationTypes.CampaignAlert));
        Assert.False(await api.WithDbAsync(db => db.Set<NotificationDelivery>().AnyAsync(d => d.NotificationId == newcomerAlert.Id)));

        // Switched off: nothing is sent.
        await api.SetSettingAsync(SettingKeys.RetentionEnabled, false);
        var lateUnverified = await UserAsync(false, TimeSpan.FromHours(30));
        var disabled = await RunAsync();
        Assert.Equal("disabled", disabled!.Summary);
        Assert.Empty(await LogsAsync(lateUnverified.Id));

        // Back on, a day later: the waiting alert goes out, the pending reminder is sent, nothing is repeated.
        await api.SetSettingAsync(SettingKeys.RetentionEnabled, true);
        api.Clock.Advance(TimeSpan.FromHours(25));
        await RunAsync();
        await RunAsync();
        Assert.Equal(new[] { (RetentionKinds.CampaignAlert, alert), (RetentionKinds.CampaignAlert, topicCampaign.Id.ToString()) }
            .OrderBy(x => x.Item2).ToArray(), (await LogsAsync(fitness.Id)).OrderBy(x => x.Key).ToArray());
        Assert.Equal(new[] { (RetentionKinds.VerifyEmail, "d1") }, await LogsAsync(lateUnverified.Id));
        Assert.Equal(new[] { (RetentionKinds.VerifyEmail, "d1") }, await LogsAsync(unverified.Id));
        Assert.Equal(new[] { (RetentionKinds.VerifyEmail, "d1") }, await LogsAsync(tooFresh.Id)); // now past 24 hours
        Assert.Equal(1, await NotificationsAsync(unverified.Id, NotificationTypes.OnboardingReminder));
        Assert.Equal(1, await NotificationsAsync(inactive.Id, NotificationTypes.Reactivation));
        Assert.Equal(1, await NotificationsAsync(newcomer.Id, NotificationTypes.CampaignAlert));

        // Marketing reporting.
        var client = await api.LoginAsync(manager);
        var summary = await (await client.GetAsync("/api/v1/marketing/retention/summary")).ReadJsonAsync();
        var counts = summary.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("kind").GetString()!, i => i.GetProperty("sent").GetInt32());
        Assert.Equal(3, counts[RetentionKinds.VerifyEmail]);
        Assert.Equal(1, counts[RetentionKinds.AddSocial]);
        Assert.Equal(1, counts[RetentionKinds.FirstSubmission]);
        Assert.Equal(5, counts[RetentionKinds.CampaignAlert]);
        Assert.Equal(1, counts[RetentionKinds.Reactivation]);
        Assert.Equal(11, summary.GetProperty("total").GetInt32());

        var log = await (await client.GetAsync($"/api/v1/marketing/retention/log?kind={RetentionKinds.CampaignAlert}&pageSize=2")).ReadJsonAsync();
        Assert.Equal(5, log.GetProperty("total").GetInt32());
        Assert.Equal(2, log.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Reactivation_windows_are_30_days_long()
    {
        var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var keys = Enumerable.Range(0, 90).Select(d => RetentionJob.ReactivationKey(start.AddDays(d))).ToList();
        Assert.Equal(4, keys.Distinct().Count()); // 90 days touch at most 4 windows
        Assert.All(keys.GroupBy(k => k), g => Assert.True(g.Count() <= 30));
    }
}
