using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Auth;

/// <summary>Helpers for acting with an impersonation access token on the admin's own (cookie-carrying) client.</summary>
internal static class Impersonating
{
    public const string Reason = "Support ticket #4211: participant can't see their earnings";

    public static async Task<HttpResponseMessage> StartAsync(HttpClient admin, Guid targetId, string reason = Reason, bool confirm = true) =>
        await admin.PostAsJsonAsync($"/api/v1/admin/users/{targetId}/impersonate", new { reason, confirm });

    /// <summary>Starts impersonating and returns the target's access token.</summary>
    public static async Task<string> TokenAsync(HttpClient admin, Guid targetId)
    {
        var body = await (await StartAsync(admin, targetId)).ReadJsonAsync();
        return body.GetProperty("accessToken").GetString()!;
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    public static JsonElement Payload(string jwt)
    {
        var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
        return JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(Convert.FromBase64String(part)));
    }
}

public sealed class ImpersonationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private async Task<(TestUser User, HttpClient Client)> AdminAsync() => await api.CreateClientAsync(Role.Admin);

    [Fact]
    public async Task Admin_views_as_a_participant_with_an_act_claim_while_keeping_their_own_session()
    {
        var (adminUser, admin) = await AdminAsync();
        var participant = await api.CreateUserAsync();

        await (await Impersonating.StartAsync(admin, participant.Id, confirm: false)).ShouldFailAsync(400, "admin.confirmation_required");
        await (await admin.PostAsJsonAsync($"/api/v1/admin/users/{participant.Id}/impersonate", new { confirm = true })).ShouldFailAsync(400);

        var started = await (await Impersonating.StartAsync(admin, participant.Id)).ReadJsonAsync();
        var token = started.GetProperty("accessToken").GetString()!;
        var user = started.GetProperty("user");
        Assert.Equal(participant.Id, user.GetProperty("id").GetGuid());
        Assert.Equal(adminUser.Id, user.GetProperty("impersonatedBy").GetProperty("id").GetGuid());
        Assert.Contains("participant.portal", user.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        Assert.DoesNotContain("users.impersonate", user.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

        var claims = Impersonating.Payload(token);
        Assert.Equal(participant.Id.ToString(), claims.GetProperty("sub").GetString());
        Assert.Equal(adminUser.Id.ToString(), claims.GetProperty("act_sub").GetString());
        Assert.False(string.IsNullOrEmpty(claims.GetProperty("act_name").GetString()));

        // Short-lived: the token never outlives the 60-minute session.
        var expiresAt = started.GetProperty("expiresAt").GetDateTime().ToUniversalTime();
        Assert.True(expiresAt <= api.Clock.GetUtcNow().UtcDateTime.AddMinutes(60).AddSeconds(1));

        var me = await (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/auth/me", token)).ReadJsonAsync();
        Assert.Equal(participant.Id, me.GetProperty("id").GetGuid());
        Assert.Equal(adminUser.Id, me.GetProperty("impersonatedBy").GetProperty("id").GetGuid());
        (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/me/profile", token)).EnsureSuccessStatusCode();

        // The admin's own access token (and session) keep working in parallel.
        (await admin.GetAsync("/api/v1/admin/users")).EnsureSuccessStatusCode();

        // A page reload (refresh with the cookies) resumes the impersonation, not the admin session.
        var resumed = await (await admin.PostAsync("/api/v1/auth/refresh", null)).ReadJsonAsync();
        Assert.Equal(participant.Id, resumed.GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal(adminUser.Id, resumed.GetProperty("user").GetProperty("impersonatedBy").GetProperty("id").GetGuid());

        var session = await api.WithDbAsync(db => db.Set<ImpersonationSession>().AsNoTracking().SingleAsync(s => s.TargetUserId == participant.Id));
        Assert.Equal(adminUser.Id, session.ImpersonatorUserId);
        Assert.Equal(Impersonating.Reason, session.Reason);
        var auditRow = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == "admin.impersonation_started" && a.EntityId == participant.Id.ToString()));
        Assert.Equal(adminUser.Id, auditRow.ActorUserId);
        Assert.Equal(Impersonating.Reason, auditRow.Reason);
    }

    [Fact]
    public async Task Self_admins_impersonators_inactive_and_unknown_users_are_refused_and_only_holders_of_the_permission_may_start()
    {
        var (adminUser, admin) = await AdminAsync();
        var otherAdmin = await api.CreateUserAsync(new[] { Role.Admin });
        var adminAndParticipant = await api.CreateUserAsync(new[] { Role.Participant, Role.Admin });
        var suspended = await api.CreateUserAsync();
        await api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == suspended.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Suspended)));

        await (await Impersonating.StartAsync(admin, adminUser.Id)).ShouldFailAsync(403, "admin.impersonation_self");
        await (await Impersonating.StartAsync(admin, otherAdmin.Id)).ShouldFailAsync(403, "admin.impersonation_target_forbidden");
        await (await Impersonating.StartAsync(admin, adminAndParticipant.Id)).ShouldFailAsync(403, "admin.impersonation_target_forbidden");
        await (await Impersonating.StartAsync(admin, suspended.Id)).ShouldFailAsync(409, "admin.impersonation_target_inactive");
        await (await Impersonating.StartAsync(admin, Guid.NewGuid())).ShouldFailAsync(404);

        // users.view is not enough (reviewer, finance).
        var participant = await api.CreateUserAsync();
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        await (await Impersonating.StartAsync(reviewer, participant.Id)).ShouldFailAsync(403);
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        await (await Impersonating.StartAsync(finance, participant.Id)).ShouldFailAsync(403);

        Assert.False(await api.WithDbAsync(db => db.Set<ImpersonationSession>().AnyAsync(s => s.ImpersonatorUserId == adminUser.Id)));
    }

    [Fact]
    public async Task High_risk_actions_are_refused_while_impersonating_and_every_write_is_audited_with_the_impersonator()
    {
        var (adminUser, admin) = await AdminAsync();
        var participant = await api.CreateUserAsync();
        var token = await Impersonating.TokenAsync(admin, participant.Id);
        const string code = "auth.impersonation_forbidden_action";

        await (await Impersonating.SendAsync(admin, HttpMethod.Post, "/api/v1/auth/change-password", token,
            new { currentPassword = participant.Password, newPassword = "Another-Long-Password-9" })).ShouldFailAsync(403, code);
        await (await Impersonating.SendAsync(admin, HttpMethod.Put, "/api/v1/me/payout-profile", token, new
        {
            method = "BankTransfer", accountHolderName = "Mallory", destination = "PK36SCBL0000001123456702", preferredCurrency = "USD",
        })).ShouldFailAsync(403, code);
        var other = await api.CreateUserAsync();
        // Impersonating again: refused (here already by the permission, which impersonable users never hold; the
        // endpoint also carries [DeniedWhileImpersonating] and the service re-checks, see the unit tests).
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/admin/users/{other.Id}/impersonate", token,
            new { reason = Impersonating.Reason, confirm = true })).ShouldFailAsync(403);
        Assert.False(await api.WithDbAsync(db => db.Set<ImpersonationSession>().AnyAsync(s => s.TargetUserId == other.Id)));
        // Reads stay possible.
        Assert.NotEqual(HttpStatusCode.Forbidden,
            (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/me/payout-profile", token)).StatusCode);

        // The password did not change.
        await api.LoginAsync(participant);

        // An allowed write goes through and, like the refused ones, is recorded as "admin as participant".
        (await Impersonating.SendAsync(admin, HttpMethod.Post, "/api/v1/me/support/tickets", token, new
        {
            category = "General", subject = "Checking the earnings page", body = "Opened while viewing as the participant.",
        })).EnsureSuccessStatusCode();

        var rows = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == "impersonation.request" && a.ActorUserId == participant.Id).ToListAsync());
        Assert.True(rows.Count >= 4);
        Assert.All(rows, r => Assert.Equal(adminUser.Id, r.ImpersonatorUserId));
        Assert.All(rows, r => Assert.Equal("impersonation", r.ActorType));
        Assert.Contains(rows, r => r.AfterJson!.Contains("/api/v1/auth/change-password") && r.AfterJson.Contains("403"));
        Assert.Contains(rows, r => r.AfterJson!.Contains("/api/v1/me/support/tickets") && r.AfterJson.Contains("201"));

        // Business audit rows written during the session carry the impersonator too, and the audit API shows "X as Y".
        var ticketAudit = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.ActorUserId == participant.Id && a.Action != "impersonation.request").ToListAsync());
        Assert.All(ticketAudit, r => Assert.Equal(adminUser.Id, r.ImpersonatorUserId));
        var list = await (await admin.GetAsync($"/api/v1/admin/audit-logs?actorUserId={participant.Id}&action=impersonation.request")).ReadJsonAsync();
        var first = list.GetProperty("items")[0];
        Assert.Equal(adminUser.Id, first.GetProperty("impersonatorUserId").GetGuid());
        Assert.False(string.IsNullOrEmpty(first.GetProperty("impersonatorDisplayName").GetString()));
    }

    [Fact]
    public async Task Finance_approvals_payment_recording_and_payout_exports_are_refused_while_viewing_as_a_finance_user()
    {
        var (_, admin) = await AdminAsync();
        var financeUser = await api.CreateUserAsync(new[] { Role.Finance });
        var token = await Impersonating.TokenAsync(admin, financeUser.Id);
        const string code = "auth.impersonation_forbidden_action";

        (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/finance/payout-batches", token)).EnsureSuccessStatusCode();
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, "/api/v1/finance/payout-batches/prepare", token, new { }))
            .ShouldFailAsync(403, code);
        var batchId = Guid.NewGuid();
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/finance/payout-batches/{batchId}/finalize", token,
            new { confirm = true, reason = "Approve", concurrencyStamp = Guid.NewGuid() })).ShouldFailAsync(403, code);
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/finance/payout-batches/{batchId}/items/{Guid.NewGuid()}/record-payment",
            token, new { externalReference = "BANK-1" })).ShouldFailAsync(403, code);
        await (await Impersonating.SendAsync(admin, HttpMethod.Get, $"/api/v1/finance/payout-batches/{batchId}/payment-instructions.csv?confirm=true",
            token)).ShouldFailAsync(403, code);
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, "/api/v1/finance/adjustments", token,
            new { userId = financeUser.Id, amount = 5, currency = "USD", reason = "Bonus" })).ShouldFailAsync(403, code);
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/finance/pending-earnings/{Guid.NewGuid()}/approve", token, new { }))
            .ShouldFailAsync(403, code);
        await (await Impersonating.SendAsync(admin, HttpMethod.Put, "/api/v1/finance/payout-schedule", token, new { }))
            .ShouldFailAsync(403, code);
    }

    [Fact]
    public async Task Exit_ends_the_session_and_returns_the_admin_session()
    {
        var (adminUser, admin) = await AdminAsync();
        var participant = await api.CreateUserAsync();
        var token = await Impersonating.TokenAsync(admin, participant.Id);

        var back = await (await Impersonating.SendAsync(admin, HttpMethod.Post, "/api/v1/auth/impersonation/exit", token)).ReadJsonAsync();
        Assert.Equal(adminUser.Id, back.GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, back.GetProperty("user").GetProperty("impersonatedBy").ValueKind);
        var adminToken = back.GetProperty("accessToken").GetString()!;
        Assert.False(Impersonating.Payload(adminToken).TryGetProperty("act_sub", out _));

        // The impersonation token is dead at once; the admin token works; a reload is the admin again.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/auth/me", token)).StatusCode);
        (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/admin/users", adminToken)).EnsureSuccessStatusCode();
        var refreshed = await (await admin.PostAsync("/api/v1/auth/refresh", null)).ReadJsonAsync();
        Assert.Equal(adminUser.Id, refreshed.GetProperty("user").GetProperty("id").GetGuid());

        var session = await api.WithDbAsync(db => db.Set<ImpersonationSession>().AsNoTracking().SingleAsync(s => s.TargetUserId == participant.Id));
        Assert.NotNull(session.EndedAt);
        Assert.Equal("exit", session.EndedReason);
        var ended = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == "admin.impersonation_ended" && a.EntityId == participant.Id.ToString()));
        Assert.Equal(adminUser.Id, ended.ImpersonatorUserId);

        // Exiting again is harmless (idempotent) and still answers with the admin session.
        var again = await (await Impersonating.SendAsync(admin, HttpMethod.Post, "/api/v1/auth/impersonation/exit", adminToken)).ReadJsonAsync();
        Assert.Equal(adminUser.Id, again.GetProperty("user").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Admin_sign_out_ends_the_impersonation()
    {
        var (_, admin) = await AdminAsync();
        var participant = await api.CreateUserAsync();
        var token = await Impersonating.TokenAsync(admin, participant.Id);
        (await admin.PostAsync("/api/v1/auth/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/me/profile", token)).StatusCode);
        await (await admin.PostAsync("/api/v1/auth/refresh", null)).ShouldFailAsync(401);
    }
}

/// <summary>Moves the clock, so it owns its database.</summary>
public sealed class ImpersonationExpiryTests : IAsyncLifetime
{
    private readonly ApiFactory api = new();

    public Task InitializeAsync() => api.InitializeAsync();

    public Task DisposeAsync() => api.DisposeAsync();

    [Fact]
    public async Task The_session_expires_after_an_hour_without_extension_and_refresh_falls_back_to_the_admin()
    {
        var (adminUser, admin) = await api.CreateClientAsync(Role.Admin);
        var participant = await api.CreateUserAsync();
        var token = await Impersonating.TokenAsync(admin, participant.Id);

        // Refreshing half-way does not extend the session.
        api.Clock.Advance(TimeSpan.FromMinutes(40));
        var resumed = await (await admin.PostAsync("/api/v1/auth/refresh", null)).ReadJsonAsync();
        Assert.Equal(participant.Id, resumed.GetProperty("user").GetProperty("id").GetGuid());
        var sessionEnd = resumed.GetProperty("user").GetProperty("impersonatedBy").GetProperty("expiresAt").GetDateTime().ToUniversalTime();
        Assert.True(resumed.GetProperty("expiresAt").GetDateTime().ToUniversalTime() <= sessionEnd);
        var laterToken = resumed.GetProperty("accessToken").GetString()!;

        api.Clock.Advance(TimeSpan.FromMinutes(21));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/me/profile", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/me/profile", laterToken)).StatusCode);

        var back = await (await admin.PostAsync("/api/v1/auth/refresh", null)).ReadJsonAsync();
        Assert.Equal(adminUser.Id, back.GetProperty("user").GetProperty("id").GetGuid());
        var session = await api.WithDbAsync(db => db.Set<ImpersonationSession>().AsNoTracking().SingleAsync());
        Assert.Equal("expired", session.EndedReason);
    }
}
