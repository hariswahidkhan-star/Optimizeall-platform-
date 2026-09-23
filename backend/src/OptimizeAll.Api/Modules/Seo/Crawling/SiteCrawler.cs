using System.Diagnostics;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Modules.Seo.Http;
using OptimizeAll.Domain.Seo;

namespace OptimizeAll.Api.Modules.Seo.Crawling;

public enum DiscoveredVia
{
    Start,
    Link,
    Sitemap,
    Redirect,
}

public sealed class CrawledPage
{
    public required string Url { get; init; }
    public required int Depth { get; init; }
    public required DiscoveredVia Via { get; init; }
    public required FetchResult Fetch { get; init; }
    public PageData? Data { get; set; }
    public bool InSitemap { get; set; }
    public int InboundLinks { get; set; }

    public int? StatusCode => Fetch.StatusCode;
    public bool Redirected => Fetch.Redirects.Count > 0;

    /// <summary>A 200 HTML page answered directly (the subject of content checks).</summary>
    public bool IsContentPage => Data is not null && !Redirected && Fetch.StatusCode == 200;
    public bool IsNoindex => Data?.IsNoindex(Fetch.XRobotsTag) ?? (Fetch.XRobotsTag?.Contains("noindex", StringComparison.OrdinalIgnoreCase) ?? false);
}

public sealed record CrawlRequest(string StartUrl, int MaxPages, int MaxDepth, string? SitemapUrl);

public sealed class CrawlResult
{
    public required string StartUrl { get; init; }
    public required string SiteHost { get; init; }
    public List<CrawledPage> Pages { get; } = new();
    public string RobotsUrl { get; set; } = string.Empty;
    public bool RobotsFound { get; set; }
    public bool RobotsUnreachable { get; set; }
    public IReadOnlyList<string> RobotsInvalidLines { get; set; } = Array.Empty<string>();
    public string? SitemapUrl { get; set; }
    public bool SitemapFound { get; set; }
    public List<string> SitemapErrors { get; } = new();
    public HashSet<string> SitemapUrls { get; } = new(StringComparer.Ordinal);
    public List<string> BlockedByRobots { get; } = new();

    /// <summary>Status of external link targets (normalized URL → result).</summary>
    public Dictionary<string, FetchResult> External { get; } = new(StringComparer.Ordinal);

    /// <summary>Canonical/hreflang targets fetched outside the crawl (normalized URL → result without following redirects).</summary>
    public Dictionary<string, FetchResult> Extra { get; } = new(StringComparer.Ordinal);
    public bool HitPageLimit { get; set; }
    public bool HitTimeLimit { get; set; }

    public CrawledPage? Find(string url) =>
        Pages.FirstOrDefault(p => p.Url == SiteCrawler.Normalize(url));
}

/// <summary>
/// Polite, bounded site crawler: honours robots.txt (and its Crawl-delay), stays on the start URL's registrable domain,
/// fetches at most <see cref="SeoCrawlerOptions.PerHostConcurrency"/> URLs at a time with a pause between batches,
/// stops at the page/depth/time limits, records redirect chains, reads the XML sitemap (incl. sitemap indexes), and checks
/// external link targets and canonical/hreflang targets. All requests go through <see cref="SafeHttpFetcher"/>.
/// </summary>
public sealed class SiteCrawler(SafeHttpFetcher fetcher, IOptionsMonitor<SeoCrawlerOptions> options, ILogger<SiteCrawler> logger)
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(8);
    private const int MaxExtraChecks = 50;
    private const int MaxChildSitemaps = 10;

    public async Task<CrawlResult> CrawlAsync(CrawlRequest request, CancellationToken ct)
    {
        var o = options.CurrentValue;
        var start = new Uri(request.StartUrl);
        var maxPages = Math.Clamp(request.MaxPages, 1, o.AbsoluteMaxPages);
        var result = new CrawlResult { StartUrl = Normalize(start.AbsoluteUri), SiteHost = start.Host };
        var stopwatch = Stopwatch.StartNew();

        var robots = await LoadRobotsAsync(start, result, ct);
        var crawlDelay = robots.CrawlDelayFor(o.RobotsToken);
        var delay = TimeSpan.FromMilliseconds(Math.Max(o.DelayMilliseconds,
            Math.Min(crawlDelay ?? 0, o.MaxCrawlDelaySeconds) * 1000));

        await LoadSitemapAsync(start, request.SitemapUrl, robots, result, o, ct);

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string Url, int Depth, DiscoveredVia Via)>();
        queue.Enqueue((result.StartUrl, 0, DiscoveredVia.Start));
        var sitemapQueue = new Queue<string>(result.SitemapUrls.Where(u => IsSameSite(u, start.Host)));
        var concurrency = Math.Max(1, o.PerHostConcurrency);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (result.Pages.Count >= maxPages) { result.HitPageLimit = queue.Count > 0 || sitemapQueue.Count > 0; break; }
            if (stopwatch.Elapsed > MaxDuration) { result.HitTimeLimit = true; break; }

            var batch = new List<(string Url, int Depth, DiscoveredVia Via)>();
            while (batch.Count < concurrency && result.Pages.Count + batch.Count < maxPages)
            {
                (string Url, int Depth, DiscoveredVia Via) next;
                if (queue.Count > 0) next = queue.Dequeue();
                else if (sitemapQueue.Count > 0) next = (sitemapQueue.Dequeue(), request.MaxDepth, DiscoveredVia.Sitemap);
                else break;
                if (!visited.Add(next.Url)) continue;
                if (result.RobotsUnreachable || !robots.IsAllowed(o.RobotsToken, PathAndQuery(next.Url)))
                {
                    result.BlockedByRobots.Add(next.Url);
                    continue;
                }
                batch.Add(next);
            }
            if (batch.Count == 0) break;

            var fetched = await Task.WhenAll(batch.Select(b => fetcher.GetAsync(b.Url, ct)));
            for (var i = 0; i < batch.Count; i++)
            {
                var (url, depth, via) = batch[i];
                var fetch = fetched[i];
                var page = new CrawledPage { Url = url, Depth = depth, Via = via, Fetch = fetch };
                result.Pages.Add(page);

                CrawledPage? contentPage = page;
                if (fetch.Redirects.Count > 0 && fetch.ErrorKind == FetchErrorKind.None)
                {
                    var final = Normalize(fetch.FinalUrl);
                    if (IsSameSite(final, start.Host) && visited.Add(final) && result.Pages.Count < maxPages)
                    {
                        contentPage = new CrawledPage
                        {
                            Url = final, Depth = depth, Via = DiscoveredVia.Redirect,
                            Fetch = fetch with { RequestedUrl = final, Redirects = Array.Empty<RedirectHop>() },
                        };
                        result.Pages.Add(contentPage);
                    }
                    else contentPage = null;
                }
                if (contentPage is null || contentPage.Fetch.StatusCode != 200 || !contentPage.Fetch.IsHtml) continue;

                try
                {
                    contentPage.Data = HtmlPageExtractor.Extract(contentPage.Fetch.BodyText, new Uri(contentPage.Url));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Could not parse {Url}", contentPage.Url);
                    continue;
                }

                var nofollowPage = contentPage.Data.MetaRobots?.Contains("nofollow", StringComparison.OrdinalIgnoreCase) ?? false;
                if (nofollowPage || depth >= request.MaxDepth) continue;
                foreach (var link in contentPage.Data.Links)
                {
                    var target = Normalize(link.Href);
                    if (IsSameSite(target, start.Host) && !visited.Contains(target)) queue.Enqueue((target, depth + 1, DiscoveredVia.Link));
                }
            }

            if (queue.Count > 0 || sitemapQueue.Count > 0) await Task.Delay(delay, ct);
        }

        foreach (var page in result.Pages) page.InSitemap = result.SitemapUrls.Contains(page.Url);
        CountInboundLinks(result);
        await CheckExtraTargetsAsync(result, start.Host, ct);
        await CheckExternalLinksAsync(result, start.Host, o, ct);
        return result;
    }

    private async Task<RobotsTxt> LoadRobotsAsync(Uri start, CrawlResult result, CancellationToken ct)
    {
        result.RobotsUrl = new Uri(start, "/robots.txt").AbsoluteUri;
        var fetch = await fetcher.GetAsync(result.RobotsUrl, ct);
        if (fetch.ErrorKind == FetchErrorKind.None && fetch.StatusCode == 200)
        {
            var robots = RobotsTxt.Parse(fetch.BodyText);
            result.RobotsFound = true;
            result.RobotsInvalidLines = robots.InvalidLines;
            return robots;
        }
        // RFC 9309: 4xx = no restrictions; 5xx/unreachable = assume complete disallow.
        result.RobotsUnreachable = fetch.ErrorKind is FetchErrorKind.Timeout or FetchErrorKind.Network || fetch.StatusCode >= 500;
        return RobotsTxt.AllowAll;
    }

    private async Task LoadSitemapAsync(Uri start, string? configured, RobotsTxt robots, CrawlResult result, SeoCrawlerOptions o, CancellationToken ct)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);
        candidates.AddRange(robots.Sitemaps);
        if (candidates.Count == 0) candidates.Add(new Uri(start, "/sitemap.xml").AbsoluteUri);

        var pending = new Queue<string>(candidates.Distinct().Take(3));
        var fetchedSitemaps = 0;
        while (pending.Count > 0 && fetchedSitemaps < MaxChildSitemaps + 3)
        {
            var url = pending.Dequeue();
            fetchedSitemaps++;
            var fetch = await fetcher.GetAsync(url, ct);
            if (fetch.ErrorKind != FetchErrorKind.None || fetch.StatusCode != 200)
            {
                if (result.SitemapUrl is null && fetchedSitemaps == 1 && fetch.StatusCode is >= 400 and < 500) continue;
                if (result.SitemapFound) result.SitemapErrors.Add($"{url}: {(fetch.StatusCode?.ToString() ?? fetch.Error)}");
                continue;
            }
            result.SitemapFound = true;
            result.SitemapUrl ??= url;
            var parsed = SitemapReader.Parse(fetch.Body, o.MaxSitemapUrls);
            result.SitemapErrors.AddRange(parsed.Errors.Select(e => $"{url}: {e}"));
            if (parsed.IsIndex)
                foreach (var child in parsed.Locations.Take(MaxChildSitemaps)) pending.Enqueue(child);
            else
                foreach (var loc in parsed.Locations)
                    if (result.SitemapUrls.Count < o.MaxSitemapUrls) result.SitemapUrls.Add(Normalize(loc));
        }
    }

    private static void CountInboundLinks(CrawlResult result)
    {
        var counts = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var page in result.Pages.Where(p => p.Data is not null))
        {
            foreach (var link in page.Data!.Links)
            {
                var target = Normalize(link.Href);
                if (target == page.Url) continue;
                if (!counts.TryGetValue(target, out var sources)) counts[target] = sources = new HashSet<string>(StringComparer.Ordinal);
                sources.Add(page.Url);
            }
        }
        foreach (var page in result.Pages)
            page.InboundLinks = counts.TryGetValue(page.Url, out var s) ? s.Count : 0;
    }

    private async Task CheckExtraTargetsAsync(CrawlResult result, string siteHost, CancellationToken ct)
    {
        var crawled = result.Pages.Select(p => p.Url).ToHashSet(StringComparer.Ordinal);
        var targets = result.Pages.Where(p => p.Data is not null)
            .SelectMany(p => p.Data!.Hreflangs.Select(h => h.Href).Append(p.Data.Canonical ?? string.Empty))
            .Where(u => u.Length > 0).Select(Normalize)
            .Where(u => !crawled.Contains(u) && IsSameSite(u, siteHost))
            .Distinct().Take(MaxExtraChecks).ToList();
        foreach (var chunk in targets.Chunk(2))
        {
            var fetched = await Task.WhenAll(chunk.Select(u => fetcher.GetAsync(u, ct, followRedirects: false)));
            for (var i = 0; i < chunk.Length; i++) result.Extra[chunk[i]] = fetched[i];
        }
    }

    private async Task CheckExternalLinksAsync(CrawlResult result, string siteHost, SeoCrawlerOptions o, CancellationToken ct)
    {
        var targets = result.Pages.Where(p => p.Data is not null)
            .SelectMany(p => p.Data!.Links.Select(l => Normalize(l.Href)))
            .Where(u => !IsSameSite(u, siteHost))
            .Distinct().Take(o.MaxExternalLinkChecks).ToList();
        foreach (var chunk in targets.Chunk(Math.Max(1, o.PerHostConcurrency) * 2))
        {
            var fetched = await Task.WhenAll(chunk.Select(async u =>
            {
                var head = await fetcher.HeadAsync(u, ct);
                // Many servers reject HEAD; confirm failures with a GET before calling the link broken.
                return head.ErrorKind is FetchErrorKind.None && head.StatusCode < 400 ? head : await fetcher.GetAsync(u, ct);
            }));
            for (var i = 0; i < chunk.Length; i++) result.External[chunk[i]] = fetched[i];
        }
    }

    /// <summary>Canonical form for de-duplication: lower-case scheme/host, default port dropped, fragment removed.</summary>
    public static string Normalize(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        var value = uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped);
        return value;
    }

    public static bool IsSameSite(string url, string siteHost) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && RegistrableDomain.SameSite(uri.Host, siteHost);

    private static string PathAndQuery(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.PathAndQuery : "/";
}
