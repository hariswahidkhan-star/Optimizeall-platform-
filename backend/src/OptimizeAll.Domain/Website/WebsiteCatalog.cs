using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Website;

/// <summary>Per-page search/social metadata. Stored inline on the owning row.</summary>
public class SeoMeta
{
    /// <summary>Overrides the page title (defaults to the item's name/title).</summary>
    public string? Title { get; set; }
    public string? Description { get; set; }

    /// <summary>Open Graph / Twitter image: an upload or an allowlisted https image.</summary>
    public string? OgImageUrl { get; set; }

    /// <summary>Canonical URL when the content also lives elsewhere (https or an app path).</summary>
    public string? CanonicalUrl { get; set; }

    /// <summary>Ask search engines not to index the page (also excluded from the sitemap).</summary>
    public bool NoIndex { get; set; }
}

/// <summary>A question and its answer (FAQ blocks, service FAQs → FAQPage JSON-LD).</summary>
public sealed record FaqEntry(string Question, string Answer);

/// <summary>A step of a process ("Discover", "Plan", "Launch", "Optimize").</summary>
public sealed record ProcessStep(string Title, string Description);

/// <summary>How a result figure was obtained. An estimate must never be presented as measured.</summary>
public enum MetricMeasurement
{
    Measured,
    Estimated,
}

/// <summary>A result figure on a case study ("Organic sessions", "+212%").</summary>
public sealed record ResultMetric(string Label, string Value, MetricMeasurement Measurement, string? Context = null);

/// <summary>A link in the navigation, footer or a block.</summary>
public sealed record SiteLink(string Label, string Url);

/// <summary>Rows addressed by a unique URL slug.</summary>
public interface ISlugged
{
    string Slug { get; }
}

/// <summary>Groups services on the website ("Search", "Paid Media").</summary>
public class ServiceCategory : AuditedEntity, IConcurrencyStamped, ISlugged
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Lucide icon name used by the web app (e.g. "search", "megaphone").</summary>
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>A service the agency sells (SEO, Meta Ads, Influencer &amp; UGC Marketing…).</summary>
public class AgencyService : AuditedEntity, IConcurrencyStamped, ISlugged
{
    public Guid CategoryId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;
    public string? HeroTitle { get; set; }
    public string? HeroBody { get; set; }

    /// <summary>Sanitized Markdown.</summary>
    public string? OverviewMarkdown { get; set; }
    public List<string> ProblemsSolved { get; set; } = new();
    public List<string> Deliverables { get; set; } = new();
    public List<ProcessStep> ProcessSteps { get; set; } = new();
    public List<string> Tools { get; set; } = new();
    public List<string> Kpis { get; set; } = new();
    public List<FaqEntry> Faqs { get; set; } = new();
    public List<Guid> RelatedServiceIds { get; set; } = new();
    public string? Icon { get; set; }
    public string? HeroImageUrl { get; set; }

    /// <summary>Optional call to action that replaces the default "Get a quote" (e.g. the creator program).</summary>
    public string? CtaLabel { get; set; }
    public string? CtaUrl { get; set; }
    public SeoMeta Seo { get; set; } = new();
    public bool IsPublished { get; set; }
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<ServicePackage> Packages { get; set; } = new();
}

public enum PackageBillingPeriod
{
    OneTime,
    Monthly,
    Quarterly,
    Yearly,
}

/// <summary>
/// A priced package of a service (Starter / Growth / Scale). Referenced by id from CRM proposals and billing, so a
/// package is deactivated rather than deleted once it is in use.
/// </summary>
public class ServicePackage : AuditedEntity, IConcurrencyStamped
{
    public Guid ServiceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Price per billing period; null when <see cref="IsCustomQuote"/>.</summary>
    public decimal? Price { get; set; }

    /// <summary>ISO 4217 currency of <see cref="Price"/> and <see cref="SetupFee"/>.</summary>
    public string Currency { get; set; } = "USD";
    public PackageBillingPeriod BillingPeriod { get; set; } = PackageBillingPeriod.Monthly;
    public decimal? SetupFee { get; set; }
    public List<string> Features { get; set; } = new();
    public bool IsMostPopular { get; set; }
    public bool IsCustomQuote { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>An industry the agency serves, with the services and case studies that matter to it.</summary>
public class Industry : AuditedEntity, IConcurrencyStamped, ISlugged
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? BodyMarkdown { get; set; }
    public List<string> Challenges { get; set; } = new();
    public List<Guid> ServiceIds { get; set; } = new();
    public string? Icon { get; set; }
    public string? HeroImageUrl { get; set; }
    public SeoMeta Seo { get; set; } = new();
    public bool IsPublished { get; set; }
    public int SortOrder { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class CaseStudy : AuditedEntity, IConcurrencyStamped, ISlugged
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;

    /// <summary>When the client asked not to be named, the site shows a description instead ("A DTC skincare brand").</summary>
    public bool ClientAnonymized { get; set; }
    public string Summary { get; set; } = string.Empty;
    public Guid? IndustryId { get; set; }
    public List<Guid> ServiceIds { get; set; } = new();
    public string? ChallengeMarkdown { get; set; }
    public string? StrategyMarkdown { get; set; }
    public string? ExecutionMarkdown { get; set; }
    public List<ResultMetric> Metrics { get; set; } = new();
    public string? TestimonialQuote { get; set; }
    public string? TestimonialAuthor { get; set; }
    public string? TestimonialRole { get; set; }
    public string? CoverImageUrl { get; set; }
    public List<string> GalleryImageUrls { get; set; } = new();
    public SeoMeta Seo { get; set; } = new();
    public bool IsPublished { get; set; }
    public bool IsFeatured { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int SortOrder { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class Testimonial : AuditedEntity, IConcurrencyStamped
{
    public string Quote { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorRole { get; set; }
    public string? Company { get; set; }

    /// <summary>1–5 stars; null when the client gave no rating.</summary>
    public int? Rating { get; set; }
    public string? AvatarUrl { get; set; }
    public Guid? ServiceId { get; set; }
    public bool IsPublished { get; set; }
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class TeamMember : AuditedEntity, IConcurrencyStamped, ISlugged
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public string? PhotoUrl { get; set; }
    public List<string> Expertise { get; set; } = new();
    public List<SiteLink> SocialLinks { get; set; } = new();
    public bool IsPublished { get; set; } = true;
    public int SortOrder { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public enum SitePageKind
{
    Standard,
    /// <summary>Privacy, terms, cookies… (linked from the footer's legal row).</summary>
    Legal,
}

/// <summary>
/// A generic CMS page built from ordered blocks. <see cref="BlocksJson"/> holds a validated JSON array of
/// <c>{ id, type, data }</c> objects; each block type has its own schema (see the Website module's PageBlocks).
/// </summary>
public class SitePage : AuditedEntity, IConcurrencyStamped, ISlugged
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public SitePageKind Kind { get; set; }
    public string BlocksJson { get; set; } = "[]";
    public SeoMeta Seo { get; set; } = new();
    public bool IsPublished { get; set; }
    public int SortOrder { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Site-wide settings (navigation, footer, contact details, social profiles, trust logos, announcement bar, default SEO,
/// organization schema, analytics ids) stored as one validated JSON document under a key.
/// </summary>
public class SiteSettingsDocument : AuditedEntity, IConcurrencyStamped
{
    public const string DefaultKey = "site";

    public string Key { get; set; } = DefaultKey;
    public string Json { get; set; } = "{}";
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
