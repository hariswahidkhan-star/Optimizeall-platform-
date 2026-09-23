using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Accounts;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Content;

public sealed class ContentTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Baseline_faq_is_public_and_grouped_by_category()
    {
        var faq = await (await api.CreateClient().GetAsync("/api/v1/content/faqs")).ReadJsonAsync();
        var categories = faq.GetProperty("categories").EnumerateArray().ToList();
        var names = categories.Select(c => c.GetProperty("category").GetString()).ToList();
        foreach (var expected in new[] { "Getting started", "Eligibility", "Submissions", "Payments", "Disclosure & rules", "Account" })
            Assert.Contains(expected, names);
        Assert.True(categories.Sum(c => c.GetProperty("items").GetArrayLength()) >= 10);
        var all = string.Join(" ", categories.SelectMany(c => c.GetProperty("items").EnumerateArray()).Select(i => i.GetProperty("answer").GetString()));
        Assert.Contains("biweekly", all);
        Assert.Contains("minimum", all);
        Assert.Contains("not proof", all);
        Assert.Contains("mandatory", all);
    }

    [Fact]
    public async Task Admin_manages_banners_with_concurrency_reorder_and_audit()
    {
        var (_, admin) = await api.AdminAsync();
        var created = await admin.PostAsJsonAsync("/api/v1/admin/content/banners", new
        {
            title = "Summer bonus", body = "Earn double", ctaLabel = "See campaigns", ctaUrl = "/app/campaigns",
            imageUrl = "https://cdn.example.com/b.png", audience = "Eligible", countryCode = "pk", languageCode = "EN", sortOrder = 30,
        });
        Assert.Equal(201, (int)created.StatusCode);
        var banner = await created.ReadJsonAsync();
        var id = banner.GetProperty("id").GetGuid();
        Assert.Equal("PK", banner.GetProperty("countryCode").GetString());
        Assert.Equal("en", banner.GetProperty("languageCode").GetString());
        var stamp = banner.GetProperty("concurrencyStamp").GetGuid();

        var update = JsonSerializer.SerializeToNode(banner)!.AsObject();
        update["title"] = "Summer bonus (updated)";
        var updated = await (await admin.PutAsJsonAsync($"/api/v1/admin/content/banners/{id}", update)).ReadJsonAsync();
        Assert.Equal("Summer bonus (updated)", updated.GetProperty("title").GetString());
        Assert.NotEqual(stamp, updated.GetProperty("concurrencyStamp").GetGuid());

        // Stale stamp is rejected.
        update["title"] = "Lost update";
        await (await admin.PutAsJsonAsync($"/api/v1/admin/content/banners/{id}", update)).ShouldFailAsync(409, "concurrency.conflict");

        var second = await (await admin.PostAsJsonAsync("/api/v1/admin/content/banners", new { title = "Second", audience = "Everyone", sortOrder = 1 })).ReadJsonAsync();
        var secondId = second.GetProperty("id").GetGuid();
        var reorder = await (await admin.PostAsJsonAsync("/api/v1/admin/content/banners/reorder", new { ids = new[] { id, secondId } })).ReadJsonAsync();
        Assert.Equal(2, reorder.GetProperty("updated").GetInt32());
        Assert.Equal(10, (await (await admin.GetAsync($"/api/v1/admin/content/banners/{id}")).ReadJsonAsync()).GetProperty("sortOrder").GetInt32());
        Assert.Equal(20, (await (await admin.GetAsync($"/api/v1/admin/content/banners/{secondId}")).ReadJsonAsync()).GetProperty("sortOrder").GetInt32());

        var list = await (await admin.GetAsync("/api/v1/admin/content/banners?search=Summer&pageSize=5")).ReadJsonAsync();
        Assert.Equal(1, list.GetProperty("total").GetInt32());

        Assert.Equal(204, (int)(await admin.DeleteAsync($"/api/v1/admin/content/banners/{secondId}")).StatusCode);
        await (await admin.GetAsync($"/api/v1/admin/content/banners/{secondId}")).ShouldFailAsync(404);

        var actions = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityType == "HomepageBanner").Select(a => a.Action).ToListAsync());
        Assert.Contains("content.banner_created", actions);
        Assert.Contains("content.banner_updated", actions);
        Assert.Contains("content.banners_reordered", actions);
        Assert.Contains("content.banner_deleted", actions);
    }

    [Fact]
    public async Task Faq_crud_hides_unpublished_items_from_the_public()
    {
        var (_, admin) = await api.AdminAsync();
        var faq = await (await admin.PostAsJsonAsync("/api/v1/admin/content/faqs", new
        {
            question = "Draft question about payouts?", answer = "Draft answer", category = "Payments", sortOrder = 999, isPublished = false,
        })).ReadJsonAsync();
        var id = faq.GetProperty("id").GetGuid();

        var publicFaq = await (await api.CreateClient().GetAsync("/api/v1/content/faqs")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Draft question about payouts?", publicFaq);

        var body = JsonSerializer.SerializeToNode(faq)!.AsObject();
        body["isPublished"] = true;
        (await admin.PutAsJsonAsync($"/api/v1/admin/content/faqs/{id}", body)).EnsureSuccessStatusCode();
        publicFaq = await (await api.CreateClient().GetAsync("/api/v1/content/faqs")).Content.ReadAsStringAsync();
        Assert.Contains("Draft question about payouts?", publicFaq);

        var filtered = await (await admin.GetAsync("/api/v1/admin/content/faqs?category=Payments&search=Draft")).ReadJsonAsync();
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        Assert.Equal(204, (int)(await admin.DeleteAsync($"/api/v1/admin/content/faqs/{id}")).StatusCode);
    }

    [Fact]
    public async Task Onboarding_step_keys_are_unique_slugs()
    {
        var (_, admin) = await api.AdminAsync();
        await (await admin.PostAsJsonAsync("/api/v1/admin/content/onboarding-steps", new
        {
            key = "verify-email", title = "Dup", description = "Dup", completionRule = "Manual",
        })).ShouldFailAsync(409, "content.duplicate_key");
        await (await admin.PostAsJsonAsync("/api/v1/admin/content/onboarding-steps", new
        {
            key = "Not A Slug", title = "Bad", description = "Bad", completionRule = "Manual",
        })).ShouldFailAsync(400, "content.invalid");

        var steps = await (await admin.GetAsync("/api/v1/admin/content/onboarding-steps?pageSize=50")).ReadJsonAsync();
        var keys = steps.GetProperty("items").EnumerateArray().Select(s => s.GetProperty("key").GetString()).ToList();
        Assert.Equal(new[] { "verify-email", "complete-profile", "add-social-account", "eligible-account", "payout-details", "first-submission", "first-approved" },
            keys.Take(7));
    }

    [Fact]
    public async Task Announcements_are_filtered_by_audience_and_publish_window()
    {
        var (_, admin) = await api.AdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var now = api.Clock.GetUtcNow().UtcDateTime;
        async Task Create(string name, string audience, DateTime publishAt, DateTime? expiresAt = null) =>
            (await admin.PostAsJsonAsync("/api/v1/admin/content/announcements", new
            {
                title = $"{tag}-{name}", body = "Body", severity = "Info", audience, publishAt, expiresAt, isActive = true,
            })).EnsureSuccessStatusCode();

        await Create("all", "Everyone", now.AddMinutes(-1));
        await Create("eligible", "Eligible", now.AddMinutes(-1));
        await Create("scheduled", "Everyone", now.AddDays(1));
        await Create("expired", "Everyone", now.AddDays(-3), now.AddDays(-1));
        await (await admin.PostAsJsonAsync("/api/v1/admin/content/announcements", new
        {
            title = "bad", body = "Body", severity = "Info", audience = "Everyone", publishAt = now, expiresAt = now.AddMinutes(-5),
        })).ShouldFailAsync(400, "content.invalid");

        var (newcomer, newcomerClient) = await api.CreateClientAsync();
        _ = newcomer;
        var seen = await Titles(newcomerClient, tag);
        Assert.Equal(new[] { "all" }, seen);

        var (eligible, eligibleClient) = await api.CreateClientAsync();
        await api.AddSocialAccountAsync(eligible.Id, ageDays: 400);
        seen = await Titles(eligibleClient, tag);
        Assert.Equal(new[] { "all", "eligible" }, seen);

        // Staff can read announcements too (any authenticated user); anonymous cannot.
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        (await reviewer.GetAsync("/api/v1/content/announcements")).EnsureSuccessStatusCode();
        Assert.Equal(401, (int)(await api.CreateClient().GetAsync("/api/v1/content/announcements")).StatusCode);
    }

    private static async Task<string[]> Titles(HttpClient client, string tag)
    {
        var items = await (await client.GetAsync("/api/v1/content/announcements")).ReadJsonAsync();
        return items.EnumerateArray().Select(a => a.GetProperty("title").GetString()!)
            .Where(t => t.StartsWith(tag + "-")).Select(t => t[(tag.Length + 1)..]).OrderBy(t => t).ToArray();
    }
}
