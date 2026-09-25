using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.LandingPages;
using OptimizeAll.Api.Modules.Website.Blog;
using OptimizeAll.Api.Modules.Website.Pages;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Api.Modules.Website.Settings;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Website;
using ContentFaq = OptimizeAll.Domain.Content.FaqItem;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

public sealed partial class SeoPageResolver
{
    // ---------------------------------------------------------------- Home

    private async Task<SeoPage> HomeAsync(CancellationToken ct)
    {
        var home = await site.HomeAsync(ct);
        var page = NewPage("/", _settings.Seo.DefaultTitle, home.Seo.Description ?? _copy.Text("home.seo.description"), fullTitle: true);
        page.Source = "Site settings → SEO";
        page.EditPath = "/agency/website/settings";
        page.JsonLd.AddRange(home.JsonLd);
        page.JsonLd.Add(_seoLd.WebPage("WebPage", _settings.Seo.DefaultTitle, page.Description, "/", _settingsUpdatedAt));

        var c = page.Content;
        c.Add(new ParagraphNode(_copy.Text("home.hero.eyebrow")));
        c.Add(new HeadingNode(1, $"{_copy.Text("home.hero.title")} {_copy.Text("home.hero.titleHighlight")}".Trim()));
        c.Add(new ParagraphNode(_copy.Text("home.hero.lead")));
        c.Add(new ListNode(_copy.List("home.hero.proof")));
        c.Add(new LinkListNode(new[]
        {
            new LinkItem(_copy.Text("home.hero.primaryCta"), "/free-audit"), new LinkItem(_copy.Text("home.hero.secondaryCta"), "/book-a-consultation"),
        }));
        c.Add(new HeadingNode(2, _copy.Text("home.audit.title")));
        c.Add(new ListNode(_copy.List("home.audit.items")));

        c.Add(new HeadingNode(2, _copy.Text("home.services.title")));
        c.Add(new ParagraphNode(_copy.Text("home.services.intro")));
        foreach (var group in home.ServiceCategories)
        {
            c.Add(new HeadingNode(3, group.Name));
            c.Add(new LinkListNode(group.Services.Select(s => new LinkItem(s.Name, $"/services/{s.Slug}", s.Tagline)).ToList()));
        }
        c.Add(new ActionNode(_copy.Text("home.services.cta"), "/services"));

        if (home.Stats.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("home.results.title")));
            c.Add(new ParagraphNode(_copy.Text("home.results.intro")));
            c.Add(new FactsNode(home.Stats.Select(s => KeyValuePair.Create(s.Label, $"{s.Value} ({s.Measurement.ToString().ToLowerInvariant()})")).ToList()));
        }
        if (home.FeaturedCaseStudies.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("home.caseStudies.title")));
            c.Add(new LinkListNode(home.FeaturedCaseStudies.Select(cs => new LinkItem(cs.Title, $"/case-studies/{cs.Slug}", cs.Summary)).ToList()));
            c.Add(new ActionNode(_copy.Text("home.caseStudies.cta"), "/case-studies"));
        }
        c.Add(new HeadingNode(2, _copy.Text("home.process.title")));
        foreach (var (title, text) in _copy.Pairs("home.process.steps"))
        {
            c.Add(new HeadingNode(3, title));
            c.Add(new ParagraphNode(text));
        }
        c.Add(new ActionNode(_copy.Text("home.process.cta"), "/how-we-work"));
        if (home.Industries.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("home.industries.title")));
            c.Add(new LinkListNode(home.Industries.Select(i => new LinkItem(i.Name, $"/industries/{i.Slug}", i.Summary)).ToList()));
        }
        if (home.Testimonials.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("home.testimonials.title")));
            foreach (var t in home.Testimonials.Take(4))
                c.Add(new QuoteNode(t.Quote, string.Join(", ", new[] { t.AuthorName, t.AuthorRole, t.Company }.Where(x => !string.IsNullOrWhiteSpace(x)))));
        }
        if (home.PricingTeaser.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("home.pricing.title")));
            c.Add(new ParagraphNode(_copy.Text("home.pricing.intro")));
            c.Add(new FactsNode(home.PricingTeaser.Select(p => KeyValuePair.Create($"{p.ServiceName} — {p.Package.Name}", PackagePrice(p.Package))).ToList()));
            c.Add(new ActionNode(_copy.Text("home.pricing.cta"), "/pricing"));
        }
        if (home.LatestPosts.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("home.blog.title")));
            c.Add(new LinkListNode(home.LatestPosts.Select(p => new LinkItem(p.Title, $"/blog/{p.Slug}", p.Excerpt)).ToList()));
        }
        c.Add(new HeadingNode(2, _copy.Text("home.creators.title")));
        c.Add(new ParagraphNode(_copy.Text("home.creators.intro")));
        c.Add(new ActionNode(_copy.Text("home.creators.secondaryCta"), "/creators"));
        page.ModifiedAt = Latest(_settingsUpdatedAt, _copyUpdatedAt, home.LatestPosts.Select(p => p.PublishedAt).Max());
        return AddCatalogVideos(page);
    }

    // ---------------------------------------------------------------- Built-in listing pages

    /// <summary>Title, description, breadcrumb, h1 and lead of a built-in page from its editable page texts.</summary>
    private SeoPage CopyPage(string path, string webPageType = "CollectionPage")
    {
        var prefix = CopyPages[path];
        var page = NewPage(path, _copy.Text($"{prefix}.seo.title"), _copy.Text($"{prefix}.seo.description"));
        page.Source = "Page texts";
        page.EditPath = "/agency/website/copy";
        var crumb = _copy.Text($"{prefix}.hero.eyebrow") is { Length: > 0 } eyebrow ? eyebrow : _copy.Text($"{prefix}.seo.title");
        CrumbsLd(page, (crumb, path));
        page.JsonLd.Add(_seoLd.WebPage(webPageType, _copy.Text($"{prefix}.seo.title"), page.Description, path));
        page.Content.Add(new HeadingNode(1, _copy.Text($"{prefix}.hero.title")));
        if (_copy.Text($"{prefix}.hero.lead") is { Length: > 0 } lead) page.Content.Add(new ParagraphNode(lead));
        page.ModifiedAt = _copyUpdatedAt;
        return page;
    }

    private async Task<SeoPage> ServicesAsync(CancellationToken ct)
    {
        var page = CopyPage("/services");
        var groups = await site.ServicesAsync(ct);
        foreach (var g in groups)
        {
            page.Content.Add(new HeadingNode(2, g.Name));
            if (!string.IsNullOrWhiteSpace(g.Description)) page.Content.Add(new ParagraphNode(g.Description));
            page.Content.Add(new LinkListNode(g.Services.Select(s => new LinkItem(s.Name, $"/services/{s.Slug}",
                s.StartingPrice is { } p ? $"{s.Tagline} From {Money(p.Amount, p.Currency)} {Period(p.BillingPeriod)}." : s.Tagline)).ToList()));
        }
        page.Content.Add(new HeadingNode(2, _copy.Text("services.cta.title")));
        page.Content.Add(new ParagraphNode(_copy.Text("services.cta.text")));
        page.Content.Add(new ActionNode("Get a free audit", "/free-audit"));
        if (_seoLd.ItemList("Services", groups.SelectMany(g => g.Services).Select(s => (s.Name, $"/services/{s.Slug}")).ToList()) is { } list)
            page.JsonLd.Add(list);
        return AddCatalogVideos(page);
    }

    private async Task<SeoPage> PricingAsync(CancellationToken ct)
    {
        var page = CopyPage("/pricing", "WebPage");
        var pricing = await site.PricingAsync(ct);
        foreach (var s in pricing.Services)
        {
            page.Content.Add(new HeadingNode(2, s.Service.Name));
            page.Content.Add(new ParagraphNode(s.Service.Tagline));
            page.Content.Add(new FactsNode(s.Packages.Select(p => KeyValuePair.Create(p.Name, PackagePrice(p))).ToList()));
            page.Content.Add(new ActionNode($"{s.Service.Name}: what's included", $"/services/{s.Service.Slug}#pricing"));
        }
        if (await CmsBlocksAsync("pricing", ct) is { } extra) page.Content.AddRange(extra);
        if (_seoLd.OfferCatalog(pricing.Services) is { } catalog) page.JsonLd.Add(catalog);
        return AddCatalogVideos(page);
    }

    private async Task<SeoPage> IndustriesAsync(CancellationToken ct)
    {
        var page = CopyPage("/industries");
        var items = await site.IndustriesAsync(ct);
        page.Content.Add(new LinkListNode(items.Select(i => new LinkItem(i.Name, $"/industries/{i.Slug}", i.Summary)).ToList()));
        if (_seoLd.ItemList("Industries", items.Select(i => (i.Name, $"/industries/{i.Slug}")).ToList()) is { } list) page.JsonLd.Add(list);
        return AddCatalogVideos(page);
    }

    private async Task<SeoPage> CaseStudiesAsync(IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var page = CopyPage("/case-studies");
        query.TryGetValue("service", out var service);
        query.TryGetValue("industry", out var industry);
        var items = await site.CaseStudiesAsync(service, industry, ct);
        // Filtered views are variations of the one list: canonical to it, crawlable but not indexed separately.
        if (service is not null || industry is not null)
        {
            page.NoIndex = true;
            page.Canonical = _ld.Url("/case-studies");
        }
        page.Content.Add(new LinkListNode(items.Select(c => new LinkItem(c.Title, $"/case-studies/{c.Slug}", $"{c.ClientName}: {c.Summary}")).ToList()));
        if (_seoLd.ItemList("Case studies", items.Select(c => (c.Title, $"/case-studies/{c.Slug}")).ToList()) is { } list) page.JsonLd.Add(list);
        return AddCatalogVideos(page);
    }

    private async Task<SeoPage> BlogAsync(IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var page = CopyPage("/blog", "Blog");
        var pageNumber = query.TryGetValue("page", out var p) && int.TryParse(p, out var n) && n is > 1 and <= 1000 ? n : 1;
        query.TryGetValue("category", out var category);
        query.TryGetValue("tag", out var tag);
        query.TryGetValue("q", out var search);
        var index = await PublicBlogQueries.IndexAsync(db, Now, new PublicBlogQuery { Page = pageNumber, Category = category, Tag = tag, Search = search }, ct);
        var pages = Math.Max(1, (int)Math.Ceiling(index.Total / (double)index.PageSize));
        if (pageNumber > pages) return NotFound("/blog");

        // Paginated archive: each page is its own canonical URL with prev/next links; filters and searches are noindex.
        string Url(int number) => number <= 1 ? "/blog" : $"/blog?page={number}";
        if (category is not null)
        {
            var cat = index.Categories.FirstOrDefault(c => c.Slug == category);
            if (cat is null) return NotFound("/blog");
            page.Title = SeoText.ApplyTemplate($"{cat.Name} articles", _settings.Seo.TitleTemplate, _settings.SiteName);
            page.Description = SeoText.Clamp(cat.Description ?? page.Description);
            page.Canonical = _ld.Url($"/blog?category={Uri.EscapeDataString(category)}" + (pageNumber > 1 ? $"&page={pageNumber}" : string.Empty));
            page.Content[0] = new HeadingNode(1, $"{cat.Name} articles");
        }
        else if (tag is not null || search is not null)
        {
            page.NoIndex = true;
            page.Canonical = _ld.Url("/blog");
        }
        else
        {
            page.Canonical = _ld.Url(Url(pageNumber));
            if (pageNumber > 1) page.Title = SeoText.ApplyTemplate($"{_copy.Text("blog.seo.title")} — page {pageNumber}", _settings.Seo.TitleTemplate, _settings.SiteName);
            if (pageNumber > 1) page.PrevUrl = Url(pageNumber - 1);
            if (pageNumber < pages) page.NextUrl = Url(pageNumber + 1);
        }
        foreach (var post in index.Items)
        {
            page.Content.Add(new HeadingNode(2, post.Title));
            page.Content.Add(new ParagraphNode(post.Excerpt));
            page.Content.Add(new ActionNode($"Read “{post.Title}”", $"/blog/{post.Slug}"));
        }
        if (index.Categories.Count > 0)
        {
            page.Content.Add(new HeadingNode(2, "Topics"));
            page.Content.Add(new LinkListNode(index.Categories.Select(c => new LinkItem(c.Name, $"/blog?category={c.Slug}")).ToList()));
        }
        if (_seoLd.ItemList("Articles", index.Items.Select(i => (i.Title, $"/blog/{i.Slug}")).ToList()) is { } list) page.JsonLd.Add(list);
        page.ModifiedAt = Latest(_copyUpdatedAt, index.Items.Select(i => i.PublishedAt).Max());
        return page;
    }

    private async Task<SeoPage> TeamAsync(CancellationToken ct)
    {
        var page = CopyPage("/team", "AboutPage");
        var team = await site.TeamAsync(ct);
        foreach (var m in team)
        {
            page.Content.Add(new HeadingNode(2, m.Name));
            page.Content.Add(new ParagraphNode(m.Role));
            if (!string.IsNullOrWhiteSpace(m.Bio)) page.Content.Add(new MarkdownNode(m.Bio, 3));
        }
        page.Content.Add(new HeadingNode(2, _copy.Text("team.join.title")));
        page.Content.Add(new ParagraphNode(_copy.Text("team.join.text")));
        page.Content.Add(new ActionNode(_copy.Text("team.join.link"), "/careers"));
        if (_seoLd.People(team) is { } people) page.JsonLd.Add(people);
        return AddCatalogVideos(page);
    }

    private async Task<SeoPage> CareersAsync(CancellationToken ct)
    {
        var page = CopyPage("/careers");
        var jobs = await careers.PublicJobsAsync(ct);
        page.Content.Add(new HeadingNode(2, _copy.Text("careers.openRoles")));
        if (jobs.Count == 0)
        {
            page.Content.Add(new ParagraphNode(_copy.Text("careers.empty.title")));
            page.Content.Add(new ParagraphNode(_copy.Text("careers.empty.description")));
        }
        else
            page.Content.Add(new LinkListNode(jobs.Select(j => new LinkItem(j.Title, $"/careers/{j.Slug}", $"{j.Department} · {j.Location} · {j.Summary}")).ToList()));
        page.Content.Add(new HeadingNode(2, _copy.Text("careers.cta.title")));
        page.Content.Add(new ParagraphNode(_copy.Text("careers.cta.text")));
        page.Content.Add(new ActionNode("Contact us", "/contact"));
        if (_seoLd.ItemList("Open roles", jobs.Select(j => (j.Title, $"/careers/{j.Slug}")).ToList()) is { } list) page.JsonLd.Add(list);
        page.ModifiedAt = Latest(_copyUpdatedAt, jobs.Select(j => j.PostedAt).Max());
        return AddCatalogVideos(page);
    }

    private async Task<SeoPage> FormPageAsync(string path, CancellationToken ct)
    {
        var page = CopyPage(path, path == "/contact" ? "ContactPage" : "WebPage");
        var prefix = CopyPages[path];
        var c = page.Content;
        switch (prefix)
        {
            case "audit":
                c.Add(new HeadingNode(2, _copy.Text("audit.included.title")));
                c.Add(new ListNode(_copy.List("audit.included.items")));
                break;
            case "quote":
                c.Add(new HeadingNode(2, _copy.Text("quote.next.title")));
                c.Add(new ListNode(_copy.List("quote.next.items"), Ordered: true));
                break;
            case "booking":
                c.Add(new HeadingNode(2, _copy.Text("booking.agenda.title")));
                c.Add(new ListNode(_copy.List("booking.agenda.items")));
                break;
        }
        if (path == "/contact" && await CmsBlocksAsync("contact", ct) is { } extra) c.AddRange(extra);
        var contact = _settings.Contact;
        var facts = new List<KeyValuePair<string, string>>();
        if (contact.Email is not null) facts.Add(KeyValuePair.Create("Email", contact.Email));
        if (contact.Phone is not null) facts.Add(KeyValuePair.Create("Phone", contact.Phone));
        if (contact.WhatsApp is not null) facts.Add(KeyValuePair.Create("WhatsApp", contact.WhatsApp));
        if (contact.Address is not null) facts.Add(KeyValuePair.Create("Address", contact.Address));
        if (contact.Hours is not null) facts.Add(KeyValuePair.Create("Hours", contact.Hours));
        if (facts.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("contact.details.title")));
            c.Add(new FactsNode(facts));
        }
        c.Add(new ParagraphNode("The form on this page needs JavaScript. You can also email us directly."));
        if (path == "/contact" && _ld.LocalBusiness() is { } business) page.JsonLd.Add(business);
        page.ModifiedAt = Latest(_copyUpdatedAt, _settingsUpdatedAt);
        return AddCatalogVideos(page);
    }

    private SeoPage CreatorsPage()
    {
        var page = NewPage("/creators", _copy.Text("creators.seo.title"), _copy.Text("creators.seo.description"), fullTitle: true);
        page.Source = "Page texts";
        page.EditPath = "/agency/website/copy";
        CrumbsLd(page, ("Creators", "/creators"));
        var c = page.Content;
        c.Add(new ParagraphNode(_copy.Text("creators.hero.eyebrow")));
        c.Add(new HeadingNode(1, $"{_copy.Text("creators.hero.title")} {_copy.Text("creators.hero.titleAccent")}".Trim()));
        c.Add(new ParagraphNode(_copy.Text("creators.hero.lead")));
        c.Add(new ListNode(_copy.List("creators.hero.trust")));
        c.Add(new ActionNode(_copy.Text("creators.hero.primaryCta"), "/register"));
        c.Add(new HeadingNode(2, _copy.Text("creators.how.title")));
        foreach (var (title, text) in _copy.Pairs("creators.how.steps"))
        {
            c.Add(new HeadingNode(3, title));
            c.Add(new ParagraphNode(text));
        }
        c.Add(new HeadingNode(2, _copy.Text("creators.rules.title")));
        c.Add(new ParagraphNode(_copy.Text("creators.rules.lead")));
        foreach (var (title, text) in _copy.Pairs("creators.rules.items"))
        {
            c.Add(new HeadingNode(3, title));
            c.Add(new ParagraphNode(text));
        }
        c.Add(new HeadingNode(2, _copy.Text("creators.faq.title")));
        var faqs = _copy.Pairs("creators.faq.items").Select(p => new FaqEntry(p.Title, p.Text)).ToList();
        foreach (var f in faqs) c.Add(new QuestionNode(f.Question, f.Answer));
        c.Add(new ActionNode(_copy.Text("creators.faq.cta"), "/faq"));
        c.Add(new HeadingNode(2, _copy.Text("creators.cta.title")));
        c.Add(new ParagraphNode(_copy.Text("creators.cta.text")));
        if (_ld.FaqPage(faqs) is { } faqLd) page.JsonLd.Add(faqLd);
        page.ModifiedAt = _copyUpdatedAt;
        return AddCatalogVideos(page);
    }

    private async Task<SeoPage> FaqAsync(CancellationToken ct)
    {
        var page = NewPage("/faq", _copy.Text("faq.seo.title"), _copy.Text("faq.seo.description"), fullTitle: true);
        page.Source = "Page texts (help centre) + Admin → Content → FAQ";
        page.EditPath = "/admin/content";
        CrumbsLd(page, ("FAQ", "/faq"));
        page.Content.Add(new HeadingNode(1, _copy.Text("faq.hero.title")));
        page.Content.Add(new ParagraphNode(_copy.Text("faq.hero.lead")));
        var items = await db.Set<ContentFaq>().AsNoTracking().Where(f => f.IsPublished).OrderBy(f => f.SortOrder).ThenBy(f => f.CreatedAt).ToListAsync(ct);
        foreach (var group in items.GroupBy(f => f.Category))
        {
            page.Content.Add(new HeadingNode(2, group.Key));
            foreach (var f in group) page.Content.Add(new QuestionNode(f.Question, f.Answer));
        }
        if (_ld.FaqPage(items.Select(f => new FaqEntry(f.Question, f.Answer)).ToList()) is { } faqLd) page.JsonLd.Add(faqLd);
        page.ModifiedAt = Latest(_copyUpdatedAt, items.Select(f => (DateTime?)f.UpdatedAt).Max());
        return page;
    }

    // ---------------------------------------------------------------- Detail pages

    private async Task<SeoPage> ServiceAsync(string slug, CancellationToken ct)
    {
        var s = await site.ServiceAsync(slug, ct);
        var page = NewPage($"/services/{slug}", s.Name, s.Tagline);
        page.Source = "Service";
        page.EditPath = "/agency/website/services";
        ApplySeo(page, s.Seo, s.JsonLd, s.Name);
        Crumbs(page, ("Services", "/services"), (s.Name, $"/services/{s.Slug}"));
        var c = page.Content;
        c.Add(new ParagraphNode(s.CategoryName));
        c.Add(new HeadingNode(1, s.HeroTitle ?? s.Name));
        c.Add(new ParagraphNode(s.HeroBody ?? s.Tagline));
        if (s.HeroImageUrl is not null) c.Add(new ImageNode(s.HeroImageUrl, string.Empty, 640, 480, Priority: true));
        if (s.Kpis.Count > 0) { c.Add(new HeadingNode(2, _copy.Text("services.detail.kpisTitle"))); c.Add(new ListNode(s.Kpis)); }
        if (!string.IsNullOrWhiteSpace(s.OverviewMarkdown)) c.Add(new MarkdownNode(s.OverviewMarkdown));
        if (s.ProblemsSolved.Count > 0) { c.Add(new HeadingNode(2, _copy.Text("services.detail.problemsTitle"))); c.Add(new ListNode(s.ProblemsSolved)); }
        if (s.Deliverables.Count > 0) { c.Add(new HeadingNode(2, _copy.Text("services.detail.includedTitle"))); c.Add(new ListNode(s.Deliverables)); }
        if (s.ProcessSteps.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("services.detail.processTitle")));
            foreach (var step in s.ProcessSteps) { c.Add(new HeadingNode(3, step.Title)); c.Add(new ParagraphNode(step.Description)); }
        }
        if (s.Tools.Count > 0) { c.Add(new HeadingNode(2, _copy.Text("services.detail.toolsTitle"))); c.Add(new ListNode(s.Tools)); }
        if (s.Packages.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("services.detail.pricingTitle")));
            foreach (var p in s.Packages)
            {
                c.Add(new HeadingNode(3, $"{p.Name}: {PackagePrice(p)}"));
                if (!string.IsNullOrWhiteSpace(p.Description)) c.Add(new ParagraphNode(p.Description));
                if (p.Features.Count > 0) c.Add(new ListNode(p.Features));
            }
            c.Add(new ParagraphNode(_copy.Text("services.detail.pricingIntro")));
        }
        if (s.CaseStudies.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("services.detail.caseStudiesTitle")));
            c.Add(new LinkListNode(s.CaseStudies.Select(cs => new LinkItem(cs.Title, $"/case-studies/{cs.Slug}", cs.Summary)).ToList()));
        }
        if (s.Testimonials.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("services.detail.testimonialsTitle")));
            foreach (var t in s.Testimonials) c.Add(new QuoteNode(t.Quote, string.Join(", ", new[] { t.AuthorName, t.Company }.Where(x => !string.IsNullOrWhiteSpace(x)))));
        }
        if (s.Faqs.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("services.detail.faqTitle")));
            foreach (var f in s.Faqs) c.Add(new QuestionNode(f.Question, f.Answer));
        }
        if (s.RelatedServices.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("services.detail.relatedTitle")));
            c.Add(new LinkListNode(s.RelatedServices.Select(r => new LinkItem(r.Name, $"/services/{r.Slug}", r.Tagline)).ToList()));
        }
        c.Add(new HeadingNode(2, _copy.Text("services.detail.ctaTitle", ("name", s.Name))));
        c.Add(new LinkListNode(new[]
        {
            new LinkItem(_copy.Text("services.detail.quoteCta"), $"/get-a-quote?service={s.Slug}"),
            new LinkItem(_copy.Text("services.detail.callCta"), "/book-a-consultation"),
        }));
        page.ModifiedAt = await db.Set<AgencyService>().AsNoTracking().Where(x => x.Id == s.Id).Select(x => (DateTime?)x.UpdatedAt).FirstOrDefaultAsync(ct);
        return AddCatalogVideos(page);
    }

    private async Task<SeoPage> IndustryAsync(string slug, CancellationToken ct)
    {
        var i = await site.IndustryAsync(slug, ct);
        var page = NewPage($"/industries/{slug}", i.Name, i.Summary);
        page.Source = "Industry";
        page.EditPath = "/agency/website/industries";
        ApplySeo(page, i.Seo, i.JsonLd, i.Name);
        Crumbs(page, ("Industries", "/industries"), (i.Name, $"/industries/{i.Slug}"));
        var c = page.Content;
        c.Add(new HeadingNode(1, _copy.Text("industries.detail.title", ("name", i.Name))));
        c.Add(new ParagraphNode(i.Summary));
        if (i.HeroImageUrl is not null) c.Add(new ImageNode(i.HeroImageUrl, string.Empty, 640, 480, Priority: true));
        if (!string.IsNullOrWhiteSpace(i.BodyMarkdown)) c.Add(new MarkdownNode(i.BodyMarkdown));
        if (i.Challenges.Count > 0) { c.Add(new HeadingNode(2, _copy.Text("industries.detail.challengesTitle"))); c.Add(new ListNode(i.Challenges)); }
        if (i.Services.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("industries.detail.servicesTitle")));
            c.Add(new LinkListNode(i.Services.Select(s => new LinkItem(s.Name, $"/services/{s.Slug}", s.Tagline)).ToList()));
        }
        if (i.CaseStudies.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("industries.detail.caseStudiesTitle", ("name", i.Name))));
            c.Add(new LinkListNode(i.CaseStudies.Select(cs => new LinkItem(cs.Title, $"/case-studies/{cs.Slug}", cs.Summary)).ToList()));
        }
        c.Add(new HeadingNode(2, _copy.Text("industries.detail.ctaTitle", ("name", i.Name))));
        c.Add(new ActionNode("Get a free audit", "/free-audit"));
        page.ModifiedAt = await db.Set<Industry>().AsNoTracking().Where(x => x.Slug == slug).Select(x => (DateTime?)x.UpdatedAt).FirstOrDefaultAsync(ct);
        return AddCatalogVideos(page);
    }

    private async Task<SeoPage> CaseStudyAsync(string slug, CancellationToken ct)
    {
        var cs = await site.CaseStudyAsync(slug, ct);
        var page = NewPage($"/case-studies/{slug}", cs.Title, cs.Summary);
        page.Source = "Case study";
        page.EditPath = "/agency/website/case-studies";
        page.OgType = "article";
        ApplySeo(page, cs.Seo, cs.JsonLd, cs.Title);
        Crumbs(page, ("Case studies", "/case-studies"), (cs.Title, $"/case-studies/{cs.Slug}"));
        page.PublishedAt = cs.PublishedAt;
        page.Section = "Case studies";
        var c = page.Content;
        c.Add(new ParagraphNode(string.Join(" · ", new[] { cs.ClientName, cs.IndustryName }.Where(x => !string.IsNullOrWhiteSpace(x)))));
        c.Add(new HeadingNode(1, cs.Title));
        c.Add(new ParagraphNode(cs.Summary));
        if (cs.CoverImageUrl is not null) c.Add(new ImageNode(cs.CoverImageUrl, cs.Title, 1200, 675, Priority: true));
        if (cs.Metrics.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("caseStudies.detail.resultsTitle")));
            c.Add(new FactsNode(cs.Metrics.Select(m => KeyValuePair.Create(m.Label, $"{m.Value} ({m.Measurement.ToString().ToLowerInvariant()})")).ToList()));
        }
        void Part(string key, string? md)
        {
            if (string.IsNullOrWhiteSpace(md)) return;
            c.Add(new HeadingNode(2, _copy.Text(key)));
            c.Add(new MarkdownNode(md, 3));
        }
        Part("caseStudies.detail.challengeTitle", cs.ChallengeMarkdown);
        Part("caseStudies.detail.strategyTitle", cs.StrategyMarkdown);
        Part("caseStudies.detail.executionTitle", cs.ExecutionMarkdown);
        if (cs.TestimonialQuote is not null) c.Add(new QuoteNode(cs.TestimonialQuote, string.Join(", ", new[] { cs.TestimonialAuthor, cs.TestimonialRole }.Where(x => x is not null))));
        foreach (var img in cs.GalleryImageUrls) page.Images.Add(new SeoImage(_ld.Url(img), cs.Title));
        if (cs.Services.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("caseStudies.detail.servicesTitle")));
            c.Add(new LinkListNode(cs.Services.Select(s => new LinkItem(s.Name, $"/services/{s.Slug}")).ToList()));
        }
        if (cs.Related.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("caseStudies.detail.moreTitle")));
            c.Add(new LinkListNode(cs.Related.Select(r => new LinkItem(r.Title, $"/case-studies/{r.Slug}")).ToList()));
        }
        page.ModifiedAt = await db.Set<CaseStudy>().AsNoTracking().Where(x => x.Slug == slug).Select(x => (DateTime?)x.UpdatedAt).FirstOrDefaultAsync(ct);
        return AddCatalogVideos(page);
    }

    private async Task<SeoPage> PostAsync(string slug, CancellationToken ct)
    {
        var post = await PublicBlogQueries.PostAsync(db, site, Now, slug, ct);
        var page = NewPage($"/blog/{slug}", post.Title, post.Excerpt);
        page.Source = "Blog post";
        page.EditPath = "/agency/website/blog";
        page.OgType = "article";
        ApplySeo(page, post.Seo, post.JsonLd, post.CoverImageAlt ?? post.Title);
        Crumbs(page, ("Blog", "/blog"), (post.Title, $"/blog/{post.Slug}"));
        page.PublishedAt = post.PublishedAt;
        page.ModifiedAt = post.UpdatedAt;
        page.Author = post.Author?.Name;
        page.Section = post.Categories.FirstOrDefault()?.Name;
        var c = page.Content;
        c.Add(new HeadingNode(1, post.Title));
        c.Add(new ParagraphNode(string.Join(" · ", new[]
        {
            post.Author?.Name, post.PublishedAt?.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture), $"{post.ReadingMinutes} min read",
        }.Where(x => x is not null))));
        if (post.CoverImageUrl is not null) c.Add(new ImageNode(post.CoverImageUrl, post.CoverImageAlt ?? string.Empty, 1200, 630, Priority: true));
        c.Add(new MarkdownNode(post.BodyMarkdown));
        if (post.Categories.Count > 0)
            c.Add(new LinkListNode(post.Categories.Select(cat => new LinkItem(cat.Name, $"/blog?category={cat.Slug}")).ToList()));
        if (post.Author is { } a && !string.IsNullOrWhiteSpace(a.Bio))
        {
            c.Add(new HeadingNode(2, $"About {a.Name}"));
            c.Add(new MarkdownNode(a.Bio, 3));
        }
        if (post.Related.Count > 0)
        {
            c.Add(new HeadingNode(2, _copy.Text("blog.detail.relatedTitle")));
            c.Add(new LinkListNode(post.Related.Select(r => new LinkItem(r.Title, $"/blog/{r.Slug}", r.Excerpt)).ToList()));
        }
        return AddCatalogVideos(page);
    }

    private async Task<SeoPage> JobAsync(string slug, CancellationToken ct)
    {
        Public.PublicJobDto job;
        try
        {
            job = await careers.PublicJobAsync(slug, ct);
        }
        catch (Domain.Common.DomainException ex) when (ex.Kind == Domain.Common.DomainErrorKind.NotFound)
        {
            // A role that existed but closed is gone for good: 410 tells crawlers to drop it faster than 404.
            return await db.Set<JobOpening>().AsNoTracking().AnyAsync(j => j.Slug == slug, ct) ? Gone($"/careers/{slug}") : NotFound($"/careers/{slug}");
        }
        var page = NewPage($"/careers/{slug}", job.Title, job.Summary);
        page.Source = "Job opening";
        page.EditPath = "/agency/website/careers";
        ApplySeo(page, job.Seo, job.JsonLd);
        Crumbs(page, ("Careers", "/careers"), (job.Title, $"/careers/{job.Slug}"));
        var c = page.Content;
        c.Add(new ParagraphNode(job.Department));
        c.Add(new HeadingNode(1, job.Title));
        c.Add(new ParagraphNode(job.Summary));
        var facts = new List<KeyValuePair<string, string>>
        {
            KeyValuePair.Create("Location", job.Location), KeyValuePair.Create("Workplace", job.Workplace.ToString()),
            KeyValuePair.Create("Employment", job.EmploymentType.ToString()),
        };
        if (job.Salary is { } sal && (sal.Min is not null || sal.Max is not null))
            facts.Add(KeyValuePair.Create("Salary", $"{sal.Currency} {sal.Min:#,0}–{sal.Max:#,0} per {sal.Period.ToString().ToLowerInvariant()}".Replace("– ", " ")));
        if (job.ClosesAt is { } closes) facts.Add(KeyValuePair.Create("Apply by", closes.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)));
        c.Add(new FactsNode(facts));
        c.Add(new MarkdownNode(job.DescriptionMarkdown));
        if (job.Requirements.Count > 0) { c.Add(new HeadingNode(2, _copy.Text("careers.detail.requirementsTitle"))); c.Add(new ListNode(job.Requirements)); }
        if (job.Benefits.Count > 0) { c.Add(new HeadingNode(2, _copy.Text("careers.detail.benefitsTitle"))); c.Add(new ListNode(job.Benefits)); }
        c.Add(new HeadingNode(2, _copy.Text("careers.detail.applyTitle")));
        c.Add(new ParagraphNode("The application form needs JavaScript."));
        page.PublishedAt = job.PostedAt;
        page.ModifiedAt = await db.Set<JobOpening>().AsNoTracking().Where(x => x.Slug == slug).Select(x => (DateTime?)x.UpdatedAt).FirstOrDefaultAsync(ct);
        return page;
    }

    // ---------------------------------------------------------------- CMS pages

    private async Task<SeoPage?> CmsPageAsync(string slug, CancellationToken ct)
    {
        if (!Accounts.FieldRules.IsSlug(slug)) return null;
        var p = await site.PageAsync(slug, ct);
        var page = NewPage($"/{slug}", p.Title, p.Summary);
        page.Source = "CMS page";
        page.EditPath = "/agency/website/pages";
        ApplySeo(page, p.Seo, p.JsonLd);
        Crumbs(page, (p.Title, $"/{p.Slug}"));
        page.JsonLd.Add(_seoLd.WebPage(slug is "about" or "how-we-work" ? "AboutPage" : "WebPage", p.Title, page.Description, $"/{p.Slug}", p.UpdatedAt));
        page.ModifiedAt = p.UpdatedAt;
        if (p.Blocks.FirstOrDefault()?.Type != PageBlockTypes.Hero)
        {
            page.Content.Add(new HeadingNode(1, p.Title));
            if (!string.IsNullOrWhiteSpace(p.Summary)) page.Content.Add(new ParagraphNode(p.Summary));
        }
        page.Content.AddRange(BlockNodes(page, p.Blocks, p.Title, p.Testimonials, p.CaseStudies, p.ServiceCategories, p.UpdatedAt));
        return AddCatalogVideos(page);
    }

    /// <summary>Extra CMS blocks embedded in a built-in page (/pricing and /contact show the CMS page of the same slug).</summary>
    private async Task<List<ContentNode>?> CmsBlocksAsync(string slug, CancellationToken ct)
    {
        try
        {
            var p = await site.PageAsync(slug, ct);
            var holder = new SeoPage { Path = "/" + slug };
            return BlockNodes(holder, p.Blocks.Where(b => b.Type != PageBlockTypes.Hero).ToList(), p.Title, p.Testimonials, p.CaseStudies, p.ServiceCategories, p.UpdatedAt);
        }
        catch (Domain.Common.DomainException ex) when (ex.Kind == Domain.Common.DomainErrorKind.NotFound)
        {
            return null;
        }
    }

    private List<ContentNode> BlockNodes(SeoPage page, IReadOnlyList<PageBlock> blocks, string pageTitle, IReadOnlyList<PublicTestimonialDto> testimonials,
        IReadOnlyList<CaseStudyCardDto> caseStudies, IReadOnlyList<ServiceCategoryGroupDto> groups, DateTime updatedAt)
    {
        var nodes = new List<ContentNode>();
        T? Read<T>(PageBlock b) where T : class
        {
            try { return b.Data.Deserialize<T>(SiteSettingsService.Json); }
            catch (JsonException) { return null; }
        }
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            switch (block.Type)
            {
                case PageBlockTypes.Hero when Read<HeroBlock>(block) is { } h:
                    if (h.Eyebrow is not null) nodes.Add(new ParagraphNode(h.Eyebrow));
                    nodes.Add(new HeadingNode(index == 0 ? 1 : 2, h.Title ?? pageTitle));
                    if (h.Subtitle is not null) nodes.Add(new ParagraphNode(h.Subtitle));
                    if (h.ImageUrl is not null) nodes.Add(new ImageNode(h.ImageUrl, string.Empty, 640, 480, Priority: index == 0));
                    var links = new[] { h.PrimaryCta, h.SecondaryCta }.OfType<SiteLink>().Select(l => new LinkItem(l.Label, l.Url)).ToList();
                    if (links.Count > 0) nodes.Add(new LinkListNode(links));
                    break;
                case PageBlockTypes.RichText when Read<RichTextBlock>(block) is { } r:
                    nodes.Add(new MarkdownNode(r.Markdown));
                    break;
                case PageBlockTypes.FeaturesGrid when Read<FeaturesGridBlock>(block) is { } f:
                    nodes.Add(new HeadingNode(2, f.Title ?? "Highlights"));
                    if (f.Intro is not null) nodes.Add(new ParagraphNode(f.Intro));
                    foreach (var item in f.Items) { nodes.Add(new HeadingNode(3, item.Title)); nodes.Add(new ParagraphNode(item.Text)); }
                    break;
                case PageBlockTypes.Stats when Read<StatsBlock>(block) is { } s:
                    nodes.Add(new HeadingNode(2, s.Title ?? "Results"));
                    nodes.Add(new FactsNode(s.Items.Select(i => KeyValuePair.Create(i.Label, $"{i.Value} ({i.Measurement.ToString().ToLowerInvariant()})")).ToList()));
                    break;
                case PageBlockTypes.Cta when Read<CtaBlock>(block) is { } cta:
                    nodes.Add(new HeadingNode(2, cta.Title));
                    if (cta.Text is not null) nodes.Add(new ParagraphNode(cta.Text));
                    nodes.Add(new LinkListNode(new[] { cta.Primary, cta.Secondary }.OfType<SiteLink>().Select(l => new LinkItem(l.Label, l.Url)).ToList()));
                    break;
                case PageBlockTypes.Faq when Read<FaqBlock>(block) is { } faq:
                    nodes.Add(new HeadingNode(2, faq.Title ?? "Frequently asked questions"));
                    foreach (var q in faq.Items) nodes.Add(new QuestionNode(q.Question, q.Answer));
                    break;
                case PageBlockTypes.Testimonials when Read<TestimonialsBlock>(block) is { } t:
                    var chosen = t.TestimonialIds.Count > 0 ? testimonials.Where(x => t.TestimonialIds.Contains(x.Id)).ToList() : testimonials.Take(8).ToList();
                    if (chosen.Count == 0) break;
                    nodes.Add(new HeadingNode(2, t.Title ?? "What clients say"));
                    foreach (var q in chosen) nodes.Add(new QuoteNode(q.Quote, string.Join(", ", new[] { q.AuthorName, q.Company }.Where(x => !string.IsNullOrWhiteSpace(x)))));
                    break;
                case PageBlockTypes.ServicesGrid when Read<ServicesGridBlock>(block) is { } sg:
                    var services = groups.Where(g => sg.CategorySlug is null || g.Slug == sg.CategorySlug).SelectMany(g => g.Services).ToList();
                    if (services.Count == 0) break;
                    nodes.Add(new HeadingNode(2, sg.Title ?? "Our services"));
                    if (sg.Intro is not null) nodes.Add(new ParagraphNode(sg.Intro));
                    nodes.Add(new LinkListNode(services.Select(x => new LinkItem(x.Name, $"/services/{x.Slug}", x.Tagline)).ToList()));
                    break;
                case PageBlockTypes.CaseStudyHighlight when Read<CaseStudyHighlightBlock>(block) is { } ch:
                    var study = caseStudies.FirstOrDefault(x => x.Slug == ch.CaseStudySlug);
                    if (study is null) break;
                    nodes.Add(new HeadingNode(2, ch.Title ?? "Featured case study"));
                    nodes.Add(new LinkListNode(new[] { new LinkItem(study.Title, $"/case-studies/{study.Slug}", study.Summary) }));
                    break;
                case PageBlockTypes.LogoCloud when Read<LogoCloudBlock>(block) is { } logos:
                    var list = logos.Logos.Count > 0 ? logos.Logos : _settings.TrustLogos;
                    if (list.Count == 0) break;
                    if (logos.Title is not null) nodes.Add(new ParagraphNode(logos.Title));
                    nodes.Add(new ListNode(list.Select(l => l.Name).ToList()));
                    break;
                case PageBlockTypes.Video when Read<VideoBlock>(block) is { } v:
                    var video = ToVideo(v, updatedAt);
                    nodes.Add(new HeadingNode(2, v.Title));
                    nodes.Add(new VideoNode(video));
                    AddVideo(page, video);
                    break;
            }
        }
        return nodes;
    }

    /// <summary>A CMS video block as an absolute-URL <see cref="SeoVideo"/> (upload date: the block's date or the page's last update).</summary>
    public SeoVideo ToVideo(VideoBlock v, DateTime pageUpdatedAt) => new(
        v.Title, v.Description ?? v.Title, Abs(v.Mp4Url), Abs(v.WebmUrl), Abs(v.PosterUrl), Abs(v.CaptionsUrl), v.CaptionsLanguage ?? "en", null,
        v.UploadDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) ?? pageUpdatedAt, v.DurationSeconds, v.Transcript);

    private string? Abs(string? url) => url is null ? null : _ld.Url(url);

    private void AddVideo(SeoPage page, SeoVideo video)
    {
        page.Videos.Add(video);
        page.JsonLd.Add(_seoLd.VideoObject(video, page.Path));
    }

    /// <summary>Videos placed on a built-in page in code (<see cref="SiteVideoCatalog"/>): content, VideoObject and video sitemap.</summary>
    private SeoPage AddCatalogVideos(SeoPage page)
    {
        foreach (var v in SiteVideoCatalog.ForPath(page.Path))
        {
            var video = ToVideo(v.Block, v.UploadDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            page.Content.Add(new HeadingNode(2, v.Block.Title));
            page.Content.Add(new VideoNode(video));
            AddVideo(page, video);
        }
        return page;
    }

    // ---------------------------------------------------------------- Landing pages and campaigns

    private async Task<SeoPage?> LandingAsync(string clientSlug, string pageSlug, CancellationToken ct)
    {
        var client = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Slug == clientSlug).Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync(ct);
        if (client is null) return null;
        var live = await landing.FindLiveAsync(client.Id, pageSlug, ct);
        if (live is null) return null;
        var snap = live.Snapshot;
        var path = $"/lp/{clientSlug}/{pageSlug}";
        var title = snap.MetaTitle ?? snap.Name;
        // Client landing pages carry their own brand: the title is used as written (no agency suffix), as in the web app.
        var page = NewPage(path, title, snap.MetaDescription, fullTitle: true);
        page.Source = $"Landing page ({client.Name})";
        page.EditPath = "/agency/pages";
        page.NoIndex = snap.NoIndex;
        page.NoFollow = snap.NoIndex; // as the landing page view writes it
        page.ModifiedAt = live.Version.PublishedAt;
        page.Images.Clear();
        SetImage(page, snap.OgImageUrl, title);
        page.JsonLd.Add(_seoLd.WebPage("WebPage", title, page.Description, path, live.Version.PublishedAt));
        var variant = LandingPageService.Variants(snap.Variants).FirstOrDefault();
        if (variant.Key is not null) page.Content.AddRange(LandingNodes(page, variant.Blocks, title, live.Version.PublishedAt));
        if (!page.Content.OfType<HeadingNode>().Any(h => h.Level == 1)) page.Content.Insert(0, new HeadingNode(1, title));
        return page;
    }

    private List<ContentNode> LandingNodes(SeoPage page, JsonElement blocks, string title, DateTime publishedAt)
    {
        var nodes = new List<ContentNode>();
        var first = true;
        foreach (var block in blocks.EnumerateArray())
        {
            var type = block.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (!block.TryGetProperty("props", out var props) || props.ValueKind != JsonValueKind.Object) continue;
            string? S(string name) => props.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
            IEnumerable<JsonElement> Items(string name) =>
                props.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();
            string? Si(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            switch (type)
            {
                case "hero":
                    nodes.Add(new HeadingNode(first ? 1 : 2, S("headline") ?? title));
                    if (S("subheadline") is { } sub) nodes.Add(new ParagraphNode(sub));
                    if (S("imageUrl") is { } img) nodes.Add(new ImageNode(img, S("imageAlt") ?? string.Empty, 1200, 630, Priority: first));
                    if (S("ctaLabel") is { } label && S("ctaHref") is { } href) nodes.Add(new ActionNode(label, href));
                    break;
                case "text":
                    if (S("heading") is { } heading) nodes.Add(new HeadingNode(2, heading));
                    if (S("body") is { } body) nodes.Add(new MarkdownNode(body, 3));
                    break;
                case "image":
                    if (S("url") is { } url && !(props.TryGetProperty("decorative", out var d) && d.ValueKind == JsonValueKind.True))
                    {
                        nodes.Add(new ImageNode(url, S("alt") ?? string.Empty, 1200, 675));
                        page.Images.Add(new SeoImage(_ld.Url(url), S("alt") ?? S("caption")));
                    }
                    break;
                case "video":
                    if (Domain.LandingPages.VideoEmbed.Resolve(null, S("provider"), S("videoId")) is { } embed)
                    {
                        var video = new SeoVideo(S("title") ?? title, S("title") ?? title, null, null,
                            embed.Provider == "youtube" ? $"https://i.ytimg.com/vi/{embed.Id}/hqdefault.jpg" : null, null, "en",
                            embed.Provider == "youtube" ? $"https://www.youtube-nocookie.com/embed/{embed.Id}" : $"https://player.vimeo.com/video/{embed.Id}?dnt=1",
                            publishedAt, null);
                        nodes.Add(new VideoNode(video));
                        if (video.PosterUrl is not null) AddVideo(page, video);
                    }
                    break;
                case "features":
                    if (S("heading") is { } fh) nodes.Add(new HeadingNode(2, fh));
                    if (S("intro") is { } intro) nodes.Add(new ParagraphNode(intro));
                    foreach (var i in Items("items"))
                    {
                        nodes.Add(new HeadingNode(3, Si(i, "title") ?? string.Empty));
                        if (Si(i, "body") is { } b) nodes.Add(new ParagraphNode(b));
                    }
                    break;
                case "testimonials":
                    if (S("heading") is { } th) nodes.Add(new HeadingNode(2, th));
                    foreach (var i in Items("items")) nodes.Add(new QuoteNode(Si(i, "quote") ?? string.Empty, Si(i, "author")));
                    break;
                case "pricing":
                    if (S("heading") is { } ph) nodes.Add(new HeadingNode(2, ph));
                    nodes.Add(new FactsNode(Items("plans").Select(i => KeyValuePair.Create(Si(i, "name") ?? string.Empty,
                        $"{Si(i, "price")} {Si(i, "period")}".Trim())).ToList()));
                    break;
                case "faq":
                    nodes.Add(new HeadingNode(2, S("heading") ?? "Frequently asked questions"));
                    foreach (var i in Items("items")) nodes.Add(new QuestionNode(Si(i, "question") ?? string.Empty, Si(i, "answer") ?? string.Empty));
                    break;
                case "cta":
                    if (S("heading") is { } ch) nodes.Add(new HeadingNode(2, ch));
                    if (S("body") is { } cb) nodes.Add(new ParagraphNode(cb));
                    if (S("buttonLabel") is { } bl && S("buttonHref") is { } bh) nodes.Add(new ActionNode(bl, bh));
                    break;
                case "form":
                    if (S("heading") is { } formHeading) nodes.Add(new HeadingNode(2, formHeading));
                    if (S("description") is { } fd) nodes.Add(new ParagraphNode(fd));
                    break;
            }
            first = false;
        }
        return nodes;
    }

    private async Task<SeoPage?> CampaignAsync(string slug, CancellationToken ct)
    {
        var campaign = await db.Set<Campaign>().AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct);
        if (campaign is null || campaign.Visibility != CampaignVisibility.Public ||
            campaign.Status is not (CampaignStatus.Scheduled or CampaignStatus.Active))
            return null;
        var path = $"/c/{slug}";
        var headline = string.IsNullOrWhiteSpace(campaign.LandingHeadline) ? campaign.Title : campaign.LandingHeadline;
        var body = string.IsNullOrWhiteSpace(campaign.LandingBody) ? campaign.Summary : campaign.LandingBody;
        var page = NewPage(path, campaign.Title, campaign.Summary);
        page.Source = "Public campaign";
        page.EditPath = "/manage/campaigns";
        page.ModifiedAt = campaign.UpdatedAt;
        page.Images.Clear();
        SetImage(page, campaign.HeroImageUrl, campaign.Title);
        CrumbsLd(page, ("Creators", "/creators"), (campaign.Title, path));
        page.JsonLd.Add(_seoLd.Campaign(campaign.Title, campaign.Summary, path, campaign.HeroImageUrl));
        page.Content.Add(new HeadingNode(1, headline));
        page.Content.Add(new ParagraphNode(body));
        page.Content.Add(new FactsNode(new[]
        {
            KeyValuePair.Create("Runs", $"{campaign.StartsAt:d MMMM yyyy} – {campaign.EndsAt:d MMMM yyyy}"),
            KeyValuePair.Create("Submit by", $"{campaign.SubmissionDeadline:d MMMM yyyy}"),
        }));
        page.Content.Add(new ParagraphNode(campaign.DefaultDisclosureText));
        page.Content.Add(new ActionNode("Join as a creator to take part", "/register"));
        return page;
    }
}
