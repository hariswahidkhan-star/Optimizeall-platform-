using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Auth;

/// <summary>
/// Regression tests for the impersonation review: permission changes mid-session, credential/identity endpoints, audit
/// of reads and of every session ending, and custom role names on the session.
/// </summary>
public sealed class ImpersonationHardeningTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Roles = "/api/v1/admin/roles";
    private const string Code = "auth.impersonation_forbidden_action";

    private static async Task<Guid> CreateRoleAsync(HttpClient admin, string name, params string[] permissions)
    {
        var response = await admin.PostAsJsonAsync(Roles, new { name = name + " " + Guid.NewGuid().ToString("N")[..8], permissions });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("role").GetProperty("id").GetGuid();
    }

    private static async Task AssignAsync(HttpClient admin, Guid roleId, Guid userId) =>
        (await admin.PutAsync($"{Roles}/{roleId}/users/{userId}", null)).EnsureSuccessStatusCode();

    private Task<ImpersonationSession> SessionAsync(Guid targetId) =>
        api.WithDbAsync(db => db.Set<ImpersonationSession>().AsNoTracking().SingleAsync(s => s.TargetUserId == targetId));

    [Fact]
    public async Task An_impersonator_who_loses_users_impersonate_through_a_custom_role_is_cut_off_at_once()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var supportLead = await api.CreateUserAsync(new[] { Role.Reviewer });
        var roleId = await CreateRoleAsync(admin, "Support leads", Permissions.UsersImpersonate, Permissions.UsersView);
        await AssignAsync(admin, roleId, supportLead.Id);
        var impersonator = await api.LoginAsync(supportLead);
        var participant = await api.CreateUserAsync();

        var token = await Impersonating.TokenAsync(impersonator, participant.Id);
        (await Impersonating.SendAsync(impersonator, HttpMethod.Get, "/api/v1/me/profile", token)).EnsureSuccessStatusCode();

        // Custom-role changes don't bump the security version: the permission itself must be re-checked.
        (await admin.DeleteAsync($"{Roles}/{roleId}/users/{supportLead.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Impersonating.SendAsync(impersonator, HttpMethod.Get, "/api/v1/me/profile", token)).StatusCode);

        // A reload does not resume the impersonation: it lands on the staff member's own session and ends the session.
        var back = await (await impersonator.PostAsync("/api/v1/auth/refresh", null)).ReadJsonAsync();
        Assert.Equal(supportLead.Id, back.GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, back.GetProperty("user").GetProperty("impersonatedBy").ValueKind);
        var session = await SessionAsync(participant.Id);
        Assert.Equal("permissions_changed", session.EndedReason);
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a =>
            a.Action == "admin.impersonation_ended" && a.EntityId == participant.Id.ToString() && a.AfterJson!.Contains("permissions_changed"))));
    }

    [Fact]
    public async Task A_target_who_becomes_an_impersonator_mid_session_can_no_longer_be_viewed_as()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var participant = await api.CreateUserAsync();
        var token = await Impersonating.TokenAsync(admin, participant.Id);
        (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/me/profile", token)).EnsureSuccessStatusCode();

        var roleId = await CreateRoleAsync(admin, "Promoted", Permissions.UsersImpersonate, Permissions.UsersView);
        await AssignAsync(admin, roleId, participant.Id);

        // The impersonator would otherwise act with the target's new users.impersonate/users.view.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/admin/users", token)).StatusCode);
        var back = await (await admin.PostAsync("/api/v1/auth/refresh", null)).ReadJsonAsync();
        Assert.NotEqual(participant.Id, back.GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal("permissions_changed", (await SessionAsync(participant.Id)).EndedReason);
    }

    [Fact]
    public async Task Credentials_identity_and_client_access_endpoints_are_refused_while_impersonating()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var participant = await api.CreateUserAsync();
        var token = await Impersonating.TokenAsync(admin, participant.Id);

        // Linking the impersonator's Google account to the target would be a permanent back door into the account.
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, "/api/v1/auth/external-logins/google/start", token,
            new { returnTo = "/app/profile" })).ShouldFailAsync(403, Code);
        await (await Impersonating.SendAsync(admin, HttpMethod.Delete, "/api/v1/auth/external-logins/google", token)).ShouldFailAsync(403, Code);
        await (await Impersonating.SendAsync(admin, HttpMethod.Put, "/api/v1/me/profile", token, new
        {
            displayName = "Changed By Staff", countryCode = "PK", languageCode = "en", timeZone = "UTC", whatsAppNumber = "+923001234567",
        })).ShouldFailAsync(403, Code);
        // Reads stay available.
        (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/me/profile", token)).EnsureSuccessStatusCode();
        (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/auth/external-logins", token)).EnsureSuccessStatusCode();

        // Viewing as a finance/billing user: invoice issuing and billing settings are money.
        var finance = await api.CreateUserAsync(new[] { Role.Finance });
        var financeToken = await Impersonating.TokenAsync(admin, finance.Id);
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/agency/billing/invoices/{Guid.NewGuid()}/issue", financeToken, new { }))
            .ShouldFailAsync(403, Code);
        await (await Impersonating.SendAsync(admin, HttpMethod.Put, "/api/v1/agency/billing/settings", financeToken, new { }))
            .ShouldFailAsync(403, Code);
        (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/agency/billing/invoices", financeToken)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Reads_and_every_session_ending_are_audited()
    {
        var (adminUser, admin) = await api.CreateClientAsync(Role.Admin);
        var first = await api.CreateUserAsync();
        var second = await api.CreateUserAsync();
        var token = await Impersonating.TokenAsync(admin, first.Id);
        (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/me/payout-profile?view=full", token)).EnsureSuccessStatusCode();

        var read = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == "impersonation.read" && a.ActorUserId == first.Id));
        Assert.Equal(adminUser.Id, read.ImpersonatorUserId);
        Assert.Equal("impersonation", read.ActorType);
        Assert.Contains("/api/v1/me/payout-profile", read.AfterJson);
        Assert.Contains("view=full", read.AfterJson);
        Assert.Contains("200", read.AfterJson);

        // Starting another session replaces the first one: that ending is on record too.
        await Impersonating.TokenAsync(admin, second.Id);
        Assert.Equal("replaced", (await SessionAsync(first.Id)).EndedReason);
        var ended = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == "admin.impersonation_ended" && a.EntityId == first.Id.ToString()));
        Assert.Contains("replaced", ended.AfterJson);
        Assert.Equal(adminUser.Id, ended.ActorUserId);
    }

    [Fact]
    public async Task The_session_names_the_custom_roles_for_header_badges()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var name = "Night shift " + Guid.NewGuid().ToString("N")[..6];
        var created = await (await admin.PostAsJsonAsync(Roles, new { name, permissions = new[] { Permissions.CrmView } })).ReadJsonAsync();
        var user = await api.CreateUserAsync();
        await AssignAsync(admin, created.GetProperty("role").GetProperty("id").GetGuid(), user.Id);

        var client = await api.LoginAsync(user);
        var me = await (await client.GetAsync("/api/v1/auth/me")).ReadJsonAsync();
        Assert.Equal(new[] { name }, me.GetProperty("customRoles").EnumerateArray().Select(r => r.GetString()).ToArray());
        var refreshed = await (await client.PostAsync("/api/v1/auth/refresh", null)).ReadJsonAsync();
        Assert.Equal(new[] { name }, refreshed.GetProperty("user").GetProperty("customRoles").EnumerateArray().Select(r => r.GetString()).ToArray());

        var (_, plain) = await api.CreateClientAsync();
        Assert.Empty((await (await plain.GetAsync("/api/v1/auth/me")).ReadJsonAsync()).GetProperty("customRoles").EnumerateArray());

        // Impersonating shows the target's custom roles.
        var started = await (await Impersonating.StartAsync(admin, user.Id)).ReadJsonAsync();
        Assert.Equal(new[] { name }, started.GetProperty("user").GetProperty("customRoles").EnumerateArray().Select(r => r.GetString()).ToArray());
    }
}
