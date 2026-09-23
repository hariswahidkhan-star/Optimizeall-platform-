using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.SocialMedia;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.SocialMedia;

/// <summary>Replaces the X adapter: counts calls and holds each one briefly so concurrent runs overlap.</summary>
public sealed class CountingXPublisher : ISocialPublisher
{
    private int _calls;
    private readonly System.Collections.Concurrent.ConcurrentBag<Guid> _variants = new();
    public int Calls => _calls;
    public SocialNetwork Network => SocialNetwork.X;

    /// <summary>Calls for the given variants (other tests' due posts in the shared database may also reach this adapter).</summary>
    public int CallsFor(IEnumerable<Guid> variantIds) => _variants.Count(variantIds.Contains);

    public async Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken ct)
    {
        _variants.Add(request.VariantId);
        var n = Interlocked.Increment(ref _calls);
        await Task.Delay(300, ct);
        return PublishResult.Ok($"x-{n}", $"https://x.com/{request.Handle}/status/{n}");
    }
}

public sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public int Calls;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return Task.FromResult(respond(request));
    }
}

[Collection(SocialAdsCollection.Name)]
public sealed class SocialPublishingTests(ApiFactory api)
{
    private async Task<(Guid ClientId, HttpClient Manager, Guid Profile)> SetupAsync(WebApplicationFactory<Program> host, SocialNetwork network, string? externalId = null)
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var user = await api.CreateUserAsync(new[] { Role.SocialMediaManager });
        var manager = host.CreateClient();
        var login = await manager.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password });
        var token = (await login.ReadJsonAsync()).GetProperty("accessToken").GetString();
        manager.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var profile = await SocialAdsKit.ProfileAsync(manager, client.Id, network, externalId: externalId);
        return (client.Id, manager, profile);
    }

    private static async Task<SocialPostStatus?> PublishAsync(WebApplicationFactory<Program> host, Guid postId)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SocialPublishingService>().PublishPostAsync(postId, CancellationToken.None);
    }

    [Fact]
    public async Task Concurrent_runs_and_retries_publish_exactly_once()
    {
        var fake = new CountingXPublisher();
        using var host = api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<ISocialPublisher>(_ => fake)));
        var (clientId, manager, profile) = await SetupAsync(host, SocialNetwork.X);
        var postId = await SocialAdsKit.ScheduledPostAsync(manager, clientId, profile, "Exactly once", api.Clock.GetUtcNow().UtcDateTime.AddMinutes(2));

        // Not due yet: nothing is claimed.
        Assert.Null(await PublishAsync(host, postId));
        api.Clock.Advance(TimeSpan.FromMinutes(3));

        var variantIds = await api.WithDbAsync(db => db.Set<SocialPostVariant>().Where(v => v.PostId == postId).Select(v => v.Id).ToListAsync());
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PublishAsync(host, postId)));
        Assert.Equal(1, fake.CallsFor(variantIds));
        Assert.Single(results, r => r == SocialPostStatus.Published);
        Assert.Equal(3, results.Count(r => r is null));

        // A retry (another run of the job) does nothing more.
        await host.Services.GetRequiredService<JobRunner>().RunAsync<SocialPublishingJob>();
        Assert.Null(await PublishAsync(host, postId));
        Assert.Equal(1, fake.CallsFor(variantIds));

        var (post, attempts) = await api.WithDbAsync(async db => (
            await db.Set<SocialPost>().Include(p => p.Variants).FirstAsync(p => p.Id == postId),
            await db.Set<SocialPublishAttempt>().CountAsync(a => a.PostId == postId)));
        Assert.Equal(SocialPostStatus.Published, post.Status);
        Assert.StartsWith("x-", post.Variants[0].ExternalPostId);
        Assert.False(post.Variants[0].PublishedManually);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Interrupted_publish_is_never_resent_automatically()
    {
        var fake = new CountingXPublisher();
        using var host = api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<ISocialPublisher>(_ => fake)));
        var (clientId, manager, profile) = await SetupAsync(host, SocialNetwork.X);
        var postId = await SocialAdsKit.ScheduledPostAsync(manager, clientId, profile, "Crash", api.Clock.GetUtcNow().UtcDateTime.AddMinutes(2));

        // Simulate a crash after the variant was claimed and the request sent.
        var claimedAt = api.Clock.GetUtcNow().UtcDateTime;
        await api.WithDbAsync(async db =>
        {
            await db.Set<SocialPost>().Where(p => p.Id == postId).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, SocialPostStatus.Publishing).SetProperty(p => p.PublishClaimId, Guid.NewGuid())
                .SetProperty(p => p.PublishClaimedAt, claimedAt));
            await db.Set<SocialPostVariant>().Where(v => v.PostId == postId).ExecuteUpdateAsync(s => s
                .SetProperty(v => v.PublishStatus, VariantPublishStatus.Publishing).SetProperty(v => v.Attempts, 1));
        });
        api.Clock.Advance(TimeSpan.FromMinutes(20));
        var crashedVariants = await api.WithDbAsync(db => db.Set<SocialPostVariant>().Where(v => v.PostId == postId).Select(v => v.Id).ToListAsync());
        await host.Services.GetRequiredService<JobRunner>().RunAsync<SocialPublishingJob>();

        Assert.Equal(0, fake.CallsFor(crashedVariants));
        var post = await api.WithDbAsync(db => db.Set<SocialPost>().Include(p => p.Variants).FirstAsync(p => p.Id == postId));
        Assert.Equal(SocialPostStatus.Failed, post.Status);
        Assert.Equal(PublishFailureKind.Unknown, post.Variants[0].FailureKind);

        // Retrying requires confirming it is not live.
        var fresh = await api.LoginAsync(await api.CreateUserAsync(new[] { Role.SocialMediaManager }));
        await (await fresh.PostAsJsonAsync($"/api/v1/agency/social/posts/{postId}/retry", new { })).ShouldFailAsync(409, "social.confirm_not_published");
        var retried = await (await fresh.PostAsJsonAsync($"/api/v1/agency/social/posts/{postId}/retry", new { confirmNotPublished = true })).ReadJsonAsync();
        Assert.Equal("Scheduled", retried.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Not_configured_adapters_never_mark_a_post_published_and_manual_publishing_records_the_url()
    {
        var (clientId, manager, profile) = await SetupAsync(api, SocialNetwork.LinkedIn);
        var postId = await SocialAdsKit.ScheduledPostAsync(manager, clientId, profile, "We're hiring!", api.Clock.GetUtcNow().UtcDateTime.AddMinutes(2));
        api.Clock.Advance(TimeSpan.FromMinutes(3));
        await api.RunJobAsync<SocialPublishingJob>();

        var post = await api.WithDbAsync(db => db.Set<SocialPost>().Include(p => p.Variants).FirstAsync(p => p.Id == postId));
        Assert.Equal(SocialPostStatus.Failed, post.Status);
        Assert.Null(post.PublishedAt);
        var variant = Assert.Single(post.Variants);
        Assert.Equal(VariantPublishStatus.Failed, variant.PublishStatus);
        Assert.Equal(PublishFailureKind.NotConfigured, variant.FailureKind);
        Assert.Null(variant.ExternalPostId);
        Assert.Null(variant.PublishedAt);

        // Not retried automatically.
        api.Clock.Advance(TimeSpan.FromHours(1));
        await api.RunJobAsync<SocialPublishingJob>();
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<SocialPublishAttempt>().CountAsync(a => a.PostId == postId)));

        var fresh = await api.LoginAsync(await api.CreateUserAsync(new[] { Role.SocialMediaManager }));
        await (await fresh.PostAsJsonAsync($"/api/v1/agency/social/posts/{postId}/mark-published", new { variantId = variant.Id, url = "javascript:alert(1)" }))
            .ShouldFailAsync(400);
        var marked = await (await fresh.PostAsJsonAsync($"/api/v1/agency/social/posts/{postId}/mark-published",
            new { variantId = variant.Id, url = "https://www.linkedin.com/feed/update/urn:li:activity:1" })).ReadJsonAsync();
        Assert.Equal("Published", marked.GetProperty("status").GetString());
        var v = marked.GetProperty("variants")[0];
        Assert.True(v.GetProperty("publishedManually").GetBoolean());
        Assert.Equal("https://www.linkedin.com/feed/update/urn:li:activity:1", v.GetProperty("publishedUrl").GetString());
        await (await fresh.PostAsJsonAsync($"/api/v1/agency/social/posts/{postId}/mark-published",
            new { variantId = variant.Id, url = "https://www.linkedin.com/x" })).ShouldFailAsync(409, "social.already_published");
    }

    [Fact]
    public async Task Meta_token_expiry_marks_the_profile_and_integration_as_error()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":{\"message\":\"Error validating access token: Session has expired\",\"type\":\"OAuthException\",\"code\":190}}",
                Encoding.UTF8, "application/json"),
        });
        using var host = api.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddHttpClient(MetaGraphClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler)));
        var (clientId, manager, profile) = await SetupAsync(host, SocialNetwork.Facebook, externalId: "1122334455");

        var admin = await api.CreateUserAsync(new[] { Role.Admin });
        var adminHttp = host.CreateClient();
        var login = await adminHttp.PostAsJsonAsync("/api/v1/auth/login", new { email = admin.Email, password = admin.Password });
        adminHttp.DefaultRequestHeaders.Authorization = new("Bearer", (await login.ReadJsonAsync()).GetProperty("accessToken").GetString());
        var connected = await (await adminHttp.PostAsJsonAsync($"/api/v1/agency/social/profiles/{profile}/token", new { accessToken = "EAAB-expired-page-token" })).ReadJsonAsync();
        Assert.Equal("Connected", connected.GetProperty("connectionState").GetString());

        var postId = await SocialAdsKit.ScheduledPostAsync(manager, clientId, profile, "Hello Facebook", api.Clock.GetUtcNow().UtcDateTime.AddMinutes(2));
        api.Clock.Advance(TimeSpan.FromMinutes(3));
        using (var scope = host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SocialPublishingService>().RunAsync(10, CancellationToken.None);

        Assert.True(handler.Calls >= 1);
        var (post, brand, connection) = await api.WithDbAsync(async db =>
        {
            var p = await db.Set<SocialPost>().Include(x => x.Variants).FirstAsync(x => x.Id == postId);
            var b = await db.Set<BrandProfile>().FirstAsync(x => x.Id == profile);
            var c = await db.Set<IntegrationConnection>().FirstAsync(x => x.Id == b.IntegrationConnectionId);
            return (p, b, c);
        });
        Assert.Equal(SocialPostStatus.Failed, post.Status);
        Assert.Equal(PublishFailureKind.Authorization, post.Variants[0].FailureKind);
        Assert.Equal(ProfileConnectionStatus.Error, brand.ConnectionStatus);
        Assert.Equal(IntegrationStatus.Error, connection.Status);
        Assert.DoesNotContain("EAAB-expired-page-token", connection.EncryptedSecrets);
    }

    [Fact]
    public async Task Oauth_requires_app_credentials_and_rejects_tampered_or_foreign_state()
    {
        var (clientId, manager, profile) = await SetupAsync(api, SocialNetwork.X);
        await (await manager.PostAsJsonAsync($"/api/v1/agency/social/profiles/{profile}/connect/start", new { }))
            .ShouldFailAsync(409, "social.app_credentials_required");
        var profiles = await (await manager.GetAsync($"/api/v1/agency/social/clients/{clientId}/profiles")).ReadJsonAsync();
        Assert.Equal("AppCredentialsRequired", profiles[0].GetProperty("connectionState").GetString());

        await api.WithDbAsync(async db =>
        {
            if (await db.Set<IntegrationConnection>().AnyAsync(c => c.Provider == "x" && c.ClientAccountId == null)) return;
            using var scope = api.Services.CreateScope();
            var vault = scope.ServiceProvider.GetRequiredService<ICredentialVault>();
            db.Set<IntegrationConnection>().Add(new IntegrationConnection
            {
                Provider = "x", DisplayName = "X developer app", SettingsJson = "{\"clientId\":\"x-client-id\"}",
                EncryptedSecrets = vault.Protect(new Dictionary<string, string> { ["clientSecret"] = "x-secret" }), Status = IntegrationStatus.Connected,
            });
            await db.SaveChangesAsync();
        });

        var start = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/profiles/{profile}/connect/start", new { })).ReadJsonAsync();
        var url = new Uri(start.GetProperty("authorizationUrl").GetString()!);
        Assert.Equal("x.com", url.Host);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(url.Query);
        Assert.Equal("x-client-id", query["client_id"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        var state = query["state"].ToString();

        var tampered = state[..^2] + (state[^2] == 'A' ? "BB" : "AA");
        await (await manager.PostAsJsonAsync("/api/v1/agency/social/oauth/callback", new { code = "abc", state = tampered }))
            .ShouldFailAsync(400, "social.oauth_state_invalid");
        var other = await api.LoginAsync(await api.CreateUserAsync(new[] { Role.SocialMediaManager }));
        await (await other.PostAsJsonAsync("/api/v1/agency/social/oauth/callback", new { code = "abc", state }))
            .ShouldFailAsync(400, "social.oauth_state_invalid");
        api.Clock.Advance(TimeSpan.FromMinutes(16));
        var relogged = await api.LoginAsync(await api.CreateUserAsync(new[] { Role.SocialMediaManager }));
        await (await relogged.PostAsJsonAsync("/api/v1/agency/social/oauth/callback", new { code = "abc", state }))
            .ShouldFailAsync(400, "social.oauth_state_invalid");
    }

    [Fact]
    public async Task Evergreen_posts_are_requeued_up_to_the_maximum()
    {
        var (clientId, _, profile) = await SetupAsync(api, SocialNetwork.X);
        var now = api.Clock.GetUtcNow().UtcDateTime;
        var original = new SocialPost
        {
            ClientAccountId = clientId, Title = "Evergreen tip", Status = SocialPostStatus.Published, ScheduledAt = now.AddDays(-31), PublishedAt = now.AddDays(-31),
            IsEvergreen = true, EvergreenIntervalDays = 30, EvergreenMaxRepeats = 2, CreatedByUserId = Guid.NewGuid(),
        };
        original.Variants.Add(new SocialPostVariant
        {
            PostId = original.Id, ClientAccountId = clientId, ProfileId = profile, Network = SocialNetwork.X, Text = "Stretch before you run.",
            PublishStatus = VariantPublishStatus.Published, PublishedAt = now.AddDays(-31), ExternalPostId = "1",
        });
        await api.WithDbAsync(async db => { db.Add(original); await db.SaveChangesAsync(); });

        await api.RunJobAsync<SocialEvergreenJob>();
        await api.RunJobAsync<SocialEvergreenJob>(); // idempotent: the pending copy blocks another one
        var copies = await api.WithDbAsync(db => db.Set<SocialPost>().Where(p => p.RecycledFromPostId == original.Id).ToListAsync());
        var copy = Assert.Single(copies);
        Assert.Equal(1, copy.RecycleNumber);
        Assert.Equal(SocialPostStatus.Scheduled, copy.Status);

        // Publish the copy (manually) and move past the interval twice: only one more copy (max 2).
        for (var round = 0; round < 3; round++)
        {
            await api.WithDbAsync(db => db.Set<SocialPost>().Where(p => p.RecycledFromPostId == original.Id && p.Status == SocialPostStatus.Scheduled)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, SocialPostStatus.Published).SetProperty(p => p.PublishedAt, api.Clock.GetUtcNow().UtcDateTime)));
            api.Clock.Advance(TimeSpan.FromDays(31));
            await api.RunJobAsync<SocialEvergreenJob>();
        }
        var all = await api.WithDbAsync(db => db.Set<SocialPost>().Where(p => p.RecycledFromPostId == original.Id).OrderBy(p => p.RecycleNumber).ToListAsync());
        Assert.Equal(new int?[] { 1, 2 }, all.Select(p => p.RecycleNumber).ToArray());
        Assert.Equal(2, await api.WithDbAsync(db => db.Set<SocialPost>().Where(p => p.Id == original.Id).Select(p => p.EvergreenRepeatCount).FirstAsync()));
    }
}
