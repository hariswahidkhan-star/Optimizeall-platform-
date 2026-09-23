using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.SocialMedia;

/// <summary>Networks a client brand can publish to. Distinct from <c>SocialPlatform</c> (participant sharing).</summary>
public enum SocialNetwork
{
    Facebook,
    Instagram,
    X,
    LinkedIn,
    TikTok,
    YouTube,
    Pinterest,
    GoogleBusiness,
}

/// <summary>Stored connection state of a brand profile. "App credentials required" is derived from configuration, not stored.</summary>
public enum ProfileConnectionStatus
{
    NotConnected,
    Connected,
    /// <summary>The provider rejected the token (expired or revoked) or a verification failed.</summary>
    Error,
    Disconnected,
}

/// <summary>
/// Content workflow shared by social posts and ad creatives:
/// Draft → InternalReview → ClientApproval (optional per client) → Approved → Scheduled → Publishing → Published / Failed.
/// </summary>
public enum SocialPostStatus
{
    Draft,
    InternalReview,
    ClientApproval,
    Approved,
    Scheduled,
    Publishing,
    Published,
    Failed,
}

/// <summary>Publishing state of one network variant of a post.</summary>
public enum VariantPublishStatus
{
    Pending,
    Publishing,
    Published,
    Failed,
}

/// <summary>Why a variant failed (drives retry and UI hints).</summary>
public enum PublishFailureKind
{
    None,
    /// <summary>No credentials / adapter not configured: use the manual workflow.</summary>
    NotConfigured,
    /// <summary>The adapter cannot do this (e.g. media type or network without an API).</summary>
    NotSupported,
    /// <summary>Token expired or revoked; reconnect the profile.</summary>
    Authorization,
    /// <summary>The provider rejected the content (validation).</summary>
    Rejected,
    /// <summary>Network/5xx/rate limit; retried with backoff.</summary>
    Transient,
    /// <summary>The job was interrupted after the request was sent; the outcome must be verified by a person.</summary>
    Unknown,
}

public enum MediaKind
{
    Image,
    Video,
}

/// <summary>Where a metric came from. Api and PlatformExport are labelled "Measured"; Manual is labelled "Manual".</summary>
public enum MetricSource
{
    Api,
    PlatformExport,
    Manual,
}

public enum ListeningQueryKind
{
    Keyword,
    Hashtag,
    CompetitorHandle,
}

public enum Sentiment
{
    Positive,
    Neutral,
    Negative,
}

public enum SentimentSource
{
    Manual,
    /// <summary>Lexicon-based automatic estimate (always labelled as such in the UI).</summary>
    Automatic,
}

public enum IngestSource
{
    Manual,
    Api,
}

public enum InboxItemKind
{
    Comment,
    DirectMessage,
    Mention,
}

public enum InboxItemStatus
{
    Open,
    Assigned,
    Replied,
    Closed,
}

public enum PostCommentKind
{
    Comment,
    Submitted,
    Approved,
    ChangesRequested,
    Scheduled,
    Published,
    Failed,
    System,
}

public static class MetricSources
{
    public static string Label(MetricSource source) => source == MetricSource.Manual ? "Manual" : "Measured";
}

/// <summary>A client brand's account on a network (Facebook Page, Instagram Business, X, ...).</summary>
public class BrandProfile : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public SocialNetwork Network { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ProfileUrl { get; set; }
    public string? AvatarUrl { get; set; }

    /// <summary>Provider id used by the API (Facebook Page id, Instagram business user id, X user id, ...).</summary>
    public string? ExternalId { get; set; }
    public ProfileConnectionStatus ConnectionStatus { get; set; } = ProfileConnectionStatus.NotConnected;
    public string? StatusMessage { get; set; }

    /// <summary>Encrypted token row (integration_connections) holding this profile's access token.</summary>
    public Guid? IntegrationConnectionId { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public DateTime? ConnectedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Per-client social settings.</summary>
public class SocialClientSettings : IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }

    /// <summary>When true, posts go InternalReview → ClientApproval → Approved; otherwise InternalReview → Approved.</summary>
    public bool RequireClientApproval { get; set; }
    public string DefaultUtmMedium { get; set; } = "social";
    public DateTime UpdatedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>A marketing campaign grouping posts, with the UTM settings used by the UTM auto-append option.</summary>
public class SocialCampaign : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UtmCampaign { get; set; } = string.Empty;
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmContent { get; set; }
    public string? UtmTerm { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>A post: one piece of content with a variant per target profile/network.</summary>
public class SocialPost : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public string Title { get; set; } = string.Empty;
    public SocialPostStatus Status { get; set; } = SocialPostStatus.Draft;

    /// <summary>Planned time while drafting; the publish time once Scheduled (UTC).</summary>
    public DateTime? ScheduledAt { get; set; }
    public Guid? CampaignId { get; set; }
    public bool AutoAppendUtm { get; set; }

    public bool IsEvergreen { get; set; }
    public int EvergreenIntervalDays { get; set; } = 30;
    public int EvergreenMaxRepeats { get; set; } = 3;
    public int EvergreenRepeatCount { get; set; }

    /// <summary>Evergreen copy: the post it was recycled from and its repeat number (unique together).</summary>
    public Guid? RecycledFromPostId { get; set; }
    public int? RecycleNumber { get; set; }

    /// <summary>Earliest time the publishing job may (re)try this post.</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>Claim token written by the conditional update that moves a post to Publishing.</summary>
    public Guid? PublishClaimId { get; set; }
    public DateTime? PublishClaimedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? FailureReason { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<SocialPostVariant> Variants { get; set; } = new();
}

public class SocialPostVariant : Entity
{
    public Guid PostId { get; set; }
    public Guid ClientAccountId { get; set; }
    public Guid ProfileId { get; set; }
    public SocialNetwork Network { get; set; }
    public string Text { get; set; } = string.Empty;

    /// <summary>YouTube video title / Pinterest pin title.</summary>
    public string? Title { get; set; }
    public List<Guid> MediaIds { get; set; } = new();
    public List<string> AltTexts { get; set; } = new();
    public string? Link { get; set; }
    public string? FirstComment { get; set; }
    public List<string> Hashtags { get; set; } = new();
    public List<string> Mentions { get; set; } = new();

    public VariantPublishStatus PublishStatus { get; set; } = VariantPublishStatus.Pending;
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public PublishFailureKind FailureKind { get; set; }
    public string? FailureReason { get; set; }
    public string? ExternalPostId { get; set; }
    public string? PublishedUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public bool PublishedManually { get; set; }
    public Guid? MarkedPublishedByUserId { get; set; }
}

/// <summary>Approver comments and workflow history of a post.</summary>
public class SocialPostComment : Entity
{
    public Guid PostId { get; set; }
    public Guid ClientAccountId { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public bool IsClient { get; set; }

    /// <summary>Internal notes are never shown in the client portal.</summary>
    public bool IsInternal { get; set; }
    public PostCommentKind Kind { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>One publish attempt of a variant (publishing log).</summary>
public class SocialPublishAttempt : Entity
{
    public Guid PostId { get; set; }
    public Guid VariantId { get; set; }
    public Guid ClientAccountId { get; set; }
    public SocialNetwork Network { get; set; }
    public int AttemptNumber { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public PublishFailureKind FailureKind { get; set; }
    public string? Message { get; set; }
    public string? ExternalPostId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public Guid? ActorUserId { get; set; }
}

/// <summary>Client media library item: an uploaded image (validated like the Files module) or an https URL.</summary>
public class SocialMediaAsset : AuditedEntity
{
    public Guid ClientAccountId { get; set; }
    public MediaKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? FileId { get; set; }
    public string? ExternalUrl { get; set; }
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? DurationSeconds { get; set; }
    public string? AltText { get; set; }
    public List<string> Tags { get; set; } = new();

    /// <summary>Reachable without a session (required by the Instagram/Facebook APIs, which fetch media by URL).</summary>
    public bool IsPublic { get; set; }
    public Guid CreatedByUserId { get; set; }
}

public class SocialHashtagSet : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Hashtags { get; set; } = new();
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class SocialCaptionSnippet : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Weekly queue slot for a profile, in the client's time zone.</summary>
public class SocialQueueSlot : Entity
{
    public Guid ClientAccountId { get; set; }
    public Guid ProfileId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>Minutes after local midnight (0–1439).</summary>
    public int MinuteOfDay { get; set; }
}

/// <summary>Editable per-network best-practice preset (seeded by the Baseline profile).</summary>
public class SocialNetworkPreset
{
    public SocialNetwork Network { get; set; }
    public int MaxTextLength { get; set; }
    public int? MaxTitleLength { get; set; }
    public int MaxHashtags { get; set; }
    public int? RecommendedHashtags { get; set; }
    public int MaxMentions { get; set; }
    public int MaxMedia { get; set; }
    public int MaxVideos { get; set; }
    public bool RequiresMedia { get; set; }
    public bool RequiresVideo { get; set; }
    public bool AllowsMixedMedia { get; set; }
    public decimal? MinAspectRatio { get; set; }
    public decimal? MaxAspectRatio { get; set; }
    public int? MaxVideoSeconds { get; set; }
    public int? MinVideoSeconds { get; set; }
    public long? MaxImageBytes { get; set; }
    public int MaxAltTextLength { get; set; }
    public bool SupportsFirstComment { get; set; }
    public LinkHandling LinkHandling { get; set; }

    /// <summary>Recommended local posting times as "Mon 09:00" entries.</summary>
    public List<string> RecommendedTimes { get; set; } = new();
    public string Source { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public enum LinkHandling
{
    /// <summary>Link is sent as a separate share attachment (Facebook, LinkedIn); not counted in text.</summary>
    Attachment,
    /// <summary>Link is appended to the text and counted with a fixed weight (X counts every URL as 23).</summary>
    InText,
    /// <summary>Links in captions are not clickable (Instagram, TikTok): warn and suggest link in bio.</summary>
    NotClickable,
    /// <summary>Destination URL field (Pinterest pin link, Google Business call-to-action).</summary>
    Destination,
}

/// <summary>Holiday or awareness day shown on the content calendar (seeded list, each with its source).</summary>
public class SocialAwarenessDay : Entity
{
    public int Month { get; set; }
    public int Day { get; set; }

    /// <summary>Null = every year; otherwise only that year (moveable dates).</summary>
    public int? Year { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>ISO country codes, or empty for global observances.</summary>
    public List<string> Countries { get; set; } = new();
    public string SourceUrl { get; set; } = string.Empty;
}

public class SocialListeningQuery : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public ListeningQueryKind Kind { get; set; }
    public string Term { get; set; } = string.Empty;
    public List<SocialNetwork> Networks { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class SocialMention : AuditedEntity
{
    public Guid ClientAccountId { get; set; }
    public Guid? QueryId { get; set; }
    public SocialNetwork Network { get; set; }
    public string AuthorHandle { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Url { get; set; }
    public DateTime PostedAt { get; set; }
    public Sentiment? Sentiment { get; set; }
    public SentimentSource? SentimentSource { get; set; }
    public decimal? SentimentScore { get; set; }
    public IngestSource Source { get; set; }

    /// <summary>Provider id (or a hash of the URL) used to de-duplicate ingested mentions.</summary>
    public string DedupeKey { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
}

public class SocialInboxItem : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public Guid? ProfileId { get; set; }
    public SocialNetwork Network { get; set; }
    public InboxItemKind Kind { get; set; }
    public string AuthorHandle { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Url { get; set; }
    public DateTime ReceivedAt { get; set; }
    public InboxItemStatus Status { get; set; } = InboxItemStatus.Open;
    public Guid? AssignedToUserId { get; set; }
    public Sentiment? Sentiment { get; set; }
    public IngestSource Source { get; set; }
    public string DedupeKey { get; set; } = string.Empty;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class SocialInboxReply : Entity
{
    public Guid ItemId { get; set; }
    public Guid ClientAccountId { get; set; }
    public string Body { get; set; } = string.Empty;

    /// <summary>True only when a provider accepted the reply; otherwise it is a log of a reply sent by hand.</summary>
    public bool SentViaApi { get; set; }
    public string? ExternalId { get; set; }
    public Guid ByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Per-post metrics snapshot (lifetime totals as of <see cref="Date"/>).</summary>
public class SocialPostMetric : Entity
{
    public Guid ClientAccountId { get; set; }
    public Guid ProfileId { get; set; }
    public SocialNetwork Network { get; set; }
    public Guid? PostId { get; set; }
    public Guid? VariantId { get; set; }

    /// <summary>Provider post id or post URL; identifies the post in imports.</summary>
    public string PostKey { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public DateTime? PublishedAt { get; set; }
    public long Impressions { get; set; }
    public long Reach { get; set; }
    public long Engagements { get; set; }
    public long Clicks { get; set; }
    public long VideoViews { get; set; }
    public MetricSource Source { get; set; }
    public Guid? ImportBatchId { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Daily profile metrics.</summary>
public class SocialProfileMetric : Entity
{
    public Guid ClientAccountId { get; set; }
    public Guid ProfileId { get; set; }
    public DateOnly Date { get; set; }
    public long Followers { get; set; }
    public long Impressions { get; set; }
    public long Reach { get; set; }
    public long Engagements { get; set; }
    public long Clicks { get; set; }
    public long VideoViews { get; set; }
    public MetricSource Source { get; set; }
    public Guid? ImportBatchId { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SocialMetricImport : Entity
{
    public Guid ClientAccountId { get; set; }
    public Guid ProfileId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public MetricSource Source { get; set; }
    public int RowsTotal { get; set; }
    public int RowsImported { get; set; }
    public int RowsUpdated { get; set; }
    public int RowsSkipped { get; set; }
    public List<string> Errors { get; set; } = new();
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SocialCompetitor : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public SocialNetwork Network { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string? ProfileUrl { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class SocialCompetitorSnapshot : Entity
{
    public Guid CompetitorId { get; set; }
    public Guid ClientAccountId { get; set; }
    public DateOnly Date { get; set; }
    public long Followers { get; set; }

    /// <summary>Average engagement rate of recent posts as a ratio (0.034 = 3.4%).</summary>
    public decimal? EngagementRate { get; set; }
    public int? PostsLast30Days { get; set; }
    public MetricSource Source { get; set; }
    public DateTime UpdatedAt { get; set; }
}
