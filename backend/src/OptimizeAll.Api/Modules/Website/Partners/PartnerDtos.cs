using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.Partners;

// ---------------------------------------------------------------- staff (site.manage)

public sealed class PartnerOfferingInput
{
    [MaxLength(120)]
    public string? Title { get; set; }

    [MaxLength(600)]
    public string? Summary { get; set; }

    [MaxLength(12)]
    public List<string?>? Facts { get; set; }

    /// <summary>Section id on the profile page (lower-case slug). Default: none.</summary>
    [MaxLength(60)]
    public string? Anchor { get; set; }

    /// <summary>A site path the item links to, e.g. /partners/pci-ai#pcl-ai.</summary>
    [MaxLength(300)]
    public string? Link { get; set; }
}

public sealed class PartnerInput
{
    [Required, MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string LogoUrl { get; set; } = string.Empty;

    /// <summary>Optional until known: while empty, every link and call to action to the partner is hidden.</summary>
    [MaxLength(500)]
    public string? WebsiteUrl { get; set; }

    [Required, MaxLength(160)]
    public string Tagline { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string? DescriptionMarkdown { get; set; }

    /// <summary>"{Partner}" is replaced by the partner's name. Defaults to <see cref="PartnerRules.DefaultRelationship"/>.</summary>
    [MaxLength(160)]
    public string? RelationshipLabel { get; set; }

    [MaxLength(12)]
    public List<string?>? Highlights { get; set; }

    [MaxLength(PartnerRules.MaxOfferings)]
    public List<PartnerOfferingInput?>? Offerings { get; set; }

    [MaxLength(PartnerRules.MaxKeywords)]
    public List<string?>? Keywords { get; set; }

    [MaxLength(20)]
    public List<string?>? Categories { get; set; }

    [MaxLength(10)]
    public List<string?>? SameAs { get; set; }

    [MaxLength(20)]
    public List<Guid>? RelatedPartnerIds { get; set; }

    [MaxLength(20)]
    public List<string?>? Slots { get; set; }

    [MaxLength(7)]
    public string? BrandColor { get; set; }

    [MaxLength(100)]
    public string? UtmSource { get; set; }

    [MaxLength(100)]
    public string? UtmMedium { get; set; }

    [MaxLength(100)]
    public string? UtmCampaign { get; set; }

    [MaxLength(200)]
    public string? OfferText { get; set; }

    [MaxLength(40)]
    public string? OfferCode { get; set; }

    public DateTime? OfferExpiresAt { get; set; }

    /// <summary>Tick after checking the offer is still valid: offers are never shown unconfirmed.</summary>
    public bool OfferConfirmed { get; set; }

    public SeoInput? Seo { get; set; }
    public bool IsActive { get; set; }

    [Range(0, 100000)]
    public int SortOrder { get; set; }

    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record PartnerOfferingDto(string Title, string? Summary, IReadOnlyList<string> Facts, string? Anchor, string? Link)
{
    public static PartnerOfferingDto From(PartnerOffering o) => new(o.Title, o.Summary, o.Facts, o.Anchor, o.Link);
}

public sealed record PartnerDto(
    Guid Id, string Slug, string Name, string LogoUrl, string? WebsiteUrl, string Tagline, string? DescriptionMarkdown,
    string RelationshipLabel, IReadOnlyList<string> Highlights, IReadOnlyList<PartnerOfferingDto> Offerings,
    IReadOnlyList<string> Keywords, IReadOnlyList<string> Categories, IReadOnlyList<string> SameAs, IReadOnlyList<Guid> RelatedPartnerIds,
    IReadOnlyList<string> Slots, string? BrandColor, string UtmSource, string UtmMedium, string? UtmCampaign,
    string? OfferText, string? OfferCode, DateTime? OfferExpiresAt, bool OfferConfirmed, DateTime? OfferUpdatedAt,
    bool OfferVisible, DateTime? OfferVisibleUntil, SeoDto Seo, bool IsActive, int SortOrder, string PublicPath,
    DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed record PartnerSlotDto(string Name, PartnerSlotKind Kind, string Label, string Description);

public sealed class PartnerReportQuery
{
    /// <summary>First day (UTC, inclusive). Default: 30 days before <see cref="To"/>.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Last day (UTC, inclusive). Default: today.</summary>
    public DateOnly? To { get; set; }

    public Guid? PartnerId { get; set; }

    [MaxLength(40)]
    public string? Slot { get; set; }
}

public sealed record PartnerMetricDto(string Key, string Label, int Impressions, int Clicks, decimal? ClickThroughRate);

public sealed record PartnerDayDto(DateOnly Day, int Impressions, int Clicks);

public sealed record PartnerReportDto(
    DateOnly From, DateOnly To, int Impressions, int Clicks, decimal? ClickThroughRate,
    IReadOnlyList<PartnerMetricDto> ByPartner, IReadOnlyList<PartnerMetricDto> BySlot, IReadOnlyList<PartnerMetricDto> ByPage,
    IReadOnlyList<PartnerDayDto> Daily);

// ---------------------------------------------------------------- public

/// <summary>A currently valid offer of a partner (only confirmed, unexpired offers are ever returned).</summary>
public sealed record PublicPartnerOfferDto(string Text, string? Code, DateTime? ExpiresAt);

/// <summary>
/// A partner as a card, strip logo or ad unit. <see cref="VisitUrl"/> (the click counter, which redirects to the partner's site
/// with UTM tags) and <see cref="WebsiteHost"/> are null while the partner has no website: every outbound link is then hidden.
/// Outbound links must be rendered with <c>rel="sponsored noopener"</c> and <c>target="_blank"</c>.
/// </summary>
public sealed record PublicPartnerCardDto(
    string Slug, string Name, string LogoUrl, string Tagline, string RelationshipLabel, string? BrandColor,
    string? WebsiteHost, string? VisitUrl, string ProfilePath, IReadOnlyList<string> Slots, PublicPartnerOfferDto? Offer);

public sealed record PartnerLinkRuleDto(string Slug, string Host, string UtmSource, string UtmMedium, string? UtmCampaign);

public sealed record PublicPartnersDto(
    IReadOnlyList<PublicPartnerCardDto> Partners, IReadOnlyList<PartnerLinkRuleDto> LinkRules, PublicSeoDto Seo, IReadOnlyList<JsonElement> JsonLd);

public sealed record PublicPartnerDto(
    string Slug, string Name, string LogoUrl, string Tagline, string RelationshipLabel, string? BrandColor, string? WebsiteHost,
    string? VisitUrl, string? DescriptionMarkdown, IReadOnlyList<string> Highlights, IReadOnlyList<PartnerOfferingDto> Offerings,
    IReadOnlyList<string> Keywords, PublicPartnerOfferDto? Offer, IReadOnlyList<PublicPartnerCardDto> Related, DateTime UpdatedAt,
    PublicSeoDto Seo, IReadOnlyList<JsonElement> JsonLd);

public sealed record PartnerPlacementDto(string Slot, PublicPartnerCardDto? Partner);

public sealed class PartnerPlacementQuery
{
    [Required, MaxLength(40)]
    public string Slot { get; set; } = string.Empty;

    /// <summary>Page keywords (blog tags, service name, course topics). Comma-separated or repeated.</summary>
    [MaxLength(PartnerTargeting.MaxTerms)]
    public List<string>? Keywords { get; set; }

    /// <summary>Page categories (blog category slugs, service slug, course category).</summary>
    [MaxLength(PartnerTargeting.MaxTerms)]
    public List<string>? Categories { get; set; }

    /// <summary>The page path (rotation key).</summary>
    [MaxLength(PartnerRules.MaxPathLength)]
    public string? Path { get; set; }
}

public sealed class PartnerImpressionInput
{
    [Required, MaxLength(100)]
    public string Partner { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string Slot { get; set; } = string.Empty;

    [Required, MaxLength(PartnerRules.MaxPathLength)]
    public string Path { get; set; } = string.Empty;
}

public sealed class PartnerImpressionsInput
{
    [Required, MaxLength(PartnerRules.MaxImpressionBatch)]
    public List<PartnerImpressionInput?> Items { get; set; } = new();
}

public sealed record PartnerImpressionsResult(int Accepted);

public static class PartnerDtoMapping
{
    public static PartnerSlotDto ToDto(PartnerSlot s) => new(s.Name, s.Kind, s.Label, s.Description);

    public static PagedResult<T> Page<T>(IReadOnlyList<T> items) => new(items.ToList(), items.Count, 1, Math.Max(1, items.Count));
}
