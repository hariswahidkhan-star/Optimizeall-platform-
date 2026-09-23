using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Seo;

/// <summary>A website of a client that the agency audits, tracks rankings for and builds links to.</summary>
public class SeoSite : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Host, optionally with a port (e.g. "www.example.com" or "127.0.0.1:5106"), lower case, no scheme or path.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>"https" (default) or "http".</summary>
    public string Protocol { get; set; } = "https";
    public string? SitemapUrl { get; set; }

    /// <summary>ISO 3166-1 alpha-2 target market, e.g. "US".</summary>
    public string TargetCountry { get; set; } = "US";

    /// <summary>BCP 47 primary language, e.g. "en".</summary>
    public string TargetLanguage { get; set; } = "en";

    /// <summary>Competitor domains used for share of voice.</summary>
    public List<string> Competitors { get; set; } = new();

    public int MaxPages { get; set; } = 500;
    public int MaxDepth { get; set; } = 10;
    public bool IsArchived { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public string BaseUrl => $"{Protocol}://{Domain}/";
}

public enum SeoAuditStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public enum SeoSeverity
{
    Error,
    Warning,
    Notice,
}

/// <summary>One crawl + analysis run of a site.</summary>
public class SeoAudit : Entity
{
    public Guid SiteId { get; set; }
    public Guid ClientAccountId { get; set; }
    public SeoAuditStatus Status { get; set; } = SeoAuditStatus.Queued;
    public Guid? RequestedByUserId { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int PagesCrawled { get; set; }
    public int? HealthScore { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int NoticeCount { get; set; }
    public bool RobotsTxtFound { get; set; }
    public bool SitemapFound { get; set; }
    public string? FailureMessage { get; set; }

    /// <summary>Limits used for this run (copied from the site when queued).</summary>
    public int MaxPages { get; set; } = 500;
    public int MaxDepth { get; set; } = 10;
}

/// <summary>A crawled URL of an audit.</summary>
public class SeoAuditPage : Entity
{
    public Guid AuditId { get; set; }
    public string Url { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public int Depth { get; set; }
    public int ResponseTimeMs { get; set; }
    public long ContentLength { get; set; }
    public string? ContentType { get; set; }
    public string? Title { get; set; }
    public string? MetaDescription { get; set; }
    public int H1Count { get; set; }
    public int WordCount { get; set; }
    public string? Canonical { get; set; }
    public bool IsNoindex { get; set; }
    public bool InSitemap { get; set; }
    public int InboundLinks { get; set; }

    /// <summary>Redirect hops "301 http://a → https://a" joined by newlines; null when the URL answered directly.</summary>
    public string? RedirectChain { get; set; }
    public string? FetchError { get; set; }
}

/// <summary>One failed check of an audit, with the URLs it affects (capped; see <see cref="AffectedCount"/>).</summary>
public class SeoAuditIssue : Entity
{
    public Guid AuditId { get; set; }

    /// <summary>Rule key from <see cref="SeoAuditRules"/>, e.g. "title.missing".</summary>
    public string RuleKey { get; set; } = string.Empty;
    public SeoSeverity Severity { get; set; }
    public int AffectedCount { get; set; }
    public List<string> AffectedUrls { get; set; } = new();

    /// <summary>Optional detail per URL ("url\tdetail" lines) such as the broken link target or the duplicate title.</summary>
    public string? Details { get; set; }
}

/// <summary>Editable copy of an audit rule's explanation (seeded from <see cref="SeoAuditRules"/>).</summary>
public class SeoAuditRule
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public SeoSeverity Severity { get; set; }
    public string WhyItMatters { get; set; } = string.Empty;
    public string HowToFix { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

public enum KeywordIntent
{
    Unknown,
    Informational,
    Navigational,
    Commercial,
    Transactional,
}

public class SeoKeyword : AuditedEntity, IConcurrencyStamped
{
    public Guid SiteId { get; set; }
    public Guid ClientAccountId { get; set; }
    public string Keyword { get; set; } = string.Empty;

    /// <summary>Lower-case, single-spaced keyword (unique per site).</summary>
    public string NormalizedKeyword { get; set; } = string.Empty;
    public KeywordIntent Intent { get; set; }
    public int? SearchVolume { get; set; }

    /// <summary>0–100 keyword difficulty when a data provider supplies it.</summary>
    public int? Difficulty { get; set; }
    public string? TargetUrl { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool IsTracked { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public enum RankSource
{
    Manual,
    CsvImport,
    DataForSeo,
    SearchConsole,
}

/// <summary>
/// Daily ranking of a domain for a keyword (the site's own domain or a competitor's). Unique per
/// (keyword, date, domain), so re-imports and re-runs update instead of duplicating.
/// </summary>
public class SeoRankSnapshot : Entity
{
    public Guid KeywordId { get; set; }
    public Guid SiteId { get; set; }
    public Guid ClientAccountId { get; set; }
    public DateOnly Date { get; set; }
    public string Domain { get; set; } = string.Empty;

    /// <summary>Organic position (1 = top); null when not ranking in the checked depth.</summary>
    public int? Position { get; set; }
    public string? Url { get; set; }
    public List<string> SerpFeatures { get; set; } = new();
    public RankSource Source { get; set; }
    public DateTime RecordedAt { get; set; }
}

/// <summary>Search Console performance row (query × page × day), imported via the API adapter or a CSV export.</summary>
public class SeoSearchPerformance : Entity
{
    public Guid SiteId { get; set; }
    public Guid ClientAccountId { get; set; }
    public DateOnly Date { get; set; }
    public string Query { get; set; } = string.Empty;
    public string Page { get; set; } = string.Empty;

    /// <summary>SHA-256 of "query\npage" for the unique index (long text cannot be indexed portably).</summary>
    public string RowHash { get; set; } = string.Empty;
    public int Clicks { get; set; }
    public int Impressions { get; set; }
    public double Ctr { get; set; }
    public double Position { get; set; }
    public RankSource Source { get; set; }
}

public enum BacklinkStatus
{
    Unchecked,
    Live,
    Nofollow,
    Lost,
    Error,
}

public class SeoBacklink : AuditedEntity
{
    public Guid SiteId { get; set; }
    public Guid ClientAccountId { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;

    /// <summary>SHA-256 of "source\ntarget" (unique per site).</summary>
    public string LinkHash { get; set; } = string.Empty;
    public string? AnchorText { get; set; }

    /// <summary>Rel attribute as last seen ("" = dofollow; e.g. "nofollow", "ugc sponsored").</summary>
    public string? Rel { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public BacklinkStatus Status { get; set; }
    public int? LastStatusCode { get; set; }
    public string? CheckMessage { get; set; }
}

public enum OutreachStatus
{
    Identified,
    Contacted,
    FollowedUp,
    Replied,
    Won,
    Lost,
}

public class SeoOutreachProspect : AuditedEntity, IConcurrencyStamped
{
    public Guid SiteId { get; set; }
    public Guid ClientAccountId { get; set; }
    public string ProspectUrl { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public OutreachStatus Status { get; set; }
    public string? Notes { get; set; }
    public DateTime? LastContactedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Canonical NAP (name, address, phone) and Google Business Profile checklist of a site's business.</summary>
public class SeoLocalProfile : AuditedEntity, IConcurrencyStamped
{
    public Guid SiteId { get; set; }
    public Guid ClientAccountId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }

    /// <summary>Completed <see cref="LocalSeoCatalog.GbpChecklist"/> item keys.</summary>
    public List<string> CompletedChecklist { get; set; } = new();
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>A business directory where citations can be listed (seeded; see <see cref="LocalSeoCatalog.Directories"/>).</summary>
public class SeoCitationSource : Entity
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    /// <summary>Country codes where the directory matters most; empty = global.</summary>
    public List<string> Countries { get; set; } = new();
    public int SortOrder { get; set; }
}

public enum CitationStatus
{
    NotStarted,
    Submitted,
    Live,
    NeedsUpdate,
    Rejected,
}

public class SeoCitation : AuditedEntity, IConcurrencyStamped
{
    public Guid SiteId { get; set; }
    public Guid ClientAccountId { get; set; }
    public Guid SourceId { get; set; }
    public CitationStatus Status { get; set; }
    public string? ListingUrl { get; set; }

    /// <summary>What the listing shows, compared with the canonical NAP for consistency.</summary>
    public string? ListedName { get; set; }
    public string? ListedAddress { get; set; }
    public string? ListedPhone { get; set; }
    public string? Notes { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class SeoReview : AuditedEntity
{
    public Guid SiteId { get; set; }
    public Guid ClientAccountId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? AuthorName { get; set; }
    public string? Text { get; set; }
    public DateTime ReviewedAt { get; set; }
    public bool Responded { get; set; }
    public string? ResponseText { get; set; }
}

public enum ContentBriefStatus
{
    Draft,
    Ready,
    HandedOff,
}

public class SeoContentBrief : AuditedEntity, IConcurrencyStamped
{
    public Guid SiteId { get; set; }
    public Guid ClientAccountId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TargetKeyword { get; set; } = string.Empty;
    public List<string> RelatedKeywords { get; set; } = new();
    public List<string> Questions { get; set; } = new();

    /// <summary>Outline lines; "## " / "### " prefixes mark H2/H3.</summary>
    public List<string> Outline { get; set; } = new();
    public int WordCountTarget { get; set; } = 1200;
    public List<string> CompetitorUrls { get; set; } = new();
    public string? Notes { get; set; }
    public ContentBriefStatus Status { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
