using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Accounts;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Admin;

public sealed class AdminUsersTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Suspension_revokes_existing_tokens_and_refresh_and_reactivation_restores_access()
    {
        var participant = await api.CreateUserAsync();
        var client = await api.LoginAsync(participant); // holds a refresh cookie too
        (await client.GetAsync("/api/v1/me/profile")).EnsureSuccessStatusCode();

        var (adminUser, admin) = await api.AdminAsync();
        await (await admin.PostAsJsonAsync($"/api/v1/admin/users/{participant.Id}/suspend", new { reason = "Fake engagement", confirm = false }))
            .ShouldFailAsync(400, "admin.confirmation_required");

        var suspended = await (await admin.PostAsJsonAsync($"/api/v1/admin/users/{participant.Id}/suspend", new
        {
            reason = "Fake engagement on submissions", confirm = true,
        })).ReadJsonAsync();
        Assert.Equal("Suspended", suspended.GetProperty("profile").GetProperty("status").GetString());
        Assert.Equal("admin.user_suspended", suspended.GetProperty("statusHistory")[0].GetProperty("action").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me/profile")).StatusCode);
        await (await client.PostAsync("/api/v1/auth/refresh", null)).ShouldFailAsync(401, "auth.session_expired");
        await (await api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = participant.Email, password = participant.Password }))
            .ShouldFailAsync(403, "account.suspended");

        await (await admin.PostAsJsonAsync($"/api/v1/admin/users/{participant.Id}/suspend", new { reason = "again", confirm = true }))
            .ShouldFailAsync(409, "admin.already_suspended");
        await (await admin.PostAsJsonAsync($"/api/v1/admin/users/{adminUser.Id}/suspend", new { reason = "self", confirm = true }))
            .ShouldFailAsync(403, "admin.cannot_suspend_self");

        var notification = await api.WithDbAsync(db => db.Set<Notification>().AsNoTracking()
            .FirstAsync(n => n.UserId == participant.Id && n.Type == NotificationTypes.AccountStatusChanged));
        Assert.True(await api.WithDbAsync(db => db.Set<NotificationDelivery>().AnyAsync(d => d.NotificationId == notification.Id && d.Channel == NotificationChannel.Email)));
        var audit = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .FirstAsync(a => a.Action == "admin.user_suspended" && a.EntityId == participant.Id.ToString()));
        Assert.Equal(adminUser.Id, audit.ActorUserId);
        Assert.Equal("Fake engagement on submissions", audit.Reason);

        var reactivated = await (await admin.PostAsJsonAsync($"/api/v1/admin/users/{participant.Id}/reactivate", new { reason = "Appeal accepted" })).ReadJsonAsync();
        Assert.Equal("Active", reactivated.GetProperty("profile").GetProperty("status").GetString());
        Assert.Equal(2, reactivated.GetProperty("statusHistory").GetArrayLength());
        var again = await api.LoginAsync(participant);
        (await again.GetAsync("/api/v1/me/profile")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Reviewers_can_view_users_but_not_suspend_or_change_roles()
    {
        var target = await api.CreateUserAsync();
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        (await reviewer.GetAsync($"/api/v1/admin/users/{target.Id}")).EnsureSuccessStatusCode();
        await (await reviewer.PostAsJsonAsync($"/api/v1/admin/users/{target.Id}/suspend", new { reason = "nope", confirm = true })).ShouldFailAsync(403);
        await (await reviewer.PutAsJsonAsync($"/api/v1/admin/users/{target.Id}/roles", new { roles = new[] { "Admin" }, reason = "escalate", confirm = true }))
            .ShouldFailAsync(403);
        await (await reviewer.PutAsJsonAsync($"/api/v1/admin/users/{target.Id}/tier", new { tier = "Gold", reason = "nope" })).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Role_changes_apply_after_forced_reauthentication_and_admins_cannot_demote_themselves()
    {
        var target = await api.CreateUserAsync();
        var targetClient = await api.LoginAsync(target);
        await (await targetClient.GetAsync("/api/v1/review/social-accounts")).ShouldFailAsync(403);

        var (adminUser, admin) = await api.AdminAsync();
        var changed = await (await admin.PutAsJsonAsync($"/api/v1/admin/users/{target.Id}/roles", new
        {
            roles = new[] { "Participant", "Reviewer" }, reason = "Joined the review team", confirm = true,
        })).ReadJsonAsync();
        Assert.Equal(new[] { "Participant", "Reviewer" }, changed.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        // The old token (issued with Participant only) no longer works; a fresh login carries the new permissions.
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/v1/me/profile")).StatusCode);
        var relogged = await api.LoginAsync(target);
        (await relogged.GetAsync("/api/v1/review/social-accounts")).EnsureSuccessStatusCode();

        await (await admin.PutAsJsonAsync($"/api/v1/admin/users/{adminUser.Id}/roles", new
        {
            roles = new[] { "Reviewer" }, reason = "oops", confirm = true,
        })).ShouldFailAsync(403, "admin.cannot_remove_own_admin");
        await (await admin.PutAsJsonAsync($"/api/v1/admin/users/{target.Id}/roles", new
        {
            roles = new[] { "Reviewer" }, reason = "no confirm",
        })).ShouldFailAsync(400, "admin.confirmation_required");
        await (await admin.PutAsJsonAsync($"/api/v1/admin/users/{target.Id}/roles", new
        {
            roles = Array.Empty<string>(), reason = "empty", confirm = true,
        })).ShouldFailAsync(400);

        // Another admin can be demoted while at least one active admin remains.
        var (otherAdmin, _) = await api.AdminAsync();
        var demoted = await (await admin.PutAsJsonAsync($"/api/v1/admin/users/{otherAdmin.Id}/roles", new
        {
            roles = new[] { "Finance" }, reason = "Moved to finance", confirm = true,
        })).ReadJsonAsync();
        Assert.Equal(new[] { "Finance" }, demoted.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        var audit = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == "admin.user_roles_changed" && a.EntityId == target.Id.ToString()).SingleAsync());
        Assert.Contains("Reviewer", audit.AfterJson);
        Assert.Equal("Joined the review team", audit.Reason);
    }

    [Fact]
    public async Task Tier_change_list_detail_and_csv_export()
    {
        var country = "NG";
        var target = await api.CreateUserAsync(countryCode: country, email: $"tier-{Guid.NewGuid():N}@example.test");
        var account = await api.AddSocialAccountAsync(target.Id, ageDays: 400);
        await api.AddSubmissionAsync(target.Id, account.Id, SubmissionStatus.Approved);
        await api.AddSubmissionAsync(target.Id, account.Id, SubmissionStatus.Rejected);

        var (_, admin) = await api.AdminAsync();
        var tiered = await (await admin.PutAsJsonAsync($"/api/v1/admin/users/{target.Id}/tier", new { tier = "Gold", reason = "Top performer" })).ReadJsonAsync();
        Assert.Equal("Gold", tiered.GetProperty("profile").GetProperty("tier").GetString());

        var list = await (await admin.GetAsync($"/api/v1/admin/users?search={Uri.EscapeDataString(target.Email)}&country={country}&tier=Gold&role=Participant&status=Active")).ReadJsonAsync();
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(target.Id, item.GetProperty("id").GetGuid());

        var detail = await (await admin.GetAsync($"/api/v1/admin/users/{target.Id}")).ReadJsonAsync();
        Assert.Equal(2, detail.GetProperty("submissionCounts").GetProperty("total").GetInt32());
        Assert.Equal(1, detail.GetProperty("submissionCounts").GetProperty("approved").GetInt32());
        Assert.Single(detail.GetProperty("socialAccounts").EnumerateArray());
        Assert.Contains(detail.GetProperty("recentAudit").EnumerateArray(), a => a.GetProperty("action").GetString() == "admin.user_tier_changed");
        Assert.Empty(detail.GetProperty("activePayoutHolds").EnumerateArray());
        Assert.Empty(detail.GetProperty("earnings").EnumerateArray());

        var csv = await admin.GetAsync($"/api/v1/admin/users/export.csv?country={country}");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var text = await csv.Content.ReadAsStringAsync();
        Assert.Contains(target.Email, text);
        Assert.DoesNotContain("PasswordHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQAAAA", text); // ASP.NET Identity password hash prefix
    }

    [Fact]
    public async Task Staff_accounts_are_created_verified_and_set_their_own_password()
    {
        var (_, admin) = await api.AdminAsync();
        var email = $"staff-{Guid.NewGuid():N}@example.test";
        var created = await admin.PostAsJsonAsync("/api/v1/admin/users/staff", new
        {
            email, displayName = "New Reviewer", countryCode = "gb", roles = new[] { "Reviewer" },
        });
        Assert.Equal(201, (int)created.StatusCode);
        var dto = await created.ReadJsonAsync();
        Assert.True(dto.GetProperty("profile").GetProperty("emailVerified").GetBoolean());
        Assert.Equal("GB", dto.GetProperty("profile").GetProperty("countryCode").GetString());

        await (await admin.PostAsJsonAsync("/api/v1/admin/users/staff", new
        {
            email = email.ToUpperInvariant(), displayName = "Dup", countryCode = "GB", roles = new[] { "Reviewer" },
        })).ShouldFailAsync(409, "admin.email_exists");
        await (await admin.PostAsJsonAsync("/api/v1/admin/users/staff", new
        {
            email = $"p-{Guid.NewGuid():N}@example.test", displayName = "Participant", countryCode = "GB", roles = new[] { "Participant" },
        })).ShouldFailAsync(400, "admin.staff_role_required");

        var anon = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        anon.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        var mail = await (await anon.GetAsync($"/api/v1/dev/mailbox?to={Uri.EscapeDataString(email)}")).ReadJsonAsync();
        var link = mail.GetProperty("links")[0].GetString()!;
        var token = Uri.UnescapeDataString(link.Split("token=")[1]);
        (await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "Reviewer-Chosen-Secret-9" })).EnsureSuccessStatusCode();

        var login = await (await anon.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Reviewer-Chosen-Secret-9" })).ReadJsonAsync();
        var staff = api.CreateClient();
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        (await staff.GetAsync("/api/v1/review/social-accounts")).EnsureSuccessStatusCode();
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "admin.staff_created")));
    }
}
