using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Accounts;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Support;

public sealed class SupportTicketTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Ticket_lifecycle_with_internal_notes_hidden_from_the_participant()
    {
        var (participant, client) = await api.CreateClientAsync();
        var account = await api.AddSocialAccountAsync(participant.Id, ageDays: 300);
        var submissionId = await api.AddSubmissionAsync(participant.Id, account.Id, SubmissionStatus.Rejected);

        var created = await client.PostAsJsonAsync("/api/v1/me/support/tickets", new
        {
            subject = "My submission was rejected", category = "Submission",
            body = "I included the #ad disclosure, please take another look.", submissionId,
        });
        Assert.Equal(201, (int)created.StatusCode);
        var ticket = await created.ReadJsonAsync();
        var id = ticket.GetProperty("id").GetGuid();
        Assert.Matches(new Regex("^SUP-[A-Z0-9]{6}$"), ticket.GetProperty("reference").GetString());
        Assert.Equal("Open", ticket.GetProperty("status").GetString());
        Assert.Equal(submissionId, ticket.GetProperty("submissionId").GetGuid());
        Assert.Single(ticket.GetProperty("messages").EnumerateArray());

        var (staffUser, staff) = await api.CreateClientAsync(Role.Reviewer);
        var queue = await (await staff.GetAsync($"/api/v1/admin/support/tickets?search={Uri.EscapeDataString(participant.Email)}&status=Open")).ReadJsonAsync();
        var summary = Assert.Single(queue.GetProperty("items").EnumerateArray());
        Assert.Equal(participant.Email, summary.GetProperty("requester").GetProperty("email").GetString());

        // Internal note: not visible to the participant and no notification.
        (await staff.PostAsJsonAsync($"/api/v1/admin/support/tickets/{id}/messages", new
        {
            body = "INTERNAL: user has 3 prior rejections, check carefully", isInternalNote = true,
        })).EnsureSuccessStatusCode();
        var reply = await (await staff.PostAsJsonAsync($"/api/v1/admin/support/tickets/{id}/messages", new
        {
            body = "Thanks, we're re-checking your post now.", isInternalNote = false,
        })).ReadJsonAsync();
        Assert.Equal("AwaitingParticipant", reply.GetProperty("status").GetString());
        Assert.Equal(3, reply.GetProperty("messages").GetArrayLength());
        Assert.Contains(reply.GetProperty("messages").EnumerateArray(), m => m.GetProperty("isInternalNote").GetBoolean());
        Assert.Equal(1, reply.GetProperty("requester").GetProperty("openTicketCount").GetInt32());

        var mineRaw = await (await client.GetAsync($"/api/v1/me/support/tickets/{id}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("INTERNAL", mineRaw);
        Assert.DoesNotContain("isInternalNote", mineRaw);
        var mine = await (await client.GetAsync($"/api/v1/me/support/tickets/{id}")).ReadJsonAsync();
        var messages = mine.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(2, messages.Count);
        Assert.True(messages[1].GetProperty("fromStaff").GetBoolean());
        Assert.Equal("Optimize All Support", messages[1].GetProperty("authorName").GetString());
        Assert.DoesNotContain(staffUser.Email, mineRaw);

        var notification = await api.WithDbAsync(db => db.Set<Notification>().AsNoTracking()
            .SingleAsync(n => n.UserId == participant.Id && n.Type == NotificationTypes.SupportReply));
        Assert.Contains(ticket.GetProperty("reference").GetString()!, notification.Title);
        Assert.True(await api.WithDbAsync(db => db.Set<NotificationDelivery>().AnyAsync(d => d.NotificationId == notification.Id && d.Channel == NotificationChannel.Email)));

        // Staff resolves; a participant reply reopens it as AwaitingStaff.
        var detail = await (await staff.GetAsync($"/api/v1/admin/support/tickets/{id}")).ReadJsonAsync();
        var resolved = await (await staff.PutAsJsonAsync($"/api/v1/admin/support/tickets/{id}", new
        {
            status = "Resolved", priority = "High", assignedToUserId = staffUser.Id, concurrencyStamp = detail.GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
        Assert.Equal(staffUser.Id, resolved.GetProperty("assignedTo").GetProperty("id").GetGuid());
        await (await staff.PutAsJsonAsync($"/api/v1/admin/support/tickets/{id}", new
        {
            status = "Open", priority = "High", concurrencyStamp = detail.GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(409, "concurrency.conflict");

        var assignedToMe = await (await staff.GetAsync("/api/v1/admin/support/tickets?assignedTo=me")).ReadJsonAsync();
        Assert.Contains(assignedToMe.GetProperty("items").EnumerateArray(), t => t.GetProperty("id").GetGuid() == id);

        var reopened = await (await client.PostAsJsonAsync($"/api/v1/me/support/tickets/{id}/messages", new { body = "Still waiting." })).ReadJsonAsync();
        Assert.Equal("AwaitingStaff", reopened.GetProperty("status").GetString());

        var closed = await (await client.PostAsync($"/api/v1/me/support/tickets/{id}/close", null)).ReadJsonAsync();
        Assert.Equal("Closed", closed.GetProperty("status").GetString());
        Assert.False(closed.GetProperty("canReply").GetBoolean());
        await (await client.PostAsJsonAsync($"/api/v1/me/support/tickets/{id}/messages", new { body = "Hello?" })).ShouldFailAsync(409, "support.ticket_closed");

        var list = await (await client.GetAsync("/api/v1/me/support/tickets")).ReadJsonAsync();
        Assert.Equal(1, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Linked_records_must_belong_to_the_participant_and_tickets_are_private()
    {
        var (owner, ownerClient) = await api.CreateClientAsync();
        var account = await api.AddSocialAccountAsync(owner.Id, ageDays: 300);
        var submissionId = await api.AddSubmissionAsync(owner.Id, account.Id, SubmissionStatus.Approved);

        var (_, other) = await api.CreateClientAsync();
        await (await other.PostAsJsonAsync("/api/v1/me/support/tickets", new
        {
            subject = "Someone else's submission", category = "Submission", body = "Trying to link another user's submission.", submissionId,
        })).ShouldFailAsync(400, "support.invalid_submission");
        await (await other.PostAsJsonAsync("/api/v1/me/support/tickets", new
        {
            subject = "Unknown payout", category = "Payout", body = "Trying to link a payout item that isn't mine.", payoutItemId = Guid.NewGuid(),
        })).ShouldFailAsync(400, "support.invalid_payout_item");

        var ticket = await (await ownerClient.PostAsJsonAsync("/api/v1/me/support/tickets", new
        {
            subject = "Question", category = "General", body = "A general question about campaigns.",
        })).ReadJsonAsync();
        var id = ticket.GetProperty("id").GetGuid();
        await (await other.GetAsync($"/api/v1/me/support/tickets/{id}")).ShouldFailAsync(404);
        await (await other.PostAsJsonAsync($"/api/v1/me/support/tickets/{id}/messages", new { body = "hi" })).ShouldFailAsync(404);
        await (await other.PostAsync($"/api/v1/me/support/tickets/{id}/close", null)).ShouldFailAsync(404);
        await (await ownerClient.GetAsync("/api/v1/admin/support/tickets")).ShouldFailAsync(403);

        // Assignee must hold support.manage.
        var (_, admin) = await api.AdminAsync();
        var detail = await (await admin.GetAsync($"/api/v1/admin/support/tickets/{id}")).ReadJsonAsync();
        await (await admin.PutAsJsonAsync($"/api/v1/admin/support/tickets/{id}", new
        {
            status = "Open", priority = "Normal", assignedToUserId = owner.Id, concurrencyStamp = detail.GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(400, "support.invalid_assignee");
    }
}
