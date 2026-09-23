using System.Net.Http.Json;
using System.Text.Json;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Accounts;

public sealed class HomeTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static JsonElement Step(JsonElement home, string key) =>
        home.GetProperty("onboarding").GetProperty("steps").EnumerateArray().Single(s => s.GetProperty("key").GetString() == key);

    [Fact]
    public async Task Home_state_moves_from_verify_email_to_active()
    {
        var user = await api.CreateUserAsync(emailVerified: false);
        var client = await api.LoginAsync(user);

        var home = await (await client.GetAsync("/api/v1/me/home")).ReadJsonAsync();
        Assert.Equal("VerifyEmail", home.GetProperty("state").GetString());
        Assert.False(Step(home, "verify-email").GetProperty("completed").GetBoolean());
        Assert.Equal(7, home.GetProperty("onboarding").GetProperty("totalCount").GetInt32());
        Assert.Equal(0, home.GetProperty("onboarding").GetProperty("progressPercent").GetInt32());
        // Onboarding-audience welcome announcement from the baseline seed.
        Assert.Contains(home.GetProperty("announcements").EnumerateArray(), a => a.GetProperty("title").GetString() == "Welcome to Optimize All");

        await api.WithDbAsync(async db =>
        {
            var u = await db.Set<User>().FindAsync(user.Id);
            u!.EmailVerifiedAt = api.Clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        });
        home = await (await client.GetAsync("/api/v1/me/home")).ReadJsonAsync();
        Assert.Equal("AddSocialAccount", home.GetProperty("state").GetString());
        Assert.True(Step(home, "verify-email").GetProperty("completed").GetBoolean());

        var young = await api.AddSocialAccountAsync(user.Id, ageDays: 10);
        home = await (await client.GetAsync("/api/v1/me/home")).ReadJsonAsync();
        Assert.Equal("AwaitingEligibility", home.GetProperty("state").GetString());
        var expectedFrom = young.AccountCreatedAt.AddDays(90);
        Assert.Equal(expectedFrom, home.GetProperty("eligibleFrom").GetDateTime(), TimeSpan.FromSeconds(1));
        var social = home.GetProperty("socialAccounts");
        Assert.Equal(1, social.GetProperty("total").GetInt32());
        Assert.Equal(0, social.GetProperty("eligible").GetInt32());
        Assert.Equal(90, social.GetProperty("minAccountAgeDays").GetInt32());
        Assert.True(Step(home, "add-social-account").GetProperty("completed").GetBoolean());
        Assert.False(Step(home, "eligible-account").GetProperty("completed").GetBoolean());

        var established = await api.AddSocialAccountAsync(user.Id, ageDays: 400);
        home = await (await client.GetAsync("/api/v1/me/home")).ReadJsonAsync();
        Assert.Equal("Ready", home.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, home.GetProperty("eligibleFrom").ValueKind);
        Assert.True(Step(home, "eligible-account").GetProperty("completed").GetBoolean());
        // Eligible users are no longer in the Onboarding audience.
        Assert.DoesNotContain(home.GetProperty("announcements").EnumerateArray(), a => a.GetProperty("title").GetString() == "Welcome to Optimize All");

        await api.AddSubmissionAsync(user.Id, established.Id, SubmissionStatus.Pending);
        await api.AddSubmissionAsync(user.Id, established.Id, SubmissionStatus.Approved);
        home = await (await client.GetAsync("/api/v1/me/home")).ReadJsonAsync();
        Assert.Equal("Active", home.GetProperty("state").GetString());
        var subs = home.GetProperty("submissions");
        Assert.Equal(2, subs.GetProperty("total").GetInt32());
        Assert.Equal(1, subs.GetProperty("pending").GetInt32());
        Assert.Equal(1, subs.GetProperty("approved").GetInt32());
        Assert.True(Step(home, "first-submission").GetProperty("completed").GetBoolean());
        Assert.True(Step(home, "first-approved").GetProperty("completed").GetBoolean());
    }

    [Fact]
    public async Task Onboarding_profile_and_payout_steps_and_manual_completion()
    {
        var (admin, adminClient) = await api.AdminAsync();
        _ = admin;
        var manual = await (await adminClient.PostAsJsonAsync("/api/v1/admin/content/onboarding-steps", new
        {
            key = "read-rules-" + Guid.NewGuid().ToString("N")[..6], title = "Read the campaign rules", description = "Know the rules.",
            actionLabel = "Read rules", actionUrl = "/app/help", completionRule = "Manual", sortOrder = 5, isActive = true,
        })).ReadJsonAsync();
        var manualId = manual.GetProperty("id").GetGuid();

        var (_, client) = await api.CreateClientAsync();
        var home = await (await client.GetAsync("/api/v1/me/home")).ReadJsonAsync();
        var manualStep = home.GetProperty("onboarding").GetProperty("steps").EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == manualId);
        Assert.True(manualStep.GetProperty("isManual").GetBoolean());
        Assert.False(manualStep.GetProperty("completed").GetBoolean());
        Assert.False(Step(home, "complete-profile").GetProperty("completed").GetBoolean());

        var first = await (await client.PostAsync($"/api/v1/me/onboarding/{manualId}/complete", null)).ReadJsonAsync();
        var again = await (await client.PostAsync($"/api/v1/me/onboarding/{manualId}/complete", null)).ReadJsonAsync();
        Assert.Equal(first.GetProperty("completedAt").GetDateTime(), again.GetProperty("completedAt").GetDateTime());

        var automatic = Step(home, "verify-email").GetProperty("id").GetGuid();
        await (await client.PostAsync($"/api/v1/me/onboarding/{automatic}/complete", null)).ShouldFailAsync(400, "onboarding.not_manual");
        await (await client.PostAsync($"/api/v1/me/onboarding/{Guid.NewGuid()}/complete", null)).ShouldFailAsync(404);

        (await client.PutAsJsonAsync("/api/v1/me/profile", new
        {
            displayName = "Profile Done", countryCode = "PK", languageCode = "en", timeZone = "Asia/Karachi", interests = new[] { "tech" },
        })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/v1/me/payout-profile", new
        {
            method = "PayPal", accountHolderName = "Profile Done", destination = "done@example.com", preferredCurrency = "USD",
        })).EnsureSuccessStatusCode();

        home = await (await client.GetAsync("/api/v1/me/home")).ReadJsonAsync();
        Assert.True(Step(home, "complete-profile").GetProperty("completed").GetBoolean());
        Assert.True(Step(home, "payout-details").GetProperty("completed").GetBoolean());
        manualStep = home.GetProperty("onboarding").GetProperty("steps").EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == manualId);
        Assert.True(manualStep.GetProperty("completed").GetBoolean());
        Assert.True(home.GetProperty("onboarding").GetProperty("progressPercent").GetInt32() > 0);

        // Deactivate the manual step again so other tests in this class see the baseline checklist.
        var current = await (await adminClient.GetAsync($"/api/v1/admin/content/onboarding-steps/{manualId}")).ReadJsonAsync();
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(current.GetRawText())!;
        body["isActive"] = false;
        (await adminClient.PutAsJsonAsync($"/api/v1/admin/content/onboarding-steps/{manualId}", body)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Banners_are_filtered_by_audience_country_language_and_date_window()
    {
        var (_, admin) = await api.AdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var now = api.Clock.GetUtcNow().UtcDateTime;

        async Task Banner(string name, object extra)
        {
            var body = JsonSerializer.SerializeToNode(new { title = $"{tag}-{name}", audience = "Everyone", sortOrder = 1, isActive = true })!.AsObject();
            foreach (var p in JsonSerializer.SerializeToNode(extra)!.AsObject().ToList()) body[p.Key] = p.Value?.DeepClone();
            (await admin.PostAsJsonAsync("/api/v1/admin/content/banners", body)).EnsureSuccessStatusCode();
        }

        await Banner("everyone", new { });
        await Banner("pk-only", new { countryCode = "PK" });
        await Banner("ae-only", new { countryCode = "AE" });
        await Banner("urdu-only", new { languageCode = "ur" });
        await Banner("future", new { startsAt = now.AddDays(2) });
        await Banner("expired", new { startsAt = now.AddDays(-10), endsAt = now.AddDays(-1) });
        await Banner("current-window", new { startsAt = now.AddDays(-1), endsAt = now.AddDays(1) });
        await Banner("inactive", new { isActive = false });
        await Banner("onboarding", new { audience = "Onboarding" });
        await Banner("eligible", new { audience = "Eligible" });
        await Banner("earners", new { audience = "ActiveEarners" });
        await Banner("inactive-users", new { audience = "Inactive" });

        // PK, English, verified, no social accounts → Onboarding audience.
        var user = await api.CreateUserAsync(countryCode: "PK");
        var client = await api.LoginAsync(user);
        var names = await BannerNames(client, tag);
        Assert.Equal(new[] { "current-window", "everyone", "onboarding", "pk-only" }, names);

        // Becoming eligible switches the audience.
        await api.AddSocialAccountAsync(user.Id, ageDays: 365);
        names = await BannerNames(client, tag);
        Assert.Equal(new[] { "current-window", "eligible", "everyone", "pk-only" }, names);

        // Inactive participants (LastActiveAt older than retention.inactivityDays = 30 by default).
        await api.WithDbAsync(async db =>
        {
            var u = await db.Set<User>().FindAsync(user.Id);
            u!.LastActiveAt = now.AddDays(-45);
            await db.SaveChangesAsync();
        });
        names = await BannerNames(client, tag);
        Assert.Contains("inactive-users", names);

        // Invalid banner input is rejected.
        await (await admin.PostAsJsonAsync("/api/v1/admin/content/banners", new
        {
            title = "bad", audience = "Everyone", ctaLabel = "Go", ctaUrl = "javascript:alert(1)",
        })).ShouldFailAsync(400, "content.invalid");
        await (await admin.PostAsJsonAsync("/api/v1/admin/content/banners", new
        {
            title = "bad", audience = "Everyone", startsAt = now, endsAt = now.AddHours(-1),
        })).ShouldFailAsync(400, "content.invalid");
        await (await admin.PostAsJsonAsync("/api/v1/admin/content/banners", new
        {
            title = "bad", audience = "Everyone", imageUrl = "//evil.example/x.png",
        })).ShouldFailAsync(400, "content.invalid");
    }

    private static async Task<string[]> BannerNames(HttpClient client, string tag)
    {
        var home = await (await client.GetAsync("/api/v1/me/home")).ReadJsonAsync();
        return home.GetProperty("banners").EnumerateArray()
            .Select(b => b.GetProperty("title").GetString()!)
            .Where(t => t.StartsWith(tag + "-"))
            .Select(t => t[(tag.Length + 1)..])
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    public async Task Home_counts_unread_notifications_and_open_tickets_and_requires_participant_portal()
    {
        var (_, client) = await api.CreateClientAsync();
        (await client.PostAsJsonAsync("/api/v1/me/support/tickets", new
        {
            subject = "Where is my payout?", category = "Payout", body = "I expected a payout last week.",
        })).EnsureSuccessStatusCode();
        var home = await (await client.GetAsync("/api/v1/me/home")).ReadJsonAsync();
        Assert.Equal(1, home.GetProperty("openSupportTicketCount").GetInt32());
        Assert.Equal(0, home.GetProperty("unreadNotificationCount").GetInt32());

        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        await (await reviewer.GetAsync("/api/v1/me/home")).ShouldFailAsync(403);
        Assert.Equal(401, (int)(await api.CreateClient().GetAsync("/api/v1/me/home")).StatusCode);
    }
}
