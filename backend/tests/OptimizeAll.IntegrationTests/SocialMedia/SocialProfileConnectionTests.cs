using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.SocialMedia;

/// <summary>A connected profile publishes to its ExternalId with the stored token, so re-pointing it needs integrations.manage.</summary>
public sealed class SocialProfileConnectionTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Only_integration_managers_can_change_the_account_id_of_a_connected_profile()
    {
        var client = await SocialAdsKit.CreateClientAsync(api);
        var manager = await api.LoginAsync(await api.CreateUserAsync(new[] { Role.SocialMediaManager }));
        var admin = await api.LoginAsync(await api.CreateUserAsync(new[] { Role.Admin }));
        var profile = await SocialAdsKit.ProfileAsync(manager, client.Id, SocialNetwork.Facebook, externalId: "1122334455");

        async Task<HttpResponseMessage> UpdateAsync(HttpClient http, string externalId, string displayName)
        {
            var stamp = await api.WithDbAsync(db => db.Set<BrandProfile>().Where(p => p.Id == profile).Select(p => p.ConcurrencyStamp).SingleAsync());
            return await http.PutAsJsonAsync($"/api/v1/agency/social/profiles/{profile}", new
            {
                network = "Facebook", handle = "nimbus", displayName, externalId, concurrencyStamp = stamp,
            });
        }

        // Not connected yet: the social manager may set the id.
        Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(manager, "1122334455", "Nimbus")).StatusCode);

        await api.WithDbAsync(async db =>
        {
            var connection = new IntegrationConnection { Provider = "social-facebook", ClientAccountId = client.Id, DisplayName = "Nimbus page", EncryptedSecrets = "x" };
            db.Add(connection);
            var p = await db.Set<BrandProfile>().SingleAsync(x => x.Id == profile);
            p.IntegrationConnectionId = connection.Id;
            await db.SaveChangesAsync();
            return true;
        });

        await (await UpdateAsync(manager, "9999999999", "Nimbus")).ShouldFailAsync(403, "social.external_id_requires_integrations");
        Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(manager, "1122334455", "Nimbus Fitness")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(admin, "9999999999", "Nimbus Fitness")).StatusCode);
        Assert.Equal("9999999999", await api.WithDbAsync(db => db.Set<BrandProfile>().Where(p => p.Id == profile).Select(p => p.ExternalId).SingleAsync()));
    }
}
