namespace OptimizeAll.Api.Modules.Website.Public;

/// <summary>
/// A public page a feature contributes to the sitemaps: path, last modification (UTC), title (for the SEO overview),
/// an optional image and optional videos (video sitemap: e.g. academy lectures embedded from YouTube).
/// </summary>
public sealed record SitemapContribution(string Path, DateTime? Modified, string Title, string? ImageUrl = null,
    IReadOnlyList<SiteSeo.SeoVideo>? Videos = null);

/// <summary>
/// Extension point of the sitemap index (<c>SiteSeo/SeoPageResolver.SitemapUrlsAsync</c>): a feature that owns public
/// pages outside the CMS tables registers one (scoped); its URLs become the child sitemap <c>/sitemaps/{Group}.xml</c>
/// (a path already listed elsewhere is not repeated) and feed llms.txt, IndexNow and the admin SEO overview. Return only
/// published, indexable, self-canonical addresses. The server renderer must also render these paths
/// (<c>SeoPageResolver</c>). Used by the partner pages (Partners/PartnerSitemapContributor.cs, group "partners").
/// </summary>
public interface ISitemapContributor
{
    /// <summary>The child sitemap's name (lower-case letters and hyphens), one of <c>SeoPageResolver.UrlGroups</c>.</summary>
    string Group { get; }

    Task<IReadOnlyList<SitemapContribution>> UrlsAsync(CancellationToken ct);
}
