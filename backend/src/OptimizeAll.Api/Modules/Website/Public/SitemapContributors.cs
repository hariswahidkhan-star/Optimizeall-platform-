namespace OptimizeAll.Api.Modules.Website.Public;

/// <summary>A site path for the sitemap with its last modification (UTC), if known.</summary>
public sealed record SitemapUrl(string Path, DateTime? Modified);

/// <summary>
/// Extension point of <see cref="PublicSiteService.SitemapAsync"/>: a feature that owns public pages outside the CMS
/// tables registers one (scoped) and its URLs are appended to <c>sitemap.xml</c> (a path already listed is not repeated).
/// Return only published, indexable addresses. Used by the partner pages (Partners/PartnerSitemapContributor.cs).
/// </summary>
public interface ISitemapContributor
{
    Task<IReadOnlyList<SitemapUrl>> UrlsAsync(CancellationToken ct);
}
