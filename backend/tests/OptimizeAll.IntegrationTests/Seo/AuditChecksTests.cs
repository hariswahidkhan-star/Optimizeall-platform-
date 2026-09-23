using OptimizeAll.Api.Modules.Seo.Audit;
using OptimizeAll.Api.Modules.Seo.Crawling;
using OptimizeAll.Api.Modules.Seo.Http;
using OptimizeAll.Domain.Seo;
using R = OptimizeAll.Domain.Seo.SeoAuditRules;

namespace OptimizeAll.IntegrationTests.Seo;

/// <summary>Runs the crawler + every audit check against a crafted local site where each page has exactly one defect.</summary>
public sealed class AuditChecksTests
{
    private static string Words(string seed, int count) =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => $"{seed}{i % 41}term{i}"));

    /// <summary>A page that passes every check unless a parameter introduces a defect.</summary>
    private static string Page(TestSite site, string path, string? title = "default", string? description = "default", string h1 = "default",
        string images = "<img src=\"/logo.png\" alt=\"Logo\">", string? canonical = "self", bool og = true, bool viewport = true,
        string? jsonLd = "{\"@context\":\"https://schema.org\",\"@type\":\"WebPage\"}", string? body = null, string head = "", string links = "")
    {
        var seed = new string(path.Where(char.IsLetterOrDigit).ToArray());
        title = title == "default" ? $"Audit fixture page {path} for the test site" : title;
        description = description == "default" ? $"Summary of {path}: a carefully written description of this fixture page for search result snippets." : description;
        h1 = h1 == "default" ? $"<h1>Heading {path}</h1>" : h1;
        var canonicalTag = canonical switch { null => "", "self" => $"<link rel=\"canonical\" href=\"{site.Url(path)}\">", _ => $"<link rel=\"canonical\" href=\"{canonical}\">" };
        return $"""
            <!doctype html><html lang="en"><head>
            {(title is null ? "" : $"<title>{title}</title>")}
            {(description is null ? "" : $"<meta name=\"description\" content=\"{description}\">")}
            {(viewport ? "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" : "")}
            {(og ? $"<meta property=\"og:title\" content=\"{path}\"><meta property=\"og:description\" content=\"d\"><meta property=\"og:image\" content=\"{site.Url("/og.png")}\">" : "")}
            {canonicalTag}
            {(jsonLd is null ? "" : $"<script type=\"application/ld+json\">{jsonLd}</script>")}
            {head}
            </head><body>{h1}{images}<p>{body ?? Words(seed, 260)}</p>{links}</body></html>
            """;
    }

    [Fact]
    public async Task Each_check_flags_its_crafted_page()
    {
        await using var site = await TestSite.StartAsync();
        await using var external = await TestSite.StartAsync();
        var dupBody = Words("duplicate", 300);
        var pages = new Dictionary<string, string>
        {
            ["/good"] = Page(site, "/good"),
            ["/no-title"] = Page(site, "/no-title", title: null),
            ["/dup-title-a"] = Page(site, "/dup-title-a", title: "The very same title shared by two pages"),
            ["/dup-title-b"] = Page(site, "/dup-title-b", title: "The very same title shared by two pages"),
            ["/long-title"] = Page(site, "/long-title", title: new string('L', 20) + " a title that goes on and on well beyond sixty characters"),
            ["/short-title"] = Page(site, "/short-title", title: "Short"),
            ["/no-desc"] = Page(site, "/no-desc", description: null),
            ["/dup-desc-a"] = Page(site, "/dup-desc-a", description: "Exactly the same meta description on two different pages of this fixture site."),
            ["/dup-desc-b"] = Page(site, "/dup-desc-b", description: "Exactly the same meta description on two different pages of this fixture site."),
            ["/long-desc"] = Page(site, "/long-desc", description: new string('d', 170)),
            ["/short-desc"] = Page(site, "/short-desc", description: "Too short."),
            ["/no-h1"] = Page(site, "/no-h1", h1: "<h2>Only a subheading</h2>"),
            ["/multi-h1"] = Page(site, "/multi-h1", h1: "<h1>One</h1><h1>Two</h1>"),
            ["/img-no-alt"] = Page(site, "/img-no-alt", images: "<img src=\"/a.png\"><img src=\"/b.png\" alt=\"\">"),
            ["/no-canonical"] = Page(site, "/no-canonical", canonical: null),
            ["/canonical-404"] = Page(site, "/canonical-404", canonical: site.Url("/gone")),
            ["/canonical-cross"] = Page(site, "/canonical-cross", canonical: "https://another-domain.example/page"),
            ["/hreflang"] = Page(site, "/hreflang", head: $"<link rel=\"alternate\" hreflang=\"english\" href=\"{site.Url("/good")}\"><link rel=\"alternate\" hreflang=\"fr\" href=\"{site.Url("/fr")}\">"),
            ["/fr"] = Page(site, "/fr"),
            ["/no-schema"] = Page(site, "/no-schema", jsonLd: null),
            ["/bad-jsonld"] = Page(site, "/bad-jsonld", jsonLd: "{\"@type\": \"Product\", \"name\": }"),
            ["/thin"] = Page(site, "/thin", body: "Just a few words here."),
            ["/dup-1"] = Page(site, "/dup-1", h1: "<h1>Same heading</h1>", body: dupBody),
            ["/dup-2"] = Page(site, "/dup-2", h1: "<h1>Same heading</h1>", body: dupBody + " plus"),
            ["/no-og"] = Page(site, "/no-og", og: false),
            ["/no-viewport"] = Page(site, "/no-viewport", viewport: false),
            ["/noindex"] = Page(site, "/noindex", head: "<meta name=\"robots\" content=\"noindex, follow\">"),
            ["/orphan"] = Page(site, "/orphan"),
            ["/large"] = Page(site, "/large", body: Words("large", 260) + new string(' ', 1_100_000)),
        };
        foreach (var (path, html) in pages) site.Html(path, html);
        site.Html("/slow", Page(site, "/slow"), delayMs: 1700)
            .Redirect("/chain", "/chain-2").Redirect("/chain-2", "/good", 302)
            .Redirect("/loop-a", "/loop-b").Redirect("/loop-b", "/loop-a")
            .Status("/server-error", 500)
            .Text("/robots.txt", $"User-agent: *\nDisallow: /private\nThis line is not a rule\nSitemap: {site.Url("/sitemap.xml")}\n")
            .Text("/sitemap.xml", $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>{site.Url("/")}</loc></url><url><loc>{site.Url("/good")}</loc></url>
                  <url><loc>{site.Url("/noindex")}</loc></url><url><loc>{site.Url("/orphan")}</loc></url>
                </urlset>
                """, "application/xml");
        external.Html("/fine", "<p>ok</p>");

        var linked = pages.Keys.Where(p => p is not "/orphan" and not "/noindex").Concat(new[] { "/slow", "/chain", "/loop-a", "/missing", "/server-error", "/private/area" });
        site.Html("/", Page(site, "/", links: string.Join("", linked.Select(p => $"<a href=\"{p}\">{p}</a>")) +
                                               $"<a href=\"http://localhost:{external.Port}/fine\">ok</a><a href=\"http://localhost:{external.Port}/dead\">dead</a>"));

        var (_, crawler) = CrawlerKit.Create();
        var crawl = await crawler.CrawlAsync(new CrawlRequest(site.Url("/"), 200, 5, null), CancellationToken.None);
        var outcome = AuditChecks.Run(crawl);
        var issues = outcome.Issues.ToDictionary(i => i.RuleKey);

        void Flags(string rule, string path)
        {
            Assert.True(issues.TryGetValue(rule, out var issue), $"{rule} not reported");
            Assert.Contains(issue!.Hits, h => new Uri(h.Url).AbsolutePath == path || h.Url == path);
        }
        void DoesNotFlag(string rule, string path)
        {
            if (issues.TryGetValue(rule, out var issue)) Assert.DoesNotContain(issue.Hits, h => new Uri(h.Url).AbsolutePath == path);
        }

        Flags(R.Http4xx, "/missing");
        Flags(R.Http5xx, "/server-error");
        Flags(R.RedirectChain, "/chain");
        Flags(R.RedirectLoop, "/loop-a");
        Flags(R.BrokenInternalLink, "/");
        Assert.Contains(issues[R.BrokenInternalLink].Hits, h => h.Detail!.Contains("/missing (404)"));
        Flags(R.BrokenExternalLink, "/");
        Assert.Contains(issues[R.BrokenExternalLink].Hits, h => h.Detail!.Contains("/dead"));
        Assert.DoesNotContain(issues[R.BrokenExternalLink].Hits, h => h.Detail!.Contains("/fine"));
        Flags(R.TitleMissing, "/no-title");
        Flags(R.TitleDuplicate, "/dup-title-a");
        Flags(R.TitleDuplicate, "/dup-title-b");
        Flags(R.TitleTooLong, "/long-title");
        Flags(R.TitleTooShort, "/short-title");
        Flags(R.DescriptionMissing, "/no-desc");
        Flags(R.DescriptionDuplicate, "/dup-desc-a");
        Flags(R.DescriptionTooLong, "/long-desc");
        Flags(R.DescriptionTooShort, "/short-desc");
        Flags(R.H1Missing, "/no-h1");
        Flags(R.H1Multiple, "/multi-h1");
        Flags(R.ImageAltMissing, "/img-no-alt");
        Assert.Contains(issues[R.ImageAltMissing].Hits, h => h.Detail == "1 image(s) without alt"); // alt="" (decorative) is fine
        Flags(R.CanonicalMissing, "/no-canonical");
        Flags(R.CanonicalNon200, "/canonical-404");
        Flags(R.CanonicalCrossDomain, "/canonical-cross");
        Flags(R.HreflangInvalid, "/hreflang");
        Assert.Contains(issues[R.HreflangInvalid].Hits, h => h.Detail!.Contains("english"));
        Assert.Contains(issues[R.HreflangInvalid].Hits, h => h.Detail!.Contains("no return link"));
        Flags(R.StructuredDataMissing, "/no-schema");
        Flags(R.StructuredDataInvalid, "/bad-jsonld");
        Flags(R.ThinContent, "/thin");
        Flags(R.DuplicateContent, "/dup-1");
        Flags(R.DuplicateContent, "/dup-2");
        Flags(R.OpenGraphMissing, "/no-og");
        Flags(R.ViewportMissing, "/no-viewport");
        Flags(R.NoindexInSitemap, "/noindex");
        Flags(R.OrphanPage, "/orphan");
        Flags(R.PageTooLarge, "/large");
        Flags(R.SlowResponse, "/slow");
        Flags(R.NotHttps, "/good");
        Flags(R.BlockedByRobots, "/private/area");
        Flags(R.RobotsInvalid, "/robots.txt");
        Assert.False(issues.ContainsKey(R.RobotsMissing));
        Assert.False(issues.ContainsKey(R.SitemapMissing));
        Assert.False(issues.ContainsKey(R.SitemapInvalid));

        // The clean page trips nothing except the site-wide "served over HTTP" (timing is left out: a loaded CI box may be slow).
        foreach (var rule in issues.Keys.Where(k => k is not R.NotHttps and not R.SlowResponse)) DoesNotFlag(rule, "/good");
        Assert.InRange(outcome.HealthScore, 0, 99);
        Assert.Equal(outcome.Issues.Where(i => i.Severity == SeoSeverity.Error).Sum(i => i.Hits.Count), outcome.Errors);
    }

    [Fact]
    public async Task Missing_robots_and_sitemap_are_reported()
    {
        await using var site = await TestSite.StartAsync();
        site.Html("/", "<html><head><title>Home</title></head><body><h1>Home</h1></body></html>");
        var (_, crawler) = CrawlerKit.Create();
        var outcome = AuditChecks.Run(await crawler.CrawlAsync(new CrawlRequest(site.Url("/"), 10, 2, null), CancellationToken.None));
        Assert.Contains(outcome.Issues, i => i.RuleKey == R.RobotsMissing);
        Assert.Contains(outcome.Issues, i => i.RuleKey == R.SitemapMissing);
    }

    [Fact]
    public async Task Invalid_sitemap_is_reported()
    {
        await using var site = await TestSite.StartAsync();
        site.Html("/", "<p>home</p>")
            .Text("/robots.txt", "User-agent: *\nAllow: /\n")
            .Text("/sitemap.xml", "<urlset><url><loc>not a url</loc></url><url></url>", "application/xml");
        var (_, crawler) = CrawlerKit.Create();
        var crawl = await crawler.CrawlAsync(new CrawlRequest(site.Url("/"), 10, 2, site.Url("/sitemap.xml")), CancellationToken.None);
        Assert.True(crawl.SitemapFound);
        Assert.Contains(AuditChecks.Run(crawl).Issues, i => i.RuleKey == R.SitemapInvalid);
    }

    [Fact]
    public void Mixed_content_is_flagged_on_https_pages()
    {
        const string url = "https://secure.example/page";
        var html = "<html><head><link rel=\"stylesheet\" href=\"http://cdn.example/site.css\"></head><body><img src=\"http://img.example/a.png\" alt=\"a\">" +
                   "<script src=\"https://ok.example/app.js\"></script></body></html>";
        var fetch = new FetchResult(url, url, 200, "text/html", System.Text.Encoding.UTF8.GetBytes(html), false, html.Length, 50,
            Array.Empty<RedirectHop>(), FetchErrorKind.None, null, null);
        var crawl = new CrawlResult { StartUrl = url, SiteHost = "secure.example", RobotsFound = true, SitemapFound = true };
        crawl.Pages.Add(new CrawledPage { Url = url, Depth = 0, Via = DiscoveredVia.Start, Fetch = fetch, Data = HtmlPageExtractor.Extract(html, new Uri(url)) });
        var issue = AuditChecks.Run(crawl).Issues.Single(i => i.RuleKey == R.MixedContent);
        Assert.Contains("http://cdn.example/site.css", issue.Hits[0].Detail);
        Assert.Contains("http://img.example/a.png", issue.Hits[0].Detail);
        Assert.DoesNotContain("ok.example", issue.Hits[0].Detail);
    }

    [Fact]
    public void Health_score_weights_errors_over_warnings_and_ignores_notices()
    {
        IssueDraft D(string rule, SeoSeverity s, params string[] urls) => new(rule, s, urls.Select(u => new IssueHit(u)).ToList());
        Assert.Equal(100, AuditChecks.HealthScore(new[] { D(R.CanonicalMissing, SeoSeverity.Notice, "a", "b") }, 10));
        Assert.Equal(93, AuditChecks.HealthScore(new[] { D(R.TitleMissing, SeoSeverity.Error, "a") }, 10));
        Assert.Equal(97, AuditChecks.HealthScore(new[] { D(R.DescriptionMissing, SeoSeverity.Warning, "a") }, 10));
        Assert.Equal(98, AuditChecks.HealthScore(new[] { D(R.RobotsMissing, SeoSeverity.Warning, "robots") }, 10));
        Assert.Equal(0, AuditChecks.HealthScore(new[] { D(R.Http5xx, SeoSeverity.Error, "a"), D(R.ThinContent, SeoSeverity.Warning, "a") }, 1));
    }
}
