using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.EmailMarketing;

/// <summary>
/// Open pixels and provider webhooks arrive in bulk from a few shared IPs, so they have their own high limits and bypass
/// the global 300/minute per-IP limiter; ordinary public endpoints stay limited.
/// </summary>
public sealed class TrackingRateLimitTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private WebApplicationFactory<Program> Limited() => api.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
        c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Enabled"] = "true" })));

    [Fact]
    public async Task Open_pixels_and_webhooks_are_not_capped_by_the_global_per_ip_limit()
    {
        using var limited = Limited();
        var client = limited.CreateClient();

        var pixels = new List<HttpStatusCode>();
        for (var i = 0; i < 320; i++) pixels.Add((await client.GetAsync($"/e/o/not-a-token-{i}.gif")).StatusCode);
        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, pixels);

        var webhooks = new List<HttpStatusCode>();
        for (var i = 0; i < 320; i++)
        {
            using var body = new StringContent("[]", Encoding.UTF8, "application/json");
            webhooks.Add((await client.PostAsync("/api/v1/public/email/webhooks/sendgrid/agency", body)).StatusCode);
        }
        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, webhooks);
    }

    [Fact]
    public async Task Other_public_email_endpoints_keep_the_public_limit()
    {
        using var limited = Limited();
        var client = limited.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 125; i++) statuses.Add((await client.GetAsync($"/api/v1/public/email/preferences/not-a-token-{i}")).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
