using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Modules.Notifications;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.IntegrationTests.Accounts;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Notifications;

/// <summary>Email sender test double that always fails (provider outage).</summary>
public sealed class FailingEmailSender : INotificationChannelSender
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public Task<ChannelSendResult> SendAsync(NotificationDelivery delivery, Notification notification, User user, CancellationToken ct) =>
        Task.FromResult(ChannelSendResult.Failed("SMTP 451: temporary failure"));
}

/// <summary>Email sender test double that records every send and is slow enough for concurrent runs to overlap.</summary>
public sealed class CountingEmailSender : INotificationChannelSender
{
    public ConcurrentDictionary<Guid, int> Sends { get; } = new();
    public NotificationChannel Channel => NotificationChannel.Email;

    public async Task<ChannelSendResult> SendAsync(NotificationDelivery delivery, Notification notification, User user, CancellationToken ct)
    {
        Sends.AddOrUpdate(delivery.Id, 1, (_, c) => c + 1);
        await Task.Delay(15, ct);
        return ChannelSendResult.Sent("msg-" + delivery.Id.ToString("N"));
    }
}

public sealed class NotificationDispatchTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    /// <summary>
    /// JobRunner derives the run key from the clock (millisecond precision) and the test clock only moves when told to,
    /// so every run nudges it forward by 1 ms to keep run keys unique.
    /// </summary>
    private Task<OptimizeAll.Domain.Jobs.JobRun?> RunAsync(JobRunner runner)
    {
        api.Clock.Advance(TimeSpan.FromMilliseconds(1));
        return runner.RunAsync<NotificationDispatchJob>();
    }

    private Task<OptimizeAll.Domain.Jobs.JobRun?> RunAsync() => RunAsync(api.Services.GetRequiredService<JobRunner>());

    private async Task<NotificationDelivery> DeliveryAsync(Guid notificationId, NotificationChannel channel) =>
        await api.WithDbAsync(db => db.Set<NotificationDelivery>().AsNoTracking().FirstAsync(d => d.NotificationId == notificationId && d.Channel == channel));

    [Fact]
    public async Task Email_is_sent_to_the_mailbox_and_WhatsApp_is_skipped_when_not_configured()
    {
        var user = await api.CreateUserAsync();
        await api.WithDbAsync(async db =>
        {
            var u = await db.Set<User>().FindAsync(user.Id);
            u!.WhatsAppNumber = "+923001234567";
            u.WhatsAppOptIn = true;
            await db.SaveChangesAsync();
        });
        var id = await api.StageAsync(user.Id, NotificationTypes.SubmissionDecision, "Approved: <Summer> campaign", "Your post was approved.",
            "/app/submissions/1", NotificationChannel.InApp, NotificationChannel.Email, NotificationChannel.WhatsApp);

        var run = await RunAsync();
        Assert.NotNull(run);
        Assert.Equal(OptimizeAll.Domain.Jobs.JobRunStatus.Succeeded, run!.Status);

        var email = await DeliveryAsync(id, NotificationChannel.Email);
        Assert.Equal(DeliveryStatus.Sent, email.Status);
        Assert.NotNull(email.SentAt);
        Assert.False(string.IsNullOrEmpty(email.ProviderMessageId));

        var whatsApp = await DeliveryAsync(id, NotificationChannel.WhatsApp);
        Assert.Equal(DeliveryStatus.Skipped, whatsApp.Status);
        Assert.Equal(WhatsAppChannelSender.NotConfiguredError, whatsApp.LastError);

        var mail = await (await api.CreateClient().GetAsync($"/api/v1/dev/mailbox?to={Uri.EscapeDataString(user.Email)}")).ReadJsonAsync();
        Assert.Equal("Approved: <Summer> campaign", mail.GetProperty("subject").GetString());
        Assert.Contains("http://app.test/app/submissions/1", mail.GetProperty("text").GetString());
        var eml = Directory.GetFiles(api.MailDirectory, "*.eml").Select(File.ReadAllText).Single(t => t.Contains(user.Email));
        Assert.DoesNotContain("<Summer>", eml.Split("text/html")[1]); // HTML part is encoded

        // Sent deliveries are never re-sent.
        int MailsFor() => Directory.GetFiles(api.MailDirectory, "*.eml").Count(f => File.ReadAllText(f).Contains(user.Email));
        Assert.Equal(1, MailsFor());
        await RunAsync();
        Assert.Equal(1, MailsFor());
        Assert.Equal(DeliveryStatus.Sent, (await DeliveryAsync(id, NotificationChannel.Email)).Status);
    }

    [Fact]
    public async Task Failed_sends_back_off_then_fail_permanently_and_can_be_retried_by_an_admin()
    {
        await using var failing = api.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddScoped<INotificationChannelSender, FailingEmailSender>()));
        await failing.StartAsync();
        var runner = failing.Services.GetRequiredService<JobRunner>();

        var user = await api.CreateUserAsync();
        var id = await api.StageAsync(user.Id, channels: NotificationChannel.Email);

        var expectedDelays = new[] { 1, 5, 30, 120, 720 };
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await RunAsync(runner);
            var d = await DeliveryAsync(id, NotificationChannel.Email);
            Assert.Equal(DeliveryStatus.Pending, d.Status);
            Assert.Equal(attempt, d.Attempts);
            Assert.Equal("SMTP 451: temporary failure", d.LastError);
            var now = api.Clock.GetUtcNow().UtcDateTime;
            Assert.Equal(now.AddMinutes(expectedDelays[attempt - 1]), d.NextAttemptAt, TimeSpan.FromSeconds(1));

            // Not due yet: another run does not touch it.
            await RunAsync(runner);
            Assert.Equal(attempt, (await DeliveryAsync(id, NotificationChannel.Email)).Attempts);
            api.Clock.Advance(TimeSpan.FromMinutes(expectedDelays[attempt - 1]) + TimeSpan.FromSeconds(1));
        }

        await RunAsync(runner);
        var failed = await DeliveryAsync(id, NotificationChannel.Email);
        Assert.Equal(DeliveryStatus.Failed, failed.Status);
        Assert.Equal(6, failed.Attempts);

        // Operator view + manual retry.
        var (_, admin) = await api.AdminAsync();
        var list = await (await admin.GetAsync($"/api/v1/admin/notifications/deliveries?status=Failed&channel=Email&userId={user.Id}")).ReadJsonAsync();
        var row = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(user.Email, row.GetProperty("userEmail").GetString());
        Assert.Equal("SMTP 451: temporary failure", row.GetProperty("lastError").GetString());

        var retried = await (await admin.PostAsync($"/api/v1/admin/notifications/deliveries/{failed.Id}/retry", null)).ReadJsonAsync();
        Assert.Equal("Pending", retried.GetProperty("status").GetString());
        await (await admin.PostAsync($"/api/v1/admin/notifications/deliveries/{failed.Id}/retry", null)).ShouldFailAsync(409, "notifications.not_failed");
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "notification.delivery_retried" && a.EntityId == failed.Id.ToString())));

        // With the real email sender it now goes out.
        await RunAsync();
        Assert.Equal(DeliveryStatus.Sent, (await DeliveryAsync(id, NotificationChannel.Email)).Status);

        var (_, participant) = await api.CreateClientAsync();
        await (await participant.GetAsync("/api/v1/admin/notifications/deliveries")).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Concurrent_dispatch_runs_never_send_a_delivery_twice()
    {
        var counter = new CountingEmailSender();
        await using var counting = api.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<INotificationChannelSender>(counter)));
        await counting.StartAsync();

        var user = await api.CreateUserAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 40; i++) ids.Add(await api.StageAsync(counting, user.Id, $"n{i}"));
        var deliveryIds = await api.WithDbAsync(db => db.Set<NotificationDelivery>().AsNoTracking()
            .Where(d => ids.Contains(d.NotificationId)).Select(d => d.Id).ToListAsync());
        Assert.Equal(40, deliveryIds.Count);

        // Four job instances in separate scopes (as on four API instances) race over the same outbox.
        var runs = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            using var scope = counting.Services.CreateScope();
            var job = scope.ServiceProvider.GetRequiredService<NotificationDispatchJob>();
            return await job.ExecuteAsync(CancellationToken.None);
        })).ToArray();
        await Task.WhenAll(runs);

        foreach (var deliveryId in deliveryIds)
            Assert.Equal(1, counter.Sends.GetValueOrDefault(deliveryId));
        var statuses = await api.WithDbAsync(db => db.Set<NotificationDelivery>().AsNoTracking()
            .Where(d => deliveryIds.Contains(d.Id)).Select(d => d.Status).ToListAsync());
        Assert.All(statuses, s => Assert.Equal(DeliveryStatus.Sent, s));
    }
}

internal static class DispatchTestExtensions
{
    public static Task<Guid> StageAsync(this ApiFactory _, WebApplicationFactory<Program> factory, Guid userId, string title) =>
        factory.StageAsync(userId, NotificationTypes.SubmissionDecision, title, "Body", "/app", NotificationChannel.Email);
}
