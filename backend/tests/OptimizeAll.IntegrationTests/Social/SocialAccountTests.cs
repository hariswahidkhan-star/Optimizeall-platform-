using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.IntegrationTests.Accounts;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Social;

public sealed class SocialAccountTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private object NewAccount(string handle, int ageDays, string platform = "Instagram", string? url = null, int followers = 2500) => new
    {
        platform,
        handle = "@" + handle,
        profileUrl = url ?? $"https://www.instagram.com/{handle}/",
        accountCreatedAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(-ageDays),
        followerCount = followers,
        primaryLanguage = "EN",
        audienceCountryCode = "pk",
    };

    private static string Handle() => "user" + Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task New_account_does_not_qualify_until_the_minimum_age_and_old_account_qualifies()
    {
        var (user, client) = await api.CreateClientAsync();
        var handle = Handle();
        var created = await client.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(handle, ageDays: 10));
        Assert.Equal(201, (int)created.StatusCode);
        var young = await created.ReadJsonAsync();
        Assert.Equal(handle, young.GetProperty("handle").GetString());
        Assert.False(young.GetProperty("qualifies").GetBoolean());
        Assert.Equal(10, young.GetProperty("accountAgeDays").GetInt32());
        Assert.Equal("Unverified", young.GetProperty("verificationStatus").GetString());
        Assert.Equal("en", young.GetProperty("primaryLanguage").GetString());
        Assert.Equal("PK", young.GetProperty("audienceCountryCode").GetString());
        var reason = young.GetProperty("reasons")[0];
        Assert.Equal("social.account_too_new", reason.GetProperty("code").GetString());
        Assert.Contains("qualifies in 80 days", reason.GetProperty("message").GetString());
        Assert.NotEqual(JsonValueKind.Null, young.GetProperty("eligibleFrom").ValueKind);

        var old = await (await client.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(Handle(), ageDays: 200))).ReadJsonAsync();
        Assert.True(old.GetProperty("qualifies").GetBoolean());
        Assert.Empty(old.GetProperty("reasons").EnumerateArray());

        var list = await (await client.GetAsync("/api/v1/me/social-accounts")).ReadJsonAsync();
        Assert.Equal(90, list.GetProperty("minAccountAgeDays").GetInt32());
        Assert.Equal(2, list.GetProperty("items").GetArrayLength());

        // Time passes: the young profile qualifies once it reaches 90 days.
        api.Clock.Advance(TimeSpan.FromDays(81));
        var client2 = await api.LoginAsync(user); // the old access token expired with the clock jump
        list = await (await client2.GetAsync("/api/v1/me/social-accounts")).ReadJsonAsync();
        Assert.All(list.GetProperty("items").EnumerateArray(), i => Assert.True(i.GetProperty("qualifies").GetBoolean()));
    }

    [Theory]
    [InlineData("Instagram", "https://www.tiktok.com/@someone")]
    [InlineData("Instagram", "http://instagram.com/someone")]
    [InlineData("X", "https://evilx.com/someone")]
    [InlineData("YouTube", "not a url")]
    public async Task Profile_url_must_be_https_on_the_platform_host(string platform, string url)
    {
        var (_, client) = await api.CreateClientAsync();
        await (await client.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(Handle(), 100, platform, url)))
            .ShouldFailAsync(400, "social.invalid");
    }

    [Fact]
    public async Task Declared_facts_are_validated()
    {
        var (_, client) = await api.CreateClientAsync();
        var future = JsonSerializer.SerializeToNode(NewAccount(Handle(), 100))!.AsObject();
        future["accountCreatedAt"] = api.Clock.GetUtcNow().UtcDateTime.AddDays(2);
        await (await client.PostAsJsonAsync("/api/v1/me/social-accounts", future)).ShouldFailAsync(400, "social.invalid");

        var ancient = JsonSerializer.SerializeToNode(NewAccount(Handle(), 100))!.AsObject();
        ancient["accountCreatedAt"] = new DateTime(2003, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        await (await client.PostAsJsonAsync("/api/v1/me/social-accounts", ancient)).ShouldFailAsync(400, "social.invalid");

        var followers = JsonSerializer.SerializeToNode(NewAccount(Handle(), 100))!.AsObject();
        followers["followerCount"] = 2_000_000_000;
        Assert.Equal(400, (int)(await client.PostAsJsonAsync("/api/v1/me/social-accounts", followers)).StatusCode);
    }

    [Fact]
    public async Task Same_handle_on_the_same_platform_cannot_be_registered_by_two_users()
    {
        var handle = Handle();
        var (_, first) = await api.CreateClientAsync();
        (await first.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(handle, 300))).EnsureSuccessStatusCode();

        var (_, second) = await api.CreateClientAsync();
        var dup = await second.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(handle.ToUpperInvariant(), 300));
        await dup.ShouldFailAsync(409, "social.already_registered");
        var text = await dup.Content.ReadAsStringAsync();
        Assert.DoesNotContain("@example.test", text);

        // Same handle on a different platform is fine.
        (await second.PostAsJsonAsync("/api/v1/me/social-accounts",
            NewAccount(handle, 300, "TikTok", $"https://www.tiktok.com/@{handle}"))).EnsureSuccessStatusCode();

        // The owner adding it twice gets a clear message instead.
        await (await first.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(handle, 300))).ShouldFailAsync(409, "social.already_added");
    }

    [Fact]
    public async Task Other_users_accounts_are_not_found_and_lifecycle_works()
    {
        var (_, owner) = await api.CreateClientAsync();
        var account = await (await owner.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(Handle(), 300))).ReadJsonAsync();
        var id = account.GetProperty("id").GetGuid();
        var stamp = account.GetProperty("concurrencyStamp").GetGuid();

        var (_, intruder) = await api.CreateClientAsync();
        await (await intruder.PutAsJsonAsync($"/api/v1/me/social-accounts/{id}", new
        {
            handle = "stolen", profileUrl = "https://instagram.com/stolen", accountCreatedAt = DateTime.UtcNow.AddDays(-300),
            followerCount = 1, concurrencyStamp = stamp,
        })).ShouldFailAsync(404);
        await (await intruder.DeleteAsync($"/api/v1/me/social-accounts/{id}")).ShouldFailAsync(404);
        await (await intruder.PostAsync($"/api/v1/me/social-accounts/{id}/request-verification", null)).ShouldFailAsync(404);
        await (await intruder.PostAsync($"/api/v1/me/social-accounts/{id}/reactivate", null)).ShouldFailAsync(404);

        var requested = await (await owner.PostAsync($"/api/v1/me/social-accounts/{id}/request-verification", null)).ReadJsonAsync();
        Assert.Equal("PendingReview", requested.GetProperty("verificationStatus").GetString());
        await (await owner.PostAsync($"/api/v1/me/social-accounts/{id}/request-verification", null)).ShouldFailAsync(409, "social.verification_not_allowed");

        // Stale stamp → 409; editing verified facts resets PendingReview to Unverified.
        var edit = new
        {
            handle = account.GetProperty("handle").GetString(), profileUrl = account.GetProperty("profileUrl").GetString(),
            accountCreatedAt = account.GetProperty("accountCreatedAt").GetDateTime(), followerCount = 9999,
            concurrencyStamp = stamp,
        };
        await (await owner.PutAsJsonAsync($"/api/v1/me/social-accounts/{id}", edit)).ShouldFailAsync(409, "concurrency.conflict");
        var fresh = requested.GetProperty("concurrencyStamp").GetGuid();
        var retry = JsonSerializer.SerializeToNode(edit)!.AsObject();
        retry["concurrencyStamp"] = fresh;
        var changed = await (await owner.PutAsJsonAsync($"/api/v1/me/social-accounts/{id}", retry)).ReadJsonAsync();
        Assert.True(changed.GetProperty("verificationReset").GetBoolean());
        Assert.Equal("Unverified", changed.GetProperty("account").GetProperty("verificationStatus").GetString());
        Assert.Contains("verified again", changed.GetProperty("message").GetString());

        var deactivated = await (await owner.DeleteAsync($"/api/v1/me/social-accounts/{id}")).ReadJsonAsync();
        Assert.False(deactivated.GetProperty("isActive").GetBoolean());
        Assert.False(deactivated.GetProperty("qualifies").GetBoolean());
        Assert.True(await api.WithDbAsync(db => db.Set<OptimizeAll.Domain.Social.SocialAccount>().AnyAsync(a => a.Id == id)));
        var reactivated = await (await owner.PostAsync($"/api/v1/me/social-accounts/{id}/reactivate", null)).ReadJsonAsync();
        Assert.True(reactivated.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task At_most_ten_active_accounts()
    {
        var (_, client) = await api.CreateClientAsync();
        for (var i = 0; i < 10; i++)
            (await client.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(Handle(), 300))).EnsureSuccessStatusCode();
        await (await client.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(Handle(), 300))).ShouldFailAsync(409, "social.limit_reached");
    }

    [Fact]
    public async Task Reviewer_verifies_and_rejects_with_notifications_and_audit()
    {
        var (participant, owner) = await api.CreateClientAsync();
        var a1 = await (await owner.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(Handle(), 30))).ReadJsonAsync();
        var a2 = await (await owner.PostAsJsonAsync("/api/v1/me/social-accounts", NewAccount(Handle(), 300))).ReadJsonAsync();
        var id1 = a1.GetProperty("id").GetGuid();
        var id2 = a2.GetProperty("id").GetGuid();
        await owner.PostAsync($"/api/v1/me/social-accounts/{id1}/request-verification", null);

        var (reviewerUser, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        var queue = await (await reviewer.GetAsync($"/api/v1/review/social-accounts?status=PendingReview&search={Uri.EscapeDataString(participant.Email)}")).ReadJsonAsync();
        var item = Assert.Single(queue.GetProperty("items").EnumerateArray());
        Assert.Equal(id1, item.GetProperty("id").GetGuid());
        Assert.Equal(participant.Email, item.GetProperty("owner").GetProperty("email").GetString());
        Assert.Equal(30, item.GetProperty("accountAgeDays").GetInt32());

        var detail = await (await reviewer.GetAsync($"/api/v1/review/social-accounts/{id1}")).ReadJsonAsync();
        Assert.False(detail.GetProperty("qualifies").GetBoolean());
        var stamp1 = detail.GetProperty("account").GetProperty("concurrencyStamp").GetGuid();

        // Verify and correct the creation date (the reviewer found it is actually older).
        var verified = await (await reviewer.PostAsJsonAsync($"/api/v1/review/social-accounts/{id1}/decision", new
        {
            decision = "Verified", verifiedAccountCreatedAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(-500), verifiedFollowerCount = 12000,
            concurrencyStamp = stamp1,
        })).ReadJsonAsync();
        Assert.Equal("Verified", verified.GetProperty("account").GetProperty("verificationStatus").GetString());
        Assert.True(verified.GetProperty("qualifies").GetBoolean());
        Assert.Equal(12000, verified.GetProperty("account").GetProperty("followerCount").GetInt32());
        Assert.Equal(reviewerUser.Id, verified.GetProperty("verifiedBy").GetProperty("id").GetGuid());

        // A second decision on an already-decided profile → 409 (it is no longer pending review).
        await (await reviewer.PostAsJsonAsync($"/api/v1/review/social-accounts/{id1}/decision", new
        {
            decision = "Rejected", note = "late", concurrencyStamp = verified.GetProperty("account").GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(409, "social.not_pending");

        // A profile that was never submitted for review can't be decided.
        await (await reviewer.PostAsJsonAsync($"/api/v1/review/social-accounts/{id2}/decision", new
        {
            decision = "Verified", concurrencyStamp = a2.GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(409, "social.not_pending");
        var requested2 = await (await owner.PostAsync($"/api/v1/me/social-accounts/{id2}/request-verification", null)).ReadJsonAsync();

        // Stale stamp → 409.
        await (await reviewer.PostAsJsonAsync($"/api/v1/review/social-accounts/{id2}/decision", new
        {
            decision = "Rejected", note = "stale", concurrencyStamp = a2.GetProperty("concurrencyStamp").GetGuid(),
        })).ShouldFailAsync(409, "concurrency.conflict");

        // Rejection requires a note.
        var stamp2 = requested2.GetProperty("concurrencyStamp").GetGuid();
        await (await reviewer.PostAsJsonAsync($"/api/v1/review/social-accounts/{id2}/decision", new
        {
            decision = "Rejected", concurrencyStamp = stamp2,
        })).ShouldFailAsync(400, "social.note_required");
        var rejected = await (await reviewer.PostAsJsonAsync($"/api/v1/review/social-accounts/{id2}/decision", new
        {
            decision = "Rejected", note = "Profile is private; we could not confirm ownership.", concurrencyStamp = stamp2,
        })).ReadJsonAsync();
        Assert.Equal("Rejected", rejected.GetProperty("account").GetProperty("verificationStatus").GetString());
        Assert.False(rejected.GetProperty("qualifies").GetBoolean());
        Assert.Contains(rejected.GetProperty("reasons").EnumerateArray(), r => r.GetProperty("code").GetString() == "social.verification_rejected");

        var notifications = await api.WithDbAsync(db => db.Set<Notification>().AsNoTracking()
            .Where(n => n.UserId == participant.Id && n.Type == NotificationTypes.SocialAccountVerified).ToListAsync());
        Assert.Equal(2, notifications.Count);
        var deliveries = await api.WithDbAsync(db => db.Set<NotificationDelivery>().AsNoTracking()
            .Where(d => d.UserId == participant.Id && d.Channel == NotificationChannel.Email).CountAsync());
        Assert.Equal(2, deliveries);
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "social.account_rejected" && a.EntityId == id2.ToString())));

        // Participant sees the rejection note and can request verification again.
        var mine = await (await owner.GetAsync("/api/v1/me/social-accounts")).ReadJsonAsync();
        var mineRejected = mine.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id2);
        Assert.Contains("private", mineRejected.GetProperty("verificationNote").GetString());
        (await owner.PostAsync($"/api/v1/me/social-accounts/{id2}/request-verification", null)).EnsureSuccessStatusCode();

        // Participants can't reach the review queue.
        await (await owner.GetAsync("/api/v1/review/social-accounts")).ShouldFailAsync(403);
    }
}
