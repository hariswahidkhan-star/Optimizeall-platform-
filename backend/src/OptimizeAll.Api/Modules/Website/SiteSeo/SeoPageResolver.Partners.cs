using OptimizeAll.Api.Modules.Website.Partners;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>
/// Server-rendered partner pages (Website → Partners): <c>/partners</c> and <c>/partners/{slug}</c>, with the same title,
/// description, canonical, robots and JSON-LD (CollectionPage/ItemList, Organization/WebPage, BreadcrumbList) as the web
/// app, which reads them from <see cref="PartnerPublicService"/> too. Every outbound partner link in the HTML gets
/// <c>rel="sponsored noopener"</c> (<see cref="SeoPartnerLinks"/>).
/// </summary>
public sealed partial class SeoPageResolver
{
    /// <summary>Link rules of the active partners, applied to every link of the server-rendered HTML.</summary>
    public Task<IReadOnlyList<PartnerLinkRule>> PartnerLinkRulesAsync(CancellationToken ct) => partners.LinkRulesAsync(ct);

    private async Task<SeoPage> PartnersAsync(CancellationToken ct)
    {
        var dir = await partners.DirectoryAsync(ct);
        var page = NewPage("/partners", dir.Seo.Title, dir.Seo.Description);
        page.Source = "Partners";
        page.EditPath = "/agency/website/partners";
        ApplySeo(page, dir.Seo, dir.JsonLd);
        // Listed in the sitemap only while a partner is active; an empty directory is not worth indexing.
        if (dir.Partners.Count == 0) page.NoIndex = true;
        Crumbs(page, ("Partners", "/partners"));
        var c = page.Content;
        c.Add(new ParagraphNode("Partners"));
        c.Add(new HeadingNode(1, "Our partners"));
        c.Add(new ParagraphNode(dir.Partners.Count > 0
            ? $"{_settings.SiteName} is the official marketing partner of {PartnerPublicService.JoinNames(dir.Partners.Select(p => p.Name).ToList())}. Each is an independent platform; here is what they offer."
            : $"Organizations {_settings.SiteName} is the official marketing partner of."));
        if (dir.Partners.Count > 0)
            c.Add(new LinkListNode(dir.Partners.Select(p => new LinkItem(p.Name, p.ProfilePath, $"{p.Tagline} ({p.RelationshipLabel})")).ToList()));
        page.ModifiedAt = (await partners.ActiveAsync(ct)).Select(p => (DateTime?)p.UpdatedAt).Max();
        return page;
    }

    private async Task<SeoPage> PartnerAsync(string slug, CancellationToken ct)
    {
        var p = await partners.ProfileAsync(slug, ct); // unknown or inactive → website.not_found → 404
        var path = $"/partners/{p.Slug}";
        var page = NewPage(path, p.Seo.Title, p.Seo.Description);
        page.Source = "Partner";
        page.EditPath = "/agency/website/partners";
        ApplySeo(page, p.Seo, p.JsonLd, p.Name);
        Crumbs(page, ("Partners", "/partners"), (p.Name, path));
        page.ModifiedAt = p.UpdatedAt;
        var visit = p.VisitUrl is null ? null : $"{p.VisitUrl}?slot=partners.profile&path={Uri.EscapeDataString(path)}";
        var c = page.Content;
        c.Add(new ParagraphNode("Official marketing partner"));
        c.Add(new HeadingNode(1, p.Name));
        c.Add(new ParagraphNode(p.Tagline));
        c.Add(new ParagraphNode(p.RelationshipLabel + "."));
        if (visit is not null) c.Add(new ActionNode($"Visit {p.WebsiteHost}", visit));
        if (p.Highlights.Count > 0) c.Add(new ListNode(p.Highlights));
        if (!string.IsNullOrWhiteSpace(p.DescriptionMarkdown))
        {
            c.Add(new HeadingNode(2, $"About {p.Name}"));
            c.Add(new MarkdownNode(p.DescriptionMarkdown, 3));
        }
        if (p.Offer is { } offer)
        {
            c.Add(new ParagraphNode(offer.Code is null ? offer.Text : $"{offer.Text} Code: {offer.Code}"));
            if (visit is not null) c.Add(new ActionNode("Get the offer", visit));
        }
        if (p.Offerings.Count > 0)
        {
            c.Add(new HeadingNode(2, $"What {p.Name} offers"));
            foreach (var o in p.Offerings)
            {
                c.Add(new HeadingNode(3, o.Title));
                if (!string.IsNullOrWhiteSpace(o.Summary)) c.Add(new ParagraphNode(o.Summary));
                if (o.Facts.Count > 0) c.Add(new ListNode(o.Facts));
                if (!string.IsNullOrWhiteSpace(o.Link)) c.Add(new ActionNode(o.Title, o.Link));
            }
        }
        if (p.Related.Count > 0)
        {
            c.Add(new HeadingNode(2, "Related partners"));
            c.Add(new LinkListNode(p.Related.Select(r => new LinkItem(r.Name, r.ProfilePath, r.Tagline)).ToList()));
        }
        if (visit is not null)
        {
            c.Add(new HeadingNode(2, $"Learn more at {p.WebsiteHost}"));
            c.Add(new ActionNode($"Visit {p.WebsiteHost}", visit));
        }
        return page;
    }
}
