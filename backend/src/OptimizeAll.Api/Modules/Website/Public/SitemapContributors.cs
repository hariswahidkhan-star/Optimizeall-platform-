namespace OptimizeAll.Api.Modules.Website.Public;

/// <summary>
/// Adds public, indexable URLs of another module to the website's XML sitemap (<see cref="PublicSiteService.SitemapAsync"/>).
/// Register implementations with <c>services.AddScoped&lt;IPublicSitemapContributor, T&gt;()</c>. Paths are app paths
/// ("/learn/…"); the sitemap makes them absolute. Implementations must only list published, indexable pages and must not
/// depend on <see cref="PublicSiteService"/> (it resolves them).
/// </summary>
public interface IPublicSitemapContributor
{
    Task<IReadOnlyList<(string Path, DateTime? Modified)>> SitemapEntriesAsync(CancellationToken ct);
}
