using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Notifications;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Api.Modules.PaymentsHub;
using Microsoft.Extensions.Options;
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

        // Admins receive every kind except those only client-portal users get.
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var clientOnly = NotificationCatalog.StaffTypes.Count(kv => kv.Value.SequenceEqual(new[] { Permissions.ClientPortal }));
        Assert.Equal(NotificationCatalog.AllTypes.Count - clientOnly, (await TypesAsync(admin)).Count);
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
        // Every kind except the two staff-only ones (including the two discount-code kinds).
        Assert.Equal(20, types.Count);
        Assert.Contains(types, t => t.GetProperty("type").GetString() == NotificationTypes.CodeSaleDecision);
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

    private static async Task<List<JsonElement>> RowsAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/v1/me/notification-preferences")).ReadJsonAsync()).GetProperty("types").EnumerateArray().ToList();

    [Fact]
    public async Task Staff_and_client_kinds_of_the_agency_modules_are_listed_per_audience_and_grouped()
    {
        var (_, manager) = await api.CreateClientAsync(Role.AccountManager);
        var rows = await RowsAsync(manager);
        var types = rows.Select(r => r.GetProperty("type").GetString()).ToList();
        Assert.Contains(BillingNotificationTypes.LeadAssigned, types);
        Assert.Contains(PaymentClaimService.ClaimSubmittedType, types);
        Assert.Contains(BillingNotificationTypes.InvoicePaid, types);
        Assert.Contains("projects.task_assigned", types);
        // Kinds for client users or participants are not offered to staff who never receive them.
        Assert.DoesNotContain(BillingNotificationTypes.InvoiceReminder, types);
        Assert.DoesNotContain(NotificationTypes.SubmissionDecision, types);
        Assert.Equal("Sales, billing and payments",
            rows.Single(r => r.GetProperty("type").GetString() == PaymentClaimService.ClaimSubmittedType).GetProperty("group").GetString());

        var (_, client) = await api.CreateClientAsync(Role.Client);
        var clientTypes = (await RowsAsync(client)).Select(r => r.GetProperty("type").GetString()).ToList();
        Assert.Contains(BillingNotificationTypes.InvoiceReminder, clientTypes);
        Assert.Contains(PaymentClaimService.ClaimReviewedType, clientTypes);
        Assert.DoesNotContain(PaymentClaimService.ClaimSubmittedType, clientTypes);
        Assert.DoesNotContain(NotificationTypes.SubmissionDecision, clientTypes);

        var (_, participant) = await api.CreateClientAsync();
        await (await participant.PutAsJsonAsync("/api/v1/me/notification-preferences", new
        {
            preferences = new[] { new { type = PaymentClaimService.ClaimSubmittedType, channel = "Email", enabled = false } },
        })).ShouldFailAsync(400, "notifications.preference_locked");
    }

    [Fact]
    public async Task Staff_preferences_for_payments_hub_claims_mute_the_email()
    {
        var (finance, client) = await api.CreateClientAsync(Role.Finance);
        (await client.PutAsJsonAsync("/api/v1/me/notification-preferences", new
        {
            preferences = new[] { new { type = PaymentClaimService.ClaimSubmittedType, channel = "Email", enabled = false } },
        })).EnsureSuccessStatusCode();

        var muted = await api.StageAsync(finance.Id, PaymentClaimService.ClaimSubmittedType, "Acme reported a payment", "…", "/finance/payments",
            NotificationChannel.InApp, NotificationChannel.Email);
        Assert.False(await api.WithDbAsync(db => db.Set<NotificationDelivery>().AnyAsync(d => d.NotificationId == muted)));
        Assert.True(await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.Id == muted)));

        // Another kind is unaffected.
        var other = await api.StageAsync(finance.Id, BillingNotificationTypes.InvoicePaid, "Invoice paid", "…", "/agency/billing",
            NotificationChannel.InApp, NotificationChannel.Email);
        Assert.True(await api.WithDbAsync(db => db.Set<NotificationDelivery>().AnyAsync(d => d.NotificationId == other && d.Channel == NotificationChannel.Email)));
    }

    [Fact]
    public async Task WhatsApp_follows_email_when_configured_opted_in_and_not_muted_for_the_kind()
    {
        var (user, client) = await api.CreateClientAsync();
        await api.WithDbAsync(async db =>
        {
            var u = await db.Set<User>().FirstAsync(x => x.Id == user.Id);
            u.WhatsAppNumber = "+923001234567";
            u.WhatsAppOptIn = true;
            await db.SaveChangesAsync();
        });
        var configured = Options.Create(new WhatsAppOptions
        {
            Enabled = true, PhoneNumberId = "1", AccessToken = "t", TemplateName = "notice",
        });

        async Task<List<NotificationChannel>> StageAsync(string type) => await api.WithDbAsync(async db =>
        {
            var n = await new NotificationService(db, api.Clock, configured).StageAsync(new NotificationRequest(user.Id, type, "T", "B", null,
                new[] { NotificationChannel.Email }));
            await db.SaveChangesAsync();
            return await db.Set<NotificationDelivery>().Where(d => d.NotificationId == n.Id).Select(d => d.Channel).ToListAsync();
        });

        Assert.Equal(new[] { NotificationChannel.Email, NotificationChannel.WhatsApp }.OrderBy(c => c), (await StageAsync(NotificationTypes.SubmissionDecision)).OrderBy(c => c));

        (await client.PutAsJsonAsync("/api/v1/me/notification-preferences", new
        {
            preferences = new[] { new { type = NotificationTypes.SubmissionDecision, channel = "WhatsApp", enabled = false } },
        })).EnsureSuccessStatusCode();
        Assert.Equal(new[] { NotificationChannel.Email }, await StageAsync(NotificationTypes.SubmissionDecision));

        // Marketing kinds need marketing consent on WhatsApp too.
        Assert.Empty(await StageAsync(NotificationTypes.CampaignAlert));

        // Without configuration nothing is added (the default in tests).
        var plain = await api.StageAsync(user.Id, NotificationTypes.SupportReply, channels: new[] { NotificationChannel.Email });
        Assert.False(await api.WithDbAsync(db => db.Set<NotificationDelivery>().AnyAsync(d => d.NotificationId == plain && d.Channel == NotificationChannel.WhatsApp)));
    }
}
