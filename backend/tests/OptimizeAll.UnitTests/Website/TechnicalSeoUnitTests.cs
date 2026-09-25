using System.Xml.Linq;
using OptimizeAll.Api.Modules.Website.SiteSeo;

namespace OptimizeAll.UnitTests.Website;

/// <summary>Pure parts of the technical SEO module: URL normalization, text limits, Markdown, sitemaps, robots.txt, video catalog.</summary>
public sealed class TechnicalSeoUnitTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray())))
            dir = dir.Parent;
        Assert.True(dir is not null, $"{string.Join('/', parts)} was not found above the test directory.");
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    [Theory]
    [InlineData("/", null)]
    [InlineData("/services", null)]
    [InlineData("/services/", "/services")]
    [InlineData("//services//seo/", "/services/seo")]
    [InlineData("/Services/SEO", "/services/seo")]
    [InlineData("/index.html", "/")]
    [InlineData("/blog/index.html", "/blog")]
    [InlineData("/p/AbCdEf", null)]
    [InlineData("/join/AbC/", "/join/AbC")]
    [InlineData("/Login", null)]
    public void Paths_normalize_to_one_lower_case_form_without_trailing_slash(string path, string? expected) =>
        Assert.Equal(expected, SeoPageResolver.NormalizePath(path));

    [Fact]
    public void Seeded_services_and_pages_ship_search_snippets_within_the_limits()
    {
        foreach (var service in OptimizeAll.Api.Modules.Website.Seed.BaselineServices.Categories.SelectMany(c => c.Services))
        {
            var description = OptimizeAll.Api.Modules.Website.Seed.BaselineSeo.Description(service.HeroBody, service.Slug);
            Assert.InRange(description.Length, 50, SeoText.DescriptionMax);
            Assert.False(description.EndsWith('…'), $"{service.Slug}: the seeded description is a cut sentence");
        }
        foreach (var page in OptimizeAll.Api.Modules.Website.Seed.BaselinePages.Pages.Where(p => p.Slug is not ("pricing" or "contact")))
        {
            var seo = OptimizeAll.Api.Modules.Website.Seed.BaselineSeo.ForPage(page.Slug, page.Summary);
            Assert.NotNull(seo.Title);
            Assert.InRange(SeoText.ApplyTemplate(seo.Title!, "%s | Optimize All", "Optimize All").Length, SeoText.TitleMin, SeoText.TitleMax);
            Assert.InRange(seo.Description!.Length, SeoText.DescriptionMin, SeoText.DescriptionMax);
        }
    }

    [Fact]
    public void Descriptions_are_cut_at_a_word_boundary()
    {
        Assert.Null(SeoText.Clamp("  "));
        Assert.Equal("Short text.", SeoText.Clamp(" Short   text. "));
        var cut = SeoText.Clamp(string.Join(' ', Enumerable.Repeat("marketing", 40)))!;
        Assert.True(cut.Length <= SeoText.DescriptionMax);
        Assert.EndsWith("marketing…", cut);
    }

    [Theory]
    [InlineData("Services", "Services | Optimize All")]
    [InlineData("Get paid | Optimize All", "Get paid | Optimize All")]
    [InlineData("Why optimize all matters", "Why optimize all matters")]
    [InlineData("Tripling organic revenue for an outdoor retailer", "Tripling organic revenue for an outdoor retailer")]
    public void The_title_template_is_applied_unless_the_title_names_the_site(string title, string expected) =>
        Assert.Equal(expected, SeoText.ApplyTemplate(title, "%s | Optimize All", "Optimize All"));

    [Fact]
    public void Markdown_renders_safe_html_with_shifted_headings()
    {
        var html = SafeMarkdown.ToHtml("# Title\n\nA **bold** and *em* [link](/services) and [bad](javascript:alert(1)).\n\n- one\n- two\n\n```\n<b>x</b>\n```\n\n<script>alert(1)</script>");
        Assert.Contains("<h2>Title</h2>", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>em</em>", html);
        Assert.Contains("<a href=\"/services\">link</a>", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.Contains("<ul><li>one</li><li>two</li></ul>", html);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Equal("## A\n### B", SafeMarkdown.ShiftHeadings("### A\n#### B", 2));
    }

    private static SitemapUrl Url(string path, string group, int images = 0, bool video = false) => new(
        path, new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), group,
        Enumerable.Range(0, images).Select(i => new SeoImage($"https://example.test/i{i}.png", null)).ToList(),
        video ? new[] { new SeoVideo("V", "D", "https://example.test/v.mp4", null, "https://example.test/p.jpg", null, "en", null, DateTime.UtcNow, 30) } : Array.Empty<SeoVideo>(),
        path);

    [Fact]
    public void Sitemaps_split_large_groups_and_derive_image_and_video_files()
    {
        var urls = Enumerable.Range(0, 5).Select(i => Url($"/blog/p{i}", SeoPageResolver.GroupBlog, images: 1))
            .Append(Url("/", SeoPageResolver.GroupPages, video: true)).ToList();
        var files = SitemapWriter.Files(urls, maxUrls: 2);
        Assert.Equal(new[] { "pages", "blog", "blog-2", "blog-3", "images", "images-2", "images-3", "videos" }, files.Select(f => f.Name));

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var index = XDocument.Parse(SitemapWriter.Index(files, "https://www.example.test"));
        Assert.Equal(8, index.Root!.Elements(ns + "sitemap").Count());
        Assert.Equal("https://www.example.test/sitemaps/blog-2.xml", index.Root.Elements(ns + "sitemap").ElementAt(2).Element(ns + "loc")!.Value);

        var video = XDocument.Parse(SitemapWriter.UrlSet(files.Single(f => f.Name == "videos"), "https://www.example.test"));
        XNamespace v = "http://www.google.com/schemas/sitemap-video/1.1";
        Assert.Equal("https://example.test/p.jpg", video.Descendants(v + "thumbnail_loc").Single().Value);
        Assert.Equal("30", video.Descendants(v + "duration").Single().Value);
        var blog = XDocument.Parse(SitemapWriter.UrlSet(files.Single(f => f.Name == "blog"), "https://www.example.test"));
        Assert.Equal("2026-09-01T12:00:00Z", blog.Descendants(ns + "lastmod").First().Value);
    }

    [Fact]
    public void Robots_txt_names_every_crawler_and_follows_the_policy()
    {
        var policy = SeoSettings.Defaults;
        var text = RobotsWriter.Write(policy, "https://www.example.test");
        foreach (var agent in CrawlerCatalog.Groups.SelectMany(g => g.UserAgents)) Assert.Contains($"User-agent: {agent}\n", text);
        Assert.Contains("Sitemap: https://www.example.test/sitemap.xml\n", text);
        Assert.Contains("Disallow: /app$\n", text);
        Assert.DoesNotContain("Disallow: /app\n", text);

        var blocked = policy with { Bots = new BotPolicy(CrawlerCatalog.Groups.ToDictionary(g => g.Key, g => g.Key != CrawlerCatalog.AiTraining)) };
        var t2 = RobotsWriter.Write(blocked, "https://www.example.test");
        Assert.Contains("# AI model training — blocked\nUser-agent: GPTBot", t2);
        Assert.Contains("# Aggressive scrapers — allowed", t2);
    }

    [Fact]
    public void Seo_settings_fall_back_to_defaults_for_new_or_unknown_groups()
    {
        var parsed = SeoSettingsService.Parse("{\"bots\":{\"groups\":{\"search\":false}},\"llmsTxtEnabled\":true}");
        Assert.False(parsed.Bots.IsAllowed(CrawlerCatalog.Search));
        Assert.True(parsed.Bots.IsAllowed(CrawlerCatalog.AiSearch));
        Assert.False(parsed.Bots.IsAllowed(CrawlerCatalog.Scrapers));
        Assert.False(parsed.IndexNow.Enabled);
        Assert.Equal(SeoSettings.Defaults.LlmsTxtEnabled, SeoSettingsService.Parse("not json").LlmsTxtEnabled);
        Assert.True(SeoSettingsService.IsIndexNowKey(SeoSettingsService.NewIndexNowKey()));
    }

    [Theory]
    [InlineData("/media/videos/intro.mp4", "mp4", true)]
    [InlineData("/media/videos/intro.webm", "webm", true)]
    [InlineData("/media/videos/intro.en.vtt", "vtt", true)]
    [InlineData("/media/videos/intro.jpg", "image", true)]
    [InlineData("/media/videos/intro.mp4", "webm", false)]
    [InlineData("/media/../secrets.mp4", "mp4", false)]
    [InlineData("/media/videos/Intro.MP4", "mp4", false)]
    public void Site_media_paths_are_checked_by_kind(string path, string kind, bool ok) =>
        Assert.Equal(ok, SiteMedia.IsSiteMediaPath(path, kind));

    [Fact]
    public void Site_video_catalog_matches_the_web_app_and_rejects_videos_without_captions()
    {
        var backend = File.ReadAllText(RepoFile("backend", "src", "OptimizeAll.Api", "Modules", "Website", "SiteSeo", "site-videos.json"));
        var frontend = File.ReadAllText(RepoFile("frontend", "src", "features", "public", "site", "siteVideos.json"));
        Assert.True(backend.ReplaceLineEndings() == frontend.ReplaceLineEndings(), "site-videos.json and siteVideos.json differ: copy one over the other.");
        Assert.NotNull(SiteVideoCatalog.Entries);

        var entry = SiteVideoCatalog.Parse("""
            {"videos":[{"path":"/","title":"Tour","mp4Url":"/media/videos/tour.mp4","posterUrl":"/media/videos/tour.jpg","captionsUrl":"/media/videos/tour.vtt","uploadDate":"2026-09-01"}]}
            """).Single();
        Assert.Equal("/", entry.Path);
        Assert.Equal(new DateOnly(2026, 9, 1), entry.UploadDate);
        Assert.Throws<InvalidOperationException>(() => SiteVideoCatalog.Parse("""
            {"videos":[{"path":"/","title":"Tour","mp4Url":"/media/videos/tour.mp4","posterUrl":"/media/videos/tour.jpg"}]}
            """));
    }
}
