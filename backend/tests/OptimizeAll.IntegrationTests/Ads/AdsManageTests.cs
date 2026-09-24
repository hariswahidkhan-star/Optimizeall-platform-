using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.SocialMedia;

namespace OptimizeAll.IntegrationTests.Ads;

/// <summary>Edit/delete/duplicate/reopen coverage of the ads workspace.</summary>
[Collection(SocialAdsCollection.Name)]
public sealed class AdsManageTests(ApiFactory api)
{
    private static async Task<Guid> AccountAsync(HttpClient http, Guid clientId, string external) =>
        (await (await http.PostAsJsonAsync("/api/v1/agency/ads/accounts", new
        {
            clientAccountId = clientId, platform = "GoogleAds", externalAccountId = external, name = "Search " + external, currency = "USD", timeZone = "Europe/London",
        })).ReadJsonAsync()).GetProperty("id").GetGuid();

    [Fact]
    public async Task Planned_structure_can_be_edited_and_deleted_but_synced_history_is_kept()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, ads) = await api.CreateClientAsync(Role.AdsSpecialist);
        var account = await AccountAsync(ads, client.Id, "100-" + Guid.NewGuid().ToString("N")[..6]);
        var campaign = await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/accounts/{account}/campaigns", new { name = "Brand" })).ReadJsonAsync();
        var campaignId = campaign.GetProperty("id").GetGuid();
        var group = await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/campaigns/{campaignId}/ad-groups", new { name = "Exact" })).ReadJsonAsync();
        var groupId = group.GetProperty("id").GetGuid();
        var ad = await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/ad-groups/{groupId}/ads", new { name = "Ad 1" })).ReadJsonAsync();
        var adId = ad.GetProperty("id").GetGuid();

        var editedGroup = await (await ads.PutAsJsonAsync($"/api/v1/agency/ads/ad-groups/{groupId}", new
        {
            name = "Exact match", status = "Paused", budgetAmount = 25, concurrencyStamp = group.GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        Assert.Equal("Paused", editedGroup.GetProperty("status").GetString());
        await (await ads.PutAsJsonAsync($"/api/v1/agency/ads/ad-groups/{groupId}", new { name = "Stale", concurrencyStamp = group.GetProperty("concurrencyStamp").GetGuid() }))
            .ShouldFailAsync(409, "concurrency.conflict");
        await (await ads.PutAsJsonAsync($"/api/v1/agency/ads/ad-groups/{groupId}", new { name = "" })).ShouldFailAsync(400);

        var editedAd = await (await ads.PutAsJsonAsync($"/api/v1/agency/ads/ads/{adId}", new { name = "Ad one", status = "Active" })).ReadJsonAsync();
        Assert.Equal("Ad one", editedAd.GetProperty("name").GetString());
        await (await ads.PutAsJsonAsync($"/api/v1/agency/ads/ads/{adId}", new { name = "Ad one", creativeId = Guid.NewGuid() })).ShouldFailAsync(404);

        // An account with campaigns cannot be deleted; deactivate instead.
        await (await ads.DeleteAsync($"/api/v1/agency/ads/accounts/{account}")).ShouldFailAsync(409, "ads.account_has_history");

        Assert.Equal(HttpStatusCode.NoContent, (await ads.DeleteAsync($"/api/v1/agency/ads/ads/{adId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ads.DeleteAsync($"/api/v1/agency/ads/ad-groups/{groupId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ads.DeleteAsync($"/api/v1/agency/ads/campaigns/{campaignId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ads.DeleteAsync($"/api/v1/agency/ads/accounts/{account}")).StatusCode);
        await (await ads.DeleteAsync($"/api/v1/agency/ads/accounts/{account}")).ShouldFailAsync(404);

        // Synced campaigns are platform history: only their status may change.
        var other = await AccountAsync(ads, client.Id, "200-" + Guid.NewGuid().ToString("N")[..6]);
        var synced = new AdCampaign { ClientAccountId = client.Id, AdAccountId = other, Name = "Synced", Source = AdEntitySource.Synced, ExternalId = "77" };
        await api.WithDbAsync(async db => { db.Add(synced); await db.SaveChangesAsync(); });
        await (await ads.DeleteAsync($"/api/v1/agency/ads/campaigns/{synced.Id}")).ShouldFailAsync(409, "ads.campaign_has_history");

        var (_, social) = await api.CreateClientAsync(Role.SocialMediaManager);
        await (await social.DeleteAsync($"/api/v1/agency/ads/campaigns/{synced.Id}")).ShouldFailAsync(403);
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "ads.campaign.deleted" && a.EntityId == campaignId.ToString())));
    }

    [Fact]
    public async Task Plans_creatives_experiments_and_utm_links_can_be_duplicated_and_deleted()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, ads) = await api.CreateClientAsync(Role.AdsSpecialist);
        var plan = await (await ads.PostAsJsonAsync("/api/v1/agency/ads/media-plans", new
        {
            clientAccountId = client.Id, name = "March plan", month = "2026-03-01", currency = "USD", status = "Approved",
            lines = new[] { new { platform = "MetaAds", channel = "Prospecting", plannedBudget = 1000, flightStart = "2026-03-01", flightEnd = "2026-03-31", kpiName = "CPA" } },
        })).ReadJsonAsync();
        var planId = plan.GetProperty("id").GetGuid();
        var fetched = await (await ads.GetAsync($"/api/v1/agency/ads/media-plans/{planId}")).ReadJsonAsync();
        Assert.Equal("March plan", fetched.GetProperty("name").GetString());

        var copy = await (await ads.PostAsync($"/api/v1/agency/ads/media-plans/{planId}/duplicate?month=2026-04-01", null)).ReadJsonAsync();
        Assert.Equal("Draft", copy.GetProperty("status").GetString());
        Assert.Equal("2026-04-01", copy.GetProperty("month").GetString());
        Assert.Equal("2026-04-30", copy.GetProperty("lines")[0].GetProperty("flightEnd").GetString());
        await (await ads.DeleteAsync($"/api/v1/agency/ads/media-plans/{planId}")).ShouldFailAsync(409, "ads.plan_approved");
        Assert.Equal(HttpStatusCode.NoContent, (await ads.DeleteAsync($"/api/v1/agency/ads/media-plans/{copy.GetProperty("id").GetGuid()}")).StatusCode);
        await (await ads.GetAsync($"/api/v1/agency/ads/media-plans/{Guid.NewGuid()}")).ShouldFailAsync(404);

        var creative = await (await ads.PostAsJsonAsync("/api/v1/agency/ads/creatives", new
        {
            clientAccountId = client.Id, name = "Carousel", platform = "MetaAds", format = "Carousel", primaryText = "Hello",
        })).ReadJsonAsync();
        var creativeId = creative.GetProperty("id").GetGuid();
        var creativeCopy = await (await ads.PostAsync($"/api/v1/agency/ads/creatives/{creativeId}/duplicate", null)).ReadJsonAsync();
        Assert.Equal("Carousel (copy)", creativeCopy.GetProperty("name").GetString());
        Assert.Equal("Draft", creativeCopy.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await ads.DeleteAsync($"/api/v1/agency/ads/creatives/{creativeCopy.GetProperty("id").GetGuid()}")).StatusCode);

        var experiment = await (await ads.PostAsJsonAsync("/api/v1/agency/ads/experiments", new
        {
            clientAccountId = client.Id, platform = "MetaAds", name = "Test", hypothesis = "H", metric = "Ctr", status = "Running",
            variants = new object[] { new { name = "A", isControl = true }, new { name = "B" } },
        })).ReadJsonAsync();
        var experimentId = experiment.GetProperty("id").GetGuid();
        await (await ads.DeleteAsync($"/api/v1/agency/ads/experiments/{experimentId}")).ShouldFailAsync(409, "ads.experiment_running");

        var utm = await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/clients/{client.Id}/utm", new { url = "https://example.com/", campaign = "spring" })).ReadJsonAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await ads.DeleteAsync($"/api/v1/agency/ads/utm/{utm.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Empty((await (await ads.GetAsync($"/api/v1/agency/ads/clients/{client.Id}/utm")).ReadJsonAsync()).EnumerateArray());
        await (await ads.DeleteAsync($"/api/v1/agency/ads/utm/{Guid.NewGuid()}")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Resolved_alerts_can_be_reopened()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, ads) = await api.CreateClientAsync(Role.AdsSpecialist);
        var alert = new AdAlert
        {
            ClientAccountId = client.Id, Kind = AdAlertKind.OverPacing, Severity = AlertSeverity.Warning, Title = "Over pacing", Message = "Spend is ahead",
            DedupeKey = "test-" + Guid.NewGuid().ToString("N"), EvaluatedFor = new DateOnly(2026, 9, 1), CreatedAt = DateTime.UtcNow,
        };
        await api.WithDbAsync(async db => { db.Add(alert); await db.SaveChangesAsync(); });

        var resolved = await (await ads.PostAsync($"/api/v1/agency/ads/alerts/{alert.Id}/resolve", null)).ReadJsonAsync();
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
        var reopened = await (await ads.PostAsync($"/api/v1/agency/ads/alerts/{alert.Id}/reopen", null)).ReadJsonAsync();
        Assert.Equal("Open", reopened.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, reopened.GetProperty("acknowledgedAt").ValueKind);
        await (await ads.PostAsync($"/api/v1/agency/ads/alerts/{Guid.NewGuid()}/reopen", null)).ShouldFailAsync(404);
        var (_, designer) = await api.CreateClientAsync(Role.Designer);
        await (await designer.PostAsync($"/api/v1/agency/ads/alerts/{alert.Id}/reopen", null)).ShouldFailAsync(403);
    }
}
