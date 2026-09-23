using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Submissions;

namespace OptimizeAll.Api.Modules.Submissions;

/// <summary>multipart/form-data body of POST /me/submissions.</summary>
public sealed class CreateSubmissionForm
{
    [Required]
    public Guid? CampaignId { get; set; }

    [Required]
    public Guid? SocialAccountId { get; set; }

    [Required]
    public SocialPlatform? Platform { get; set; }

    [Required, MaxLength(1000)]
    public string PostUrl { get; set; } = string.Empty;

    /// <summary>When the post went live (ISO-8601, UTC).</summary>
    [Required]
    public DateTimeOffset? PostedAt { get; set; }

    [MaxLength(5000)]
    public string? CaptionText { get; set; }

    public Guid? ExperimentVariantId { get; set; }

    public IFormFile? Screenshot { get; set; }
}

/// <summary>multipart/form-data body of PUT /me/submissions/{id} (only while NeedsCorrection). Omitted fields keep their value.</summary>
public sealed class UpdateSubmissionForm
{
    [MaxLength(1000)]
    public string? PostUrl { get; set; }

    public DateTimeOffset? PostedAt { get; set; }

    [MaxLength(5000)]
    public string? CaptionText { get; set; }

    public IFormFile? Screenshot { get; set; }
}

public sealed class AppealRequest
{
    [Required, MinLength(20), MaxLength(2000)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class MySubmissionsQuery : PageQuery
{
    public SubmissionStatus? Status { get; set; }
    public Guid? CampaignId { get; set; }
}

public sealed record CampaignRefDto(Guid Id, string Slug, string Title);

public sealed record MySubmissionListItemDto(
    Guid Id, CampaignRefDto Campaign, SocialPlatform Platform, string PostUrl, SubmissionStatus Status, DateTime SubmittedAt,
    decimal EstimatedReward, string Currency, string? DecisionReason);

public sealed record TimelineEntryDto(string Action, SubmissionStatus? FromStatus, SubmissionStatus ToStatus, string? Reason, string Actor, DateTime At);

public sealed record LiveCheckDto(LiveCheckStatus Status, DateTime? DueAt, DateTime? CheckedAt);

public sealed record SubmissionEarningDto(Guid Id, EarningType Type, decimal Amount, string Currency, EarningStatus Status, DateTime CreatedAt);

public sealed record AppealSummaryDto(Guid Id, AppealStatus Status, SubmissionStatus DecisionAppealed, string Reason, string? ResolutionNote,
    DateTime CreatedAt, DateTime? ResolvedAt);

public sealed record SocialAccountRefDto(Guid Id, SocialPlatform Platform, string Handle);

public sealed record MySubmissionDetailDto(
    Guid Id, CampaignRefDto Campaign, SocialPlatform Platform, SocialAccountRefDto SocialAccount, string PostUrl, DateTime PostedAt,
    string? CaptionText, string? ScreenshotUrl, SubmissionStatus Status, DateTime SubmittedAt, DateTime? DecidedAt,
    string? DecisionReason, int CorrectionCount, decimal EstimatedReward, string Currency, int RewardRuleSetVersion,
    LiveCheckDto LiveCheck, IReadOnlyList<TimelineEntryDto> Timeline, IReadOnlyList<SubmissionEarningDto> Earnings,
    AppealSummaryDto? Appeal, bool CanEdit, bool CanAppeal, DateTime? AppealDeadline);
