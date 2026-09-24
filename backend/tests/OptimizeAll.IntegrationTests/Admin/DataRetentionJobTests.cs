using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Modules.Admin.Housekeeping;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Jobs;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Marketing;

namespace OptimizeAll.IntegrationTests.Admin;

/// <summary>
/// DataRetentionJob (docs/DATABASE.md § Retention): one test per policy. Row ids are generated on the real clock while
/// timestamps follow the test clock, so each test advances the test clock past the cutoff instead of back-dating rows
/// (the job's primary-key range only ever narrows the candidates; the time condition decides).
/// </summary>
public sealed class DataRetentionJobTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private Task<JobRun?> RunAsync()
    {
        api.Clock.Advance(TimeSpan.FromSeconds(1));
        return api.RunJobAsync<DataRetentionJob>();
    }

    private Task<bool> ExistsAsync<T>(Guid id) where T : OptimizeAll.Domain.Common.Entity =>
        api.WithDbAsync(db => db.Set<T>().AnyAsync(e => e.Id == id));

    private static string Hash() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Job_runs_older_than_the_retention_period_are_deleted_but_running_ones_are_kept()
    {
        var now = api.Now();
        JobRun Run(double daysAgo, JobRunStatus status) => new()
        {
            JobName = "RetentionProbeJob", RunKey = Guid.NewGuid().ToString("N"), Status = status, Attempt = 1,
            StartedAt = now.AddDays(-daysAgo), FinishedAt = status == JobRunStatus.Running ? null : now.AddDays(-daysAgo),
        };
        var old = Run(31, JobRunStatus.Succeeded);
        var oldFailed = Run(45, JobRunStatus.Failed);
        var recent = Run(29, JobRunStatus.Succeeded);
        var stuck = Run(40, JobRunStatus.Running);
        await api.WithDbAsync(db => { db.AddRange(old, oldFailed, recent, stuck); return db.SaveChangesAsync(); });

        var run = await RunAsync();

        Assert.Equal(JobRunStatus.Succeeded, run!.Status);
        Assert.Contains("job_runs=", run.Summary);
        Assert.False(await ExistsAsync<JobRun>(old.Id));
        Assert.False(await ExistsAsync<JobRun>(oldFailed.Id));
        Assert.True(await ExistsAsync<JobRun>(recent.Id));
        Assert.True(await ExistsAsync<JobRun>(stuck.Id));
        Assert.True(await ExistsAsync<JobRun>(run.Id)); // its own run log row
    }

    [Fact]
    public async Task Read_notifications_go_after_180_days_all_after_365_but_never_with_a_queued_delivery()
    {
        var user = await api.CreateUserAsync();
        var now = api.Now();
        Notification N(bool read) => new()
        {
            UserId = user.Id, Type = "retention.probe", Title = "t", Body = "b", CreatedAt = now, ReadAt = read ? now.AddHours(1) : null,
        };
        var read = N(read: true);
        var unread = N(read: false);
        var readWithQueuedEmail = N(read: true);
        var sentEmail = new NotificationDelivery
        {
            NotificationId = read.Id, UserId = user.Id, Channel = NotificationChannel.Email, Status = DeliveryStatus.Sent,
            NextAttemptAt = now, CreatedAt = now, SentAt = now,
        };
        var queuedEmail = new NotificationDelivery
        {
            NotificationId = readWithQueuedEmail.Id, UserId = user.Id, Channel = NotificationChannel.Email, Status = DeliveryStatus.Pending,
            NextAttemptAt = now.AddYears(2), CreatedAt = now,
        };
        await api.WithDbAsync(async db =>
        {
            db.AddRange(read, unread, readWithQueuedEmail);
            await db.SaveChangesAsync();
            db.AddRange(sentEmail, queuedEmail);
            await db.SaveChangesAsync();
        });

        api.Clock.Advance(TimeSpan.FromDays(179));
        await RunAsync();
        Assert.True(await ExistsAsync<Notification>(read.Id));

        api.Clock.Advance(TimeSpan.FromDays(2));
        await RunAsync();
        Assert.False(await ExistsAsync<Notification>(read.Id));
        Assert.False(await ExistsAsync<NotificationDelivery>(sentEmail.Id)); // cascades with its notification
        Assert.True(await ExistsAsync<Notification>(unread.Id));
        Assert.True(await ExistsAsync<Notification>(readWithQueuedEmail.Id));

        api.Clock.Advance(TimeSpan.FromDays(185));
        await RunAsync();
        Assert.False(await ExistsAsync<Notification>(unread.Id));
        Assert.True(await ExistsAsync<Notification>(readWithQueuedEmail.Id));
        Assert.True(await ExistsAsync<NotificationDelivery>(queuedEmail.Id));
    }

    [Fact]
    public async Task Expired_tokens_are_deleted_after_the_grace_period()
    {
        var user = await api.CreateUserAsync();
        var now = api.Now();
        var refresh = new RefreshToken { UserId = user.Id, TokenHash = Hash(), FamilyId = Guid.NewGuid(), CreatedAt = now, ExpiresAt = now.AddDays(14) };
        var verification = new UserToken
        {
            UserId = user.Id, Purpose = UserTokenPurpose.EmailVerification, TokenHash = Hash(), CreatedAt = now, ExpiresAt = now.AddDays(1),
        };
        await api.WithDbAsync(db => { db.AddRange(refresh, verification); return db.SaveChangesAsync(); });

        api.Clock.Advance(TimeSpan.FromDays(40)); // user token expired 39 days ago, refresh token 26 days ago
        await RunAsync();
        Assert.False(await ExistsAsync<UserToken>(verification.Id));
        Assert.True(await ExistsAsync<RefreshToken>(refresh.Id));

        api.Clock.Advance(TimeSpan.FromDays(5));
        await RunAsync();
        Assert.False(await ExistsAsync<RefreshToken>(refresh.Id));
    }

    [Fact]
    public async Task Tracking_events_are_kept_by_default_and_pruned_only_when_configured()
    {
        var admin = await api.CreateUserAsync(new[] { Role.Admin });
        var campaign = await api.CreateCampaignAsync(admin.Id);
        var link = new TrackingLink
        {
            Code = "ret" + Guid.NewGuid().ToString("N")[..8], CampaignId = campaign.Id, DestinationUrl = "https://example.com",
            UtmSource = "optimizeall", UtmCampaign = "retention", CreatedAt = api.Now(),
        };
        var old = new TrackingClick { TrackingLinkId = link.Id, ClickedAt = api.Now(), IsUnique = true };
        await api.WithDbAsync(async db => { db.Add(link); await db.SaveChangesAsync(); db.Add(old); await db.SaveChangesAsync(); });

        api.Clock.Advance(TimeSpan.FromDays(100));
        var recent = new TrackingClick { TrackingLinkId = link.Id, ClickedAt = api.Now(), IsUnique = true };
        await api.WithDbAsync(db => { db.Add(recent); return db.SaveChangesAsync(); });

        await RunAsync(); // TrackingEventDays defaults to 0: analytics history is kept
        Assert.True(await ExistsAsync<TrackingClick>(old.Id));

        var summary = await api.WithDbAsync(db => new DataRetentionJob(db, Options.Create(new DataRetentionOptions
        {
            JobRunDays = 0, ReadNotificationDays = 0, NotificationDays = 0, ExpiredTokenDays = 0, TrackingEventDays = 90,
        }), api.Clock).ExecuteAsync(CancellationToken.None));

        Assert.Contains("tracking_clicks=", summary);
        Assert.False(await ExistsAsync<TrackingClick>(old.Id));
        Assert.True(await ExistsAsync<TrackingClick>(recent.Id));
    }

    [Fact]
    public async Task Disabled_retention_deletes_nothing()
    {
        var summary = await api.WithDbAsync(db => new DataRetentionJob(db, Options.Create(new DataRetentionOptions { Enabled = false }), api.Clock)
            .ExecuteAsync(CancellationToken.None));
        Assert.Equal("disabled", summary);
    }
}
