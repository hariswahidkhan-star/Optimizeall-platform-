using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Content;
using OptimizeAll.Api.Modules.Website.Seed;
using OptimizeAll.Domain.Content;
using OptimizeAll.Domain.Website;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Website;

namespace OptimizeAll.IntegrationTests.Content;

/// <summary>
/// Editable content: markup can't be smuggled past the tag stripper, required email links stay in the email text, and the
/// baseline seeders never re-create content an editor deleted or renamed.
/// </summary>
public sealed class EditableContentHardeningTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Nested_tags_in_page_texts_are_stripped_completely()
    {
        var admin = await api.AdminAsync();
        var saved = await admin.PutJsonAsync("/api/v1/agency/website/copy", new
        {
            changes = new object[]
            {
                new { key = "home.hero.title", value = "Grow <<b>script>alert(1)<</b>/script> now", concurrencyStamp = (Guid?)null },
            },
        });
        var value = saved.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("entries").EnumerateArray())
            .Single(e => e.GetProperty("key").GetString() == "home.hero.title").GetProperty("value").GetString()!;
        Assert.DoesNotContain("<script", value, StringComparison.OrdinalIgnoreCase);
        var published = await api.Anonymous().GetJsonAsync("/api/v1/content/copy");
        Assert.DoesNotContain("<script", published.GetProperty("values").GetProperty("home.hero.title").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_required_link_must_stay_in_the_email_text_not_only_in_the_subject()
    {
        var admin = await api.AdminAsync();
        var response = await admin.PutAsJsonAsync("/api/v1/admin/email-templates/website.newsletter_confirm", new
        {
            subject = "Confirm — or leave: {{unsubscribeUrl}}", body = "Tap to confirm: {{confirmUrl}}",
        });
        await response.ShouldFailAsync(400, "email_template.invalid");
        Assert.False((await admin.GetJsonAsync("/api/v1/admin/email-templates/website.newsletter_confirm")).GetProperty("isCustomized").GetBoolean());

        var preview = await admin.PostAsJsonAsync("/api/v1/admin/email-templates/notification.layout/preview", new
        {
            subject = "{{content}}", body = "Hi {{displayName}}", actionLabel = "Open",
        });
        await preview.ShouldFailAsync(400, "email_template.invalid");
    }

    [Fact]
    public async Task Content_seeder_does_not_recreate_faq_steps_or_announcement_an_admin_removed_or_renamed()
    {
        var question = ContentBaselineSeeder.Faqs[0].Question;
        await api.WithDbAsync(async db =>
        {
            await db.Set<FaqItem>().Where(f => f.Question == question).ExecuteUpdateAsync(s => s.SetProperty(f => f.Question, "Reworded by the editor?"));
            await db.Set<OnboardingStep>().Where(s => s.Key == "first-approved").ExecuteDeleteAsync();
            await db.Set<Announcement>().Where(a => a.Title == ContentBaselineSeeder.WelcomeAnnouncementTitle)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Title, "Hello and welcome"));
            return true;
        });

        await api.WithDbAsync(async db =>
        {
            await new ContentBaselineSeeder(api.Clock).SeedAsync(db, CancellationToken.None);
            return true;
        });

        var (faqs, stepGone, welcome) = await api.WithDbAsync(async db => (
            await db.Set<FaqItem>().CountAsync(f => f.Question == question),
            !await db.Set<OnboardingStep>().AnyAsync(s => s.Key == "first-approved"),
            await db.Set<Announcement>().CountAsync(a => a.Title == ContentBaselineSeeder.WelcomeAnnouncementTitle)));
        Assert.Equal(0, faqs);
        Assert.True(stepGone);
        Assert.Equal(0, welcome);
    }

    [Fact]
    public async Task Website_seeder_does_not_recreate_legal_pages_an_editor_deleted_or_moved()
    {
        await api.WithDbAsync(async db =>
        {
            await db.Set<SitePage>().Where(p => p.Slug == "terms-of-service").ExecuteDeleteAsync();
            await db.Set<SitePage>().Where(p => p.Slug == "privacy-policy").ExecuteUpdateAsync(s => s.SetProperty(p => p.Slug, "privacy"));
            return true;
        });

        await api.WithDbAsync(async db =>
        {
            await new WebsiteBaselineSeeder().SeedAsync(db, CancellationToken.None);
            return true;
        });

        var slugs = await api.WithDbAsync(db => db.Set<SitePage>().Select(p => p.Slug).ToListAsync());
        Assert.DoesNotContain("terms-of-service", slugs);
        Assert.DoesNotContain("privacy-policy", slugs);
        Assert.Contains("privacy", slugs);
        Assert.Contains("about", slugs);
    }
}
