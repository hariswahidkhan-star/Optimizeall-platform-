using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Campaigns;

public enum CampaignStatus
{
    Draft,
    /// <summary>Published but StartsAt is in the future.</summary>
    Scheduled,
    Active,
    Paused,
    Ended,
    Archived,
}

public enum CampaignVisibility
{
    /// <summary>Listed to every participant who matches the targeting rules.</summary>
    Public,
    /// <summary>Only reachable through an invitation link.</summary>
    InviteOnly,
}

public class CampaignCategory : AuditedEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Campaign : AuditedEntity, IConcurrencyStamped
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public CampaignCategory? Category { get; set; }

    /// <summary>Free-form topic tags used for browsing and interest matching (lower-case).</summary>
    public List<string> Topics { get; set; } = new();

    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    public CampaignVisibility Visibility { get; set; } = CampaignVisibility.Public;

    /// <summary>Posting window (UTC). Submissions whose PostedAt falls outside it are flagged or rejected.</summary>
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    /// <summary>Last moment proof can be submitted (UTC). Defaults to EndsAt plus a grace period.</summary>
    public DateTime SubmissionDeadline { get; set; }

    /// <summary>IANA zone used to present dates and evaluate daily caps for this campaign.</summary>
    public string TimeZone { get; set; } = "UTC";

    public string PostingInstructions { get; set; } = string.Empty;

    /// <summary>Default paid-content disclosure; platform/country-specific overrides live in <see cref="CampaignDisclosure"/>.</summary>
    public string DefaultDisclosureText { get; set; } = "#ad";
    public string? RequiredHashtags { get; set; }
    public string? RequiredMentions { get; set; }

    /// <summary>Total spend budget in <see cref="BudgetCurrency"/>. Null = unlimited.</summary>
    public decimal? BudgetAmount { get; set; }
    public string BudgetCurrency { get; set; } = "USD";

    /// <summary>Max submissions (any status except Rejected) one participant may make to this campaign.</summary>
    public int MaxSubmissionsPerParticipant { get; set; } = 1;

    /// <summary>Hours the post must stay publicly accessible before its reward becomes payable. 0 = no live check.</summary>
    public int MinPostLiveHours { get; set; }
    public bool RequireScreenshot { get; set; } = true;

    /// <summary>Participant eligibility and segmentation rules.</summary>
    public CampaignEligibility Eligibility { get; set; } = new();

    public List<CampaignPlatform> Platforms { get; set; } = new();
    public List<CampaignAsset> Assets { get; set; } = new();
    public List<CampaignDisclosure> Disclosures { get; set; } = new();

    /// <summary>Landing page content (hero headline, body, image) rendered for invitation/landing links.</summary>
    public string? LandingHeadline { get; set; }
    public string? LandingBody { get; set; }
    public string? HeroImageUrl { get; set; }

    /// <summary>Optional destination for tracking links (UTM parameters are appended per participant).</summary>
    public string? TrackingDestinationUrl { get; set; }
    public string? UtmCampaign { get; set; }

    public Guid CreatedByUserId { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public bool IsOpenForSubmissions(DateTime nowUtc) =>
        Status == CampaignStatus.Active && nowUtc >= StartsAt && nowUtc <= SubmissionDeadline;
}

/// <summary>
/// Eligibility and segmentation rules, stored as columns of the Campaign row.
/// Empty lists mean "no restriction". Null numeric values fall back to platform defaults.
/// </summary>
public class CampaignEligibility
{
    /// <summary>Minimum social account age in days. Null uses the global setting "eligibility.minAccountAgeDays".</summary>
    public int? MinAccountAgeDays { get; set; }
    public int MinFollowers { get; set; }
    public bool RequireVerifiedAccount { get; set; }
    public List<string> Countries { get; set; } = new();
    public List<string> Languages { get; set; } = new();
    public List<string> Interests { get; set; } = new();
    public List<ParticipantTier> Tiers { get; set; } = new();
}

public class CampaignPlatform
{
    public Guid CampaignId { get; set; }
    public SocialPlatform Platform { get; set; }
}

public enum CampaignAssetType
{
    Image,
    Video,
    Caption,
    Link,
    Document,
}

/// <summary>Company-approved content participants are allowed to share.</summary>
public class CampaignAsset : AuditedEntity
{
    public Guid CampaignId { get; set; }
    public CampaignAssetType Type { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Public URL of the asset (CDN or /api/v1/files/public/..). Null for caption-only assets.</summary>
    public string? Url { get; set; }
    public Guid? FileId { get; set; }

    /// <summary>Approved caption/copy text.</summary>
    public string? Body { get; set; }

    /// <summary>Null = usable on every allowed platform.</summary>
    public SocialPlatform? Platform { get; set; }
    public Guid? TemplateId { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Disclosure wording for a platform and/or country. Most specific match wins.</summary>
public class CampaignDisclosure : Entity
{
    public Guid CampaignId { get; set; }
    public SocialPlatform? Platform { get; set; }
    public string? CountryCode { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>Reusable post copy managed by campaign managers.</summary>
public class PostTemplate : AuditedEntity
{
    public string Name { get; set; } = string.Empty;
    public SocialPlatform? Platform { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? Hashtags { get; set; }
    public string? LanguageCode { get; set; }
    public bool IsArchived { get; set; }
    public Guid CreatedByUserId { get; set; }
}

public enum CalendarEntryStatus
{
    Planned,
    Scheduled,
    Published,
    Cancelled,
}

public class ContentCalendarEntry : AuditedEntity
{
    public Guid? CampaignId { get; set; }
    public Guid? TemplateId { get; set; }
    public string Title { get; set; } = string.Empty;
    public SocialPlatform? Platform { get; set; }
    public DateTime ScheduledFor { get; set; }
    public string? Notes { get; set; }
    public CalendarEntryStatus Status { get; set; } = CalendarEntryStatus.Planned;
    public Guid CreatedByUserId { get; set; }
}
