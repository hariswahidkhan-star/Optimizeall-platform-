using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;

namespace OptimizeAll.Api.Modules.Review;

public enum ReviewDecision
{
    Approve,
    RequestCorrection,
    Reject,
}

public enum LiveCheckResult
{
    ConfirmedLive,
    Removed,
}

public enum AppealOutcome
{
    Upheld,
    Overturned,
}

public sealed class ReviewQueueQuery : PageQuery
{
    /// <summary>Pending or UnderReview; default both.</summary>
    public SubmissionStatus? Status { get; set; }
    public Guid? CampaignId { get; set; }
    public SocialPlatform? Platform { get; set; }

    [Range(0, 10000)]
    public int? MinRisk { get; set; }

    /// <summary>true = only submissions with unresolved flags; false = only unflagged.</summary>
    public bool? Flagged { get; set; }
    public bool AssignedToMe { get; set; }
}

public sealed class DecisionRequest
{
    [Required]
    public ReviewDecision? Decision { get; set; }

    /// <summary>Required (5+ characters after trimming) unless approving.</summary>
    [MaxLength(ReasonText.MaxInputLength)]
    public string? Reason { get; set; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal? QualityBonusAmount { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class LiveCheckRequest
{
    [Required]
    public LiveCheckResult? Result { get; set; }

    /// <summary>Required (5+ characters after trimming) when the result is Removed.</summary>
    [MaxLength(ReasonText.MaxInputLength)]
    public string? Note { get; set; }
}

public sealed class ReverseRequest
{
    /// <summary>At least 5 characters after trimming (400 review.reason_too_short).</summary>
    [Required, MaxLength(ReasonText.MaxInputLength)]
    public string Reason { get; set; } = string.Empty;

    public bool Confirm { get; set; }
}

public sealed class ResolveAppealRequest
{
    [Required]
    public AppealOutcome? Outcome { get; set; }

    /// <summary>At least 5 characters after trimming (400 review.reason_too_short).</summary>
    [Required, MaxLength(ReasonText.MaxInputLength)]
    public string Note { get; set; } = string.Empty;

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class AssignRequest
{
    [Required, MinLength(1), MaxLength(200)]
    public List<Guid> SubmissionIds { get; set; } = new();

    [Required]
    public Guid? ReviewerId { get; set; }
}

public sealed class LiveCheckQuery : PageQuery
{
    /// <summary>true (default) = only checks whose due time has passed.</summary>
    public bool Due { get; set; } = true;
}

public sealed class AppealQuery : PageQuery
{
    public AppealStatus? Status { get; set; } = AppealStatus.Open;
}

public sealed record PersonRefDto(Guid Id, string DisplayName);

public sealed record QueueCampaignDto(Guid Id, string Title);

public sealed record QueueParticipantDto(Guid Id, string DisplayName, string CountryCode);

public sealed record ReviewQueueItemDto(
    Guid Id, QueueCampaignDto Campaign, QueueParticipantDto Participant, SocialPlatform Platform, string Handle,
    SubmissionStatus Status, DateTime SubmittedAt, int RiskScore, IReadOnlyList<SubmissionFlagType> FlagTypes,
    PersonRefDto? ClaimedBy, DateTime? ClaimExpiresAt, PersonRefDto? AssignedReviewer, int CorrectionCount);

public sealed record ClaimDto(Guid SubmissionId, SubmissionStatus Status, PersonRefDto ClaimedBy, DateTime ClaimExpiresAt, Guid ConcurrencyStamp);

public sealed record RequirementsDto(
    Guid CampaignId, string CampaignTitle, string PostingInstructions, string? RequiredHashtags, string? RequiredMentions,
    string DisclosureText, IReadOnlyList<SocialPlatform> AllowedPlatforms, DateTime StartsAt, DateTime EndsAt,
    DateTime SubmissionDeadline, int MinPostLiveHours, bool RequireScreenshot);

public sealed record ReviewClaimInfoDto(PersonRefDto? ClaimedBy, DateTime? ClaimExpiresAt, bool IsMine, bool IsActive);

public sealed record ReviewSubmissionDto(
    Guid Id, string PostUrl, string NormalizedPostUrl, SocialPlatform Platform, DateTime PostedAt, string? CaptionText,
    string? ScreenshotUrl, string? ScreenshotSha256, DateTime SubmittedAt, SubmissionStatus Status, int CorrectionCount,
    int RiskScore, int RewardRuleSetVersion, decimal EstimatedReward, string Currency, DateTime? DecidedAt, PersonRefDto? DecidedBy,
    string? DecisionReason, LiveCheckStatus LiveCheckStatus, DateTime? LiveCheckDueAt, PersonRefDto? AssignedReviewer,
    Guid ConcurrencyStamp, ReviewClaimInfoDto Claim);

public sealed record ReviewAccountDto(
    Guid Id, string Handle, string ProfileUrl, SocialPlatform Platform, DateTime AccountCreatedAt, int AccountAgeDays,
    int FollowerCount, SocialAccountVerificationStatus VerificationStatus, bool IsActive);

public sealed record ReviewParticipantDto(
    Guid Id, string DisplayName, string Email, string CountryCode, ParticipantTier Tier, DateTime JoinedAt,
    int ApprovedCount, int RejectedCount, int ReversedCount);

public sealed record HistoryItemDto(Guid Id, QueueCampaignDto Campaign, SubmissionStatus Status, DateTime SubmittedAt, string PostUrl);

public sealed record FlagDto(Guid Id, SubmissionFlagType Type, string Detail, int Weight, DateTime CreatedAt, bool Resolved,
    DateTime? ResolvedAt, string? ResolutionNote);

public sealed record RelatedSubmissionDto(Guid Id, string Match, QueueCampaignDto Campaign, PersonRefDto Participant, SubmissionStatus Status,
    DateTime SubmittedAt);

public sealed record ReviewEventDto(string Action, SubmissionStatus? FromStatus, SubmissionStatus ToStatus, string? Reason,
    PersonRefDto? Actor, DateTime At);

public sealed record ReviewAppealDto(Guid Id, AppealStatus Status, SubmissionStatus DecisionAppealed, string Reason, DateTime CreatedAt,
    PersonRefDto? ResolvedBy, string? ResolutionNote, DateTime? ResolvedAt, Guid ConcurrencyStamp);

public sealed record ReviewEarningDto(Guid Id, EarningType Type, decimal Amount, string Currency, EarningStatus Status, DateTime CreatedAt,
    int? RewardRuleSetVersion);

public sealed record ReviewDetailDto(
    RequirementsDto Requirements, ReviewSubmissionDto Submission, ReviewAccountDto Account, ReviewParticipantDto Participant,
    IReadOnlyList<HistoryItemDto> History, IReadOnlyList<FlagDto> Flags, IReadOnlyList<RelatedSubmissionDto> RelatedSubmissions,
    IReadOnlyList<ReviewEventDto> Events, RewardQuoteDto? RewardQuote, IReadOnlyList<ReviewEarningDto> Earnings,
    IReadOnlyList<ReviewAppealDto> Appeals);

public sealed record DecisionResultDto(
    Guid SubmissionId, SubmissionStatus Status, DateTime DecidedAt, string? DecisionReason, Guid ConcurrencyStamp,
    RewardQuoteDto? Reward, IReadOnlyList<ReviewEarningDto> Earnings, LiveCheckStatus LiveCheckStatus, DateTime? LiveCheckDueAt);

public sealed record LiveCheckItemDto(Guid SubmissionId, QueueCampaignDto Campaign, PersonRefDto Participant, SocialPlatform Platform,
    string PostUrl, DateTime PostedAt, DateTime? DecidedAt, DateTime? DueAt, bool IsDue);

public sealed record LiveCheckResultDto(Guid SubmissionId, SubmissionStatus Status, LiveCheckStatus LiveCheckStatus, IReadOnlyList<ReviewEarningDto> Earnings);

public sealed record ReverseResultDto(Guid SubmissionId, SubmissionStatus Status, IReadOnlyList<ReviewEarningDto> Earnings);

public sealed record AppealListItemDto(Guid Id, Guid SubmissionId, QueueCampaignDto Campaign, PersonRefDto Participant, AppealStatus Status,
    SubmissionStatus DecisionAppealed, string Reason, DateTime CreatedAt, PersonRefDto? OriginalDecidedBy, Guid ConcurrencyStamp);

public sealed record AppealDetailDto(ReviewAppealDto Appeal, PersonRefDto? OriginalDecidedBy, ReviewDetailDto Review);

public sealed record AppealResolutionDto(ReviewAppealDto Appeal, SubmissionStatus SubmissionStatus, IReadOnlyList<ReviewEarningDto> Earnings);

public sealed record ReviewerDto(Guid Id, string DisplayName, string Email, int AssignedOpen, int DecisionsToday);

public sealed record AssignResultDto(int Updated, IReadOnlyList<Guid> SkippedIds);

public sealed record ReviewStatsDto(
    int MyDecisionsToday, IReadOnlyDictionary<string, int> QueueByStatus, DateTime? OldestPendingSubmittedAt,
    double? OldestPendingAgeHours, int DueLiveChecks, int OpenAppeals, int ClaimedByMe);
