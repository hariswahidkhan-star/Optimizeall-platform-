using System.Net;
using OptimizeAll.Api.Modules.Seo.Backlinks;
using OptimizeAll.Api.Modules.Seo.Crawling;
using OptimizeAll.Api.Modules.Seo.Http;
using OptimizeAll.Domain.Seo;

namespace OptimizeAll.IntegrationTests.Seo;

/// <summary>SSRF protection, fetch limits and crawler limits against a local loopback test site (no database, no internet).</summary>
public sealed class CrawlerTests
{
    [Fact]
    public async Task Loopback_is_blocked_unless_the_test_flag_allows_it()
    {
        await using var site = await TestSite.StartAsync();
        site.Html("/", "<html><body>hi</body></html>");

        var (blocked, _) = CrawlerKit.Create(CrawlerKit.Options(allowLoopback: false));
        var result = await blocked.GetAsync(site.Url("/"), CancellationToken.None);
        Assert.Equal(FetchErrorKind.Blocked, result.ErrorKind);
        Assert.Equal(0, site.HitCount("/"));

        var (allowed, _) = CrawlerKit.Create(CrawlerKit.Options(allowLoopback: true));
        Assert.Equal(200, (await allowed.GetAsync(site.Url("/"), CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task Host_names_resolving_to_loopback_are_blocked()
    {
        await using var site = await TestSite.StartAsync();
        site.Html("/", "<p>secret</p>");
        var (fetcher, _) = CrawlerKit.Create(CrawlerKit.Options(allowLoopback: false));

        // Real DNS: localhost → 127.0.0.1 / ::1.
        Assert.Equal(FetchErrorKind.Blocked, (await fetcher.GetAsync($"http://localhost:{site.Port}/", CancellationToken.None)).ErrorKind);

        // An innocent-looking name that resolves to 127.0.0.1.
        var (mapped, _) = CrawlerKit.Create(CrawlerKit.Options(allowLoopback: false),
            new MapResolver((host, _) => host == "innocent.test" ? new[] { IPAddress.Loopback } : null));
        var result = await mapped.GetAsync($"http://innocent.test:{site.Port}/", CancellationToken.None);
        Assert.Equal(FetchErrorKind.Blocked, result.ErrorKind);
        Assert.Contains("non-public", result.Error);
        Assert.Equal(0, site.HitCount("/"));
    }

    [Fact]
    public async Task Dns_rebinding_between_check_and_connect_is_blocked_at_connect_time()
    {
        await using var site = await TestSite.StartAsync();
        site.Html("/", "<p>secret</p>");
        // First lookup (pre-check) answers a public address, the lookup at connect time answers loopback.
        var (fetcher, _) = CrawlerKit.Create(CrawlerKit.Options(allowLoopback: false),
            new MapResolver((host, call) => host == "rebind.test" ? new[] { call == 1 ? IPAddress.Parse("93.184.216.34") : IPAddress.Loopback } : null));
        var result = await fetcher.GetAsync($"http://rebind.test:{site.Port}/", CancellationToken.None);
        Assert.Equal(FetchErrorKind.Blocked, result.ErrorKind);
        Assert.Equal(0, site.HitCount("/"));
    }

    [Fact]
    public async Task Redirects_to_private_addresses_are_blocked()
    {
        await using var site = await TestSite.StartAsync();
        site.Redirect("/metadata", "http://169.254.169.254/latest/meta-data/")
            .Redirect("/internal", "http://intranet.test/admin")
            .Redirect("/v6", "http://[fd00:ec2::254]/latest");
        var (fetcher, _) = CrawlerKit.Create(CrawlerKit.Options(allowLoopback: true),
            new MapResolver((host, _) => host == "intranet.test" ? new[] { IPAddress.Parse("10.0.0.7") } : null));

        foreach (var path in new[] { "/metadata", "/internal", "/v6" })
        {
            var result = await fetcher.GetAsync(site.Url(path), CancellationToken.None);
            Assert.Equal(FetchErrorKind.Blocked, result.ErrorKind);
            Assert.Single(result.Redirects);
            Assert.Equal(301, result.Redirects[0].StatusCode);
        }
    }

    [Fact]
    public async Task Redirect_chains_loops_body_limit_and_timeouts()
    {
        await using var site = await TestSite.StartAsync();
        site.Redirect("/a", "/b").Redirect("/b", "/c", 302).Html("/c", "<p>final</p>")
            .Redirect("/loop1", "/loop2").Redirect("/loop2", "/loop1")
            .Html("/huge", new string('x', 3 * 1024 * 1024))
            .Html("/slow", "<p>late</p>", delayMs: 3000);
        var (fetcher, _) = CrawlerKit.Create();
        var impatient = CrawlerKit.Options();
        impatient.RequestTimeoutSeconds = 1;
        var (quick, _) = CrawlerKit.Create(impatient);

        var chain = await fetcher.GetAsync(site.Url("/a"), CancellationToken.None);
        Assert.Equal(200, chain.StatusCode);
        Assert.Equal(new[] { 301, 302 }, chain.Redirects.Select(r => r.StatusCode));
        Assert.EndsWith("/c", chain.FinalUrl);

        Assert.Equal(FetchErrorKind.RedirectLoop, (await fetcher.GetAsync(site.Url("/loop1"), CancellationToken.None)).ErrorKind);

        var huge = await fetcher.GetAsync(site.Url("/huge"), CancellationToken.None);
        Assert.True(huge.Truncated);
        Assert.Equal(2 * 1024 * 1024, huge.Body.Length);

        Assert.Equal(FetchErrorKind.Timeout, (await quick.GetAsync(site.Url("/slow"), CancellationToken.None)).ErrorKind);
    }

    [Fact]
    public async Task Crawler_honours_page_and_depth_limits_robots_and_domain_scope()
    {
        await using var site = await TestSite.StartAsync();
        await using var external = await TestSite.StartAsync();
        external.Html("/", "<p>external</p>");
        site.Text("/robots.txt", "User-agent: *\nDisallow: /private\n");
        site.Html("/", $"<html><body><a href=\"/p1\">1</a><a href=\"/private/x\">p</a><a href=\"http://localhost:{external.Port}/\">ext</a></body></html>");
        for (var i = 1; i <= 30; i++) site.Html($"/p{i}", $"<html><body><a href=\"/p{i + 1}\">next</a></body></html>");
        site.Html("/private/x", "<p>private</p>");

        var (_, crawler) = CrawlerKit.Create();
        var limited = await crawler.CrawlAsync(new CrawlRequest(site.Url("/"), MaxPages: 10, MaxDepth: 20, SitemapUrl: null), CancellationToken.None);
        Assert.Equal(10, limited.Pages.Count);
        Assert.True(limited.HitPageLimit);
        Assert.Equal(0, site.HitCount("/private/x"));
        Assert.Contains(limited.BlockedByRobots, u => u.EndsWith("/private/x", StringComparison.Ordinal));
        Assert.DoesNotContain(limited.Pages, p => p.Url.Contains("localhost", StringComparison.Ordinal)); // other registrable domain: not crawled…
        Assert.Contains(limited.External.Keys, k => k.Contains("localhost", StringComparison.Ordinal)); // …but checked as an external link

        var shallow = await crawler.CrawlAsync(new CrawlRequest(site.Url("/"), MaxPages: 100, MaxDepth: 2, SitemapUrl: null), CancellationToken.None);
        Assert.Equal(new[] { "/", "/p1", "/p2" }, shallow.Pages.Select(p => new Uri(p.Url).AbsolutePath));
        Assert.All(shallow.Pages, p => Assert.True(p.Depth <= 2));
    }

    [Fact]
    public async Task Crawler_fetches_at_most_two_urls_at_a_time()
    {
        await using var site = await TestSite.StartAsync();
        site.Html("/", "<html><body>" + string.Join("", Enumerable.Range(1, 8).Select(i => $"<a href=\"/s{i}\">{i}</a>")) + "</body></html>");
        for (var i = 1; i <= 8; i++) site.Html($"/s{i}", "<p>slow</p>", delayMs: 150);
        var (_, crawler) = CrawlerKit.Create();
        var result = await crawler.CrawlAsync(new CrawlRequest(site.Url("/"), 50, 5, null), CancellationToken.None);
        Assert.Equal(9, result.Pages.Count);
        Assert.True(site.MaxConcurrent <= 2, $"peak concurrency {site.MaxConcurrent}");
    }

    [Fact]
    public async Task Robots_crawl_delay_slows_the_crawl()
    {
        await using var site = await TestSite.StartAsync();
        site.Text("/robots.txt", "User-agent: OptimizeAllBot\nCrawl-delay: 1\n");
        site.Html("/", "<a href=\"/x\">x</a><a href=\"/y\">y</a><a href=\"/z\">z</a>");
        foreach (var p in new[] { "/x", "/y", "/z" }) site.Html(p, "<p>ok</p>");
        var (_, crawler) = CrawlerKit.Create();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await crawler.CrawlAsync(new CrawlRequest(site.Url("/"), 10, 3, null), CancellationToken.None);
        Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(1.9), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task Backlink_checker_verifies_live_nofollow_and_lost_links()
    {
        await using var site = await TestSite.StartAsync();
        const string target = "https://www.nimbusfitness.test/pricing/";
        site.Html("/live", "<p>Try <a href=\"https://nimbusfitness.test/pricing\">Nimbus pricing</a></p>")
            .Html("/nofollow", "<p><a rel=\"sponsored nofollow\" href=\"https://nimbusfitness.test/pricing\">ad</a></p>")
            .Html("/gone-link", "<p>No links here any more.</p>")
            .Status("/deleted", 410);
        var (fetcher, _) = CrawlerKit.Create();
        var checker = new BacklinkChecker(null!, fetcher, TimeProvider.System);

        var live = await checker.VerifyAsync(site.Url("/live"), target, CancellationToken.None);
        Assert.Equal(BacklinkStatus.Live, live.Status);
        Assert.Equal("Nimbus pricing", live.Anchor);
        Assert.Equal(BacklinkStatus.Nofollow, (await checker.VerifyAsync(site.Url("/nofollow"), target, CancellationToken.None)).Status);
        Assert.Equal(BacklinkStatus.Lost, (await checker.VerifyAsync(site.Url("/gone-link"), target, CancellationToken.None)).Status);
        var deleted = await checker.VerifyAsync(site.Url("/deleted"), target, CancellationToken.None);
        Assert.Equal(BacklinkStatus.Lost, deleted.Status);
        Assert.Equal(410, deleted.StatusCode);

        var (blockedFetcher, _) = CrawlerKit.Create(CrawlerKit.Options(allowLoopback: false));
        var blocked = await new BacklinkChecker(null!, blockedFetcher, TimeProvider.System).VerifyAsync(site.Url("/live"), target, CancellationToken.None);
        Assert.Equal(BacklinkStatus.Error, blocked.Status);
    }
}
