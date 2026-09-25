using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Modules.Website.Blog;
using OptimizeAll.Api.Modules.Website.Leads;
using OptimizeAll.Api.Modules.Website.Pages;
using OptimizeAll.Api.Modules.Website.Settings;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Public;

/// <summary>
/// Anonymous read model of the agency website. Only published content is ever returned: unpublished services (or services
/// in an unpublished category), industries, case studies, testimonials, team members and pages answer 404 / are left out.
/// </summary>
public sealed class PublicSiteService(
    AppDbContext db, SiteSettingsService settingsService, IOptions<EmailOptions> email, TimeProvider clock, IEnumerable<ISitemapContributor> sitemapContributors)
{
    private SiteSettings? _settings;
    private Catalog? _catalog;

    public async Task<SiteSettings> SettingsAsync(CancellationToken ct) => _settings ??= await settingsService.GetAsync(ct);

    /// <summary>Public origin used for absolute URLs (sitemap, canonical, JSON-LD): settings → Email:AppBaseUrl.</summary>
    public async Task<string> BaseUrlAsync(CancellationToken ct) =>
        ((await SettingsAsync(ct)).Seo.SiteUrl ?? email.Value.AppBaseUrl).TrimEnd('/');

    private async Task<JsonLd> LdAsync(CancellationToken ct) => new(await BaseUrlAsync(ct), await SettingsAsync(ct));

    private sealed record Catalog(
        IReadOnlyList<ServiceCategory> Categories, IReadOnlyList<AgencyService> Services, IReadOnlyDictionary<Guid, List<ServicePackage>> Packages,
        IReadOnlyDictionary<Guid, ServiceCategory> CategoryById, IReadOnlyDictionary<Guid, AgencyService> ServiceById);

    private async Task<Catalog> CatalogAsync(CancellationToken ct)
    {
        if (_catalog is not null) return _catalog;
        var categories = await db.Set<ServiceCategory>().AsNoTracking().Where(c => c.IsPublished)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var categoryIds = categories.Select(c => c.Id).ToList();
        var services = await db.Set<AgencyService>().AsNoTracking().Where(s => s.IsPublished && categoryIds.Contains(s.CategoryId))
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(ct);
        var serviceIds = services.Select(s => s.Id).ToList();
        var packages = (await db.Set<ServicePackage>().AsNoTracking().Where(p => p.IsActive && serviceIds.Contains(p.ServiceId))
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Price).ToListAsync(ct))
            .GroupBy(p => p.ServiceId).ToDictionary(g => g.Key, g => g.ToList());
        var categoryOrder = categories.Select((c, i) => (c.Id, i)).ToDictionary(x => x.Id, x => x.i);
        services = services.OrderBy(s => categoryOrder[s.CategoryId]).ThenBy(s => s.SortOrder).ThenBy(s => s.Name).ToList();
        return _catalog = new Catalog(categories, services, packages, categories.ToDictionary(c => c.Id), services.ToDictionary(s => s.Id));
    }

    private static ServiceCardDto Card(Catalog cat, AgencyService s)
    {
        var c = cat.CategoryById[s.CategoryId];
        var start = cat.Packages.GetValueOrDefault(s.Id)?.Where(p => !p.IsCustomQuote && p.Price is not null)
            .OrderBy(p => p.Price).FirstOrDefault();
        return new ServiceCardDto(s.Id, s.Slug, s.Name, s.Tagline, s.Icon, c.Slug, c.Name,
            start is null ? null : new PriceDto(start.Price!.Value, start.Currency, start.BillingPeriod));
    }

    private static PublicPackageDto Package(ServicePackage p) =>
        new(p.Id, p.Name, p.Description, p.Price, p.Currency, p.BillingPeriod, p.SetupFee, p.Features, p.IsMostPopular, p.IsCustomQuote);

    private static IReadOnlyList<ServiceCategoryGroupDto> Groups(Catalog cat, string? onlyCategorySlug = null) =>
        cat.Categories.Where(c => onlyCategorySlug is null || c.Slug == onlyCategorySlug)
            .Select(c => new ServiceCategoryGroupDto(c.Slug, c.Name, c.Description, c.Icon,
                cat.Services.Where(s => s.CategoryId == c.Id).Select(s => Card(cat, s)).ToList()))
            .Where(g => g.Services.Count > 0)
            .ToList();

    private async Task<PublicSeoDto> SeoAsync(SeoMeta? seo, string fallbackTitle, string? fallbackDescription, string? fallbackImage, string path, CancellationToken ct)
    {
        var ld = await LdAsync(ct);
        var s = await SettingsAsync(ct);
        return new PublicSeoDto(
            seo?.Title ?? fallbackTitle,
            seo?.Description ?? fallbackDescription ?? s.Seo.DefaultDescription,
            seo?.OgImageUrl ?? fallbackImage ?? s.Seo.DefaultOgImageUrl,
            ld.Url(seo?.CanonicalUrl ?? path),
            seo?.NoIndex ?? false);
    }

    // ---------------------------------------------------------------- Site & home

    public async Task<PublicSiteDto> SiteAsync(CancellationToken ct)
    {
        var s = await SettingsAsync(ct);
        var cat = await CatalogAsync(ct);
        var menu = cat.Categories.Select(c => new MenuCategoryDto(c.Slug, c.Name, c.Description, c.Icon,
                cat.Services.Where(x => x.CategoryId == c.Id).Select(x => new MenuServiceDto(x.Slug, x.Name, x.Tagline, x.Icon)).ToList()))
            .Where(m => m.Services.Count > 0).ToList();
        var booking = await db.Set<ConsultationSettings>().AsNoTracking().Where(x => x.Key == ConsultationSettings.DefaultKey)
            .Select(x => (bool?)x.IsEnabled).FirstOrDefaultAsync(ct) ?? false;
        return new PublicSiteDto(s.SiteName, s.Tagline, s.Header, s.Footer, s.Contact, s.Social, s.TrustLogos, s.Announcement,
            s.Seo with { SiteUrl = await BaseUrlAsync(ct) }, s.Analytics, menu, ConsentTexts.Dto, booking);
    }

    public async Task<HomeDto> HomeAsync(CancellationToken ct)
    {
        var s = await SettingsAsync(ct);
        var cat = await CatalogAsync(ct);
        var ld = await LdAsync(ct);
        var caseStudies = await CaseStudyCardsAsync(q => q.Where(c => c.IsFeatured), 3, ct);
        if (caseStudies.Count < 3)
            caseStudies = caseStudies.Concat((await CaseStudyCardsAsync(q => q, 6, ct)).Where(c => caseStudies.All(x => x.Slug != c.Slug)))
                .Take(3).ToList();
        var testimonials = await TestimonialsAsync(featuredFirst: true, limit: 8, ct);
        var industries = (await db.Set<Industry>().AsNoTracking().Where(i => i.IsPublished).OrderBy(i => i.SortOrder).ThenBy(i => i.Name).ToListAsync(ct))
            .Select(i => new IndustryCardDto(i.Slug, i.Name, i.Summary, i.Icon)).ToList();
        var posts = await PublicBlogQueries.LatestAsync(db, clock.GetUtcNow().UtcDateTime, 3, ct);

        var teaser = cat.Services.Where(x => x.IsFeatured || cat.Packages.ContainsKey(x.Id))
            .Select(x => (Service: x, Package: cat.Packages.GetValueOrDefault(x.Id)?.FirstOrDefault(p => p.IsMostPopular && !p.IsCustomQuote)))
            .Where(x => x.Package is not null).Take(3)
            .Select(x => new PricingTeaserDto(x.Service.Slug, x.Service.Name, Package(x.Package!))).ToList();

        return new HomeDto(Groups(cat), caseStudies, testimonials, industries, posts, teaser, s.HomeStats, s.TrustLogos,
            await SeoAsync(null, s.Seo.DefaultTitle, s.Seo.DefaultDescription, null, "/", ct),
            new[] { ld.Organization(), ld.WebSite() });
    }

    // ---------------------------------------------------------------- Services & pricing

    public async Task<IReadOnlyList<ServiceCategoryGroupDto>> ServicesAsync(CancellationToken ct) => Groups(await CatalogAsync(ct));

    public async Task<PublicServiceDto> ServiceAsync(string slug, CancellationToken ct)
    {
        var cat = await CatalogAsync(ct);
        var s = cat.Services.FirstOrDefault(x => x.Slug == slug) ?? throw CmsStore.NotFound<AgencyService>();
        var category = cat.CategoryById[s.CategoryId];
        var packages = cat.Packages.GetValueOrDefault(s.Id) ?? new List<ServicePackage>();
        var related = s.RelatedServiceIds.Where(cat.ServiceById.ContainsKey).Select(id => Card(cat, cat.ServiceById[id])).ToList();
        if (related.Count == 0)
            related = cat.Services.Where(x => x.CategoryId == s.CategoryId && x.Id != s.Id).Take(3).Select(x => Card(cat, x)).ToList();
        var caseStudies = (await CaseStudyCardsAsync(q => q, 50, ct)).Where(c => c.ServiceSlugs.Contains(s.Slug)).Take(3).ToList();
        var testimonials = (await TestimonialsAsync(false, 50, ct)).Where(t => t.ServiceSlug == s.Slug).Take(6).ToList();
        var ld = await LdAsync(ct);
        var jsonLd = new List<JsonElement>
        {
            ld.Service(s, category.Name, packages),
            ld.Breadcrumbs(("Home", "/"), ("Services", "/services"), (s.Name, $"/services/{s.Slug}")),
        };
        if (ld.FaqPage(s.Faqs) is { } faq) jsonLd.Add(faq);
        return new PublicServiceDto(s.Id, s.Slug, s.Name, s.Tagline, s.HeroTitle, s.HeroBody, s.OverviewMarkdown, s.ProblemsSolved, s.Deliverables,
            s.ProcessSteps, s.Tools, s.Kpis, s.Faqs, s.Icon, s.HeroImageUrl, s.CtaLabel, s.CtaUrl, category.Slug, category.Name,
            packages.Select(Package).ToList(), related, caseStudies, testimonials,
            await SeoAsync(s.Seo, s.Name, s.Tagline, s.HeroImageUrl, $"/services/{s.Slug}", ct), jsonLd);
    }

    public async Task<PricingDto> PricingAsync(CancellationToken ct)
    {
        var cat = await CatalogAsync(ct);
        return new PricingDto(cat.Services.Where(s => cat.Packages.ContainsKey(s.Id))
            .Select(s => new PricingServiceDto(Card(cat, s), cat.Packages[s.Id].Select(Package).ToList())).ToList());
    }

    // ---------------------------------------------------------------- Industries

    public async Task<IReadOnlyList<IndustryCardDto>> IndustriesAsync(CancellationToken ct) =>
        (await db.Set<Industry>().AsNoTracking().Where(i => i.IsPublished).OrderBy(i => i.SortOrder).ThenBy(i => i.Name).ToListAsync(ct))
            .Select(i => new IndustryCardDto(i.Slug, i.Name, i.Summary, i.Icon)).ToList();

    public async Task<PublicIndustryDto> IndustryAsync(string slug, CancellationToken ct)
    {
        var i = await db.Set<Industry>().AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && x.IsPublished, ct)
            ?? throw CmsStore.NotFound<Industry>();
        var cat = await CatalogAsync(ct);
        var services = i.ServiceIds.Where(cat.ServiceById.ContainsKey).Select(id => Card(cat, cat.ServiceById[id])).ToList();
        var caseStudies = (await CaseStudyCardsAsync(q => q, 50, ct)).Where(c => c.IndustrySlug == i.Slug).Take(6).ToList();
        var ld = await LdAsync(ct);
        return new PublicIndustryDto(i.Slug, i.Name, i.Summary, i.BodyMarkdown, i.Challenges, i.Icon, i.HeroImageUrl, services, caseStudies,
            await SeoAsync(i.Seo, $"Marketing for {i.Name}", i.Summary, i.HeroImageUrl, $"/industries/{i.Slug}", ct),
            new[] { ld.Breadcrumbs(("Home", "/"), ("Industries", "/industries"), (i.Name, $"/industries/{i.Slug}")) });
    }

    // ---------------------------------------------------------------- Case studies

    private async Task<List<CaseStudyCardDto>> CaseStudyCardsAsync(Func<IQueryable<CaseStudy>, IQueryable<CaseStudy>> filter, int limit, CancellationToken ct)
    {
        var cat = await CatalogAsync(ct);
        var rows = await filter(db.Set<CaseStudy>().AsNoTracking().Where(c => c.IsPublished))
            .OrderBy(c => c.SortOrder).ThenByDescending(c => c.PublishedAt).Take(limit).ToListAsync(ct);
        var industryIds = rows.Where(r => r.IndustryId is not null).Select(r => r.IndustryId!.Value).Distinct().ToList();
        var industries = await db.Set<Industry>().AsNoTracking().Where(i => industryIds.Contains(i.Id) && i.IsPublished)
            .ToDictionaryAsync(i => i.Id, ct);
        return rows.Select(c =>
        {
            var industry = c.IndustryId is { } id ? industries.GetValueOrDefault(id) : null;
            var services = c.ServiceIds.Where(cat.ServiceById.ContainsKey).Select(x => cat.ServiceById[x]).ToList();
            return new CaseStudyCardDto(c.Slug, c.Title, c.ClientName, c.Summary, industry?.Slug, industry?.Name,
                services.Select(x => x.Slug).ToList(), services.Select(x => x.Name).ToList(), c.CoverImageUrl,
                c.Metrics.Take(3).Select(Metric).ToList(), c.IsFeatured);
        }).ToList();
    }

    private static MetricDto Metric(ResultMetric m) => new(m.Label, m.Value, m.Measurement, m.Context);

    public async Task<IReadOnlyList<CaseStudyCardDto>> CaseStudiesAsync(string? service, string? industry, CancellationToken ct)
    {
        var all = await CaseStudyCardsAsync(q => q, 200, ct);
        return all.Where(c => (string.IsNullOrWhiteSpace(service) || c.ServiceSlugs.Contains(service.Trim())) &&
                              (string.IsNullOrWhiteSpace(industry) || c.IndustrySlug == industry.Trim())).ToList();
    }

    public async Task<PublicCaseStudyDto> CaseStudyAsync(string slug, CancellationToken ct)
    {
        var c = await db.Set<CaseStudy>().AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && x.IsPublished, ct)
            ?? throw CmsStore.NotFound<CaseStudy>();
        var cat = await CatalogAsync(ct);
        var industry = c.IndustryId is { } iid
            ? await db.Set<Industry>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == iid && i.IsPublished, ct) : null;
        var services = c.ServiceIds.Where(cat.ServiceById.ContainsKey).Select(id => Card(cat, cat.ServiceById[id])).ToList();
        var related = (await CaseStudyCardsAsync(q => q.Where(x => x.Id != c.Id), 50, ct))
            .OrderByDescending(x => x.ServiceSlugs.Intersect(services.Select(s => s.Slug)).Count())
            .ThenByDescending(x => x.IndustrySlug == industry?.Slug).Take(3).ToList();
        var ld = await LdAsync(ct);
        var path = $"/case-studies/{c.Slug}";
        return new PublicCaseStudyDto(c.Slug, c.Title, c.ClientName, c.Summary, industry?.Slug, industry?.Name, services,
            c.ChallengeMarkdown, c.StrategyMarkdown, c.ExecutionMarkdown, c.Metrics.Select(Metric).ToList(), c.TestimonialQuote,
            c.TestimonialAuthor, c.TestimonialRole, c.CoverImageUrl, c.GalleryImageUrls, c.PublishedAt, related,
            await SeoAsync(c.Seo, c.Title, c.Summary, c.CoverImageUrl, path, ct),
            new[]
            {
                ld.Article("Article", c.Title, c.Summary, path, c.CoverImageUrl, c.PublishedAt, c.UpdatedAt, null),
                ld.Breadcrumbs(("Home", "/"), ("Case studies", "/case-studies"), (c.Title, path)),
            });
    }

    // ---------------------------------------------------------------- Testimonials, team, pages

    public async Task<IReadOnlyList<PublicTestimonialDto>> TestimonialsAsync(bool featuredFirst, int limit, CancellationToken ct)
    {
        var cat = await CatalogAsync(ct);
        var q = db.Set<Testimonial>().AsNoTracking().Where(t => t.IsPublished);
        var ordered = featuredFirst ? q.OrderByDescending(t => t.IsFeatured).ThenBy(t => t.SortOrder) : q.OrderBy(t => t.SortOrder);
        var rows = await ordered.ThenByDescending(t => t.CreatedAt).Take(limit).ToListAsync(ct);
        return rows.Select(t => new PublicTestimonialDto(t.Id, t.Quote, t.AuthorName, t.AuthorRole, t.Company, t.Rating, t.AvatarUrl,
            t.ServiceId is { } sid && cat.ServiceById.TryGetValue(sid, out var s) ? s.Slug : null)).ToList();
    }

    public async Task<IReadOnlyList<PublicTeamMemberDto>> TeamAsync(CancellationToken ct) =>
        (await db.Set<TeamMember>().AsNoTracking().Where(m => m.IsPublished).OrderBy(m => m.SortOrder).ThenBy(m => m.Name).ToListAsync(ct))
            .Select(m => new PublicTeamMemberDto(m.Slug, m.Name, m.Role, m.Bio, m.PhotoUrl, m.Expertise, m.SocialLinks)).ToList();

    public async Task<PublicPageDto> PageAsync(string slug, CancellationToken ct)
    {
        var at = clock.GetUtcNow().UtcDateTime;
        var p = await db.Set<SitePage>().AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && x.IsPublished && (x.PublishAt == null || x.PublishAt <= at), ct)
            ?? throw CmsStore.NotFound<SitePage>();
        var blocks = PageBlockValidator.Parse(p.BlocksJson);
        var s = await SettingsAsync(ct);
        var cat = await CatalogAsync(ct);

        IReadOnlyList<PublicTestimonialDto> testimonials = Array.Empty<PublicTestimonialDto>();
        if (blocks.Any(b => b.Type == PageBlockTypes.Testimonials)) testimonials = await TestimonialsAsync(true, 50, ct);
        IReadOnlyList<CaseStudyCardDto> caseStudies = Array.Empty<CaseStudyCardDto>();
        if (blocks.Any(b => b.Type == PageBlockTypes.CaseStudyHighlight)) caseStudies = await CaseStudyCardsAsync(q => q, 200, ct);
        var groups = blocks.Any(b => b.Type == PageBlockTypes.ServicesGrid) ? Groups(cat) : Array.Empty<ServiceCategoryGroupDto>();

        var ld = await LdAsync(ct);
        var jsonLd = new List<JsonElement> { ld.Breadcrumbs(("Home", "/"), (p.Title, $"/{p.Slug}")) };
        var faqs = blocks.Where(b => b.Type == PageBlockTypes.Faq)
            .SelectMany(b => b.Data.Deserialize<FaqBlock>(SiteSettingsService.Json)?.Items ?? Array.Empty<FaqEntry>()).ToList();
        if (ld.FaqPage(faqs) is { } faqLd) jsonLd.Add(faqLd);

        return new PublicPageDto(p.Slug, p.Title, p.Summary, p.Kind, blocks, testimonials, caseStudies, groups, s.TrustLogos, p.UpdatedAt,
            await SeoAsync(p.Seo, p.Title, p.Summary, null, $"/{p.Slug}", ct), jsonLd);
    }

    // ---------------------------------------------------------------- Search

    public async Task<SearchResultDto> SearchAsync(string? query, CancellationToken ct)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length < 2) return new SearchResultDto(q, Array.Empty<SearchHitDto>(), Array.Empty<SearchHitDto>(), Array.Empty<SearchHitDto>());
        if (q.Length > 100) q = q[..100];
        var pattern = PagingExtensions.LikePattern(q);
        var cat = await CatalogAsync(ct);
        var serviceIds = cat.Services.Select(s => s.Id).ToList();

        var services = await db.Set<AgencyService>().AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id) && (EF.Functions.Like(s.Name, pattern, "\\") || EF.Functions.Like(s.Tagline, pattern, "\\") ||
                        (s.OverviewMarkdown != null && EF.Functions.Like(s.OverviewMarkdown, pattern, "\\"))))
            .OrderBy(s => s.SortOrder).Take(10).ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var posts = await db.Set<BlogPost>().AsNoTracking()
            .Where(p => p.Status == BlogPostStatus.Published && p.PublishedAt <= now &&
                        (EF.Functions.Like(p.Title, pattern, "\\") || EF.Functions.Like(p.Excerpt, pattern, "\\") || EF.Functions.Like(p.BodyMarkdown, pattern, "\\")))
            .OrderByDescending(p => p.PublishedAt).Take(10).ToListAsync(ct);
        var cases = await db.Set<CaseStudy>().AsNoTracking()
            .Where(c => c.IsPublished && (EF.Functions.Like(c.Title, pattern, "\\") || EF.Functions.Like(c.Summary, pattern, "\\") ||
                        EF.Functions.Like(c.ClientName, pattern, "\\")))
            .OrderBy(c => c.SortOrder).Take(10).ToListAsync(ct);

        // Rank title matches first.
        static int Rank(string title, string term) => title.Contains(term, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        return new SearchResultDto(q,
            services.OrderBy(s => Rank(s.Name, q)).Select(s => new SearchHitDto("service", s.Slug, s.Name, s.Tagline, $"/services/{s.Slug}")).ToList(),
            posts.OrderBy(p => Rank(p.Title, q)).Select(p => new SearchHitDto("post", p.Slug, p.Title, p.Excerpt, $"/blog/{p.Slug}")).ToList(),
            cases.OrderBy(c => Rank(c.Title, q)).Select(c => new SearchHitDto("caseStudy", c.Slug, c.Title, c.Summary, $"/case-studies/{c.Slug}")).ToList());
    }

    // ---------------------------------------------------------------- Sitemap & robots

    /// <summary>
    /// XML sitemap of every published, indexable URL: home, index pages, services, industries, case studies, blog posts,
    /// CMS pages and open jobs. Unpublished, scheduled and noindex content is never listed.
    /// </summary>
    public async Task<string> SitemapAsync(CancellationToken ct)
    {
        var baseUrl = await BaseUrlAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var cat = await CatalogAsync(ct);
        var urls = new List<(string Path, DateTime? Modified)>
        {
            ("/", null), ("/services", null), ("/industries", null), ("/case-studies", null), ("/pricing", null), ("/blog", null),
            ("/team", null), ("/careers", null), ("/contact", null), ("/free-audit", null), ("/get-a-quote", null),
            ("/book-a-consultation", null), ("/creators", null),
        };
        urls.AddRange(cat.Services.Where(s => !s.Seo.NoIndex).Select(s => ($"/services/{s.Slug}", (DateTime?)s.UpdatedAt)));
        urls.AddRange((await db.Set<Industry>().AsNoTracking().Where(i => i.IsPublished && !i.Seo.NoIndex).Select(i => new { i.Slug, i.UpdatedAt }).ToListAsync(ct))
            .Select(i => ($"/industries/{i.Slug}", (DateTime?)i.UpdatedAt)));
        urls.AddRange((await db.Set<CaseStudy>().AsNoTracking().Where(c => c.IsPublished && !c.Seo.NoIndex).Select(c => new { c.Slug, c.UpdatedAt }).ToListAsync(ct))
            .Select(c => ($"/case-studies/{c.Slug}", (DateTime?)c.UpdatedAt)));
        urls.AddRange((await db.Set<BlogPost>().AsNoTracking()
                .Where(p => p.Status == BlogPostStatus.Published && p.PublishedAt <= now && !p.Seo.NoIndex).Select(p => new { p.Slug, p.UpdatedAt }).ToListAsync(ct))
            .Select(p => ($"/blog/{p.Slug}", (DateTime?)p.UpdatedAt)));
        urls.AddRange((await db.Set<SitePage>().AsNoTracking().Where(p => p.IsPublished && (p.PublishAt == null || p.PublishAt <= now) && !p.Seo.NoIndex).Select(p => new { p.Slug, p.UpdatedAt }).ToListAsync(ct))
            .Where(p => urls.All(u => u.Path != $"/{p.Slug}"))
            .Select(p => ($"/{p.Slug}", (DateTime?)p.UpdatedAt)));
        urls.AddRange((await db.Set<JobOpening>().AsNoTracking()
                .Where(j => j.Status == JobOpeningStatus.Open && (j.ClosesAt == null || j.ClosesAt > now)).Select(j => new { j.Slug, j.UpdatedAt }).ToListAsync(ct))
            .Select(j => ($"/careers/{j.Slug}", (DateTime?)j.UpdatedAt)));
        foreach (var contributor in sitemapContributors)
            foreach (var url in await contributor.UrlsAsync(ct))
                if (urls.All(u => u.Path != url.Path)) urls.Add((url.Path, url.Modified));

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false, Encoding = Encoding.UTF8 }))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
            foreach (var (path, modified) in urls)
            {
                writer.WriteStartElement("url");
                writer.WriteElementString("loc", baseUrl + path);
                if (modified is { } m) writer.WriteElementString("lastmod", m.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
    }

    /// <summary>Paths crawlers must not visit: every signed-in portal and the API.</summary>
    public static readonly string[] DisallowedPaths = { "/app", "/agency", "/client", "/admin", "/finance", "/review", "/manage", "/api" };

    public async Task<string> RobotsAsync(CancellationToken ct)
    {
        var baseUrl = await BaseUrlAsync(ct);
        var sb = new StringBuilder();
        sb.Append("User-agent: *\n");
        foreach (var path in DisallowedPaths) sb.Append("Disallow: ").Append(path).Append('\n');
        // The sitemap itself lives under /api, so allow exactly that path.
        sb.Append("Allow: /api/v1/public/sitemap.xml\n");
        sb.Append("Allow: /\n\n");
        sb.Append("Sitemap: ").Append(baseUrl).Append("/api/v1/public/sitemap.xml\n");
        return sb.ToString();
    }
}
