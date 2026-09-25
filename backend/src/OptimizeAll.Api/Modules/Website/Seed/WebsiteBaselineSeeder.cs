using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Website.Leads;
using OptimizeAll.Api.Modules.Website.Pages;
using OptimizeAll.Api.Modules.Website.Settings;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Seed;

/// <summary>
/// Baseline website content every environment needs: the service catalog (9 service lines, 33 services, starting packages),
/// industries, core and legal CMS pages, blog categories, default site settings and consultation availability.
/// Idempotent and keyed by slug: existing rows are never overwritten, so CMS edits survive re-seeding.
/// </summary>
public sealed class WebsiteBaselineSeeder : ISeeder
{
    public string Profile => "Baseline";
    public int Order => 60;

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        // Seeded once per slug: rows an editor deleted, or whose slug they changed, are not created again on the next start.
        var ledger = await SeedLedger.LoadAsync(db, "website_baseline", ct);

        // ---- Service lines and services
        var categories = await db.Set<ServiceCategory>().ToDictionaryAsync(c => c.Slug, ct);
        var serviceSlugs = (await db.Set<AgencyService>().Select(s => s.Slug).ToListAsync(ct)).ToHashSet();
        var newServices = new List<(AgencyService Service, ServiceSeed Seed)>();
        for (var ci = 0; ci < BaselineServices.Categories.Length; ci++)
        {
            var cs = BaselineServices.Categories[ci];
            var categorySeeded = ledger.WasSeeded("category:" + cs.Slug);
            ledger.Record("category:" + cs.Slug);
            if (!categories.TryGetValue(cs.Slug, out var category))
            {
                if (categorySeeded)
                {
                    // The service line was removed (or renamed): its services are not brought back either.
                    foreach (var removed in cs.Services) ledger.Record("service:" + removed.Slug);
                    continue;
                }
                category = new ServiceCategory
                {
                    Slug = cs.Slug, Name = cs.Name, Icon = cs.Icon, Description = cs.Description, SortOrder = (ci + 1) * 10, IsPublished = true,
                };
                db.Add(category);
                categories[cs.Slug] = category;
            }
            for (var si = 0; si < cs.Services.Length; si++)
            {
                var s = cs.Services[si];
                var serviceSeeded = ledger.WasSeeded("service:" + s.Slug);
                ledger.Record("service:" + s.Slug);
                if (serviceSeeded || serviceSlugs.Contains(s.Slug)) continue;
                var service = new AgencyService
                {
                    CategoryId = category.Id,
                    Slug = s.Slug,
                    Name = s.Name,
                    Tagline = s.Tagline,
                    HeroTitle = s.Tagline,
                    HeroBody = s.HeroBody,
                    OverviewMarkdown = s.Overview,
                    ProblemsSolved = s.Problems.ToList(),
                    Deliverables = s.Deliverables.ToList(),
                    ProcessSteps = cs.Process.Select(p => new ProcessStep(p.Title, p.Text)).ToList(),
                    Tools = s.Tools.ToList(),
                    Kpis = s.Kpis.ToList(),
                    Faqs = s.Faqs.Select(f => new FaqEntry(f.Q, f.A)).ToList(),
                    Icon = s.Icon,
                    CtaLabel = s.CtaLabel,
                    CtaUrl = s.CtaUrl,
                    Seo = new SeoMeta { Title = $"{s.Name} services", Description = BaselineSeo.Description(s.HeroBody, s.Slug) },
                    IsPublished = true,
                    IsFeatured = s.Featured,
                    SortOrder = (si + 1) * 10,
                };
                for (var pi = 0; pi < s.Packages.Length; pi++)
                {
                    var p = s.Packages[pi];
                    service.Packages.Add(new ServicePackage
                    {
                        ServiceId = service.Id,
                        Name = p.Name,
                        Description = p.Description,
                        Price = p.Price,
                        Currency = "USD",
                        BillingPeriod = p.Period,
                        SetupFee = p.SetupFee,
                        Features = p.Features.ToList(),
                        IsMostPopular = p.Popular,
                        IsCustomQuote = p.Price is null,
                        IsActive = true,
                        SortOrder = (pi + 1) * 10,
                    });
                }
                db.Add(service);
                newServices.Add((service, s));
                serviceSlugs.Add(s.Slug);
            }
        }
        await db.SaveChangesAsync(ct);

        // Related services resolve by slug once every service exists.
        if (newServices.Count > 0)
        {
            var ids = await db.Set<AgencyService>().ToDictionaryAsync(s => s.Slug, s => s.Id, ct);
            foreach (var (service, seed) in newServices)
                service.RelatedServiceIds = (seed.Related ?? Array.Empty<string>()).Where(ids.ContainsKey).Select(slug => ids[slug]).ToList();
            await db.SaveChangesAsync(ct);
        }

        // ---- Industries
        var industrySlugs = (await db.Set<Industry>().Select(i => i.Slug).ToListAsync(ct)).ToHashSet();
        var serviceIds = await db.Set<AgencyService>().ToDictionaryAsync(s => s.Slug, s => s.Id, ct);
        for (var i = 0; i < BaselinePages.Industries.Length; i++)
        {
            var ind = BaselinePages.Industries[i];
            var industrySeeded = ledger.WasSeeded("industry:" + ind.Slug);
            ledger.Record("industry:" + ind.Slug);
            if (industrySeeded || industrySlugs.Contains(ind.Slug)) continue;
            db.Add(new Industry
            {
                Slug = ind.Slug, Name = ind.Name, Icon = ind.Icon, Summary = ind.Summary, BodyMarkdown = ind.Body, Challenges = ind.Challenges.ToList(),
                ServiceIds = ind.Services.Where(serviceIds.ContainsKey).Select(s => serviceIds[s]).ToList(),
                Seo = new SeoMeta { Title = $"Digital marketing for {ind.Name}", Description = ind.Summary },
                IsPublished = true, SortOrder = (i + 1) * 10,
            });
        }

        // ---- Pages
        var pageSlugs = (await db.Set<SitePage>().Select(p => p.Slug).ToListAsync(ct)).ToHashSet();
        foreach (var page in BaselinePages.Pages)
        {
            var pageSeeded = ledger.WasSeeded("page:" + page.Slug);
            ledger.Record("page:" + page.Slug);
            if (pageSeeded || pageSlugs.Contains(page.Slug)) continue;
            db.Add(new SitePage
            {
                Slug = page.Slug, Title = page.Title, Summary = page.Summary, Kind = page.Kind, BlocksJson = PageBlockValidator.Serialize(page.Blocks),
                Seo = BaselineSeo.ForPage(page.Slug, page.Summary), IsPublished = true, SortOrder = page.Sort,
            });
        }

        // ---- Blog categories
        var blogSlugs = (await db.Set<BlogCategory>().Select(c => c.Slug).ToListAsync(ct)).ToHashSet();
        for (var i = 0; i < BaselinePages.BlogCategories.Length; i++)
        {
            var c = BaselinePages.BlogCategories[i];
            var blogSeeded = ledger.WasSeeded("blog-category:" + c.Slug);
            ledger.Record("blog-category:" + c.Slug);
            if (blogSeeded || blogSlugs.Contains(c.Slug)) continue;
            db.Add(new BlogCategory { Slug = c.Slug, Name = c.Name, Description = c.Description, SortOrder = (i + 1) * 10 });
        }

        // ---- Settings and consultation availability
        if (!await db.Set<SiteSettingsDocument>().AnyAsync(d => d.Key == SiteSettingsDocument.DefaultKey, ct))
            db.Add(new SiteSettingsDocument { Json = JsonSerializer.Serialize(SiteSettingsService.Defaults, SiteSettingsService.Json) });
        if (!await db.Set<ConsultationSettings>().AnyAsync(s => s.Key == ConsultationSettings.DefaultKey, ct))
            db.Add(BookingService.Defaults());

        await db.SaveChangesAsync(ct);
    }
}
