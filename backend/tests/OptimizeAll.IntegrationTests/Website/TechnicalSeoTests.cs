using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Modules.Website.Pages;
using OptimizeAll.Api.Modules.Website.SiteSeo;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Website;
using OptimizeAll.IntegrationTests.Auth;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Website;

/// <summary>
/// Technical SEO of the public site (docs/SEO_CRO.md): every public URL is served as complete HTML without JavaScript
/// (status, title, description, canonical, robots, Open Graph, Twitter, JSON-LD, content), URL normalization and
/// 404/410, noindex on private areas and the API, robots.txt with the crawler policy, the sitemap index and sitemaps,
/// llms.txt and Markdown page versions, the video block and the SEO overview/settings endpoints.
/// </summary>
public sealed class TechnicalSeoTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Base = "http://app.test";
    private static readonly HtmlParser Parser = new();

    /// <summary>Every built-in public page, with the JSON-LD types it must carry.</summary>
    public static readonly TheoryData<string, string[]> StaticPages = new()
    {
        { "/", new[] { "Organization", "WebSite", "WebPage" } },
        { "/services", new[] { "BreadcrumbList", "CollectionPage", "ItemList" } },
        { "/pricing", new[] { "BreadcrumbList", "WebPage", "OfferCatalog" } },
        { "/industries", new[] { "BreadcrumbList", "CollectionPage", "ItemList" } },
        { "/case-studies", new[] { "BreadcrumbList", "CollectionPage" } },
        { "/blog", new[] { "BreadcrumbList", "Blog" } },
        { "/team", new[] { "BreadcrumbList", "AboutPage" } },
        { "/careers", new[] { "BreadcrumbList", "CollectionPage" } },
        { "/contact", new[] { "BreadcrumbList", "ContactPage" } },
        { "/free-audit", new[] { "BreadcrumbList", "WebPage" } },
        { "/get-a-quote", new[] { "BreadcrumbList", "WebPage" } },
        { "/book-a-consultation", new[] { "BreadcrumbList", "WebPage" } },
        { "/creators", new[] { "BreadcrumbList", "FAQPage" } },
        { "/faq", new[] { "BreadcrumbList" } },
        { "/about", new[] { "BreadcrumbList", "AboutPage" } },
        { "/how-we-work", new[] { "BreadcrumbList", "AboutPage" } },
        { "/privacy-policy", new[] { "BreadcrumbList", "WebPage" } },
        { "/terms-of-service", new[] { "BreadcrumbList", "WebPage" } },
        { "/services/seo", new[] { "Service", "BreadcrumbList", "FAQPage" } },
        { "/industries/ecommerce", new[] { "BreadcrumbList" } },
    };

    private sealed record Doc(HttpStatusCode Status, IHtmlDocument Html, HttpResponseMessage Response, string Raw)
    {
        public string? Meta(string name) =>
            Html.QuerySelector($"meta[name='{name}']")?.GetAttribute("content") ?? Html.QuerySelector($"meta[property='{name}']")?.GetAttribute("content");

        public string? Canonical => Html.QuerySelector("link[rel=canonical]")?.GetAttribute("href");

        public List<JsonElement> JsonLd => Html.QuerySelectorAll("script[type='application/ld+json']")
            .Select(s => JsonDocument.Parse(s.TextContent).RootElement.Clone()).ToList();

        public IEnumerable<string> Types => JsonLd.Select(j => j.GetProperty("@type").GetString()!);
    }

    private async Task<Doc> GetDocAsync(string path)
    {
        var client = api.Anonymous();
        var response = await client.GetAsync("/_document" + path);
        var raw = await response.Content.ReadAsStringAsync();
        return new Doc(response.StatusCode, Parser.ParseDocument(raw), response, raw);
    }

    /// <summary>schema.org shape rules for the node types the site emits (what Google's rich results need).</summary>
    internal static void AssertValidJsonLd(JsonElement node)
    {
        Assert.Equal("https://schema.org", node.GetProperty("@context").GetString());
        var type = node.GetProperty("@type").GetString();
        bool Has(string p) => node.TryGetProperty(p, out var v) && v.ValueKind != JsonValueKind.Null &&
                              !(v.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetString()));
        void Abs(string url) => Assert.StartsWith("http", url);
        switch (type)
        {
            case "Organization":
                Assert.True(Has("name") && Has("url") && Has("logo"), "Organization needs name, url and logo");
                Abs(node.GetProperty("logo").GetProperty("url").GetString()!);
                break;
            case "WebSite":
                Assert.True(Has("name") && Has("url"));
                Assert.Contains("{search_term_string}", node.GetProperty("potentialAction").GetProperty("target").GetProperty("urlTemplate").GetString());
                Assert.Equal("required name=search_term_string", node.GetProperty("potentialAction").GetProperty("query-input").GetString());
                break;
            case "BreadcrumbList":
                var items = node.GetProperty("itemListElement").EnumerateArray().ToList();
                Assert.True(items.Count >= 2, "A breadcrumb trail has at least Home and the page");
                for (var i = 0; i < items.Count; i++)
                {
                    Assert.Equal(i + 1, items[i].GetProperty("position").GetInt32());
                    Assert.False(string.IsNullOrWhiteSpace(items[i].GetProperty("name").GetString()));
                    Abs(items[i].GetProperty("item").GetString()!);
                }
                break;
            case "FAQPage":
                foreach (var q in node.GetProperty("mainEntity").EnumerateArray())
                {
                    Assert.Equal("Question", q.GetProperty("@type").GetString());
                    Assert.False(string.IsNullOrWhiteSpace(q.GetProperty("name").GetString()));
                    Assert.False(string.IsNullOrWhiteSpace(q.GetProperty("acceptedAnswer").GetProperty("text").GetString()));
                }
                break;
            case "Service":
                Assert.True(Has("name") && Has("provider") && Has("url"));
                if (node.TryGetProperty("offers", out var offers))
                    foreach (var o in offers.EnumerateArray())
                        Assert.True(o.TryGetProperty("price", out _) && o.TryGetProperty("priceCurrency", out _));
                break;
            case "Article" or "BlogPosting":
                Assert.True(Has("headline") && Has("datePublished") && Has("dateModified") && Has("author") && Has("publisher"));
                Assert.True(node.GetProperty("headline").GetString()!.Length <= 110);
                break;
            case "JobPosting":
                Assert.True(Has("title") && Has("description") && Has("datePosted") && Has("hiringOrganization"));
                Assert.True(Has("jobLocation") || Has("jobLocationType"));
                break;
            case "VideoObject":
                Assert.True(Has("name") && Has("description") && Has("thumbnailUrl") && Has("uploadDate"));
                Assert.True(Has("contentUrl") || Has("embedUrl"));
                break;
            case "ItemList":
                Assert.True(node.GetProperty("itemListElement").GetArrayLength() > 0);
                break;
            case "OfferCatalog":
                Assert.True(Has("name") && node.GetProperty("itemListElement").GetArrayLength() > 0);
                break;
            case "WebPage" or "CollectionPage" or "AboutPage" or "ContactPage" or "Blog":
                Assert.True(Has("name") && Has("url"));
                break;
            case "ProfessionalService":
                Assert.True(Has("name") && Has("address"));
                break;
            default:
                Assert.Fail($"Unexpected JSON-LD type {type}");
                break;
        }
    }

    [Theory]
    [MemberData(nameof(StaticPages))]
    public async Task Public_page_is_complete_html_without_javascript(string path, string[] jsonLdTypes)
    {
        var doc = await GetDocAsync(path);
        Assert.Equal(HttpStatusCode.OK, doc.Status);
        Assert.Equal("text/html", doc.Response.Content.Headers.ContentType!.MediaType);
        Assert.False(doc.Response.Headers.Contains("X-Robots-Tag"), "Indexable pages carry no X-Robots-Tag");

        var title = doc.Html.Title!;
        Assert.InRange(title.Length, 15, 60);
        Assert.Contains("Optimize All", title);
        var description = doc.Meta("description");
        Assert.NotNull(description);
        Assert.InRange(description!.Length, 50, 155);
        Assert.Equal(Base + path.TrimEnd('/') + (path == "/" ? "/" : string.Empty), doc.Canonical);
        Assert.StartsWith("index, follow", doc.Meta("robots"));
        Assert.Equal(title, doc.Meta("og:title"));
        Assert.Equal(description, doc.Meta("og:description"));
        Assert.Equal(doc.Canonical, doc.Meta("og:url"));
        Assert.StartsWith(Base + "/", doc.Meta("og:image"));
        Assert.Equal("summary_large_image", doc.Meta("twitter:card"));
        Assert.NotNull(doc.Meta("og:image:alt"));
        Assert.Single(doc.Html.QuerySelectorAll("h1"));
        Assert.True(doc.Html.QuerySelectorAll("#oa-ssr main a[href^='/']").Length > 0 || path is "/free-audit" or "/get-a-quote" or "/book-a-consultation",
            "The content links to other pages");
        Assert.True(doc.Html.QuerySelectorAll("#oa-ssr footer a").Length > 5, "The footer links are crawlable");
        Assert.Contains(SiteSeoIncludes.Head, doc.Raw);
        Assert.Contains(SiteSeoIncludes.Body, doc.Raw);
        Assert.Equal(path == "/" ? "/index.md" : path + ".md", doc.Html.QuerySelector("link[type='text/markdown']")!.GetAttribute("href"));

        var types = doc.Types.ToList();
        foreach (var expected in jsonLdTypes) Assert.Contains(expected, types);
        foreach (var node in doc.JsonLd) AssertValidJsonLd(node);
    }

    private static class SiteSeoIncludes
    {
        public const string Head = "<!--# include virtual=\"/__shell/head.html\" -->";
        public const string Body = "<!--# include virtual=\"/__shell/body.html\" -->";
    }

    [Fact]
    public async Task Titles_and_descriptions_are_unique_across_the_indexable_site()
    {
        var titles = new Dictionary<string, string>();
        var descriptions = new Dictionary<string, string>();
        foreach (var path in StaticPages.Select(row => (string)row[0]))
        {
            var doc = await GetDocAsync(path);
            Assert.True(titles.TryAdd(doc.Html.Title!, path), $"{path} repeats the title of {titles.GetValueOrDefault(doc.Html.Title!)}");
            var d = doc.Meta("description")!;
            Assert.True(descriptions.TryAdd(d, path), $"{path} repeats the description of {descriptions.GetValueOrDefault(d)}");
        }
    }

    [Fact]
    public async Task Content_pages_render_article_job_and_page_metadata()
    {
        var now = api.Clock.GetUtcNow().UtcDateTime;
        var slug = "seo-test-" + Guid.NewGuid().ToString("N")[..8];
        await api.WithDbAsync(async db =>
        {
            db.Add(new BlogPost
            {
                Slug = slug, Title = "How technical SEO wins", Excerpt = "Why server-rendered HTML, sitemaps and structured data still decide who ranks.",
                BodyMarkdown = "## Rendering\n\nCrawlers read **HTML**. See [our services](/services).\n\n- one\n- two", ReadingMinutes = 3,
                Status = BlogPostStatus.Published, PublishedAt = now.AddDays(-1), CoverImageUrl = "/api/v1/files/" + Guid.NewGuid(), CoverImageAlt = "A chart",
            });
            db.Add(new JobOpening
            {
                Slug = slug, Title = "SEO strategist", Department = "Search", Location = "London", CountryCode = "GB", Summary = "Lead technical SEO for our clients.",
                DescriptionMarkdown = "You will own **technical SEO** audits.", Status = JobOpeningStatus.Open, PostedAt = now.AddDays(-2),
            });
            await db.SaveChangesAsync();
        });

        var post = await GetDocAsync($"/blog/{slug}");
        Assert.Equal(HttpStatusCode.OK, post.Status);
        Assert.Equal("How technical SEO wins | Optimize All", post.Html.Title);
        Assert.Equal("article", post.Meta("og:type"));
        Assert.NotNull(post.Meta("article:published_time"));
        Assert.Equal("Rendering", post.Html.QuerySelector("article h2")!.TextContent);
        Assert.Equal("HTML", post.Html.QuerySelector("article strong")!.TextContent);
        Assert.Equal("/services", post.Html.QuerySelector("article a[href='/services']")!.GetAttribute("href"));
        Assert.Contains("BlogPosting", post.Types);
        Assert.Equal("fetchpriority", post.Html.QuerySelector("#oa-ssr img")!.Attributes.First(a => a.Name == "fetchpriority").Name);
        Assert.NotNull(post.Html.QuerySelector("link[rel=preload][as=image]"));
        foreach (var node in post.JsonLd) AssertValidJsonLd(node);

        var job = await GetDocAsync($"/careers/{slug}");
        Assert.Equal(HttpStatusCode.OK, job.Status);
        Assert.Contains("JobPosting", job.Types);
        foreach (var node in job.JsonLd) AssertValidJsonLd(node);

        // A role that closed is gone for good: 410, noindex, with helpful links.
        await api.WithDbAsync(db => db.Set<JobOpening>().Where(j => j.Slug == slug).ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobOpeningStatus.Closed)));
        var closed = await GetDocAsync($"/careers/{slug}");
        Assert.Equal(HttpStatusCode.Gone, closed.Status);
        Assert.StartsWith("noindex", closed.Meta("robots"));
    }

    [Theory]
    [InlineData("/no-such-page")]
    [InlineData("/services/no-such-service")]
    [InlineData("/blog/no-such-post")]
    [InlineData("/case-studies/no-such-study")]
    [InlineData("/lp/no-such-client/page")]
    [InlineData("/c/no-such-campaign")]
    [InlineData("/services/seo/extra")]
    [InlineData("/blog?page=999")]
    public async Task Unknown_urls_are_real_404s_with_helpful_links(string path)
    {
        var doc = await GetDocAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, doc.Status);
        Assert.Equal("noindex, follow", doc.Meta("robots"));
        Assert.Equal("noindex, follow", doc.Response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Null(doc.Canonical);
        Assert.NotNull(doc.Html.QuerySelector("#oa-ssr a[href='/services']"));
        Assert.Contains(SiteSeoIncludes.Head, doc.Raw); // the app still boots and shows its own 404 page
    }

    [Theory]
    [InlineData("/services/", "/services")]
    [InlineData("/Services/SEO", "/services/seo")]
    [InlineData("/blog//", "/blog")]
    [InlineData("/index.html", "/")]
    [InlineData("/pricing/?utm_source=x", "/pricing?utm_source=x")]
    public async Task Duplicate_url_forms_redirect_permanently_to_the_canonical_form(string path, string location)
    {
        var client = api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/_document" + path);
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(location, response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/register")]
    [InlineData("/agency/website/seo")]
    [InlineData("/admin")]
    [InlineData("/app/earnings")]
    [InlineData("/p/some-token")]
    [InlineData("/join/ABC123")]
    public async Task Private_and_personal_pages_are_noindex_nofollow(string path)
    {
        var doc = await GetDocAsync(path);
        Assert.Equal(HttpStatusCode.OK, doc.Status);
        Assert.Equal("noindex, nofollow", doc.Meta("robots"));
        Assert.Equal("noindex, nofollow", doc.Response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Null(doc.Canonical);
        Assert.Empty(doc.JsonLd);
    }

    [Fact]
    public async Task Search_and_filtered_listings_are_noindex_but_followable()
    {
        foreach (var path in new[] { "/search?q=seo", "/blog?tag=ga4", "/case-studies?service=seo" })
        {
            var doc = await GetDocAsync(path);
            Assert.Equal(HttpStatusCode.OK, doc.Status);
            Assert.Equal("noindex, follow", doc.Meta("robots"));
        }
    }

    [Fact]
    public async Task Api_responses_are_noindex_but_uploaded_images_are_not()
    {
        var anon = api.Anonymous();
        var site = await anon.GetAsync("/api/v1/public/site");
        Assert.Equal("noindex", site.Headers.GetValues("X-Robots-Tag").Single());
        var file = await anon.GetAsync("/api/v1/files/" + Guid.NewGuid());
        Assert.False(file.Headers.Contains("X-Robots-Tag"));
    }

    [Fact]
    public async Task Robots_txt_lists_crawler_groups_disallows_private_areas_and_links_the_sitemap_index()
    {
        var text = await (await api.Anonymous().GetAsync("/robots.txt")).Content.ReadAsStringAsync();
        foreach (var agent in new[] { "Googlebot", "Bingbot", "GPTBot", "OAI-SearchBot", "ChatGPT-User", "ClaudeBot", "Claude-SearchBot", "anthropic-ai",
                     "PerplexityBot", "Google-Extended", "Applebot-Extended", "CCBot", "Bytespider", "Meta-ExternalAgent", "*" })
            Assert.Contains($"User-agent: {agent}\n", text);
        Assert.Contains("Sitemap: http://app.test/sitemap.xml\n", text);
        // The aggressive-scraper group is blocked by default; the everyone-else group keeps the public site open.
        var bytespider = text[text.IndexOf("User-agent: Bytespider", StringComparison.Ordinal)..];
        Assert.StartsWith("Disallow: /\n", bytespider[(bytespider.IndexOf("Disallow", StringComparison.Ordinal))..]);
        var everyone = text[text.IndexOf("User-agent: *", StringComparison.Ordinal)..];
        Assert.Contains("Allow: /\n", everyone);
        Assert.DoesNotContain("Disallow: /\n", everyone);
    }

    [Fact]
    public async Task Sitemap_index_lists_group_sitemaps_whose_urls_are_all_indexable_200_pages()
    {
        // A published post with a cover image (for the image sitemap) and a noindex one (never listed).
        var slug = "sitemap-" + Guid.NewGuid().ToString("N")[..8];
        var cover = "/api/v1/files/" + Guid.NewGuid();
        await api.WithDbAsync(async db =>
        {
            var at = api.Clock.GetUtcNow().UtcDateTime.AddHours(-1);
            db.Add(new BlogPost
            {
                Slug = slug, Title = "Sitemaps that search engines trust", Excerpt = "Only indexable, canonical, live URLs belong in a sitemap.",
                BodyMarkdown = "Text.", Status = BlogPostStatus.Published, PublishedAt = at, CoverImageUrl = cover,
            });
            db.Add(new BlogPost
            {
                Slug = slug + "-hidden", Title = "Hidden post", Excerpt = "Not for search engines.", BodyMarkdown = "Text.",
                Status = BlogPostStatus.Published, PublishedAt = at, Seo = new SeoMeta { NoIndex = true },
            });
            await db.SaveChangesAsync();
        });
        var anon = api.Anonymous();
        var indexResponse = await anon.GetAsync("/sitemap.xml");
        Assert.Equal("application/xml", indexResponse.Content.Headers.ContentType!.MediaType);
        Assert.NotNull(indexResponse.Headers.ETag);
        var index = XDocument.Parse(await indexResponse.Content.ReadAsStringAsync());
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        Assert.Equal(ns + "sitemapindex", index.Root!.Name);
        var files = index.Root.Elements(ns + "sitemap").Select(s => s.Element(ns + "loc")!.Value).ToList();
        foreach (var name in new[] { "pages", "services", "blog", "images" }) Assert.Contains($"{Base}/sitemaps/{name}.xml", files);

        // Conditional GET.
        var again = new HttpRequestMessage(HttpMethod.Get, "/sitemap.xml");
        again.Headers.IfNoneMatch.Add(indexResponse.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await anon.SendAsync(again)).StatusCode);

        var locs = new List<string>();
        foreach (var file in files.Where(f => !f.EndsWith("/images.xml", StringComparison.Ordinal) && !f.EndsWith("/videos.xml", StringComparison.Ordinal)))
        {
            var set = XDocument.Parse(await anon.GetStringAsync(file.Replace(Base, string.Empty)));
            foreach (var url in set.Root!.Elements(ns + "url"))
            {
                locs.Add(url.Element(ns + "loc")!.Value);
                Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", url.Element(ns + "lastmod")!.Value);
            }
        }
        Assert.Equal(locs.Count, locs.Distinct().Count());
        Assert.Contains($"{Base}/services/seo", locs);
        Assert.Contains($"{Base}/blog/{slug}", locs);
        Assert.DoesNotContain($"{Base}/blog/{slug}-hidden", locs);
        Assert.DoesNotContain(locs, l => l.Contains("/login") || l.Contains("/search") || l.Contains("/agency"));
        foreach (var loc in locs)
        {
            var doc = await GetDocAsync(loc[Base.Length..]);
            Assert.True(doc.Status == HttpStatusCode.OK, $"{loc} answers {doc.Status}");
            Assert.StartsWith("index", doc.Meta("robots"));
            Assert.Equal(loc, doc.Canonical);
        }

        var images = XDocument.Parse(await anon.GetStringAsync("/sitemaps/images.xml"));
        XNamespace image = "http://www.google.com/schemas/sitemap-image/1.1";
        Assert.Contains(images.Descendants(image + "loc"), l => l.Value == Base + cover);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/sitemaps/nope.xml")).StatusCode);

        // The flat legacy sitemap still answers with the same URLs.
        var legacy = XDocument.Parse(await anon.GetStringAsync("/api/v1/public/sitemap.xml"));
        Assert.Equal(locs.Count + legacy.Root!.Elements(ns + "url").Count(u => !locs.Contains(u.Element(ns + "loc")!.Value)),
            legacy.Root.Elements(ns + "url").Count());
    }

    [Fact]
    public async Task Llms_txt_and_markdown_page_versions_describe_the_site()
    {
        var anon = api.Anonymous();
        var llms = await anon.GetStringAsync("/llms.txt");
        Assert.StartsWith("# Optimize All\n\n> ", llms);
        foreach (var section in new[] { "## Key pages", "## Services", "## Machine-readable" }) Assert.Contains(section, llms);
        Assert.Contains("(http://app.test/services/seo.md): ", llms);
        Assert.Contains("(http://app.test/index.md)", llms);

        var full = await anon.GetStringAsync("/llms-full.txt");
        Assert.Contains("URL: http://app.test/services/seo", full);

        var md = await anon.GetAsync("/_markdown/services/seo");
        Assert.Equal("text/markdown", md.Content.Headers.ContentType!.MediaType);
        var text = await md.Content.ReadAsStringAsync();
        Assert.StartsWith("---\ntitle: \"", text);
        Assert.Contains("\n# ", text);
        Assert.Equal("noindex", md.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync("/_markdown/index")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/_markdown/login")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/_markdown/no-such-page")).StatusCode);

        var security = await anon.GetStringAsync("/.well-known/security.txt");
        Assert.Contains("Contact: mailto:", security);
        Assert.Matches(@"Expires: \d{4}-\d{2}-\d{2}T00:00:00Z", security);
        Assert.Contains("Canonical: http://app.test/.well-known/security.txt", security);
        Assert.Contains("/* TEAM */", await anon.GetStringAsync("/humans.txt"));
    }

    [Fact]
    public async Task Video_block_renders_an_accessible_player_videoobject_and_video_sitemap_entry()
    {
        var admin = await api.AdminAsync();
        var slug = "video-" + Guid.NewGuid().ToString("N")[..8];
        var video = new
        {
            title = "How we run an SEO audit", description = "A two-minute walkthrough of our audit process.", mp4Url = "/media/videos/seo-audit.mp4",
            webmUrl = "/media/videos/seo-audit.webm", posterUrl = "/media/videos/seo-audit.jpg", captionsUrl = "/media/videos/seo-audit.en.vtt",
            captionsLanguage = "en", durationSeconds = 125, uploadDate = "2026-09-01", transcript = "We start with **tracking**.",
        };
        // Missing captions and a non-video file are rejected.
        var invalid = await admin.PostJsonAsync("/api/v1/agency/website/pages", new
        {
            slug, title = "Video page", kind = "Standard", isPublished = true,
            blocks = new[] { new { type = PageBlockTypes.Video, data = (object)new { title = "x", mp4Url = "/media/videos/a.exe", posterUrl = "/media/videos/a.jpg" } } },
        }, 400);
        var errors = invalid.GetProperty("errors");
        Assert.True(errors.TryGetProperty("blocks[0].data.mp4Url", out _));
        Assert.True(errors.TryGetProperty("blocks[0].data.captionsUrl", out _));

        await admin.PostJsonAsync("/api/v1/agency/website/pages", new
        {
            slug, title = "Our SEO audit on video", summary = "Watch how a senior strategist audits a website, step by step, in two minutes.",
            kind = "Standard", isPublished = true, blocks = new[] { new { type = PageBlockTypes.Video, data = (object)video } },
        }, 201);

        var doc = await GetDocAsync($"/{slug}");
        Assert.Equal(HttpStatusCode.OK, doc.Status);
        var player = doc.Html.QuerySelector("#oa-ssr video")!;
        Assert.Equal("none", player.GetAttribute("preload"));
        Assert.Equal($"{Base}/media/videos/seo-audit.jpg", player.GetAttribute("poster"));
        Assert.NotNull(player.QuerySelector("track[kind=captions][srclang=en]"));
        Assert.NotNull(player.QuerySelector("source[type='video/mp4']"));
        var videoLd = doc.JsonLd.Single(j => j.GetProperty("@type").GetString() == "VideoObject");
        AssertValidJsonLd(videoLd);
        Assert.Equal("PT2M5S", videoLd.GetProperty("duration").GetString());
        Assert.Equal($"{Base}/media/videos/seo-audit.mp4", videoLd.GetProperty("contentUrl").GetString());

        var sitemap = XDocument.Parse(await api.Anonymous().GetStringAsync("/sitemaps/videos.xml"));
        XNamespace v = "http://www.google.com/schemas/sitemap-video/1.1";
        var entry = sitemap.Descendants(v + "video").Single(e => e.Element(v + "title")!.Value == video.title);
        Assert.Equal($"{Base}/media/videos/seo-audit.jpg", entry.Element(v + "thumbnail_loc")!.Value);
        Assert.Equal("125", entry.Element(v + "duration")!.Value);
    }

    [Fact]
    public async Task Seo_overview_lists_public_urls_with_warnings_for_site_managers_only()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Anonymous().GetAsync("/api/v1/agency/website/seo/overview")).StatusCode);
        var (_, participant) = await api.CreateClientAsync(Role.Participant);
        Assert.Equal(HttpStatusCode.Forbidden, (await participant.GetAsync("/api/v1/agency/website/seo/overview")).StatusCode);

        var admin = await api.AdminAsync();
        var overview = await admin.GetJsonAsync("/api/v1/agency/website/seo/overview");
        var rows = overview.GetProperty("rows").EnumerateArray().ToList();
        Assert.True(overview.GetProperty("total").GetInt32() == rows.Count && rows.Count > 30);
        var services = rows.Single(r => r.GetProperty("path").GetString() == "/services");
        Assert.True(services.GetProperty("indexable").GetBoolean());
        Assert.True(services.GetProperty("inSitemap").GetBoolean());
        Assert.Equal("services.seo.title", services.GetProperty("copyKeys").GetProperty("title").GetString());
        Assert.Contains("ItemList", services.GetProperty("jsonLdTypes").EnumerateArray().Select(t => t.GetString()));
        var search = rows.Single(r => r.GetProperty("path").GetString() == "/search");
        Assert.False(search.GetProperty("indexable").GetBoolean());
        // Built-in pages have no errors or warnings: every title and description fits and is unique.
        foreach (var path in StaticPages.Select(r => (string)r[0]).Where(p => !p.StartsWith("/services/") && !p.StartsWith("/industries/") &&
                                                                          p is not ("/about" or "/how-we-work" or "/privacy-policy" or "/terms-of-service")))
        {
            var row = rows.Single(r => r.GetProperty("path").GetString() == path);
            Assert.DoesNotContain(row.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("severity").GetString() != "notice");
        }
    }

    [Fact]
    public async Task Crawler_policy_is_editable_audited_and_applied_to_robots_txt()
    {
        var admin = await api.AdminAsync();
        var settings = await admin.GetJsonAsync("/api/v1/agency/website/seo/settings");
        var training = settings.GetProperty("crawlerGroups").EnumerateArray().Single(g => g.GetProperty("key").GetString() == "aiTraining");
        Assert.True(training.GetProperty("allowed").GetBoolean());

        await admin.PutJsonAsync("/api/v1/agency/website/seo/settings", new
        {
            crawlerGroups = new Dictionary<string, bool> { ["aiTraining"] = false, ["nope"] = true },
            indexNowEnabled = false, llmsTxtEnabled = true, concurrencyStamp = settings.GetProperty("concurrencyStamp").GetGuid(),
        }, 400);
        var saved = await admin.PutJsonAsync("/api/v1/agency/website/seo/settings", new
        {
            crawlerGroups = new Dictionary<string, bool> { ["aiTraining"] = false },
            indexNowEnabled = true, llmsTxtEnabled = false, securityContactEmail = "security@optimizeall.test",
            concurrencyStamp = settings.GetProperty("concurrencyStamp").GetGuid(),
        });
        var key = saved.GetProperty("indexNowKey").GetString()!;
        try
        {
            var robots = await api.Anonymous().GetStringAsync("/robots.txt");
            var gpt = robots[robots.IndexOf("User-agent: GPTBot", StringComparison.Ordinal)..];
            Assert.StartsWith("Disallow: /\n", gpt[gpt.IndexOf("Disallow", StringComparison.Ordinal)..]);
            Assert.Contains("# AI model training — blocked", robots);
            Assert.Equal(HttpStatusCode.NotFound, (await api.Anonymous().GetAsync("/llms.txt")).StatusCode);
            Assert.Equal(key, await api.Anonymous().GetStringAsync($"/{key}.txt"));
            Assert.Equal(HttpStatusCode.NotFound, (await api.Anonymous().GetAsync("/0123456789abcdef.txt")).StatusCode);
            Assert.Contains("Contact: mailto:security@optimizeall.test", await api.Anonymous().GetStringAsync("/.well-known/security.txt"));
            Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "website.seo_settings_updated")));

            // A stale stamp is a conflict.
            await admin.PutJsonAsync("/api/v1/agency/website/seo/settings", new
            {
                crawlerGroups = new Dictionary<string, bool>(), indexNowEnabled = false, llmsTxtEnabled = true,
                concurrencyStamp = settings.GetProperty("concurrencyStamp").GetGuid(),
            }, 409);
        }
        finally
        {
            await admin.PutJsonAsync("/api/v1/agency/website/seo/settings", new
            {
                crawlerGroups = new Dictionary<string, bool> { ["aiTraining"] = true }, indexNowEnabled = false, llmsTxtEnabled = true,
                concurrencyStamp = saved.GetProperty("concurrencyStamp").GetGuid(),
            });
        }
    }

    private sealed class FakeRedirects : ISeoRedirectLookup
    {
        public Task<SeoRedirect?> FindAsync(string path, CancellationToken ct) => Task.FromResult(path switch
        {
            "/old-seo-page" => new SeoRedirect(301, "/services/seo"),
            "/retired-offer" => new SeoRedirect(410, null),
            _ => (SeoRedirect?)null,
        });
    }

    [Fact]
    public async Task Managed_redirects_from_the_redirect_lookup_are_real_301s_and_410s()
    {
        await using var withRedirects = api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<ISeoRedirectLookup, FakeRedirects>()));
        var client = withRedirects.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var moved = await client.GetAsync("/_document/old-seo-page?utm_source=mail");
        Assert.Equal(HttpStatusCode.MovedPermanently, moved.StatusCode);
        Assert.Equal("/services/seo?utm_source=mail", moved.Headers.Location!.OriginalString);
        var gone = await client.GetAsync("/_document/retired-offer");
        Assert.Equal(HttpStatusCode.Gone, gone.StatusCode);
        Assert.Contains("noindex", gone.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/_document/services/seo")).StatusCode);
    }

    [Fact]
    public async Task IndexNow_is_off_by_default_and_only_submits_for_a_public_https_site()
    {
        using var scope = api.Services.CreateScope();
        var job = ActivatorUtilities.CreateInstance<IndexNowJob>(scope.ServiceProvider);
        Assert.Equal("IndexNow is off.", await job.ExecuteAsync(CancellationToken.None));

        var admin = await api.AdminAsync();
        var settings = await admin.GetJsonAsync("/api/v1/agency/website/seo/settings");
        var saved = await admin.PutJsonAsync("/api/v1/agency/website/seo/settings", new
        {
            crawlerGroups = new Dictionary<string, bool>(), indexNowEnabled = true, llmsTxtEnabled = true,
            concurrencyStamp = settings.GetProperty("concurrencyStamp").GetGuid(),
        });
        try
        {
            using var scope2 = api.Services.CreateScope();
            var job2 = ActivatorUtilities.CreateInstance<IndexNowJob>(scope2.ServiceProvider);
            // The test site URL is http://app.test: nothing is sent to the IndexNow endpoint.
            Assert.StartsWith("IndexNow skipped", await job2.ExecuteAsync(CancellationToken.None));
        }
        finally
        {
            await admin.PutJsonAsync("/api/v1/agency/website/seo/settings", new
            {
                crawlerGroups = new Dictionary<string, bool>(), indexNowEnabled = false, llmsTxtEnabled = true,
                concurrencyStamp = saved.GetProperty("concurrencyStamp").GetGuid(),
            });
        }
    }

    [Fact]
    public async Task Seo_settings_cannot_be_changed_while_impersonating()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        // A content editor holding site.manage through a custom role (admins cannot be impersonated).
        var editor = await api.CreateUserAsync(new[] { Role.ContentCreator });
        await api.WithDbAsync(async db =>
        {
            var role = new CustomRole
            {
                Name = "SEO editor", NormalizedName = CustomRole.Normalize("SEO editor " + Guid.NewGuid().ToString("N")[..6]), Permissions = new() { "site.manage" },
            };
            db.Add(role);
            db.Add(new UserCustomRole { UserId = editor.Id, CustomRoleId = role.Id, AssignedAt = DateTime.UtcNow });
            await db.Set<User>().Where(u => u.Id == editor.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.PermissionVersion, u => u.PermissionVersion + 1));
            await db.SaveChangesAsync();
        });
        var token = await Impersonating.TokenAsync(admin, editor.Id);
        var settings = await (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/agency/website/seo/settings", token)).ReadJsonAsync();
        await (await Impersonating.SendAsync(admin, HttpMethod.Put, "/api/v1/agency/website/seo/settings", token,
            new { crawlerGroups = new Dictionary<string, bool>(), indexNowEnabled = false, llmsTxtEnabled = true,
                concurrencyStamp = settings.GetProperty("concurrencyStamp").GetGuid() }))
            .ShouldFailAsync(403, "auth.impersonation_forbidden_action");
    }
}
