using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Marketing;

public sealed class ContentPlanningTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Templates_crud_search_and_archive_instead_of_delete_when_referenced()
    {
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var created = await manager.PostAsJsonAsync("/api/v1/marketing/templates", new
        {
            name = $"Launch post {tag}", platform = "Instagram", body = "Our spring drop is here! #ad", hashtags = "#spring #drop", languageCode = "en",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var template = await created.ReadJsonAsync();
        var id = template.GetProperty("id").GetGuid();

        var updated = await (await manager.PutAsJsonAsync($"/api/v1/marketing/templates/{id}", new
        {
            name = $"Launch post {tag} v2", platform = "Instagram", body = "Updated body #ad", languageCode = "en-GB",
        })).ReadJsonAsync();
        Assert.Equal("Updated body #ad", updated.GetProperty("body").GetString());
        Assert.Equal("en-GB", updated.GetProperty("languageCode").GetString());

        var search = await (await manager.GetAsync($"/api/v1/marketing/templates?search={tag}&platform=Instagram")).ReadJsonAsync();
        Assert.Equal(1, search.GetProperty("total").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/v1/marketing/templates",
            new { name = "Too long", body = new string('x', 5001) })).StatusCode);

        // Referenced by a campaign asset: archived, not deleted.
        var managerUser = await api.CreateUserAsync(new[] { Role.CampaignManager });
        var campaign = await api.CreateCampaignAsync(managerUser.Id);
        await api.WithDbAsync(db => db.Set<CampaignAsset>().Where(a => a.Id == campaign.Assets[1].Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.TemplateId, id)));
        var deleted = await (await manager.DeleteAsync($"/api/v1/marketing/templates/{id}")).ReadJsonAsync();
        Assert.False(deleted.GetProperty("deleted").GetBoolean());
        Assert.True(deleted.GetProperty("archived").GetBoolean());
        Assert.Equal(0, (await (await manager.GetAsync($"/api/v1/marketing/templates?search={tag}")).ReadJsonAsync()).GetProperty("total").GetInt32());
        var archived = await (await manager.GetAsync($"/api/v1/marketing/templates?search={tag}&includeArchived=true")).ReadJsonAsync();
        Assert.Equal(1, archived.GetProperty("items")[0].GetProperty("usageCount").GetInt32());

        var unused = await manager.PostJsonAsync("/api/v1/marketing/templates", new { name = "Unused", body = "Body" });
        var gone = await (await manager.DeleteAsync($"/api/v1/marketing/templates/{unused.GetProperty("id").GetGuid()}")).ReadJsonAsync();
        Assert.True(gone.GetProperty("deleted").GetBoolean());
        await (await manager.GetAsync($"/api/v1/marketing/templates/{unused.GetProperty("id").GetGuid()}")).ShouldFailAsync(404, "template.not_found");
    }

    [Fact]
    public async Task Calendar_entries_crud_with_filters_and_range_validation()
    {
        var (managerUser, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var campaign = await api.CreateCampaignAsync(managerUser.Id);
        var template = await manager.PostJsonAsync("/api/v1/marketing/templates", new { name = "Teaser", body = "Soon." });
        var at = api.Now().Date.AddDays(3).AddHours(15);

        var entry = await manager.PostJsonAsync("/api/v1/marketing/calendar", new
        {
            title = "Teaser post", campaignId = campaign.Id, templateId = template.GetProperty("id").GetGuid(), platform = "TikTok",
            scheduledFor = at, notes = "Use the vertical cut",
        });
        Assert.Equal(campaign.Title, entry.GetProperty("campaignTitle").GetString());
        Assert.Equal("Teaser", entry.GetProperty("templateName").GetString());
        Assert.Equal("Planned", entry.GetProperty("status").GetString());
        await manager.PostJsonAsync("/api/v1/marketing/calendar", new { title = "Other platform", platform = "X", scheduledFor = at });
        await manager.PostJsonAsync("/api/v1/marketing/calendar", new { title = "Far future", scheduledFor = at.AddDays(90) });

        var from = api.Now().Date.ToString("O");
        var to = api.Now().Date.AddDays(10).ToString("O");
        var list = await (await manager.GetAsync($"/api/v1/marketing/calendar?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}")).ReadJsonAsync();
        Assert.Equal(2, list.GetProperty("items").GetArrayLength());
        var tiktok = await (await manager.GetAsync($"/api/v1/marketing/calendar?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&platform=TikTok&campaignId={campaign.Id}")).ReadJsonAsync();
        Assert.Equal("Teaser post", Assert.Single(tiktok.GetProperty("items").EnumerateArray().ToList()).GetProperty("title").GetString());

        var id = entry.GetProperty("id").GetGuid();
        var updated = await (await manager.PutAsJsonAsync($"/api/v1/marketing/calendar/{id}", new
        {
            title = "Teaser post", campaignId = campaign.Id, platform = "TikTok", scheduledFor = at, status = "Scheduled",
        })).ReadJsonAsync();
        Assert.Equal("Scheduled", updated.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("templateId").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/v1/marketing/calendar/{id}")).StatusCode);
        await (await manager.GetAsync($"/api/v1/marketing/calendar/{id}")).ShouldFailAsync(404);

        await (await manager.GetAsync($"/api/v1/marketing/calendar?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(api.Now().Date.AddDays(400).ToString("O"))}"))
            .ShouldFailAsync(400, "range.too_long");
        await (await manager.GetAsync($"/api/v1/marketing/calendar?from={Uri.EscapeDataString(to)}&to={Uri.EscapeDataString(from)}"))
            .ShouldFailAsync(400, "range.invalid");
        await (await manager.PostAsJsonAsync("/api/v1/marketing/calendar", new { title = "Bad", campaignId = Guid.NewGuid(), scheduledFor = at }))
            .ShouldFailAsync(400, "calendar.campaign_not_found");
    }
}
