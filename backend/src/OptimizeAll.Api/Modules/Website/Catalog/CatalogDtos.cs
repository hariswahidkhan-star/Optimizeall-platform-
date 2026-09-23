using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Website.Pages;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.Catalog;

public class StampedInput
{
    /// <summary>Required on update: the stamp the editor last loaded (stale writes get 409).</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

// ---------- Service categories ----------

public sealed record ServiceCategoryDto(
    Guid Id, string Slug, string Name, string? Description, string? Icon, int SortOrder, bool IsPublished, int ServiceCount,
    DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class ServiceCategoryInput : StampedInput
{
    [Required, MaxLength(100)] public string Slug { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    [MaxLength(40)] public string? Icon { get; set; }
    [Range(-100000, 100000)] public int SortOrder { get; set; }
    public bool IsPublished { get; set; } = true;
}

// ---------- Services & packages ----------

public sealed record PackageDto(
    Guid Id, Guid ServiceId, string Name, string? Description, decimal? Price, string Currency, PackageBillingPeriod BillingPeriod,
    decimal? SetupFee, IReadOnlyList<string> Features, bool IsMostPopular, bool IsCustomQuote, bool IsActive, int SortOrder,
    DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class PackageInput : StampedInput
{
    [Required, MaxLength(80)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    [Range(0, 10_000_000)] public decimal? Price { get; set; }
    [Required, MaxLength(3)] public string Currency { get; set; } = "USD";
    [Required] public PackageBillingPeriod? BillingPeriod { get; set; }
    [Range(0, 10_000_000)] public decimal? SetupFee { get; set; }
    public List<string?>? Features { get; set; }
    public bool IsMostPopular { get; set; }
    public bool IsCustomQuote { get; set; }
    public bool IsActive { get; set; } = true;
    [Range(-100000, 100000)] public int SortOrder { get; set; }
}

public sealed record ServiceSummaryDto(
    Guid Id, Guid CategoryId, string CategoryName, string Slug, string Name, string Tagline, string? Icon, bool IsPublished,
    bool IsFeatured, int SortOrder, int PackageCount, DateTime UpdatedAt);

public sealed record ServiceDto(
    Guid Id, Guid CategoryId, string Slug, string Name, string Tagline, string? HeroTitle, string? HeroBody, string? OverviewMarkdown,
    IReadOnlyList<string> ProblemsSolved, IReadOnlyList<string> Deliverables, IReadOnlyList<ProcessStep> ProcessSteps,
    IReadOnlyList<string> Tools, IReadOnlyList<string> Kpis, IReadOnlyList<FaqEntry> Faqs, IReadOnlyList<Guid> RelatedServiceIds,
    string? Icon, string? HeroImageUrl, string? CtaLabel, string? CtaUrl, SeoDto Seo, bool IsPublished, bool IsFeatured, int SortOrder,
    IReadOnlyList<PackageDto> Packages, DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class ServiceInput : StampedInput
{
    [Required] public Guid? CategoryId { get; set; }
    [Required, MaxLength(100)] public string Slug { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Tagline { get; set; } = string.Empty;
    [MaxLength(150)] public string? HeroTitle { get; set; }
    [MaxLength(600)] public string? HeroBody { get; set; }
    [MaxLength(60000)] public string? OverviewMarkdown { get; set; }
    public List<string?>? ProblemsSolved { get; set; }
    public List<string?>? Deliverables { get; set; }
    public List<ProcessStepInput>? ProcessSteps { get; set; }
    public List<string?>? Tools { get; set; }
    public List<string?>? Kpis { get; set; }
    public List<FaqEntryInput>? Faqs { get; set; }
    public List<Guid>? RelatedServiceIds { get; set; }
    [MaxLength(40)] public string? Icon { get; set; }
    [MaxLength(500)] public string? HeroImageUrl { get; set; }
    [MaxLength(60)] public string? CtaLabel { get; set; }
    [MaxLength(500)] public string? CtaUrl { get; set; }
    public SeoInput? Seo { get; set; }
    public bool IsPublished { get; set; }
    public bool IsFeatured { get; set; }
    [Range(-100000, 100000)] public int SortOrder { get; set; }
}

public sealed class ServiceQuery : PageQuery
{
    public Guid? CategoryId { get; set; }
    public bool? IsPublished { get; set; }
}

// ---------- Industries ----------

public sealed record IndustryDto(
    Guid Id, string Slug, string Name, string Summary, string? BodyMarkdown, IReadOnlyList<string> Challenges, IReadOnlyList<Guid> ServiceIds,
    string? Icon, string? HeroImageUrl, SeoDto Seo, bool IsPublished, int SortOrder, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class IndustryInput : StampedInput
{
    [Required, MaxLength(100)] public string Slug { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string Summary { get; set; } = string.Empty;
    [MaxLength(60000)] public string? BodyMarkdown { get; set; }
    public List<string?>? Challenges { get; set; }
    public List<Guid>? ServiceIds { get; set; }
    [MaxLength(40)] public string? Icon { get; set; }
    [MaxLength(500)] public string? HeroImageUrl { get; set; }
    public SeoInput? Seo { get; set; }
    public bool IsPublished { get; set; }
    [Range(-100000, 100000)] public int SortOrder { get; set; }
}

// ---------- Case studies ----------

public sealed record CaseStudyDto(
    Guid Id, string Slug, string Title, string ClientName, bool ClientAnonymized, string Summary, Guid? IndustryId, IReadOnlyList<Guid> ServiceIds,
    string? ChallengeMarkdown, string? StrategyMarkdown, string? ExecutionMarkdown, IReadOnlyList<ResultMetric> Metrics,
    string? TestimonialQuote, string? TestimonialAuthor, string? TestimonialRole, string? CoverImageUrl, IReadOnlyList<string> GalleryImageUrls,
    SeoDto Seo, bool IsPublished, bool IsFeatured, DateTime? PublishedAt, int SortOrder, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class ResultMetricInput
{
    [MaxLength(80)] public string? Label { get; set; }
    [MaxLength(20)] public string? Value { get; set; }

    /// <summary>Required for every metric: Measured or Estimated.</summary>
    public MetricMeasurement? Measurement { get; set; }
    [MaxLength(160)] public string? Context { get; set; }
}

public sealed class CaseStudyInput : StampedInput
{
    [Required, MaxLength(100)] public string Slug { get; set; } = string.Empty;
    [Required, MaxLength(160)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string ClientName { get; set; } = string.Empty;
    public bool ClientAnonymized { get; set; }
    [Required, MaxLength(500)] public string Summary { get; set; } = string.Empty;
    public Guid? IndustryId { get; set; }
    public List<Guid>? ServiceIds { get; set; }
    [MaxLength(30000)] public string? ChallengeMarkdown { get; set; }
    [MaxLength(30000)] public string? StrategyMarkdown { get; set; }
    [MaxLength(30000)] public string? ExecutionMarkdown { get; set; }
    public List<ResultMetricInput>? Metrics { get; set; }
    [MaxLength(1000)] public string? TestimonialQuote { get; set; }
    [MaxLength(120)] public string? TestimonialAuthor { get; set; }
    [MaxLength(120)] public string? TestimonialRole { get; set; }
    [MaxLength(500)] public string? CoverImageUrl { get; set; }
    public List<string?>? GalleryImageUrls { get; set; }
    public SeoInput? Seo { get; set; }
    public bool IsPublished { get; set; }
    public bool IsFeatured { get; set; }
    [Range(-100000, 100000)] public int SortOrder { get; set; }
}

// ---------- Testimonials ----------

public sealed record TestimonialDto(
    Guid Id, string Quote, string AuthorName, string? AuthorRole, string? Company, int? Rating, string? AvatarUrl, Guid? ServiceId,
    bool IsPublished, bool IsFeatured, int SortOrder, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class TestimonialInput : StampedInput
{
    [Required, MaxLength(1000)] public string Quote { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string AuthorName { get; set; } = string.Empty;
    [MaxLength(120)] public string? AuthorRole { get; set; }
    [MaxLength(120)] public string? Company { get; set; }
    [Range(1, 5)] public int? Rating { get; set; }
    [MaxLength(500)] public string? AvatarUrl { get; set; }
    public Guid? ServiceId { get; set; }
    public bool IsPublished { get; set; }
    public bool IsFeatured { get; set; }
    [Range(-100000, 100000)] public int SortOrder { get; set; }
}

// ---------- Team ----------

public sealed record TeamMemberDto(
    Guid Id, string Slug, string Name, string Role, string? Bio, string? PhotoUrl, IReadOnlyList<string> Expertise,
    IReadOnlyList<SiteLink> SocialLinks, bool IsPublished, int SortOrder, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class SiteLinkInput
{
    [MaxLength(60)] public string? Label { get; set; }
    [MaxLength(500)] public string? Url { get; set; }
}

public sealed class TeamMemberInput : StampedInput
{
    [Required, MaxLength(100)] public string Slug { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Role { get; set; } = string.Empty;
    [MaxLength(2000)] public string? Bio { get; set; }
    [MaxLength(500)] public string? PhotoUrl { get; set; }
    public List<string?>? Expertise { get; set; }
    public List<SiteLinkInput>? SocialLinks { get; set; }
    public bool IsPublished { get; set; } = true;
    [Range(-100000, 100000)] public int SortOrder { get; set; }
}

// ---------- Pages ----------

public sealed record SitePageSummaryDto(Guid Id, string Slug, string Title, SitePageKind Kind, bool IsPublished, int BlockCount, DateTime UpdatedAt);

public sealed record SitePageDto(
    Guid Id, string Slug, string Title, string? Summary, SitePageKind Kind, IReadOnlyList<PageBlock> Blocks, SeoDto Seo, bool IsPublished,
    int SortOrder, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class SitePageInput : StampedInput
{
    [Required, MaxLength(100)] public string Slug { get; set; } = string.Empty;
    [Required, MaxLength(160)] public string Title { get; set; } = string.Empty;
    [MaxLength(500)] public string? Summary { get; set; }
    [Required] public SitePageKind? Kind { get; set; }
    public List<PageBlockInput>? Blocks { get; set; }
    public SeoInput? Seo { get; set; }
    public bool IsPublished { get; set; }
    [Range(-100000, 100000)] public int SortOrder { get; set; }
}

/// <summary>Filter for simple CMS lists.</summary>
public sealed class CmsQuery : PageQuery
{
    public bool? IsPublished { get; set; }
}
