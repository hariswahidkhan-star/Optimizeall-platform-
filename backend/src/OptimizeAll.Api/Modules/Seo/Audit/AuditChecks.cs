using System.Text.RegularExpressions;
using OptimizeAll.Api.Modules.Seo.Crawling;
using OptimizeAll.Api.Modules.Seo.Http;
using OptimizeAll.Domain.Seo;

namespace OptimizeAll.Api.Modules.Seo.Audit;

/// <summary>One affected URL of an issue, with an optional detail (the broken target, the duplicate title, …).</summary>
public sealed record IssueHit(string Url, string? Detail = null);

public sealed record IssueDraft(string RuleKey, SeoSeverity Severity, IReadOnlyList<IssueHit> Hits);

public sealed record AuditOutcome(IReadOnlyList<IssueDraft> Issues, int HealthScore, int Errors, int Warnings, int Notices);

/// <summary>
/// Runs every site-audit rule (<see cref="SeoAuditRules"/>) over a crawl. Pure: the same crawl always yields the same
/// issues. See docs/SEO_CRO.md for thresholds and the health-score formula.
/// </summary>
public static partial class AuditChecks
{
    /// <summary>Rules that concern the whole site rather than a share of its pages.</summary>
    private static readonly HashSet<string> SiteLevelRules = new()
    {
        SeoAuditRules.RobotsMissing, SeoAuditRules.RobotsInvalid, SeoAuditRules.SitemapMissing, SeoAuditRules.SitemapInvalid,
    };

    /// <param name="crawl">The crawled pages and site-level findings to check.</param>
    /// <param name="rules">
    /// The agency's rule settings (seo_audit_rules). Disabled rules are skipped and their severity overrides the catalog
    /// default; rules missing from the dictionary keep the catalog defaults.
    /// </param>
    public static AuditOutcome Run(CrawlResult crawl, IReadOnlyDictionary<string, SeoAuditRule>? rules = null)
    {
        var issues = new Dictionary<string, List<IssueHit>>();
        void Hit(string rule, string url, string? detail = null)
        {
            if (!issues.TryGetValue(rule, out var list)) issues[rule] = list = new List<IssueHit>();
            if (!list.Any(h => h.Url == url && h.Detail == detail)) list.Add(new IssueHit(url, detail));
        }

        var byUrl = crawl.Pages.GroupBy(p => p.Url).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var content = crawl.Pages.Where(p => p.IsContentPage).ToList();
        var indexable = content.Where(p => !p.IsNoindex).ToList();

        // --- Status codes and redirects -------------------------------------------------------------------------
        foreach (var page in crawl.Pages)
        {
            var f = page.Fetch;
            if (f.ErrorKind == FetchErrorKind.RedirectLoop) Hit(SeoAuditRules.RedirectLoop, page.Url, string.Join(" → ", f.Redirects.Select(r => r.From)));
            if (f.Redirects.Count >= 2)
                Hit(SeoAuditRules.RedirectChain, page.Url, string.Join(" → ", f.Redirects.Select(r => $"{r.StatusCode} {r.To}")));

            // A redirect whose final URL was recorded as its own page is judged there.
            if (page.Redirected && byUrl.ContainsKey(SiteCrawler.Normalize(f.FinalUrl))) continue;
            if (f.StatusCode is >= 400 and < 500) Hit(SeoAuditRules.Http4xx, page.Url, f.StatusCode.ToString());
            else if (f.StatusCode >= 500) Hit(SeoAuditRules.Http5xx, page.Url, f.StatusCode.ToString());
        }

        // --- Links -------------------------------------------------------------------------------------------------
        foreach (var page in crawl.Pages.Where(p => p.Data is not null))
        {
            foreach (var link in page.Data!.Links)
            {
                var target = SiteCrawler.Normalize(link.Href);
                if (SiteCrawler.IsSameSite(target, crawl.SiteHost))
                {
                    if (byUrl.TryGetValue(target, out var t) && FinalStatusIsError(t, byUrl, out var reason))
                        Hit(SeoAuditRules.BrokenInternalLink, page.Url, $"{target} ({reason})");
                }
                else if (crawl.External.TryGetValue(target, out var ext) && IsBroken(ext, out var why))
                    Hit(SeoAuditRules.BrokenExternalLink, page.Url, $"{target} ({why})");
            }
        }

        // --- Titles & descriptions -----------------------------------------------------------------------------------
        foreach (var page in content)
        {
            var d = page.Data!;
            if (string.IsNullOrWhiteSpace(d.Title)) Hit(SeoAuditRules.TitleMissing, page.Url);
            else if (d.Title.Length > SeoAuditRules.TitleMaxLength) Hit(SeoAuditRules.TitleTooLong, page.Url, $"{d.Title.Length} characters");
            else if (d.Title.Length < SeoAuditRules.TitleMinLength) Hit(SeoAuditRules.TitleTooShort, page.Url, $"{d.Title.Length} characters");

            if (string.IsNullOrWhiteSpace(d.MetaDescription)) Hit(SeoAuditRules.DescriptionMissing, page.Url);
            else if (d.MetaDescription.Length > SeoAuditRules.DescriptionMaxLength) Hit(SeoAuditRules.DescriptionTooLong, page.Url, $"{d.MetaDescription.Length} characters");
            else if (d.MetaDescription.Length < SeoAuditRules.DescriptionMinLength) Hit(SeoAuditRules.DescriptionTooShort, page.Url, $"{d.MetaDescription.Length} characters");

            if (d.H1.Count == 0) Hit(SeoAuditRules.H1Missing, page.Url);
            else if (d.H1.Count > 1) Hit(SeoAuditRules.H1Multiple, page.Url, $"{d.H1.Count} H1 headings");

            var missingAlt = d.Images.Count(i => i.Alt is null);
            if (missingAlt > 0) Hit(SeoAuditRules.ImageAltMissing, page.Url, $"{missingAlt} image(s) without alt");

            if (!d.HasViewport) Hit(SeoAuditRules.ViewportMissing, page.Url);
            var missingOg = new[] { "og:title", "og:description", "og:image" }.Where(k => !d.OpenGraph.ContainsKey(k)).ToList();
            if (missingOg.Count > 0) Hit(SeoAuditRules.OpenGraphMissing, page.Url, "Missing " + string.Join(", ", missingOg));

            if (d.InsecureResources.Count > 0)
                Hit(SeoAuditRules.MixedContent, page.Url, string.Join(", ", d.InsecureResources.Take(5)));

            if (d.JsonLd.Count == 0 && !d.HasMicrodata) Hit(SeoAuditRules.StructuredDataMissing, page.Url);
            foreach (var error in d.InvalidJsonLd()) Hit(SeoAuditRules.StructuredDataInvalid, page.Url, Short(error));

            CheckCanonical(page, crawl, byUrl, Hit);
            CheckHreflang(page, crawl, byUrl, Hit);
        }

        foreach (var group in indexable.Where(p => !string.IsNullOrWhiteSpace(p.Data!.Title))
                     .GroupBy(p => p.Data!.Title!.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var page in group) Hit(SeoAuditRules.TitleDuplicate, page.Url, group.Key);
        foreach (var group in indexable.Where(p => !string.IsNullOrWhiteSpace(p.Data!.MetaDescription))
                     .GroupBy(p => p.Data!.MetaDescription!.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var page in group) Hit(SeoAuditRules.DescriptionDuplicate, page.Url, Short(group.Key));

        // --- Content -------------------------------------------------------------------------------------------------
        foreach (var page in indexable.Where(p => p.Data!.WordCount < SeoAuditRules.ThinContentWords))
            Hit(SeoAuditRules.ThinContent, page.Url, $"{page.Data!.WordCount} words");

        var candidates = indexable.Where(p => p.Data!.WordCount >= 50).ToList();
        for (var i = 0; i < candidates.Count; i++)
            for (var j = i + 1; j < candidates.Count; j++)
            {
                var distance = SeoText.HammingDistance(candidates[i].Data!.SimHash, candidates[j].Data!.SimHash);
                if (distance > SeoAuditRules.DuplicateContentMaxHammingDistance) continue;
                Hit(SeoAuditRules.DuplicateContent, candidates[i].Url, $"Similar to {candidates[j].Url}");
                Hit(SeoAuditRules.DuplicateContent, candidates[j].Url, $"Similar to {candidates[i].Url}");
            }

        // --- Performance & security ------------------------------------------------------------------------------------
        foreach (var page in crawl.Pages.Where(p => p.Fetch.ErrorKind == FetchErrorKind.None && !p.Redirected))
        {
            if (page.Fetch.Truncated || page.Fetch.ContentLength > SeoAuditRules.LargePageBytes)
                Hit(SeoAuditRules.PageTooLarge, page.Url, page.Fetch.Truncated ? "Over the 2 MB fetch limit" : $"{page.Fetch.ContentLength / 1024} KB");
            if (page.Fetch.ElapsedMs > SeoAuditRules.SlowResponseMs) Hit(SeoAuditRules.SlowResponse, page.Url, $"{page.Fetch.ElapsedMs} ms");
            if (page.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && page.StatusCode == 200)
                Hit(SeoAuditRules.NotHttps, page.Url);
        }

        // --- Sitemap & robots ------------------------------------------------------------------------------------------
        foreach (var page in crawl.Pages.Where(p => p.InSitemap && p.IsContentPage && p.IsNoindex))
            Hit(SeoAuditRules.NoindexInSitemap, page.Url);
        foreach (var page in crawl.Pages.Where(p => p.InSitemap && p.IsContentPage && p.InboundLinks == 0 && p.Url != crawl.StartUrl))
            Hit(SeoAuditRules.OrphanPage, page.Url);

        if (!crawl.RobotsFound)
            Hit(SeoAuditRules.RobotsMissing, crawl.RobotsUrl, crawl.RobotsUnreachable
                ? "robots.txt could not be fetched (server error or timeout); crawlers treat the site as fully disallowed."
                : null);
        foreach (var line in crawl.RobotsInvalidLines.Take(50)) Hit(SeoAuditRules.RobotsInvalid, crawl.RobotsUrl, line);
        foreach (var url in crawl.BlockedByRobots.Take(500)) Hit(SeoAuditRules.BlockedByRobots, url);
        if (!crawl.SitemapFound) Hit(SeoAuditRules.SitemapMissing, crawl.SitemapUrl ?? new Uri(new Uri(crawl.StartUrl), "/sitemap.xml").AbsoluteUri);
        foreach (var error in crawl.SitemapErrors.Take(50)) Hit(SeoAuditRules.SitemapInvalid, crawl.SitemapUrl ?? crawl.StartUrl, error);

        var drafts = issues
            .Where(i => rules is null || !rules.TryGetValue(i.Key, out var r) || r.IsEnabled)
            .Select(i => new IssueDraft(i.Key,
                rules is not null && rules.TryGetValue(i.Key, out var r) ? r.Severity : SeoAuditRules.Find(i.Key)?.Severity ?? SeoSeverity.Notice, i.Value))
            .OrderBy(i => i.Severity).ThenByDescending(i => i.Hits.Count).ThenBy(i => i.RuleKey, StringComparer.Ordinal)
            .ToList();
        var score = HealthScore(drafts, crawl.Pages.Count);
        return new AuditOutcome(drafts, score,
            drafts.Where(d => d.Severity == SeoSeverity.Error).Sum(d => d.Hits.Count),
            drafts.Where(d => d.Severity == SeoSeverity.Warning).Sum(d => d.Hits.Count),
            drafts.Where(d => d.Severity == SeoSeverity.Notice).Sum(d => d.Hits.Count));
    }

    /// <summary>
    /// Health score 0–100: 100 × (1 − 0.7 × E − 0.3 × W) where E/W are the shares of crawled pages with at least one
    /// page-level error/warning, minus 5 points per site-level error rule and 2 per site-level warning rule. Notices do
    /// not lower the score.
    /// </summary>
    public static int HealthScore(IReadOnlyList<IssueDraft> issues, int pageCount)
    {
        var pageLevel = issues.Where(i => !SiteLevelRules.Contains(i.RuleKey)).ToList();
        var pagesWithErrors = pageLevel.Where(i => i.Severity == SeoSeverity.Error).SelectMany(i => i.Hits.Select(h => h.Url)).Distinct().Count();
        var pagesWithWarnings = pageLevel.Where(i => i.Severity == SeoSeverity.Warning).SelectMany(i => i.Hits.Select(h => h.Url)).Distinct().Count();
        var pages = Math.Max(1, pageCount);
        var score = 100.0 * (1 - 0.7 * Math.Min(1.0, (double)pagesWithErrors / pages) - 0.3 * Math.Min(1.0, (double)pagesWithWarnings / pages));
        foreach (var site in issues.Where(i => SiteLevelRules.Contains(i.RuleKey)))
            score -= site.Severity == SeoSeverity.Error ? 5 : site.Severity == SeoSeverity.Warning ? 2 : 0;
        return (int)Math.Round(Math.Clamp(score, 0, 100), MidpointRounding.AwayFromZero);
    }

    private static void CheckCanonical(CrawledPage page, CrawlResult crawl, Dictionary<string, CrawledPage> byUrl, Action<string, string, string?> hit)
    {
        var canonical = page.Data!.Canonical;
        if (string.IsNullOrWhiteSpace(canonical))
        {
            hit(SeoAuditRules.CanonicalMissing, page.Url, null);
            return;
        }
        var target = SiteCrawler.Normalize(canonical);
        if (!SiteCrawler.IsSameSite(target, crawl.SiteHost))
        {
            hit(SeoAuditRules.CanonicalCrossDomain, page.Url, target);
            return;
        }
        if (target == page.Url) return;
        int? status = null;
        var redirected = false;
        if (byUrl.TryGetValue(target, out var t)) { status = t.Fetch.Redirects.Count > 0 ? t.Fetch.Redirects[0].StatusCode : t.StatusCode; redirected = t.Redirected; }
        else if (crawl.Extra.TryGetValue(target, out var extra)) status = extra.StatusCode;
        if (status is not null && (status != 200 || redirected))
            hit(SeoAuditRules.CanonicalNon200, page.Url, $"{target} ({status})");
        else if (status is null && (byUrl.ContainsKey(target) || crawl.Extra.ContainsKey(target)))
            hit(SeoAuditRules.CanonicalNon200, page.Url, $"{target} (unreachable)");
    }

    private static void CheckHreflang(CrawledPage page, CrawlResult crawl, Dictionary<string, CrawledPage> byUrl, Action<string, string, string?> hit)
    {
        foreach (var h in page.Data!.Hreflangs)
        {
            if (!HreflangRegex().IsMatch(h.Lang) && !h.Lang.Equals("x-default", StringComparison.OrdinalIgnoreCase))
            {
                hit(SeoAuditRules.HreflangInvalid, page.Url, $"Invalid language code '{Short(h.Lang)}'");
                continue;
            }
            if (string.IsNullOrEmpty(h.Href)) { hit(SeoAuditRules.HreflangInvalid, page.Url, $"'{h.Lang}' has no URL"); continue; }
            var target = SiteCrawler.Normalize(h.Href);
            if (target == page.Url) continue;
            if (byUrl.TryGetValue(target, out var t))
            {
                if (!t.IsContentPage)
                    hit(SeoAuditRules.HreflangInvalid, page.Url, $"'{h.Lang}' points to {target} ({t.StatusCode?.ToString() ?? "unreachable"})");
                else if (!t.Data!.Hreflangs.Any(back => SiteCrawler.Normalize(back.Href) == page.Url))
                    hit(SeoAuditRules.HreflangInvalid, page.Url, $"'{h.Lang}' → {target} has no return link");
            }
            else if (crawl.Extra.TryGetValue(target, out var extra) && extra.StatusCode != 200)
                hit(SeoAuditRules.HreflangInvalid, page.Url, $"'{h.Lang}' points to {target} ({extra.StatusCode?.ToString() ?? "unreachable"})");
        }
    }

    private static bool FinalStatusIsError(CrawledPage target, Dictionary<string, CrawledPage> byUrl, out string reason)
    {
        var f = target.Fetch;
        if (f.ErrorKind != FetchErrorKind.None && f.ErrorKind != FetchErrorKind.Blocked)
        {
            reason = f.ErrorKind == FetchErrorKind.RedirectLoop ? "redirect loop" : f.Error ?? f.ErrorKind.ToString();
            return true;
        }
        if (f.StatusCode >= 400) { reason = f.StatusCode.ToString()!; return true; }
        reason = string.Empty;
        return false;
    }

    private static bool IsBroken(FetchResult f, out string reason)
    {
        if (f.ErrorKind == FetchErrorKind.Blocked) { reason = "points to a private address"; return true; }
        if (f.ErrorKind is FetchErrorKind.Network or FetchErrorKind.Timeout or FetchErrorKind.RedirectLoop or FetchErrorKind.TooManyRedirects)
        {
            reason = f.ErrorKind == FetchErrorKind.Timeout ? "timeout" : Short(f.Error ?? f.ErrorKind.ToString());
            return true;
        }
        // 401/403/429 usually mean the server refuses bots, not that the page is gone.
        if (f.StatusCode is >= 400 and not (401 or 403 or 429)) { reason = f.StatusCode.ToString()!; return true; }
        reason = string.Empty;
        return false;
    }

    private static string Short(string s) => s.Length <= 180 ? s : s[..180] + "…";

    [GeneratedRegex(@"^[a-zA-Z]{2,3}(-[a-zA-Z]{4})?(-([a-zA-Z]{2}|[0-9]{3}))?$")]
    private static partial Regex HreflangRegex();
}
