using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OptimizeAll.Api.Modules.SocialMedia;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Seo;

namespace OptimizeAll.IntegrationTests.SocialMedia;

/// <summary>Listening/inbox providers that return the same items on every call (with an in-batch duplicate), slowly.</summary>
public sealed class RepeatingSocialProviders : ISocialListeningProvider, ISocialInboxProvider
{
    public async Task<AdapterResult<IngestedMention>> FetchAsync(Guid clientId, IReadOnlyList<SocialListeningQuery> queries, DateTime since, CancellationToken ct)
    {
        await Task.Delay(50, ct);
        var at = DateTime.UtcNow.Date;
        var items = new[]
        {
            new IngestedMention(SocialNetwork.X, "m-1", "fan", "Great service", null, at),
            new IngestedMention(SocialNetwork.X, "m-2", "critic", "Slow delivery", null, at),
            new IngestedMention(SocialNetwork.X, "m-1", "fan", "Great service", null, at),
        };
        return new AdapterResult<IngestedMention>(true, items, "ok");
    }

    public async Task<AdapterResult<IngestedInboxItem>> FetchAsync(BrandProfile profile, DateTime since, CancellationToken ct)
    {
        await Task.Delay(50, ct);
        var at = DateTime.UtcNow.Date;
        var items = new[]
        {
            new IngestedInboxItem(profile.Network, InboxItemKind.Comment, "c-1", "asker", "Do you ship?", null, at),
            new IngestedInboxItem(profile.Network, InboxItemKind.Comment, "c-2", "fan", "Love it", null, at),
            new IngestedInboxItem(profile.Network, InboxItemKind.Comment, "c-1", "asker", "Do you ship?", null, at),
        };
        return new AdapterResult<IngestedInboxItem>(true, items, "ok");
    }

    public Task<(bool Sent, string? ExternalId, string Message)> ReplyAsync(BrandProfile profile, SocialInboxItem item, string body, CancellationToken ct) =>
        Task.FromResult((false, (string?)null, "not supported"));
}

public sealed class SocialSyncFixture : IAsyncLifetime
{
    public ApiFactory Api { get; } = new();
    public WebApplicationFactory<Program> Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Api.InitializeAsync();
        Host = Api.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISocialListeningProvider>();
            services.RemoveAll<ISocialInboxProvider>();
            services.AddSingleton<RepeatingSocialProviders>();
            services.AddSingleton<ISocialListeningProvider>(sp => sp.GetRequiredService<RepeatingSocialProviders>());
            services.AddSingleton<ISocialInboxProvider>(sp => sp.GetRequiredService<RepeatingSocialProviders>());
        }));
        _ = Host.Services;
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        await Api.DisposeAsync();
    }
}

/// <summary>Concurrent metric imports and listening/inbox syncs are serialized per profile/client: no 409/500, no duplicates.</summary>
public sealed class SocialSyncConcurrencyTests(SocialSyncFixture fx) : IClassFixture<SocialSyncFixture>
{
    private static async Task AllSucceedAsync(IEnumerable<HttpResponseMessage> responses)
    {
        foreach (var r in responses)
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Concurrent_metric_imports_of_one_profile_upsert_each_day_once()
    {
        var client = await SocialAdsKit.CreateClientAsync(fx.Api);
        var manager = await fx.Host.LoginAsync(await fx.Api.CreateUserAsync(new[] { Role.SocialMediaManager }));
        var profile = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.Instagram);
        var csv = "Date,Followers,Impressions,Reach,Engagements\n" + string.Join("\n",
            Enumerable.Range(1, 20).Select(d => $"2026-08-{d:00},{1000 + d},{5000 + d},{4000 + d},{250 + d}")) + "\n";
        var mapping = new Dictionary<string, string>
        {
            ["date"] = "Date", ["followers"] = "Followers", ["impressions"] = "Impressions", ["reach"] = "Reach", ["engagements"] = "Engagements",
        };
        var body = new { kind = "profile-daily", fileName = "export.csv", csv, source = "Manual", mapping };

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            manager.PostAsJsonAsync($"/api/v1/agency/social/profiles/{profile}/metrics/import", body)));
        await AllSucceedAsync(responses);
        var results = await Task.WhenAll(responses.Select(r => r.ReadJsonAsync()));
        Assert.Equal(20, results.Sum(r => r.GetProperty("rowsImported").GetInt32()));
        Assert.Equal(20, await fx.Api.WithDbAsync(db => db.Set<SocialProfileMetric>().CountAsync(m => m.ProfileId == profile)));
    }

    [Fact]
    public async Task Concurrent_listening_and_inbox_syncs_import_each_item_once()
    {
        var client = await SocialAdsKit.CreateClientAsync(fx.Api);
        var manager = await fx.Host.LoginAsync(await fx.Api.CreateUserAsync(new[] { Role.SocialMediaManager }));
        await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.Instagram);

        var listening = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            manager.PostAsJsonAsync($"/api/v1/agency/social/clients/{client.Id}/listening/sync", new { })));
        await AllSucceedAsync(listening);
        Assert.Equal(2, await fx.Api.WithDbAsync(db => db.Set<SocialMention>().CountAsync(m => m.ClientAccountId == client.Id)));

        var inbox = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            manager.PostAsJsonAsync($"/api/v1/agency/social/clients/{client.Id}/inbox/sync", new { })));
        await AllSucceedAsync(inbox);
        Assert.Equal(2, await fx.Api.WithDbAsync(db => db.Set<SocialInboxItem>().CountAsync(i => i.ClientAccountId == client.Id)));
        var imported = await Task.WhenAll(inbox.Select(r => r.ReadJsonAsync()));
        Assert.Equal(2, imported.Sum(r => r.GetProperty("imported").GetInt32()));
    }
}
