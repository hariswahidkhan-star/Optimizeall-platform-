using System.Text.Json;
using OptimizeAll.Api.Modules.Website.Pages;
using OptimizeAll.Api.Modules.Website.Settings;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.Public;

/// <summary>Resolved page metadata for the web app's head manager (title, description, canonical, OG image, robots).</summary>
public sealed record PublicSeoDto(string Title, string? Description, string? OgImageUrl, string? CanonicalUrl, bool NoIndex);

public sealed record ConsentTextsDto(string FormVersion, string FormText, string NewsletterVersion, string NewsletterText, string CareersVersion, string CareersText);

public sealed record MenuServiceDto(string Slug, string Name, string Tagline, string? Icon);

public sealed record MenuCategoryDto(string Slug, string Name, string? Description, string? Icon, IReadOnlyList<MenuServiceDto> Services);

/// <summary><c>GET /public/site</c>: everything the header, footer, consent banner and head manager need.</summary>
public sealed record PublicSiteDto(
    string SiteName, string Tagline, HeaderSettings Header, FooterSettings Footer, ContactSettings Contact, IReadOnlyList<SocialProfile> Social,
    IReadOnlyList<TrustLogo> TrustLogos, AnnouncementBar Announcement, DefaultSeo Seo, AnalyticsSettings Analytics,
    IReadOnlyList<MenuCategoryDto> ServiceMenu, ConsentTextsDto Consent, bool BookingEnabled);

public sealed record PriceDto(decimal Amount, string Currency, PackageBillingPeriod BillingPeriod);

public sealed record ServiceCardDto(
    Guid Id, string Slug, string Name, string Tagline, string? Icon, string CategorySlug, string CategoryName, PriceDto? StartingPrice);

public sealed record PublicPackageDto(
    Guid Id, string Name, string? Description, decimal? Price, string Currency, PackageBillingPeriod BillingPeriod, decimal? SetupFee,
    IReadOnlyList<string> Features, bool IsMostPopular, bool IsCustomQuote);

public sealed record ServiceCategoryGroupDto(string Slug, string Name, string? Description, string? Icon, IReadOnlyList<ServiceCardDto> Services);

public sealed record MetricDto(string Label, string Value, MetricMeasurement Measurement, string? Context);

public sealed record CaseStudyCardDto(
    string Slug, string Title, string ClientName, string Summary, string? IndustrySlug, string? IndustryName, IReadOnlyList<string> ServiceSlugs,
    IReadOnlyList<string> ServiceNames, string? CoverImageUrl, IReadOnlyList<MetricDto> Highlights, bool IsFeatured);

public sealed record PublicTestimonialDto(
    Guid Id, string Quote, string AuthorName, string? AuthorRole, string? Company, int? Rating, string? AvatarUrl, string? ServiceSlug);

public sealed record IndustryCardDto(string Slug, string Name, string Summary, string? Icon);

public sealed record PublicServiceDto(
    Guid Id, string Slug, string Name, string Tagline, string? HeroTitle, string? HeroBody, string? OverviewMarkdown,
    IReadOnlyList<string> ProblemsSolved, IReadOnlyList<string> Deliverables, IReadOnlyList<ProcessStep> ProcessSteps, IReadOnlyList<string> Tools,
    IReadOnlyList<string> Kpis, IReadOnlyList<FaqEntry> Faqs, string? Icon, string? HeroImageUrl, string? CtaLabel, string? CtaUrl,
    string CategorySlug, string CategoryName, IReadOnlyList<PublicPackageDto> Packages, IReadOnlyList<ServiceCardDto> RelatedServices,
    IReadOnlyList<CaseStudyCardDto> CaseStudies, IReadOnlyList<PublicTestimonialDto> Testimonials, PublicSeoDto Seo,
    IReadOnlyList<JsonElement> JsonLd);

public sealed record PublicIndustryDto(
    string Slug, string Name, string Summary, string? BodyMarkdown, IReadOnlyList<string> Challenges, string? Icon, string? HeroImageUrl,
    IReadOnlyList<ServiceCardDto> Services, IReadOnlyList<CaseStudyCardDto> CaseStudies, PublicSeoDto Seo, IReadOnlyList<JsonElement> JsonLd);

public sealed record PublicCaseStudyDto(
    string Slug, string Title, string ClientName, string Summary, string? IndustrySlug, string? IndustryName, IReadOnlyList<ServiceCardDto> Services,
    string? ChallengeMarkdown, string? StrategyMarkdown, string? ExecutionMarkdown, IReadOnlyList<MetricDto> Metrics, string? TestimonialQuote,
    string? TestimonialAuthor, string? TestimonialRole, string? CoverImageUrl, IReadOnlyList<string> GalleryImageUrls, DateTime? PublishedAt,
    IReadOnlyList<CaseStudyCardDto> Related, PublicSeoDto Seo, IReadOnlyList<JsonElement> JsonLd);

public sealed record PublicTeamMemberDto(string Slug, string Name, string Role, string? Bio, string? PhotoUrl, IReadOnlyList<string> Expertise, IReadOnlyList<SiteLink> SocialLinks);

public sealed record PublicPageDto(
    string Slug, string Title, string? Summary, SitePageKind Kind, IReadOnlyList<PageBlock> Blocks, IReadOnlyList<PublicTestimonialDto> Testimonials,
    IReadOnlyList<CaseStudyCardDto> CaseStudies, IReadOnlyList<ServiceCategoryGroupDto> ServiceCategories, IReadOnlyList<TrustLogo> TrustLogos,
    DateTime UpdatedAt, PublicSeoDto Seo, IReadOnlyList<JsonElement> JsonLd);

public sealed record BlogCategoryRefDto(string Slug, string Name);

public sealed record AuthorDto(string Slug, string Name, string Role, string? Bio, string? PhotoUrl, IReadOnlyList<SiteLink> SocialLinks);

public sealed record PostCardDto(
    string Slug, string Title, string Excerpt, string? CoverImageUrl, string? CoverImageAlt, string? AuthorName, IReadOnlyList<BlogCategoryRefDto> Categories,
    IReadOnlyList<string> Tags, int ReadingMinutes, DateTime? PublishedAt);

public sealed record PublicPostDto(
    string Slug, string Title, string Excerpt, string BodyMarkdown, string? CoverImageUrl, string? CoverImageAlt, AuthorDto? Author,
    IReadOnlyList<BlogCategoryRefDto> Categories, IReadOnlyList<string> Tags, int ReadingMinutes, DateTime? PublishedAt, DateTime UpdatedAt,
    IReadOnlyList<PostCardDto> Related, PublicSeoDto Seo, IReadOnlyList<JsonElement> JsonLd);

public sealed record BlogCategoryCountDto(string Slug, string Name, string? Description, int PostCount);

public sealed record TagCountDto(string Tag, int PostCount);

public sealed record BlogIndexDto(
    IReadOnlyList<PostCardDto> Items, int Total, int Page, int PageSize, IReadOnlyList<BlogCategoryCountDto> Categories, IReadOnlyList<TagCountDto> Tags);

public sealed record PricingServiceDto(ServiceCardDto Service, IReadOnlyList<PublicPackageDto> Packages);

public sealed record PricingDto(IReadOnlyList<PricingServiceDto> Services);

public sealed record PricingTeaserDto(string ServiceSlug, string ServiceName, PublicPackageDto Package);

public sealed record HomeDto(
    IReadOnlyList<ServiceCategoryGroupDto> ServiceCategories, IReadOnlyList<CaseStudyCardDto> FeaturedCaseStudies,
    IReadOnlyList<PublicTestimonialDto> Testimonials, IReadOnlyList<IndustryCardDto> Industries, IReadOnlyList<PostCardDto> LatestPosts,
    IReadOnlyList<PricingTeaserDto> PricingTeaser, IReadOnlyList<HomeStat> Stats, IReadOnlyList<TrustLogo> TrustLogos, PublicSeoDto Seo,
    IReadOnlyList<JsonElement> JsonLd);

public sealed record JobCardDto(
    string Slug, string Title, string Department, string Location, WorkplaceType Workplace, EmploymentType EmploymentType, string Summary,
    DateTime? PostedAt);

public sealed record SalaryDto(decimal? Min, decimal? Max, string Currency, SalaryPeriod Period);

public sealed record PublicJobDto(
    string Slug, string Title, string Department, string Location, WorkplaceType Workplace, EmploymentType EmploymentType, string Summary,
    string DescriptionMarkdown, IReadOnlyList<string> Requirements, IReadOnlyList<string> Benefits, SalaryDto? Salary, DateTime? PostedAt,
    DateTime? ClosesAt, PublicSeoDto Seo, IReadOnlyList<JsonElement> JsonLd);

public sealed record SearchHitDto(string Kind, string Slug, string Title, string Summary, string Url);

public sealed record SearchResultDto(string Query, IReadOnlyList<SearchHitDto> Services, IReadOnlyList<SearchHitDto> Posts, IReadOnlyList<SearchHitDto> CaseStudies);
