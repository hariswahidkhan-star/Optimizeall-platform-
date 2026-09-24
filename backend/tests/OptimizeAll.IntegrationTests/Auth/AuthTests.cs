using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;
using Xunit;

namespace OptimizeAll.IntegrationTests.Auth;

public sealed class AuthTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private HttpClient NewClient()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        return client;
    }

    private static object Registration(string email, string password = "Horizon-Tulip-42") => new
    {
        email, password, displayName = "Sara Khan", countryCode = "PK", languageCode = "en",
        timeZone = "Asia/Karachi", acceptTerms = true,
    };

    private async Task<string> LatestLinkTokenAsync(HttpClient client, string email)
    {
        var mail = await (await client.GetAsync($"/api/v1/dev/mailbox?to={Uri.EscapeDataString(email)}")).ReadJsonAsync();
        var link = mail.GetProperty("links")[0].GetString()!;
        return Uri.UnescapeDataString(link.Split("token=")[1]);
    }

    [Fact]
    public async Task Register_verify_login_refresh_and_logout_journey()
    {
        var client = NewClient();
        var email = $"journey-{Guid.NewGuid():N}@example.test";

        var register = await client.PostAsJsonAsync("/api/v1/auth/register", Registration(email));
        Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);

        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Horizon-Tulip-42" })).ReadJsonAsync();
        Assert.False(login.GetProperty("user").GetProperty("emailVerified").GetBoolean());
        Assert.Contains("participant.portal", login.GetProperty("user").GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

        var token = await LatestLinkTokenAsync(client, email);
        (await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { token })).EnsureSuccessStatusCode();
        // Tokens are single-use.
        await (await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { token })).ShouldFailAsync(400, "auth.invalid_token");

        var refreshed = await (await client.PostAsync("/api/v1/auth/refresh", null)).ReadJsonAsync();
        Assert.True(refreshed.GetProperty("user").GetProperty("emailVerified").GetBoolean());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.GetProperty("accessToken").GetString());
        var me = await (await client.GetAsync("/api/v1/auth/me")).ReadJsonAsync();
        Assert.Equal(email, me.GetProperty("email").GetString());

        (await client.PostAsync("/api/v1/auth/logout", null)).EnsureSuccessStatusCode();
        await (await client.PostAsync("/api/v1/auth/refresh", null)).ShouldFailAsync(401, "auth.session_expired");
    }

    [Fact]
    public async Task Registering_an_existing_email_is_indistinguishable_and_does_not_create_a_second_account()
    {
        var client = NewClient();
        var email = $"dupe-{Guid.NewGuid():N}@example.test";
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/auth/register", Registration(email))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/auth/register", Registration(email.ToUpperInvariant()))).StatusCode);

        var count = await api.WithDbAsync(db => db.Set<User>().CountAsync(u => u.NormalizedEmail == email.ToUpperInvariant()));
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData("short1!", "auth.weak_password")]
    [InlineData("password123", "auth.weak_password")]
    public async Task Weak_passwords_are_rejected(string password, string code)
    {
        var response = await NewClient().PostAsJsonAsync("/api/v1/auth/register",
            Registration($"weak-{Guid.NewGuid():N}@example.test", password));
        // "short1!" fails DataAnnotations (MinLength) before the policy runs.
        if (password.Length < 10) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        else await response.ShouldFailAsync(400, code);
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures()
    {
        var user = await api.CreateUserAsync();
        var client = NewClient();
        for (var i = 0; i < 5; i++)
            await (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "wrong-password-123" }))
                .ShouldFailAsync(401, "auth.invalid_credentials");

        // While locked, even the correct password gets the same generic answer (no account enumeration).
        await (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password }))
            .ShouldFailAsync(401, "auth.invalid_credentials");

        api.Clock.Advance(TimeSpan.FromMinutes(16));
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Parallel_wrong_passwords_are_all_counted_toward_lockout()
    {
        var user = await api.CreateUserAsync();
        var client = NewClient();
        var attempts = Enumerable.Range(0, 12).Select(_ =>
            client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "wrong-password-123" }));
        var responses = await Task.WhenAll(attempts);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        await (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password }))
            .ShouldFailAsync(401, "auth.invalid_credentials");
    }

    [Fact]
    public async Task A_refresh_that_loses_a_rotation_race_keeps_the_session_alive()
    {
        var user = await api.CreateUserAsync();
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password });
        var cookie = login.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        HttpRequestMessage Refresh(string c)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
            request.Headers.Add("Cookie", c);
            return request;
        }

        // Tab A rotates; tab B presents the same (now rotated) cookie a moment later, before anyone used A's
        // replacement: B gets a sibling in the same family (A's unused replacement is superseded) instead of a 401.
        var a = await client.SendAsync(Refresh(cookie));
        a.EnsureSuccessStatusCode();
        var newCookie = RefreshCookieOf(a);
        var b = await client.SendAsync(Refresh(cookie));
        b.EnsureSuccessStatusCode();
        var siblingCookie = RefreshCookieOf(b);
        Assert.NotEqual(newCookie, siblingCookie);

        // Whichever Set-Cookie the browser kept last, the session goes on.
        (await client.SendAsync(Refresh(newCookie))).EnsureSuccessStatusCode();
    }

    private HttpClient CookielessClient()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        return client;
    }

    private static HttpRequestMessage RefreshWith(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static string RefreshCookieOf(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("oa_refresh=", StringComparison.Ordinal)).Split(';')[0];

    private static async Task<string> LoginCookieAsync(HttpClient client, TestUser user)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password });
        login.EnsureSuccessStatusCode();
        return RefreshCookieOf(login);
    }

    private Task<int> LiveRefreshTokensAsync(Guid userId) =>
        api.WithDbAsync(db => db.Set<RefreshToken>().CountAsync(t => t.UserId == userId && t.RevokedAt == null));

    [Fact]
    public async Task A_reload_that_aborts_the_refresh_response_keeps_the_session()
    {
        // The browser sent the refresh, the server rotated, the page reloaded before the new cookie was stored: the next
        // load presents the old cookie again, within the grace window, and gets a new session instead of /login.
        var user = await api.CreateUserAsync();
        var client = CookielessClient();
        var cookie = await LoginCookieAsync(client, user);

        (await client.SendAsync(RefreshWith(cookie))).EnsureSuccessStatusCode(); // response lost
        api.Clock.Advance(TimeSpan.FromSeconds(10));
        (await client.SendAsync(RefreshWith(cookie))).EnsureSuccessStatusCode(); // lost again
        var again = await client.SendAsync(RefreshWith(cookie));
        again.EnsureSuccessStatusCode();
        var access = (await again.ReadJsonAsync()).GetProperty("accessToken").GetString();

        (await client.SendAsync(RefreshWith(RefreshCookieOf(again)))).EnsureSuccessStatusCode();
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        (await client.SendAsync(me)).EnsureSuccessStatusCode();
        // Only one token of the family is live at a time.
        Assert.Equal(1, await LiveRefreshTokensAsync(user.Id));
    }

    [Fact]
    public async Task Parallel_refreshes_with_one_cookie_all_keep_the_session()
    {
        // Several tabs restored at once all present the same cookie.
        var user = await api.CreateUserAsync();
        var client = CookielessClient();
        var cookie = await LoginCookieAsync(client, user);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.SendAsync(RefreshWith(cookie))));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, await LiveRefreshTokensAsync(user.Id));
    }

    [Fact]
    public async Task A_rotated_token_presented_after_its_replacement_was_used_revokes_the_family_even_within_the_grace_window()
    {
        var user = await api.CreateUserAsync();
        var client = CookielessClient();
        var first = await LoginCookieAsync(client, user);
        var r1 = await client.SendAsync(RefreshWith(first));
        r1.EnsureSuccessStatusCode();
        var r2 = await client.SendAsync(RefreshWith(RefreshCookieOf(r1))); // the replacement is used
        r2.EnsureSuccessStatusCode();
        var third = RefreshCookieOf(r2);

        api.Clock.Advance(TimeSpan.FromSeconds(5));
        await (await client.SendAsync(RefreshWith(first))).ShouldFailAsync(401, "auth.session_expired");
        await (await client.SendAsync(RefreshWith(third))).ShouldFailAsync(401, "auth.session_expired");
        Assert.Equal(0, await LiveRefreshTokensAsync(user.Id));
    }

    [Fact]
    public async Task A_lost_response_token_presented_after_the_grace_window_revokes_the_family()
    {
        var user = await api.CreateUserAsync();
        var client = CookielessClient();
        var first = await LoginCookieAsync(client, user);
        var r1 = await client.SendAsync(RefreshWith(first));
        r1.EnsureSuccessStatusCode();

        api.Clock.Advance(TimeSpan.FromMinutes(1));
        await (await client.SendAsync(RefreshWith(first))).ShouldFailAsync(401, "auth.session_expired");
        await (await client.SendAsync(RefreshWith(RefreshCookieOf(r1)))).ShouldFailAsync(401, "auth.session_expired");
    }

    [Fact]
    public async Task A_signed_out_session_is_not_revived_by_an_old_cookie_within_the_grace_window()
    {
        var user = await api.CreateUserAsync();
        var client = CookielessClient();
        var first = await LoginCookieAsync(client, user);
        var r1 = await client.SendAsync(RefreshWith(first));
        r1.EnsureSuccessStatusCode();
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("Cookie", RefreshCookieOf(r1));
        (await client.SendAsync(logout)).EnsureSuccessStatusCode();

        await (await client.SendAsync(RefreshWith(first))).ShouldFailAsync(401, "auth.session_expired");
        Assert.Equal(0, await LiveRefreshTokensAsync(user.Id));
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_session_family()
    {
        var user = await api.CreateUserAsync();
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password });
        login.EnsureSuccessStatusCode();
        var firstCookie = login.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        var refresh1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refresh1.Headers.Add("Cookie", firstCookie);
        var r1 = await client.SendAsync(refresh1);
        r1.EnsureSuccessStatusCode();
        var secondCookie = r1.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        // Replay the first (already rotated) token after the race grace window: treated as theft.
        api.Clock.Advance(TimeSpan.FromMinutes(1));
        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        replay.Headers.Add("Cookie", firstCookie);
        await (await client.SendAsync(replay)).ShouldFailAsync(401, "auth.session_expired");

        var legit = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        legit.Headers.Add("Cookie", secondCookie);
        await (await client.SendAsync(legit)).ShouldFailAsync(401, "auth.session_expired");
    }

    [Fact]
    public async Task Refresh_requires_csrf_header()
    {
        var client = api.CreateClient();
        await (await client.PostAsync("/api/v1/auth/refresh", null)).ShouldFailAsync(403, "auth.csrf");
    }

    [Fact]
    public async Task Access_tokens_expire_and_suspended_users_are_rejected_immediately()
    {
        var (user, client) = await api.CreateClientAsync();
        (await client.GetAsync("/api/v1/auth/me")).EnsureSuccessStatusCode();

        await api.WithDbAsync(async db =>
        {
            var entity = await db.Set<User>().FirstAsync(u => u.Id == user.Id);
            entity.Status = UserStatus.Suspended;
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);

        await api.WithDbAsync(async db =>
        {
            var entity = await db.Set<User>().FirstAsync(u => u.Id == user.Id);
            entity.Status = UserStatus.Active;
            await db.SaveChangesAsync();
        });
        (await client.GetAsync("/api/v1/auth/me")).EnsureSuccessStatusCode();

        api.Clock.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Password_reset_revokes_existing_sessions()
    {
        var user = await api.CreateUserAsync();
        var client = await api.LoginAsync(user);
        var anon = NewClient();

        Assert.Equal(HttpStatusCode.Accepted, (await anon.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = user.Email })).StatusCode);
        var token = await LatestLinkTokenAsync(anon, user.Email);
        (await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "Brand-New-Secret-77" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        (await anon.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "Brand-New-Secret-77" })).EnsureSuccessStatusCode();
    }

    private async Task<HttpStatusCode> MeStatusAsync(HttpClient client, string accessToken)
    {
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return (await client.SendAsync(me)).StatusCode;
    }

    [Fact]
    public async Task Signing_out_ends_the_access_tokens_of_that_session_only()
    {
        var user = await api.CreateUserAsync();
        var client = CookielessClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password });
        login.EnsureSuccessStatusCode();
        var access = (await login.ReadJsonAsync()).GetProperty("accessToken").GetString()!;
        var otherLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password });
        var otherAccess = (await otherLogin.ReadJsonAsync()).GetProperty("accessToken").GetString()!;
        // A refreshed token of the first session belongs to the same session.
        var refreshed = await client.SendAsync(RefreshWith(RefreshCookieOf(login)));
        refreshed.EnsureSuccessStatusCode();
        var refreshedAccess = (await refreshed.ReadJsonAsync()).GetProperty("accessToken").GetString()!;
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(client, access));

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("Cookie", RefreshCookieOf(refreshed));
        (await client.SendAsync(logout)).EnsureSuccessStatusCode();

        // Signed out: that session's access tokens stop working at once, not when they expire; other devices go on.
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client, access));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client, refreshedAccess));
        Assert.Equal(HttpStatusCode.OK, await MeStatusAsync(client, otherAccess));
    }

    [Fact]
    public async Task Reuse_detection_also_ends_the_access_tokens_of_the_session()
    {
        var user = await api.CreateUserAsync();
        var client = CookielessClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password });
        var first = RefreshCookieOf(login);
        var r1 = await client.SendAsync(RefreshWith(first));
        r1.EnsureSuccessStatusCode();
        var access = (await r1.ReadJsonAsync()).GetProperty("accessToken").GetString()!;

        api.Clock.Advance(TimeSpan.FromMinutes(1));
        await (await client.SendAsync(RefreshWith(first))).ShouldFailAsync(401, "auth.session_expired");
        Assert.Equal(HttpStatusCode.Unauthorized, await MeStatusAsync(client, access));
    }

    [Fact]
    public async Task A_password_reset_invalidates_every_other_outstanding_reset_link()
    {
        var user = await api.CreateUserAsync();
        var anon = NewClient();
        Assert.Equal(HttpStatusCode.Accepted, (await anon.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = user.Email })).StatusCode);
        var older = await LatestLinkTokenAsync(anon, user.Email);
        api.Clock.Advance(TimeSpan.FromMinutes(3)); // past the 2-minute resend throttle
        Assert.Equal(HttpStatusCode.Accepted, (await anon.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = user.Email })).StatusCode);
        var newer = await LatestLinkTokenAsync(anon, user.Email);
        Assert.NotEqual(older, newer);

        (await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = newer, newPassword = "Brand-New-Secret-77" })).EnsureSuccessStatusCode();
        await (await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = newer, newPassword = "Another-Secret-88" }))
            .ShouldFailAsync(400, "auth.invalid_token");
        // The older link, still within its hour, must not change the password again.
        await (await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = older, newPassword = "Attacker-Secret-99" }))
            .ShouldFailAsync(400, "auth.invalid_token");
        (await anon.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "Brand-New-Secret-77" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Changing_the_password_invalidates_outstanding_reset_links()
    {
        var user = await api.CreateUserAsync();
        var anon = NewClient();
        Assert.Equal(HttpStatusCode.Accepted, (await anon.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = user.Email })).StatusCode);
        var pending = await LatestLinkTokenAsync(anon, user.Email);

        var client = await api.LoginAsync(user);
        (await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = user.Password, newPassword = "Changed-Secret-4242" })).EnsureSuccessStatusCode();

        await (await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = pending, newPassword = "Attacker-Secret-99" }))
            .ShouldFailAsync(400, "auth.invalid_token");
        (await anon.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "Changed-Secret-4242" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Reset_links_expire_after_an_hour_and_verification_links_after_48_hours()
    {
        var anon = NewClient();
        var email = $"expiry-{Guid.NewGuid():N}@example.test";
        Assert.Equal(HttpStatusCode.Accepted, (await anon.PostAsJsonAsync("/api/v1/auth/register", Registration(email))).StatusCode);
        var verification = await LatestLinkTokenAsync(anon, email);
        Assert.Equal(HttpStatusCode.Accepted, (await anon.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email })).StatusCode);
        var reset = await LatestLinkTokenAsync(anon, email);

        api.Clock.Advance(TimeSpan.FromMinutes(61));
        await (await anon.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = reset, newPassword = "Brand-New-Secret-77" }))
            .ShouldFailAsync(400, "auth.invalid_token");
        api.Clock.Advance(TimeSpan.FromHours(47));
        await (await anon.PostAsJsonAsync("/api/v1/auth/verify-email", new { token = verification }))
            .ShouldFailAsync(400, "auth.invalid_token");
        (await anon.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Horizon-Tulip-42" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Unauthenticated_requests_to_protected_endpoints_get_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Tampered_tokens_are_rejected()
    {
        var (_, client) = await api.CreateClientAsync();
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        var parts = token.Split('.');
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(
            System.Text.Encoding.UTF8.GetString(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(parts[1])))!;
        payload["role"] = "Admin";
        var forged = parts[0] + "." + Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(JsonSerializer.Serialize(payload)) + "." + parts[2];
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }
}
