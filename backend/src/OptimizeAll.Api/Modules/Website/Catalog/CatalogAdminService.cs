using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Website.Pages;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.Catalog;

/// <summary>
/// Staff CMS for service categories, services and packages, industries, case studies, testimonials, team members and
/// pages (<c>site.manage</c>). Every change is validated server-side and audited.
/// </summary>
public sealed class CatalogAdminService(
    CmsStore store, IAuditLogger audit, WebsiteRules rules, PageBlockValidator blocks, TimeProvider clock, ICurrentUser currentUser)
{
    private Microsoft.EntityFrameworkCore.DbContext Db => store.Db;

    // ---------------------------------------------------------------- Categories

    public async Task<IReadOnlyList<ServiceCategoryDto>> ListCategoriesAsync(CancellationToken ct)
    {
        var counts = await Db.Set<AgencyService>().AsNoTracking().GroupBy(s => s.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var list = await Db.Set<ServiceCategory>().AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        return list.Select(c => ToDto(c, counts.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<ServiceCategoryDto> CreateCategoryAsync(ServiceCategoryInput input, CancellationToken ct)
    {
        var c = new ServiceCategory();
        Apply(c, input);
        await store.EnsureSlugFreeAsync<ServiceCategory>(c.Slug, null, ct);
        Db.Add(c);
        audit.Record("website.category_created", nameof(ServiceCategory), c.Id, after: ToDto(c, 0));
        await store.SaveAsync(ct);
        return ToDto(c, 0);
    }

    public async Task<ServiceCategoryDto> UpdateCategoryAsync(Guid id, ServiceCategoryInput input, CancellationToken ct)
    {
        var c = await store.FindAsync<ServiceCategory>(id, ct);
        CmsStore.CheckStamp(store.Db, c, input.ConcurrencyStamp);
        var before = ToDto(c, 0);
        Apply(c, input);
        await store.EnsureSlugFreeAsync<ServiceCategory>(c.Slug, c.Id, ct);
        audit.Record("website.category_updated", nameof(ServiceCategory), c.Id, before, ToDto(c, 0));
        await store.SaveAsync(ct);
        return ToDto(c, await Db.Set<AgencyService>().CountAsync(s => s.CategoryId == id, ct));
    }

    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct)
    {
        if (await Db.Set<AgencyService>().AnyAsync(s => s.CategoryId == id, ct))
            throw DomainException.Conflict("website.category_in_use", "Move or delete this category's services first.");
        await store.DeleteAsync<ServiceCategory>(id, "website.category_deleted", c => ToDto(c, 0), ct);
    }

    public async Task<ReorderResult> ReorderCategoriesAsync(ReorderInput input, CancellationToken ct) =>
        new(await store.ReorderAsync<ServiceCategory>(input.Ids, "website.categories_reordered", (c, o) => c.SortOrder = o, ct));

    private static void Apply(ServiceCategory c, ServiceCategoryInput r)
    {
        var e = new FieldErrors();
        c.Slug = WebsiteRules.Slug(r.Slug, "slug", e);
        e.ThrowIfAny();
        c.Name = r.Name.Trim();
        c.Description = WebsiteRules.Clean(r.Description);
        c.Icon = WebsiteRules.Clean(r.Icon);
        c.SortOrder = r.SortOrder;
        c.IsPublished = r.IsPublished;
    }

    public static ServiceCategoryDto ToDto(ServiceCategory c, int count) =>
        new(c.Id, c.Slug, c.Name, c.Description, c.Icon, c.SortOrder, c.IsPublished, count, c.UpdatedAt, c.ConcurrencyStamp);

    // ---------------------------------------------------------------- Services

    public async Task<PagedResult<ServiceSummaryDto>> ListServicesAsync(ServiceQuery query, CancellationToken ct)
    {
        var q = from s in Db.Set<AgencyService>().AsNoTracking()
                join c in Db.Set<ServiceCategory>() on s.CategoryId equals c.Id
                select new { s, c };
        if (query.CategoryId is { } cat) q = q.Where(x => x.s.CategoryId == cat);
        if (query.IsPublished is { } pub) q = q.Where(x => x.s.IsPublished == pub);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(x => EF.Functions.Like(x.s.Name, p, "\\") || EF.Functions.Like(x.s.Slug, p, "\\") || EF.Functions.Like(x.s.Tagline, p, "\\"));
        }
        var page = await q.OrderBy(x => x.c.SortOrder).ThenBy(x => x.s.SortOrder).ThenBy(x => x.s.Name).ThenBy(x => x.s.Id)
            .Select(x => new ServiceSummaryDto(x.s.Id, x.c.Id, x.c.Name, x.s.Slug, x.s.Name, x.s.Tagline, x.s.Icon, x.s.IsPublished, x.s.IsFeatured,
                x.s.SortOrder, x.s.Packages.Count, x.s.UpdatedAt))
            .ToPagedAsync(query, ct);
        return page;
    }

    public async Task<ServiceDto> GetServiceAsync(Guid id, CancellationToken ct)
    {
        var s = await Db.Set<AgencyService>().AsNoTracking().Include(x => x.Packages).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw CmsStore.NotFound<AgencyService>();
        return ToDto(s);
    }

    public async Task<ServiceDto> CreateServiceAsync(ServiceInput input, CancellationToken ct)
    {
        var s = new AgencyService();
        await ApplyAsync(s, input, ct);
        await store.EnsureSlugFreeAsync<AgencyService>(s.Slug, null, ct);
        Db.Add(s);
        audit.Record("website.service_created", nameof(AgencyService), s.Id, after: ToDto(s));
        await store.SaveAsync(ct);
        return ToDto(s);
    }

    public async Task<ServiceDto> UpdateServiceAsync(Guid id, ServiceInput input, CancellationToken ct)
    {
        var s = await Db.Set<AgencyService>().Include(x => x.Packages).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw CmsStore.NotFound<AgencyService>();
        CmsStore.CheckStamp(store.Db, s, input.ConcurrencyStamp);
        var before = ToDto(s);
        await ApplyAsync(s, input, ct);
        await store.EnsureSlugFreeAsync<AgencyService>(s.Slug, s.Id, ct);
        audit.Record("website.service_updated", nameof(AgencyService), s.Id, before, ToDto(s));
        await store.SaveAsync(ct);
        return ToDto(s);
    }

    public Task DeleteServiceAsync(Guid id, CancellationToken ct) =>
        store.DeleteAsync<AgencyService>(id, "website.service_deleted", s => new { s.Id, s.Slug, s.Name }, ct);

    public async Task<ReorderResult> ReorderServicesAsync(ReorderInput input, CancellationToken ct) =>
        new(await store.ReorderAsync<AgencyService>(input.Ids, "website.services_reordered", (s, o) => s.SortOrder = o, ct));

    private async Task ApplyAsync(AgencyService s, ServiceInput r, CancellationToken ct)
    {
        var e = new FieldErrors();
        var slug = WebsiteRules.Slug(r.Slug, "slug", e);
        if (!await Db.Set<ServiceCategory>().AnyAsync(c => c.Id == r.CategoryId, ct)) e.Add("categoryId", "Pick an existing category.");
        var related = (r.RelatedServiceIds ?? new List<Guid>()).Distinct().Where(x => x != s.Id).ToList();
        if (related.Count > 8) e.Add("relatedServiceIds", "Pick at most 8 related services.");
        if (related.Count > 0 && await Db.Set<AgencyService>().CountAsync(x => related.Contains(x.Id), ct) != related.Count)
            e.Add("relatedServiceIds", "Some related services no longer exist.");
        var ctaUrl = WebsiteRules.Link(r.CtaUrl, "ctaUrl", e);
        var ctaLabel = WebsiteRules.Clean(r.CtaLabel);
        if ((ctaUrl is null) != (ctaLabel is null)) e.Add("ctaLabel", "Button label and link go together.");
        var seo = rules.Seo(r.Seo, e);
        var hero = rules.Image(r.HeroImageUrl, "heroImageUrl", e);
        var overview = WebsiteRules.Markdown(r.OverviewMarkdown, "overviewMarkdown", e);
        var problems = WebsiteRules.Lines(r.ProblemsSolved, "problemsSolved", e, 12, 300);
        var deliverables = WebsiteRules.Lines(r.Deliverables, "deliverables", e, 30, 300);
        var steps = WebsiteRules.Steps(r.ProcessSteps, "processSteps", e);
        var tools = WebsiteRules.Lines(r.Tools, "tools", e, 30, 60);
        var kpis = WebsiteRules.Lines(r.Kpis, "kpis", e, 15, 120);
        var faqs = WebsiteRules.Faqs(r.Faqs, "faqs", e);
        if (r.IsPublished && problems.Count == 0 && deliverables.Count == 0 && overview is null)
            e.Add("overviewMarkdown", "Add an overview, problems solved or deliverables before publishing.");
        e.ThrowIfAny();

        s.CategoryId = r.CategoryId!.Value;
        s.Slug = slug;
        s.Name = r.Name.Trim();
        s.Tagline = r.Tagline.Trim();
        s.HeroTitle = WebsiteRules.Clean(r.HeroTitle);
        s.HeroBody = WebsiteRules.Clean(r.HeroBody);
        s.OverviewMarkdown = overview;
        s.ProblemsSolved = problems;
        s.Deliverables = deliverables;
        s.ProcessSteps = steps;
        s.Tools = tools;
        s.Kpis = kpis;
        s.Faqs = faqs;
        s.RelatedServiceIds = related;
        s.Icon = WebsiteRules.Clean(r.Icon);
        s.HeroImageUrl = hero;
        s.CtaLabel = ctaLabel;
        s.CtaUrl = ctaUrl;
        s.Seo = seo;
        s.IsPublished = r.IsPublished;
        s.IsFeatured = r.IsFeatured;
        s.SortOrder = r.SortOrder;
    }

    public static ServiceDto ToDto(AgencyService s) => new(
        s.Id, s.CategoryId, s.Slug, s.Name, s.Tagline, s.HeroTitle, s.HeroBody, s.OverviewMarkdown, s.ProblemsSolved, s.Deliverables,
        s.ProcessSteps, s.Tools, s.Kpis, s.Faqs, s.RelatedServiceIds, s.Icon, s.HeroImageUrl, s.CtaLabel, s.CtaUrl, SeoDto.From(s.Seo),
        s.IsPublished, s.IsFeatured, s.SortOrder, s.Packages.OrderBy(p => p.SortOrder).Select(ToDto).ToList(), s.CreatedAt, s.UpdatedAt,
        s.ConcurrencyStamp);

    // ---------------------------------------------------------------- Packages

    public async Task<PackageDto> CreatePackageAsync(Guid serviceId, PackageInput input, CancellationToken ct)
    {
        var service = await store.FindAsync<AgencyService>(serviceId, ct);
        var p = new ServicePackage { ServiceId = service.Id };
        await ApplyAsync(p, input, ct);
        Db.Add(p);
        audit.Record("website.package_created", nameof(ServicePackage), p.Id, after: ToDto(p));
        await Db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    public async Task<PackageDto> UpdatePackageAsync(Guid serviceId, Guid id, PackageInput input, CancellationToken ct)
    {
        var p = await Db.Set<ServicePackage>().FirstOrDefaultAsync(x => x.Id == id && x.ServiceId == serviceId, ct)
            ?? throw CmsStore.NotFound<ServicePackage>();
        CmsStore.CheckStamp(store.Db, p, input.ConcurrencyStamp);
        var before = ToDto(p);
        await ApplyAsync(p, input, ct);
        audit.Record("website.package_updated", nameof(ServicePackage), p.Id, before, ToDto(p));
        await Db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    /// <summary>Deletes a package. Packages referenced by proposals or invoices should be deactivated instead.</summary>
    public async Task DeletePackageAsync(Guid serviceId, Guid id, CancellationToken ct)
    {
        if (!await Db.Set<ServicePackage>().AnyAsync(x => x.Id == id && x.ServiceId == serviceId, ct))
            throw CmsStore.NotFound<ServicePackage>();
        await store.DeleteAsync<ServicePackage>(id, "website.package_deleted", p => ToDto(p), ct);
    }

    private async Task ApplyAsync(ServicePackage p, PackageInput r, CancellationToken ct)
    {
        var e = new FieldErrors();
        var currency = (r.Currency ?? string.Empty).Trim().ToUpperInvariant();
        if (!WebsiteRules.IsCurrency(currency)) e.Add("currency", "Use a three-letter currency code such as USD.");
        if (r.BillingPeriod is not { } period || !Enum.IsDefined(period)) e.Add("billingPeriod", "Pick a billing period.");
        if (!r.IsCustomQuote && r.Price is null) e.Add("price", "Enter a price, or mark the package as a custom quote.");
        if (r.IsCustomQuote && r.Price is not null) e.Add("price", "Custom-quote packages have no fixed price.");
        var features = WebsiteRules.Lines(r.Features, "features", e, 25, 160);
        e.ThrowIfAny();

        if (r.IsMostPopular)
        {
            // One "most popular" package per service.
            var others = await Db.Set<ServicePackage>().Where(x => x.ServiceId == p.ServiceId && x.Id != p.Id && x.IsMostPopular).ToListAsync(ct);
            foreach (var o in others) o.IsMostPopular = false;
        }
        p.Name = r.Name.Trim();
        p.Description = WebsiteRules.Clean(r.Description);
        p.Currency = currency;
        p.Price = r.Price is { } price ? Money.Round(price, currency) : null;
        p.SetupFee = r.SetupFee is { } fee && fee > 0 ? Money.Round(fee, currency) : null;
        p.BillingPeriod = r.BillingPeriod!.Value;
        p.Features = features;
        p.IsMostPopular = r.IsMostPopular;
        p.IsCustomQuote = r.IsCustomQuote;
        p.IsActive = r.IsActive;
        p.SortOrder = r.SortOrder;
    }

    public static PackageDto ToDto(ServicePackage p) => new(
        p.Id, p.ServiceId, p.Name, p.Description, p.Price, p.Currency, p.BillingPeriod, p.SetupFee, p.Features, p.IsMostPopular,
        p.IsCustomQuote, p.IsActive, p.SortOrder, p.UpdatedAt, p.ConcurrencyStamp);

    // ---------------------------------------------------------------- Industries

    public async Task<PagedResult<IndustryDto>> ListIndustriesAsync(CmsQuery query, CancellationToken ct)
    {
        var q = Db.Set<Industry>().AsNoTracking();
        if (query.IsPublished is { } pub) q = q.Where(x => x.IsPublished == pub);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(x => EF.Functions.Like(x.Name, p, "\\") || EF.Functions.Like(x.Slug, p, "\\"));
        }
        return CmsStore.Map(await q.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id).ToPagedAsync(query, ct), ToDto);
    }

    public async Task<IndustryDto> GetIndustryAsync(Guid id, CancellationToken ct) => ToDto(await store.FindAsync<Industry>(id, ct, true));

    public async Task<IndustryDto> CreateIndustryAsync(IndustryInput input, CancellationToken ct)
    {
        var x = new Industry();
        await ApplyAsync(x, input, ct);
        await store.EnsureSlugFreeAsync<Industry>(x.Slug, null, ct);
        Db.Add(x);
        audit.Record("website.industry_created", nameof(Industry), x.Id, after: ToDto(x));
        await store.SaveAsync(ct);
        return ToDto(x);
    }

    public async Task<IndustryDto> UpdateIndustryAsync(Guid id, IndustryInput input, CancellationToken ct)
    {
        var x = await store.FindAsync<Industry>(id, ct);
        CmsStore.CheckStamp(store.Db, x, input.ConcurrencyStamp);
        var before = ToDto(x);
        await ApplyAsync(x, input, ct);
        await store.EnsureSlugFreeAsync<Industry>(x.Slug, x.Id, ct);
        audit.Record("website.industry_updated", nameof(Industry), x.Id, before, ToDto(x));
        await store.SaveAsync(ct);
        return ToDto(x);
    }

    public Task DeleteIndustryAsync(Guid id, CancellationToken ct) =>
        store.DeleteAsync<Industry>(id, "website.industry_deleted", x => new { x.Id, x.Slug, x.Name }, ct);

    private async Task ApplyAsync(Industry x, IndustryInput r, CancellationToken ct)
    {
        var e = new FieldErrors();
        var slug = WebsiteRules.Slug(r.Slug, "slug", e);
        var services = await ServiceIdsAsync(r.ServiceIds, "serviceIds", e, ct);
        var seo = rules.Seo(r.Seo, e);
        var hero = rules.Image(r.HeroImageUrl, "heroImageUrl", e);
        var body = WebsiteRules.Markdown(r.BodyMarkdown, "bodyMarkdown", e);
        var challenges = WebsiteRules.Lines(r.Challenges, "challenges", e, 12, 300);
        e.ThrowIfAny();
        x.Slug = slug;
        x.Name = r.Name.Trim();
        x.Summary = r.Summary.Trim();
        x.BodyMarkdown = body;
        x.Challenges = challenges;
        x.ServiceIds = services;
        x.Icon = WebsiteRules.Clean(r.Icon);
        x.HeroImageUrl = hero;
        x.Seo = seo;
        x.IsPublished = r.IsPublished;
        x.SortOrder = r.SortOrder;
    }

    public static IndustryDto ToDto(Industry x) => new(
        x.Id, x.Slug, x.Name, x.Summary, x.BodyMarkdown, x.Challenges, x.ServiceIds, x.Icon, x.HeroImageUrl, SeoDto.From(x.Seo), x.IsPublished,
        x.SortOrder, x.UpdatedAt, x.ConcurrencyStamp);

    // ---------------------------------------------------------------- Case studies

    public async Task<PagedResult<CaseStudyDto>> ListCaseStudiesAsync(CmsQuery query, CancellationToken ct)
    {
        var q = Db.Set<CaseStudy>().AsNoTracking();
        if (query.IsPublished is { } pub) q = q.Where(x => x.IsPublished == pub);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(x => EF.Functions.Like(x.Title, p, "\\") || EF.Functions.Like(x.ClientName, p, "\\") || EF.Functions.Like(x.Slug, p, "\\"));
        }
        return CmsStore.Map(await q.OrderBy(x => x.SortOrder).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id).ToPagedAsync(query, ct), ToDto);
    }

    public async Task<CaseStudyDto> GetCaseStudyAsync(Guid id, CancellationToken ct) => ToDto(await store.FindAsync<CaseStudy>(id, ct, true));

    public async Task<CaseStudyDto> CreateCaseStudyAsync(CaseStudyInput input, CancellationToken ct)
    {
        var x = new CaseStudy();
        await ApplyAsync(x, input, ct);
        await store.EnsureSlugFreeAsync<CaseStudy>(x.Slug, null, ct);
        Db.Add(x);
        audit.Record("website.case_study_created", nameof(CaseStudy), x.Id, after: ToDto(x));
        await store.SaveAsync(ct);
        return ToDto(x);
    }

    public async Task<CaseStudyDto> UpdateCaseStudyAsync(Guid id, CaseStudyInput input, CancellationToken ct)
    {
        var x = await store.FindAsync<CaseStudy>(id, ct);
        CmsStore.CheckStamp(store.Db, x, input.ConcurrencyStamp);
        var before = ToDto(x);
        await ApplyAsync(x, input, ct);
        await store.EnsureSlugFreeAsync<CaseStudy>(x.Slug, x.Id, ct);
        audit.Record("website.case_study_updated", nameof(CaseStudy), x.Id, before, ToDto(x));
        await store.SaveAsync(ct);
        return ToDto(x);
    }

    public Task DeleteCaseStudyAsync(Guid id, CancellationToken ct) =>
        store.DeleteAsync<CaseStudy>(id, "website.case_study_deleted", x => new { x.Id, x.Slug, x.Title }, ct);

    private async Task ApplyAsync(CaseStudy x, CaseStudyInput r, CancellationToken ct)
    {
        var e = new FieldErrors();
        var slug = WebsiteRules.Slug(r.Slug, "slug", e);
        var services = await ServiceIdsAsync(r.ServiceIds, "serviceIds", e, ct);
        if (r.IndustryId is { } ind && !await Db.Set<Industry>().AnyAsync(i => i.Id == ind, ct)) e.Add("industryId", "Pick an existing industry.");
        var metrics = new List<ResultMetric>();
        var metricInputs = r.Metrics ?? new List<ResultMetricInput>();
        for (var i = 0; i < metricInputs.Count; i++)
        {
            var m = metricInputs[i];
            var label = WebsiteRules.Clean(m.Label);
            var value = WebsiteRules.Clean(m.Value);
            if (label is null && value is null) continue;
            if (label is null || value is null) e.Add($"metrics[{i}]", "Each result needs a label and a value.");
            // Never default: an estimate must not be shown as measured because someone forgot to say which it is.
            if (m.Measurement is not { } kind || !Enum.IsDefined(kind)) e.Add($"metrics[{i}].measurement", "Say whether the figure is measured or estimated.");
            metrics.Add(new ResultMetric(label ?? string.Empty, value ?? string.Empty, m.Measurement ?? MetricMeasurement.Estimated, WebsiteRules.Clean(m.Context)));
        }
        if (metrics.Count > 8) e.Add("metrics", "Show at most 8 results.");
        var seo = rules.Seo(r.Seo, e);
        var cover = rules.Image(r.CoverImageUrl, "coverImageUrl", e);
        var gallery = rules.Images(r.GalleryImageUrls, "galleryImageUrls", e, 12);
        var challenge = WebsiteRules.Markdown(r.ChallengeMarkdown, "challengeMarkdown", e, 30000);
        var strategy = WebsiteRules.Markdown(r.StrategyMarkdown, "strategyMarkdown", e, 30000);
        var execution = WebsiteRules.Markdown(r.ExecutionMarkdown, "executionMarkdown", e, 30000);
        var quote = WebsiteRules.Clean(r.TestimonialQuote);
        var author = WebsiteRules.Clean(r.TestimonialAuthor);
        if (quote is not null && author is null) e.Add("testimonialAuthor", "Say who gave the testimonial.");
        e.ThrowIfAny();

        x.Slug = slug;
        x.Title = r.Title.Trim();
        x.ClientName = r.ClientName.Trim();
        x.ClientAnonymized = r.ClientAnonymized;
        x.Summary = r.Summary.Trim();
        x.IndustryId = r.IndustryId;
        x.ServiceIds = services;
        x.ChallengeMarkdown = challenge;
        x.StrategyMarkdown = strategy;
        x.ExecutionMarkdown = execution;
        x.Metrics = metrics;
        x.TestimonialQuote = quote;
        x.TestimonialAuthor = author;
        x.TestimonialRole = WebsiteRules.Clean(r.TestimonialRole);
        x.CoverImageUrl = cover;
        x.GalleryImageUrls = gallery;
        x.Seo = seo;
        if (r.IsPublished && !x.IsPublished) x.PublishedAt ??= clock.GetUtcNow().UtcDateTime;
        x.IsPublished = r.IsPublished;
        x.IsFeatured = r.IsFeatured;
        x.SortOrder = r.SortOrder;
    }

    public static CaseStudyDto ToDto(CaseStudy x) => new(
        x.Id, x.Slug, x.Title, x.ClientName, x.ClientAnonymized, x.Summary, x.IndustryId, x.ServiceIds, x.ChallengeMarkdown, x.StrategyMarkdown,
        x.ExecutionMarkdown, x.Metrics, x.TestimonialQuote, x.TestimonialAuthor, x.TestimonialRole, x.CoverImageUrl, x.GalleryImageUrls,
        SeoDto.From(x.Seo), x.IsPublished, x.IsFeatured, x.PublishedAt, x.SortOrder, x.UpdatedAt, x.ConcurrencyStamp);

    // ---------------------------------------------------------------- Testimonials

    public async Task<PagedResult<TestimonialDto>> ListTestimonialsAsync(CmsQuery query, CancellationToken ct)
    {
        var q = Db.Set<Testimonial>().AsNoTracking();
        if (query.IsPublished is { } pub) q = q.Where(x => x.IsPublished == pub);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(x => EF.Functions.Like(x.AuthorName, p, "\\") || EF.Functions.Like(x.Quote, p, "\\") || (x.Company != null && EF.Functions.Like(x.Company, p, "\\")));
        }
        return CmsStore.Map(await q.OrderBy(x => x.SortOrder).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id).ToPagedAsync(query, ct), ToDto);
    }

    public async Task<TestimonialDto> CreateTestimonialAsync(TestimonialInput input, CancellationToken ct)
    {
        var x = new Testimonial();
        await ApplyAsync(x, input, ct);
        Db.Add(x);
        audit.Record("website.testimonial_created", nameof(Testimonial), x.Id, after: ToDto(x));
        await Db.SaveChangesAsync(ct);
        return ToDto(x);
    }

    public async Task<TestimonialDto> UpdateTestimonialAsync(Guid id, TestimonialInput input, CancellationToken ct)
    {
        var x = await store.FindAsync<Testimonial>(id, ct);
        CmsStore.CheckStamp(store.Db, x, input.ConcurrencyStamp);
        var before = ToDto(x);
        await ApplyAsync(x, input, ct);
        audit.Record("website.testimonial_updated", nameof(Testimonial), x.Id, before, ToDto(x));
        await Db.SaveChangesAsync(ct);
        return ToDto(x);
    }

    public Task DeleteTestimonialAsync(Guid id, CancellationToken ct) =>
        store.DeleteAsync<Testimonial>(id, "website.testimonial_deleted", x => ToDto(x), ct);

    public async Task<ReorderResult> ReorderIndustriesAsync(ReorderInput input, CancellationToken ct) =>
        new(await store.ReorderAsync<Industry>(input.Ids, "website.industries_reordered", (x, o) => x.SortOrder = o, ct));

    public async Task<ReorderResult> ReorderCaseStudiesAsync(ReorderInput input, CancellationToken ct) =>
        new(await store.ReorderAsync<CaseStudy>(input.Ids, "website.case_studies_reordered", (x, o) => x.SortOrder = o, ct));

    public async Task<ReorderResult> ReorderTestimonialsAsync(ReorderInput input, CancellationToken ct) =>
        new(await store.ReorderAsync<Testimonial>(input.Ids, "website.testimonials_reordered", (t, o) => t.SortOrder = o, ct));

    private async Task ApplyAsync(Testimonial x, TestimonialInput r, CancellationToken ct)
    {
        var e = new FieldErrors();
        var avatar = rules.Image(r.AvatarUrl, "avatarUrl", e);
        if (r.ServiceId is { } sid && !await Db.Set<AgencyService>().AnyAsync(s => s.Id == sid, ct)) e.Add("serviceId", "Pick an existing service.");
        e.ThrowIfAny();
        x.Quote = r.Quote.Trim();
        x.AuthorName = r.AuthorName.Trim();
        x.AuthorRole = WebsiteRules.Clean(r.AuthorRole);
        x.Company = WebsiteRules.Clean(r.Company);
        x.Rating = r.Rating;
        x.AvatarUrl = avatar;
        x.ServiceId = r.ServiceId;
        x.IsPublished = r.IsPublished;
        x.IsFeatured = r.IsFeatured;
        x.SortOrder = r.SortOrder;
    }

    public static TestimonialDto ToDto(Testimonial x) => new(
        x.Id, x.Quote, x.AuthorName, x.AuthorRole, x.Company, x.Rating, x.AvatarUrl, x.ServiceId, x.IsPublished, x.IsFeatured, x.SortOrder,
        x.UpdatedAt, x.ConcurrencyStamp);

    // ---------------------------------------------------------------- Team

    public async Task<IReadOnlyList<TeamMemberDto>> ListTeamAsync(CancellationToken ct) =>
        (await Db.Set<TeamMember>().AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<TeamMemberDto> CreateTeamMemberAsync(TeamMemberInput input, CancellationToken ct)
    {
        var x = new TeamMember();
        Apply(x, input);
        await store.EnsureSlugFreeAsync<TeamMember>(x.Slug, null, ct);
        Db.Add(x);
        audit.Record("website.team_member_created", nameof(TeamMember), x.Id, after: ToDto(x));
        await store.SaveAsync(ct);
        return ToDto(x);
    }

    public async Task<TeamMemberDto> UpdateTeamMemberAsync(Guid id, TeamMemberInput input, CancellationToken ct)
    {
        var x = await store.FindAsync<TeamMember>(id, ct);
        CmsStore.CheckStamp(store.Db, x, input.ConcurrencyStamp);
        var before = ToDto(x);
        Apply(x, input);
        await store.EnsureSlugFreeAsync<TeamMember>(x.Slug, x.Id, ct);
        audit.Record("website.team_member_updated", nameof(TeamMember), x.Id, before, ToDto(x));
        await store.SaveAsync(ct);
        return ToDto(x);
    }

    public Task DeleteTeamMemberAsync(Guid id, CancellationToken ct) =>
        store.DeleteAsync<TeamMember>(id, "website.team_member_deleted", x => ToDto(x), ct);

    public async Task<ReorderResult> ReorderTeamAsync(ReorderInput input, CancellationToken ct) =>
        new(await store.ReorderAsync<TeamMember>(input.Ids, "website.team_reordered", (t, o) => t.SortOrder = o, ct));

    private void Apply(TeamMember x, TeamMemberInput r)
    {
        var e = new FieldErrors();
        var slug = WebsiteRules.Slug(r.Slug, "slug", e);
        var photo = rules.Image(r.PhotoUrl, "photoUrl", e);
        var links = new List<SiteLink>();
        foreach (var (l, i) in (r.SocialLinks ?? new List<SiteLinkInput>()).Select((l, i) => (l, i)))
        {
            var label = WebsiteRules.Clean(l.Label);
            var url = WebsiteRules.Clean(l.Url);
            if (label is null && url is null) continue;
            if (label is null || url is null || !url.StartsWith("https://", StringComparison.Ordinal) || !Accounts.FieldRules.IsSafeContentUrl(url))
            {
                e.Add($"socialLinks[{i}]", "Each profile needs a label and an https:// link.");
                continue;
            }
            links.Add(new SiteLink(label, url));
        }
        if (links.Count > 8) e.Add("socialLinks", "Add at most 8 profiles.");
        var expertise = WebsiteRules.Lines(r.Expertise, "expertise", e, 12, 60);
        e.ThrowIfAny();
        x.Slug = slug;
        x.Name = r.Name.Trim();
        x.Role = r.Role.Trim();
        x.Bio = WebsiteRules.Clean(r.Bio);
        x.PhotoUrl = photo;
        x.Expertise = expertise;
        x.SocialLinks = links;
        x.IsPublished = r.IsPublished;
        x.SortOrder = r.SortOrder;
    }

    public static TeamMemberDto ToDto(TeamMember x) => new(
        x.Id, x.Slug, x.Name, x.Role, x.Bio, x.PhotoUrl, x.Expertise, x.SocialLinks, x.IsPublished, x.SortOrder, x.UpdatedAt, x.ConcurrencyStamp);

    // ---------------------------------------------------------------- Pages

    public async Task<IReadOnlyList<SitePageSummaryDto>> ListPagesAsync(CancellationToken ct)
    {
        var pages = await Db.Set<SitePage>().AsNoTracking().OrderBy(p => p.Kind).ThenBy(p => p.SortOrder).ThenBy(p => p.Title).ToListAsync(ct);
        return pages.Select(p => new SitePageSummaryDto(p.Id, p.Slug, p.Title, p.Kind, p.IsPublished, PageBlockValidator.Parse(p.BlocksJson).Count,
            p.UpdatedAt, p.PublishAt, p.Version)).ToList();
    }

    public async Task<SitePageDto> GetPageAsync(Guid id, CancellationToken ct) => ToDto(await store.FindAsync<SitePage>(id, ct, true));

    public async Task<SitePageDto> CreatePageAsync(SitePageInput input, CancellationToken ct)
    {
        var x = new SitePage();
        Apply(x, input);
        await store.EnsureSlugFreeAsync<SitePage>(x.Slug, null, ct);
        Db.Add(x);
        AddRevision(x, "created", input.RevisionNote);
        audit.Record("website.page_created", nameof(SitePage), x.Id, after: new { x.Slug, x.Title, x.Kind, x.IsPublished, x.PublishAt });
        await store.SaveAsync(ct);
        return ToDto(x);
    }

    public async Task<SitePageDto> UpdatePageAsync(Guid id, SitePageInput input, CancellationToken ct)
    {
        var x = await store.FindAsync<SitePage>(id, ct);
        CmsStore.CheckStamp(store.Db, x, input.ConcurrencyStamp);
        var before = new { x.Slug, x.Title, x.Kind, x.IsPublished, x.PublishAt, Blocks = x.BlocksJson };
        await EnsureBaselineRevisionAsync(x, ct);
        Apply(x, input);
        await store.EnsureSlugFreeAsync<SitePage>(x.Slug, x.Id, ct);
        AddRevision(x, "updated", input.RevisionNote);
        audit.Record("website.page_updated", nameof(SitePage), x.Id, before, new { x.Slug, x.Title, x.Kind, x.IsPublished, x.PublishAt, Blocks = x.BlocksJson });
        await store.SaveAsync(ct);
        return ToDto(x);
    }

    public Task DeletePageAsync(Guid id, CancellationToken ct) =>
        store.DeleteAsync<SitePage>(id, "website.page_deleted", x => new { x.Slug, x.Title, Blocks = x.BlocksJson }, ct);

    /// <summary>The page's saved versions, newest first.</summary>
    public async Task<IReadOnlyList<SitePageRevisionSummaryDto>> ListRevisionsAsync(Guid pageId, CancellationToken ct)
    {
        var page = await store.FindAsync<SitePage>(pageId, ct, true);
        var rows = await Db.Set<SitePageRevision>().AsNoTracking().Where(r => r.PageId == pageId)
            .OrderByDescending(r => r.Version).Take(200).ToListAsync(ct);
        var names = await AuthorNamesAsync(rows.Select(r => r.AuthorUserId), ct);
        return rows.Select(r => new SitePageRevisionSummaryDto(r.Version, r.Action, r.Note, r.Title, r.IsPublished, r.PublishAt, r.AuthorUserId,
            r.AuthorUserId is { } a ? names.GetValueOrDefault(a) : null, r.CreatedAt, r.Version == page.Version)).ToList();
    }

    public async Task<SitePageRevisionDto> GetRevisionAsync(Guid pageId, int version, CancellationToken ct)
    {
        var page = await store.FindAsync<SitePage>(pageId, ct, true);
        var r = await Db.Set<SitePageRevision>().AsNoTracking().FirstOrDefaultAsync(x => x.PageId == pageId && x.Version == version, ct)
                ?? throw new DomainException("website.not_found", "That version of the page was not found.", DomainErrorKind.NotFound);
        var names = await AuthorNamesAsync(new[] { r.AuthorUserId }, ct);
        return new SitePageRevisionDto(r.Version, r.Action, r.Note, r.Slug, r.Title, r.Summary, r.Kind, PageBlockValidator.Parse(r.BlocksJson),
            SeoDto.From(ParseSeo(r.SeoJson)), r.IsPublished, r.PublishAt, r.AuthorUserId,
            r.AuthorUserId is { } a ? names.GetValueOrDefault(a) : null, r.CreatedAt, r.Version == page.Version);
    }

    /// <summary>
    /// Restores the content (slug, title, summary, kind, blocks, SEO) of an earlier version as a new version. Publishing state
    /// and schedule stay as they are, so restoring never publishes or hides a page by surprise.
    /// </summary>
    public async Task<SitePageDto> RestoreRevisionAsync(Guid pageId, int version, RestorePageRevisionInput input, CancellationToken ct)
    {
        var x = await store.FindAsync<SitePage>(pageId, ct);
        CmsStore.CheckStamp(store.Db, x, input.ConcurrencyStamp);
        var r = await Db.Set<SitePageRevision>().AsNoTracking().FirstOrDefaultAsync(v => v.PageId == pageId && v.Version == version, ct)
                ?? throw new DomainException("website.not_found", "That version of the page was not found.", DomainErrorKind.NotFound);
        if (r.Version == x.Version)
            throw DomainException.Conflict("website.revision_current", "This version is already the current one.");
        await EnsureBaselineRevisionAsync(x, ct);
        await store.EnsureSlugFreeAsync<SitePage>(r.Slug, x.Id, ct);
        var before = new { x.Slug, x.Title, x.Version, Blocks = x.BlocksJson };
        x.Slug = r.Slug;
        x.Title = r.Title;
        x.Summary = r.Summary;
        x.Kind = r.Kind;
        x.BlocksJson = r.BlocksJson;
        x.Seo = ParseSeo(r.SeoJson);
        AddRevision(x, "restored", WebsiteRules.Clean(input.Note) ?? $"Restored version {r.Version}");
        audit.Record("website.page_restored", nameof(SitePage), x.Id, before, new { x.Slug, x.Title, RestoredVersion = r.Version, x.Version });
        await store.SaveAsync(ct);
        return ToDto(x);
    }

    /// <summary>Pages saved before revisions existed (e.g. seeded legal pages) get their current content recorded as version 0 first.</summary>
    private async Task EnsureBaselineRevisionAsync(SitePage x, CancellationToken ct)
    {
        if (x.Version > 0 || await Db.Set<SitePageRevision>().AnyAsync(r => r.PageId == x.Id, ct)) return;
        Db.Add(Snapshot(x, 0, "initial", "Content before version history was enabled"));
    }

    private void AddRevision(SitePage x, string action, string? note)
    {
        x.Version += 1;
        Db.Add(Snapshot(x, x.Version, action, WebsiteRules.Clean(note)));
    }

    private SitePageRevision Snapshot(SitePage x, int version, string action, string? note) => new()
    {
        PageId = x.Id, Version = version, Slug = x.Slug, Title = x.Title, Summary = x.Summary, Kind = x.Kind, BlocksJson = x.BlocksJson,
        SeoJson = System.Text.Json.JsonSerializer.Serialize(x.Seo), IsPublished = x.IsPublished, PublishAt = x.PublishAt, Action = action,
        Note = note, AuthorUserId = currentUser.IdOrNull, CreatedAt = clock.GetUtcNow().UtcDateTime,
    };

    private static SeoMeta ParseSeo(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<SeoMeta>(json) ?? new SeoMeta();
        }
        catch (System.Text.Json.JsonException)
        {
            return new SeoMeta();
        }
    }

    private async Task<Dictionary<Guid, string>> AuthorNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, string>();
        return await Db.Set<Domain.Identity.User>().AsNoTracking().Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    /// <summary>Validates blocks without saving (live preview in the editor shows the normalized result).</summary>
    public IReadOnlyList<PageBlock> PreviewBlocks(IReadOnlyList<PageBlockInput> input)
    {
        var e = new FieldErrors();
        var result = blocks.Validate(input, e);
        e.ThrowIfAny();
        return result;
    }

    private void Apply(SitePage x, SitePageInput r)
    {
        var e = new FieldErrors();
        var slug = WebsiteRules.Slug(r.Slug, "slug", e);
        if (ReservedSlugs.Contains(slug)) e.Add("slug", "This address is used by a built-in page. Pick another slug.");
        if (r.Kind is not { } kind || !Enum.IsDefined(kind)) e.Add("kind", "Pick a page kind.");
        var validated = blocks.Validate(r.Blocks, e);
        if (r.IsPublished && validated.Count == 0) e.Add("blocks", "Add at least one block before publishing.");
        var seo = rules.Seo(r.Seo, e);
        e.ThrowIfAny();
        x.Slug = slug;
        x.Title = r.Title.Trim();
        x.Summary = WebsiteRules.Clean(r.Summary);
        x.Kind = r.Kind!.Value;
        x.BlocksJson = PageBlockValidator.Serialize(validated);
        x.Seo = seo;
        x.IsPublished = r.IsPublished;
        x.PublishAt = r.IsPublished ? WebsiteRules.Utc(r.PublishAt) : null;
        x.SortOrder = r.SortOrder;
    }

    /// <summary>Slugs of built-in routes a CMS page must not shadow.</summary>
    public static readonly IReadOnlySet<string> ReservedSlugs = new HashSet<string>(StringComparer.Ordinal)
    {
        "services", "industries", "case-studies", "team", "careers", "blog", "free-audit", "get-a-quote",
        "book-a-consultation", "newsletter", "login", "register", "app", "agency", "client", "admin", "finance", "review", "manage",
        "api", "faq", "creators", "join", "c", "t", "search",
    };

    public static SitePageDto ToDto(SitePage x) => new(
        x.Id, x.Slug, x.Title, x.Summary, x.Kind, PageBlockValidator.Parse(x.BlocksJson), SeoDto.From(x.Seo), x.IsPublished, x.SortOrder,
        x.UpdatedAt, x.ConcurrencyStamp, x.PublishAt, x.Version);

    // ---------------------------------------------------------------- Shared

    private async Task<List<Guid>> ServiceIdsAsync(List<Guid>? ids, string field, FieldErrors e, CancellationToken ct)
    {
        var list = (ids ?? new List<Guid>()).Distinct().ToList();
        if (list.Count > 20) e.Add(field, "Pick at most 20 services.");
        if (list.Count > 0 && await Db.Set<AgencyService>().CountAsync(s => list.Contains(s.Id), ct) != list.Count)
            e.Add(field, "Some services no longer exist.");
        return list;
    }
}
