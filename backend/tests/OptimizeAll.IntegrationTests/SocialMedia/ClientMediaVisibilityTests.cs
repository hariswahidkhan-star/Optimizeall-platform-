using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.SocialMedia;

/// <summary>Client users can open the media of posts they may see, never library items or media of drafts.</summary>
[Collection(SocialAdsCollection.Name)]
public sealed class ClientMediaVisibilityTests(ApiFactory api)
{
    private static async Task<Guid> MediaAsync(HttpClient staff, Guid clientId, string url) =>
        (await (await staff.PostAsJsonAsync($"/api/v1/agency/social/clients/{clientId}/media/url", new
        {
            kind = "Image", url, title = "Photo", width = 1080, height = 1080,
        })).ReadJsonAsync()).GetProperty("id").GetGuid();

    [Fact]
    public async Task Client_media_content_is_limited_to_media_of_visible_posts()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        await SocialAdsKit.SetRequireClientApprovalAsync(api, client.Id, true);
        var (_, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var profile = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.X);
        var draftMedia = await MediaAsync(manager, client.Id, "https://cdn.example.test/draft.jpg");
        var sharedMedia = await MediaAsync(manager, client.Id, "https://cdn.example.test/shared.jpg");
        var libraryOnly = await MediaAsync(manager, client.Id, "https://cdn.example.test/library.jpg");

        object Body(string title, Guid media) => new
        {
            clientAccountId = client.Id, title, variants = new object[] { new { profileId = profile, text = "Coming soon", mediaIds = new[] { media } } },
        };
        (await manager.PostAsJsonAsync("/api/v1/agency/social/posts", Body("Draft", draftMedia))).EnsureSuccessStatusCode();
        var shared = (await (await manager.PostAsJsonAsync("/api/v1/agency/social/posts", Body("For the client", sharedMedia))).ReadJsonAsync())
            .GetProperty("id").GetGuid();
        (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{shared}/submit", new { })).EnsureSuccessStatusCode();
        (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{shared}/approve", new { })).EnsureSuccessStatusCode(); // → ClientApproval

        var (user, _) = await SocialAdsKit.ClientUserAsync(api, client.Id, ClientMemberRole.Approver);
        var loggedIn = await api.LoginAsync(user);
        var viewer = api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        viewer.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        viewer.DefaultRequestHeaders.Authorization = loggedIn.DefaultRequestHeaders.Authorization;

        var visible = await viewer.GetAsync($"/api/v1/client/social/media/{sharedMedia}/content");
        Assert.Equal(HttpStatusCode.Redirect, visible.StatusCode);
        await (await viewer.GetAsync($"/api/v1/client/social/media/{draftMedia}/content")).ShouldFailAsync(404);
        await (await viewer.GetAsync($"/api/v1/client/social/media/{libraryOnly}/content")).ShouldFailAsync(404);
    }
}
