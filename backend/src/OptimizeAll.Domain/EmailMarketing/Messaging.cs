using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.EmailMarketing;

/// <summary>
/// A block-based email template. Agency templates (<c>ClientAccountId = null</c>) form the global library every
/// workspace can start from; client templates belong to one client.
/// </summary>
public class EmailTemplate : AuditedEntity, IConcurrencyStamped
{
    public Guid? ClientAccountId { get; set; }
    public string ScopeKey { get; set; } = Workspace.AgencyKey;
    public string Name { get; set; } = string.Empty;
    /// <summary>welcome, newsletter, promo, abandoned-cart, re-engagement, event, nps, transactional, other.</summary>
    public string Category { get; set; } = "other";
    public string Subject { get; set; } = string.Empty;
    public string? PreviewText { get; set; }
    /// <summary>JSON <see cref="EmailDesign"/>.</summary>
    public string DesignJson { get; set; } = "{}";
    public bool IsArchived { get; set; }
    /// <summary>Stable key for seeded starter templates.</summary>
    public string? SeedKey { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>A saved audience rule set (JSON <see cref="SegmentDefinition"/>), evaluated server-side.</summary>
public class Segment : AuditedEntity, IConcurrencyStamped
{
    public Guid? ClientAccountId { get; set; }
    public string ScopeKey { get; set; } = Workspace.AgencyKey;
    public string Name { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = "{}";
    public int? LastCount { get; set; }
    public DateTime? LastCountedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public enum CampaignStatus
{
    Draft,
    /// <summary>Confirmed for sending; waiting for its time (and for client approval when required).</summary>
    Scheduled,
    Sending,
    Paused,
    Sent,
    Cancelled,
}

public enum CampaignType
{
    Regular,
    AbTest,
}

public enum ScheduleMode
{
    /// <summary>As soon as the campaign is confirmed.</summary>
    Immediate,
    /// <summary>At <c>ScheduledAt</c> (UTC).</summary>
    FixedTime,
    /// <summary>At <c>ScheduledLocalTime</c> in each recipient's own time zone.</summary>
    RecipientTimeZone,
}

public enum AbWinnerMetric
{
    OpenRate,
    ClickRate,
}

public enum ApprovalStatus
{
    NotRequired,
    Pending,
    Approved,
    Rejected,
}

/// <summary>An email, SMS or WhatsApp campaign to a list and/or segment.</summary>
public class EmailCampaign : AuditedEntity, IConcurrencyStamped
{
    public Guid? ClientAccountId { get; set; }
    public string ScopeKey { get; set; } = Workspace.AgencyKey;
    public string Name { get; set; } = string.Empty;
    public MessageChannel Channel { get; set; } = MessageChannel.Email;
    public CampaignType Type { get; set; } = CampaignType.Regular;
    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;

    // Audience: a list, a segment, or both (segment evaluated within the list).
    public Guid? ListId { get; set; }
    public Guid? SegmentId { get; set; }

    // Email content (variant A for A/B tests).
    public Guid? TemplateId { get; set; }
    public Guid? SenderProfileId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? PreviewText { get; set; }
    public string DesignJson { get; set; } = "{}";
    /// <summary>Preference-center topic (list) the campaign belongs to, if any.</summary>
    public string? Topic { get; set; }

    // SMS / WhatsApp content.
    public string? SmsBody { get; set; }
    public string? WhatsAppTemplateName { get; set; }
    public string? WhatsAppTemplateLanguage { get; set; }
    /// <summary>JSON array of template body parameters (merge tags allowed).</summary>
    public string? WhatsAppParametersJson { get; set; }

    // Scheduling & throttling.
    public ScheduleMode ScheduleMode { get; set; } = ScheduleMode.Immediate;
    public DateTime? ScheduledAt { get; set; }
    /// <summary>Local wall-clock time "yyyy-MM-ddTHH:mm" for <see cref="ScheduleMode.RecipientTimeZone"/>.</summary>
    public string? ScheduledLocalTime { get; set; }
    /// <summary>Optional daily send window (recipient local hours, start inclusive, end exclusive).</summary>
    public int? SendWindowStartHour { get; set; }
    public int? SendWindowEndHour { get; set; }
    public int ThrottlePerMinute { get; set; } = 600;

    // A/B testing.
    public int AbTestPercent { get; set; } = 20;
    public AbWinnerMetric AbWinnerMetric { get; set; } = AbWinnerMetric.OpenRate;
    public int AbWaitHours { get; set; } = 4;
    public string? AbWinnerVariant { get; set; }
    public DateTime? AbTestCompletedAt { get; set; }
    public DateTime? AbDecidedAt { get; set; }

    // Client approval.
    public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.NotRequired;
    public Guid? ApprovalDecidedByUserId { get; set; }
    public DateTime? ApprovalDecidedAt { get; set; }
    public string? ApprovalNote { get; set; }

    // Lifecycle.
    public Guid? CreatedByUserId { get; set; }
    public Guid? SendConfirmedByUserId { get; set; }
    public DateTime? SendConfirmedAt { get; set; }
    public DateTime? SendStartedAt { get; set; }
    public DateTime? ExpandedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? PausedAt { get; set; }
    public string? PauseReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int RecipientCount { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>An A/B variant. Unset fields inherit the campaign's (variant A = the campaign content itself).</summary>
public class CampaignVariant : Entity
{
    public Guid CampaignId { get; set; }
    /// <summary>"A", "B", "C".</summary>
    public string Key { get; set; } = "A";
    public string? Subject { get; set; }
    public string? PreviewText { get; set; }
    public Guid? SenderProfileId { get; set; }
    public string? DesignJson { get; set; }
}

public enum RecipientStatus
{
    /// <summary>A/B remainder waiting for the winner.</summary>
    Held,
    Pending,
    /// <summary>Claimed by a worker; the provider call is in flight.</summary>
    Sending,
    Sent,
    Failed,
    /// <summary>Not sent by design (suppressed, no consent, frequency cap, invalid address) — never retried.</summary>
    Skipped,
    Cancelled,
}

public enum BounceType
{
    Soft,
    Hard,
}

/// <summary>
/// One recipient of a campaign. Unique per (campaign, subscriber) so a subscriber can never be sent the same campaign
/// twice, whatever retries or concurrent workers do; sending claims the row with a conditional update first.
/// </summary>
public class CampaignRecipient : Entity
{
    public Guid CampaignId { get; set; }
    public Guid? ClientAccountId { get; set; }
    public Guid SubscriberId { get; set; }
    public MessageChannel Channel { get; set; }
    /// <summary>A/B variant key; null for regular campaigns.</summary>
    public string? Variant { get; set; }
    /// <summary>True for the A/B test cohort.</summary>
    public bool IsTestCohort { get; set; }
    /// <summary>Email or E.164 phone snapshot at expansion.</summary>
    public string Address { get; set; } = string.Empty;
    public RecipientStatus Status { get; set; } = RecipientStatus.Pending;
    public string? SkipReason { get; set; }
    public DateTime? DueAt { get; set; }
    public int Attempts { get; set; }
    public Guid? ClaimId { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public BounceType? BounceType { get; set; }
    public DateTime? BouncedAt { get; set; }
    /// <summary>First human (non-machine) open.</summary>
    public DateTime? OpenedAt { get; set; }
    public int OpenCount { get; set; }
    public int MachineOpenCount { get; set; }
    public DateTime? ClickedAt { get; set; }
    public int ClickCount { get; set; }
    public DateTime? UnsubscribedAt { get; set; }
    public DateTime? ComplainedAt { get; set; }
    /// <summary>First attributed conversion (revenue is summed from the conversion events).</summary>
    public DateTime? ConvertedAt { get; set; }
    /// <summary>SMS segments billed for this message.</summary>
    public int Segments { get; set; }
    public decimal Cost { get; set; }
}

/// <summary>
/// A link that appeared in sent content. Click tokens reference a link id, so the click redirect can only ever go to a
/// URL that is part of the message (no open redirect). <see cref="Url"/> may contain merge tags resolved per contact.
/// </summary>
public class TrackedLink : Entity
{
    public Guid? ClientAccountId { get; set; }
    /// <summary>"c:{campaignId}" or "a:{automationId}:{stepKey}".</summary>
    public string SourceKey { get; set; } = string.Empty;
    /// <summary>SHA-256 of the URL (uniqueness per source without indexing a long column).</summary>
    public string UrlHash { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum EngagementType
{
    Sent,
    Delivered,
    Open,
    Click,
    Bounce,
    Complaint,
    Unsubscribe,
    Conversion,
    /// <summary>A custom event posted through the event API (e.g. "cart_abandoned").</summary>
    Custom,
}

public enum DeviceType
{
    Unknown,
    Desktop,
    Mobile,
    Tablet,
}

/// <summary>An engagement or delivery event (opens, clicks, bounces, conversions, custom events).</summary>
public class EngagementEvent : Entity
{
    public Guid? ClientAccountId { get; set; }
    public Guid SubscriberId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? RecipientId { get; set; }
    public Guid? AutomationId { get; set; }
    public Guid? AutomationStepRunId { get; set; }
    public Guid? LinkId { get; set; }
    public EngagementType Type { get; set; }
    public DateTime OccurredAt { get; set; }
    /// <summary>Machine open/click (Apple Mail Privacy Protection proxy, security scanner, prefetch).</summary>
    public bool IsMachine { get; set; }
    public DeviceType Device { get; set; }
    public string? MailClient { get; set; }
    public string? IpHash { get; set; }
    /// <summary>Custom event name.</summary>
    public string? Name { get; set; }
    public decimal? Value { get; set; }
    public string? Currency { get; set; }
    public string? ExternalReference { get; set; }
    public string? Provider { get; set; }
    public string? Detail { get; set; }
    /// <summary>Idempotency key (provider event id, conversion reference); unique when set.</summary>
    public string? DedupKey { get; set; }
}
