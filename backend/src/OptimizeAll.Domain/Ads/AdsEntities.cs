using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.SocialMedia;

namespace OptimizeAll.Domain.Ads;

public enum AdPlatform
{
    GoogleAds,
    MetaAds,
    TikTokAds,
    LinkedInAds,
    MicrosoftAds,
    SnapchatAds,
}

public enum AdAccountStatus
{
    NotConnected,
    Connected,
    Error,
    Disconnected,
}

public enum AdEntityStatus
{
    Draft,
    Active,
    Paused,
    Ended,
    Removed,
}

public enum BudgetType
{
    Daily,
    Lifetime,
}

/// <summary>How a campaign structure row came to exist.</summary>
public enum AdEntitySource
{
    /// <summary>Planned internally (naming convention enforced).</summary>
    Plan,
    /// <summary>Mirrored from the provider API.</summary>
    Synced,
    /// <summary>Created by a CSV import.</summary>
    Imported,
}

public enum AdLevel
{
    Campaign,
    AdGroup,
    Ad,
}

public enum AdMetricSource
{
    Api,
    CsvImport,
    Manual,
}

public enum AdAlertKind
{
    OverPacing,
    UnderPacing,
    CpaAboveTarget,
    RoasBelowTarget,
    SpendWithoutConversions,
}

public enum AdAlertStatus
{
    Open,
    Acknowledged,
    Resolved,
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical,
}

public enum MediaPlanStatus
{
    Draft,
    Approved,
    Archived,
}

public enum AdExperimentStatus
{
    Planned,
    Running,
    Concluded,
}

public enum AdCreativeFormat
{
    ResponsiveSearch,
    SingleImage,
    Video,
    Carousel,
    Text,
}

public static class AdMetricSources
{
    /// <summary>Api and CsvImport (a platform export) are "Measured"; Manual entries are "Manual".</summary>
    public static string Label(AdMetricSource source) => source == AdMetricSource.Manual ? "Manual" : "Measured";
}

public class AdAccount : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public AdPlatform Platform { get; set; }

    /// <summary>Provider account id: Google Ads customer id (digits only), Meta "act_…" id, TikTok advertiser id, ...</summary>
    public string ExternalAccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public string TimeZone { get; set; } = "UTC";
    public AdAccountStatus Status { get; set; } = AdAccountStatus.NotConnected;
    public string? StatusMessage { get; set; }

    /// <summary>Encrypted credentials row (integration_connections) for this account, when connected individually.</summary>
    public Guid? IntegrationConnectionId { get; set; }

    /// <summary>Staff member responsible for the account (receives alerts with the client's account manager).</summary>
    public Guid? ManagerUserId { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastSyncMessage { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Per-client ads settings: campaign naming template and UTM defaults.</summary>
public class AdsClientSettings : IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }

    /// <summary>E.g. "{client}_{platform}_{objective}_{yyyymm}_{name}". Tokens: see <see cref="NamingConvention"/>.</summary>
    public string? CampaignNamingTemplate { get; set; }
    public string DefaultUtmSource { get; set; } = "{platform}";
    public string DefaultUtmMedium { get; set; } = "cpc";
    public bool LowercaseUtm { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class AdCampaign : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public Guid AdAccountId { get; set; }
    public string? ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Objective { get; set; }
    public AdEntityStatus Status { get; set; } = AdEntityStatus.Draft;
    public BudgetType BudgetType { get; set; } = BudgetType.Daily;
    public decimal? BudgetAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? BidStrategy { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? TargetingSummary { get; set; }
    public decimal? TargetCpa { get; set; }
    public decimal? TargetRoas { get; set; }
    public AdEntitySource Source { get; set; }
    public bool NamingCompliant { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class AdGroup : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public Guid AdAccountId { get; set; }
    public Guid CampaignId { get; set; }
    public string? ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public AdEntityStatus Status { get; set; } = AdEntityStatus.Draft;
    public decimal? BudgetAmount { get; set; }
    public string? BidStrategy { get; set; }
    public string? TargetingSummary { get; set; }
    public AdEntitySource Source { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class Ad : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public Guid AdAccountId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid AdGroupId { get; set; }
    public string? ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public AdEntityStatus Status { get; set; } = AdEntityStatus.Draft;
    public Guid? CreativeId { get; set; }
    public AdEntitySource Source { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Daily metrics of one campaign / ad group / ad, in the ad account's currency (the original values are never converted
/// in place; reports convert on read). Unique by (account, date, level, entity key), so re-imports and re-syncs update.
/// </summary>
public class AdDailyMetric : Entity
{
    public Guid ClientAccountId { get; set; }
    public Guid AdAccountId { get; set; }
    public AdPlatform Platform { get; set; }
    public AdLevel Level { get; set; }

    /// <summary>Provider entity id, or "name:&lt;normalized name&gt;" when the source has no ids.</summary>
    public string EntityKey { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public Guid? CampaignId { get; set; }
    public Guid? AdGroupId { get; set; }
    public Guid? AdId { get; set; }
    public DateOnly Date { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal Spend { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Conversions { get; set; }
    public decimal ConversionValue { get; set; }
    public long? Reach { get; set; }
    public long? VideoViews { get; set; }
    public AdMetricSource Source { get; set; }
    public Guid? ImportBatchId { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AdImportBatch : Entity
{
    public Guid ClientAccountId { get; set; }
    public Guid AdAccountId { get; set; }
    public string Template { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public int RowsTotal { get; set; }
    public int RowsImported { get; set; }
    public int RowsUpdated { get; set; }
    public int RowsSkipped { get; set; }
    public List<string> Errors { get; set; } = new();
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Monthly budget for a client, optionally narrowed to a platform or one campaign.</summary>
public class AdBudget : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }

    /// <summary>First day of the month.</summary>
    public DateOnly Month { get; set; }
    public AdPlatform? Platform { get; set; }
    public Guid? CampaignId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Alert when actual/expected spend is above this ratio (1.15 = 15% over).</summary>
    public decimal OverPacingThreshold { get; set; } = 1.15m;

    /// <summary>Alert when actual/expected spend is below this ratio (0.85 = 15% under).</summary>
    public decimal UnderPacingThreshold { get; set; } = 0.85m;
    public decimal? TargetCpa { get; set; }
    public decimal? TargetRoas { get; set; }
    public string? Notes { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class AdAlert : Entity
{
    public Guid ClientAccountId { get; set; }
    public AdAlertKind Kind { get; set; }
    public AlertSeverity Severity { get; set; }
    public Guid? BudgetId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? AdAccountId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>Unique: kind + scope + month. A condition raises one alert per scope per month.</summary>
    public string DedupeKey { get; set; } = string.Empty;
    public DateOnly EvaluatedFor { get; set; }
    public AdAlertStatus Status { get; set; } = AdAlertStatus.Open;
    public Guid? AcknowledgedByUserId { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MediaPlan : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly Month { get; set; }
    public string Currency { get; set; } = "USD";
    public MediaPlanStatus Status { get; set; } = MediaPlanStatus.Draft;
    public string? Notes { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public List<MediaPlanLine> Lines { get; set; } = new();
}

public class MediaPlanLine : Entity
{
    public Guid PlanId { get; set; }
    public Guid ClientAccountId { get; set; }
    public AdPlatform Platform { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string? Objective { get; set; }
    public decimal PlannedBudget { get; set; }
    public DateOnly FlightStart { get; set; }
    public DateOnly FlightEnd { get; set; }

    /// <summary>KPI the line is judged on: CPA, ROAS, CPC, CPM, CTR, Conversions, Clicks, Impressions.</summary>
    public string KpiName { get; set; } = "CPA";
    public decimal? KpiTarget { get; set; }
    public long? PlannedImpressions { get; set; }
    public long? PlannedClicks { get; set; }
    public decimal? PlannedConversions { get; set; }
}

/// <summary>Ad copy and creative: headlines/descriptions validated per platform, linked media, approval workflow.</summary>
public class AdCreative : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public AdPlatform Platform { get; set; }
    public AdCreativeFormat Format { get; set; }
    public List<string> Headlines { get; set; } = new();
    public List<string> Descriptions { get; set; } = new();
    public string? PrimaryText { get; set; }
    public string? CallToAction { get; set; }
    public string? FinalUrl { get; set; }

    /// <summary>Linked media from the client's library (social media assets).</summary>
    public List<Guid> MediaAssetIds { get; set; } = new();
    public Guid? CampaignId { get; set; }

    /// <summary>Uses the social approval states (Draft, InternalReview, ClientApproval, Approved).</summary>
    public SocialPostStatus Status { get; set; } = SocialPostStatus.Draft;
    public string? ReviewNote { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class AdExperiment : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public Guid? AdAccountId { get; set; }
    public Guid? CampaignId { get; set; }
    public AdPlatform Platform { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Hypothesis { get; set; } = string.Empty;

    /// <summary>What is compared: "ConversionRate" (conversions/clicks) or "Ctr" (clicks/impressions).</summary>
    public string Metric { get; set; } = "ConversionRate";
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public AdExperimentStatus Status { get; set; } = AdExperimentStatus.Planned;
    public string? Result { get; set; }
    public string? WinnerVariant { get; set; }

    /// <summary>p-value entered from the ad platform's own experiment report (overrides the computed one).</summary>
    public decimal? EnteredPValue { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public List<AdExperimentVariant> Variants { get; set; } = new();
}

public class AdExperimentVariant : Entity
{
    public Guid ExperimentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsControl { get; set; }
    public string? Description { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Conversions { get; set; }
    public decimal Spend { get; set; }
}

/// <summary>A tagged URL produced by the UTM builder (kept as history for reuse).</summary>
public class AdUtmLink : Entity
{
    public Guid ClientAccountId { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public string Campaign { get; set; } = string.Empty;
    public string? Term { get; set; }
    public string? Content { get; set; }
    public string TaggedUrl { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
