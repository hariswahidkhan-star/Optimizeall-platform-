using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.SocialMedia;

/// <summary>Edit/archive/delete/"mark done" coverage of the social workspace and its agency-wide settings.</summary>
[Collection(SocialAdsCollection.Name)]
public sealed class SocialManageTests(ApiFactory api)
{
    [Fact]
    public async Task Listening_queries_competitors_and_mentions_can_be_edited_and_removed()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var query = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/clients/{client.Id}/listening/queries", new { kind = "Hashtag", term = "#launch" })).ReadJsonAsync();
        var qid = query.GetProperty("id").GetGuid();
        var paused = await (await manager.PutAsJsonAsync($"/api/v1/agency/social/listening/queries/{qid}", new
        {
            term = "Relaunch", networks = new[] { "X" }, isActive = false, concurrencyStamp = query.GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        Assert.Equal("#Relaunch", paused.GetProperty("term").GetString());
        Assert.False(paused.GetProperty("isActive").GetBoolean());
        await (await manager.PutAsJsonAsync($"/api/v1/agency/social/listening/queries/{qid}", new
        {
            term = "Again", concurrencyStamp = query.GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(409, "concurrency.conflict");
        await (await manager.PutAsJsonAsync($"/api/v1/agency/social/listening/queries/{qid}", new { term = "x" })).ShouldFailAsync(400);
        await (await manager.PutAsJsonAsync($"/api/v1/agency/social/listening/queries/{Guid.NewGuid()}", new { term = "valid" })).ShouldFailAsync(404);

        var mention = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/clients/{client.Id}/listening/mentions", new
        {
            network = "X", authorHandle = "@fan", text = "Love it", postedAt = DateTime.UtcNow,
        })).ReadJsonAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/v1/agency/social/listening/mentions/{mention.GetProperty("id").GetGuid()}")).StatusCode);
        await (await manager.DeleteAsync($"/api/v1/agency/social/listening/mentions/{mention.GetProperty("id").GetGuid()}")).ShouldFailAsync(404);

        var competitor = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/clients/{client.Id}/competitors", new { name = "Rival", network = "Instagram", handle = "rival" })).ReadJsonAsync();
        var cid = competitor.GetProperty("id").GetGuid();
        var snapshot = await (await manager.PutAsJsonAsync($"/api/v1/agency/social/competitors/{cid}/snapshots", new { date = "2026-09-01", followers = 1200 })).ReadJsonAsync();
        var renamed = await (await manager.PutAsJsonAsync($"/api/v1/agency/social/competitors/{cid}", new
        {
            name = "Rival Inc", network = "Instagram", handle = "@rival_inc", profileUrl = "https://instagram.com/rival_inc",
            concurrencyStamp = competitor.GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        Assert.Equal("Rival Inc", renamed.GetProperty("name").GetString());
        Assert.Single(renamed.GetProperty("snapshots").EnumerateArray());
        await (await manager.PutAsJsonAsync($"/api/v1/agency/social/competitors/{cid}", new { name = "R", network = "Instagram", handle = "r", profileUrl = "javascript:alert(1)" }))
            .ShouldFailAsync(400);
        Assert.Equal(HttpStatusCode.NoContent,
            (await manager.DeleteAsync($"/api/v1/agency/social/competitors/{cid}/snapshots/{snapshot.GetProperty("id").GetGuid()}")).StatusCode);

        var (_, designer) = await api.CreateClientAsync(Role.Designer);
        await (await designer.PutAsJsonAsync($"/api/v1/agency/social/competitors/{cid}", new { name = "X", network = "X", handle = "x" })).ShouldFailAsync(403);

        // Archived profiles can be restored.
        var profile = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.X);
        await (await manager.PostAsync($"/api/v1/agency/social/profiles/{profile}/restore", null)).ShouldFailAsync(409, "social.profile_active");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/v1/agency/social/profiles/{profile}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsync($"/api/v1/agency/social/profiles/{profile}/restore", null)).StatusCode);
        var profiles = await (await manager.GetAsync($"/api/v1/agency/social/clients/{client.Id}/profiles")).ReadJsonAsync();
        Assert.Contains(profiles.EnumerateArray(), p => p.GetProperty("id").GetGuid() == profile);
        await (await manager.PostAsync($"/api/v1/agency/social/profiles/{Guid.NewGuid()}/restore", null)).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Campaigns_archive_restore_and_delete_only_when_unused()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var profile = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.LinkedIn);
        var used = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/clients/{client.Id}/campaigns", new { name = "Spring", utmCampaign = "spring" })).ReadJsonAsync();
        var unused = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/clients/{client.Id}/campaigns", new { name = "Draft idea", utmCampaign = "idea" })).ReadJsonAsync();
        var usedId = used.GetProperty("id").GetGuid();
        (await manager.PostAsJsonAsync("/api/v1/agency/social/posts", new
        {
            clientAccountId = client.Id, title = "Spring post", campaignId = usedId, variants = new[] { new { profileId = profile, text = "Hello" } },
        })).EnsureSuccessStatusCode();

        await (await manager.DeleteAsync($"/api/v1/agency/social/campaigns/{usedId}")).ShouldFailAsync(409, "social.campaign_in_use");
        var archived = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/campaigns/{usedId}/archive", new { concurrencyStamp = used.GetProperty("concurrencyStamp").GetGuid() })).ReadJsonAsync();
        Assert.True(archived.GetProperty("isArchived").GetBoolean());
        await (await manager.PostAsJsonAsync($"/api/v1/agency/social/campaigns/{usedId}/restore", new { concurrencyStamp = used.GetProperty("concurrencyStamp").GetGuid() }))
            .ShouldFailAsync(409, "concurrency.conflict");

        var active = await (await manager.GetAsync($"/api/v1/agency/social/clients/{client.Id}/campaigns")).ReadJsonAsync();
        Assert.DoesNotContain(active.EnumerateArray(), c => c.GetProperty("id").GetGuid() == usedId);
        var all = await (await manager.GetAsync($"/api/v1/agency/social/clients/{client.Id}/campaigns?includeArchived=true")).ReadJsonAsync();
        Assert.Contains(all.EnumerateArray(), c => c.GetProperty("id").GetGuid() == usedId);

        // Archived campaigns cannot be picked for new posts.
        await (await manager.PostAsJsonAsync("/api/v1/agency/social/posts", new
        {
            clientAccountId = client.Id, title = "Late", campaignId = usedId, variants = new[] { new { profileId = profile, text = "Hi" } },
        })).ShouldFailAsync(400, "social.campaign_archived");

        (await manager.PostAsJsonAsync($"/api/v1/agency/social/campaigns/{usedId}/restore", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/v1/agency/social/campaigns/{unused.GetProperty("id").GetGuid()}")).StatusCode);
        await (await manager.PostAsJsonAsync($"/api/v1/agency/social/campaigns/{Guid.NewGuid()}/archive", new { })).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Posts_can_be_duplicated_and_feedback_resolved_or_deleted_by_its_author()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var (_, colleague) = await api.CreateClientAsync(Role.SocialMediaManager);
        var profile = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.Facebook);
        var post = await SocialAdsKit.CreatePostAsync(manager, client.Id, profile, "Original text", "Launch");
        var id = post.GetProperty("id").GetGuid();

        var dup = await manager.PostAsync($"/api/v1/agency/social/posts/{id}/duplicate", null);
        Assert.Equal(HttpStatusCode.Created, dup.StatusCode);
        var copyId = (await dup.ReadJsonAsync()).GetProperty("id").GetGuid();
        var copy = await (await manager.GetAsync($"/api/v1/agency/social/posts/{copyId}")).ReadJsonAsync();
        Assert.Equal("Launch (copy)", copy.GetProperty("title").GetString());
        Assert.Equal("Draft", copy.GetProperty("status").GetString());
        Assert.Equal("Original text", copy.GetProperty("variants")[0].GetProperty("text").GetString());

        var commented = await (await manager.PostAsJsonAsync($"/api/v1/agency/social/posts/{id}/comments", new { body = "Shorten the intro", @internal = true })).ReadJsonAsync();
        var commentId = commented.GetProperty("comments").EnumerateArray().Last().GetProperty("id").GetGuid();
        var resolved = await (await colleague.PostAsync($"/api/v1/agency/social/posts/{id}/comments/{commentId}/resolve", null)).ReadJsonAsync();
        Assert.True(resolved.GetProperty("isResolved").GetBoolean());
        var reopened = await (await colleague.PostAsync($"/api/v1/agency/social/posts/{id}/comments/{commentId}/reopen", null)).ReadJsonAsync();
        Assert.False(reopened.GetProperty("isResolved").GetBoolean());

        await (await colleague.DeleteAsync($"/api/v1/agency/social/posts/{id}/comments/{commentId}")).ShouldFailAsync(403, "social.comment_not_yours");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/v1/agency/social/posts/{id}/comments/{commentId}")).StatusCode);
        await (await manager.PostAsync($"/api/v1/agency/social/posts/{id}/comments/{Guid.NewGuid()}/resolve", null)).ShouldFailAsync(404);
        await (await manager.PostAsync($"/api/v1/agency/social/posts/{Guid.NewGuid()}/duplicate", null)).ShouldFailAsync(404);
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "social.post.duplicated" && a.EntityId == copyId.ToString())));
    }

    [Fact]
    public async Task Network_presets_and_awareness_days_are_editable_by_admins_only()
    {
        var (_, manager) = await api.CreateClientAsync(Role.SocialMediaManager);
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var presets = await (await manager.GetAsync("/api/v1/agency/social/admin/presets")).ReadJsonAsync();
        var pinterest = presets.EnumerateArray().Single(p => p.GetProperty("preset").GetProperty("network").GetString() == "Pinterest");
        var p = pinterest.GetProperty("preset");
        object Body(int maxText, object? stamp, params string[] times) => new
        {
            maxTextLength = maxText, maxTitleLength = p.GetProperty("maxTitleLength").GetInt32(), maxHashtags = p.GetProperty("maxHashtags").GetInt32(),
            maxMentions = p.GetProperty("maxMentions").GetInt32(), maxMedia = p.GetProperty("maxMedia").GetInt32(), maxVideos = p.GetProperty("maxVideos").GetInt32(),
            maxAltTextLength = p.GetProperty("maxAltTextLength").GetInt32(), supportsFirstComment = false, recommendedTimes = times, source = "Agency research 2026",
            concurrencyStamp = stamp,
        };
        await (await manager.PutAsJsonAsync("/api/v1/agency/social/admin/presets/Pinterest", Body(400, null, "Sat 20:00"))).ShouldFailAsync(403);
        await (await admin.PutAsJsonAsync("/api/v1/agency/social/admin/presets/Pinterest", Body(400, null, "Someday"))).ShouldFailAsync(400);
        var updated = await (await admin.PutAsJsonAsync("/api/v1/agency/social/admin/presets/Pinterest", Body(400, pinterest.GetProperty("concurrencyStamp").GetGuid(), "Sat 20:00")))
            .ReadJsonAsync();
        Assert.True(updated.GetProperty("isCustomized").GetBoolean());
        Assert.Equal(400, updated.GetProperty("preset").GetProperty("maxTextLength").GetInt32());
        await (await admin.PutAsJsonAsync("/api/v1/agency/social/admin/presets/Pinterest", Body(450, pinterest.GetProperty("concurrencyStamp").GetGuid(), "Sat 20:00")))
            .ShouldFailAsync(409, "concurrency.conflict");
        var live = await (await manager.GetAsync("/api/v1/agency/social/presets")).ReadJsonAsync();
        Assert.Equal(400, live.EnumerateArray().Single(x => x.GetProperty("network").GetString() == "Pinterest").GetProperty("maxTextLength").GetInt32());
        var reset = await (await admin.PostAsync("/api/v1/agency/social/admin/presets/Pinterest/reset", null)).ReadJsonAsync();
        Assert.False(reset.GetProperty("isCustomized").GetBoolean());

        var name = "Agency anniversary " + Guid.NewGuid().ToString("N")[..6];
        var day = new { month = 2, day = 30, name, sourceUrl = "https://agency.example/", countries = new[] { "gb" } };
        await (await admin.PostAsJsonAsync("/api/v1/agency/social/admin/awareness-days", day)).ShouldFailAsync(400);
        var created = await (await admin.PostAsJsonAsync("/api/v1/agency/social/admin/awareness-days", day with { day = 14 })).ReadJsonAsync();
        Assert.False(created.GetProperty("isBuiltIn").GetBoolean());
        await (await admin.PostAsJsonAsync("/api/v1/agency/social/admin/awareness-days", day with { day = 14 })).ShouldFailAsync(409, "social.awareness_day_exists");
        var did = created.GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync($"/api/v1/agency/social/admin/awareness-days/{did}", new
        {
            month = 2, day = 15, name, sourceUrl = "https://agency.example/", isActive = true, concurrencyStamp = created.GetProperty("concurrencyStamp").GetGuid(),
        })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/agency/social/admin/awareness-days/{did}")).StatusCode);
        await (await admin.DeleteAsync($"/api/v1/agency/social/admin/awareness-days/{did}")).ShouldFailAsync(404);

        // Built-in days are hidden (not deleted) so the seeder does not bring them back.
        var builtIn = (await (await manager.GetAsync("/api/v1/agency/social/admin/awareness-days")).ReadJsonAsync()).EnumerateArray()
            .First(d => d.GetProperty("isBuiltIn").GetBoolean() && d.GetProperty("isActive").GetBoolean());
        var builtInId = builtIn.GetProperty("id").GetGuid();
        await (await manager.DeleteAsync($"/api/v1/agency/social/admin/awareness-days/{builtInId}")).ShouldFailAsync(403);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/agency/social/admin/awareness-days/{builtInId}")).StatusCode);
        var row = await api.WithDbAsync(db => db.Set<SocialAwarenessDay>().AsNoTracking().FirstAsync(d => d.Id == builtInId));
        Assert.False(row.IsActive);
        await api.WithDbAsync(async db =>
        {
            await db.Set<SocialAwarenessDay>().Where(d => d.Id == builtInId).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsActive, true));
        });
    }
}
