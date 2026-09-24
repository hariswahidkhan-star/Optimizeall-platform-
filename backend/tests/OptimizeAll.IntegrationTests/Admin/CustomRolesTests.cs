using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Website.Leads;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Seo;

namespace OptimizeAll.IntegrationTests.Admin;

/// <summary>Admin-managed custom roles: effective permissions, guardrails, audit and the permission directory.</summary>
public sealed class CustomRolesTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Roles = "/api/v1/admin/roles";

    private async Task<(TestUser User, HttpClient Client)> AdminAsync() => await api.CreateClientAsync(Role.Admin);

    private static async Task<JsonElement> CreateRoleAsync(HttpClient client, params string[] permissions)
    {
        var response = await client.PostAsJsonAsync(Roles, new
        {
            name = "Role " + Guid.NewGuid().ToString("N")[..8], description = "Test role", permissions,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("role");
    }

    private static async Task AssignAsync(HttpClient client, JsonElement role, Guid userId) =>
        await (await client.PutAsync($"{Roles}/{role.GetProperty("id").GetGuid()}/users/{userId}", null)).ReadJsonAsync();

    private static async Task<string[]> SessionPermissionsAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/v1/auth/me")).ReadJsonAsync()).GetProperty("permissions").EnumerateArray()
        .Select(p => p.GetString()!).ToArray();

    [Fact]
    public async Task A_custom_role_grants_exactly_its_permissions_without_re_login()
    {
        var (_, admin) = await AdminAsync();
        var user = await api.CreateUserAsync(Array.Empty<Role>());
        var client = await api.LoginAsync(user);
        await (await client.GetAsync("/api/v1/agency/crm/assignees")).ShouldFailAsync(403);
        Assert.Empty(await SessionPermissionsAsync(client));

        var role = await CreateRoleAsync(admin, Permissions.CrmView);
        await AssignAsync(admin, role, user.Id);

        // Same access token: the assignment applies to authorization on the very next request.
        (await client.GetAsync("/api/v1/agency/crm/assignees")).EnsureSuccessStatusCode();
        (await client.GetAsync("/api/v1/agency/crm/dashboard")).EnsureSuccessStatusCode();
        await (await client.PostAsJsonAsync("/api/v1/agency/crm/companies", new { name = "Nope Ltd" })).ShouldFailAsync(403); // crm.manage
        await (await client.GetAsync("/api/v1/agency/clients")).ShouldFailAsync(403);
        await (await client.GetAsync("/api/v1/admin/users")).ShouldFailAsync(403);
        await (await client.GetAsync("/api/v1/me/payout-profile")).ShouldFailAsync(403); // participant.portal
        await (await client.GetAsync(Roles)).ShouldFailAsync(403);
        Assert.Equal(new[] { Permissions.CrmView }, await SessionPermissionsAsync(client));

        // The refreshed session carries the effective permissions too.
        var refreshed = await (await client.PostAsync("/api/v1/auth/refresh", null)).ReadJsonAsync();
        Assert.Equal(new[] { Permissions.CrmView },
            refreshed.GetProperty("user").GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToArray());

        // CRM owners/assignees are holders of crm.view: the custom-role holder is one.
        var assignees = await (await client.GetAsync("/api/v1/agency/crm/assignees")).ReadJsonAsync();
        Assert.Contains(assignees.EnumerateArray(), a => a.GetProperty("id").GetGuid() == user.Id);
    }

    [Fact]
    public async Task Campaign_options_serve_every_page_with_a_campaign_picker_for_custom_role_holders()
    {
        // The ledger (ledger.view), the review queue (submissions.review) and the manager portal (campaigns.manage) all
        // filter by campaign through GET /campaigns/options: a custom role granting just that page must be able to load it.
        var (_, admin) = await AdminAsync();
        foreach (var permission in new[] { Permissions.LedgerView, Permissions.SubmissionsReview, Permissions.CampaignsManage })
        {
            var user = await api.CreateUserAsync(Array.Empty<Role>());
            var client = await api.LoginAsync(user);
            await (await client.GetAsync("/api/v1/campaigns/options")).ShouldFailAsync(403);
            await AssignAsync(admin, await CreateRoleAsync(admin, permission), user.Id);
            (await client.GetAsync("/api/v1/campaigns/options")).EnsureSuccessStatusCode();
        }

        // Pages without a campaign picker don't open it.
        var crmOnly = await api.CreateUserAsync(Array.Empty<Role>());
        var crmClient = await api.LoginAsync(crmOnly);
        await AssignAsync(admin, await CreateRoleAsync(admin, Permissions.CrmView), crmOnly.Id);
        await (await crmClient.GetAsync("/api/v1/campaigns/options")).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Effective_permissions_are_the_union_with_built_in_roles()
    {
        var (_, admin) = await AdminAsync();
        var (reviewer, client) = await api.CreateClientAsync(Role.Reviewer);
        var role = await CreateRoleAsync(admin, Permissions.CrmView, Permissions.UsersView);
        await AssignAsync(admin, role, reviewer.Id);

        (await client.GetAsync("/api/v1/agency/crm/assignees")).EnsureSuccessStatusCode(); // custom
        (await client.GetAsync("/api/v1/admin/users")).EnsureSuccessStatusCode(); // both
        (await client.GetAsync("/api/v1/review/social-accounts")).EnsureSuccessStatusCode(); // built-in (social.verify)

        var expected = RolePermissions.For(Role.Reviewer).Append(Permissions.CrmView).Distinct().OrderBy(p => p).ToArray();
        Assert.Equal(expected, (await SessionPermissionsAsync(client)).OrderBy(p => p).ToArray());

        var userRoles = await (await admin.GetAsync($"{Roles}/users/{reviewer.Id}")).ReadJsonAsync();
        Assert.Equal(expected, userRoles.GetProperty("effectivePermissions").EnumerateArray().Select(p => p.GetString()).OrderBy(p => p).ToArray());
        var detail = await (await admin.GetAsync($"/api/v1/admin/users/{reviewer.Id}")).ReadJsonAsync();
        Assert.Equal(role.GetProperty("name").GetString(), detail.GetProperty("customRoles")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Unassigning_editing_or_deleting_a_role_takes_effect_immediately()
    {
        var (_, admin) = await AdminAsync();
        var user = await api.CreateUserAsync(Array.Empty<Role>());
        var client = await api.LoginAsync(user);
        var role = await CreateRoleAsync(admin, Permissions.CrmView, Permissions.AuditView);
        var id = role.GetProperty("id").GetGuid();
        await AssignAsync(admin, role, user.Id);
        (await client.GetAsync("/api/v1/agency/crm/assignees")).EnsureSuccessStatusCode();
        (await client.GetAsync("/api/v1/admin/audit-logs")).EnsureSuccessStatusCode();

        // Edit: drop audit.view.
        var updated = await (await admin.PutAsJsonAsync($"{Roles}/{id}", new
        {
            name = role.GetProperty("name").GetString(), permissions = new[] { Permissions.CrmView },
            concurrencyStamp = role.GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        await (await client.GetAsync("/api/v1/admin/audit-logs")).ShouldFailAsync(403);
        (await client.GetAsync("/api/v1/agency/crm/assignees")).EnsureSuccessStatusCode();

        // A stale stamp is rejected.
        await (await admin.PutAsJsonAsync($"{Roles}/{id}", new
        {
            name = "Renamed", permissions = new[] { Permissions.CrmView }, concurrencyStamp = role.GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(409, "concurrency.conflict");

        // Unassign.
        (await admin.DeleteAsync($"{Roles}/{id}/users/{user.Id}")).EnsureSuccessStatusCode();
        await (await client.GetAsync("/api/v1/agency/crm/assignees")).ShouldFailAsync(403);

        // Re-assign, then delete the role (assigned: needs confirmation).
        await AssignAsync(admin, updated.GetProperty("role"), user.Id);
        (await client.GetAsync("/api/v1/agency/crm/assignees")).EnsureSuccessStatusCode();
        await (await admin.DeleteAsync($"{Roles}/{id}")).ShouldFailAsync(409, "roles.in_use");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Roles}/{id}?confirm=true")).StatusCode);
        await (await client.GetAsync("/api/v1/agency/crm/assignees")).ShouldFailAsync(403);
        await (await admin.GetAsync($"{Roles}/{id}")).ShouldFailAsync(404);
        Assert.False(await api.WithDbAsync(db => db.Set<UserCustomRole>().AnyAsync(a => a.CustomRoleId == id)));
    }

    [Fact]
    public async Task Deleting_with_reassignment_moves_the_holders_and_unassigned_roles_delete_directly()
    {
        var (_, admin) = await AdminAsync();
        var user = await api.CreateUserAsync(Array.Empty<Role>());
        var client = await api.LoginAsync(user);
        var oldRole = await CreateRoleAsync(admin, Permissions.CrmView);
        var newRole = await CreateRoleAsync(admin, Permissions.AuditView);
        await AssignAsync(admin, oldRole, user.Id);

        await (await admin.DeleteAsync($"{Roles}/{oldRole.GetProperty("id").GetGuid()}?confirm=true&reassignTo={oldRole.GetProperty("id").GetGuid()}"))
            .ShouldFailAsync(400, "roles.invalid_reassignment");
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"{Roles}/{oldRole.GetProperty("id").GetGuid()}?confirm=true&reassignTo={newRole.GetProperty("id").GetGuid()}")).StatusCode);
        await (await client.GetAsync("/api/v1/agency/crm/assignees")).ShouldFailAsync(403);
        (await client.GetAsync("/api/v1/admin/audit-logs")).EnsureSuccessStatusCode();
        var detail = await (await admin.GetAsync($"{Roles}/{newRole.GetProperty("id").GetGuid()}")).ReadJsonAsync();
        Assert.Equal(user.Id, Assert.Single(detail.GetProperty("holders").EnumerateArray()).GetProperty("userId").GetGuid());

        var unused = await CreateRoleAsync(admin, Permissions.CrmView);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Roles}/{unused.GetProperty("id").GetGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Roles_manage_is_required_and_admins_see_built_in_roles_read_only()
    {
        foreach (var r in new[] { Role.Reviewer, Role.Finance, Role.AccountManager, Role.CampaignManager, Role.Participant })
        {
            var (_, client) = await api.CreateClientAsync(r);
            await (await client.GetAsync(Roles)).ShouldFailAsync(403);
            await (await client.GetAsync($"{Roles}/catalog")).ShouldFailAsync(403);
            await (await client.PostAsJsonAsync(Roles, new { name = "Sneaky", permissions = new[] { Permissions.CrmView } })).ShouldFailAsync(403);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync(Roles)).StatusCode);

        var (_, admin) = await AdminAsync();
        Assert.Contains(Permissions.RolesManage, await SessionPermissionsAsync(admin));
        var overview = await (await admin.GetAsync(Roles)).ReadJsonAsync();
        var builtIn = overview.GetProperty("builtIn").EnumerateArray().ToList();
        Assert.Equal(Enum.GetValues<Role>().Length, builtIn.Count);
        var finance = builtIn.Single(b => b.GetProperty("name").GetString() == "Finance");
        Assert.Equal(RolePermissions.For(Role.Finance).OrderBy(p => p, StringComparer.Ordinal),
            finance.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

        var catalog = await (await admin.GetAsync($"{Roles}/catalog")).ReadJsonAsync();
        Assert.True(catalog.GetProperty("callerIsAdmin").GetBoolean());
        var entries = catalog.GetProperty("areas").EnumerateArray().SelectMany(a => a.GetProperty("permissions").EnumerateArray()).ToList();
        Assert.Equal(Permissions.All.Count, entries.Count);
        var rolesManage = entries.Single(e => e.GetProperty("key").GetString() == Permissions.RolesManage);
        Assert.True(rolesManage.GetProperty("adminOnly").GetBoolean());
        Assert.True(rolesManage.GetProperty("granted").GetBoolean());
        Assert.False(entries.Single(e => e.GetProperty("key").GetString() == Permissions.ClientPortal).GetProperty("granted").GetBoolean());
    }

    [Fact]
    public async Task Nobody_can_grant_permissions_they_do_not_hold()
    {
        var (_, admin) = await AdminAsync();
        // A delegated role manager: roles.manage (admin-only grant, so only an admin could create this) plus CRM.
        var managerRole = await CreateRoleAsync(admin, Permissions.RolesManage, Permissions.CrmView, Permissions.CrmManage, Permissions.UsersView);
        var (managerUser, manager) = await api.CreateClientAsync(Role.SalesRep);
        await AssignAsync(admin, managerRole, managerUser.Id);

        (await manager.GetAsync(Roles)).EnsureSuccessStatusCode();
        var catalog = await (await manager.GetAsync($"{Roles}/catalog")).ReadJsonAsync();
        Assert.False(catalog.GetProperty("callerIsAdmin").GetBoolean());
        var granted = catalog.GetProperty("areas").EnumerateArray().SelectMany(a => a.GetProperty("permissions").EnumerateArray())
            .Where(e => e.GetProperty("granted").GetBoolean()).Select(e => e.GetProperty("key").GetString()).ToHashSet();
        Assert.Contains(Permissions.CrmView, granted);
        Assert.DoesNotContain(Permissions.RolesManage, granted); // held, but admin-only
        Assert.DoesNotContain(Permissions.PayoutsFinalize, granted);

        await (await manager.PostAsJsonAsync(Roles, new { name = "Payout power", permissions = new[] { Permissions.CrmView, Permissions.PayoutsFinalize } }))
            .ShouldFailAsync(403, "roles.cannot_grant_unheld");
        await (await manager.PostAsJsonAsync(Roles, new { name = "Role admins", permissions = new[] { Permissions.RolesManage } }))
            .ShouldFailAsync(403, "roles.admin_only_permission");
        await (await manager.PostAsJsonAsync(Roles, new { name = "Settings", permissions = new[] { Permissions.SettingsManage } }))
            .ShouldFailAsync(403, "roles.admin_only_permission");
        var crmRole = await CreateRoleAsync(manager, Permissions.CrmView, Permissions.ProposalsManage); // SalesRep holds proposals.manage

        // Can't escalate an existing role, assign/unassign or delete a role holding permissions the actor lacks.
        var financeRole = await CreateRoleAsync(admin, Permissions.PayoutsFinalize);
        var target = await api.CreateUserAsync(Array.Empty<Role>());
        await (await manager.PutAsync($"{Roles}/{financeRole.GetProperty("id").GetGuid()}/users/{target.Id}", null)).ShouldFailAsync(403, "roles.cannot_grant_unheld");
        await (await manager.DeleteAsync($"{Roles}/{financeRole.GetProperty("id").GetGuid()}")).ShouldFailAsync(403, "roles.cannot_grant_unheld");
        await (await manager.PutAsJsonAsync($"{Roles}/{financeRole.GetProperty("id").GetGuid()}", new
        {
            name = "Finance lite", permissions = new[] { Permissions.CrmView }, concurrencyStamp = financeRole.GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(403, "roles.cannot_grant_unheld");
        await (await manager.PutAsJsonAsync($"{Roles}/{crmRole.GetProperty("id").GetGuid()}", new
        {
            name = crmRole.GetProperty("name").GetString(), permissions = new[] { Permissions.CrmView, Permissions.LedgerAdjust },
            concurrencyStamp = crmRole.GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(403, "roles.cannot_grant_unheld");
        var overview = await (await manager.GetAsync(Roles)).ReadJsonAsync();
        var listed = overview.GetProperty("custom").EnumerateArray().ToList();
        Assert.False(listed.Single(r => r.GetProperty("id").GetGuid() == financeRole.GetProperty("id").GetGuid()).GetProperty("canManage").GetBoolean());
        Assert.True(listed.Single(r => r.GetProperty("id").GetGuid() == crmRole.GetProperty("id").GetGuid()).GetProperty("canManage").GetBoolean());

        // Within their own permissions they can assign.
        await AssignAsync(manager, crmRole, target.Id);
        Assert.Contains(await api.WithDbAsync(db => db.Set<UserCustomRole>().Where(a => a.UserId == target.Id).Select(a => a.CustomRoleId).ToListAsync()),
            id => id == crmRole.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Client_portal_never_mixes_with_staff_permissions()
    {
        var (_, admin) = await AdminAsync();
        await (await admin.PostAsJsonAsync(Roles, new { name = "Hybrid", permissions = new[] { Permissions.ClientPortal, Permissions.CrmView } }))
            .ShouldFailAsync(400, "roles.client_portal_mixed");
        await (await admin.PostAsJsonAsync(Roles, new { name = "Client only", permissions = new[] { Permissions.ClientPortal } }))
            .ShouldFailAsync(403, "roles.cannot_grant_unheld"); // admins don't hold client.portal either
        await (await admin.PostAsJsonAsync(Roles, new { name = "Unknown", permissions = new[] { "everything.all" } }))
            .ShouldFailAsync(400, "roles.unknown_permission");
        await (await admin.PostAsJsonAsync(Roles, new { name = "Admin", permissions = new[] { Permissions.CrmView } }))
            .ShouldFailAsync(400, "roles.name_reserved");

        // A staff custom role can't be given to a client user (they would see every client's data).
        var clientUser = await api.CreateUserAsync(new[] { Role.Client });
        var staffRole = await CreateRoleAsync(admin, Permissions.CrmView);
        await (await admin.PutAsync($"{Roles}/{staffRole.GetProperty("id").GetGuid()}/users/{clientUser.Id}", null))
            .ShouldFailAsync(409, "roles.client_staff_conflict");
    }

    [Fact]
    public async Task Names_are_unique_case_insensitively()
    {
        var (_, admin) = await AdminAsync();
        var name = "Unique " + Guid.NewGuid().ToString("N")[..6];
        (await admin.PostAsJsonAsync(Roles, new { name, permissions = new[] { Permissions.CrmView } })).EnsureSuccessStatusCode();
        await (await admin.PostAsJsonAsync(Roles, new { name = name.ToUpperInvariant(), permissions = new[] { Permissions.CrmView } }))
            .ShouldFailAsync(409, "roles.name_taken");
    }

    [Fact]
    public async Task Every_change_is_audited_with_before_and_after()
    {
        var (adminUser, admin) = await AdminAsync();
        var user = await api.CreateUserAsync(Array.Empty<Role>());
        var role = await CreateRoleAsync(admin, Permissions.CrmView);
        var id = role.GetProperty("id").GetGuid();
        await (await admin.PutAsJsonAsync($"{Roles}/{id}", new
        {
            name = role.GetProperty("name").GetString(), permissions = new[] { Permissions.CrmView, Permissions.AuditView },
            concurrencyStamp = role.GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        await AssignAsync(admin, role, user.Id);
        (await admin.DeleteAsync($"{Roles}/{id}/users/{user.Id}")).EnsureSuccessStatusCode();
        (await admin.DeleteAsync($"{Roles}/{id}")).EnsureSuccessStatusCode();

        var logs = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => (a.EntityType == nameof(CustomRole) && a.EntityId == id.ToString()) || (a.EntityType == nameof(User) && a.EntityId == user.Id.ToString()))
            .OrderBy(a => a.Id).ToListAsync());
        Assert.Equal(new[] { "admin.custom_role_created", "admin.custom_role_updated", "admin.custom_role_assigned", "admin.custom_role_unassigned", "admin.custom_role_deleted" },
            logs.Select(l => l.Action));
        Assert.All(logs, l => Assert.Equal(adminUser.Id, l.ActorUserId));
        var update = logs[1];
        Assert.Contains(Permissions.AuditView, update.AfterJson);
        Assert.DoesNotContain(Permissions.AuditView, update.BeforeJson);
        Assert.Contains(role.GetProperty("name").GetString()!, logs[2].AfterJson);
        Assert.DoesNotContain(role.GetProperty("name").GetString()!, logs[2].BeforeJson);
        Assert.Contains(role.GetProperty("name").GetString()!, logs[4].BeforeJson);
    }

    [Fact]
    public async Task Directory_includes_custom_role_holders_for_assignees_and_notifications()
    {
        var (_, admin) = await AdminAsync();
        var supportRole = await CreateRoleAsync(admin, Permissions.SupportManage, Permissions.UsersView);
        var agent = await api.CreateUserAsync(Array.Empty<Role>());
        await AssignAsync(admin, supportRole, agent.Id);
        var agentClient = await api.LoginAsync(agent);

        // Users filtered by permission (the ticket assignee picker) include the custom-role holder.
        var holders = await (await admin.GetAsync($"/api/v1/admin/users?permission={Permissions.SupportManage}&status=Active&pageSize=200")).ReadJsonAsync();
        Assert.Contains(holders.GetProperty("items").EnumerateArray(), u => u.GetProperty("id").GetGuid() == agent.Id);
        await (await admin.GetAsync("/api/v1/admin/users?permission=nope.nothing")).ShouldFailAsync(400, "admin.invalid_permission");

        // A ticket can be assigned to them, and they can work it.
        var (_, participant) = await api.CreateClientAsync();
        var ticket = await (await participant.PostAsJsonAsync("/api/v1/me/support/tickets", new
        {
            subject = "Where is my payout?", category = "Payout", body = "My payout has not arrived yet, please check.",
        })).ReadJsonAsync();
        var ticketId = ticket.GetProperty("id").GetGuid();
        var detail = await (await agentClient.GetAsync($"/api/v1/admin/support/tickets/{ticketId}")).ReadJsonAsync();
        var assigned = await (await admin.PutAsJsonAsync($"/api/v1/admin/support/tickets/{ticketId}", new
        {
            status = "Open", priority = "Normal", assignedToUserId = agent.Id, concurrencyStamp = detail.GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        Assert.Equal(agent.Id, assigned.GetProperty("assignedTo").GetProperty("id").GetGuid());

        // Without the role they are no longer a valid assignee (nor can they open the ticket queue).
        (await admin.DeleteAsync($"{Roles}/{supportRole.GetProperty("id").GetGuid()}/users/{agent.Id}")).EnsureSuccessStatusCode();
        await (await agentClient.GetAsync($"/api/v1/admin/support/tickets/{ticketId}")).ShouldFailAsync(403);
        var second = await (await participant.PostAsJsonAsync("/api/v1/me/support/tickets", new
        {
            subject = "Another question", category = "General", body = "A second question about my account.",
        })).ReadJsonAsync();
        var secondId = second.GetProperty("id").GetGuid();
        var secondDetail = await (await admin.GetAsync($"/api/v1/admin/support/tickets/{secondId}")).ReadJsonAsync();
        await (await admin.PutAsJsonAsync($"/api/v1/admin/support/tickets/{secondId}", new
        {
            status = "Open", priority = "Normal", assignedToUserId = agent.Id, concurrencyStamp = secondDetail.GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(400, "support.invalid_assignee");

        // Notification recipients: website inquiries go to holders of site.manage / crm.manage, custom roles included.
        var siteRole = await CreateRoleAsync(admin, Permissions.SiteManage);
        var editor = await api.CreateUserAsync(Array.Empty<Role>());
        await AssignAsync(admin, siteRole, editor.Id);
        var inquiryId = Guid.NewGuid();
        using (var scope = api.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetServices<IEventHandler<WebsiteInquiryReceived>>().OfType<InquiryNotificationHandler>().Single();
            await handler.HandleAsync(new WebsiteInquiryReceived(inquiryId, "Contact", "Ada", "ada@example.test", null, null, null, "Hi",
                Array.Empty<string>(), null, null, null, null, null, DateTime.UtcNow), CancellationToken.None);
        }
        var link = WebsiteLinks.Inquiry(inquiryId);
        Assert.True(await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == editor.Id && n.LinkUrl == link)));

        // The directory service itself.
        using var s = api.Services.CreateScope();
        var directory = s.ServiceProvider.GetRequiredService<IPermissionDirectory>();
        Assert.True(await directory.UserHasPermissionAsync(editor.Id, Permissions.SiteManage));
        Assert.False(await directory.UserHasPermissionAsync(editor.Id, Permissions.CrmManage));
        Assert.False(await directory.UserHasPermissionAsync(agent.Id, Permissions.SupportManage));
        Assert.Contains(await (await directory.UsersWithAnyPermissionAsync(new[] { Permissions.CrmManage, Permissions.SiteManage })).Select(u => u.Id).ToListAsync(),
            id => id == editor.Id);
    }

    [Fact]
    public async Task Clients_view_through_a_custom_role_makes_the_user_agency_staff_in_client_scoping()
    {
        var (_, admin) = await AdminAsync();
        var account = await api.CreateClientAccountAsync("Scoped " + Guid.NewGuid().ToString("N")[..6]);
        var user = await api.CreateUserAsync(Array.Empty<Role>());
        var client = await api.LoginAsync(user);
        await (await client.GetAsync("/api/v1/agency/clients")).ShouldFailAsync(403);

        var role = await CreateRoleAsync(admin, Permissions.ClientsView);
        await AssignAsync(admin, role, user.Id);
        var list = await (await client.GetAsync($"/api/v1/agency/clients?search={Uri.EscapeDataString(account.Name)}")).ReadJsonAsync();
        Assert.Contains(list.GetProperty("items").EnumerateArray(), c => c.GetProperty("id").GetGuid() == account.Id);
    }
}
