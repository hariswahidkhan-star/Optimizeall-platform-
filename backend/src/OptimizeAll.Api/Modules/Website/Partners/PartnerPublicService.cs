using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Partners;

/// <summary>
/// Anonymous read model of partners: the partners page and footer/strip data (<see cref="DirectoryAsync"/>), a partner's
/// profile (<see cref="ProfileAsync"/>) and the ad unit of a slot (<see cref="PlacementAsync"/>). Only active partners are
/// ever returned; a partner without a website has no outbound link at all (<c>visitUrl</c>/<c>websiteHost</c> null).
/// </summary>
public sealed class PartnerPublicService(AppDbContext db, PublicSiteService site, TimeProvider clock)
{
    public const string VisitPathTemplate = "/api/v1/public/partners/{0}/visit";

    private List<WebsitePartner>? _active;

    public async Task<IReadOnlyList<WebsitePartner>> ActiveAsync(CancellationToken ct) =>
        _active ??= await db.Set<WebsitePartner>().AsNoTracking().Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync(ct);

    /// <summary>Link rules of the active partners with a website (see <see cref="PartnerLinkPolicy"/>).</summary>
    public async Task<IReadOnlyList<PartnerLinkRule>> LinkRulesAsync(CancellationToken ct) =>
        (await ActiveAsync(ct)).Select(Rule).OfType<PartnerLinkRule>().ToList();

    public static PartnerLinkRule? Rule(WebsitePartner p) =>
        PartnerLinkPolicy.HostOf(p.WebsiteUrl) is { } host ? new PartnerLinkRule(p.Slug, host, p.UtmSource, p.UtmMedium, p.UtmCampaign) : null;

    public PublicPartnerCardDto Card(WebsitePartner p)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var host = PartnerLinkPolicy.HostOf(p.WebsiteUrl);
        return new PublicPartnerCardDto(
            p.Slug, p.Name, p.LogoUrl, p.Tagline, PartnerRules.Relationship(p), p.BrandColor, host,
            host is null ? null : string.Format(System.Globalization.CultureInfo.InvariantCulture, VisitPathTemplate, p.Slug),
            $"/partners/{p.Slug}", p.Slots, Offer(p, now));
    }

    private static PublicPartnerOfferDto? Offer(WebsitePartner p, DateTime now) =>
        PartnerOfferRules.IsVisible(p, now) ? new PublicPartnerOfferDto(p.OfferText!, p.OfferCode, p.OfferExpiresAt) : null;

    public async Task<PublicPartnersDto> DirectoryAsync(CancellationToken ct)
    {
        var partners = await ActiveAsync(ct);
        var ld = new JsonLd(await site.BaseUrlAsync(ct), await site.SettingsAsync(ct));
        var pld = new PartnerJsonLd(ld);
        var names = JoinNames(partners.Select(p => p.Name).ToList());
        const string title = "Our partners";
        var description = partners.Count == 0
            ? "Organizations Optimize All works with as their marketing partner."
            : Truncate($"Optimize All is the official marketing partner of {names}. Learn about each platform and what it offers.", 200);
        var seo = new PublicSeoDto(title, description, null, ld.Url("/partners"), false);
        return new PublicPartnersDto(
            partners.Select(Card).ToList(),
            partners.Select(Rule).OfType<PartnerLinkRule>().Select(r => new PartnerLinkRuleDto(r.Slug, r.Host, r.UtmSource, r.UtmMedium, r.UtmCampaign)).ToList(),
            seo,
            new[] { pld.Directory(partners, title, description), pld.Breadcrumbs(("Home", "/"), ("Partners", "/partners")) });
    }

    public async Task<PublicPartnerDto> ProfileAsync(string slug, CancellationToken ct)
    {
        var partners = await ActiveAsync(ct);
        var p = partners.FirstOrDefault(x => x.Slug == slug)
                ?? throw new DomainException("website.not_found", "Partner was not found.", DomainErrorKind.NotFound);
        var ld = new JsonLd(await site.BaseUrlAsync(ct), await site.SettingsAsync(ct));
        var pld = new PartnerJsonLd(ld);
        var path = $"/partners/{p.Slug}";
        var title = p.Seo.Title ?? DefaultTitle(p);
        var plain = MarkdownSanitizer.ToPlainText(p.DescriptionMarkdown);
        var description = p.Seo.Description ?? Truncate($"{p.Tagline}. {PartnerRules.Relationship(p)}.", 200);
        var seo = new PublicSeoDto(title, description, p.Seo.OgImageUrl ?? p.LogoUrl, ld.Url(p.Seo.CanonicalUrl ?? path), p.Seo.NoIndex);
        var related = p.RelatedPartnerIds.Select(id => partners.FirstOrDefault(x => x.Id == id)).OfType<WebsitePartner>().Select(Card).ToList();
        var card = Card(p);
        return new PublicPartnerDto(
            p.Slug, p.Name, p.LogoUrl, p.Tagline, card.RelationshipLabel, p.BrandColor, card.WebsiteHost, card.VisitUrl, p.DescriptionMarkdown,
            p.Highlights, p.Offerings.Select(PartnerOfferingDto.From).ToList(), p.Keywords, card.Offer, related, p.UpdatedAt, seo,
            new[]
            {
                pld.Organization(p, Truncate(plain.Length > 0 ? plain : p.Tagline, 500)),
                pld.ProfilePage(p, title, description, path),
                pld.Breadcrumbs(("Home", "/"), ("Partners", "/partners"), (p.Name, path)),
            });
    }

    /// <summary>The ad unit of <paramref name="query"/>'s slot (see <see cref="PartnerTargeting"/>): at most one partner.</summary>
    public async Task<PartnerPlacementDto> PlacementAsync(PartnerPlacementQuery query, CancellationToken ct)
    {
        var slot = PartnerSlots.Find(query.Slot?.Trim());
        if (slot is not { Kind: PartnerSlotKind.Unit })
        {
            var e = new FieldErrors();
            e.Add("slot", "Unknown ad slot. Lists (home strip, footer) use GET /public/partners.");
            e.ThrowIfAny("website.partner_slot_invalid", "Unknown ad slot.");
        }
        var eligible = (await ActiveAsync(ct)).Where(p => p.Slots.Contains(slot!.Name)).ToList();
        var chosen = PartnerTargeting.Choose(
            eligible.Select(p => new PartnerCandidate(p.Id, p.Slug, p.SortOrder, p.Keywords, p.Categories)).ToList(),
            Split(query.Keywords), Split(query.Categories), PartnerRules.NormalizePagePath(query.Path) ?? "/",
            DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        return new PartnerPlacementDto(slot!.Name, chosen is null ? null : Card(eligible.First(p => p.Id == chosen.Id)));
    }

    /// <summary>Accepts repeated values and comma-separated lists.</summary>
    private static IEnumerable<string> Split(IEnumerable<string>? values) =>
        (values ?? Enumerable.Empty<string>()).Where(v => v is not null).SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Take(PartnerTargeting.MaxTerms);

    private static string DefaultTitle(WebsitePartner p)
    {
        var withTagline = $"{p.Name} — {p.Tagline}";
        return withTagline.Length <= 60 ? withTagline : Truncate($"{p.Name} — official marketing partner", 70);
    }

    public static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)].TrimEnd() + "…";
}
