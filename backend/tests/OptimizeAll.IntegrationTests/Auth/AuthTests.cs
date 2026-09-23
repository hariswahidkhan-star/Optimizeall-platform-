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

        await (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password }))
            .ShouldFailAsync(429, "auth.locked_out");

        api.Clock.Advance(TimeSpan.FromMinutes(16));
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password })).EnsureSuccessStatusCode();
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

        // Replay the first (already rotated) token: rejected, and the whole family is revoked.
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
