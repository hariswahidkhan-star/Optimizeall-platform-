using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Ads;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.SocialMedia;

namespace OptimizeAll.IntegrationTests.Ads;

[Collection(SocialAdsCollection.Name)]
public sealed class AdsTests(ApiFactory api)
{
    private static async Task<Guid> AccountAsync(HttpClient http, Guid clientId, AdPlatform platform, string currency, string external)
    {
        var res = await http.PostAsJsonAsync("/api/v1/agency/ads/accounts", new
        {
            clientAccountId = clientId, platform = platform.ToString(), externalAccountId = external, name = $"{platform} {external}", currency, timeZone = "Europe/London",
        });
        return (await res.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static string GoogleCsv(DateOnly day) =>
        "Campaign report\r\n\"All time\"\r\nDay,Campaign,Campaign ID,Currency code,Cost,Impr.,Clicks,Conversions,Conv. value\r\n" +
        $"{day:yyyy-MM-dd},Brand,901,USD,\"1,000.00\",\"20,000\",400,20.00,\"4,000.00\"\r\n" +
        $"{day.AddDays(1):yyyy-MM-dd},Brand,901,USD,500.00,10000,200,0,0\r\n" +
        $"{day.AddDays(1):yyyy-MM-dd},Generic,902,USD,250.00,5000,100,5.00,500.00\r\n";

    [Fact]
    public async Task Csv_import_maps_validates_and_is_idempotent_and_kpis_convert_to_the_client_currency()
    {
        var client = await SocialAdsKit.CreateClientAsync(api, currency: "GBP", country: "GB", zone: "Europe/London");
        var (_, ads) = await api.CreateClientAsync(Role.AdsSpecialist);
        var account = await AccountAsync(ads, client.Id, AdPlatform.GoogleAds, "USD", "111-222-3333");
        var day = new DateOnly(2025, 3, 10);
        await api.WithDbAsync(async db =>
        {
            db.Set<ExchangeRate>().Add(new ExchangeRate { BaseCurrency = "GBP", QuoteCurrency = "USD", Rate = 1.25m, EffectiveAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        var preview = await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/accounts/{account}/import/preview",
            new { template = "google-ads", fileName = "report.csv", csv = GoogleCsv(day) })).ReadJsonAsync();
        Assert.Equal(3, preview.GetProperty("headerRow").GetInt32());
        Assert.Equal(3, preview.GetProperty("validRows").GetInt32());
        Assert.Equal(0, preview.GetProperty("errors").GetArrayLength());
        var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(preview.GetProperty("mapping").GetRawText())!;
        Assert.Equal("Cost", mapping["spend"]);

        var body = new { template = "google-ads", fileName = "report.csv", csv = GoogleCsv(day), mapping };
        var first = await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/accounts/{account}/import", body)).ReadJsonAsync();
        Assert.Equal(3, first.GetProperty("rowsImported").GetInt32());
        Assert.Equal("Measured", first.GetProperty("sourceLabel").GetString());
        var again = await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/accounts/{account}/import", body)).ReadJsonAsync();
        Assert.Equal(0, again.GetProperty("rowsImported").GetInt32());
        Assert.Equal(3, again.GetProperty("rowsUpdated").GetInt32());
        Assert.Equal(3, await api.WithDbAsync(db => db.Set<AdDailyMetric>().CountAsync(m => m.AdAccountId == account)));
        Assert.Equal(2, await api.WithDbAsync(db => db.Set<AdCampaign>().CountAsync(c => c.AdAccountId == account)));

        // Invalid rows: nothing written unless partial import is allowed.
        var bad = GoogleCsv(day) + "2025-03-12,Brand,901,EUR,10,100,5,0,0\r\n";
        await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/accounts/{account}/import", new { template = "google-ads", fileName = "r.csv", csv = bad, mapping }))
            .ShouldFailAsync(400, "import.invalid");
        Assert.Equal(3, await api.WithDbAsync(db => db.Set<AdDailyMetric>().CountAsync(m => m.AdAccountId == account)));

        // KPIs in GBP, originals kept in USD.
        var kpis = await (await ads.GetAsync($"/api/v1/agency/ads/clients/{client.Id}/kpis?from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}")).ReadJsonAsync();
        Assert.Equal("GBP", kpis.GetProperty("reportingCurrency").GetString());
        Assert.Equal(1400m, kpis.GetProperty("totals").GetProperty("spend").GetDecimal()); // 1750 USD / 1.25
        Assert.Equal(3600m, kpis.GetProperty("totals").GetProperty("conversionValue").GetDecimal());
        var platform = kpis.GetProperty("platforms")[0];
        var original = platform.GetProperty("original")[0];
        Assert.Equal("USD", original.GetProperty("currency").GetString());
        Assert.Equal(1750m, original.GetProperty("totals").GetProperty("spend").GetDecimal());
        Assert.Equal(56m, kpis.GetProperty("kpis").GetProperty("cpa").GetDecimal()); // 1400 / 25
        Assert.Equal(2.5714m, kpis.GetProperty("blendedRoas").GetDecimal());
        Assert.Empty(kpis.GetProperty("fxMissing").EnumerateArray());
        var stored = await api.WithDbAsync(db => db.Set<AdDailyMetric>().Where(m => m.AdAccountId == account).SumAsync(m => m.Spend));
        Assert.Equal(1750m, stored);

        var detail = await (await ads.GetAsync($"/api/v1/agency/ads/accounts/{account}?from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}")).ReadJsonAsync();
        var brand = detail.GetProperty("campaigns").EnumerateArray().First(c => c.GetProperty("name").GetString() == "Brand");
        Assert.Equal(new[] { 1000m, 500m }, brand.GetProperty("spendSparkline").EnumerateArray().Select(v => v.GetDecimal()).ToArray());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("campaigns").EnumerateArray().First(c => c.GetProperty("name").GetString() == "Brand")
            .GetProperty("kpis").GetProperty("frequency").ValueKind);

        // Sync without credentials reports NotConfigured and writes nothing.
        var sync = await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/accounts/{account}/sync", new { })).ReadJsonAsync();
        Assert.Equal("NotConfigured", sync.GetProperty("outcome").GetString());
        Assert.Equal(3, await api.WithDbAsync(db => db.Set<AdDailyMetric>().CountAsync(m => m.AdAccountId == account)));
    }

    [Fact]
    public async Task Pacing_board_and_alerts_are_deduplicated_and_notify_the_account_team()
    {
        // Evaluate in the middle of a month so "month to date" has enough days.
        var now = api.Clock.GetUtcNow().UtcDateTime;
        var target = new DateTime(now.Year, now.Month, 16, 6, 0, 0, DateTimeKind.Utc);
        if (target <= now) target = target.AddMonths(1);
        api.Clock.SetUtcNow(target);

        var (managerUser, _) = await api.CreateClientAsync(Role.AccountManager);
        var client = await SocialAdsKit.CreateClientAsync(api, accountManager: managerUser.Id);
        var (_, ads) = await api.CreateClientAsync(Role.AdsSpecialist);
        var account = await AccountAsync(ads, client.Id, AdPlatform.MetaAds, "USD", "act_" + Guid.NewGuid().ToString("N")[..8]);
        var month = new DateOnly(target.Year, target.Month, 1);
        await api.WithDbAsync(async db =>
        {
            var campaign = new AdCampaign { ClientAccountId = client.Id, AdAccountId = account, Name = "Prospecting", ExternalId = "c1", Currency = "USD", Source = AdEntitySource.Imported };
            db.Add(campaign);
            for (var d = 0; d < 15; d++)
                db.Add(new AdDailyMetric
                {
                    ClientAccountId = client.Id, AdAccountId = account, Platform = AdPlatform.MetaAds, Level = AdLevel.Campaign, EntityKey = "c1", EntityName = "Prospecting",
                    CampaignId = campaign.Id, Date = month.AddDays(d), Currency = "USD", Spend = 200m, Impressions = 10_000, Clicks = 100, Conversions = 0,
                    Source = AdMetricSource.CsvImport, UpdatedAt = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        });
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        var budget = await (await ads.PostAsJsonAsync("/api/v1/agency/ads/budgets", new
        {
            clientAccountId = client.Id, month = month.ToString("yyyy-MM-dd"), amount = 100m * days, currency = "USD", targetCpa = 50m,
        })).ReadJsonAsync();
        Assert.Equal("Over", budget.GetProperty("pacing").GetProperty("state").GetString());
        Assert.Equal(3000m, budget.GetProperty("pacing").GetProperty("actualToDate").GetDecimal());
        Assert.Equal(1500m, budget.GetProperty("pacing").GetProperty("expectedToDate").GetDecimal());
        Assert.Equal(2m, budget.GetProperty("pacing").GetProperty("pacingRatio").GetDecimal());

        var board = await (await ads.GetAsync($"/api/v1/agency/ads/pacing?clientId={client.Id}")).ReadJsonAsync();
        Assert.Single(board.EnumerateArray());

        await api.RunJobAsync<AdsAlertJob>();
        await api.RunJobAsync<AdsAlertJob>();
        var alerts = await api.WithDbAsync(db => db.Set<AdAlert>().Where(a => a.ClientAccountId == client.Id).ToListAsync());
        Assert.Single(alerts, a => a.Kind == AdAlertKind.OverPacing);
        Assert.Single(alerts, a => a.Kind == AdAlertKind.SpendWithoutConversions);
        Assert.DoesNotContain(alerts, a => a.Kind == AdAlertKind.CpaAboveTarget); // no conversions: CPA undefined, not "above target"
        var notified = await api.WithDbAsync(db => db.Set<Notification>().CountAsync(n => n.UserId == managerUser.Id && n.Type == "ads.alert"));
        Assert.Equal(alerts.Count, notified);

        var list = await (await ads.GetAsync($"/api/v1/agency/ads/alerts?clientId={client.Id}&status=Open")).ReadJsonAsync();
        var first = list[0].GetProperty("id").GetGuid();
        var acked = await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/alerts/{first}/acknowledge", new { })).ReadJsonAsync();
        Assert.Equal("Acknowledged", acked.GetProperty("status").GetString());
        await api.RunJobAsync<AdsAlertJob>();
        Assert.Equal(alerts.Count, await api.WithDbAsync(db => db.Set<AdAlert>().CountAsync(a => a.ClientAccountId == client.Id)));
    }

    [Fact]
    public async Task Naming_convention_is_enforced_for_planned_campaigns_and_utm_links_are_built()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var (_, ads) = await api.CreateClientAsync(Role.AdsSpecialist);
        var account = await AccountAsync(ads, client.Id, AdPlatform.GoogleAds, "USD", "999-888-" + Random.Shared.Next(1000, 9999));
        (await ads.PutAsJsonAsync($"/api/v1/agency/ads/clients/{client.Id}/settings", new
        {
            campaignNamingTemplate = "{client}_{platform}_{objective}_{name}", defaultUtmSource = "{platform}", defaultUtmMedium = "cpc", lowercaseUtm = true,
        })).EnsureSuccessStatusCode();
        await (await ads.PutAsJsonAsync($"/api/v1/agency/ads/clients/{client.Id}/settings", new { campaignNamingTemplate = "{client}_{bogus}", defaultUtmSource = "x", defaultUtmMedium = "y" }))
            .ShouldFailAsync(400, "ads.naming_template_invalid");

        await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/accounts/{account}/campaigns", new { name = "Brand Search", objective = "conversions" }))
            .ShouldFailAsync(400, "ads.naming_convention");
        var good = await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/accounts/{account}/campaigns",
            new { name = $"{client.Slug}_google_conversions_brand-search", objective = "conversions", budgetAmount = 50, targetCpa = 20 })).ReadJsonAsync();
        Assert.True(good.GetProperty("namingCompliant").GetBoolean());
        Assert.Equal("Plan", good.GetProperty("source").GetString());

        var generated = await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/clients/{client.Id}/naming/generate",
            new { platform = "MetaAds", objective = "Lead Gen", name = "Winter Sale" })).ReadJsonAsync();
        Assert.Equal($"{client.Slug}_meta_lead-gen_winter-sale", generated.GetProperty("suggested").GetString());
        Assert.True(generated.GetProperty("compliant").GetBoolean());

        var utm = await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/clients/{client.Id}/utm",
            new { url = "https://example.com/landing?ref=1", campaign = "Winter Sale", platform = "MetaAds" })).ReadJsonAsync();
        Assert.Equal("https://example.com/landing?ref=1&utm_source=meta&utm_medium=cpc&utm_campaign=winter-sale", utm.GetProperty("taggedUrl").GetString());
        var history = await (await ads.GetAsync($"/api/v1/agency/ads/clients/{client.Id}/utm")).ReadJsonAsync();
        Assert.Single(history.EnumerateArray());
    }

    [Fact]
    public async Task Ads_tenant_isolation_creatives_and_experiments()
    {
        var clientA = await SocialAdsKit.CreateClientAsync(api);
        var (_, ads) = await api.CreateClientAsync(Role.AdsSpecialist);
        var (_, memberA) = await SocialAdsKit.ClientUserAsync(api, clientA.Id, ClientMemberRole.Owner);
        await (await memberA.GetAsync("/api/v1/agency/ads/accounts")).ShouldFailAsync(403);

        var creative = await (await ads.PostAsJsonAsync("/api/v1/agency/ads/creatives", new
        {
            clientAccountId = clientA.Id, name = "RSA", platform = "GoogleAds", format = "ResponsiveSearch",
            headlines = new[] { "One", "Two", new string('x', 31) }, descriptions = new[] { "First description", "Second description" },
        })).ReadJsonAsync();
        Assert.Contains(creative.GetProperty("issues").EnumerateArray(), i => i.GetProperty("severity").GetString() == "Error");
        var id = creative.GetProperty("id").GetGuid();
        await (await ads.PostAsJsonAsync($"/api/v1/agency/ads/creatives/{id}/submit", new { })).ShouldFailAsync(400, "ads.copy_invalid");

        var experiment = await (await ads.PostAsJsonAsync("/api/v1/agency/ads/experiments", new
        {
            clientAccountId = clientA.Id, platform = "GoogleAds", name = "Headline test", hypothesis = "Benefit beats price", metric = "ConversionRate",
            variants = new object[]
            {
                new { name = "A", isControl = true, impressions = 50000, clicks = 2000, conversions = 80 },
                new { name = "B", impressions = 50000, clicks = 2000, conversions = 130 },
            },
        })).ReadJsonAsync();
        var b = experiment.GetProperty("variants")[1];
        Assert.True(b.GetProperty("significant").GetBoolean());
        Assert.True(b.GetProperty("pValue").GetDouble() < 0.05);
        Assert.Equal("Computed", experiment.GetProperty("significanceSource").GetString());

        var (_, otherAds) = await api.CreateClientAsync(Role.Strategist);
        (await otherAds.GetAsync($"/api/v1/agency/ads/experiments?clientId={clientA.Id}")).EnsureSuccessStatusCode(); // staff see all clients
    }
}
