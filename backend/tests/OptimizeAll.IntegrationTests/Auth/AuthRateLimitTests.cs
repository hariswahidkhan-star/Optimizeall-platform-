using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Auth;

/// <summary>
/// Credential endpoints are limited to 10 requests a minute per address by default: the 11th sign-in attempt answers
/// 429 with a problem body the web app shows and a Retry-After header, while session refreshes (their own policy) and
/// other requests go on.
/// </summary>
public sealed class AuthRateLimitTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task The_eleventh_credential_request_in_a_minute_is_refused_with_a_friendly_429()
    {
        await using var limited = api.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Enabled"] = "true" })));
        await limited.StartAsync();
        var client = limited.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 10; i++)
        {
            // Mixed credential endpoints share one budget.
            var response = i % 2 == 0
                ? await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@example.test", password = "Wrong-Password-1" })
                : await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "nobody@example.test" });
            statuses.Add(response.StatusCode);
        }
        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);

        var refused = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@example.test", password = "Wrong-Password-1" });
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        Assert.True(refused.Headers.RetryAfter is not null, "Retry-After header");
        var body = await refused.ReadJsonAsync();
        Assert.Equal("rate_limited", body.GetProperty("code").GetString());
        Assert.Equal("Too many requests. Please wait and try again.", body.GetProperty("title").GetString());

        // Registration is a credential endpoint too; the session refresh has its own, larger budget.
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/api/v1/auth/register", new { email = "x@example.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }
}
