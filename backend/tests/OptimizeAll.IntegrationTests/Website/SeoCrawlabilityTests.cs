using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Api.Modules.Website.SiteSeo;
using OptimizeAll.Domain.Website;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Website;

/// <summary>
/// Crawlability additions of the 2026-09 SEO audit (docs/SEO_AUDIT_2026-09.md): generated social cards, hreflang and
/// head completeness, Article images, blog topic archives, sitemap media from contributors and the llms.txt academy guide.
/// </summary>
public sealed class SeoCrawlabilityTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Base = "http://app.test";
    private static readonly HtmlParser Parser = new();

    private async Task<AngleSharp.Html.Dom.IHtmlDocument> DocAsync(string path)
    {
        var response = await api.Anonymous().GetAsync("/_document" + path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Parser.ParseDocument(await response.Content.ReadAsStringAsync());
    }

    private static string? Meta(AngleSharp.Html.Dom.IHtmlDocument doc, string key) =>
        doc.QuerySelector($"meta[property='{key}']")?.GetAttribute("content") ?? doc.QuerySelector($"meta[name='{key}']")?.GetAttribute("content");

    private static IEnumerable<JsonElement> JsonLd(AngleSharp.Html.Dom.IHtmlDocument doc) =>
        doc.QuerySelectorAll("script[type='application/ld+json']").Select(s => JsonDocument.Parse(s.TextContent).RootElement.Clone());

    /// <summary>A blog topic with two live posts, one without a cover image. Returns the topic's slug.</summary>
    private async Task<string> SeedTopicAsync()
    {
        var now = api.Clock.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid().ToString("N")[..8];
        var category = new BlogCategory { Slug = "topic-" + id, Name = "Topic " + id, Description = "Articles about topic " + id + ": playbooks, checklists and case notes." };
        await api.WithDbAsync(async db =>
        {
            db.Add(category);
            await db.SaveChangesAsync();
            db.Add(new BlogPost
            {
                Slug = "topic-post-a-" + id, Title = "First article on topic " + id, Excerpt = "An article without a cover image, to check the generated card.",
                BodyMarkdown = "## Why\n\nBecause.", ReadingMinutes = 2, Status = BlogPostStatus.Published, PublishedAt = now.AddDays(-3),
                CategoryIds = new() { category.Id },
            });
            db.Add(new BlogPost
            {
                Slug = "topic-post-b-" + id, Title = "Second article on topic " + id, Excerpt = "An article with a cover image of its own.",
                BodyMarkdown = "## How\n\nLike this.", ReadingMinutes = 4, Status = BlogPostStatus.Published, PublishedAt = now.AddDays(-2),
                CoverImageUrl = "/api/v1/files/" + Guid.NewGuid(), CoverImageAlt = "A chart", CategoryIds = new() { category.Id },
            });
            await db.SaveChangesAsync();
        });
        return category.Slug;
    }

    private static (int Width, int Height) PngSize(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        return (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/services")]
    [InlineData("/services/seo")]
    [InlineData("/about")]
    [InlineData("/learn")]
    public async Task Pages_without_their_own_image_get_a_generated_1200x630_card(string path)
    {
        var doc = await DocAsync(path);
        var image = Meta(doc, "og:image")!;
        var cardPath = OptimizeAll.Api.Modules.Website.SiteSeo.SocialCards.SocialCardFactory.CardPath(path);
        Assert.StartsWith(Base + cardPath + "?v=", image);
        Assert.Equal(image, Meta(doc, "twitter:image"));
        Assert.Equal("1200", Meta(doc, "og:image:width"));
        Assert.Equal("630", Meta(doc, "og:image:height"));
        Assert.Equal("image/png", Meta(doc, "og:image:type"));
        Assert.Equal("summary_large_image", Meta(doc, "twitter:card"));
        Assert.False(string.IsNullOrWhiteSpace(Meta(doc, "og:image:alt")));

        var anon = api.Anonymous();
        var response = await anon.GetAsync(image[Base.Length..]);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl!.ToString());
        Assert.Equal((1200, 630), PngSize(await response.Content.ReadAsByteArrayAsync()));

        // Revalidation: the ETag is the version.
        var etag = response.Headers.ETag!.Tag;
        using var conditional = new HttpRequestMessage(HttpMethod.Get, image[Base.Length..]);
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.NotModified, (await anon.SendAsync(conditional)).StatusCode);

        // An outdated version still gets the current card, with a short cache.
        var stale = await anon.GetAsync(cardPath + "?v=000000000000");
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Equal("public, max-age=3600", stale.Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData("/og/no-such-page.png")]
    [InlineData("/og/login.png")]
    [InlineData("/og/Services.png")]
    [InlineData("/og/services.jpg")]
    public async Task Cards_exist_only_for_public_pages(string path) =>
        Assert.Equal(HttpStatusCode.NotFound, (await api.Anonymous().GetAsync(path)).StatusCode);

    [Fact]
    public async Task Course_and_lesson_pages_never_share_an_svg_as_their_social_image()
    {
        var sitemap = XDocument.Parse(await api.Anonymous().GetStringAsync("/sitemaps/learn.xml"));
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var paths = sitemap.Descendants(ns + "loc").Select(l => new Uri(l.Value).AbsolutePath).ToList();
        var course = paths.First(p => p.Count(c => c == '/') == 2);
        var lesson = paths.First(p => p.Count(c => c == '/') == 3);
        foreach (var path in new[] { course, lesson })
        {
            var doc = await DocAsync(path);
            var image = Meta(doc, "og:image")!;
            Assert.DoesNotContain(".svg", image);
            Assert.Contains("/og" + path + ".png?v=", image);
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/services/seo")]
    [InlineData("/blog")]
    public async Task Indexable_pages_declare_their_language_version(string path)
    {
        var doc = await DocAsync(path);
        var canonical = doc.QuerySelector("link[rel=canonical]")!.GetAttribute("href");
        Assert.Equal(canonical, doc.QuerySelector("link[rel=alternate][hreflang=en]")!.GetAttribute("href"));
        Assert.Equal(canonical, doc.QuerySelector("link[rel=alternate][hreflang=x-default]")!.GetAttribute("href"));
        Assert.Equal("en", doc.DocumentElement.GetAttribute("lang"));
        // The footer links the machine-readable versions of the site.
        Assert.NotNull(doc.QuerySelector("#oa-ssr footer a[href='/llms.txt']"));
        Assert.NotNull(doc.QuerySelector("#oa-ssr footer a[href='/sitemap.xml']"));
    }

    [Fact]
    public async Task Noindex_pages_carry_no_hreflang()
    {
        var response = await api.Anonymous().GetAsync("/_document/login");
        var doc = Parser.ParseDocument(await response.Content.ReadAsStringAsync());
        Assert.Null(doc.QuerySelector("link[hreflang]"));
    }

    [Fact]
    public async Task Organization_names_a_contact_point_and_articles_always_have_an_image()
    {
        var home = await DocAsync("/");
        var org = JsonLd(home).Single(n => n.GetProperty("@type").GetString() == "Organization");
        var contact = org.GetProperty("contactPoint")[0];
        Assert.Equal("ContactPoint", contact.GetProperty("@type").GetString());
        Assert.True(contact.TryGetProperty("email", out _) || contact.TryGetProperty("telephone", out _));

        await SeedTopicAsync();
        var blog = XDocument.Parse(await api.Anonymous().GetStringAsync("/sitemaps/blog.xml"));
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        foreach (var loc in blog.Descendants(ns + "loc").Select(l => new Uri(l.Value)).Where(u => u.Query.Length == 0).Take(6))
        {
            var doc = await DocAsync(loc.AbsolutePath);
            var article = JsonLd(doc).Single(n => n.GetProperty("@type").GetString() == "BlogPosting");
            var image = article.GetProperty("image");
            var first = image.ValueKind == JsonValueKind.Array ? image[0].GetString()! : image.GetString()!;
            Assert.StartsWith("http", first);
            Assert.Equal(first, Meta(doc, "og:image"));
            Assert.Equal("article", Meta(doc, "og:type"));
        }
    }

    [Fact]
    public async Task Blog_topics_are_indexable_archives_in_the_blog_sitemap()
    {
        var seeded = await SeedTopicAsync();
        var blog = XDocument.Parse(await api.Anonymous().GetStringAsync("/sitemaps/blog.xml"));
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var topics = blog.Descendants(ns + "loc").Select(l => l.Value).Where(u => u.Contains("/blog?category=", StringComparison.Ordinal)).ToList();
        Assert.Contains(Base + "/blog?category=" + seeded, topics);
        var titles = new HashSet<string>();
        foreach (var topic in topics)
        {
            var path = topic[Base.Length..];
            var doc = await DocAsync(path);
            Assert.Equal(topic, doc.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
            Assert.StartsWith("index, follow", Meta(doc, "robots"));
            Assert.True(titles.Add(doc.Title!), $"{path} repeats a topic title");
            var crumbs = JsonLd(doc).Single(n => n.GetProperty("@type").GetString() == "BreadcrumbList").GetProperty("itemListElement");
            Assert.Equal(3, crumbs.GetArrayLength());
            Assert.Equal(Base + "/blog", crumbs[1].GetProperty("item").GetString());
        }
        // Tag filters and searches stay out of the index.
        Assert.StartsWith("noindex", Meta(await DocAsync("/blog?tag=seo"), "robots"));
    }

    [Fact]
    public async Task Paginated_archives_have_their_own_titles_and_descriptions()
    {
        var anon = api.Anonymous();
        var first = await DocAsync("/blog");
        if (first.QuerySelector("link[rel=next]") is not { } next) return; // fewer posts than one page
        var secondPath = new Uri(next.GetAttribute("href")!).PathAndQuery;
        var second = await DocAsync(secondPath);
        Assert.NotEqual(first.Title, second.Title);
        Assert.NotEqual(Meta(first, "description"), Meta(second, "description"));
        Assert.StartsWith("Page 2 of ", Meta(second, "description"));
        Assert.Equal(Base + secondPath, second.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.NotNull(second.QuerySelector("link[rel=prev]"));
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync("/_document" + secondPath)).StatusCode);
    }

    [Fact]
    public async Task Listing_pages_report_the_same_last_modified_as_the_sitemap()
    {
        await SeedTopicAsync();
        var anon = api.Anonymous();
        var pages = XDocument.Parse(await anon.GetStringAsync("/sitemaps/pages.xml"));
        var blog = XDocument.Parse(await anon.GetStringAsync("/sitemaps/blog.xml"));
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var entries = pages.Descendants(ns + "url").Concat(blog.Descendants(ns + "url"))
            .Select(u => (Loc: u.Element(ns + "loc")!.Value, LastMod: u.Element(ns + "lastmod")?.Value))
            .Where(e => e.LastMod is not null && (e.Loc.EndsWith("/blog", StringComparison.Ordinal) || e.Loc.EndsWith("/careers", StringComparison.Ordinal) ||
                                                 e.Loc.EndsWith("/faq", StringComparison.Ordinal) || e.Loc.EndsWith("/creators", StringComparison.Ordinal) ||
                                                 e.Loc.Contains("?category=", StringComparison.Ordinal)))
            .ToList();
        Assert.NotEmpty(entries);
        foreach (var (loc, lastMod) in entries)
        {
            var response = await anon.GetAsync("/_document" + loc[Base.Length..]);
            var header = response.Content.Headers.LastModified;
            Assert.True(header is not null, $"{loc} has no Last-Modified");
            Assert.Equal(DateTimeOffset.Parse(lastMod!, System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeSeconds(), header!.Value.ToUnixTimeSeconds());
        }
    }

    [Fact]
    public async Task Llms_txt_links_the_academy_guide_which_lists_every_course_and_lesson()
    {
        var anon = api.Anonymous();
        var llms = await anon.GetStringAsync("/llms.txt");
        Assert.Contains("## Academy (free courses)", llms);
        Assert.Contains("(http://app.test/llms/academy.txt)", llms);
        Assert.DoesNotContain("/learn/platform-getting-started/", llms); // lessons are in the guide, not the index

        var guide = await anon.GetAsync("/llms/academy.txt");
        Assert.Equal(HttpStatusCode.OK, guide.StatusCode);
        Assert.Equal("text/plain", guide.Content.Headers.ContentType!.MediaType);
        var text = await guide.Content.ReadAsStringAsync();
        Assert.StartsWith("# Optimize All Academy", text);
        var learn = XDocument.Parse(await anon.GetStringAsync("/sitemaps/learn.xml"));
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var lessons = learn.Descendants(ns + "loc").Select(l => l.Value).Where(u => new Uri(u).AbsolutePath.Count(c => c == '/') == 3).ToList();
        Assert.NotEmpty(lessons);
        foreach (var lesson in lessons) Assert.Contains("(" + lesson + ".md)", text);

        var full = await anon.GetStringAsync("/llms-full.txt");
        Assert.DoesNotContain("URL: " + lessons[0] + "\n", full);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/llms/unknown.txt")).StatusCode);
    }

    private static string Copy(string key) =>
        OptimizeAll.Api.Modules.Content.Copy.SiteCopyCatalog.ByKey[key].Default;

    private static List<string> Pairs(string key, bool titles) => Copy(key).Split('\n').Where(l => l.Trim().Length > 0)
        .Select(l => l.Split('|', 2)).Select(p => (titles ? p[0] : p.Length > 1 ? p[1] : string.Empty).Trim()).ToList();

    private static string Text(AngleSharp.Dom.IElement? e) => System.Text.RegularExpressions.Regex.Replace(e?.TextContent ?? string.Empty, @"\s+", " ").Trim();

    [Fact]
    public async Task Home_page_renders_the_academy_first_and_the_agency_second_in_the_web_apps_words()
    {
        var doc = await DocAsync("/");
        var main = doc.QuerySelector("#oa-ssr main")!;
        var headings = main.QuerySelectorAll("h1, h2, h3").Select(Text).ToList();
        int At(string key)
        {
            var i = headings.IndexOf(Copy(key));
            Assert.True(i >= 0, $"missing heading {key}: {Copy(key)}");
            return i;
        }
        // Hero: the first call to action is the academy.
        var heroLinks = main.QuerySelectorAll("ul a").Take(2).Select(a => (Text(a), a.GetAttribute("href"))).ToList();
        Assert.Equal((Copy("home.hero.learnCta"), "/learn"), heroLinks[0]);
        Assert.Equal((Copy("home.hero.primaryCta"), "/free-audit"), heroLinks[1]);
        // Order: academy → paths → steps → certificates → agency → audit → … → trust → final.
        var order = new[]
        {
            At("home.academy.title"), At("home.academy.subjectsTitle"), At("home.academy.featuredTitle"), At("home.paths.title"),
            At("home.learnSteps.title"), At("home.cert.title"), At("home.agency.title"), At("home.audit.title"), At("home.services.title"),
            At("home.trust.title"), At("home.final.title"),
        };
        Assert.Equal(order.OrderBy(i => i), order);
        foreach (var title in Pairs("home.learnSteps.steps", titles: true).Concat(Pairs("home.trust.items", titles: true)))
            Assert.Contains(title, headings);
        var body = Text(main);
        foreach (var key in new[] { "home.academy.intro", "home.agency.intro", "home.cert.text", "home.trust.intro", "home.final.text" })
            Assert.Contains(Copy(key), body);
        foreach (var item in Copy("home.agency.proof").Split('\n')) Assert.Contains(item.Trim(), body);

        // Live academy figures, labelled with the copy.
        var facts = main.QuerySelectorAll("dl").First(dl => dl.QuerySelectorAll("dt").Any(dt => Text(dt) == Copy("home.academy.statCourses")));
        var values = facts.QuerySelectorAll("dt").Zip(facts.QuerySelectorAll("dd"), (dt, dd) => (Text(dt), Text(dd))).ToDictionary(x => x.Item1, x => x.Item2);
        var catalog = await api.Anonymous().GetJsonAsync("/api/v1/public/learning/courses?page=1&pageSize=200");
        Assert.Equal(catalog.GetProperty("total").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture), values[Copy("home.academy.statCourses")]);
        var categories = (await api.Anonymous().GetJsonAsync("/api/v1/public/learning/categories")).EnumerateArray()
            .Where(c => c.GetProperty("courseCount").GetInt32() > 0).ToList();
        Assert.Equal(categories.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), values[Copy("home.academy.statSubjects")]);
        Assert.True(int.Parse(values[Copy("home.academy.statLessons")].TrimEnd('+'), System.Globalization.CultureInfo.InvariantCulture) > 0);

        // Subjects link the filtered catalog; featured courses link their course pages; the final band links both pillars.
        foreach (var c in categories)
            Assert.NotNull(main.QuerySelector($"a[href='/learn?category={c.GetProperty("category").GetString()}']"));
        Assert.True(main.QuerySelectorAll("a[href^='/learn/']").Length >= 2, "featured courses link their course pages");
        var final = main.QuerySelectorAll("ul").Last(ul => ul.QuerySelector("a[href='/learn']") is not null && ul.QuerySelector("a[href='/free-audit']") is not null);
        Assert.Equal(Copy("home.final.learnCta"), Text(final.QuerySelector("a[href='/learn']")));
        Assert.Equal(Copy("home.final.agencyCta"), Text(final.QuerySelector("a[href='/free-audit']")));
    }

    [Fact]
    public async Task Academy_overview_renders_live_figures_and_course_links_after_its_hero()
    {
        var doc = await DocAsync("/academy");
        var main = doc.QuerySelector("#oa-ssr main")!;
        Assert.Single(main.QuerySelectorAll("h1"));
        var headings = main.QuerySelectorAll("h2").Select(Text).ToList();
        Assert.Contains(Copy("home.academy.subjectsTitle"), headings);
        Assert.Contains(Copy("home.academy.featuredTitle"), headings);
        Assert.Contains(Copy("home.cert.title"), headings);
        Assert.NotNull(main.QuerySelectorAll("dt").FirstOrDefault(dt => Text(dt) == Copy("home.academy.statCourses")));
        Assert.True(main.QuerySelectorAll("a[href^='/learn/']").Length >= 2);
        Assert.NotNull(main.QuerySelector("a[href^='/learn?category=']"));
        // The hero comes first.
        Assert.Equal("H1", main.QuerySelectorAll("h1, dl").First().TagName);
    }

    [Fact]
    public async Task Not_found_pages_and_llms_txt_point_to_both_pillars()
    {
        var response = await api.Anonymous().GetAsync("/_document/no-such-page-" + Guid.NewGuid().ToString("N")[..6]);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var doc = Parser.ParseDocument(await response.Content.ReadAsStringAsync());
        Assert.Equal("Academy", Text(doc.QuerySelector("#oa-ssr main a[href='/academy']")));
        Assert.Equal("Free courses", Text(doc.QuerySelector("#oa-ssr main a[href='/learn']")));

        var llms = await api.Anonymous().GetStringAsync("/llms.txt");
        var keyPages = llms[llms.IndexOf("## Key pages", StringComparison.Ordinal)..llms.IndexOf("## Services", StringComparison.Ordinal)];
        Assert.Contains("(http://app.test/academy.md)", keyPages);
        Assert.Contains("(http://app.test/learn.md)", keyPages);
        Assert.Contains("Optimize All Academy", llms);

        var home = await DocAsync("/");
        Assert.Equal("Optimize All: free AI, marketing and growth courses with certificates, and a full-service marketing agency",
            await DefaultImageAltAsync());
        Assert.NotNull(Meta(home, "og:image:alt"));
    }

    /// <summary>The alt text of the built-in default image (a noindex page keeps the default social image).</summary>
    private async Task<string?> DefaultImageAltAsync()
    {
        var response = await api.Anonymous().GetAsync("/_document/login");
        return Meta(Parser.ParseDocument(await response.Content.ReadAsStringAsync()), "og:image:alt");
    }

    private sealed class VideoContributor : ISitemapContributor
    {
        public string Group => SeoPageResolver.GroupLearn;

        public Task<IReadOnlyList<SitemapContribution>> UrlsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SitemapContribution>>(new[]
        {
            new SitemapContribution("/learn/test-video-course/lecture", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), "Lecture", null, new[]
            {
                new SeoVideo("Lecture video", "What the lecture covers.", "/api/v1/files/lecture", null, "/api/v1/files/poster", null, "en", null,
                    new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 420),
                new SeoVideo("YouTube lecture", "Embedded.", null, null, "https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg", null, "en",
                    "https://www.youtube-nocookie.com/embed/abcdefghijk", new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc), 300),
                new SeoVideo("No poster", "Left out of the video sitemap.", "/api/v1/files/other", null, null, null, "en", null,
                    new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), null),
            })
            {
                Images = new[] { "/api/v1/files/poster" },
            },
        });
    }

    [Fact]
    public async Task Sitemap_contributors_can_add_images_and_lecture_videos()
    {
        await using var app = api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<ISitemapContributor, VideoContributor>()));
        var client = app.CreateClient();
        XNamespace v = "http://www.google.com/schemas/sitemap-video/1.1";
        XNamespace img = "http://www.google.com/schemas/sitemap-image/1.1";
        var videos = XDocument.Parse(await client.GetStringAsync("/sitemaps/videos.xml"));
        var entry = videos.Descendants(v + "video").Single(e => e.Element(v + "title")!.Value == "Lecture video");
        Assert.Equal(Base + "/api/v1/files/poster", entry.Element(v + "thumbnail_loc")!.Value);
        Assert.Equal(Base + "/api/v1/files/lecture", entry.Element(v + "content_loc")!.Value);
        Assert.Equal("420", entry.Element(v + "duration")!.Value);
        Assert.StartsWith("2026-09-01", entry.Element(v + "publication_date")!.Value);
        Assert.DoesNotContain(videos.Descendants(v + "title"), t => t.Value == "No poster");
        var embedded = videos.Descendants(v + "video").Single(e => e.Element(v + "title")!.Value == "YouTube lecture");
        Assert.Equal("https://www.youtube-nocookie.com/embed/abcdefghijk", embedded.Element(v + "player_loc")!.Value);
        var images = XDocument.Parse(await client.GetStringAsync("/sitemaps/images.xml"));
        Assert.Contains(images.Descendants(img + "loc"), l => l.Value == Base + "/api/v1/files/poster");
    }
}
