namespace OptimizeAll.Domain.Seo;

public sealed record SeoRuleDefinition(
    string Key, string Title, string Category, SeoSeverity Severity, string WhyItMatters, string HowToFix);

/// <summary>
/// The site-audit rule catalog. Rule keys are stable (issues and audit diffs reference them); the explanatory copy is
/// seeded into <c>seo_audit_rules</c> so it can be tuned without a deploy.
/// </summary>
public static class SeoAuditRules
{
    public const string Http4xx = "http.4xx";
    public const string Http5xx = "http.5xx";
    public const string RedirectChain = "redirect.chain";
    public const string RedirectLoop = "redirect.loop";
    public const string BrokenInternalLink = "links.broken_internal";
    public const string BrokenExternalLink = "links.broken_external";
    public const string TitleMissing = "title.missing";
    public const string TitleDuplicate = "title.duplicate";
    public const string TitleTooLong = "title.too_long";
    public const string TitleTooShort = "title.too_short";
    public const string DescriptionMissing = "meta_description.missing";
    public const string DescriptionDuplicate = "meta_description.duplicate";
    public const string DescriptionTooLong = "meta_description.too_long";
    public const string DescriptionTooShort = "meta_description.too_short";
    public const string H1Missing = "h1.missing";
    public const string H1Multiple = "h1.multiple";
    public const string ImageAltMissing = "images.missing_alt";
    public const string CanonicalMissing = "canonical.missing";
    public const string CanonicalNon200 = "canonical.non_200";
    public const string CanonicalCrossDomain = "canonical.cross_domain";
    public const string NoindexInSitemap = "sitemap.noindex";
    public const string OrphanPage = "pages.orphan";
    public const string HreflangInvalid = "hreflang.invalid";
    public const string MixedContent = "security.mixed_content";
    public const string PageTooLarge = "performance.large_page";
    public const string SlowResponse = "performance.slow_response";
    public const string StructuredDataMissing = "structured_data.missing";
    public const string StructuredDataInvalid = "structured_data.invalid_json";
    public const string ThinContent = "content.thin";
    public const string DuplicateContent = "content.duplicate";
    public const string OpenGraphMissing = "social.og_missing";
    public const string ViewportMissing = "mobile.viewport_missing";
    public const string NotHttps = "security.not_https";
    public const string RobotsMissing = "robots.missing";
    public const string RobotsInvalid = "robots.invalid";
    public const string BlockedByRobots = "robots.blocked";
    public const string SitemapMissing = "sitemap.missing";
    public const string SitemapInvalid = "sitemap.invalid";

    // Thresholds used by the checks (documented in docs/SEO_CRO.md).
    public const int TitleMaxLength = 60;
    public const int TitleMinLength = 30;
    public const int DescriptionMaxLength = 160;
    public const int DescriptionMinLength = 70;
    public const int ThinContentWords = 200;
    public const long LargePageBytes = 1_000_000;
    public const int SlowResponseMs = 1500;
    public const int DuplicateContentMaxHammingDistance = 3;

    public static readonly IReadOnlyList<SeoRuleDefinition> All = new SeoRuleDefinition[]
    {
        new(Http4xx, "Pages return a 4xx error", "Crawlability", SeoSeverity.Error,
            "Search engines drop pages that answer 404/410 from the index, and visitors who land on them leave. Links pointing at them waste crawl budget and link equity.",
            "Restore the page, or 301-redirect the URL to the closest live equivalent and update internal links that still point to it."),
        new(Http5xx, "Pages return a 5xx server error", "Crawlability", SeoSeverity.Error,
            "Server errors stop crawlers from reading the page; repeated 5xx responses make search engines slow down crawling the whole site.",
            "Check the server and application logs for the failing URLs, fix the underlying error and confirm the page answers 200."),
        new(RedirectChain, "Redirect chains", "Crawlability", SeoSeverity.Warning,
            "Every extra hop adds latency and some crawlers stop following after a few redirects, so ranking signals may not reach the final page.",
            "Point the original URL (and internal links) directly at the final destination with a single 301 redirect."),
        new(RedirectLoop, "Redirect loops", "Crawlability", SeoSeverity.Error,
            "A loop never resolves to a page, so neither visitors nor crawlers can reach the content.",
            "Review the redirect rules for the listed URLs and remove the rule that sends the request back to an earlier URL."),
        new(BrokenInternalLink, "Broken internal links", "Links", SeoSeverity.Error,
            "Internal links that lead to errors frustrate visitors and leak the authority you pass between your own pages.",
            "Update each link to a working URL or remove it. Redirect the dead target if it still receives external links."),
        new(BrokenExternalLink, "Broken external links", "Links", SeoSeverity.Warning,
            "Links to dead third-party pages signal a poorly maintained page and give visitors a bad experience.",
            "Replace the link with a current source or remove it."),
        new(TitleMissing, "Missing title tag", "On-page", SeoSeverity.Error,
            "The title is the headline of your search result and one of the strongest on-page relevance signals. Without it search engines invent one.",
            "Add a unique, descriptive <title> of 30–60 characters that leads with the page's primary keyword."),
        new(TitleDuplicate, "Duplicate title tags", "On-page", SeoSeverity.Warning,
            "Identical titles make pages compete with each other and make it hard for searchers to tell results apart.",
            "Write a distinct title for every indexable page that reflects its specific topic."),
        new(TitleTooLong, "Title too long", "On-page", SeoSeverity.Warning,
            $"Titles longer than about {TitleMaxLength} characters are truncated in search results, hiding the end of your message.",
            "Shorten the title, keeping the primary keyword and value proposition near the start."),
        new(TitleTooShort, "Title too short", "On-page", SeoSeverity.Notice,
            $"Titles under {TitleMinLength} characters usually miss an opportunity to describe the page and include secondary terms.",
            "Expand the title with a benefit, qualifier or brand name."),
        new(DescriptionMissing, "Missing meta description", "On-page", SeoSeverity.Warning,
            "The meta description is your search-result pitch. Without it search engines pick a random snippet, which usually lowers click-through rate.",
            "Add a compelling 70–160 character meta description that summarizes the page and includes a call to action."),
        new(DescriptionDuplicate, "Duplicate meta descriptions", "On-page", SeoSeverity.Warning,
            "Reused descriptions make different results look the same and are often ignored by search engines.",
            "Write a unique description for each indexable page."),
        new(DescriptionTooLong, "Meta description too long", "On-page", SeoSeverity.Notice,
            $"Descriptions longer than about {DescriptionMaxLength} characters are truncated in results.",
            "Trim the description so the key message fits in the first 155–160 characters."),
        new(DescriptionTooShort, "Meta description too short", "On-page", SeoSeverity.Notice,
            $"Descriptions under {DescriptionMinLength} characters rarely give searchers a reason to click.",
            "Expand the description with the page's benefit and a clear call to action."),
        new(H1Missing, "Missing H1 heading", "On-page", SeoSeverity.Warning,
            "The H1 tells visitors and search engines what the page is about; pages without one are harder to scan and understand.",
            "Add one visible H1 that describes the page topic and includes the primary keyword naturally."),
        new(H1Multiple, "Multiple H1 headings", "On-page", SeoSeverity.Notice,
            "Several H1s dilute the page's main topic and usually indicate a template problem.",
            "Keep a single H1 and demote the other headings to H2/H3."),
        new(ImageAltMissing, "Images without alt text", "Accessibility", SeoSeverity.Warning,
            "Alt text is read by screen readers and is how search engines understand images; missing alt text is an accessibility failure (WCAG 1.1.1).",
            "Add concise alt text describing each meaningful image; use alt=\"\" only for purely decorative images."),
        new(CanonicalMissing, "Missing canonical tag", "Indexability", SeoSeverity.Notice,
            "Without a canonical, parameters and duplicate paths can split ranking signals between several URLs.",
            "Add <link rel=\"canonical\"> pointing to the preferred URL of the page (self-referencing on unique pages)."),
        new(CanonicalNon200, "Canonical points to a non-200 URL", "Indexability", SeoSeverity.Error,
            "A canonical that redirects or errors sends search engines conflicting signals, and they may ignore it or de-index the page.",
            "Point the canonical at the final, indexable 200 URL of the content."),
        new(CanonicalCrossDomain, "Canonical points to another domain", "Indexability", SeoSeverity.Warning,
            "A cross-domain canonical asks search engines to rank the other site instead of this page; if unintended it removes the page from results.",
            "Confirm syndication is intended; otherwise make the canonical self-referencing."),
        new(NoindexInSitemap, "Noindex pages listed in the sitemap", "Indexability", SeoSeverity.Error,
            "The sitemap says 'index this' while the page says 'do not index' — a contradiction that wastes crawl budget and erodes trust in the sitemap.",
            "Remove noindex pages from the XML sitemap, or remove the noindex directive if the page should rank."),
        new(OrphanPage, "Orphan pages (in the sitemap but not linked)", "Links", SeoSeverity.Warning,
            "Pages without internal links receive no internal authority and are crawled rarely, so they tend to rank poorly.",
            "Link to each orphan page from relevant pages and navigation, or remove it from the sitemap if it is obsolete."),
        new(HreflangInvalid, "Hreflang errors", "International", SeoSeverity.Warning,
            "Invalid language codes, broken targets or missing return links make search engines ignore hreflang, so visitors may land on the wrong language version.",
            "Use valid ISO 639-1 (+ optional ISO 3166-1) codes, point to live URLs and make every alternate link back (reciprocal tags)."),
        new(MixedContent, "Mixed content on HTTPS pages", "Security", SeoSeverity.Error,
            "HTTPS pages loading HTTP resources trigger browser warnings or blocked content and undermine the security signal.",
            "Load every script, stylesheet, image and frame over HTTPS (or protocol-relative to HTTPS hosts)."),
        new(PageTooLarge, "Large HTML pages", "Performance", SeoSeverity.Warning,
            "Very large HTML documents are slow to download and parse, especially on mobile networks, and may be truncated by crawlers.",
            "Remove inline data and unused markup, paginate long lists and lazy-load secondary content."),
        new(SlowResponse, "Slow server response", "Performance", SeoSeverity.Warning,
            $"Responses slower than {SlowResponseMs} ms hurt Core Web Vitals (TTFB/LCP) and reduce how much of the site gets crawled.",
            "Add caching (page/CDN), optimize slow database queries and check hosting capacity."),
        new(StructuredDataMissing, "No structured data", "Rich results", SeoSeverity.Notice,
            "Structured data (schema.org JSON-LD) makes pages eligible for rich results such as FAQs, products, reviews and breadcrumbs.",
            "Add JSON-LD appropriate to the page type (Organization, LocalBusiness, Product, Article, FAQPage, BreadcrumbList)."),
        new(StructuredDataInvalid, "Invalid JSON-LD", "Rich results", SeoSeverity.Error,
            "JSON-LD with syntax errors is ignored entirely, so the page loses its rich-result eligibility.",
            "Fix the JSON syntax (quotes, commas, brackets) and validate with Google's Rich Results Test."),
        new(ThinContent, "Thin content", "Content", SeoSeverity.Warning,
            $"Pages with fewer than {ThinContentWords} words rarely satisfy search intent and can drag down perceived site quality.",
            "Expand the page with genuinely useful information, merge it with a related page, or noindex it if it is utility content."),
        new(DuplicateContent, "Duplicate or near-duplicate content", "Content", SeoSeverity.Warning,
            "Near-identical pages compete for the same queries and split links; search engines pick one and may ignore the rest.",
            "Consolidate duplicates with 301 redirects or canonical tags, or differentiate the content."),
        new(OpenGraphMissing, "Missing Open Graph tags", "Social", SeoSeverity.Notice,
            "Without og:title, og:description and og:image, shared links render as bare URLs on social networks and messaging apps.",
            "Add og:title, og:description, og:image (1200×630) and og:url to every shareable page."),
        new(ViewportMissing, "Missing viewport meta tag", "Mobile", SeoSeverity.Warning,
            "Without a viewport tag, mobile browsers render the desktop layout zoomed out; mobile-first indexing penalizes poor mobile usability.",
            "Add <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"> to the page head."),
        new(NotHttps, "Pages served over HTTP", "Security", SeoSeverity.Error,
            "HTTPS is a ranking signal and browsers mark HTTP pages as 'Not secure', which lowers trust and conversions.",
            "Serve the whole site over HTTPS and 301-redirect every HTTP URL to its HTTPS equivalent."),
        new(RobotsMissing, "robots.txt not found", "Crawlability", SeoSeverity.Warning,
            "Without robots.txt you cannot steer crawlers away from low-value URLs or point them at your sitemap.",
            "Publish /robots.txt with the crawl rules you need and a 'Sitemap:' line."),
        new(RobotsInvalid, "robots.txt has invalid lines", "Crawlability", SeoSeverity.Warning,
            "Crawlers ignore lines they cannot parse, so rules you think are active may not be.",
            "Fix the listed lines: each rule must be 'Field: value' inside a User-agent group."),
        new(BlockedByRobots, "URLs blocked by robots.txt", "Crawlability", SeoSeverity.Notice,
            "Blocked URLs cannot be crawled; if they are linked internally, their content will not be understood by search engines.",
            "Confirm each blocked URL is meant to be private; otherwise remove the Disallow rule."),
        new(SitemapMissing, "XML sitemap not found", "Crawlability", SeoSeverity.Warning,
            "A sitemap helps search engines discover new and deep pages quickly.",
            "Generate an XML sitemap of indexable URLs and reference it in robots.txt and Search Console."),
        new(SitemapInvalid, "Invalid XML sitemap", "Crawlability", SeoSeverity.Error,
            "Search engines reject sitemaps that are not valid XML or do not follow the sitemap protocol.",
            "Fix the XML so it uses <urlset>/<sitemapindex> with <loc> entries of absolute URLs, and re-submit it."),
    };

    private static readonly Dictionary<string, SeoRuleDefinition> ByKey = All.ToDictionary(r => r.Key);

    public static SeoRuleDefinition? Find(string key) => ByKey.GetValueOrDefault(key);
}
