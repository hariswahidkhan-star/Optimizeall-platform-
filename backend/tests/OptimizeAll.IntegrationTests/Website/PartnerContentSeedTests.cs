using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Website.Seed;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Website;

/// <summary>
/// The partner-content seed (Baseline profile, <c>Website:PartnerContent:Enabled</c>, on by default; the shared test
/// factory turns it off so other tests keep their blog fixtures): publishes the posts once, with a byline, categories and
/// public cover images, is idempotent by slug, and never re-creates posts an editor deleted or renamed.
/// </summary>
public sealed class PartnerContentSeedTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly string[] Slugs = PartnerPostLibrary.All.Select(p => p.Slug).ToArray();

    [Fact]
    public async Task Startup_seed_publishes_the_partner_posts_once_and_respects_editor_changes()
    {
        Assert.True(new PartnerContentOptions().Enabled); // production default
        Assert.Equal(0, await api.WithDbAsync(db => db.Set<BlogPost>().CountAsync(p => Slugs.Contains(p.Slug))));

        // A host started with the flag on runs migrations and the Baseline seed (including the partner content) on startup.
        await using var host = api.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { [PartnerContentOptions.Section + ":Enabled"] = "true" })));
        await host.StartAsync();

        var now = api.Clock.GetUtcNow().UtcDateTime;
        var (posts, author, categories) = await api.WithDbAsync(async db => (
            await db.Set<BlogPost>().AsNoTracking().Where(p => Slugs.Contains(p.Slug)).ToListAsync(),
            await db.Set<TeamMember>().AsNoTracking().SingleAsync(m => m.Slug == PartnerContentSeeder.AuthorSlug),
            await db.Set<BlogCategory>().AsNoTracking().Where(c => c.Slug == "project-controls" || c.Slug == "exam-prep").ToListAsync()));
        Assert.Equal(Slugs.Length, posts.Count);
        Assert.Equal(PartnerContentSeeder.AuthorName, author.Name);
        Assert.Equal(2, categories.Count);
        Assert.All(posts, p =>
        {
            Assert.Equal(BlogPostStatus.Published, p.Status);
            Assert.NotNull(p.PublishedAt);
            Assert.True(p.PublishedAt <= now && p.PublishedAt >= now.AddDays(-31), p.Slug);
            Assert.Equal(author.Id, p.AuthorId);
            Assert.NotEmpty(p.CategoryIds);
            Assert.All(p.CategoryIds, id => Assert.Contains(categories, c => c.Id == id));
            Assert.True(p.Title.Length <= 60 && p.Seo.Title == p.Title, p.Slug);
            Assert.True(p.Seo.Description!.Length <= 155 && p.Excerpt == p.Seo.Description, p.Slug);
            Assert.False(string.IsNullOrWhiteSpace(p.CoverImageAlt));
            Assert.True(FieldRules.IsAllowedImageUrl(p.CoverImageUrl, Array.Empty<string>()), p.CoverImageUrl);
            Assert.EndsWith("*" + PartnerPostLibrary.Disclosure + "*", p.BodyMarkdown);
            Assert.True(p.ReadingMinutes >= 5, p.Slug);
            Assert.NotEmpty(p.RelatedPostIds);
        });
        Assert.Equal(PartnerPostLibrary.All.Count, posts.Select(p => p.PublishedAt).Distinct().Count()); // staggered

        // One public cover per cluster, readable anonymously.
        var coverUrls = posts.Select(p => p.CoverImageUrl!).Distinct().ToList();
        Assert.Equal(PartnerPostLibrary.All.Select(p => p.Cover).Distinct().Count(), coverUrls.Count);
        var anon = api.Anonymous();
        foreach (var url in coverUrls)
        {
            var response = await anon.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        }

        // The public site serves a post with its byline.
        var live = await anon.GetJsonAsync("/api/v1/public/blog/" + Slugs[0]);
        Assert.Equal(PartnerContentSeeder.AuthorName, live.GetProperty("author").GetProperty("name").GetString());

        // Seeding again changes nothing: no duplicate posts, authors, categories or cover files.
        var counts = await CountsAsync();
        await RunSeederAsync(host);
        await RunSeederAsync(host);
        Assert.Equal(counts, await CountsAsync());

        // Seeded posts stay editable through the CMS (the cover passes the image URL policy).
        var admin = await api.AdminAsync();
        var editable = posts.Single(p => p.Slug == Slugs[1]);
        var edited = await admin.PutJsonAsync($"/api/v1/agency/website/blog/posts/{editable.Id}", new
        {
            slug = editable.Slug, title = editable.Title + " (updated)", excerpt = editable.Excerpt, bodyMarkdown = editable.BodyMarkdown,
            coverImageUrl = editable.CoverImageUrl, coverImageAlt = editable.CoverImageAlt, authorId = editable.AuthorId,
            categoryIds = editable.CategoryIds, tags = editable.Tags, relatedPostIds = editable.RelatedPostIds,
            seo = new { title = editable.Seo.Title, description = editable.Seo.Description }, concurrencyStamp = editable.ConcurrencyStamp,
        });
        Assert.EndsWith("(updated)", edited.GetProperty("title").GetString());

        // A post an editor deleted, or whose slug they changed, is not created again; the edit survives re-seeding.
        await api.WithDbAsync(async db =>
        {
            await db.Set<BlogPost>().Where(p => p.Slug == Slugs[2]).ExecuteDeleteAsync();
            await db.Set<BlogPost>().Where(p => p.Slug == Slugs[3]).ExecuteUpdateAsync(s => s.SetProperty(p => p.Slug, Slugs[3] + "-renamed"));
        });
        await RunSeederAsync(host);
        var after = await api.WithDbAsync(db => db.Set<BlogPost>().AsNoTracking().Select(p => new { p.Slug, p.Title }).ToListAsync());
        Assert.DoesNotContain(after, p => p.Slug == Slugs[2] || p.Slug == Slugs[3]);
        Assert.Contains(after, p => p.Slug == Slugs[3] + "-renamed");
        Assert.EndsWith("(updated)", after.Single(p => p.Slug == Slugs[1]).Title);
        Assert.True(await api.WithDbAsync(db => db.Set<SystemSetting>().AnyAsync(s => s.Key == SeedLedger.KeyPrefix + PartnerContentSeeder.LedgerName)));
    }

    [Fact]
    public async Task The_seed_does_nothing_when_disabled()
    {
        var before = await CountsAsync();
        using (var scope = api.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetServices<ISeeder>().OfType<PartnerContentSeeder>().Single(); // disabled in the test factory
            await seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), CancellationToken.None);
        }
        Assert.Equal(before, await CountsAsync());
    }

    private static async Task RunSeederAsync(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetServices<ISeeder>().OfType<PartnerContentSeeder>().Single();
        await seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), CancellationToken.None);
    }

    private Task<(int Posts, int Authors, int Categories, int Files)> CountsAsync() => api.WithDbAsync(async db => (
        await db.Set<BlogPost>().CountAsync(),
        await db.Set<TeamMember>().CountAsync(m => m.Slug == PartnerContentSeeder.AuthorSlug),
        await db.Set<BlogCategory>().CountAsync(),
        await db.Set<StoredFile>().CountAsync(f => f.OriginalFileName.StartsWith("partner-cover-"))));
}
