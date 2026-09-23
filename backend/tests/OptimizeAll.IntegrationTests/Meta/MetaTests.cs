using System.Net;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Settings;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Marketing;

namespace OptimizeAll.IntegrationTests.Meta;

public sealed class MetaTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Currencies_are_listed_anonymously_with_minor_units()
    {
        var currencies = await (await api.CreateClient().GetAsync("/api/v1/meta/currencies")).ReadJsonAsync();
        var items = currencies.EnumerateArray()
            .ToDictionary(c => c.GetProperty("code").GetString()!, c => c.GetProperty("minorUnits").GetInt32());

        Assert.Equal(Money.SupportedCurrencies.Count, items.Count);
        Assert.All(Money.SupportedCurrencies, code => Assert.True(items.ContainsKey(code.ToUpperInvariant())));
        Assert.Equal(items.Keys.Order(StringComparer.Ordinal), items.Keys);
        Assert.Equal(2, items["USD"]);
        Assert.Equal(0, items["JPY"]);
        Assert.Equal(3, items["KWD"]);
    }

    [Fact]
    public async Task Eligibility_defaults_follow_the_admin_settings_and_need_a_signed_in_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/v1/meta/eligibility-defaults")).StatusCode);

        var (_, participant) = await api.CreateClientAsync();
        var defaults = await (await participant.GetAsync("/api/v1/meta/eligibility-defaults")).ReadJsonAsync();
        Assert.Equal(90, defaults.GetProperty("minAccountAgeDays").GetInt32());
        Assert.Equal(0, defaults.GetProperty("minFollowers").GetInt32());

        await api.SetSettingAsync(SettingKeys.MinAccountAgeDays, 120);
        await api.SetSettingAsync(SettingKeys.MinFollowers, 250);
        var (_, manager) = await api.CreateClientAsync(OptimizeAll.Domain.Identity.Role.CampaignManager);
        defaults = await (await manager.GetAsync("/api/v1/meta/eligibility-defaults")).ReadJsonAsync();
        Assert.Equal(120, defaults.GetProperty("minAccountAgeDays").GetInt32());
        Assert.Equal(250, defaults.GetProperty("minFollowers").GetInt32());
    }
}
