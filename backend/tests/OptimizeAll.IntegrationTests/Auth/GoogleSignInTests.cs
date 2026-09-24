using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;
using Xunit;

namespace OptimizeAll.IntegrationTests.Auth;

public sealed class GoogleSignInTests(GoogleSignInFixture fx) : IClassFixture<GoogleSignInFixture>
{
    private ApiFactory Api => fx.Api;

    private sealed record Attempt(HttpClient Client, string State, string Nonce, string Challenge, JsonElement Start);

    private HttpClient NewClient(WebApplicationFactory<Program>? app = null)
    {
        var client = (app ?? fx.App).CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        return client;
    }

    private static string NewSubject() => "1" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString();
    // Gmail: Google is the authority for these addresses (see IsGoogleAuthoritative), so they can be linked by email.
    private static string NewEmail(string tag, string domain = "gmail.com") => $"{tag}-{Guid.NewGuid():N}@{domain}";

    private async Task<Attempt> StartAsync(HttpClient? client = null, string? returnTo = null, string path = "/api/v1/auth/google/start")
    {
        client ??= NewClient();
        var start = await (await client.PostAsJsonAsync(path, new { returnTo })).ReadJsonAsync();
        var url = new Uri(start.GetProperty("authorizationUrl").GetString()!);
        Assert.Equal("accounts.google.com", url.Host);
        var query = QueryHelpers.ParseQuery(url.Query);
        Assert.Equal(GoogleSignInFixture.ClientId, query["client_id"].ToString());
        Assert.Equal(FakeGoogle.ExpectedRedirectUri, query["redirect_uri"].ToString());
        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.Contains("openid", query["scope"].ToString());
        return new Attempt(client, query["state"]!, query["nonce"]!, query["code_challenge"]!, start);
    }

    private Task<HttpResponseMessage> CallbackAsync(Attempt attempt, string subject, string email, bool emailVerified = true,
        string? nonce = null, string? state = null, string? challenge = null, string? hostedDomain = null)
    {
        fx.Google.Now = Api.Clock.GetUtcNow().UtcDateTime;
        var code = fx.Google.IssueCode(challenge ?? attempt.Challenge,
            fx.Google.IdToken(subject, email, nonce ?? attempt.Nonce, emailVerified, name: "Amina Siddiqui", hostedDomain: hostedDomain));
        return attempt.Client.PostAsJsonAsync("/api/v1/auth/google/callback", new { code, state = state ?? attempt.State });
    }

    private async Task<JsonElement> SignInAsync(string subject, string email)
    {
        var attempt = await StartAsync();
        return await (await CallbackAsync(attempt, subject, email)).ReadJsonAsync();
    }

    private static object Completion(string ticket, bool acceptTerms = true) => new
    {
        ticket, acceptTerms, countryCode = "PK", languageCode = "en", timeZone = "Asia/Karachi", marketingEmailOptIn = false,
    };

    /// <summary>Creates a Google-only account through the full new-user flow; returns the signed-in client and user id.</summary>
    private async Task<(HttpClient Client, Guid UserId)> NewGoogleAccountAsync(string subject, string email)
    {
        var attempt = await StartAsync();
        var callback = await (await CallbackAsync(attempt, subject, email)).ReadJsonAsync();
        var session = await (await attempt.Client.PostAsJsonAsync("/api/v1/auth/google/complete",
            Completion(callback.GetProperty("ticket").GetString()!))).ReadJsonAsync();
        attempt.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return (attempt.Client, Guid.Parse(session.GetProperty("user").GetProperty("id").GetString()!));
    }

    private Task<List<string>> AuditActionsAsync(Guid userId) => Api.WithDbAsync(db => db.Set<AuditLog>()
        .Where(a => a.EntityId == userId.ToString() && a.Action.StartsWith("auth.")).Select(a => a.Action).ToListAsync());

    private Task<List<ExternalLogin>> LoginsAsync(Guid userId) =>
        Api.WithDbAsync(db => db.Set<ExternalLogin>().AsNoTracking().Where(l => l.UserId == userId).ToListAsync());

    // ---------- Configuration ----------

    [Fact]
    public async Task Google_is_disabled_and_hidden_when_not_configured()
    {
        var plain = NewClient(Api);
        var providers = await (await plain.GetAsync("/api/v1/auth/providers")).ReadJsonAsync();
        Assert.False(providers.GetProperty("google").GetProperty("enabled").GetBoolean());
        await (await plain.PostAsJsonAsync("/api/v1/auth/google/start", new { })).ShouldFailAsync(404, "auth.google_disabled");
        await (await plain.PostAsJsonAsync("/api/v1/auth/google/callback", new { code = "x", state = "y" })).ShouldFailAsync(404, "auth.google_disabled");
        await (await plain.PostAsJsonAsync("/api/v1/auth/google/complete", Completion("t"))).ShouldFailAsync(404, "auth.google_disabled");

        var configured = await (await NewClient().GetAsync("/api/v1/auth/providers")).ReadJsonAsync();
        Assert.True(configured.GetProperty("google").GetProperty("enabled").GetBoolean());
    }

    // ---------- New accounts ----------

    [Fact]
    public async Task New_google_user_accepts_terms_and_gets_a_verified_participant_account_without_password()
    {
        var subject = NewSubject();
        var email = NewEmail("new");
        var attempt = await StartAsync(returnTo: "/app/campaigns?tab=open");
        var callback = await (await CallbackAsync(attempt, subject, email)).ReadJsonAsync();

        Assert.Equal("needsTerms", callback.GetProperty("status").GetString());
        Assert.Equal(email, callback.GetProperty("email").GetString());
        Assert.Equal("Amina Siddiqui", callback.GetProperty("displayName").GetString());
        Assert.Equal("/app/campaigns?tab=open", callback.GetProperty("returnTo").GetString());
        Assert.Equal(JsonValueKind.Null, callback.GetProperty("auth").ValueKind);
        // Nothing is created before the terms are accepted.
        var normalizedEmail = OptimizeAll.Domain.Common.Normalization.Email(email);
        Assert.False(await Api.WithDbAsync(db => db.Set<User>().AnyAsync(u => u.NormalizedEmail == normalizedEmail)));

        var ticket = callback.GetProperty("ticket").GetString()!;
        await (await attempt.Client.PostAsJsonAsync("/api/v1/auth/google/complete", Completion(ticket, acceptTerms: false)))
            .ShouldFailAsync(400, "auth.terms_required");
        await (await attempt.Client.PostAsJsonAsync("/api/v1/auth/google/complete", Completion(ticket + "x")))
            .ShouldFailAsync(400, "auth.google_ticket_invalid");

        var session = await (await attempt.Client.PostAsJsonAsync("/api/v1/auth/google/complete", Completion(ticket))).ReadJsonAsync();
        var user = session.GetProperty("user");
        Assert.True(user.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(new[] { "Participant" }, user.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        var userId = Guid.Parse(user.GetProperty("id").GetString()!);

        // The refresh cookie was set like a normal sign-in.
        var refreshed = await (await attempt.Client.PostAsync("/api/v1/auth/refresh", null)).ReadJsonAsync();
        Assert.Equal(userId.ToString(), refreshed.GetProperty("user").GetProperty("id").GetString());

        var stored = await Api.WithDbAsync(db => db.Set<User>().Include(u => u.Roles).AsNoTracking().FirstAsync(u => u.Id == userId));
        Assert.Equal(string.Empty, stored.PasswordHash);
        Assert.Equal("PK", stored.CountryCode);
        Assert.Equal("Amina Siddiqui", stored.DisplayName);
        Assert.Equal(Role.Participant, Assert.Single(stored.Roles).Role);
        var login = Assert.Single(await LoginsAsync(userId));
        Assert.Equal(("google", subject, email), (login.Provider, login.Subject, login.Email));
        var actions = await AuditActionsAsync(userId);
        Assert.Contains("auth.registered", actions);
        Assert.Contains("auth.external_login_linked", actions);
        Assert.Contains("auth.google_sign_in", actions);

        // No password: password sign-in is impossible until one is set through "forgot password".
        await (await NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Anything-At-All-1" }))
            .ShouldFailAsync(401, "auth.invalid_credentials");

        // The ticket cannot be replayed to obtain another session.
        await (await NewClient().PostAsJsonAsync("/api/v1/auth/google/complete", Completion(ticket))).ShouldFailAsync(409, "auth.google_account_exists");

        // Next time Google signs them straight in.
        var again = await SignInAsync(subject, email);
        Assert.Equal("signedIn", again.GetProperty("status").GetString());
        Assert.Equal(userId.ToString(), again.GetProperty("auth").GetProperty("user").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Return_path_is_dropped_unless_it_is_a_same_origin_path()
    {
        var attempt = await StartAsync(returnTo: "//evil.example.com/steal");
        var callback = await (await CallbackAsync(attempt, NewSubject(), NewEmail("redirect"))).ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, callback.GetProperty("returnTo").ValueKind);
    }

    // ---------- Linking by email ----------

    [Fact]
    public async Task Existing_account_with_verified_email_is_linked_and_signed_in()
    {
        var existing = await Api.CreateUserAsync(email: NewEmail("verified"));
        var subject = NewSubject();

        var result = await SignInAsync(subject, existing.Email.ToUpperInvariant());

        Assert.Equal("signedIn", result.GetProperty("status").GetString());
        Assert.Equal(existing.Id.ToString(), result.GetProperty("auth").GetProperty("user").GetProperty("id").GetString());
        var login = Assert.Single(await LoginsAsync(existing.Id));
        Assert.Equal(subject, login.Subject);
        Assert.Contains("auth.external_login_linked", await AuditActionsAsync(existing.Id));
        // The owner gets the (editable) security notice.
        var notice = await (await NewClient().GetAsync($"/api/v1/dev/mailbox?to={Uri.EscapeDataString(existing.Email)}")).ReadJsonAsync();
        Assert.Equal("Google sign-in was connected to your Optimize All account", notice.GetProperty("subject").GetString());
        // The password keeps working.
        await Api.LoginAsync(existing);
    }

    [Fact]
    public async Task Existing_account_with_unverified_email_is_not_linked()
    {
        var existing = await Api.CreateUserAsync(email: NewEmail("unverified"), emailVerified: false);
        var attempt = await StartAsync();
        await (await CallbackAsync(attempt, NewSubject(), existing.Email)).ShouldFailAsync(409, "auth.google_link_unverified");
        Assert.Empty(await LoginsAsync(existing.Id));
    }

    [Fact]
    public async Task Staff_accounts_are_never_linked_by_email()
    {
        var staff = await Api.CreateUserAsync(roles: new[] { Role.Admin }, email: NewEmail("admin"));
        var attempt = await StartAsync();
        await (await CallbackAsync(attempt, NewSubject(), staff.Email)).ShouldFailAsync(409, "auth.google_link_requires_sign_in");
        Assert.Empty(await LoginsAsync(staff.Id));
    }

    [Fact]
    public async Task Existing_account_is_not_linked_by_email_when_google_is_not_the_authority_for_the_address()
    {
        // A consumer Google account registered with a non-Gmail address: email_verified only means the address was
        // verified once (possibly by a previous owner of the mailbox or of a reclaimed domain). No automatic link.
        var existing = await Api.CreateUserAsync(email: NewEmail("corp", "company.example"));
        await (await CallbackAsync(await StartAsync(), NewSubject(), existing.Email))
            .ShouldFailAsync(409, "auth.google_link_requires_sign_in");
        Assert.Empty(await LoginsAsync(existing.Id));

        // The same address managed by a Google Workspace organization (hd claim): Google is authoritative.
        var subject = NewSubject();
        var linked = await (await CallbackAsync(await StartAsync(), subject, existing.Email, hostedDomain: "company.example")).ReadJsonAsync();
        Assert.Equal(existing.Id.ToString(), linked.GetProperty("auth").GetProperty("user").GetProperty("id").GetString());
        Assert.Equal(subject, Assert.Single(await LoginsAsync(existing.Id)).Subject);
    }

    [Fact]
    public async Task Concurrent_link_of_the_same_google_account_by_email_signs_in_instead_of_failing()
    {
        var existing = await Api.CreateUserAsync(email: NewEmail("race-signin"));
        var subject = NewSubject();
        await using var racing = new RacingApp(fx);
        // Another tab's sign-in links the same Google account to the same user between our check and our insert.
        racing.BeforeLinkSaved = () => Api.WithDbAsync(db => AddLinkAsync(db, existing.Id, subject));

        var result = await (await CallbackAsync(await StartAsync(racing.Client()), subject, existing.Email)).ReadJsonAsync();

        Assert.Equal("signedIn", result.GetProperty("status").GetString());
        Assert.Equal(existing.Id.ToString(), result.GetProperty("auth").GetProperty("user").GetProperty("id").GetString());
        Assert.Single(await LoginsAsync(existing.Id));
    }

    [Fact]
    public async Task Concurrent_profile_link_of_the_same_google_account_gets_the_friendly_conflict()
    {
        var winner = await Api.CreateUserAsync(email: NewEmail("race-winner"));
        var loser = await Api.CreateUserAsync(email: NewEmail("race-loser"));
        var subject = NewSubject();
        await using var racing = new RacingApp(fx);
        racing.BeforeLinkSaved = () => Api.WithDbAsync(db => AddLinkAsync(db, winner.Id, subject));

        var client = racing.Client();
        var session = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = loser.Email, password = loser.Password })).ReadJsonAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        var attempt = await StartAsync(client, path: "/api/v1/auth/external-logins/google/start");

        await (await CallbackAsync(attempt, subject, NewEmail("x"))).ShouldFailAsync(409, "auth.google_already_linked");
        Assert.Empty(await LoginsAsync(loser.Id));
        Assert.Single(await LoginsAsync(winner.Id));
    }

    private static async Task AddLinkAsync(OptimizeAll.Infrastructure.Persistence.AppDbContext db, Guid userId, string subject)
    {
        db.Set<ExternalLogin>().Add(new ExternalLogin
        {
            UserId = userId, Provider = ExternalLoginProviders.Google, Subject = subject, Email = "winner@gmail.com",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The Google-enabled API with a hook that runs once right before a new Google link is saved (when its audit row is
    /// staged), to reproduce deterministically a concurrent request winning the unique index.
    /// </summary>
    private sealed class RacingApp : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> _app;
        private int _fired;

        public Func<Task>? BeforeLinkSaved { get; set; }

        public RacingApp(GoogleSignInFixture fx)
        {
            _app = fx.App.WithWebHostBuilder(b => b.ConfigureServices(services =>
                services.AddScoped<OptimizeAll.Api.Common.Audit.IAuditLogger>(sp => new Hook(
                    ActivatorUtilities.CreateInstance<OptimizeAll.Api.Common.Audit.AuditLogger>(sp), this))));
        }

        public HttpClient Client()
        {
            var client = _app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
            return client;
        }

        public ValueTask DisposeAsync() => _app.DisposeAsync();

        private sealed class Hook(OptimizeAll.Api.Common.Audit.IAuditLogger inner, RacingApp owner) : OptimizeAll.Api.Common.Audit.IAuditLogger
        {
            public void Record(string action, string entityType, object entityId, object? before = null, object? after = null, string? reason = null)
            {
                inner.Record(action, entityType, entityId, before, after, reason);
                if (action == "auth.external_login_linked" && owner.BeforeLinkSaved is { } hook && Interlocked.Exchange(ref owner._fired, 1) == 0)
                    hook().GetAwaiter().GetResult();
            }

            public void RecordSystem(string action, string entityType, object entityId, object? after = null, string? reason = null) =>
                inner.RecordSystem(action, entityType, entityId, after, reason);
        }
    }

    [Fact]
    public async Task Unverified_google_email_is_refused()
    {
        var attempt = await StartAsync();
        await (await CallbackAsync(attempt, NewSubject(), NewEmail("gunverified"), emailVerified: false))
            .ShouldFailAsync(400, "auth.google_email_unverified");
    }

    // ---------- Account status ----------

    [Fact]
    public async Task Suspended_deactivated_and_locked_users_cannot_sign_in_with_google()
    {
        var linked = await Api.CreateUserAsync(email: NewEmail("suspended"));
        var subject = NewSubject();
        await SignInAsync(subject, linked.Email); // links
        await Api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == linked.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Suspended)));
        await (await CallbackAsync(await StartAsync(), subject, linked.Email)).ShouldFailAsync(403, "account.suspended");

        // Matched by email only (not yet linked): refused as well, and nothing is linked.
        var byEmail = await Api.CreateUserAsync(email: NewEmail("deactivated"));
        await Api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == byEmail.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Deactivated)));
        await (await CallbackAsync(await StartAsync(), NewSubject(), byEmail.Email)).ShouldFailAsync(403, "account.suspended");
        Assert.Empty(await LoginsAsync(byEmail.Id));

        var locked = await Api.CreateUserAsync(email: NewEmail("locked"));
        var lockedSubject = NewSubject();
        await SignInAsync(lockedSubject, locked.Email);
        var until = Api.Clock.GetUtcNow().UtcDateTime.AddMinutes(10);
        await Api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == locked.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEndsAt, until)));
        await (await CallbackAsync(await StartAsync(), lockedSubject, locked.Email)).ShouldFailAsync(403, "auth.locked_out");
    }

    // ---------- State, nonce and PKCE ----------

    [Fact]
    public async Task Tampered_or_foreign_state_is_refused()
    {
        var attempt = await StartAsync();
        var tampered = attempt.State[..^4] + (attempt.State.EndsWith("AAAA") ? "BBBB" : "AAAA");
        await (await CallbackAsync(attempt, NewSubject(), NewEmail("tamper"), state: tampered)).ShouldFailAsync(400, "auth.google_state_invalid");

        // A valid state from another browser (no matching flow cookie) is useless (login CSRF defence).
        var victim = await StartAsync();
        var other = await StartAsync();
        await (await CallbackAsync(victim, NewSubject(), NewEmail("csrf"), state: other.State)).ShouldFailAsync(400, "auth.google_state_invalid");
        var noCookie = new Attempt(NewClient(), other.State, other.Nonce, other.Challenge, other.Start);
        await (await CallbackAsync(noCookie, NewSubject(), NewEmail("nocookie"))).ShouldFailAsync(400, "auth.google_state_invalid");
    }

    [Fact]
    public async Task State_is_single_use_and_expires()
    {
        var attempt = await StartAsync();
        (await CallbackAsync(attempt, NewSubject(), NewEmail("once"))).EnsureSuccessStatusCode();
        await (await CallbackAsync(attempt, NewSubject(), NewEmail("twice"))).ShouldFailAsync(400, "auth.google_state_invalid");

        var slow = await StartAsync();
        Api.Clock.Advance(TimeSpan.FromMinutes(11));
        await (await CallbackAsync(slow, NewSubject(), NewEmail("slow"))).ShouldFailAsync(400, "auth.google_state_invalid");
    }

    [Fact]
    public async Task Id_token_with_another_nonce_is_refused()
    {
        var attempt = await StartAsync();
        await (await CallbackAsync(attempt, NewSubject(), NewEmail("nonce"), nonce: "replayed-nonce"))
            .ShouldFailAsync(400, "auth.google_token_invalid");
    }

    [Fact]
    public async Task Code_bound_to_another_pkce_challenge_is_refused()
    {
        var attempt = await StartAsync();
        var other = await StartAsync();
        await (await CallbackAsync(attempt, NewSubject(), NewEmail("pkce"), challenge: other.Challenge))
            .ShouldFailAsync(400, "auth.google_exchange_failed");
    }

    // ---------- Profile linking and unlinking ----------

    [Fact]
    public async Task Signed_in_user_links_and_unlinks_google_from_the_profile()
    {
        var user = await Api.CreateUserAsync(email: NewEmail("profile"));
        var client = NewClient();
        var session = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password })).ReadJsonAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());

        var methods = await (await client.GetAsync("/api/v1/auth/external-logins")).ReadJsonAsync();
        Assert.True(methods.GetProperty("hasPassword").GetBoolean());
        Assert.Equal(0, methods.GetProperty("externalLogins").GetArrayLength());

        // The Google account may use a different address than the Optimize All account.
        var subject = NewSubject();
        var attempt = await StartAsync(client, returnTo: "/app/profile/security", path: "/api/v1/auth/external-logins/google/start");
        var linked = await (await CallbackAsync(attempt, subject, NewEmail("other-address"))).ReadJsonAsync();
        Assert.Equal("linked", linked.GetProperty("status").GetString());
        Assert.Equal("/app/profile/security", linked.GetProperty("returnTo").GetString());
        Assert.Equal(subject, Assert.Single(await LoginsAsync(user.Id)).Subject);

        // Google now signs in this user.
        var google = await SignInAsync(subject, "whatever@gmail.test");
        Assert.Equal(user.Id.ToString(), google.GetProperty("auth").GetProperty("user").GetProperty("id").GetString());

        // A password exists, so Google can be disconnected.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/auth/external-logins/google")).StatusCode);
        Assert.Empty(await LoginsAsync(user.Id));
        Assert.Contains("auth.external_login_unlinked", await AuditActionsAsync(user.Id));
        await (await client.DeleteAsync("/api/v1/auth/external-logins/google")).ShouldFailAsync(404, "auth.google_not_linked");
    }

    [Fact]
    public async Task Google_cannot_be_unlinked_when_it_is_the_only_sign_in_method()
    {
        var (client, userId) = await NewGoogleAccountAsync(NewSubject(), NewEmail("only"));
        var methods = await (await client.GetAsync("/api/v1/auth/external-logins")).ReadJsonAsync();
        Assert.False(methods.GetProperty("hasPassword").GetBoolean());
        Assert.Equal("google", methods.GetProperty("externalLogins")[0].GetProperty("provider").GetString());

        await (await client.DeleteAsync("/api/v1/auth/external-logins/google")).ShouldFailAsync(409, "auth.google_unlink_last_method");
        Assert.Single(await LoginsAsync(userId));
    }

    [Fact]
    public async Task Link_flow_requires_the_same_signed_in_user_and_an_unclaimed_google_account()
    {
        var owner = await Api.CreateUserAsync(email: NewEmail("owner"));
        var ownerSubject = NewSubject();
        await SignInAsync(ownerSubject, owner.Email); // owner's Google account

        var user = await Api.CreateUserAsync(email: NewEmail("linker"));
        var client = await LoginWithCookiesAsync(user);

        // Another user's Google account cannot be linked.
        var attempt = await StartAsync(client, path: "/api/v1/auth/external-logins/google/start");
        await (await CallbackAsync(attempt, ownerSubject, NewEmail("x"))).ShouldFailAsync(409, "auth.google_already_linked");

        // The callback of a link flow must come from the signed-in user who started it.
        attempt = await StartAsync(client, path: "/api/v1/auth/external-logins/google/start");
        client.DefaultRequestHeaders.Authorization = null;
        await (await CallbackAsync(attempt, NewSubject(), NewEmail("y"))).ShouldFailAsync(400, "auth.google_state_invalid");
        Assert.Empty(await LoginsAsync(user.Id));
    }

    private async Task<HttpClient> LoginWithCookiesAsync(TestUser user)
    {
        var client = NewClient();
        var session = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password })).ReadJsonAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }
}
