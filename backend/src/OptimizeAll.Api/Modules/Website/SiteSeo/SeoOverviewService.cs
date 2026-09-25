using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

public sealed record SeoWarningDto(string Code, string Severity, string Message);

/// <summary>Page-text keys that hold a built-in page's title and description (editable inline in the overview).</summary>
public sealed record SeoCopyKeysDto(string Title, string? Description);

public sealed record SeoOverviewRowDto(
    string Path, string Url, int Status, string Title, int TitleLength, string? Description, int DescriptionLength, string? Canonical,
    bool Indexable, string Robots, bool InSitemap, IReadOnlyList<string> JsonLdTypes, int H1Count, int ImageCount, int VideoCount,
    DateTime? LastModified, string Source, string? EditPath, SeoCopyKeysDto? CopyKeys, IReadOnlyList<SeoWarningDto> Warnings);

public sealed record SeoOverviewDto(
    string SiteUrl, int Total, int Indexable, int WithErrors, int WithWarnings, IReadOnlyList<SeoOverviewRowDto> Rows, DateTime GeneratedAt);

/// <summary>
/// The admin SEO overview: every public URL (the sitemap's URLs plus noindex and utility pages) with the metadata the
/// server renders for it and warnings for missing, duplicate, too long or too short titles and descriptions, missing
/// h1 or structured data, and canonicals pointing elsewhere.
/// </summary>
public sealed class SeoOverviewService(SeoPageResolver resolver, AppDbContext db, TimeProvider clock)
{
    public async Task<SeoOverviewDto> OverviewAsync(CancellationToken ct)
    {
        var urls = await resolver.SitemapUrlsAsync(ct);
        var inSitemap = urls.Select(u => u.Path).ToHashSet(StringComparer.Ordinal);
        var paths = urls.Select(u => u.Path).ToList();

        // Published content that is kept out of the index on purpose (noindex or canonical elsewhere) is listed too.
        var now = clock.GetUtcNow().UtcDateTime;
        paths.AddRange(await db.Set<SitePage>().AsNoTracking().Where(p => p.IsPublished && (p.PublishAt == null || p.PublishAt <= now) &&
                (p.Seo.NoIndex || p.Seo.CanonicalUrl != null)).Select(p => "/" + p.Slug).ToListAsync(ct));
        paths.AddRange(await db.Set<BlogPost>().AsNoTracking().Where(p => p.Status == BlogPostStatus.Published && p.PublishedAt <= now &&
                (p.Seo.NoIndex || p.Seo.CanonicalUrl != null)).Select(p => "/blog/" + p.Slug).ToListAsync(ct));
        paths.AddRange(await db.Set<AgencyService>().AsNoTracking().Where(s => s.IsPublished && (s.Seo.NoIndex || s.Seo.CanonicalUrl != null))
            .Select(s => "/services/" + s.Slug).ToListAsync(ct));
        paths.AddRange(SeoPageResolver.UtilityPaths);

        var rows = new List<SeoOverviewRowDto>();
        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            var page = await resolver.ResolveAsync(path, null, ct);
            rows.Add(Row(page, inSitemap.Contains(path)));
        }
        rows = AddDuplicateWarnings(rows);
        return new SeoOverviewDto(resolver.BaseUrl, rows.Count, rows.Count(r => r.Indexable),
            rows.Count(r => r.Warnings.Any(w => w.Severity == "error")), rows.Count(r => r.Warnings.Any(w => w.Severity == "warning")), rows,
            clock.GetUtcNow().UtcDateTime);
    }

    private SeoOverviewRowDto Row(SeoPage page, bool sitemap)
    {
        var w = new List<SeoWarningDto>();
        void Add(string code, string severity, string message) => w.Add(new SeoWarningDto(code, severity, message));
        var title = page.Title;
        var description = page.Description;
        if (page.Status != 200) Add("status", "error", $"The page answers HTTP {page.Status}.");
        if (string.IsNullOrWhiteSpace(title)) Add("title.missing", "error", "The page has no title.");
        else if (title.Length > SeoText.TitleMax) Add("title.long", "warning", $"The title is {title.Length} characters; search results show about {SeoText.TitleMax}.");
        else if (title.Length < SeoText.TitleMin) Add("title.short", "notice", $"The title is only {title.Length} characters; aim for {SeoText.TitleMin}–{SeoText.TitleMax}.");
        if (string.IsNullOrWhiteSpace(description)) Add("description.missing", "warning", "The page has no meta description.");
        else if (description.EndsWith('…')) Add("description.long", "warning", $"The description was cut to {SeoText.DescriptionMax} characters; write a shorter one.");
        else if (description.Length < SeoText.DescriptionMin) Add("description.short", "notice", $"The description is only {description.Length} characters; aim for {SeoText.DescriptionMin}–{SeoText.DescriptionMax}.");
        var h1 = page.Content.OfType<HeadingNode>().Count(h => h.Level == 1);
        if (h1 == 0) Add("h1.missing", "warning", "The page has no h1 heading.");
        if (h1 > 1) Add("h1.multiple", "notice", "The page has more than one h1 heading.");
        if (page.Kind == SeoPageKind.Content && page.JsonLd.Count == 0) Add("jsonld.missing", "notice", "The page has no structured data.");
        if (page.Kind == SeoPageKind.Content && page.Canonical is not null && page.Canonical != resolver.Absolute(page.Path) && !page.Canonical.Contains('?'))
            Add("canonical.elsewhere", "notice", $"The canonical URL points to {page.Canonical}; this page is left out of the sitemap.");
        if (page.Kind == SeoPageKind.Content && page.NoIndex) Add("noindex", "notice", "The page is set to noindex.");
        if (page.Content.OfType<ImageNode>().Any(i => string.IsNullOrWhiteSpace(i.Alt) && !i.Priority))
            Add("image.alt", "notice", "An image has no alternative text.");
        var types = page.JsonLd.Select(j => j.TryGetProperty("@type", out var t) ? t.GetString() ?? "?" : "?").ToList();
        SeoCopyKeysDto? copyKeys = SeoPageResolver.CopyPages.TryGetValue(page.Path, out var prefix)
            ? new SeoCopyKeysDto($"{prefix}.seo.title", $"{prefix}.seo.description")
            : page.Path switch
            {
                "/creators" => new SeoCopyKeysDto("creators.seo.title", "creators.seo.description"),
                // /faq's texts are portal copy (Admin → Content → Portal copy, content.manage), linked through EditPath.
                _ => null,
            };
        return new SeoOverviewRowDto(page.Path, resolver.Absolute(page.Path), page.Status, title, title.Length, description, description?.Length ?? 0,
            page.Canonical, page.IsIndexable, page.Robots, sitemap, types, h1, page.Images.Count, page.Videos.Count, page.ModifiedAt, page.Source,
            page.EditPath, copyKeys, w);
    }

    private static List<SeoOverviewRowDto> AddDuplicateWarnings(List<SeoOverviewRowDto> rows)
    {
        var indexable = rows.Where(r => r.Indexable).ToList();
        var titles = indexable.GroupBy(r => r.Title, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).SelectMany(g => g).Select(r => r.Path).ToHashSet();
        var descriptions = indexable.Where(r => r.Description is not null).GroupBy(r => r.Description!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).SelectMany(g => g).Select(r => r.Path).ToHashSet();
        return rows.Select(r =>
        {
            var extra = new List<SeoWarningDto>();
            if (titles.Contains(r.Path)) extra.Add(new SeoWarningDto("title.duplicate", "warning", "Another indexable page has the same title."));
            if (descriptions.Contains(r.Path)) extra.Add(new SeoWarningDto("description.duplicate", "warning", "Another indexable page has the same description."));
            return extra.Count == 0 ? r : r with { Warnings = r.Warnings.Concat(extra).ToList() };
        }).ToList();
    }
}
