using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Partners;

/// <summary>
/// The <c>partners</c> sitemap: <c>/partners</c> (while any partner is active) and every active, indexable, self-canonical
/// partner profile.
/// </summary>
public sealed class PartnerSitemapContributor(AppDbContext db) : ISitemapContributor
{
    public const string GroupName = "partners";

    public string Group => GroupName;

    public async Task<IReadOnlyList<SitemapContribution>> UrlsAsync(CancellationToken ct)
    {
        var partners = await db.Set<WebsitePartner>().AsNoTracking().Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Select(p => new { p.Slug, p.Name, p.LogoUrl, p.UpdatedAt, p.Seo.NoIndex, p.Seo.CanonicalUrl }).ToListAsync(ct);
        if (partners.Count == 0) return Array.Empty<SitemapContribution>();
        var urls = new List<SitemapContribution> { new("/partners", partners.Max(p => p.UpdatedAt), "Our partners") };
        urls.AddRange(partners
            .Where(p => !p.NoIndex && (string.IsNullOrWhiteSpace(p.CanonicalUrl) || p.CanonicalUrl.TrimEnd('/').EndsWith($"/partners/{p.Slug}", StringComparison.OrdinalIgnoreCase)))
            .Select(p => new SitemapContribution($"/partners/{p.Slug}", p.UpdatedAt, p.Name, p.LogoUrl)));
        return urls;
    }
}
