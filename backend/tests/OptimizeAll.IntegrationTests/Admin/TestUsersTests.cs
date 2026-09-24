using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Admin;

internal static class TestUserKit
{
    public static async Task<JsonElement> CreateTestUserAsync(this HttpClient admin, object body)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/admin/test-users", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync();
    }
}

public sealed class TestUsersTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_creates_a_verified_test_participant_whose_password_is_shown_once_and_which_is_labelled_in_lists()
    {
        var (adminUser, admin) = await api.CreateClientAsync(Role.Admin);
        var created = await admin.CreateTestUserAsync(new { roles = new[] { "Participant" }, displayName = "QA Jane Doe" });
        var id = created.GetProperty("id").GetGuid();
        var email = created.GetProperty("email").GetString()!;
        var password = created.GetProperty("password").GetString()!;
        Assert.StartsWith("test+qa-jane-doe-", email);
        Assert.EndsWith("@test.optimizeall.app", email);
        Assert.True(password.Length >= 16);

        var stored = await api.WithDbAsync(db => db.Set<User>().AsNoTracking().Include(u => u.Roles).SingleAsync(u => u.Id == id));
        Assert.True(stored.IsTestAccount);
        Assert.True(stored.IsEmailVerified);
        Assert.Equal(Role.Participant, stored.Roles.Single().Role);
        Assert.DoesNotContain(password, stored.PasswordHash);

        // The password works; the session says it is a test account.
        var client = await api.LoginAsync(new TestUser(id, email, password));
        var me = await (await client.GetAsync("/api/v1/auth/me")).ReadJsonAsync();
        Assert.True(me.GetProperty("isTestAccount").GetBoolean());

        var real = await api.CreateUserAsync();
        var onlyTest = await (await admin.GetAsync("/api/v1/admin/users?isTestAccount=true&pageSize=100")).ReadJsonAsync();
        var ids = onlyTest.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(id, ids);
        Assert.DoesNotContain(real.Id, ids);
        Assert.All(onlyTest.GetProperty("items").EnumerateArray(), u => Assert.True(u.GetProperty("isTestAccount").GetBoolean()));
        var onlyReal = await (await admin.GetAsync("/api/v1/admin/users?isTestAccount=false&pageSize=100")).ReadJsonAsync();
        Assert.DoesNotContain(id, onlyReal.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("id").GetGuid()));
        var detail = await (await admin.GetAsync($"/api/v1/admin/users/{id}")).ReadJsonAsync();
        Assert.True(detail.GetProperty("profile").GetProperty("isTestAccount").GetBoolean());

        var audit = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == "admin.test_user_created" && a.EntityId == id.ToString()));
        Assert.Equal(adminUser.Id, audit.ActorUserId);
        Assert.DoesNotContain(password, audit.AfterJson!);
    }

    [Fact]
    public async Task Test_users_of_staff_and_client_roles_with_organization_membership()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var finance = await admin.CreateTestUserAsync(new { roles = new[] { "Finance" } });
        Assert.Equal("Finance", finance.GetProperty("roles")[0].GetString());

        var clientAccount = new ClientAccount { Name = "Nimbus QA", Slug = "nimbus-qa-" + Guid.NewGuid().ToString("N")[..8] };
        await api.WithDbAsync(async db => { db.Add(clientAccount); await db.SaveChangesAsync(); });
        var clientUser = await admin.CreateTestUserAsync(new
        {
            roles = new[] { "Client" }, clientAccountId = clientAccount.Id, clientMemberRole = "Approver",
        });
        var clientUserId = clientUser.GetProperty("id").GetGuid();
        var member = await api.WithDbAsync(db => db.Set<ClientMember>().AsNoTracking().SingleAsync(m => m.UserId == clientUserId));
        Assert.Equal(clientAccount.Id, member.ClientAccountId);
        Assert.Equal(ClientMemberRole.Approver, member.Role);

        await (await admin.PostAsJsonAsync("/api/v1/admin/test-users", new { roles = new[] { "Participant" }, clientAccountId = clientAccount.Id }))
            .ShouldFailAsync(400, "admin.client_role_required");
        await (await admin.PostAsJsonAsync("/api/v1/admin/test-users", new { roles = Array.Empty<string>() })).ShouldFailAsync(400);

        // Reviewers (users.view only) cannot create test users.
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        await (await reviewer.PostAsJsonAsync("/api/v1/admin/test-users", new { roles = new[] { "Participant" } })).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Test_users_can_be_deleted_but_real_users_cannot_and_the_flag_cannot_be_set_on_existing_accounts()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var created = await admin.CreateTestUserAsync(new { roles = new[] { "Participant" } });
        var id = created.GetProperty("id").GetGuid();
        var testUser = new TestUser(id, created.GetProperty("email").GetString()!, created.GetProperty("password").GetString()!);
        var testClient = await api.LoginAsync(testUser);

        var real = await api.CreateUserAsync();
        var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/test-users/{real.Id}") { Content = JsonContent.Create(new { reason = "cleanup" }) };
        await (await admin.SendAsync(delete)).ShouldFailAsync(409, "admin.not_test_account");

        delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/test-users/{id}") { Content = JsonContent.Create(new { reason = "QA run finished" }) };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(delete)).StatusCode);
        Assert.Equal(UserStatus.Deactivated, await api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == id).Select(u => u.Status).SingleAsync()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await testClient.GetAsync("/api/v1/me/profile")).StatusCode);

        // No endpoint turns a real account into a test account (or back): unknown fields are ignored.
        var realClient = await api.LoginAsync(real);
        (await realClient.PutAsJsonAsync("/api/v1/me/profile", new
        {
            displayName = "Real Person", countryCode = "PK", languageCode = "en", timeZone = "UTC", isTestAccount = true,
        })).EnsureSuccessStatusCode();
        var staff = await (await admin.PostAsJsonAsync("/api/v1/admin/users/staff", new
        {
            email = $"staff-{Guid.NewGuid():N}@example.test", displayName = "Real Staff", countryCode = "PK", roles = new[] { "Reviewer" },
            isTestAccount = true,
        })).ReadJsonAsync();
        var staffId = staff.GetProperty("profile").GetProperty("id").GetGuid();
        Assert.False(await api.WithDbAsync(db => db.Set<User>().AnyAsync(u => (u.Id == real.Id || u.Id == staffId) && u.IsTestAccount)));
        Assert.False(staff.GetProperty("profile").GetProperty("isTestAccount").GetBoolean());
    }
}
