using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Partners;

/// <summary>Lists <c>/partners</c> (while any partner is active) and every active, indexable partner profile.</summary>
public sealed class PartnerSitemapContributor(AppDbContext db) : ISitemapContributor
{
    public async Task<IReadOnlyList<SitemapUrl>> UrlsAsync(CancellationToken ct)
    {
        var partners = await db.Set<WebsitePartner>().AsNoTracking().Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name).Select(p => new { p.Slug, p.UpdatedAt, p.Seo.NoIndex }).ToListAsync(ct);
        if (partners.Count == 0) return Array.Empty<SitemapUrl>();
        var urls = new List<SitemapUrl> { new("/partners", partners.Max(p => p.UpdatedAt)) };
        urls.AddRange(partners.Where(p => !p.NoIndex).Select(p => new SitemapUrl($"/partners/{p.Slug}", p.UpdatedAt)));
        return urls;
    }
}
