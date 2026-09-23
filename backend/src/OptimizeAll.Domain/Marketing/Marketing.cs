using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Marketing;

/// <summary>Shareable invitation link to the platform or a specific campaign landing page.</summary>
public class InvitationLink : AuditedEntity
{
    public string Code { get; set; } = string.Empty;
    public Guid? CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int UseCount { get; set; }
    public int VisitCount { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
}

public enum ReferralStatus
{
    /// <summary>Referred participant registered; waiting for the qualifying action.</summary>
    Registered,
    /// <summary>Qualifying action done; reward created (pending approval if required).</summary>
    Qualified,
    /// <summary>Blocked by fraud checks or manual review.</summary>
    Rejected,
    /// <summary>Qualifying window elapsed.</summary>
    Expired,
}

public enum ReferralQualifyingAction
{
    EmailVerified,
    FirstApprovedSubmission,
    FirstPaidPayout,
}

public class Referral : AuditedEntity, IConcurrencyStamped
{
    public Guid ReferrerUserId { get; set; }
    public Guid ReferredUserId { get; set; }
    public string CodeUsed { get; set; } = string.Empty;
    public ReferralStatus Status { get; set; } = ReferralStatus.Registered;
    public ReferralQualifyingAction QualifyingAction { get; set; } = ReferralQualifyingAction.FirstApprovedSubmission;
    public DateTime QualifyBy { get; set; }
    public DateTime? QualifiedAt { get; set; }

    /// <summary>Hashed network/device signals captured at registration, compared against the referrer's.</summary>
    public string? RegistrationIpHash { get; set; }
    public string? DeviceHash { get; set; }

    /// <summary>Comma-separated fraud signal codes (e.g. "same_ip,self_referral_email_pattern").</summary>
    public string? FraudSignals { get; set; }
    public string? RejectionReason { get; set; }
    public Guid? EarningEntryId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Per-participant tracking link with UTM parameters pointing at the campaign's destination.</summary>
public class TrackingLink : Entity
{
    public string Code { get; set; } = string.Empty;
    public Guid CampaignId { get; set; }
    public Guid? UserId { get; set; }
    public string DestinationUrl { get; set; } = string.Empty;
    public string UtmSource { get; set; } = string.Empty;
    public string UtmMedium { get; set; } = "social";
    public string UtmCampaign { get; set; } = string.Empty;
    public string? UtmContent { get; set; }
    public string? UtmTerm { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Measured click on a tracking link. IP and user agent are stored only as salted hashes.</summary>
public class TrackingClick : Entity
{
    public Guid TrackingLinkId { get; set; }
    public DateTime ClickedAt { get; set; }
    public string? VisitorHash { get; set; }
    public string? Referrer { get; set; }
    public bool IsUnique { get; set; }
    public bool IsSuspectedBot { get; set; }
}

/// <summary>Conversion reported by the advertiser (server-to-server postback). Only verified ones count as conversions.</summary>
public class TrackingConversion : Entity
{
    public Guid TrackingLinkId { get; set; }
    public string ExternalReference { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public string? Currency { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string Source { get; set; } = "postback";
}

public enum ExperimentElement
{
    Title,
    CreativeAsset,
    Instructions,
    LandingPage,
}

public enum ExperimentStatus
{
    Draft,
    Running,
    Paused,
    Completed,
}

public class Experiment : AuditedEntity, IConcurrencyStamped
{
    public Guid CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Hypothesis { get; set; }
    public ExperimentElement Element { get; set; }
    public ExperimentStatus Status { get; set; } = ExperimentStatus.Draft;
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public Guid? WinningVariantId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<ExperimentVariant> Variants { get; set; } = new();
}

public class ExperimentVariant : Entity
{
    public Guid ExperimentId { get; set; }
    public string Key { get; set; } = "A";
    public string Name { get; set; } = string.Empty;

    /// <summary>Relative traffic weight (e.g. 50/50).</summary>
    public int Weight { get; set; } = 50;

    /// <summary>Override values for the tested element: title text, asset id, instructions, landing headline/body.</summary>
    public string? Title { get; set; }
    public string? Instructions { get; set; }
    public Guid? AssetId { get; set; }
    public string? LandingHeadline { get; set; }
    public string? LandingBody { get; set; }
}

/// <summary>Sticky assignment of a participant (or anonymous visitor) to a variant.</summary>
public class ExperimentAssignment : Entity
{
    public Guid ExperimentId { get; set; }
    public Guid VariantId { get; set; }

    /// <summary>"user:{id}" or "visitor:{hash}".</summary>
    public string SubjectKey { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; }
}

public enum AchievementCriterion
{
    ApprovedSubmissions,
    CampaignsCompleted,
    TotalEarnedSettlement,
    QualifiedReferrals,
    PlatformsUsed,
}

public class Achievement : AuditedEntity
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public AchievementCriterion Criterion { get; set; }
    public decimal Threshold { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UserAchievement
{
    public Guid UserId { get; set; }
    public Guid AchievementId { get; set; }
    public DateTime AwardedAt { get; set; }
}

/// <summary>Dedup log for automated retention messages so a message kind is sent once per key.</summary>
public class RetentionMessageLog : Entity
{
    public Guid UserId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>Dedup key within the kind, e.g. campaign id for campaign alerts, "d3" for day-3 reminder.</summary>
    public string DedupKey { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}
