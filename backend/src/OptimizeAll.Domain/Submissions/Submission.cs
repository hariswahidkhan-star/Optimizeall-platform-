using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Submissions;

public enum SubmissionStatus
{
    Pending,
    UnderReview,
    Approved,
    NeedsCorrection,
    Rejected,
    /// <summary>Previously approved, later reversed (e.g. post removed, fraud); earnings reversed in the ledger.</summary>
    Reversed,
}

public enum LiveCheckStatus
{
    /// <summary>Campaign has no minimum live duration.</summary>
    NotRequired,
    /// <summary>Waiting for the live duration to elapse and a reviewer to confirm the post is still public.</summary>
    Pending,
    ConfirmedLive,
    Removed,
}

/// <summary>Proof that a participant shared campaign content. A screenshot is review evidence, not proof on its own.</summary>
public class Submission : AuditedEntity, IConcurrencyStamped
{
    public Guid CampaignId { get; set; }
    public Guid UserId { get; set; }
    public Guid SocialAccountId { get; set; }
    public SocialPlatform Platform { get; set; }

    public string PostUrl { get; set; } = string.Empty;

    /// <summary>
    /// Canonical <b>post key</b> (see <see cref="PlatformUrlRules.Parse"/>), e.g. <c>instagram:Cabc123</c>,
    /// <c>youtube:dQw4w9WgXcQ</c>, or a normalized URL when the post id can't be read from the link. The column keeps its
    /// historical name. Globally unique and compared case-sensitively (utf8mb4_bin): a post can only be claimed once.
    /// </summary>
    public string NormalizedPostUrl { get; set; } = string.Empty;

    /// <summary>When the participant says the post went live (UTC).</summary>
    public DateTime PostedAt { get; set; }
    public string? CaptionText { get; set; }
    public string? ContentHash { get; set; }

    public Guid? ScreenshotFileId { get; set; }
    public string? ScreenshotSha256 { get; set; }

    public SubmissionStatus Status { get; set; } = SubmissionStatus.Pending;
    public DateTime SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public Guid? DecidedByUserId { get; set; }

    /// <summary>Latest rejection / correction / reversal reason visible to the participant.</summary>
    public string? DecisionReason { get; set; }
    public int CorrectionCount { get; set; }

    /// <summary>Reward rule version captured at submission time; earnings are always computed from this version.</summary>
    public Guid RewardRuleSetId { get; set; }
    public int RewardRuleSetVersion { get; set; }
    public decimal EstimatedRewardAmount { get; set; }
    public string RewardCurrency { get; set; } = "USD";

    /// <summary>Sum of flag weights; high scores are routed to senior review.</summary>
    public int RiskScore { get; set; }

    public Guid? AssignedReviewerId { get; set; }

    /// <summary>A reviewer's claim on the submission; other reviewers cannot decide while it is held.</summary>
    public Guid? ClaimedByUserId { get; set; }
    public DateTime? ClaimExpiresAt { get; set; }

    public LiveCheckStatus LiveCheckStatus { get; set; } = LiveCheckStatus.NotRequired;
    public DateTime? LiveCheckDueAt { get; set; }
    public DateTime? LiveCheckedAt { get; set; }
    public Guid? LiveCheckedByUserId { get; set; }

    /// <summary>A/B variant the participant saw when opening the campaign, for experiment attribution.</summary>
    public Guid? ExperimentVariantId { get; set; }

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<SubmissionFlag> Flags { get; set; } = new();
    public List<SubmissionEvent> Events { get; set; } = new();

    public bool IsOpenForReview => Status is SubmissionStatus.Pending or SubmissionStatus.UnderReview;
}

public enum SubmissionFlagType
{
    DuplicateScreenshot,
    RepeatedContent,
    OutsideCampaignWindow,
    AfterSubmissionDeadline,
    AccountBelowMinimumAge,
    AccountNotVerified,
    PlatformMismatch,
    UrlHostMismatch,
    HighSubmissionVelocity,
    NewParticipant,
    SharedDeviceOrIp,
    ReferralFraudSuspected,
    /// <summary>The declared post time is more than 48 hours before the submission.</summary>
    PostedLongBeforeSubmission,
    /// <summary>The URL is a short link (e.g. vm.tiktok.com) that a reviewer must open to identify the post.</summary>
    UnresolvedShortLink,
    Other,
}

public class SubmissionFlag : Entity
{
    public Guid SubmissionId { get; set; }
    public SubmissionFlagType Type { get; set; }
    public string Detail { get; set; } = string.Empty;
    public int Weight { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionNote { get; set; }
}

/// <summary>Append-only status history of a submission.</summary>
public class SubmissionEvent : Entity
{
    public Guid SubmissionId { get; set; }
    public SubmissionStatus? FromStatus { get; set; }
    public SubmissionStatus ToStatus { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum AppealStatus
{
    Open,
    /// <summary>Original decision stands.</summary>
    Upheld,
    /// <summary>Original decision reversed in the participant's favour.</summary>
    Overturned,
    Withdrawn,
}

public class Appeal : AuditedEntity, IConcurrencyStamped
{
    public Guid SubmissionId { get; set; }
    public Guid UserId { get; set; }
    public SubmissionStatus DecisionAppealed { get; set; }
    public string Reason { get; set; } = string.Empty;
    public AppealStatus Status { get; set; } = AppealStatus.Open;
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
