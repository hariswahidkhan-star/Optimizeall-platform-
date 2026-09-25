using System.Text.Json;
using OptimizeAll.Api.Modules.Website.SiteSeo;
using OptimizeAll.Domain.Seo;

namespace OptimizeAll.UnitTests.Website;

/// <summary>The document head, structured-data finishing and the crawler policy (docs/SEO_AUDIT_2026-09.md).</summary>
public sealed class SeoHeadAndStructuredDataTests
{
    private static readonly SiteChromeLinks Chrome = new("Optimize All", new[] { new LinkItem("Services", "/services") },
        new[] { ("Company", (IReadOnlyList<LinkItem>)new[] { new LinkItem("About", "/about") }) }, Array.Empty<LinkItem>());

    private static SeoPage Page(bool indexable = true) => new()
    {
        Path = "/services/seo", Title = "SEO services | Optimize All", Description = "Technical SEO, content and links.",
        Canonical = "https://optimizeall.com/services/seo", NoIndex = !indexable,
        OgImage = "https://optimizeall.com/og/services/seo.png?v=abc", OgImageWidth = 1200, OgImageHeight = 630, OgImageAlt = "SEO services",
    };

    [Fact]
    public void Indexable_pages_declare_english_and_default_versions_at_their_canonical()
    {
        var html = SeoDocumentWriter.Write(Page(), Chrome, "Optimize All", null, "https://optimizeall.com");
        Assert.Contains("<link rel=\"alternate\" hreflang=\"en\" href=\"https://optimizeall.com/services/seo\"", html);
        Assert.Contains("<link rel=\"alternate\" hreflang=\"x-default\" href=\"https://optimizeall.com/services/seo\"", html);
        Assert.Contains("<meta name=\"robots\" content=\"index, follow, max-image-preview:large, max-snippet:-1, max-video-preview:-1\"", html);
        Assert.Contains("<meta property=\"og:image:type\" content=\"image/png\"", html);
        Assert.Contains("<a href=\"/llms.txt\">llms.txt</a>", html);
        Assert.Contains("<a href=\"/sitemap.xml\">Sitemap</a>", html);

        var hidden = SeoDocumentWriter.Write(Page(indexable: false), Chrome with { LlmsTxt = false }, "Optimize All", null, "https://optimizeall.com");
        Assert.DoesNotContain("hreflang", hidden);
        Assert.DoesNotContain("/llms.txt", hidden);
    }

    [Theory]
    [InlineData("https://x.com/og/a.png?v=1", "image/png")]
    [InlineData("https://x.com/api/v1/files/abc", null)]
    [InlineData("https://x.com/cover.JPG", "image/jpeg")]
    [InlineData("https://x.com/cover.webp", "image/webp")]
    public void Social_image_types_come_from_the_url(string url, string? type) => Assert.Equal(type, SeoDocumentWriter.ImageType(url));

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Articles_without_an_image_get_the_page_social_image()
    {
        var article = SeoJsonLd.WithArticleImage(Json("""{"@context":"https://schema.org","@type":"BlogPosting","headline":"H"}"""), "https://x.com/og/a.png");
        Assert.Equal("https://x.com/og/a.png", article.GetProperty("image")[0].GetString());

        var multi = SeoJsonLd.WithArticleImage(Json("""{"@context":"https://schema.org","@type":["LearningResource","Article"],"name":"L"}"""), "https://x.com/i.png");
        Assert.True(multi.TryGetProperty("image", out _));

        var graph = SeoJsonLd.WithArticleImage(Json("""{"@context":"https://schema.org","@graph":[{"@type":"Article","headline":"A"},{"@type":"Organization","name":"O"}]}"""), "https://x.com/i.png");
        Assert.True(graph.GetProperty("@graph")[0].TryGetProperty("image", out _));
        Assert.False(graph.GetProperty("@graph")[1].TryGetProperty("image", out _));

        // An existing image is kept; other types are untouched.
        var own = SeoJsonLd.WithArticleImage(Json("""{"@type":"Article","image":"https://x.com/own.jpg"}"""), "https://x.com/i.png");
        Assert.Equal("https://x.com/own.jpg", own.GetProperty("image").GetString());
        var service = SeoJsonLd.WithArticleImage(Json("""{"@type":"Service","name":"S"}"""), "https://x.com/i.png");
        Assert.False(service.TryGetProperty("image", out _));
    }

    [Theory]
    [InlineData("Googlebot")]
    [InlineData("Bingbot")]
    [InlineData("Applebot")]
    [InlineData("GPTBot")]
    [InlineData("OAI-SearchBot")]
    [InlineData("ChatGPT-User")]
    [InlineData("ClaudeBot")]
    [InlineData("Claude-SearchBot")]
    [InlineData("PerplexityBot")]
    [InlineData("Google-Extended")]
    [InlineData("Applebot-Extended")]
    [InlineData("DuckAssistBot")]
    [InlineData("Meta-ExternalFetcher")]
    [InlineData("Amazonbot")]
    [InlineData("SomeNewBot")]
    public void Search_engines_and_ai_assistants_may_read_public_pages_but_not_private_areas(string bot)
    {
        var robots = RobotsTxt.Parse(RobotsWriter.Write(SeoSettings.Defaults, "https://optimizeall.com"));
        foreach (var path in new[] { "/", "/services/seo", "/learn/seo-basics/what-is-seo", "/blog", "/llms.txt", "/og/index.png", "/sitemap.xml",
                     "/api/v1/files/abc", "/api/v1/public/blog/rss.xml", "/apple-case-study" })
            Assert.True(robots.IsAllowed(bot, path), $"{bot} should read {path}");
        foreach (var path in new[] { "/admin", "/app/dashboard", "/login", "/api/v1/auth/me", "/search?q=x", "/p/token" })
            Assert.False(robots.IsAllowed(bot, path), $"{bot} must not read {path}");
    }

    [Fact]
    public void Aggressive_scrapers_are_blocked_by_default()
    {
        var robots = RobotsTxt.Parse(RobotsWriter.Write(SeoSettings.Defaults, "https://optimizeall.com"));
        Assert.False(robots.IsAllowed("Bytespider", "/"));
        Assert.EndsWith("Sitemap: https://optimizeall.com/sitemap.xml\n", RobotsWriter.Write(SeoSettings.Defaults, "https://optimizeall.com"));
    }

    [Fact]
    public void Video_sitemap_lists_only_videos_with_a_thumbnail_and_a_playable_url()
    {
        var at = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var ok = new SeoVideo("Lecture", "About", "https://x.com/v.mp4", null, "https://x.com/p.jpg", null, "en", null, at, 60);
        var noPoster = ok with { Name = "No poster", PosterUrl = null };
        var noFile = ok with { Name = "No file", Mp4Url = null };
        var urls = new[] { new SitemapUrl("/learn/a/b", at, SeoPageResolver.GroupLearn, Array.Empty<SeoImage>(), new[] { ok, noPoster, noFile }, "B") };
        var file = SitemapWriter.Files(urls, 100).Single(f => f.Kind == "videos");
        var xml = SitemapWriter.UrlSet(file, "https://x.com");
        Assert.Contains("<video:title>Lecture</video:title>", xml);
        Assert.DoesNotContain("No poster", xml);
        Assert.DoesNotContain("No file", xml);
        Assert.DoesNotContain(SitemapWriter.Files(new[] { urls[0] with { Videos = new[] { noPoster } } }, 100), f => f.Kind == "videos");
    }
}
