using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.SocialMedia;

/// <summary>Defects found by the social + ads E2E journey (frontend/e2e/j-social).</summary>
[Collection(SocialAdsCollection.Name)]
public sealed class SocialJourneyRegressionTests(ApiFactory api)
{
    /// <summary>
    /// Most edits change only a variant (text, media, hashtags). The post row was not saved then, so its concurrency stamp
    /// stayed the same and a second editor holding the old stamp silently overwrote the first editor's text.
    /// </summary>
    [Fact]
    public async Task A_variant_only_edit_advances_the_stamp_so_a_stale_editor_gets_409()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, first) = await api.CreateClientAsync(Role.SocialMediaManager);
        var (_, second) = await api.CreateClientAsync(Role.ContentCreator);
        var profile = await SocialAdsKit.ProfileAsync(first, client.Id, SocialNetwork.LinkedIn);
        var post = await SocialAdsKit.CreatePostAsync(first, client.Id, profile, "Original text", "Stale edit");
        var id = post.GetProperty("id").GetGuid();
        var loaded = post.GetProperty("concurrencyStamp").GetGuid();

        object Edit(string text, Guid stamp) => new
        {
            clientAccountId = client.Id, title = "Stale edit", concurrencyStamp = stamp,
            variants = new object[] { new { profileId = profile, text } },
        };

        var saved = await (await first.PutAsJsonAsync($"/api/v1/agency/social/posts/{id}", Edit("Edited by the first editor", loaded))).ReadJsonAsync();
        Assert.NotEqual(loaded, saved.GetProperty("concurrencyStamp").GetGuid());

        await (await second.PutAsJsonAsync($"/api/v1/agency/social/posts/{id}", Edit("Stale overwrite", loaded))).ShouldFailAsync(409, "concurrency.conflict");
        var text = await api.WithDbAsync(db => db.Set<SocialPostVariant>().Where(v => v.PostId == id).Select(v => v.Text).SingleAsync());
        Assert.Equal("Edited by the first editor", text);
    }

    /// <summary>
    /// The JSON enum converter also reads numbers: <c>"network": 99</c> was stored (enums are persisted as strings) and every
    /// later lookup of the profile's network preset failed with 500. Undefined values are now a 400.
    /// </summary>
    [Fact]
    public async Task Undefined_enum_values_in_request_bodies_are_rejected()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var (_, ads) = await api.CreateClientAsync(Role.AdsSpecialist);

        var handle = "bogus" + Guid.NewGuid().ToString("N")[..6];
        var profile = await manager.PostAsJsonAsync($"/api/v1/agency/social/clients/{client.Id}/profiles", new { network = 99, handle, displayName = "Bogus" });
        Assert.Equal(HttpStatusCode.BadRequest, profile.StatusCode);
        var asText = await manager.PostAsJsonAsync($"/api/v1/agency/social/clients/{client.Id}/profiles", new { network = "99", handle, displayName = "Bogus" });
        Assert.Equal(HttpStatusCode.BadRequest, asText.StatusCode);
        Assert.False(await api.WithDbAsync(db => db.Set<BrandProfile>().AnyAsync(p => p.ClientAccountId == client.Id)));

        var account = await ads.PostAsJsonAsync("/api/v1/agency/ads/accounts", new
        {
            clientAccountId = client.Id, platform = 42, externalAccountId = "123-456-7890", name = "Bogus", currency = "USD", timeZone = "UTC",
        });
        Assert.Equal(HttpStatusCode.BadRequest, account.StatusCode);
        Assert.False(await api.WithDbAsync(db => db.Set<AdAccount>().AnyAsync(a => a.ClientAccountId == client.Id)));

        // Defined values keep working as names and as numbers.
        var ok = await manager.PostAsJsonAsync($"/api/v1/agency/social/clients/{client.Id}/profiles",
            new { network = (int)SocialNetwork.LinkedIn, handle, displayName = "Real" });
        Assert.Equal("LinkedIn", (await ok.ReadJsonAsync()).GetProperty("network").GetString());
    }

    /// <summary>
    /// Clients see a post whose publishing failed as "Scheduled" (the agency is handling it) in their calendar and lists,
    /// but the post itself showed "Failed" with the failure kind of each network.
    /// </summary>
    [Fact]
    public async Task The_client_sees_a_failed_post_as_scheduled_without_failure_details()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var (_, approver) = await SocialAdsKit.ClientUserAsync(api, client.Id, OptimizeAll.Domain.Agency.ClientMemberRole.Approver);
        var profile = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.LinkedIn);
        var id = await SocialAdsKit.ScheduledPostAsync(manager, client.Id, profile, "Goes live soon", DateTime.UtcNow.AddHours(1));
        await api.WithDbAsync(async db =>
        {
            var post = await db.Set<SocialPost>().Include(p => p.Variants).SingleAsync(p => p.Id == id);
            post.Status = SocialPostStatus.Failed;
            post.FailureReason = "LinkedIn: no adapter";
            post.Variants[0].PublishStatus = VariantPublishStatus.Failed;
            post.Variants[0].FailureKind = PublishFailureKind.NotConfigured;
            post.Variants[0].FailureReason = "no adapter";
            await db.SaveChangesAsync();
            return true;
        });

        var staff = await (await manager.GetAsync($"/api/v1/agency/social/posts/{id}")).ReadJsonAsync();
        Assert.Equal("Failed", staff.GetProperty("status").GetString());

        var seen = await (await approver.GetAsync($"/api/v1/client/social/posts/{id}")).ReadJsonAsync();
        Assert.Equal("Scheduled", seen.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("failureReason").ValueKind);
        var variant = seen.GetProperty("variants")[0];
        Assert.Equal("Pending", variant.GetProperty("publishStatus").GetString());
        Assert.Equal("None", variant.GetProperty("failureKind").GetString());
        Assert.Equal(JsonValueKind.Null, variant.GetProperty("failureReason").ValueKind);
    }

    /// <summary>A budget of 0.4 JPY passed validation (≥ 0.01) and was stored as 0 (JPY has no minor unit): "no budget".</summary>
    [Fact]
    public async Task A_budget_amount_that_rounds_to_zero_in_its_currency_is_refused()
    {
        var client = await SocialAdsKit.CreateClientAsync(api, currency: "JPY", country: "JP", zone: "Asia/Tokyo");
        var (_, ads) = await api.CreateClientAsync(Role.AdsSpecialist);
        object Budget(decimal amount) => new { clientAccountId = client.Id, month = DateTime.UtcNow.ToString("yyyy-MM-01"), amount, currency = "JPY" };

        await (await ads.PostAsJsonAsync("/api/v1/agency/ads/budgets", Budget(0.4m))).ShouldFailAsync(400, "ads.amount_too_small");
        Assert.False(await api.WithDbAsync(db => db.Set<AdBudget>().AnyAsync(b => b.ClientAccountId == client.Id)));

        var created = await (await ads.PostAsJsonAsync("/api/v1/agency/ads/budgets", Budget(1500.6m))).ReadJsonAsync();
        Assert.Equal(1501m, created.GetProperty("amount").GetDecimal());
    }
}
