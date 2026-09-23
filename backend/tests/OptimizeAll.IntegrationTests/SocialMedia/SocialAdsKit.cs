using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.SocialMedia;

/// <summary>All social/ads integration test classes share one database (keeps MySQL usage modest).</summary>
[CollectionDefinition(Name)]
public sealed class SocialAdsCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "SocialAds";
}

public static class SocialAdsKit
{
    public static async Task<ClientAccount> CreateClientAsync(ApiFactory api, string currency = "USD", string country = "US", string zone = "America/New_York",
        Guid? accountManager = null)
    {
        var client = new ClientAccount
        {
            Name = "Client " + Guid.NewGuid().ToString("N")[..6], Slug = "c-" + Guid.NewGuid().ToString("N")[..12], CountryCode = country, Currency = currency,
            TimeZone = zone, Status = ClientAccountStatus.Active, AccountManagerUserId = accountManager,
        };
        await api.WithDbAsync(async db =>
        {
            db.Set<ClientAccount>().Add(client);
            await db.SaveChangesAsync();
        });
        return client;
    }

    public static async Task<(TestUser User, HttpClient Http)> ClientUserAsync(ApiFactory api, Guid clientId, ClientMemberRole role)
    {
        var user = await api.CreateUserAsync(new[] { Role.Client }, countryCode: "US");
        await api.WithDbAsync(async db =>
        {
            db.Set<ClientMember>().Add(new ClientMember { ClientAccountId = clientId, UserId = user.Id, Role = role, AddedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        return (user, await api.LoginAsync(user));
    }

    public static async Task SetRequireClientApprovalAsync(ApiFactory api, Guid clientId, bool value)
    {
        await api.WithDbAsync(async db =>
        {
            var s = await db.Set<SocialClientSettings>().FirstOrDefaultAsync(x => x.ClientAccountId == clientId);
            if (s is null) db.Set<SocialClientSettings>().Add(new SocialClientSettings { ClientAccountId = clientId, RequireClientApproval = value });
            else s.RequireClientApproval = value;
            await db.SaveChangesAsync();
        });
    }

    public static async Task<Guid> ProfileAsync(HttpClient staff, Guid clientId, SocialNetwork network, string? handle = null, string? externalId = null)
    {
        var res = await staff.PostAsJsonAsync($"/api/v1/agency/social/clients/{clientId}/profiles", new
        {
            network = network.ToString(), handle = handle ?? "brand" + Guid.NewGuid().ToString("N")[..6], displayName = "Brand", externalId,
        });
        return (await res.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    public static object Post(Guid clientId, Guid profileId, string text, string title = "Post", object[]? more = null) => new
    {
        clientAccountId = clientId,
        title,
        variants = new object[] { new { profileId, text } }.Concat(more ?? Array.Empty<object>()).ToArray(),
    };

    public static async Task<JsonElement> CreatePostAsync(HttpClient staff, Guid clientId, Guid profileId, string text, string title = "Post") =>
        await (await staff.PostAsJsonAsync("/api/v1/agency/social/posts", Post(clientId, profileId, text, title))).ReadJsonAsync();

    /// <summary>Draft → InternalReview → Approved (client approval off) → Scheduled at <paramref name="at"/>.</summary>
    public static async Task<Guid> ScheduledPostAsync(HttpClient manager, Guid clientId, Guid profileId, string text, DateTime at)
    {
        var post = await CreatePostAsync(manager, clientId, profileId, text);
        var id = post.GetProperty("id").GetGuid();
        (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/submit", new { })).EnsureSuccessStatusCode();
        (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/approve", new { })).EnsureSuccessStatusCode();
        var scheduled = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/schedule", new { scheduledAt = at })).ReadJsonAsync();
        Assert.Equal("Scheduled", scheduled.GetProperty("status").GetString());
        return id;
    }
}
