using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.SocialMedia;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.SocialMedia;

/// <summary>
/// The client approval gate must hold even for posts approved internally while the client did not require approval:
/// once the client requires it, such posts cannot be scheduled (or retried) and switching the setting on sends approved
/// and scheduled posts back to the client.
/// </summary>
[Collection(SocialAdsCollection.Name)]
public sealed class SocialApprovalGateTests(ApiFactory api)
{
    private DateTime Tomorrow => api.Clock.GetUtcNow().UtcDateTime.AddDays(1);

    private static async Task<Guid> ApprovedPostAsync(HttpClient manager, Guid clientId, Guid profileId, string text)
    {
        var id = (await SocialAdsKit.CreatePostAsync(manager, clientId, profileId, text)).GetProperty("id").GetGuid();
        (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/submit", new { })).EnsureSuccessStatusCode();
        var approved = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/approve", new { })).ReadJsonAsync();
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        return id;
    }

    [Fact]
    public async Task Internally_approved_post_cannot_be_scheduled_once_the_client_requires_approval()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var profile = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.X);
        var id = await ApprovedPostAsync(manager, client.Id, profile, "Approved before the client asked to review posts.");

        await SocialAdsKit.SetRequireClientApprovalAsync(api, client.Id, true);

        var dto = await (await manager.GetAsync($"/api/v1/agency/social/posts/{id}")).ReadJsonAsync();
        Assert.DoesNotContain(dto.GetProperty("allowedActions").EnumerateArray(), a => a.GetString() is "schedule" or "queue");
        await (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/schedule", new { scheduledAt = Tomorrow }))
            .ShouldFailAsync(409, "social.client_approval_required");
        var status = await api.WithDbAsync(db => db.Set<SocialPost>().Where(p => p.Id == id).Select(p => p.Status).FirstAsync());
        Assert.Equal(SocialPostStatus.Approved, status);
    }

    [Fact]
    public async Task Switching_client_approval_on_sends_approved_and_scheduled_posts_to_the_client()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var profile = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.X);
        var approved = await ApprovedPostAsync(manager, client.Id, profile, "Approved internally only.");
        var scheduled = await SocialAdsKit.ScheduledPostAsync(manager, client.Id, profile, "Scheduled after an internal approval.", Tomorrow);
        var (_, approver) = await SocialAdsKit.ClientUserAsync(api, client.Id, ClientMemberRole.Approver);

        var settings = await (await manager.GetAsync($"/api/v1/agency/social/clients/{client.Id}/settings")).ReadJsonAsync();
        (await manager.PutAsJsonAsync($"/api/v1/agency/social/clients/{client.Id}/settings", new
        {
            requireClientApproval = true, defaultUtmMedium = "social", concurrencyStamp = settings.GetProperty("concurrencyStamp").GetGuid(),
        })).EnsureSuccessStatusCode();

        var statuses = await api.WithDbAsync(db => db.Set<SocialPost>().Where(p => p.Id == approved || p.Id == scheduled)
            .ToDictionaryAsync(p => p.Id, p => (p.Status, p.ApprovedByUserId)));
        Assert.Equal((SocialPostStatus.ClientApproval, (Guid?)null), statuses[approved]);
        Assert.Equal((SocialPostStatus.ClientApproval, (Guid?)null), statuses[scheduled]);

        // The client's approvers see them and can approve; then the post can be scheduled again.
        var queue = await (await approver.GetAsync($"/api/v1/client/social/approvals?clientId={client.Id}")).ReadJsonAsync();
        Assert.Contains(queue.EnumerateArray(), p => p.GetProperty("id").GetGuid() == scheduled);
        (await approver.PostAsJsonAsync($"/api/v1/client/social/posts/{scheduled}/approve", new { comment = "OK" })).EnsureSuccessStatusCode();
        var rescheduled = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{scheduled}/schedule", new { scheduledAt = Tomorrow }))
            .ReadJsonAsync();
        Assert.Equal("Scheduled", rescheduled.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Evergreen_copies_of_posts_the_client_never_approved_wait_for_the_client()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (staff, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var profile = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.X);
        var (approver, _) = await SocialAdsKit.ClientUserAsync(api, client.Id, ClientMemberRole.Approver);
        await SocialAdsKit.SetRequireClientApprovalAsync(api, client.Id, true);
        var now = api.Clock.GetUtcNow().UtcDateTime;

        SocialPost Original(string title, Guid approvedBy)
        {
            var post = new SocialPost
            {
                ClientAccountId = client.Id, Title = title, Status = SocialPostStatus.Published, ScheduledAt = now.AddDays(-31), PublishedAt = now.AddDays(-31),
                IsEvergreen = true, EvergreenIntervalDays = 30, EvergreenMaxRepeats = 2, CreatedByUserId = staff.Id, ApprovedByUserId = approvedBy,
                ApprovedAt = now.AddDays(-32),
            };
            post.Variants.Add(new SocialPostVariant
            {
                PostId = post.Id, ClientAccountId = client.Id, ProfileId = profile, Network = SocialNetwork.X, Text = title,
                PublishStatus = VariantPublishStatus.Published, PublishedAt = now.AddDays(-31), ExternalPostId = Guid.NewGuid().ToString("N"),
            });
            return post;
        }

        var internalOnly = Original("Approved internally before client approval was required", staff.Id);
        var clientApproved = Original("Approved by the client", approver.Id);
        await api.WithDbAsync(async db => { db.AddRange(internalOnly, clientApproved); await db.SaveChangesAsync(); });

        await api.RunJobAsync<SocialEvergreenJob>();

        var copies = await api.WithDbAsync(db => db.Set<SocialPost>()
            .Where(p => p.RecycledFromPostId == internalOnly.Id || p.RecycledFromPostId == clientApproved.Id)
            .ToDictionaryAsync(p => p.RecycledFromPostId!.Value, p => (p.Status, p.ApprovedByUserId)));
        Assert.Equal((SocialPostStatus.ClientApproval, (Guid?)null), copies[internalOnly.Id]);
        Assert.Equal((SocialPostStatus.Scheduled, (Guid?)approver.Id), copies[clientApproved.Id]);
    }
}
