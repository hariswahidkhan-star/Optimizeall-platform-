using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.LandingPages;
using OptimizeAll.Api.Modules.Website.Pages;
using OptimizeAll.Api.Modules.Website.Partners;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>An indexable public URL for the sitemaps, llms.txt and the SEO overview.</summary>
public sealed record SitemapUrl(string Path, DateTime? LastModified, string Group, IReadOnlyList<SeoImage> Images, IReadOnlyList<SeoVideo> Videos, string Title);

public sealed partial class SeoPageResolver
{
    public const string GroupPages = "pages";
    public const string GroupServices = "services";
    public const string GroupCaseStudies = "case-studies";
    public const string GroupBlog = "blog";
    public const string GroupCareers = "careers";
    public const string GroupLanding = "landing-pages";
    /// <summary>Partner pages (/partners and profiles), contributed by <see cref="PartnerSitemapContributor"/>.</summary>
    public const string GroupPartners = PartnerSitemapContributor.GroupName;
    /// <summary>The academy (/learn, courses, lessons), contributed by <see cref="Learning.LearningSitemapContributor"/>.</summary>
    public const string GroupLearn = Learning.LearningSitemapContributor.GroupName;

    /// <summary>
    /// The content sitemaps, in index order (images and videos are derived from these URLs' media). Groups of
    /// <see cref="ISitemapContributor"/>s must be listed here.
    /// </summary>
    public static readonly string[] UrlGroups = { GroupPages, GroupServices, GroupCaseStudies, GroupBlog, GroupCareers, GroupLanding, GroupPartners, GroupLearn };

    private static readonly IReadOnlyList<SeoImage> NoImages = Array.Empty<SeoImage>();
    private static readonly IReadOnlyList<SeoVideo> NoVideos = Array.Empty<SeoVideo>();

    private bool SelfCanonical(string? canonical, string path) => canonical is null || _ld.Url(canonical) == _ld.Url(path);

    private SeoImage[] Img(params (string? Url, string? Title)[] images) =>
        images.Where(i => !string.IsNullOrWhiteSpace(i.Url)).Select(i => new SeoImage(_ld.Url(i.Url), i.Title)).ToArray();

    private IReadOnlyList<SeoVideo> CatalogVideos(string path) =>
        SiteVideoCatalog.ForPath(path).Select(v => ToVideo(v.Block, v.UploadDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))).ToList();

    /// <summary>
    /// Every published, indexable, self-canonical public URL with its real last-modified time: built-in pages, CMS pages,
    /// services, industries, case studies, blog posts, open jobs, live client landing pages and public campaigns.
    /// Unpublished, scheduled, noindex and cross-canonical content is left out.
    /// </summary>
    public async Task<IReadOnlyList<SitemapUrl>> SitemapUrlsAsync(CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        var now = Now;
        var urls = new List<SitemapUrl>();
        var copyOrSettings = Latest(_copyUpdatedAt, _settingsUpdatedAt);

        var services = await db.Set<AgencyService>().AsNoTracking()
            .Where(s => s.IsPublished && db.Set<ServiceCategory>().Any(c => c.Id == s.CategoryId && c.IsPublished))
            .Select(s => new { s.Slug, s.Name, s.UpdatedAt, s.HeroImageUrl, s.Seo.NoIndex, s.Seo.CanonicalUrl, s.Seo.OgImageUrl }).ToListAsync(ct);
        var industries = await db.Set<Industry>().AsNoTracking().Where(i => i.IsPublished)
            .Select(i => new { i.Slug, i.Name, i.UpdatedAt, i.HeroImageUrl, i.Seo.NoIndex, i.Seo.CanonicalUrl }).ToListAsync(ct);
        var cases = await db.Set<CaseStudy>().AsNoTracking().Where(c => c.IsPublished)
            .Select(c => new { c.Slug, c.Title, c.UpdatedAt, c.CoverImageUrl, c.GalleryImageUrls, c.Seo.NoIndex, c.Seo.CanonicalUrl }).ToListAsync(ct);
        var posts = await db.Set<BlogPost>().AsNoTracking().Where(p => p.Status == BlogPostStatus.Published && p.PublishedAt <= now)
            .Select(p => new { p.Slug, p.Title, p.UpdatedAt, p.CoverImageUrl, p.CoverImageAlt, p.Seo.NoIndex, p.Seo.CanonicalUrl }).ToListAsync(ct);
        var pages = await db.Set<SitePage>().AsNoTracking().Where(p => p.IsPublished && (p.PublishAt == null || p.PublishAt <= now))
            .Select(p => new { p.Slug, p.Title, p.UpdatedAt, p.BlocksJson, p.Seo.NoIndex, p.Seo.CanonicalUrl }).ToListAsync(ct);
        var jobs = await db.Set<JobOpening>().AsNoTracking().Where(j => j.Status == JobOpeningStatus.Open && (j.ClosesAt == null || j.ClosesAt > now))
            .Select(j => new { j.Slug, j.Title, j.UpdatedAt }).ToListAsync(ct);
        var team = await db.Set<TeamMember>().AsNoTracking().Where(m => m.IsPublished).Select(m => new { m.Name, m.PhotoUrl, m.UpdatedAt }).ToListAsync(ct);
        var cmsBySlug = pages.ToDictionary(p => p.Slug);

        DateTime? Max(IEnumerable<DateTime> values) => values.Any() ? values.Max() : null;
        void Add(string path, DateTime? modified, string group, string title, IReadOnlyList<SeoImage>? images = null, IReadOnlyList<SeoVideo>? videos = null)
        {
            var vids = (videos ?? NoVideos).Concat(CatalogVideos(path)).ToList();
            urls.Add(new SitemapUrl(path, modified, group, images ?? NoImages, vids, title));
        }

        // Built-in pages. /pricing and /contact also show the CMS page of the same slug.
        DateTime? Embedded(string slug) => cmsBySlug.TryGetValue(slug, out var p) ? p.UpdatedAt : null;
        Add("/", Latest(copyOrSettings, Max(posts.Select(p => p.UpdatedAt))), GroupPages, _settings.Seo.DefaultTitle);
        Add("/services", Latest(copyOrSettings, Max(services.Select(s => s.UpdatedAt))), GroupPages, _copy.Text("services.seo.title"));
        Add("/pricing", Latest(copyOrSettings, Max(services.Select(s => s.UpdatedAt)), Embedded("pricing")), GroupPages, _copy.Text("pricing.seo.title"));
        Add("/industries", Latest(copyOrSettings, Max(industries.Select(i => i.UpdatedAt))), GroupPages, _copy.Text("industries.seo.title"));
        Add("/case-studies", Latest(copyOrSettings, Max(cases.Select(c => c.UpdatedAt))), GroupPages, _copy.Text("caseStudies.seo.title"));
        Add("/blog", Latest(copyOrSettings, Max(posts.Select(p => p.UpdatedAt))), GroupPages, _copy.Text("blog.seo.title"));
        Add("/team", Latest(copyOrSettings, Max(team.Select(m => m.UpdatedAt))), GroupPages, _copy.Text("team.seo.title"),
            Img(team.Select(m => (m.PhotoUrl, (string?)m.Name)).ToArray()));
        Add("/careers", Latest(copyOrSettings, Max(jobs.Select(j => j.UpdatedAt))), GroupPages, _copy.Text("careers.seo.title"));
        Add("/contact", Latest(copyOrSettings, Embedded("contact")), GroupPages, _copy.Text("contact.seo.title"));
        Add("/free-audit", copyOrSettings, GroupPages, _copy.Text("audit.seo.title"));
        Add("/get-a-quote", copyOrSettings, GroupPages, _copy.Text("quote.seo.title"));
        Add("/book-a-consultation", copyOrSettings, GroupPages, _copy.Text("booking.seo.title"));
        Add("/creators", copyOrSettings, GroupPages, _copy.Text("creators.seo.title"));
        var faqUpdated = await db.Set<Domain.Content.FaqItem>().AsNoTracking().Where(f => f.IsPublished).Select(f => (DateTime?)f.UpdatedAt).MaxAsync(ct);
        Add("/faq", Latest(copyOrSettings, faqUpdated), GroupPages, _copy.Text("faq.seo.title"));

        foreach (var p in pages.Where(p => !p.NoIndex && SelfCanonical(p.CanonicalUrl, $"/{p.Slug}") && !CopyPages.ContainsKey($"/{p.Slug}") &&
                                           urls.All(u => u.Path != $"/{p.Slug}")))
        {
            var videos = PageBlockValidator.Parse(p.BlocksJson).Where(b => b.Type == PageBlockTypes.Video)
                .Select(b => b.Data.Deserialize<VideoBlock>(Website.Settings.SiteSettingsService.Json)).OfType<VideoBlock>()
                .Select(v => ToVideo(v, p.UpdatedAt)).ToList();
            var heroImages = PageBlockValidator.Parse(p.BlocksJson).Where(b => b.Type == PageBlockTypes.Hero)
                .Select(b => b.Data.Deserialize<HeroBlock>(Website.Settings.SiteSettingsService.Json)?.ImageUrl).OfType<string>()
                .Select(u => (Url: (string?)u, Title: (string?)p.Title)).ToArray();
            Add($"/{p.Slug}", p.UpdatedAt, GroupPages, p.Title, Img(heroImages), videos);
        }
        foreach (var i in industries.Where(i => !i.NoIndex && SelfCanonical(i.CanonicalUrl, $"/industries/{i.Slug}")))
            Add($"/industries/{i.Slug}", i.UpdatedAt, GroupPages, i.Name, Img((i.HeroImageUrl, i.Name)));

        foreach (var s in services.Where(s => !s.NoIndex && SelfCanonical(s.CanonicalUrl, $"/services/{s.Slug}")))
            Add($"/services/{s.Slug}", s.UpdatedAt, GroupServices, s.Name, Img((s.HeroImageUrl, s.Name), (s.OgImageUrl, s.Name)));
        foreach (var c in cases.Where(c => !c.NoIndex && SelfCanonical(c.CanonicalUrl, $"/case-studies/{c.Slug}")))
            Add($"/case-studies/{c.Slug}", c.UpdatedAt, GroupCaseStudies, c.Title,
                Img(new[] { (c.CoverImageUrl, (string?)c.Title) }.Concat(c.GalleryImageUrls.Select(g => ((string?)g, (string?)c.Title))).ToArray()));
        foreach (var p in posts.Where(p => !p.NoIndex && SelfCanonical(p.CanonicalUrl, $"/blog/{p.Slug}")).OrderByDescending(p => p.UpdatedAt))
            Add($"/blog/{p.Slug}", p.UpdatedAt, GroupBlog, p.Title, Img((p.CoverImageUrl, p.CoverImageAlt ?? p.Title)));
        foreach (var j in jobs)
            Add($"/careers/{j.Slug}", j.UpdatedAt, GroupCareers, j.Title);

        // Blog topics (/blog?category=…): self-canonical, indexable archive pages of every category with a live post.
        var liveCategories = await db.Set<BlogPost>().AsNoTracking().Where(p => p.Status == BlogPostStatus.Published && p.PublishedAt <= now)
            .Select(p => new { p.CategoryIds, p.UpdatedAt, p.Seo.NoIndex }).ToListAsync(ct);
        foreach (var cat in await db.Set<BlogCategory>().AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct))
        {
            var inCategory = liveCategories.Where(p => p.CategoryIds.Contains(cat.Id)).ToList();
            if (inCategory.Count > 0)
                Add($"/blog?category={Uri.EscapeDataString(cat.Slug)}", Latest(copyOrSettings, inCategory.Max(p => p.UpdatedAt)), GroupBlog, $"{cat.Name} articles");
        }

        await AddLandingPagesAsync(urls, ct);
        var known = urls.Select(u => u.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var contributor in sitemapContributors)
            foreach (var u in await contributor.UrlsAsync(ct))
            {
                if (!known.Add(u.Path)) continue;
                var images = new[] { (u.ImageUrl, (string?)u.Title) }.Concat((u.Images ?? Array.Empty<string>()).Select(i => ((string?)i, (string?)u.Title))).ToArray();
                var videos = (u.Videos ?? Array.Empty<SitemapVideoContribution>())
                    .Select(v => new SeoVideo(v.Title, v.Description, Abs(v.ContentUrl), null, Abs(v.PosterUrl), Abs(v.CaptionsUrl), v.CaptionsLanguage,
                        Abs(v.PlayerUrl), v.UploadDate ?? u.Modified ?? now, v.DurationSeconds, v.TranscriptMarkdown))
                    .ToList();
                Add(u.Path, u.Modified, contributor.Group, u.Title, Img(images), videos);
            }
        return urls.Select(u => u with { Images = u.Images.DistinctBy(i => i.Url).ToList() }).ToList();
    }

    private async Task AddLandingPagesAsync(List<SitemapUrl> urls, CancellationToken ct)
    {
        var live = await db.Set<LandingPage>().AsNoTracking()
            .Where(p => p.Status == LandingPageStatus.Published && p.PublishedVersionId != null)
            .Select(p => new { p.Id, p.ClientAccountId, VersionId = p.PublishedVersionId!.Value }).ToListAsync(ct);
        if (live.Count > 0)
        {
            var versionIds = live.Select(l => l.VersionId).ToList();
            var versions = await db.Set<LandingPageVersion>().AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToListAsync(ct);
            var clientIds = live.Select(l => l.ClientAccountId).Distinct().ToList();
            var clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Slug, ct);
            foreach (var version in versions)
            {
                if (!clients.TryGetValue(version.ClientAccountId, out var clientSlug)) continue;
                var snap = LandingPageService.ReadSnapshot(version);
                if (snap.NoIndex) continue;
                var path = $"/lp/{clientSlug}/{snap.Slug}";
                var holder = new SeoPage { Path = path };
                var variant = LandingPageService.Variants(snap.Variants).FirstOrDefault();
                if (variant.Key is not null) LandingNodes(holder, variant.Blocks, snap.Name, version.PublishedAt);
                var images = Img((snap.OgImageUrl, snap.MetaTitle ?? snap.Name)).Concat(holder.Images).ToList();
                urls.Add(new SitemapUrl(path, version.PublishedAt, GroupLanding, images, holder.Videos, snap.MetaTitle ?? snap.Name));
            }
        }

        var campaigns = await db.Set<Campaign>().AsNoTracking()
            .Where(c => c.Visibility == CampaignVisibility.Public && (c.Status == CampaignStatus.Scheduled || c.Status == CampaignStatus.Active))
            .Select(c => new { c.Slug, c.Title, c.UpdatedAt, c.HeroImageUrl }).ToListAsync(ct);
        foreach (var c in campaigns)
            urls.Add(new SitemapUrl($"/c/{c.Slug}", c.UpdatedAt, GroupLanding, Img((c.HeroImageUrl, c.Title)), NoVideos, c.Title));
    }
}
