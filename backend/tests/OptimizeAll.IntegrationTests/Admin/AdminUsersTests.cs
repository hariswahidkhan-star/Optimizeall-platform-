using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task User_detail_exposes_actor_side_audit_and_network_details_only_with_audit_view()
    {
        // The target is a staff member whose own actions (actor-side entries) are audit.view data.
        var target = await api.CreateUserAsync(new[] { Role.Participant, Role.CampaignManager });
        var otherEntityId = Guid.NewGuid().ToString();
        await api.WithDbAsync(async db =>
        {
            var now = api.Clock.GetUtcNow().UtcDateTime;
            db.Set<AuditLog>().Add(new AuditLog
            {
                CreatedAt = now, ActorUserId = target.Id, ActorType = "CampaignManager", Action = "campaign.reward_rules_changed",
                EntityType = "Campaign", EntityId = otherEntityId, BeforeJson = "{\"rate\":\"secret-before\"}",
                AfterJson = "{\"rate\":\"secret-after\"}", Reason = "actor-side secret reason", IpAddress = "198.51.100.23",
                CorrelationId = "corr-actor-side",
            });
            db.Set<AuditLog>().Add(new AuditLog
            {
                CreatedAt = now, ActorUserId = null, ActorType = "system", Action = "admin.user_tier_changed",
                EntityType = "User", EntityId = target.Id.ToString(), BeforeJson = "{\"tier\":\"Standard\"}",
                AfterJson = "{\"tier\":\"Gold\"}", Reason = "about the user", IpAddress = "203.0.113.77", CorrelationId = "corr-about-user",
            });
            await db.SaveChangesAsync();
        });

        // users.view only (Reviewer): entries about the user, no actor-side entries, no IP/correlation id.
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        var limitedResponse = await reviewer.GetAsync($"/api/v1/admin/users/{target.Id}");
        var limitedText = await limitedResponse.Content.ReadAsStringAsync();
        var limited = await limitedResponse.ReadJsonAsync();
        var limitedAudit = limited.GetProperty("recentAudit").EnumerateArray().ToList();
        Assert.Contains(limitedAudit, a => a.GetProperty("reason").GetString() == "about the user");
        Assert.DoesNotContain(limitedAudit, a => a.GetProperty("entityId").GetString() == otherEntityId);
        Assert.All(limitedAudit, a =>
        {
            Assert.True(a.GetProperty("entityType").GetString() is "User" or "SocialAccount" or "SupportTicket");
            Assert.Equal(JsonValueKind.Null, a.GetProperty("ipAddress").ValueKind);
            Assert.Equal(JsonValueKind.Null, a.GetProperty("correlationId").ValueKind);
        });
        Assert.DoesNotContain("secret-before", limitedText);
        Assert.DoesNotContain("actor-side secret reason", limitedText);
        Assert.DoesNotContain("198.51.100.23", limitedText);
        Assert.DoesNotContain("203.0.113.77", limitedText);

        // audit.view (Finance): actor-side entries and network details are included.
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        var full = await (await finance.GetAsync($"/api/v1/admin/users/{target.Id}")).ReadJsonAsync();
        var fullAudit = full.GetProperty("recentAudit").EnumerateArray().ToList();
        var actorSide = Assert.Single(fullAudit, a => a.GetProperty("entityId").GetString() == otherEntityId);
        Assert.Equal("198.51.100.23", actorSide.GetProperty("ipAddress").GetString());
        var about = Assert.Single(fullAudit, a => a.GetProperty("reason").GetString() == "about the user");
        Assert.Equal("corr-about-user", about.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task User_detail_includes_entries_about_the_users_own_social_accounts()
    {
        var (target, targetClient) = await api.CreateClientAsync();
        var handle = "own" + Guid.NewGuid().ToString("N")[..10];
        var created = await (await targetClient.PostAsJsonAsync("/api/v1/me/social-accounts", new
        {
            platform = "Instagram", handle, profileUrl = $"https://www.instagram.com/{handle}/",
            accountCreatedAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(-300), followerCount = 100,
        })).ReadJsonAsync();
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        var detail = await (await reviewer.GetAsync($"/api/v1/admin/users/{target.Id}")).ReadJsonAsync();
        Assert.Contains(detail.GetProperty("recentAudit").EnumerateArray(), a =>
            a.GetProperty("action").GetString() == "social.account_added" &&
            a.GetProperty("entityId").GetString() == created.GetProperty("id").GetGuid().ToString());
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
