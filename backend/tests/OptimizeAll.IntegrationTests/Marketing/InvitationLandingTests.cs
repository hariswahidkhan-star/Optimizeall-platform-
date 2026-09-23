using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Marketing;

public sealed class InvitationLandingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private HttpClient Anonymous()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        return client;
    }

    private async Task RegisterAsync(string? inviteCode)
    {
        var response = await Anonymous().PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"inv-{Guid.NewGuid():N}@example.test", password = "Horizon-Tulip-42", displayName = "Invited Person",
            countryCode = "PK", acceptTerms = true, inviteCode,
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Platform_invitation_counts_visits_and_registrations_up_to_max_uses()
    {
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var created = await manager.PostAsJsonAsync("/api/v1/marketing/invitations", new
        {
            name = "Spring newsletter", utmSource = "newsletter", utmMedium = "email", utmCampaign = "spring", maxUses = 2,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var invite = await created.ReadJsonAsync();
        var code = invite.GetProperty("code").GetString()!;
        Assert.Matches("^[A-Za-z0-9]{8}$", code);
        Assert.Equal($"http://app.test/join/{code}", invite.GetProperty("url").GetString());

        var anon = Anonymous();
        var landing = await (await anon.GetAsync($"/api/v1/public/invitations/{code}")).ReadJsonAsync();
        Assert.Equal("platform", landing.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(landing.GetProperty("headline").GetString()));
        Assert.Equal(JsonValueKind.Null, landing.GetProperty("campaign").ValueKind);
        Assert.Equal("newsletter", landing.GetProperty("utm").GetProperty("source").GetString());
        (await anon.GetAsync($"/api/v1/public/invitations/{code}")).EnsureSuccessStatusCode();

        await RegisterAsync(code);
        await RegisterAsync(code);
        await RegisterAsync(code); // over MaxUses: not counted

        var id = invite.GetProperty("id").GetGuid();
        var stats = (await (await manager.GetAsync($"/api/v1/marketing/invitations/{id}")).ReadJsonAsync()).GetProperty("stats");
        Assert.Equal(2, stats.GetProperty("visits").GetInt32());
        Assert.Equal(2, stats.GetProperty("registrations").GetInt32());
        Assert.Equal(0, stats.GetProperty("remainingUses").GetInt32());

        // Exhausted: the landing page is gone.
        await (await anon.GetAsync($"/api/v1/public/invitations/{code}")).ShouldFailAsync(404);
        await (await anon.GetAsync("/api/v1/public/invitations/unknown1")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Staff_preview_does_not_count_a_visit_but_anonymous_preview_does()
    {
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var invite = await (await manager.PostAsJsonAsync("/api/v1/marketing/invitations", new { name = "Preview check" })).ReadJsonAsync();
        var code = invite.GetProperty("code").GetString()!;
        var id = invite.GetProperty("id").GetGuid();
        async Task<int> VisitsAsync() =>
            (await (await manager.GetAsync($"/api/v1/marketing/invitations/{id}")).ReadJsonAsync()).GetProperty("stats").GetProperty("visits").GetInt32();

        var preview = await (await manager.GetAsync($"/api/v1/public/invitations/{code}?preview=true")).ReadJsonAsync();
        Assert.Equal("platform", preview.GetProperty("type").GetString());
        Assert.Equal(0, await VisitsAsync());

        // Without marketing.manage the flag is ignored.
        (await Anonymous().GetAsync($"/api/v1/public/invitations/{code}?preview=true")).EnsureSuccessStatusCode();
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        (await reviewer.GetAsync($"/api/v1/public/invitations/{code}?preview=true")).EnsureSuccessStatusCode();
        Assert.Equal(2, await VisitsAsync());

        // The manager's normal (non-preview) visit counts.
        (await manager.GetAsync($"/api/v1/public/invitations/{code}")).EnsureSuccessStatusCode();
        Assert.Equal(3, await VisitsAsync());
    }

    [Fact]
    public async Task Inactive_and_expired_invitations_are_not_found_and_are_not_counted()
    {
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var inactive = await manager.PostJsonAsync("/api/v1/marketing/invitations", new { name = "Paused", isActive = false });
        var expiring = await manager.PostJsonAsync("/api/v1/marketing/invitations", new { name = "Short", expiresAt = api.Now().AddHours(1) });
        await (await manager.PostAsJsonAsync("/api/v1/marketing/invitations", new { name = "Past", expiresAt = api.Now().AddHours(-1) }))
            .ShouldFailAsync(400, "invitation.expiry_in_past");

        await (await Anonymous().GetAsync($"/api/v1/public/invitations/{inactive.GetProperty("code").GetString()}")).ShouldFailAsync(404);
        await RegisterAsync(inactive.GetProperty("code").GetString());
        Assert.Equal(0, await api.WithDbAsync(db => db.Set<InvitationLink>().Where(i => i.Code == inactive.GetProperty("code").GetString())
            .Select(i => i.UseCount).FirstAsync()));

        var expCode = expiring.GetProperty("code").GetString()!;
        (await Anonymous().GetAsync($"/api/v1/public/invitations/{expCode}")).EnsureSuccessStatusCode();
        await api.WithDbAsync(db => db.Set<InvitationLink>().Where(i => i.Code == expCode)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, api.Now().AddMinutes(-1))));
        await (await Anonymous().GetAsync($"/api/v1/public/invitations/{expCode}")).ShouldFailAsync(404);

        // Delete: a visited link is deactivated (keeps stats); an unused one is removed.
        var del = await (await manager.DeleteAsync($"/api/v1/marketing/invitations/{expiring.GetProperty("id").GetGuid()}")).ReadJsonAsync();
        Assert.True(del.GetProperty("deactivated").GetBoolean());
        var unused = await manager.PostJsonAsync("/api/v1/marketing/invitations", new { name = "Unused" });
        var del2 = await (await manager.DeleteAsync($"/api/v1/marketing/invitations/{unused.GetProperty("id").GetGuid()}")).ReadJsonAsync();
        Assert.True(del2.GetProperty("deleted").GetBoolean());
    }

    [Fact]
    public async Task Campaign_invitation_shows_the_campaign_landing_only_while_it_is_live()
    {
        var (managerUser, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var active = await api.CreateCampaignAsync(managerUser.Id, visibility: CampaignVisibility.InviteOnly,
            landingHeadline: "Share our spring drop", baseRate: 3.25m, platforms: new[] { OptimizeAll.Domain.Common.SocialPlatform.TikTok, OptimizeAll.Domain.Common.SocialPlatform.Instagram });
        var invite = await manager.PostJsonAsync("/api/v1/marketing/invitations", new { name = "VIP", campaignId = active.Id });
        Assert.Equal(active.Title, invite.GetProperty("campaignTitle").GetString());

        var landing = await (await Anonymous().GetAsync($"/api/v1/public/invitations/{invite.GetProperty("code").GetString()}")).ReadJsonAsync();
        Assert.Equal("campaign", landing.GetProperty("type").GetString());
        Assert.Equal("Share our spring drop", landing.GetProperty("headline").GetString());
        Assert.Equal(active.Summary, landing.GetProperty("body").GetString()); // falls back to the summary
        var campaign = landing.GetProperty("campaign");
        Assert.Equal(active.Slug, campaign.GetProperty("slug").GetString());
        Assert.Equal(new[] { "Instagram", "TikTok" }, campaign.GetProperty("platforms").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(3.25m, campaign.GetProperty("reward").GetProperty("baseAmount").GetDecimal());
        Assert.Equal("USD", campaign.GetProperty("reward").GetProperty("currency").GetString());

        await api.WithDbAsync(db => db.Set<Campaign>().Where(c => c.Id == active.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CampaignStatus.Paused)));
        await (await Anonymous().GetAsync($"/api/v1/public/invitations/{invite.GetProperty("code").GetString()}")).ShouldFailAsync(404);

        await (await manager.PostAsJsonAsync("/api/v1/marketing/invitations", new { name = "Bad", campaignId = Guid.NewGuid() }))
            .ShouldFailAsync(400, "invitation.campaign_not_found");
    }

    [Fact]
    public async Task Public_campaign_landing_serves_only_live_public_campaigns()
    {
        var manager = await api.CreateUserAsync(new[] { Role.CampaignManager });
        var live = await api.CreateCampaignAsync(manager.Id, landingHeadline: null, landingBody: "Custom landing body");
        var draft = await api.CreateCampaignAsync(manager.Id, status: CampaignStatus.Draft);
        var inviteOnly = await api.CreateCampaignAsync(manager.Id, visibility: CampaignVisibility.InviteOnly);
        var scheduled = await api.CreateCampaignAsync(manager.Id, status: CampaignStatus.Scheduled);

        var page = await (await Anonymous().GetAsync($"/api/v1/public/campaigns/{live.Slug}")).ReadJsonAsync();
        Assert.Equal(live.Title, page.GetProperty("headline").GetString()); // falls back to the title
        Assert.Equal("Custom landing body", page.GetProperty("body").GetString());
        Assert.Equal("https://cdn.example.com/hero.jpg", page.GetProperty("heroImageUrl").GetString());
        Assert.Equal("#ad", page.GetProperty("disclosure").GetString());
        var assets = page.GetProperty("assets").EnumerateArray().ToList();
        Assert.Single(assets); // images only (the caption asset is not public landing content)
        Assert.Equal(2.5m, page.GetProperty("reward").GetProperty("baseAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("experiment").ValueKind);

        (await Anonymous().GetAsync($"/api/v1/public/campaigns/{scheduled.Slug}")).EnsureSuccessStatusCode();
        await (await Anonymous().GetAsync($"/api/v1/public/campaigns/{draft.Slug}")).ShouldFailAsync(404, "campaign.not_found");
        await (await Anonymous().GetAsync($"/api/v1/public/campaigns/{inviteOnly.Slug}")).ShouldFailAsync(404, "campaign.not_found");
    }

    [Fact]
    public async Task Landing_page_experiment_applies_sticky_variants_to_identified_visitors_only()
    {
        var (managerUser, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var campaign = await api.CreateCampaignAsync(managerUser.Id, landingHeadline: "Base headline");
        var experiment = await manager.PostJsonAsync("/api/v1/marketing/experiments", new
        {
            campaignId = campaign.Id, name = "Landing headline test", element = "LandingPage",
            variants = new object[]
            {
                new { key = "A", name = "Control", weight = 50, landingHeadline = "Headline A" },
                new { key = "B", name = "Urgency", weight = 50, landingHeadline = "Headline B", landingBody = "Only this week" },
            },
        });
        var id = experiment.GetProperty("id").GetGuid();
        (await manager.PostAsync($"/api/v1/marketing/experiments/{id}/start", null)).EnsureSuccessStatusCode();

        for (var i = 0; i < 12; i++)
        {
            var visitor = $"visitor-{i}";
            var request = () =>
            {
                var r = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/public/campaigns/{campaign.Slug}");
                r.Headers.Add("X-Visitor-Id", visitor);
                return r;
            };
            var first = await (await Anonymous().SendAsync(request())).ReadJsonAsync();
            var second = await (await Anonymous().SendAsync(request())).ReadJsonAsync();
            var key = first.GetProperty("experiment").GetProperty("key").GetString()!;
            Assert.Equal(key, second.GetProperty("experiment").GetProperty("key").GetString());
            Assert.Equal($"Headline {key}", first.GetProperty("headline").GetString());
        }
        Assert.Equal(12, await api.WithDbAsync(db => db.Set<ExperimentAssignment>().CountAsync(a => a.ExperimentId == id)));
        Assert.All(await api.WithDbAsync(db => db.Set<ExperimentAssignment>().Where(a => a.ExperimentId == id).Select(a => a.SubjectKey).ToListAsync()),
            k => Assert.Matches("^visitor:[0-9a-f]{64}$", k)); // the raw visitor id is never stored

        var anonymous = await (await Anonymous().GetAsync($"/api/v1/public/campaigns/{campaign.Slug}")).ReadJsonAsync();
        Assert.Equal("Base headline", anonymous.GetProperty("headline").GetString());
        Assert.Equal(JsonValueKind.Null, anonymous.GetProperty("experiment").ValueKind);
        Assert.Equal(12, await api.WithDbAsync(db => db.Set<ExperimentAssignment>().CountAsync(a => a.ExperimentId == id)));
    }
}
