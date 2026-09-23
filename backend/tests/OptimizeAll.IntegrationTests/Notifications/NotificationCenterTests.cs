using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Notifications;

public static class NotificationTestData
{
    /// <summary>Stages a notification through the real INotificationService and commits it.</summary>
    public static async Task<Guid> StageAsync(this Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, Guid userId,
        string type = NotificationTypes.SubmissionDecision, string title = "Your post was approved", string body = "Nice work!",
        string? link = "/app/submissions", params NotificationChannel[] channels)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var n = await service.StageAsync(new NotificationRequest(userId, type, title, body, link, channels));
        await db.SaveChangesAsync();
        return n.Id;
    }
}

public sealed class NotificationCenterTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task List_read_read_all_and_unread_count()
    {
        var (user, client) = await api.CreateClientAsync();
        var first = await api.StageAsync(user.Id, title: "First");
        api.Clock.Advance(TimeSpan.FromSeconds(1));
        await api.StageAsync(user.Id, title: "Second");
        api.Clock.Advance(TimeSpan.FromSeconds(1));
        await api.StageAsync(user.Id, title: "Third");

        Assert.Equal(3, (await (await client.GetAsync("/api/v1/me/notifications/unread-count")).ReadJsonAsync()).GetProperty("count").GetInt32());
        var list = await (await client.GetAsync("/api/v1/me/notifications?pageSize=2")).ReadJsonAsync();
        Assert.Equal(3, list.GetProperty("total").GetInt32());
        Assert.Equal("Third", list.GetProperty("items")[0].GetProperty("title").GetString());
        Assert.Equal(2, list.GetProperty("items").GetArrayLength());

        Assert.Equal(204, (int)(await client.PostAsync($"/api/v1/me/notifications/{first}/read", null)).StatusCode);
        Assert.Equal(204, (int)(await client.PostAsync($"/api/v1/me/notifications/{first}/read", null)).StatusCode); // idempotent
        var unread = await (await client.GetAsync("/api/v1/me/notifications?unreadOnly=true")).ReadJsonAsync();
        Assert.Equal(2, unread.GetProperty("total").GetInt32());
        Assert.DoesNotContain(unread.GetProperty("items").EnumerateArray(), n => n.GetProperty("id").GetGuid() == first);

        // Someone else's notification is not found.
        var (_, other) = await api.CreateClientAsync();
        await (await other.PostAsync($"/api/v1/me/notifications/{first}/read", null)).ShouldFailAsync(404);
        Assert.Equal(0, (await (await other.GetAsync("/api/v1/me/notifications")).ReadJsonAsync()).GetProperty("total").GetInt32());

        var all = await (await client.PostAsync("/api/v1/me/notifications/read-all", null)).ReadJsonAsync();
        Assert.Equal(2, all.GetProperty("updated").GetInt32());
        Assert.Equal(0, (await (await client.GetAsync("/api/v1/me/notifications/unread-count")).ReadJsonAsync()).GetProperty("count").GetInt32());
        var read = await (await client.GetAsync("/api/v1/me/notifications")).ReadJsonAsync();
        Assert.All(read.GetProperty("items").EnumerateArray(), n => Assert.True(n.GetProperty("isRead").GetBoolean()));
    }

    [Fact]
    public async Task Preferences_list_staff_only_types_only_for_users_with_the_matching_permission()
    {
        static async Task<List<string>> TypesAsync(HttpClient client) =>
            (await (await client.GetAsync("/api/v1/me/notification-preferences")).ReadJsonAsync())
            .GetProperty("types").EnumerateArray().Select(t => t.GetProperty("type").GetString()!).ToList();

        var (_, participant) = await api.CreateClientAsync();
        var participantTypes = await TypesAsync(participant);
        Assert.DoesNotContain(NotificationTypes.ReviewLiveCheckDue, participantTypes);
        Assert.DoesNotContain(NotificationTypes.BatchPrepared, participantTypes);
        Assert.Contains(NotificationTypes.SubmissionDecision, participantTypes);
        // A participant can't change a kind they never receive.
        await (await participant.PutAsJsonAsync("/api/v1/me/notification-preferences", new
        {
            preferences = new[] { new { type = NotificationTypes.BatchPrepared, channel = "Email", enabled = false } },
        })).ShouldFailAsync(400, "notifications.preference_locked");

        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        var reviewerTypes = await TypesAsync(reviewer);
        Assert.Contains(NotificationTypes.ReviewLiveCheckDue, reviewerTypes);
        Assert.DoesNotContain(NotificationTypes.BatchPrepared, reviewerTypes);

        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        var financeTypes = await TypesAsync(finance);
        Assert.Contains(NotificationTypes.BatchPrepared, financeTypes);
        Assert.DoesNotContain(NotificationTypes.ReviewLiveCheckDue, financeTypes);
        (await finance.PutAsJsonAsync("/api/v1/me/notification-preferences", new
        {
            preferences = new[] { new { type = NotificationTypes.BatchPrepared, channel = "Email", enabled = false } },
        })).EnsureSuccessStatusCode();

        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        Assert.Equal(20, (await TypesAsync(admin)).Count);
    }

    [Fact]
    public async Task Preferences_matrix_locks_essential_types_and_mutes_outbox_rows()
    {
        var (user, client) = await api.CreateClientAsync();
        var prefs = await (await client.GetAsync("/api/v1/me/notification-preferences")).ReadJsonAsync();

        var whatsApp = prefs.GetProperty("channels").EnumerateArray().Single(c => c.GetProperty("channel").GetString() == "WhatsApp");
        Assert.False(whatsApp.GetProperty("available").GetBoolean());
        Assert.False(string.IsNullOrEmpty(whatsApp.GetProperty("reason").GetString()));

        var types = prefs.GetProperty("types").EnumerateArray().ToList();
        // Every kind except the two staff-only ones.
        Assert.Equal(18, types.Count);
        var essential = types.Single(t => t.GetProperty("type").GetString() == NotificationTypes.PayoutPaid);
        Assert.True(essential.GetProperty("essential").GetBoolean());
        Assert.All(essential.GetProperty("channels").EnumerateArray(), c => Assert.True(c.GetProperty("locked").GetBoolean()));
        var decision = types.Single(t => t.GetProperty("type").GetString() == NotificationTypes.SubmissionDecision);
        var email = decision.GetProperty("channels").EnumerateArray().Single(c => c.GetProperty("channel").GetString() == "Email");
        Assert.True(email.GetProperty("enabled").GetBoolean());
        Assert.False(email.GetProperty("locked").GetBoolean());

        var updated = await (await client.PutAsJsonAsync("/api/v1/me/notification-preferences", new
        {
            preferences = new[] { new { type = NotificationTypes.SubmissionDecision, channel = "Email", enabled = false } },
        })).ReadJsonAsync();
        var cell = updated.GetProperty("types").EnumerateArray().Single(t => t.GetProperty("type").GetString() == NotificationTypes.SubmissionDecision)
            .GetProperty("channels").EnumerateArray().Single(c => c.GetProperty("channel").GetString() == "Email");
        Assert.False(cell.GetProperty("enabled").GetBoolean());

        // Muted: the in-app notification is still created, but no email outbox row.
        var id = await api.StageAsync(user.Id, channels: new[] { NotificationChannel.InApp, NotificationChannel.Email });
        Assert.False(await api.WithDbAsync(db => db.Set<NotificationDelivery>().AnyAsync(d => d.NotificationId == id)));

        await (await client.PutAsJsonAsync("/api/v1/me/notification-preferences", new
        {
            preferences = new[] { new { type = NotificationTypes.PayoutPaid, channel = "Email", enabled = false } },
        })).ShouldFailAsync(400, "notifications.preference_locked");
        await (await client.PutAsJsonAsync("/api/v1/me/notification-preferences", new
        {
            preferences = new[] { new { type = NotificationTypes.SubmissionDecision, channel = "InApp", enabled = false } },
        })).ShouldFailAsync(400, "notifications.preference_locked");
        await (await client.PutAsJsonAsync("/api/v1/me/notification-preferences", new
        {
            preferences = new[] { new { type = "made.up", channel = "Email", enabled = false } },
        })).ShouldFailAsync(400, "notifications.preference_locked");
    }
}
