using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.SocialMedia;

[Collection(SocialAdsCollection.Name)]
public sealed class SocialWorkflowTests(ApiFactory api)
{
    [Fact]
    public async Task Validation_is_computed_server_side_and_blocks_submission()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var x = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.X);
        var ig = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.Instagram);

        var validation = await (await manager.PostAsJsonAsync("/api/v1/agency/social/validate", new
        {
            clientAccountId = client.Id,
            variants = new object[] { new { profileId = x, text = new string('a', 281) }, new { profileId = ig, text = "No media" } },
        })).ReadJsonAsync();
        Assert.False(validation.GetProperty("isValid").GetBoolean());
        var variants = validation.GetProperty("variants").EnumerateArray().ToList();
        Assert.Equal(281, variants[0].GetProperty("textLength").GetInt32());
        Assert.Contains(variants[0].GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "social.text_too_long");
        Assert.Contains(variants[1].GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "social.media_required");

        // Drafts may be saved while invalid, but cannot be submitted.
        var post = await SocialAdsKit.CreatePostAsync(manager, client.Id, x, new string('a', 281));
        Assert.False(post.GetProperty("isValid").GetBoolean());
        var id = post.GetProperty("id").GetGuid();
        await (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/submit", new { })).ShouldFailAsync(400, "social.validation_failed");

        // Profiles of another client cannot be used.
        var other = await SocialAdsKit.CreateClientAsync(api);
        var foreign = await SocialAdsKit.ProfileAsync(manager, other.Id, SocialNetwork.X);
        await (await manager.PostAsJsonAsync("/api/v1/agency/social/posts", SocialAdsKit.Post(client.Id, foreign, "Hi")))
            .ShouldFailAsync(400, "social.profile_not_found");
    }

    [Fact]
    public async Task Client_approval_gate_requires_an_approver_of_the_same_client()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        await SocialAdsKit.SetRequireClientApprovalAsync(api, client.Id, true);
        var (_, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var (_, creator) = await api.CreateClientAsync(Role.ContentCreator);
        var profile = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.X);

        var post = await SocialAdsKit.CreatePostAsync(creator, client.Id, profile, "Launch day! Join us.");
        var id = post.GetProperty("id").GetGuid();
        Assert.Equal("Draft", post.GetProperty("status").GetString());
        (await creator.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/submit", new { })).EnsureSuccessStatusCode();

        // Content creators hold social.manage but not social.publish: they cannot approve.
        await (await creator.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/approve", new { })).ShouldFailAsync(403);
        var approved = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/approve", new { comment = "Looks good" })).ReadJsonAsync();
        Assert.Equal("ClientApproval", approved.GetProperty("status").GetString());

        // Cannot skip the client: scheduling from ClientApproval is refused.
        await (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/schedule", new { scheduledAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(1) }))
            .ShouldFailAsync(409, "social.invalid_transition");

        var (_, viewer) = await SocialAdsKit.ClientUserAsync(api, client.Id, ClientMemberRole.Viewer);
        var (_, approver) = await SocialAdsKit.ClientUserAsync(api, client.Id, ClientMemberRole.Approver);
        var otherClient = await SocialAdsKit.CreateClientAsync(api);
        var (_, outsider) = await SocialAdsKit.ClientUserAsync(api, otherClient.Id, ClientMemberRole.Owner);

        var queue = await (await approver.GetAsync($"/api/v1/client/social/approvals?clientId={client.Id}")).ReadJsonAsync();
        Assert.Contains(queue.EnumerateArray(), p => p.GetProperty("id").GetGuid() == id);
        var seen = await (await viewer.GetAsync($"/api/v1/client/social/posts/{id}")).ReadJsonAsync();
        Assert.DoesNotContain(seen.GetProperty("comments").EnumerateArray(), c => c.GetProperty("isInternal").GetBoolean());

        await (await viewer.PostAsJsonAsync($"/api/v1/client/social/posts/{id}/approve", new { })).ShouldFailAsync(403, "client.insufficient_role");
        await (await outsider.PostAsJsonAsync($"/api/v1/client/social/posts/{id}/approve", new { })).ShouldFailAsync(404);
        await (await outsider.GetAsync($"/api/v1/client/social/posts/{id}")).ShouldFailAsync(404);
        await (await approver.PostAsJsonAsync($"/api/v1/client/social/posts/{id}/request-changes", new { })).ShouldFailAsync(400, "social.comment_required");

        var clientApproved = await (await approver.PostAsJsonAsync($"/api/v1/client/social/posts/{id}/approve", new { comment = "Approved, thanks!" })).ReadJsonAsync();
        Assert.Equal("Approved", clientApproved.GetProperty("status").GetString());

        var scheduled = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/schedule",
            new { scheduledAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(1) })).ReadJsonAsync();
        Assert.Equal("Scheduled", scheduled.GetProperty("status").GetString());

        // Editing content after approval sends the post back to Draft.
        var edited = await (await manager.PutAsJsonAsync($"/api/v1/agency/social/posts/{id}", new
        {
            clientAccountId = client.Id, title = "Post", concurrencyStamp = scheduled.GetProperty("concurrencyStamp").GetGuid(),
            variants = new object[] { new { profileId = profile, text = "Launch day! Changed copy." } },
        })).ReadJsonAsync();
        Assert.Equal("Draft", edited.GetProperty("status").GetString());

        // A stale stamp is rejected.
        await (await manager.PutAsJsonAsync($"/api/v1/agency/social/posts/{id}", new
        {
            clientAccountId = client.Id, title = "Post", concurrencyStamp = scheduled.GetProperty("concurrencyStamp").GetGuid(),
            variants = new object[] { new { profileId = profile, text = "Again" } },
        })).ShouldFailAsync(409);

        var audits = await api.WithDbAsync(db => db.Set<AuditLog>().CountAsync(a => a.EntityId == id.ToString() && a.Action.StartsWith("social.post.")));
        Assert.True(audits >= 5);
    }

    [Fact]
    public async Task Tenant_isolation_and_permission_matrix()
    {
        var clientA = await SocialAdsKit.CreateClientAsync(api);
        var clientB = await SocialAdsKit.CreateClientAsync(api);
        var (_, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var profileA = await SocialAdsKit.ProfileAsync(manager, clientA.Id, SocialNetwork.Facebook);
        var postA = (await SocialAdsKit.CreatePostAsync(manager, clientA.Id, profileA, "Tenant A")).GetProperty("id").GetGuid();
        var (_, memberB) = await SocialAdsKit.ClientUserAsync(api, clientB.Id, ClientMemberRole.Owner);

        // Client users only see their organization; staff endpoints are closed to them.
        await (await memberB.GetAsync($"/api/v1/client/social/approvals?clientId={clientA.Id}")).ShouldFailAsync(404);
        await (await memberB.GetAsync($"/api/v1/client/social/calendar?clientId={clientA.Id}&from=2026-01-01&to=2026-02-01")).ShouldFailAsync(404);
        await (await memberB.GetAsync($"/api/v1/client/social/performance?clientId={clientA.Id}")).ShouldFailAsync(404);
        await (await memberB.GetAsync($"/api/v1/agency/social/posts/{postA}")).ShouldFailAsync(403);
        var orgs = await (await memberB.GetAsync("/api/v1/client/social/organizations")).ReadJsonAsync();
        Assert.Equal(clientB.Id, Assert.Single(orgs.EnumerateArray()).GetProperty("id").GetGuid());
        (await memberB.GetAsync($"/api/v1/client/social/performance?clientId={clientB.Id}")).EnsureSuccessStatusCode();

        // Permission matrix.
        var (_, ads) = await api.CreateClientAsync(Role.AdsSpecialist);
        var (_, seo) = await api.CreateClientAsync(Role.SeoSpecialist);
        var (_, accountManager) = await api.CreateClientAsync(Role.AccountManager);
        await (await ads.GetAsync("/api/v1/agency/social/posts")).ShouldFailAsync(403);
        await (await manager.GetAsync("/api/v1/agency/ads/accounts")).ShouldFailAsync(403);
        (await accountManager.GetAsync("/api/v1/agency/social/posts")).EnsureSuccessStatusCode();
        await (await accountManager.PostAsJsonAsync($"/api/v1/agency/social/posts/{postA}/approve", new { })).ShouldFailAsync(403);
        await (await manager.PostAsJsonAsync($"/api/v1/agency/social/profiles/{profileA}/token", new { accessToken = "0123456789abc", externalId = "1" }))
            .ShouldFailAsync(403);
        (await seo.GetAsync($"/api/v1/agency/social/clients/{clientA.Id}/kpis")).EnsureSuccessStatusCode(); // reports.manage
        (await seo.GetAsync($"/api/v1/agency/ads/clients/{clientA.Id}/kpis")).EnsureSuccessStatusCode();
        await (await memberB.GetAsync($"/api/v1/agency/ads/clients/{clientB.Id}/kpis")).ShouldFailAsync(403);
        var anonymous = api.CreateClient();
        await (await anonymous.GetAsync("/api/v1/agency/social/posts")).ShouldFailAsync(401);
        await (await anonymous.GetAsync("/api/v1/client/social/organizations")).ShouldFailAsync(401);

        var list = await (await manager.GetAsync($"/api/v1/agency/social/posts?clientId={clientB.Id}")).ReadJsonAsync();
        Assert.Equal(0, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Calendar_reschedule_queue_and_awareness_days()
    {
        var client = await SocialAdsKit.CreateClientAsync(api, zone: "Asia/Karachi", country: "PK");
        var (_, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var profile = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.Facebook);
        var now = api.Clock.GetUtcNow().UtcDateTime;
        var post = await SocialAdsKit.CreatePostAsync(manager, client.Id, profile, "Calendar post");
        var id = post.GetProperty("id").GetGuid();

        var target = now.Date.AddDays(3).AddHours(9);
        var moved = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/reschedule",
            new { scheduledAt = target, concurrencyStamp = post.GetProperty("concurrencyStamp").GetGuid() })).ReadJsonAsync();
        Assert.Equal(target, moved.GetProperty("scheduledAt").GetDateTime().ToUniversalTime());

        var calendar = await (await manager.GetAsync(
            $"/api/v1/agency/social/calendar?clientId={client.Id}&from={now.Date:yyyy-MM-dd}&to={now.Date.AddDays(40):yyyy-MM-dd}")).ReadJsonAsync();
        Assert.Contains(calendar.GetProperty("posts").EnumerateArray(), p => p.GetProperty("id").GetGuid() == id);
        Assert.Equal(8, calendar.GetProperty("bestTimes").GetArrayLength());
        var year = await (await manager.GetAsync($"/api/v1/agency/social/calendar?clientId={client.Id}&from=2026-08-01&to=2026-09-01")).ReadJsonAsync();
        Assert.Contains(year.GetProperty("awarenessDays").EnumerateArray(), d => d.GetProperty("name").GetString() == "Independence Day (Pakistan)");
        Assert.DoesNotContain(year.GetProperty("awarenessDays").EnumerateArray(), d => d.GetProperty("name").GetString()!.Contains("England"));

        // Queue: next free slot in the client's time zone.
        (await manager.PutAsJsonAsync($"/api/v1/agency/social/profiles/{profile}/queue-slots", new { slots = new[] { new { day = "Monday", time = "10:00" } } }))
            .EnsureSuccessStatusCode();
        (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/submit", new { })).EnsureSuccessStatusCode();
        (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/approve", new { })).EnsureSuccessStatusCode();
        var queued = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/queue", new { })).ReadJsonAsync();
        Assert.Equal("Scheduled", queued.GetProperty("status").GetString());
        var at = queued.GetProperty("scheduledAt").GetDateTime().ToUniversalTime();
        Assert.Equal(DayOfWeek.Monday, TimeZoneInfo.ConvertTimeFromUtc(at, TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi")).DayOfWeek);
        Assert.Equal(5, at.Hour); // 10:00 PKT
    }

    [Fact]
    public async Task Listening_inbox_and_manual_metrics_import()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var profile = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.Instagram);

        var mention = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/clients/{client.Id}/listening/mentions", new
        {
            network = "Instagram", authorHandle = "@fan", text = "I love this brand, amazing service!", postedAt = api.Clock.GetUtcNow().UtcDateTime,
        })).ReadJsonAsync();
        Assert.Equal("Positive", mention.GetProperty("sentiment").GetString());
        Assert.Equal("Automatic", mention.GetProperty("sentimentSource").GetString());
        Assert.Contains("automatic estimate", mention.GetProperty("sentimentLabel").GetString());
        var sync = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/clients/{client.Id}/listening/sync", new { })).ReadJsonAsync();
        Assert.False(sync.GetProperty("configured").GetBoolean());

        var item = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/clients/{client.Id}/inbox", new
        {
            profileId = profile, network = "Instagram", kind = "Comment", authorHandle = "asker", text = "Do you ship to Canada?",
        })).ReadJsonAsync();
        var itemId = item.GetProperty("id").GetGuid();
        await (await manager.PostAsJsonAsync($"/api/v1/agency/social/inbox/{itemId}/replies", new { body = "Yes!", sendViaApi = true }))
            .ShouldFailAsync(409, "social.reply_not_configured");
        var replied = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/inbox/{itemId}/replies", new { body = "Yes, we do!" })).ReadJsonAsync();
        Assert.Equal("Replied", replied.GetProperty("status").GetString());
        Assert.False(replied.GetProperty("replies")[0].GetProperty("sentViaApi").GetBoolean());

        var csv = "Date,Followers,Impressions,Reach,Engagements\n2026-09-01,1000,5000,4000,250\n2026-09-02,1010,6000,4500,300\n";
        var preview = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/profiles/{profile}/metrics/import/preview",
            new { kind = "profile-daily", fileName = "export.csv", csv })).ReadJsonAsync();
        var mapping = preview.GetProperty("suggestedMapping");
        Assert.Equal("Followers", mapping.GetProperty("followers").GetString());
        var body = new { kind = "profile-daily", fileName = "export.csv", csv, source = "Manual", mapping = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(mapping.GetRawText()) };
        var first = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/profiles/{profile}/metrics/import", body)).ReadJsonAsync();
        Assert.Equal(2, first.GetProperty("rowsImported").GetInt32());
        Assert.Equal("Manual", first.GetProperty("sourceLabel").GetString());
        var again = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/profiles/{profile}/metrics/import", body)).ReadJsonAsync();
        Assert.Equal(0, again.GetProperty("rowsImported").GetInt32());
        Assert.Equal(2, again.GetProperty("rowsUpdated").GetInt32());
        Assert.Equal(2, await api.WithDbAsync(db => db.Set<SocialProfileMetric>().CountAsync(m => m.ProfileId == profile)));

        var kpis = await (await manager.GetAsync($"/api/v1/agency/social/clients/{client.Id}/kpis?from=2026-09-01&to=2026-09-02")).ReadJsonAsync();
        Assert.Equal(11000, kpis.GetProperty("totals").GetProperty("impressions").GetInt64());
        Assert.Equal(10, kpis.GetProperty("totals").GetProperty("followersGrowth").GetInt64());
        Assert.Equal("Manual", kpis.GetProperty("sourceLabel").GetString());
    }
}
