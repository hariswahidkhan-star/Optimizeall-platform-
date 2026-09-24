using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Domain.Support;
using OptimizeAll.IntegrationTests.Campaigns;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Admin;

/// <summary>
/// Status actions that complete the CRUD surface of the core platform: restore archived campaigns, reorder campaign
/// categories, reopen and re-file support tickets, restore settings defaults, mark notifications unread.
/// </summary>
public sealed class CoreCrudCompletenessTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly CampaignTestKit kit = new(api);

    [Fact]
    public async Task Archived_campaigns_can_be_restored_to_draft_or_ended()
    {
        var (_, manager) = await kit.ManagerAsync();
        var draft = await kit.CreateCampaignAsync(manager, publish: false);
        (await manager.PostAsync($"/api/v1/admin/campaigns/{draft.Id}/archive", null)).EnsureSuccessStatusCode();
        var restored = await (await manager.PostAsync($"/api/v1/admin/campaigns/{draft.Id}/unarchive", null)).ReadJsonAsync();
        Assert.Equal("Draft", restored.GetProperty("status").GetString());

        var published = await kit.CreateCampaignAsync(manager);
        (await manager.PostAsJsonAsync($"/api/v1/admin/campaigns/{published.Id}/end", new { reason = "Budget spent" })).EnsureSuccessStatusCode();
        (await manager.PostAsync($"/api/v1/admin/campaigns/{published.Id}/archive", null)).EnsureSuccessStatusCode();
        var ended = await (await manager.PostAsync($"/api/v1/admin/campaigns/{published.Id}/unarchive", null)).ReadJsonAsync();
        Assert.Equal("Ended", ended.GetProperty("status").GetString());

        // Only archived campaigns can be restored.
        await (await manager.PostAsync($"/api/v1/admin/campaigns/{published.Id}/unarchive", null)).ShouldFailAsync(409, "campaign.invalid_transition");
        var (_, reviewer) = await kit.ReviewerAsync();
        await (await reviewer.PostAsync($"/api/v1/admin/campaigns/{draft.Id}/unarchive", null)).ShouldFailAsync(403);
        Assert.Contains("campaign.unarchived", await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityId == draft.Id.ToString()).Select(a => a.Action).ToListAsync()));
    }

    [Fact]
    public async Task Campaign_categories_can_be_reordered()
    {
        var (_, manager) = await kit.ManagerAsync();
        var ids = (await (await manager.GetAsync("/api/v1/admin/campaign-categories")).ReadJsonAsync()).EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid()).ToList();
        var reversed = Enumerable.Reverse(ids).ToList();
        var saved = await (await manager.PostAsJsonAsync("/api/v1/admin/campaign-categories/reorder", new { ids = reversed })).ReadJsonAsync();
        Assert.Equal(reversed, saved.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList());
        var publicOrder = (await (await api.CreateClient().GetAsync("/api/v1/campaign-categories")).ReadJsonAsync()).EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(reversed.Where(publicOrder.Contains), publicOrder);

        await (await manager.PostAsJsonAsync("/api/v1/admin/campaign-categories/reorder", new { ids = new[] { ids[0], ids[0] } }))
            .ShouldFailAsync(400, "category.reorder_duplicates");
        await (await manager.PostAsJsonAsync("/api/v1/admin/campaign-categories/reorder", new { ids = new[] { Guid.NewGuid() } }))
            .ShouldFailAsync(400, "category.reorder_unknown");
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        await (await finance.PostAsJsonAsync("/api/v1/admin/campaign-categories/reorder", new { ids = reversed })).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Participants_reopen_closed_tickets_within_30_days_and_staff_can_refile_them()
    {
        var (_, participant) = await api.CreateClientAsync(Role.Participant);
        var ticket = await (await participant.PostAsJsonAsync("/api/v1/me/support/tickets", new
        {
            subject = "Payout question", category = "Payout", body = "When will my payout arrive this month?",
        })).ReadJsonAsync();
        var id = ticket.GetProperty("id").GetGuid();

        // Staff re-file the ticket under another category (and the change is audited).
        var (_, staff) = await api.CreateClientAsync(Role.Admin);
        var staffView = await (await staff.GetAsync($"/api/v1/admin/support/tickets/{id}")).ReadJsonAsync();
        var refiled = await (await staff.PutAsJsonAsync($"/api/v1/admin/support/tickets/{id}", new
        {
            status = "Open", priority = "Normal", category = "Account", concurrencyStamp = staffView.GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        Assert.Equal("Account", refiled.GetProperty("category").GetString());
        await (await staff.PutAsJsonAsync($"/api/v1/admin/support/tickets/{id}", new
        {
            status = "Open", priority = "Normal", concurrencyStamp = staffView.GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(409, "concurrency.conflict");

        var closed = await (await participant.PostAsync($"/api/v1/me/support/tickets/{id}/close", null)).ReadJsonAsync();
        Assert.Equal("Closed", closed.GetProperty("status").GetString());
        Assert.False(closed.GetProperty("canReply").GetBoolean());

        var reopened = await (await participant.PostAsync($"/api/v1/me/support/tickets/{id}/reopen", null)).ReadJsonAsync();
        Assert.Equal("AwaitingStaff", reopened.GetProperty("status").GetString());
        Assert.True(reopened.GetProperty("canReply").GetBoolean());

        // Someone else's ticket is invisible; a ticket closed more than 30 days ago must be filed anew.
        var (_, other) = await api.CreateClientAsync(Role.Participant);
        await (await other.PostAsync($"/api/v1/me/support/tickets/{id}/reopen", null)).ShouldFailAsync(404);
        (await participant.PostAsync($"/api/v1/me/support/tickets/{id}/close", null)).EnsureSuccessStatusCode();
        await api.WithDbAsync(async db =>
        {
            var t = await db.Set<SupportTicket>().SingleAsync(x => x.Id == id);
            t.ResolvedAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(-31);
            await db.SaveChangesAsync();
        });
        await (await participant.PostAsync($"/api/v1/me/support/tickets/{id}/reopen", null)).ShouldFailAsync(409, "support.reopen_expired");
    }

    [Fact]
    public async Task Settings_can_be_restored_to_their_default_with_a_reason()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        (await admin.PutAsJsonAsync($"/api/v1/admin/settings/{SettingKeys.MinAccountAgeDays}", new { value = 120, reason = "Tighter checks", confirm = true }))
            .EnsureSuccessStatusCode();

        await (await admin.PostAsJsonAsync($"/api/v1/admin/settings/{SettingKeys.MinAccountAgeDays}/reset", new { reason = "Back to normal" }))
            .ShouldFailAsync(400, "admin.confirmation_required");
        await (await admin.PostAsJsonAsync($"/api/v1/admin/settings/{SettingKeys.MinAccountAgeDays}/reset", new { confirm = true }))
            .ShouldFailAsync(400);
        await (await admin.PostAsJsonAsync("/api/v1/admin/settings/no.such.key/reset", new { reason = "Back to normal", confirm = true }))
            .ShouldFailAsync(404);

        var reset = await (await admin.PostAsJsonAsync($"/api/v1/admin/settings/{SettingKeys.MinAccountAgeDays}/reset",
            new { reason = "Back to normal", confirm = true })).ReadJsonAsync();
        Assert.True(reset.GetProperty("isDefault").GetBoolean());
        Assert.Equal(90, reset.GetProperty("value").GetInt32());
        Assert.False(await api.WithDbAsync(db => db.Set<SystemSetting>().AnyAsync(s => s.Key == SettingKeys.MinAccountAgeDays)));
        Assert.Contains("admin.setting_reset", await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityId == SettingKeys.MinAccountAgeDays).Select(a => a.Action).ToListAsync()));

        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        await (await finance.PostAsJsonAsync($"/api/v1/admin/settings/{SettingKeys.MinAccountAgeDays}/reset",
            new { reason = "Back to normal", confirm = true })).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Notifications_can_be_marked_unread_again()
    {
        var (user, client) = await api.CreateClientAsync(Role.Participant);
        var notification = new Notification
        {
            UserId = user.Id, Type = NotificationTypes.PayoutPaid, Title = "Paid", Body = "Sent", CreatedAt = api.Clock.GetUtcNow().UtcDateTime,
        };
        await api.WithDbAsync(async db =>
        {
            db.Add(notification);
            await db.SaveChangesAsync();
        });
        Assert.Equal(204, (int)(await client.PostAsync($"/api/v1/me/notifications/{notification.Id}/read", null)).StatusCode);
        Assert.Equal(0, (await (await client.GetAsync("/api/v1/me/notifications/unread-count")).ReadJsonAsync()).GetProperty("count").GetInt32());

        Assert.Equal(204, (int)(await client.PostAsync($"/api/v1/me/notifications/{notification.Id}/unread", null)).StatusCode);
        Assert.Equal(1, (await (await client.GetAsync("/api/v1/me/notifications/unread-count")).ReadJsonAsync()).GetProperty("count").GetInt32());
        Assert.Equal(204, (int)(await client.PostAsync($"/api/v1/me/notifications/{notification.Id}/unread", null)).StatusCode); // idempotent

        var (_, other) = await api.CreateClientAsync(Role.Participant);
        await (await other.PostAsync($"/api/v1/me/notifications/{notification.Id}/unread", null)).ShouldFailAsync(404);
    }
}
