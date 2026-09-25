using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Website.Redirects;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.Partners;

/// <summary>Input rules and limits of partners (server-side; the editor mirrors the limits).</summary>
public static partial class PartnerRules
{
    public const int MaxKeywords = 40;
    public const int MaxOfferings = 16;
    public const int MaxPathLength = 200;
    public const int MaxImpressionBatch = 20;
    public const string DefaultRelationship = "Optimize All is the official marketing partner of {Partner}";
    public const string PartnerPlaceholder = "{Partner}";

    /// <summary>A logo bundled with the web app (frontend/public/partners/…).</summary>
    public static bool IsBundledLogo(string value) => BundledLogo().IsMatch(value);

    public static bool IsHttpsUrl(string? value) =>
        TrackingUrl.IsValidDestination(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo) && !value!.Contains('\\');

    /// <summary>A same-site path ("/partners/pci-ai#pcl-ai"), never another origin.</summary>
    public static bool IsSitePath(string value) =>
        value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal) && !value.Contains('\\') && !value.Any(char.IsWhiteSpace) &&
        !value.Any(char.IsControl);

    /// <summary>
    /// A public page path as counted in the statistics: lower-case, at most four segments of letters, digits and dashes
    /// (query and fragment removed). Anything else is not a page of the public site and is not counted.
    /// </summary>
    public static string? NormalizePagePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        var cut = v.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) v = v[..cut];
        v = v.ToLowerInvariant();
        if (v.Length > 1) v = v.TrimEnd('/');
        return v.Length <= MaxPathLength && PagePath().IsMatch(v) ? v : null;
    }

    public static string Relationship(WebsitePartner p) =>
        (string.IsNullOrWhiteSpace(p.RelationshipLabel) ? DefaultRelationship : p.RelationshipLabel).Replace(PartnerPlaceholder, p.Name, StringComparison.Ordinal);

    [GeneratedRegex(@"^/partners/[a-z0-9]+(-[a-z0-9]+)*\.(png|jpe?g|webp|svg)$", RegexOptions.CultureInvariant)]
    private static partial Regex BundledLogo();

    [GeneratedRegex(@"^/([a-z0-9]+(-[a-z0-9]+)*(/[a-z0-9]+(-[a-z0-9]+)*){0,3})?$", RegexOptions.CultureInvariant)]
    private static partial Regex PagePath();

    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    public static partial Regex Color();

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]{1,100}$", RegexOptions.CultureInvariant)]
    public static partial Regex UtmValue();

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,40}$", RegexOptions.CultureInvariant)]
    public static partial Regex OfferCode();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    public static partial Regex Token();
}

/// <summary>
/// Website → Partners (<c>site.manage</c>): partner profiles, their placement slots, UTM tags and offers, plus the
/// impression/click report. Every change is validated and audited (<c>website.partner_*</c>); renaming the slug of an
/// active partner records a 301 from the old profile address (<see cref="RedirectService"/>).
/// </summary>
public sealed class PartnerAdminService(
    CmsStore store, IAuditLogger audit, WebsiteRules rules, ImageUrlPolicy images, RedirectService redirects, TimeProvider clock)
{
    private DbContext Db => store.Db;

    public async Task<IReadOnlyList<PartnerDto>> ListAsync(CancellationToken ct)
    {
        var list = await Db.Set<WebsitePartner>().AsNoTracking().OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        return list.Select(p => ToDto(p, now)).ToList();
    }

    public async Task<PartnerDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await store.FindAsync<WebsitePartner>(id, ct, true), clock.GetUtcNow().UtcDateTime);

    public static IReadOnlyList<PartnerSlotDto> Slots() => PartnerSlots.All.Select(PartnerDtoMapping.ToDto).ToList();

    public async Task<PartnerDto> CreateAsync(PartnerInput input, CancellationToken ct)
    {
        var p = new WebsitePartner();
        await ApplyAsync(p, input, ct);
        await store.EnsureSlugFreeAsync<WebsitePartner>(p.Slug, null, ct);
        Db.Add(p);
        var now = clock.GetUtcNow().UtcDateTime;
        audit.Record("website.partner_created", nameof(WebsitePartner), p.Id, after: Snapshot(p));
        await redirects.SaveAsync(Address(p, null), () => store.SaveAsync(ct), ct);
        return ToDto(p, now);
    }

    public async Task<PartnerDto> UpdateAsync(Guid id, PartnerInput input, CancellationToken ct)
    {
        var p = await store.FindAsync<WebsitePartner>(id, ct);
        CmsStore.CheckStamp(store.Db, p, input.ConcurrencyStamp);
        var before = Snapshot(p);
        var wasAt = LiveAddress(p);
        await ApplyAsync(p, input, ct);
        await store.EnsureSlugFreeAsync<WebsitePartner>(p.Slug, p.Id, ct);
        audit.Record("website.partner_updated", nameof(WebsitePartner), p.Id, before, Snapshot(p));
        await redirects.SaveAsync(Address(p, wasAt), () => store.SaveAsync(ct), ct);
        return ToDto(p, clock.GetUtcNow().UtcDateTime);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct) =>
        store.DeleteAsync<WebsitePartner>(id, "website.partner_deleted", Snapshot, ct);

    public async Task<ReorderResult> ReorderAsync(ReorderInput input, CancellationToken ct) =>
        new(await store.ReorderAsync<WebsitePartner>(input.Ids, "website.partners_reordered", (p, order) => p.SortOrder = order, ct));

    private static string? LiveAddress(WebsitePartner p) => p.IsActive && !string.IsNullOrEmpty(p.Slug) ? $"/partners/{p.Slug}" : null;

    private static AddressChange Address(WebsitePartner p, string? wasAt) => new("partner", p.Id, wasAt, LiveAddress(p));

    private async Task ApplyAsync(WebsitePartner p, PartnerInput r, CancellationToken ct)
    {
        var e = new FieldErrors();
        var slug = WebsiteRules.Slug(r.Slug, "slug", e);
        var name = WebsiteRules.Clean(r.Name);
        if (name is null) e.Add("name", "Enter the partner's name.");

        var logo = WebsiteRules.Clean(r.LogoUrl);
        if (logo is null) e.Add("logoUrl", "Upload the partner's logo.");
        else if (!images.IsAllowed(logo) && !PartnerRules.IsBundledLogo(logo))
            e.Add("logoUrl", ImageUrlPolicy.Message);

        var website = WebsiteRules.Clean(r.WebsiteUrl);
        if (website is not null && !PartnerRules.IsHttpsUrl(website))
            e.Add("websiteUrl", "Use the partner's https:// address, or leave it empty until you have it.");

        var tagline = WebsiteRules.Clean(r.Tagline);
        if (tagline is null) e.Add("tagline", "Enter a short tagline.");
        var description = WebsiteRules.Markdown(r.DescriptionMarkdown, "descriptionMarkdown", e, 20000);
        var relationship = WebsiteRules.Clean(r.RelationshipLabel) ?? PartnerRules.DefaultRelationship;

        var highlights = WebsiteRules.Lines(r.Highlights, "highlights", e, 12, 200);
        var offerings = Offerings(r.Offerings, e);
        var keywords = WebsiteRules.Lines(r.Keywords, "keywords", e, PartnerRules.MaxKeywords, 80);
        var categories = WebsiteRules.Lines(r.Categories?.Select(c => c?.Trim().ToLowerInvariant()), "categories", e, 20, 60);
        if (categories.Any(c => !PartnerRules.Token().IsMatch(c)))
            e.Add("categories", "Use category slugs: lower-case letters, digits and dashes (e.g. project-management).");
        var sameAs = WebsiteRules.Lines(r.SameAs, "sameAs", e, 10, 500);
        if (sameAs.Any(u => !PartnerRules.IsHttpsUrl(u))) e.Add("sameAs", "Each profile must be an https:// address.");

        var slots = WebsiteRules.Lines(r.Slots, "slots", e, 20, 40);
        if (slots.Any(s => PartnerSlots.Find(s) is not { Kind: not PartnerSlotKind.Page }))
            e.Add("slots", "Pick placements from the list.");

        var related = (r.RelatedPartnerIds ?? new List<Guid>()).Distinct().Where(id => id != p.Id).ToList();
        if (related.Count > 0 && await Db.Set<WebsitePartner>().CountAsync(x => related.Contains(x.Id), ct) != related.Count)
            e.Add("relatedPartnerIds", "Some related partners no longer exist.");

        var color = WebsiteRules.Clean(r.BrandColor);
        if (color is not null && !PartnerRules.Color().IsMatch(color)) e.Add("brandColor", "Use a colour like #1F3A93.");

        var utmSource = WebsiteRules.Clean(r.UtmSource) ?? "optimizeall";
        var utmMedium = WebsiteRules.Clean(r.UtmMedium) ?? "partner";
        var utmCampaign = WebsiteRules.Clean(r.UtmCampaign);
        if (!PartnerRules.UtmValue().IsMatch(utmSource)) e.Add("utmSource", "Use letters, digits, dots, dashes or underscores.");
        if (!PartnerRules.UtmValue().IsMatch(utmMedium)) e.Add("utmMedium", "Use letters, digits, dots, dashes or underscores.");
        if (utmCampaign is not null && !PartnerRules.UtmValue().IsMatch(utmCampaign)) e.Add("utmCampaign", "Use letters, digits, dots, dashes or underscores.");

        var offerText = WebsiteRules.Clean(r.OfferText);
        var offerCode = WebsiteRules.Clean(r.OfferCode);
        var offerExpires = WebsiteRules.Utc(r.OfferExpiresAt);
        if (offerCode is not null && !PartnerRules.OfferCode().IsMatch(offerCode)) e.Add("offerCode", "Use letters, digits, dashes or underscores.");
        if (offerText is null && (offerCode is not null || offerExpires is not null)) e.Add("offerText", "Describe the offer, or clear its code and expiry.");
        if (offerExpires is { } exp && (exp.Year < 2000 || exp.Year > 2100)) e.Add("offerExpiresAt", "Enter a date between 2000 and 2100.");
        if (r.OfferConfirmed && offerText is null) e.Add("offerConfirmed", "There is no offer to confirm.");

        var seo = rules.Seo(r.Seo, e);
        e.ThrowIfAny();

        var offerChanged = p.OfferText != offerText || p.OfferCode != offerCode || p.OfferExpiresAt != offerExpires ||
                           (r.OfferConfirmed && !p.OfferConfirmed);
        p.Slug = slug;
        p.Name = name!;
        p.LogoUrl = logo!;
        p.WebsiteUrl = website;
        p.Tagline = tagline!;
        p.DescriptionMarkdown = description;
        p.RelationshipLabel = relationship;
        p.Highlights = highlights;
        p.Offerings = offerings;
        p.Keywords = keywords;
        p.Categories = categories;
        p.SameAs = sameAs;
        p.RelatedPartnerIds = related;
        p.Slots = PartnerSlots.All.Select(s => s.Name).Where(slots.Contains).ToList();
        p.BrandColor = color?.ToUpperInvariant();
        p.UtmSource = utmSource;
        p.UtmMedium = utmMedium;
        p.UtmCampaign = utmCampaign;
        p.OfferText = offerText;
        p.OfferCode = offerCode;
        p.OfferExpiresAt = offerExpires;
        p.OfferConfirmed = offerText is not null && r.OfferConfirmed;
        if (offerChanged) p.OfferUpdatedAt = offerText is null ? null : clock.GetUtcNow().UtcDateTime;
        p.Seo = seo;
        p.IsActive = r.IsActive;
        p.SortOrder = r.SortOrder;
    }

    private static List<PartnerOffering> Offerings(IEnumerable<PartnerOfferingInput?>? values, FieldErrors e)
    {
        var list = new List<PartnerOffering>();
        var i = 0;
        foreach (var o in values ?? Array.Empty<PartnerOfferingInput?>())
        {
            var field = $"offerings[{i++}]";
            if (o is null) continue;
            var title = WebsiteRules.Clean(o.Title);
            var summary = WebsiteRules.Clean(o.Summary);
            var facts = WebsiteRules.Lines(o.Facts, field + ".facts", e, 12, 200);
            var anchor = WebsiteRules.Clean(o.Anchor)?.ToLowerInvariant();
            var link = WebsiteRules.Clean(o.Link);
            if (title is null && summary is null && facts.Count == 0) continue;
            if (title is null) { e.Add(field + ".title", "Each item needs a title."); continue; }
            if (anchor is not null && (anchor.Length > 60 || !PartnerRules.Token().IsMatch(anchor)))
                e.Add(field + ".anchor", "Use lower-case letters, digits and dashes.");
            if (link is not null && (link.Length > 300 || !PartnerRules.IsSitePath(link)))
                e.Add(field + ".link", "Use a path on this site, e.g. /partners/pci-ai#pcl-ai.");
            list.Add(new PartnerOffering(title, summary, facts, anchor, link));
        }
        if (list.Count > PartnerRules.MaxOfferings) e.Add("offerings", $"Add at most {PartnerRules.MaxOfferings} items.");
        if (list.Where(o => o.Anchor is not null).GroupBy(o => o.Anchor).Any(g => g.Count() > 1)) e.Add("offerings", "Each section id can be used once.");
        return list;
    }

    private static object Snapshot(WebsitePartner p) => new
    {
        p.Slug, p.Name, p.LogoUrl, p.WebsiteUrl, p.Tagline, p.RelationshipLabel, p.Keywords, p.Categories, p.Slots, p.UtmSource, p.UtmMedium,
        p.UtmCampaign, p.OfferText, p.OfferCode, p.OfferExpiresAt, p.OfferConfirmed, p.IsActive, p.SortOrder,
    };

    public static PartnerDto ToDto(WebsitePartner p, DateTime now) => new(
        p.Id, p.Slug, p.Name, p.LogoUrl, p.WebsiteUrl, p.Tagline, p.DescriptionMarkdown, p.RelationshipLabel, p.Highlights,
        p.Offerings.Select(PartnerOfferingDto.From).ToList(), p.Keywords, p.Categories, p.SameAs, p.RelatedPartnerIds, p.Slots, p.BrandColor,
        p.UtmSource, p.UtmMedium, p.UtmCampaign, p.OfferText, p.OfferCode, p.OfferExpiresAt, p.OfferConfirmed, p.OfferUpdatedAt,
        PartnerOfferRules.IsVisible(p, now), PartnerOfferRules.VisibleUntil(p), SeoDto.From(p.Seo), p.IsActive, p.SortOrder,
        $"/partners/{p.Slug}", p.CreatedAt, p.UpdatedAt, p.ConcurrencyStamp);
}
