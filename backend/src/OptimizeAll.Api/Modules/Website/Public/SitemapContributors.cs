namespace OptimizeAll.Api.Modules.Website.Public;

/// <summary>
/// A public page a feature contributes to the sitemaps: path, last modification (UTC) and title (for the SEO overview),
/// plus its media for the image and video sitemaps: <see cref="ImageUrl"/> / <see cref="Images"/> (content images, app
/// paths or absolute URLs) and <see cref="Videos"/> (for example a lesson's lecture video: needs a poster image and a
/// content or player URL, as Google's video sitemap requires). Videos without a poster are left out of the video sitemap.
/// </summary>
public sealed record SitemapContribution(string Path, DateTime? Modified, string Title, string? ImageUrl = null)
{
    public IReadOnlyList<string>? Images { get; init; }

    public IReadOnlyList<SitemapVideoContribution>? Videos { get; init; }
}

/// <summary>
/// A video on a contributed page (URLs are app paths such as <c>/api/v1/files/{id}</c> or absolute https URLs).
/// <see cref="UploadDate"/> defaults to the page's last modification.
/// </summary>
public sealed record SitemapVideoContribution(
    string Title, string Description, string? PosterUrl, string? ContentUrl = null, string? PlayerUrl = null, int? DurationSeconds = null,
    DateTime? UploadDate = null, string? CaptionsUrl = null, string CaptionsLanguage = "en", string? TranscriptMarkdown = null);

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
