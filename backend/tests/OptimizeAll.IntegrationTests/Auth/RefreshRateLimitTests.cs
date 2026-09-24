using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Auth;

/// <summary>
/// Many people share one IP (an office behind NAT) and every page load refreshes the session, so the refresh limit is
/// generous by default but still configurable.
/// </summary>
public sealed class RefreshRateLimitTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private async Task<List<HttpStatusCode>> RefreshManyAsync(int count, string? perMinute)
    {
        var settings = new Dictionary<string, string?> { ["RateLimiting:Enabled"] = "true" };
        if (perMinute is not null) settings["RateLimiting:RefreshPerMinute"] = perMinute;
        await using var limited = api.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(settings)));
        await limited.StartAsync();
        var client = limited.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < count; i++) statuses.Add((await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
        return statuses;
    }

    [Fact]
    public async Task A_hundred_refreshes_a_minute_from_one_address_are_not_throttled()
    {
        var statuses = await RefreshManyAsync(100, perMinute: null);
        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task The_refresh_limit_is_configurable()
    {
        var statuses = await RefreshManyAsync(8, perMinute: "5");
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }
}
