using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Social;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Campaigns;

public sealed record Participant(TestUser User, HttpClient Client, Guid AccountId, SocialPlatform Platform);

public sealed record CreatedCampaign(Guid Id, string Slug);

/// <summary>Helpers shared by the campaign, submission and review integration tests.</summary>
public sealed class CampaignTestKit(ApiFactory api)
{
    public DateTime Now => api.Clock.GetUtcNow().UtcDateTime;

    public Task<(TestUser User, HttpClient Client)> ManagerAsync() => api.CreateClientAsync(Role.CampaignManager);

    public Task<(TestUser User, HttpClient Client)> ReviewerAsync() => api.CreateClientAsync(Role.Reviewer);

    public Dictionary<string, object?> CampaignBody(string? title = null, decimal baseAmount = 5m, object[]? extraRules = null)
    {
        var now = Now;
        var rules = new List<object> { new { type = "BaseRate", amount = baseAmount } };
        if (extraRules is not null) rules.AddRange(extraRules);
        return new Dictionary<string, object?>
        {
            ["title"] = title ?? "Campaign " + Guid.NewGuid().ToString("N")[..10],
            ["summary"] = "Share our autumn launch with your followers",
            ["description"] = "Full description of the campaign.",
            ["topics"] = new[] { "fitness", "tech" },
            ["visibility"] = "Public",
            ["startsAt"] = now.AddDays(-1),
            ["endsAt"] = now.AddDays(10),
            ["submissionDeadline"] = now.AddDays(12),
            ["timeZone"] = "UTC",
            ["postingInstructions"] = "Post the approved image with the caption and tag us.",
            ["defaultDisclosureText"] = "#ad",
            ["requiredHashtags"] = "#optimizeall",
            ["maxSubmissionsPerParticipant"] = 5,
            ["minPostLiveHours"] = 0,
            ["requireScreenshot"] = true,
            ["eligibility"] = new { minFollowers = 0 },
            ["platforms"] = new[] { "Instagram", "TikTok" },
            ["rewardRules"] = new Dictionary<string, object?> { ["currency"] = "USD", ["rules"] = rules },
        };
    }

    public async Task<CreatedCampaign> CreateCampaignAsync(HttpClient manager, Dictionary<string, object?>? body = null, bool publish = true)
    {
        var created = await (await manager.PostAsJsonAsync("/api/v1/admin/campaigns", body ?? CampaignBody())).ReadJsonAsync();
        var id = created.GetProperty("id").GetGuid();
        if (publish)
            await (await manager.PostAsync($"/api/v1/admin/campaigns/{id}/publish", null)).ReadJsonAsync();
        return new CreatedCampaign(id, created.GetProperty("slug").GetString()!);
    }

    public async Task<Participant> ParticipantAsync(SocialPlatform platform = SocialPlatform.Instagram, int accountAgeDays = 400,
        string country = "PK", IEnumerable<string>? interests = null, ParticipantTier tier = ParticipantTier.Standard,
        SocialAccountVerificationStatus verification = SocialAccountVerificationStatus.Verified)
    {
        var user = await api.CreateUserAsync(countryCode: country, interests: interests, tier: tier);
        var accountId = await AddAccountAsync(user.Id, platform, accountAgeDays, verification);
        return new Participant(user, await api.LoginAsync(user), accountId, platform);
    }

    public async Task<Guid> AddAccountAsync(Guid userId, SocialPlatform platform, int accountAgeDays = 400,
        SocialAccountVerificationStatus verification = SocialAccountVerificationStatus.Verified)
    {
        var handle = "h" + Guid.NewGuid().ToString("N")[..12];
        var account = new SocialAccount
        {
            UserId = userId, Platform = platform, Handle = handle, NormalizedHandle = handle,
            ProfileUrl = $"https://example.test/{handle}", AccountCreatedAt = Now.AddDays(-accountAgeDays), FollowerCount = 5000,
            VerificationStatus = verification, IsActive = true,
        };
        await api.WithDbAsync(async db =>
        {
            db.Add(account);
            await db.SaveChangesAsync();
        });
        return account.Id;
    }

    public static string InstagramUrl() => $"https://www.instagram.com/p/{Guid.NewGuid():N}/";

    /// <summary>A PNG header of the given size followed by a random payload (unique SHA-256 per call).</summary>
    public static byte[] Png(int width = 400, int height = 400)
    {
        var bytes = new byte[33 + 64];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        bytes[24] = 8;
        bytes[25] = 2;
        Random.Shared.NextBytes(bytes.AsSpan(33));
        return bytes;
    }

    public static MultipartFormDataContent SubmissionForm(Guid campaignId, Guid accountId, string url, DateTime postedAt,
        string platform = "Instagram", byte[]? screenshot = null, string? caption = null, bool includeScreenshot = true,
        string screenshotName = "shot.png", string screenshotType = "image/png")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(campaignId.ToString()), "campaignId" },
            { new StringContent(accountId.ToString()), "socialAccountId" },
            { new StringContent(platform), "platform" },
            { new StringContent(url), "postUrl" },
            { new StringContent(postedAt.ToString("O")), "postedAt" },
        };
        if (caption is not null) form.Add(new StringContent(caption), "captionText");
        if (includeScreenshot)
        {
            var file = new ByteArrayContent(screenshot ?? Png());
            file.Headers.ContentType = new MediaTypeHeaderValue(screenshotType);
            form.Add(file, "screenshot", screenshotName);
        }
        return form;
    }

    public Task<HttpResponseMessage> SubmitAsync(Participant p, Guid campaignId, string? url = null, DateTime? postedAt = null,
        byte[]? screenshot = null, string? caption = null) =>
        p.Client.PostAsync("/api/v1/me/submissions",
            SubmissionForm(campaignId, p.AccountId, url ?? InstagramUrl(), postedAt ?? Now.AddHours(-1), p.Platform.ToString(), screenshot, caption));

    public async Task<Guid> SubmitOkAsync(Participant p, Guid campaignId, string? url = null, DateTime? postedAt = null,
        byte[]? screenshot = null, string? caption = null) =>
        (await (await SubmitAsync(p, campaignId, url, postedAt, screenshot, caption)).ReadJsonAsync()).GetProperty("id").GetGuid();

    public static async Task<Guid> StampAsync(HttpClient reviewer, Guid submissionId) =>
        (await (await reviewer.GetAsync($"/api/v1/review/submissions/{submissionId}")).ReadJsonAsync())
        .GetProperty("submission").GetProperty("concurrencyStamp").GetGuid();

    public static async Task<HttpResponseMessage> DecideAsync(HttpClient reviewer, Guid submissionId, string decision,
        string? reason = null, decimal? quality = null, Guid? stamp = null)
    {
        stamp ??= await StampAsync(reviewer, submissionId);
        return await reviewer.PostAsJsonAsync($"/api/v1/review/submissions/{submissionId}/decision",
            new { decision, reason, qualityBonusAmount = quality, concurrencyStamp = stamp });
    }

    public Task<List<EarningEntry>> EarningsAsync(Guid submissionId) =>
        api.WithDbAsync(db => db.Set<EarningEntry>().AsNoTracking().Where(e => e.SubmissionId == submissionId)
            .OrderBy(e => e.CreatedAt).ToListAsync());

    /// <summary>
    /// Runs a job through the real JobRunner. The runner keys each run by the clock's millisecond, so the frozen test
    /// clock is nudged forward one second first to keep consecutive runs distinct.
    /// </summary>
    public async Task RunJobAsync<TJob>() where TJob : OptimizeAll.Api.Common.Jobs.IJob
    {
        api.Clock.Advance(TimeSpan.FromSeconds(1));
        var run = await api.RunJobAsync<TJob>();
        Assert.NotNull(run);
        Assert.Equal(OptimizeAll.Domain.Jobs.JobRunStatus.Succeeded, run!.Status);
    }

    public static async Task<JsonElement> GetJsonAsync(HttpClient client, string url) => await (await client.GetAsync(url)).ReadJsonAsync();
}
