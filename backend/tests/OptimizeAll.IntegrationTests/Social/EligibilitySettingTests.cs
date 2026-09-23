using System.Net.Http.Json;
using OptimizeAll.IntegrationTests.Accounts;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Social;

/// <summary>Own fixture (own database): changes a platform-wide setting.</summary>
public sealed class EligibilitySettingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Changing_the_minimum_account_age_setting_changes_qualification()
    {
        var (user, client) = await api.CreateClientAsync();
        await api.AddSocialAccountAsync(user.Id, ageDays: 45);

        var before = await (await client.GetAsync("/api/v1/me/social-accounts")).ReadJsonAsync();
        Assert.Equal(90, before.GetProperty("minAccountAgeDays").GetInt32());
        Assert.False(before.GetProperty("items")[0].GetProperty("qualifies").GetBoolean());
        var home = await (await client.GetAsync("/api/v1/me/home")).ReadJsonAsync();
        Assert.Equal("AwaitingEligibility", home.GetProperty("state").GetString());

        await api.SetSettingAsync("eligibility.minAccountAgeDays", 30);

        var after = await (await client.GetAsync("/api/v1/me/social-accounts")).ReadJsonAsync();
        Assert.Equal(30, after.GetProperty("minAccountAgeDays").GetInt32());
        Assert.True(after.GetProperty("items")[0].GetProperty("qualifies").GetBoolean());
        home = await (await client.GetAsync("/api/v1/me/home")).ReadJsonAsync();
        Assert.Equal("Ready", home.GetProperty("state").GetString());

        // Follower minimum applies the same way.
        await api.SetSettingAsync("eligibility.minFollowers", 5000);
        var withFollowers = await (await client.GetAsync("/api/v1/me/social-accounts")).ReadJsonAsync();
        var item = withFollowers.GetProperty("items")[0];
        Assert.False(item.GetProperty("qualifies").GetBoolean());
        Assert.Contains(item.GetProperty("reasons").EnumerateArray(), r => r.GetProperty("code").GetString() == "social.followers_below_minimum");

        await api.SetSettingAsync("eligibility.minFollowers", 0);
    }
}
