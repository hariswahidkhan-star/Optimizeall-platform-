using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>
/// Server-rendered HTML for every public URL (docs/SEO_CRO.md § Rendering). The web server sends each page request that is
/// not a static file here as <c>/_document{path}?{query}</c> and fills the two shell includes; the React app then boots
/// over it. Answers the page's real status: 200, 301 (URL normalization and managed redirects), 404 or 410.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Documents)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class SeoDocumentController(SeoPageResolver resolver, IConfiguration configuration, ILogger<SeoDocumentController> logger)
    : ControllerBase
{
    public const string Prefix = "/_document";

    [HttpGet("/_document/{**path}")]
    [HttpHead("/_document/{**path}")]
    public async Task<IActionResult> Document(CancellationToken ct)
    {
        var raw = Request.Path.Value ?? Prefix;
        var path = raw.Length > Prefix.Length ? raw[Prefix.Length..] : "/";
        SeoPage page;
        string html;
        try
        {
            // Optional canonical-host redirect (Website:Seo:CanonicalHostRedirect): any other host name (the platform's
            // default *.onrender.com address, the bare domain) is sent to the site URL's host with a 301.
            if (configuration.GetValue("Website:Seo:CanonicalHostRedirect", false))
            {
                await resolver.EnsureLoadedAsync(ct);
                if (resolver.Settings.Seo.SiteUrl is { } siteUrl && Uri.TryCreate(siteUrl, UriKind.Absolute, out var site) &&
                    !string.Equals(Request.Host.Host, site.Host, StringComparison.OrdinalIgnoreCase))
                {
                    Response.Headers.Location = site.GetLeftPart(UriPartial.Authority) + path + Request.QueryString.Value;
                    Response.Headers.CacheControl = "public, max-age=3600";
                    return StatusCode(StatusCodes.Status301MovedPermanently);
                }
            }
            page = await resolver.ResolveAsync(path, Request.QueryString.Value, ct);
            if (page.RedirectTo is not null)
            {
                Response.Headers.Location = page.RedirectTo;
                Response.Headers.CacheControl = page.Status == 301 || page.Status == 308 ? "public, max-age=3600" : "no-cache";
                return StatusCode(page.Status);
            }
            html = SeoDocumentWriter.Write(page, await resolver.ChromeAsync(ct), resolver.Settings.SiteName, resolver.Settings.Seo.TwitterHandle,
                resolver.BaseUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never leave a visitor without the app: answer 503 (crawlers retry later) with the plain shell.
            logger.LogError(ex, "Rendering {Path} failed", path);
            Response.Headers.RetryAfter = "60";
            Response.Headers.CacheControl = "no-store";
            return new ContentResult
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
                ContentType = "text/html; charset=utf-8",
                Content = "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"UTF-8\">\n<title>Optimize All</title>\n<meta name=\"robots\" content=\"noindex\">\n"
                          + SeoDocumentWriter.ShellHeadInclude + "\n</head>\n<body>\n<div id=\"root\"></div>\n" + SeoDocumentWriter.ShellBodyInclude + "\n</body>\n</html>\n",
            };
        }

        if (!page.IsIndexable) Response.Headers["X-Robots-Tag"] = page.Robots;
        Response.Headers.ContentLanguage = "en";
        // HTML is revalidated on every visit (the shell's hashed asset names change on deploy); the ETag makes that a 304.
        Response.Headers.CacheControl = "no-cache";
        var etag = "W/\"" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)))[..32].ToLowerInvariant() + "\"";
        Response.Headers.ETag = etag;
        if (page.ModifiedAt is { } modified)
            Response.Headers.LastModified = DateTime.SpecifyKind(modified, DateTimeKind.Utc).ToString("R", CultureInfo.InvariantCulture);
        if (page.Status == 200 && Request.Headers.IfNoneMatch.ToString() == etag) return StatusCode(StatusCodes.Status304NotModified);
        return new ContentResult { StatusCode = page.Status, ContentType = "text/html; charset=utf-8", Content = html };
    }
}

/// <summary>robots.txt, sitemaps, llms.txt, Markdown page versions, security.txt, humans.txt and the IndexNow key file.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Documents)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class SeoFilesController(SeoPageResolver resolver, SeoSettingsService settings, LlmsTxtService llms, IConfiguration configuration,
    TimeProvider clock) : ControllerBase
{
    private int MaxUrls => Math.Clamp(configuration.GetValue("Website:Seo:SitemapMaxUrls", 45_000), 1, 50_000);

    /// <summary>Text or XML with a strong ETag (304 on If-None-Match) and an optional Last-Modified.</summary>
    private IActionResult Cached(string body, string contentType, int maxAgeSeconds, DateTime? lastModified = null)
    {
        var etag = "\"" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)))[..32].ToLowerInvariant() + "\"";
        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = $"public, max-age={maxAgeSeconds}";
        if (lastModified is { } m) Response.Headers.LastModified = DateTime.SpecifyKind(m, DateTimeKind.Utc).ToString("R", CultureInfo.InvariantCulture);
        if (Request.Headers.IfNoneMatch.ToString().Split(',').Select(s => s.Trim()).Contains(etag)) return StatusCode(StatusCodes.Status304NotModified);
        return Content(body, contentType);
    }

    [HttpGet("/robots.txt")]
    public async Task<IActionResult> Robots(CancellationToken ct)
    {
        await resolver.EnsureLoadedAsync(ct);
        return Cached(RobotsWriter.Write(await settings.GetAsync(ct), resolver.BaseUrl), "text/plain; charset=utf-8", 3600);
    }

    /// <summary>The sitemap index: one sitemap per content group, plus images and videos.</summary>
    [HttpGet("/sitemap.xml")]
    public async Task<IActionResult> SitemapIndex(CancellationToken ct)
    {
        var urls = await resolver.SitemapUrlsAsync(ct);
        var files = SitemapWriter.Files(urls, MaxUrls);
        return Cached(SitemapWriter.Index(files, resolver.BaseUrl), "application/xml; charset=utf-8", 300, files.Max(f => f.LastModified));
    }

    [HttpGet("/sitemaps/{name}.xml")]
    public async Task<IActionResult> Sitemap(string name, CancellationToken ct)
    {
        var urls = await resolver.SitemapUrlsAsync(ct);
        var file = SitemapWriter.Files(urls, MaxUrls).FirstOrDefault(f => f.Name == name);
        if (file is null) return NotFound();
        return Cached(SitemapWriter.UrlSet(file, resolver.BaseUrl), "application/xml; charset=utf-8", 300, file.LastModified);
    }

    /// <summary>Legacy flat sitemap (every indexable URL in one urlset); kept for links submitted before the sitemap index.</summary>
    [HttpGet("/api/v1/public/sitemap.xml")]
    public async Task<IActionResult> LegacySitemap(CancellationToken ct)
    {
        var urls = await resolver.SitemapUrlsAsync(ct);
        var file = new SitemapFile("all", "urls", urls.Take(50_000).ToList(), urls.Max(u => u.LastModified));
        return Cached(SitemapWriter.UrlSet(file, resolver.BaseUrl), "application/xml; charset=utf-8", 300, file.LastModified);
    }

    [HttpGet("/llms.txt")]
    public async Task<IActionResult> LlmsTxt(CancellationToken ct)
    {
        if (!(await settings.GetAsync(ct)).LlmsTxtEnabled) return NotFound();
        return Cached(await llms.LlmsTxtAsync(ct), "text/plain; charset=utf-8", 3600);
    }

    [HttpGet("/llms-full.txt")]
    public async Task<IActionResult> LlmsFull(CancellationToken ct)
    {
        if (!(await settings.GetAsync(ct)).LlmsTxtEnabled) return NotFound();
        return Cached(await llms.LlmsFullAsync(ct), "text/plain; charset=utf-8", 3600);
    }

    /// <summary>Markdown version of a page: the web server sends <c>/{path}.md</c> here as <c>/_markdown/{path}</c>.</summary>
    [HttpGet("/_markdown/{**path}")]
    public async Task<IActionResult> Markdown(CancellationToken ct)
    {
        if (!(await settings.GetAsync(ct)).LlmsTxtEnabled) return NotFound();
        var raw = Request.Path.Value ?? string.Empty;
        var pagePath = SeoMarkdownPaths.PagePath(raw["/_markdown".Length..] + ".md");
        if (pagePath is null) return NotFound();
        var md = await llms.PageMarkdownAsync(pagePath, ct);
        if (md is null) return NotFound();
        Response.Headers["X-Robots-Tag"] = "noindex";
        Response.Headers.Link = $"<{resolver.Absolute(pagePath)}>; rel=\"canonical\"";
        return Cached(md, "text/markdown; charset=utf-8", 3600);
    }

    /// <summary>RFC 9116 security contact (expires 180 days ahead; regenerated on every request).</summary>
    [HttpGet("/.well-known/security.txt")]
    public async Task<IActionResult> SecurityTxt(CancellationToken ct)
    {
        await resolver.EnsureLoadedAsync(ct);
        var s = await settings.GetAsync(ct);
        var email = s.SecurityContactEmail ?? resolver.Settings.Contact.Email;
        if (email is null) return NotFound();
        var expires = clock.GetUtcNow().UtcDateTime.Date.AddDays(180);
        var body = new StringBuilder()
            .Append("Contact: mailto:").Append(email).Append('\n')
            .Append("Expires: ").Append(expires.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append('\n')
            .Append("Preferred-Languages: en\n")
            .Append("Canonical: ").Append(resolver.Absolute("/.well-known/security.txt")).Append('\n').ToString();
        return Cached(body, "text/plain; charset=utf-8", 86400);
    }

    [HttpGet("/humans.txt")]
    public async Task<IActionResult> HumansTxt(CancellationToken ct)
    {
        await resolver.EnsureLoadedAsync(ct);
        var s = resolver.Settings;
        var body = new StringBuilder()
            .Append("/* TEAM */\n").Append(s.Organization.LegalName ?? s.SiteName).Append('\n')
            .Append("Site: ").Append(resolver.Absolute("/team")).Append('\n');
        if (s.Contact.Email is not null) body.Append("Contact: ").Append(s.Contact.Email).Append('\n');
        body.Append("\n/* SITE */\nLanguage: English\nStandards: HTML5, schema.org, WCAG 2.2\nComponents: React, ASP.NET Core\n");
        return Cached(body.ToString(), "text/plain; charset=utf-8", 86400);
    }

    /// <summary>IndexNow key file (<c>/{key}.txt</c>), only while IndexNow is enabled and only for the configured key.</summary>
    [HttpGet("/{key}.txt")]
    public async Task<IActionResult> IndexNowKey(string key, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        if (!s.IndexNow.Enabled || s.IndexNow.Key is null || !SeoSettingsService.IsIndexNowKey(key) || key != s.IndexNow.Key) return NotFound();
        return Content(s.IndexNow.Key, "text/plain; charset=utf-8");
    }
}

/// <summary>SEO overview of every public URL and the SEO settings (crawler policy, IndexNow, llms.txt).</summary>
[ApiController]
[HasPermission(Permissions.SiteManage)]
[Route("api/v1/agency/website/seo")]
public sealed class WebsiteSeoController(SeoOverviewService overview, SeoSettingsService settings) : ControllerBase
{
    /// <summary>Every public URL with the title, description, canonical, robots and structured data the server renders, plus warnings.</summary>
    [HttpGet("overview")]
    public Task<SeoOverviewDto> Overview(CancellationToken ct) => overview.OverviewAsync(ct);

    [HttpGet("settings")]
    public Task<SeoSettingsDto> Settings(CancellationToken ct) => settings.GetForEditAsync(ct);

    /// <summary>
    /// Denied while impersonating: the crawler policy decides which search engines and AI crawlers may read the whole public
    /// site (platform-wide configuration, like the site settings). Audited.
    /// </summary>
    [HttpPut("settings")]
    [DeniedWhileImpersonating]
    public Task<SeoSettingsDto> UpdateSettings(UpdateSeoSettingsRequest request, CancellationToken ct) => settings.UpdateAsync(request, ct);
}
