using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Admin;

/// <summary>
/// Built-in role grants follow the custom-role guardrails: a permission held through a custom role (roles.assign,
/// users.manage, users.suspend) can't be turned into Admin or into permissions the actor lacks, and client.portal never
/// mixes with staff permissions, whichever way the roles arrive.
/// </summary>
public sealed class RoleEscalationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Roles = "/api/v1/admin/roles";

    private static async Task<Guid> CreateRoleAsync(HttpClient admin, params string[] permissions)
    {
        var response = await admin.PostAsJsonAsync(Roles, new { name = "Role " + Guid.NewGuid().ToString("N")[..8], permissions });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("role").GetProperty("id").GetGuid();
    }

    private static async Task AssignAsync(HttpClient admin, Guid roleId, Guid userId) =>
        (await admin.PutAsync($"{Roles}/{roleId}/users/{userId}", null)).EnsureSuccessStatusCode();

    private static object SetRoles(params string[] roles) => new { roles, reason = "Team change", confirm = true };

    private Task<List<Role>> RolesOfAsync(Guid userId) =>
        api.WithDbAsync(db => db.Set<UserRole>().AsNoTracking().Where(r => r.UserId == userId).Select(r => r.Role).ToListAsync());

    [Fact]
    public async Task Roles_assign_held_through_a_custom_role_cannot_mint_admins_or_grant_unheld_permissions()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var delegatedUser = await api.CreateUserAsync(new[] { Role.SalesRep });
        await AssignAsync(admin, await CreateRoleAsync(admin, Permissions.RolesAssign, Permissions.UsersView, Permissions.UsersManage), delegatedUser.Id);
        var delegated = await api.LoginAsync(delegatedUser);
        var target = await api.CreateUserAsync();

        await (await delegated.PutAsJsonAsync($"/api/v1/admin/users/{target.Id}/roles", SetRoles("Participant", "Admin")))
            .ShouldFailAsync(403, "roles.admin_only_permission");
        await (await delegated.PutAsJsonAsync($"/api/v1/admin/users/{delegatedUser.Id}/roles", SetRoles("SalesRep", "Admin")))
            .ShouldFailAsync(403, "roles.admin_only_permission");
        await (await delegated.PutAsJsonAsync($"/api/v1/admin/users/{target.Id}/roles", SetRoles("Participant", "Finance")))
            .ShouldFailAsync(403, "roles.cannot_grant_unheld");
        Assert.Equal(new[] { Role.Participant }, await RolesOfAsync(target.Id));
        Assert.Equal(new[] { Role.SalesRep }, await RolesOfAsync(delegatedUser.Id));

        // Within the actor's own permissions the grant works.
        (await delegated.PutAsJsonAsync($"/api/v1/admin/users/{target.Id}/roles", SetRoles("Participant", "SalesRep"))).EnsureSuccessStatusCode();
        Assert.Contains(Role.SalesRep, await RolesOfAsync(target.Id));
        // Removing a role follows the same rule: the delegated user can't demote an admin.
        var otherAdmin = await api.CreateUserAsync(new[] { Role.Admin });
        await (await delegated.PutAsJsonAsync($"/api/v1/admin/users/{otherAdmin.Id}/roles", SetRoles("Participant")))
            .ShouldFailAsync(403, "roles.admin_only_permission");

        // Staff accounts and test users (whose password the creator receives) are grants too.
        await (await delegated.PostAsJsonAsync("/api/v1/admin/users/staff", new
        {
            email = $"new-admin-{Guid.NewGuid():N}@example.test", displayName = "New Admin", countryCode = "GB", roles = new[] { "Admin" },
        })).ShouldFailAsync(403, "roles.admin_only_permission");
        await (await delegated.PostAsJsonAsync("/api/v1/admin/test-users", new { roles = new[] { "Admin" } }))
            .ShouldFailAsync(403, "roles.admin_only_permission");
        await (await delegated.PostAsJsonAsync("/api/v1/admin/test-users", new { roles = new[] { "Finance" } }))
            .ShouldFailAsync(403, "roles.cannot_grant_unheld");
        (await delegated.PostAsJsonAsync("/api/v1/admin/test-users", new { roles = new[] { "SalesRep" } })).EnsureSuccessStatusCode();
        Assert.False(await api.WithDbAsync(db => db.Set<User>().AnyAsync(u => u.IsTestAccount && u.Roles.Any(r => r.Role == Role.Admin || r.Role == Role.Finance) &&
                                                                          u.Roles.Any(r => r.GrantedByUserId == delegatedUser.Id))));
    }

    [Fact]
    public async Task Built_in_role_changes_never_mix_client_portal_with_staff_permissions_from_custom_roles()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var user = await api.CreateUserAsync();
        await AssignAsync(admin, await CreateRoleAsync(admin, Permissions.CrmView), user.Id);

        await (await admin.PutAsJsonAsync($"/api/v1/admin/users/{user.Id}/roles", SetRoles("Participant", "Client")))
            .ShouldFailAsync(409, "roles.client_staff_conflict");
        Assert.DoesNotContain(Role.Client, await RolesOfAsync(user.Id));

        // Built-in roles alone can't mix either (client users stay tenant-scoped).
        var other = await api.CreateUserAsync();
        await (await admin.PutAsJsonAsync($"/api/v1/admin/users/{other.Id}/roles", SetRoles("Client", "Finance")))
            .ShouldFailAsync(409, "roles.client_staff_conflict");
        await (await admin.PostAsJsonAsync("/api/v1/admin/test-users", new { roles = new[] { "Client", "Finance" } }))
            .ShouldFailAsync(409, "roles.client_staff_conflict");
        // A plain client role change still works.
        (await admin.PutAsJsonAsync($"/api/v1/admin/users/{other.Id}/roles", SetRoles("Participant", "Client"))).EnsureSuccessStatusCode();

        // Inviting the custom-role staff user into a client organization would add the Client role.
        var clientAccount = new ClientAccount { Name = "Mix Co", Slug = "mix-co-" + Guid.NewGuid().ToString("N")[..8] };
        await api.WithDbAsync(async db => { db.Add(clientAccount); await db.SaveChangesAsync(); });
        var staffEmail = await api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == user.Id).Select(u => u.Email).SingleAsync());
        await (await admin.PostAsJsonAsync($"/api/v1/agency/clients/{clientAccount.Id}/members", new
        {
            email = staffEmail, displayName = "Staff Person", role = "Viewer",
        })).ShouldFailAsync(409, "client.invite_staff_account");
        Assert.DoesNotContain(Role.Client, await RolesOfAsync(user.Id));
    }

    [Fact]
    public async Task Users_suspend_through_a_custom_role_cannot_suspend_custom_role_staff()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var moderatorUser = await api.CreateUserAsync(Array.Empty<Role>());
        await AssignAsync(admin, await CreateRoleAsync(admin, Permissions.UsersSuspend, Permissions.UsersView), moderatorUser.Id);
        var moderator = await api.LoginAsync(moderatorUser);

        var staffByCustomRole = await api.CreateUserAsync();
        await AssignAsync(admin, await CreateRoleAsync(admin, Permissions.LedgerView), staffByCustomRole.Id);
        await (await moderator.PostAsJsonAsync($"/api/v1/admin/users/{staffByCustomRole.Id}/suspend", new { reason = "Abuse report", confirm = true }))
            .ShouldFailAsync(403, "admin.staff_requires_admin");

        // Plain participants remain theirs to suspend.
        var participant = await api.CreateUserAsync();
        (await moderator.PostAsJsonAsync($"/api/v1/admin/users/{participant.Id}/suspend", new { reason = "Abuse report", confirm = true }))
            .EnsureSuccessStatusCode();
    }
}
