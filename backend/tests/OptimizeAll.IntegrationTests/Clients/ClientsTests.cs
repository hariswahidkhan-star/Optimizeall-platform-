using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Projects;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Clients;

public sealed class ClientsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Client_crud_status_lifecycle_onboarding_and_brand_kit()
    {
        var am = await api.StaffAsync();
        var created = await (await am.Client.PostAsJsonAsync("/api/v1/agency/clients", new
        {
            name = "Blue Harbor Dental", countryCode = "us", timeZone = "America/Chicago", currency = "usd", industry = "Healthcare",
            website = "https://blueharbor.example", accountManagerUserId = am.User.Id,
        })).ReadJsonAsync();
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("blue-harbor-dental", created.GetProperty("slug").GetString());
        Assert.Equal("USD", created.GetProperty("currency").GetString());
        Assert.Equal("Onboarding", created.GetProperty("status").GetString());

        // Invalid currency / website are rejected server-side.
        await (await am.Client.PostAsJsonAsync("/api/v1/agency/clients", new { name = "X Co", countryCode = "US", timeZone = "UTC", currency = "XYZ" }))
            .ShouldFailAsync(400, "client.invalid_currency");
        await (await am.Client.PostAsJsonAsync("/api/v1/agency/clients", new { name = "Y Co", countryCode = "US", timeZone = "UTC", currency = "USD", website = "javascript:alert(1)" }))
            .ShouldFailAsync(400, "client.invalid_website");

        // Churning needs a reason; a stale stamp is a 409; the change is audited.
        var stamp = created.GetProperty("concurrencyStamp").GetGuid();
        await (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{id}/status", new { status = "Churned", concurrencyStamp = stamp }))
            .ShouldFailAsync(400, "client.reason_required");
        var active = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{id}/status", new { status = "Active", concurrencyStamp = stamp })).ReadJsonAsync();
        Assert.Equal("Active", active.GetProperty("status").GetString());
        await (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{id}/status", new { status = "Paused", reason = "Budget freeze", concurrencyStamp = stamp }))
            .ShouldFailAsync(409, "concurrency.conflict");
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "client.status_changed" && a.EntityId == id.ToString())));

        // Templated onboarding checklist and progress.
        var onboarding = await (await am.Client.GetAsync($"/api/v1/agency/clients/{id}/onboarding")).ReadJsonAsync();
        var items = onboarding.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("key").GetString() == "ga4-access");
        Assert.Contains(items, i => i.GetProperty("key").GetString() == "kickoff-call");
        Assert.Equal(0, onboarding.GetProperty("percentComplete").GetInt32());
        var kickoff = items.First(i => i.GetProperty("key").GetString() == "kickoff-call").GetProperty("id").GetGuid();
        var updated = await (await am.Client.PutAsJsonAsync($"/api/v1/agency/clients/{id}/onboarding/{kickoff}", new { status = "Done" })).ReadJsonAsync();
        Assert.Equal(1, updated.GetProperty("done").GetInt32());

        // Brand kit: validated, concurrency-stamped, visible to the client.
        var kit = await (await am.Client.GetAsync($"/api/v1/agency/clients/{id}/brand-kit")).ReadJsonAsync();
        await (await am.Client.PutAsJsonAsync($"/api/v1/agency/clients/{id}/brand-kit", new
        {
            colors = new[] { new { name = "Navy", hex = "blue" } }, concurrencyStamp = kit.GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(400, "brand.invalid_color");
        var saved = await (await am.Client.PutAsJsonAsync($"/api/v1/agency/clients/{id}/brand-kit", new
        {
            colors = new[] { new { name = "Harbor Navy", hex = "#1f2659" } }, fonts = new[] { "Inter" }, toneOfVoice = "Calm and caring",
            personas = new[] { new { name = "Parent Pat", description = "Books for the whole family" } }, dos = new[] { "Smile" },
            donts = new[] { "Clinical close-ups" }, keyMessages = new[] { "Gentle dentistry" }, competitors = new[] { "SmileCo" },
            concurrencyStamp = kit.GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        Assert.Equal("#1F2659", saved.GetProperty("colors")[0].GetProperty("hex").GetString());
        var owner = await api.ClientUserAsync(id, ClientMemberRole.Owner);
        var portalKit = await (await owner.Client.GetAsync($"/api/v1/client/orgs/{id}/brand-kit")).ReadJsonAsync();
        Assert.Equal("Calm and caring", portalKit.GetProperty("toneOfVoice").GetString());
        var portalOnboarding = await (await owner.Client.GetAsync($"/api/v1/client/orgs/{id}/onboarding")).ReadJsonAsync();
        Assert.Equal(1, portalOnboarding.GetProperty("done").GetInt32());

        // A client Owner ticks off a client-owned step, but not an agency step.
        var ga4 = items.First(i => i.GetProperty("key").GetString() == "ga4-access").GetProperty("id").GetGuid();
        (await owner.Client.PutAsJsonAsync($"/api/v1/client/orgs/{id}/onboarding/{ga4}", new { status = "Done" })).EnsureSuccessStatusCode();
        await (await owner.Client.PutAsJsonAsync($"/api/v1/client/orgs/{id}/onboarding/{kickoff}", new { status = "Pending" }))
            .ShouldFailAsync(403, "onboarding.agency_item");
    }

    [Fact]
    public async Task Invitation_creates_a_client_user_once_and_reinviting_an_existing_user_adds_membership()
    {
        var am = await api.StaffAsync();
        var orgA = await api.CreateOrgAsync(am.Client, "Invite Org A");
        var orgB = await api.CreateOrgAsync(am.Client, "Invite Org B");
        var email = $"new.client.{Guid.NewGuid():N}@example.test";

        var first = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{orgA}/members", new { email, displayName = "Casey Client", role = "Approver" }))
            .ReadJsonAsync();
        Assert.True(first.GetProperty("userCreated").GetBoolean());
        var user = await api.WithDbAsync(db => db.Set<User>().Include(u => u.Roles).SingleAsync(u => u.Email == email));
        Assert.Equal(new[] { Role.Client }, user.Roles.Select(r => r.Role).ToArray());
        // A set-password link (password reset flow) was issued; the password itself is unusable.
        Assert.True(await api.WithDbAsync(db => db.Set<UserToken>().AnyAsync(t => t.UserId == user.Id && t.Purpose == UserTokenPurpose.PasswordReset)));
        var login = await api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Integration-Test-Pass-1" });
        Assert.False(login.IsSuccessStatusCode);

        // Same org again: conflict, no second user or membership.
        await (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{orgA}/members", new { email, displayName = "Casey Client", role = "Approver" }))
            .ShouldFailAsync(409, "client.member_exists");
        // Another org: the existing user gains a membership.
        var second = await (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{orgB}/members", new { email = email.ToUpperInvariant(), displayName = "Casey", role = "Viewer" }))
            .ReadJsonAsync();
        Assert.False(second.GetProperty("userCreated").GetBoolean());
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<User>().CountAsync(u => u.NormalizedEmail == email.ToUpperInvariant())));
        Assert.Equal(2, await api.WithDbAsync(db => db.Set<ClientMember>().CountAsync(m => m.UserId == user.Id)));

        // Staff accounts can't be invited as client users.
        var staff = await api.StaffAsync(Role.Designer);
        await (await am.Client.PostAsJsonAsync($"/api/v1/agency/clients/{orgA}/members", new { email = staff.User.Email, displayName = "Des", role = "Viewer" }))
            .ShouldFailAsync(409, "client.invite_staff_account");
    }

    [Fact]
    public async Task Client_owners_manage_their_own_members_and_users_can_belong_to_several_orgs()
    {
        var am = await api.StaffAsync();
        var orgA = await api.CreateOrgAsync(am.Client, "Members Org A");
        var orgB = await api.CreateOrgAsync(am.Client, "Members Org B");
        var owner = await api.ClientUserAsync(orgA, ClientMemberRole.Owner);
        var viewer = await api.ClientUserAsync(orgA, ClientMemberRole.Viewer);
        await api.WithDbAsync(async db =>
        {
            db.Set<ClientMember>().Add(new ClientMember { ClientAccountId = orgB, UserId = owner.User.Id, Role = ClientMemberRole.Viewer, AddedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        var orgs = await (await owner.Client.GetAsync("/api/v1/client/orgs")).ReadJsonAsync();
        Assert.Equal(2, orgs.GetArrayLength());
        Assert.Contains(orgs.EnumerateArray(), o => o.GetProperty("clientId").GetGuid() == orgB && o.GetProperty("role").GetString() == "Viewer");

        // Owner invites and changes duties in their org; a Viewer cannot; the Owner is only a Viewer in org B.
        var invited = await (await owner.Client.PostAsJsonAsync($"/api/v1/client/orgs/{orgA}/members", new
        {
            email = $"colleague.{Guid.NewGuid():N}@example.test", displayName = "Colleague", role = "Viewer",
        })).ReadJsonAsync();
        var colleagueId = invited.GetProperty("member").GetProperty("userId").GetGuid();
        (await owner.Client.PutAsJsonAsync($"/api/v1/client/orgs/{orgA}/members/{colleagueId}", new { role = "Approver" })).EnsureSuccessStatusCode();
        await (await viewer.Client.PostAsJsonAsync($"/api/v1/client/orgs/{orgA}/members", new { email = "x@example.test", displayName = "Xavier", role = "Viewer" }))
            .ShouldFailAsync(403, "client.insufficient_role");
        await (await owner.Client.PostAsJsonAsync($"/api/v1/client/orgs/{orgB}/members", new { email = "y@example.test", displayName = "Yolanda", role = "Viewer" }))
            .ShouldFailAsync(403, "client.insufficient_role");

        // The last Owner can't be removed or demoted.
        await (await owner.Client.PutAsJsonAsync($"/api/v1/client/orgs/{orgA}/members/{owner.User.Id}", new { role = "Viewer" }))
            .ShouldFailAsync(409, "client.last_owner");
        (await owner.Client.DeleteAsync($"/api/v1/client/orgs/{orgA}/members/{viewer.User.Id}")).EnsureSuccessStatusCode();

        // A different organization's members are invisible (404).
        var outsider = await api.ClientUserAsync(await api.CreateOrgAsync(am.Client, "Members Org C"), ClientMemberRole.Owner);
        await (await outsider.Client.GetAsync($"/api/v1/client/orgs/{orgA}/members")).ShouldFailAsync(404);
        await (await outsider.Client.GetAsync($"/api/v1/client/orgs/{orgA}/team")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Health_score_explains_its_reasons()
    {
        var am = await api.StaffAsync();
        var orgId = await api.CreateOrgAsync(am.Client, "Health Org");
        var project = await am.Client.CreateProjectAsync(orgId);
        var projectId = project.GetProperty("id").GetGuid();
        var today = DateOnly.FromDateTime(api.Clock.GetUtcNow().UtcDateTime);
        for (var i = 0; i < 5; i++)
            (await am.Client.PostAsJsonAsync($"/api/v1/agency/projects/{projectId}/tasks", new { title = $"Late task {i}", dueDate = today.AddDays(-3 - i) }))
                .EnsureSuccessStatusCode();
        var deliverableId = await am.Client.DeliverableAwaitingClientAsync(projectId);
        await api.WithDbAsync(db => db.Set<Deliverable>().Where(d => d.Id == deliverableId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.SentToClientAt, DateTime.UtcNow.AddDays(-9))));

        var health = await (await am.Client.GetAsync($"/api/v1/agency/clients/{orgId}/health")).ReadJsonAsync();
        Assert.Equal("Red", health.GetProperty("level").GetString());
        var reasons = health.GetProperty("reasons").EnumerateArray().Select(r => r.GetProperty("code").GetString()).ToList();
        Assert.Contains("overdue_tasks", reasons);
        Assert.Contains("approvals_stale", reasons);
        Assert.Contains(health.GetProperty("reasons").EnumerateArray(), r => r.GetProperty("message").GetString()!.Contains("5 overdue tasks"));
        Assert.True(health.GetProperty("score").GetInt32() < 50);

        var board = await (await am.Client.GetAsync("/api/v1/agency/clients/health")).ReadJsonAsync();
        Assert.Contains(board.EnumerateArray(), b => b.GetProperty("clientId").GetGuid() == orgId);

        // NPS detractor is a reason too.
        var owner = await api.ClientUserAsync(orgId, ClientMemberRole.Owner);
        (await owner.Client.PostAsJsonAsync($"/api/v1/client/orgs/{orgId}/feedback/nps", new { score = 3, comment = "Slow" })).EnsureSuccessStatusCode();
        await (await owner.Client.PostAsJsonAsync($"/api/v1/client/orgs/{orgId}/feedback/nps", new { score = 9 })).ShouldFailAsync(409, "feedback.already_submitted");
        health = await (await am.Client.GetAsync($"/api/v1/agency/clients/{orgId}/health")).ReadJsonAsync();
        Assert.Contains(health.GetProperty("reasons").EnumerateArray(), r => r.GetProperty("code").GetString() == "nps_detractor");
        var feedback = await (await am.Client.GetAsync($"/api/v1/agency/clients/{orgId}/feedback")).ReadJsonAsync();
        Assert.Equal(-100, feedback.GetProperty("npsScore").GetInt32());
    }

    public static IEnumerable<object[]> Matrix() => new[]
    {
        // role, method, path template ({c} = a client id), expected status
        new object[] { Role.Participant, "GET", "/api/v1/agency/clients", 403 },
        new object[] { Role.Participant, "GET", "/api/v1/client/orgs", 403 },
        new object[] { Role.Client, "GET", "/api/v1/agency/clients", 403 },
        new object[] { Role.Client, "GET", "/api/v1/agency/projects", 403 },
        new object[] { Role.Client, "GET", "/api/v1/agency/dashboard", 403 },
        new object[] { Role.Client, "GET", "/api/v1/agency/files/{c}", 403 },
        new object[] { Role.SalesRep, "GET", "/api/v1/agency/clients", 200 },
        new object[] { Role.SalesRep, "POST", "/api/v1/agency/clients", 403 },
        new object[] { Role.SalesRep, "GET", "/api/v1/agency/projects", 403 },
        new object[] { Role.Designer, "GET", "/api/v1/agency/projects", 200 },
        new object[] { Role.Designer, "POST", "/api/v1/agency/projects", 403 },
        new object[] { Role.Designer, "GET", "/api/v1/agency/reports", 403 },
        new object[] { Role.Designer, "GET", "/api/v1/agency/time/timesheets/pending", 403 },
        new object[] { Role.Designer, "GET", "/api/v1/agency/time/timer", 200 },
        new object[] { Role.Designer, "GET", "/api/v1/agency/time/utilization?from=2026-01-01&to=2026-01-07", 403 },
        new object[] { Role.Strategist, "GET", "/api/v1/agency/reports", 200 },
        new object[] { Role.Strategist, "POST", "/api/v1/agency/clients", 403 },
        new object[] { Role.AccountManager, "GET", "/api/v1/agency/time/timesheets/pending", 200 },
        new object[] { Role.AccountManager, "GET", "/api/v1/client/orgs", 403 },
        new object[] { Role.Admin, "GET", "/api/v1/agency/dashboard", 200 },
        new object[] { Role.Finance, "GET", "/api/v1/agency/clients", 403 },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Permission_matrix(Role role, string method, string path, int expected)
    {
        var (_, client) = await api.CreateClientAsync(role);
        using var request = new HttpRequestMessage(new HttpMethod(method), path.Replace("{c}", Guid.NewGuid().ToString()))
        {
            Content = method == "POST" ? JsonContent.Create(new { name = "Matrix Co", countryCode = "US", timeZone = "UTC", currency = "USD", type = "Other" }) : null,
        };
        var response = await client.SendAsync(request);
        var status = (int)response.StatusCode;
        if (expected == 200) Assert.True(status is >= 200 and < 300 or 400, $"{role} {method} {path}: {status} {await response.Content.ReadAsStringAsync()}");
        else Assert.Equal(expected, status);
    }
}
