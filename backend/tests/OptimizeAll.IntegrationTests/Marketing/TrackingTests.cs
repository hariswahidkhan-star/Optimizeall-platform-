using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Marketing;

public sealed class TrackingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126.0 Safari/537.36";
    private const string Destination = "https://shop.example.com/landing?ref=abc&utm_source=old#top";

    private HttpClient NoRedirectClient() =>
        api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    private async Task<HttpResponseMessage> ClickAsync(string path, string userAgent)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        return await NoRedirectClient().SendAsync(request);
    }

    private async Task<(TestUser User, HttpClient Client, Guid CampaignId, JsonElement Link)> ParticipantLinkAsync()
    {
        var manager = await api.CreateUserAsync(new[] { Role.CampaignManager });
        var campaign = await api.CreateCampaignAsync(manager.Id, destination: Destination, utmCampaign: "spring-2026");
        var (user, client) = await api.CreateClientAsync();
        var link = await (await client.PostAsync($"/api/v1/me/campaigns/{campaign.Id}/tracking-link", null)).ReadJsonAsync();
        return (user, client, campaign.Id, link);
    }

    [Fact]
    public async Task Tracking_link_requires_a_destination_and_is_idempotent_per_participant()
    {
        var manager = await api.CreateUserAsync(new[] { Role.CampaignManager });
        var noTracking = await api.CreateCampaignAsync(manager.Id, destination: null);
        var (user, client) = await api.CreateClientAsync();
        await (await client.PostAsync($"/api/v1/me/campaigns/{noTracking.Id}/tracking-link", null)).ShouldFailAsync(409, "tracking.not_enabled");
        await (await client.PostAsync($"/api/v1/me/campaigns/{Guid.NewGuid()}/tracking-link", null)).ShouldFailAsync(404);

        var campaign = await api.CreateCampaignAsync(manager.Id, destination: Destination);
        var first = await (await client.PostAsync($"/api/v1/me/campaigns/{campaign.Id}/tracking-link", null)).ReadJsonAsync();
        var second = await (await client.PostAsync($"/api/v1/me/campaigns/{campaign.Id}/tracking-link", null)).ReadJsonAsync();
        var code = first.GetProperty("code").GetString()!;
        Assert.Equal(code, second.GetProperty("code").GetString());
        Assert.Equal($"http://app.test/t/{code}", first.GetProperty("shortUrl").GetString());

        var referralCode = await api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == user.Id).Select(u => u.ReferralCode).FirstAsync());
        Assert.Equal(
            $"https://shop.example.com/landing?ref=abc&utm_source=optimizeall&utm_medium=social&utm_campaign={campaign.Slug}&utm_content={referralCode}#top",
            first.GetProperty("destinationPreview").GetString());
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<TrackingLink>().CountAsync(l => l.CampaignId == campaign.Id && l.UserId == user.Id)));

        var mine = await (await client.GetAsync("/api/v1/me/tracking-links")).ReadJsonAsync();
        Assert.Single(mine.EnumerateArray());
    }

    [Fact]
    public async Task Redirect_is_302_with_merged_utm_and_records_unique_and_bot_clicks()
    {
        var (user, client, _, link) = await ParticipantLinkAsync();
        var code = link.GetProperty("code").GetString()!;
        var referralCode = await api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == user.Id).Select(u => u.ReferralCode).FirstAsync());

        var response = await ClickAsync($"/t/{code}", Browser);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(
            $"https://shop.example.com/landing?ref=abc&utm_source=optimizeall&utm_medium=social&utm_campaign=spring-2026&utm_content={referralCode}#top",
            response.Headers.Location!.OriginalString);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("no-referrer-when-downgrade", response.Headers.GetValues("Referrer-Policy").Single());

        Assert.Equal(HttpStatusCode.Found, (await ClickAsync($"/t/{code}", Browser)).StatusCode); // same visitor: not unique
        Assert.Equal(HttpStatusCode.Found, (await ClickAsync($"/t/{code}", Browser + " Mobile")).StatusCode); // new visitor
        Assert.Equal(HttpStatusCode.Found, (await ClickAsync($"/t/{code}", "facebookexternalhit/1.1")).StatusCode); // bot

        var clicks = await api.WithDbAsync(db => db.Set<TrackingClick>()
            .Where(c => c.TrackingLinkId == link.GetProperty("id").GetGuid()).ToListAsync());
        Assert.Equal(4, clicks.Count);
        Assert.All(clicks, c => Assert.Matches("^[0-9a-f]{64}$", c.VisitorHash));
        var byVisitor = clicks.GroupBy(c => c.VisitorHash).OrderByDescending(g => g.Count()).ToList();
        Assert.Equal(new[] { 2, 1, 1 }, byVisitor.Select(g => g.Count()));
        Assert.Equal(1, byVisitor[0].Count(c => c.IsUnique)); // the repeat visit is not unique
        Assert.All(byVisitor.Skip(1), g => Assert.True(g.Single().IsUnique));
        Assert.Equal(1, clicks.Count(c => c.IsSuspectedBot));
        Assert.DoesNotContain(byVisitor[0], c => c.IsSuspectedBot);

        var stats = (await (await client.GetAsync("/api/v1/me/tracking-links")).ReadJsonAsync())[0].GetProperty("stats");
        Assert.Equal(3, stats.GetProperty("clicks").GetInt32()); // bots excluded
        Assert.Equal(2, stats.GetProperty("uniqueClicks").GetInt32());
    }

    [Fact]
    public async Task Redirect_never_uses_request_supplied_destinations_and_unknown_codes_are_404()
    {
        var (_, _, _, link) = await ParticipantLinkAsync();
        var code = link.GetProperty("code").GetString()!;
        var response = await ClickAsync($"/t/{code}?url=https://evil.example.com&redirect=https://evil.example.com", Browser);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("https://shop.example.com/landing?", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain("evil", response.Headers.Location!.OriginalString);

        Assert.Equal(HttpStatusCode.NotFound, (await ClickAsync("/t/doesnotexist", Browser)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ClickAsync("/t/https%3A%2F%2Fevil.example.com", Browser)).StatusCode);
    }

    private static HttpRequestMessage Postback(string body, string? secret, string? signatureOverride = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/public/conversions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        var signature = signatureOverride ?? (secret is null ? null
            : "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant());
        if (signature is not null) request.Headers.Add("X-OA-Signature", signature);
        return request;
    }

    [Fact]
    public async Task Conversion_postback_requires_configuration_and_a_valid_signature_and_is_idempotent()
    {
        var (_, _, campaignId, link) = await ParticipantLinkAsync();
        var code = link.GetProperty("code").GetString()!;
        var client = api.CreateClient();
        var config = api.Services.GetRequiredService<IConfiguration>();
        var body = JsonSerializer.Serialize(new { code, externalReference = "order-1001", value = 49.99m, currency = "usd", occurredAt = api.Now().AddMinutes(-3) });

        config["Tracking:PostbackSecret"] = "";
        await (await client.SendAsync(Postback(body, "whatever"))).ShouldFailAsync(503, "tracking.postback_not_configured");

        const string secret = "advertiser-shared-secret";
        config["Tracking:PostbackSecret"] = secret;
        try
        {
            await (await client.SendAsync(Postback(body, secret, signatureOverride: "sha256=" + new string('0', 64))))
                .ShouldFailAsync(401, "tracking.invalid_signature");
            await (await client.SendAsync(Postback(body, null))).ShouldFailAsync(401, "tracking.invalid_signature");
            await (await client.SendAsync(Postback(body, "wrong-secret"))).ShouldFailAsync(401, "tracking.invalid_signature");
            // Tampering with the body after signing invalidates it.
            var signed = Postback(body, secret);
            var tampered = Postback(body.Replace("49.99", "4999"), secret, signed.Headers.GetValues("X-OA-Signature").Single());
            await (await client.SendAsync(tampered)).ShouldFailAsync(401, "tracking.invalid_signature");

            var created = await (await client.SendAsync(Postback(body, secret))).ReadJsonAsync();
            Assert.False(created.GetProperty("duplicate").GetBoolean());
            var duplicate = await (await client.SendAsync(Postback(body, secret))).ReadJsonAsync();
            Assert.True(duplicate.GetProperty("duplicate").GetBoolean());
            Assert.Equal(created.GetProperty("id").GetGuid(), duplicate.GetProperty("id").GetGuid());

            var unknown = JsonSerializer.Serialize(new { code = "nope", externalReference = "x", occurredAt = api.Now() });
            await (await client.SendAsync(Postback(unknown, secret))).ShouldFailAsync(404, "tracking.link_not_found");
            var invalid = JsonSerializer.Serialize(new { code, externalReference = "", occurredAt = api.Now() });
            await (await client.SendAsync(Postback(invalid, secret))).ShouldFailAsync(400, "tracking.postback_invalid");

            var conversion = await api.WithDbAsync(db => db.Set<TrackingConversion>().SingleAsync(c => c.ExternalReference == "order-1001"));
            Assert.NotNull(conversion.VerifiedAt);
            Assert.Equal("postback", conversion.Source);
            Assert.Equal(49.99m, conversion.Value);
            Assert.Equal("USD", conversion.Currency);

            // Marketing summary counts only measured, verified data.
            await ClickAsync($"/t/{code}", Browser);
            await ClickAsync($"/t/{code}", "curl/8.0");
            var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
            var summary = await (await manager.GetAsync($"/api/v1/marketing/tracking/summary?campaignId={campaignId}")).ReadJsonAsync();
            Assert.Equal(1, summary.GetProperty("clicks").GetInt32());
            Assert.Equal(1, summary.GetProperty("uniqueClicks").GetInt32());
            Assert.Equal(1, summary.GetProperty("botClicksExcluded").GetInt32());
            Assert.Equal(1, summary.GetProperty("verifiedConversions").GetInt32());
            var value = Assert.Single(summary.GetProperty("conversionValue").EnumerateArray().ToList());
            Assert.Equal("USD", value.GetProperty("currency").GetString());
            Assert.Equal(49.99m, value.GetProperty("amount").GetDecimal());
            var top = Assert.Single(summary.GetProperty("topParticipants").EnumerateArray().ToList());
            Assert.Equal(1, top.GetProperty("verifiedConversions").GetInt32());
        }
        finally
        {
            config["Tracking:PostbackSecret"] = "";
        }
    }
}
