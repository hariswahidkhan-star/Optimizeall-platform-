using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.SocialMedia;

namespace OptimizeAll.IntegrationTests.Ads;

/// <summary>Imports of the same ad account are idempotent even when they run at the same time (double submit, sync).</summary>
[Collection(SocialAdsCollection.Name)]
public sealed class AdsImportConcurrencyTests(ApiFactory api)
{
    private static string Csv(DateOnly day)
    {
        var sb = new System.Text.StringBuilder("Day,Campaign,Campaign ID,Currency code,Cost,Impr.,Clicks,Conversions,Conv. value\r\n");
        for (var d = 0; d < 60; d++)
            foreach (var (name, id) in new[] { ("Brand", "701"), ("Generic", "702"), ("Competitors", "703") })
                sb.Append($"{day.AddDays(d):yyyy-MM-dd},{name},{id},USD,100.00,1000,50,2,200.00\r\n");
        return sb.ToString();
    }

    [Fact]
    public async Task Concurrent_imports_of_the_same_file_all_succeed_without_duplicates()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, ads) = await api.CreateClientAsync(Role.AdsSpecialist);
        var account = (await (await ads.PostAsJsonAsync("/api/v1/agency/ads/accounts", new
        {
            clientAccountId = client.Id, platform = "GoogleAds", externalAccountId = "555-" + Guid.NewGuid().ToString("N")[..6], name = "Google",
            currency = "USD", timeZone = "UTC",
        })).ReadJsonAsync()).GetProperty("id").GetGuid();

        var csv = Csv(new DateOnly(2025, 1, 1));
        var preview = await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/accounts/{account}/import/preview",
            new { template = "google-ads", fileName = "r.csv", csv })).ReadJsonAsync();
        var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(preview.GetProperty("mapping").GetRawText())!;
        var body = new { template = "google-ads", fileName = "r.csv", csv, mapping };

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => ads.PostAsJsonAsync($"/api/v1/agency/ads/accounts/{account}/import", body)));
        foreach (var r in responses) r.EnsureSuccessStatusCode();

        var results = await Task.WhenAll(responses.Select(r => r.ReadJsonAsync()));
        Assert.Equal(180, results.Sum(r => r.GetProperty("rowsImported").GetInt32()));
        await api.WithDbAsync(async db =>
        {
            Assert.Equal(3, await db.Set<AdCampaign>().CountAsync(c => c.AdAccountId == account));
            Assert.Equal(180, await db.Set<AdDailyMetric>().CountAsync(m => m.AdAccountId == account));
        });
    }
}
