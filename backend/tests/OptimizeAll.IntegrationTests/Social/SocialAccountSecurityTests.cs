using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Social;
using OptimizeAll.IntegrationTests.Accounts;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Social;

/// <summary>Regression tests for verification integrity and the active-profile limit.</summary>
public sealed class SocialAccountSecurityTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static string Handle() => "user" + Guid.NewGuid().ToString("N")[..10];

    private object NewAccount(string handle, string platform = "Instagram", string? url = null) => new
    {
        platform,
        handle = "@" + handle,
        profileUrl = url ?? $"https://www.instagram.com/{handle}/",
        accountCreatedAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(-300),
        followerCount = 2500,
    };

    private static object Edit(JsonElement account, string profileUrl) => new
    {
        handle = account.GetProperty("handle").GetString(),
        profileUrl,
        accountCreatedAt = account.GetProperty("accountCreatedAt").GetDateTime(),
        followerCount = account.GetProperty("followerCount").GetInt32(),
        concurrencyStamp = account.GetProperty("concurrencyStamp").GetGuid(),
    };

    private async Task<JsonElement> VerifiedAccountAsync(HttpClient owner, string handle)
    {
        var account = await (await owner.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(handle))).ReadJsonAsync();
        var id = account.GetProperty("id").GetGuid();
        var pending = await (await owner.PostAsync($"/api/v1/me/social-accounts/{id}/request-verification", null)).ReadJsonAsync();
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        await (await reviewer.PostAsJsonAsync($"/api/v1/review/social-accounts/{id}/decision", new
        {
            decision = "Verified", concurrencyStamp = pending.GetProperty("concurrencyStamp").GetGuid(),
        })).ReadJsonAsync();
        var mine = await (await owner.GetAsync("/api/v1/me/social-accounts")).ReadJsonAsync();
        var verified = mine.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id);
        Assert.Equal("Verified", verified.GetProperty("verificationStatus").GetString());
        return verified;
    }

    // ---------- Finding 2: profile URL is a verified fact and must match the handle ----------

    [Fact]
    public async Task Changing_the_profile_url_of_a_verified_account_resets_verification()
    {
        var (_, owner) = await api.CreateClientAsync();
        var handle = Handle();
        var verified = await VerifiedAccountAsync(owner, handle);
        var id = verified.GetProperty("id").GetGuid();

        // A cosmetically different link to the same profile (no www., no trailing slash) is not a change.
        var same = await (await owner.PutAsJsonAsync($"/api/v1/me/social-accounts/{id}", Edit(verified, $"https://instagram.com/{handle}")))
            .ReadJsonAsync();
        Assert.False(same.GetProperty("verificationReset").GetBoolean());
        Assert.Equal("Verified", same.GetProperty("account").GetProperty("verificationStatus").GetString());

        // A different link is a new fact the reviewer never checked.
        var changed = await (await owner.PutAsJsonAsync($"/api/v1/me/social-accounts/{id}",
            Edit(same.GetProperty("account"), $"https://www.instagram.com/{handle}/reels/"))).ReadJsonAsync();
        Assert.True(changed.GetProperty("verificationReset").GetBoolean());
        Assert.Equal("Unverified", changed.GetProperty("account").GetProperty("verificationStatus").GetString());
        var stored = await api.WithDbAsync(db => db.Set<SocialAccount>().AsNoTracking().FirstAsync(a => a.Id == id));
        Assert.Null(stored.VerifiedByUserId);
        Assert.Null(stored.VerifiedAt);
    }

    [Theory]
    [InlineData("Instagram", "https://www.instagram.com/someoneelse/")]
    [InlineData("TikTok", "https://www.tiktok.com/@someoneelse")]
    [InlineData("X", "https://x.com/someoneelse")]
    [InlineData("Threads", "https://www.threads.net/@someoneelse")]
    [InlineData("YouTube", "https://www.youtube.com/@someoneelse")]
    [InlineData("Pinterest", "https://www.pinterest.com/someoneelse/")]
    [InlineData("Snapchat", "https://www.snapchat.com/add/someoneelse")]
    [InlineData("Instagram", "https://www.instagram.com/")]
    public async Task Profile_url_must_contain_the_handle(string platform, string url)
    {
        var (_, client) = await api.CreateClientAsync();
        await (await client.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(Handle(), platform, url)))
            .ShouldFailAsync(400, "social.url_handle_mismatch");
    }

    [Theory]
    [InlineData("TikTok", "https://www.tiktok.com/@{0}")]
    [InlineData("YouTube", "https://m.youtube.com/@{0}?feature=share")]
    [InlineData("Snapchat", "https://www.snapchat.com/add/{0}")]
    [InlineData("Facebook", "https://www.facebook.com/profile.php?id=100012345678")]
    [InlineData("LinkedIn", "https://www.linkedin.com/in/some-member-8a1b2c")]
    public async Task Handle_in_url_is_case_insensitive_and_not_required_for_facebook_or_linkedin(string platform, string template)
    {
        var (_, client) = await api.CreateClientAsync();
        var handle = Handle();
        var created = await client.PostAsJsonAsync("/api/v1/me/social-accounts",
            NewAccount(handle, platform, string.Format(template, handle.ToUpperInvariant())));
        Assert.Equal(201, (int)created.StatusCode);
    }

    [Fact]
    public async Task Editing_the_url_to_another_profile_is_rejected()
    {
        var (_, owner) = await api.CreateClientAsync();
        var handle = Handle();
        var verified = await VerifiedAccountAsync(owner, handle);
        var id = verified.GetProperty("id").GetGuid();
        await (await owner.PutAsJsonAsync($"/api/v1/me/social-accounts/{id}", Edit(verified, "https://www.instagram.com/famous.brand/")))
            .ShouldFailAsync(400, "social.url_handle_mismatch");
        var stored = await api.WithDbAsync(db => db.Set<SocialAccount>().AsNoTracking().FirstAsync(a => a.Id == id));
        Assert.Equal(SocialAccountVerificationStatus.Verified, stored.VerificationStatus);
        Assert.Contains(handle, stored.ProfileUrl);
    }

    // ---------- Finding 3: reviewers can't verify their own profiles; decisions need PendingReview ----------

    [Fact]
    public async Task Staff_cannot_verify_their_own_social_account()
    {
        var (staffUser, staff) = await api.CreateClientAsync(Role.Participant, Role.Reviewer);
        var account = await (await staff.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(Handle()))).ReadJsonAsync();
        var id = account.GetProperty("id").GetGuid();
        var pending = await (await staff.PostAsync($"/api/v1/me/social-accounts/{id}/request-verification", null)).ReadJsonAsync();
        var stamp = pending.GetProperty("concurrencyStamp").GetGuid();

        await (await staff.PostAsJsonAsync($"/api/v1/review/social-accounts/{id}/decision", new
        {
            decision = "Verified", verifiedFollowerCount = 5_000_000, concurrencyStamp = stamp,
        })).ShouldFailAsync(403, "social.self_verification");

        var stored = await api.WithDbAsync(db => db.Set<SocialAccount>().AsNoTracking().FirstAsync(a => a.Id == id));
        Assert.Equal(SocialAccountVerificationStatus.PendingReview, stored.VerificationStatus);
        Assert.Equal(2500, stored.FollowerCount);
        Assert.Null(stored.VerifiedByUserId);

        // A different reviewer can decide it.
        var (_, other) = await api.CreateClientAsync(Role.Reviewer);
        var decided = await (await other.PostAsJsonAsync($"/api/v1/review/social-accounts/{id}/decision", new
        {
            decision = "Verified", concurrencyStamp = stamp,
        })).ReadJsonAsync();
        Assert.Equal("Verified", decided.GetProperty("account").GetProperty("verificationStatus").GetString());
        Assert.NotEqual(staffUser.Id, decided.GetProperty("verifiedBy").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Decisions_require_pending_review()
    {
        var (participant, _) = await api.CreateClientAsync();
        var unverified = await api.AddSocialAccountAsync(participant.Id, ageDays: 300);
        var rejected = await api.AddSocialAccountAsync(participant.Id, ageDays: 300, status: SocialAccountVerificationStatus.Rejected);
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        foreach (var account in new[] { unverified, rejected })
        {
            await (await reviewer.PostAsJsonAsync($"/api/v1/review/social-accounts/{account.Id}/decision", new
            {
                decision = "Verified", concurrencyStamp = account.ConcurrencyStamp,
            })).ShouldFailAsync(409, "social.not_pending");
        }
    }

    // ---------- Finding 5: the active-profile limit holds under concurrency ----------

    [Fact]
    public async Task Parallel_creates_cannot_exceed_the_active_account_limit()
    {
        var (user, client) = await api.CreateClientAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 15)
            .Select(_ => client.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(Handle()))));

        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        Assert.True(created <= 10, $"{created} profiles were created in parallel; the limit is 10.");
        Assert.Equal(10, created);
        foreach (var failed in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            await failed.ShouldFailAsync(409, "social.limit_reached");

        var active = await api.WithDbAsync(db => db.Set<SocialAccount>().CountAsync(a => a.UserId == user.Id && a.IsActive));
        Assert.Equal(10, active);
    }

    [Fact]
    public async Task Parallel_reactivations_cannot_exceed_the_active_account_limit()
    {
        var (user, client) = await api.CreateClientAsync();
        for (var i = 0; i < 9; i++) await api.AddSocialAccountAsync(user.Id, ageDays: 300);
        var inactive = new List<SocialAccount>();
        for (var i = 0; i < 5; i++) inactive.Add(await api.AddSocialAccountAsync(user.Id, ageDays: 300, isActive: false));

        var responses = await Task.WhenAll(inactive.Select(a => client.PostAsync($"/api/v1/me/social-accounts/{a.Id}/reactivate", null)));

        Assert.Equal(1, responses.Count(r => r.IsSuccessStatusCode));
        foreach (var failed in responses.Where(r => !r.IsSuccessStatusCode))
            await failed.ShouldFailAsync(409, "social.limit_reached");
        var active = await api.WithDbAsync(db => db.Set<SocialAccount>().CountAsync(a => a.UserId == user.Id && a.IsActive));
        Assert.Equal(10, active);
    }
}
