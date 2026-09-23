using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Ledger;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Payouts;

namespace OptimizeAll.Api.Modules.Payouts;

// ---------- Schedule ----------

public sealed record PayoutScheduleDto(
    Guid? Id,
    PayoutFrequency Frequency,
    DateOnly AnchorCutoffDate,
    string CutoffLocalTime,
    string TimeZone,
    int PaymentDelayDays,
    decimal MinimumPayoutAmount,
    string SettlementCurrency,
    int EarningHoldDays,
    bool AutoPrepareBatches,
    DateTime? EffectiveFrom,
    DateTime? CreatedAt,
    Guid? CreatedByUserId,
    string? ChangeReason,
    bool IsDefault);

/// <summary>A payout period: earnings with AvailableAt in (periodStart, cutoffAt] are paid on/after paymentDate.</summary>
public sealed record PayoutPeriodDto(string PeriodKey, DateTime PeriodStart, DateTime CutoffAt, DateOnly CutoffLocalDate, DateOnly PaymentDate)
{
    public static PayoutPeriodDto From(PayoutPeriod p) => new(p.PeriodKey, p.PeriodStartUtc, p.CutoffUtc, p.CutoffLocalDate, p.PaymentDate);
}

public sealed record PayoutScheduleResponse(
    PayoutScheduleDto Current,
    PayoutPeriodDto CurrentPeriod,
    PayoutPeriodDto LastCompletedPeriod,
    IReadOnlyList<PayoutPeriodDto> Upcoming,
    IReadOnlyList<PayoutScheduleDto> ScheduledChanges,
    IReadOnlyList<PayoutScheduleDto> History);

public sealed class UpdatePayoutScheduleRequest
{
    [Required]
    public PayoutFrequency? Frequency { get; set; }

    [Required]
    public DateOnly? AnchorCutoffDate { get; set; }

    /// <summary>"HH:mm" or "HH:mm:ss", local to <see cref="TimeZone"/>.</summary>
    [Required, RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d(:[0-5]\d)?$", ErrorMessage = "Use HH:mm or HH:mm:ss.")]
    public string CutoffLocalTime { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string TimeZone { get; set; } = string.Empty;

    [Range(0, 30)]
    public int PaymentDelayDays { get; set; }

    [Range(0, 100_000)]
    public decimal MinimumPayoutAmount { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string SettlementCurrency { get; set; } = string.Empty;

    [Range(0, 60)]
    public int EarningHoldDays { get; set; }

    public bool AutoPrepareBatches { get; set; }

    /// <summary>When the new version takes effect (UTC, not in the past).</summary>
    [Required]
    public DateTime? EffectiveFrom { get; set; }

    [Required, MinLength(10), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public bool Confirm { get; set; }
}

// ---------- Holds ----------

public sealed class HoldQuery : PageQuery
{
    public bool? Active { get; set; }
    public Guid? UserId { get; set; }
}

public sealed record PayoutHoldDto(
    Guid Id, UserRefDto User, string Reason, DateTime CreatedAt, Guid CreatedByUserId, bool IsActive,
    DateTime? ReleasedAt, Guid? ReleasedByUserId, string? ReleaseNote);

public sealed class CreateHoldRequest
{
    [Required]
    public Guid? UserId { get; set; }

    [Required, MinLength(5), MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// <c>HeldDraftItemIds</c>: pending items of draft batches that were put on hold automatically.
/// <c>AwaitingPaymentItemIds</c>: items of finalized batches awaiting payment — finance must decide on these manually
/// (mark failed or pay).
/// </summary>
public sealed record CreateHoldResponse(PayoutHoldDto Hold, IReadOnlyList<Guid> HeldDraftItemIds, IReadOnlyList<Guid> AwaitingPaymentItemIds);

public sealed class ReleaseHoldRequest
{
    [MaxLength(1000)]
    public string? Note { get; set; }
}

// ---------- Batches ----------

public sealed class PreparePayoutBatchRequest
{
    /// <summary>Cutoff local date (yyyy-MM-dd) of the period; default: the last completed period.</summary>
    [RegularExpression(@"^\d{4}-\d{2}-\d{2}$")]
    public string? PeriodKey { get; set; }

    [MaxLength(2000)]
    public string? Note { get; set; }
}

public sealed record PayoutExclusionDto(PayoutUserDto User, PayoutExclusionReason Reason, decimal Amount, int EarningCount);

public sealed record PayoutUserDto(Guid Id, string DisplayName, string Email, string Country);

public sealed record PayoutBatchSummaryDto(
    Guid Id,
    string Reference,
    string PeriodKey,
    DateTime PeriodStart,
    DateTime CutoffAt,
    DateOnly PaymentDate,
    PayoutBatchStatus Status,
    int ItemCount,
    decimal TotalAmount,
    string Currency,
    int PaidCount,
    decimal PaidAmount,
    UserRefDto? PreparedBy,
    UserRefDto? FinalizedBy,
    DateTime CreatedAt,
    DateTime? FinalizedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    DateTime? InstructionsExportedAt = null);

public sealed record PrepareBatchResponse(bool Created, PayoutBatchSummaryDto Batch, IReadOnlyList<PayoutExclusionDto> Exclusions);

public sealed class BatchListQuery : PageQuery
{
    public PayoutBatchStatus? Status { get; set; }
}

public sealed class BatchItemsQuery : PageQuery
{
    public PayoutItemStatus? ItemStatus { get; set; }
}

public sealed record StatusTotalDto(PayoutItemStatus Status, int Count, decimal Amount);

public sealed record PayoutItemDto(
    Guid ItemId,
    PayoutUserDto User,
    decimal Amount,
    string Currency,
    int EarningCount,
    PayoutItemStatus Status,
    string PaymentProvider,
    string? DestinationHint,
    string? PaymentReference,
    DateTime? PaidAt,
    string? HoldReason,
    string? FailureReason,
    Guid ConcurrencyStamp);

public sealed record UserWarningDto(PayoutUserDto User, Guid? ItemId, string Detail);

public sealed record BatchWarningsDto(
    IReadOnlyList<UserWarningDto> MissingPayoutDetails,
    IReadOnlyList<UserWarningDto> OpenAppeals,
    IReadOnlyList<UserWarningDto> OpenDisputes,
    IReadOnlyList<UserWarningDto> HighRiskSubmissions,
    int HighRiskThreshold,
    IReadOnlyList<PayoutExclusionDto> Exclusions);

public sealed record PayoutBatchDetailDto(
    PayoutBatchSummaryDto Batch,
    Guid ConcurrencyStamp,
    string? Notes,
    string? CancelReason,
    IReadOnlyList<StatusTotalDto> TotalsByStatus,
    BatchWarningsDto Warnings,
    PagedResult<PayoutItemDto> Items);

public sealed record ItemEarningDto(
    Guid Id,
    EarningType Type,
    EarningStatus Status,
    string Description,
    CampaignRefDto? Campaign,
    Guid? SubmissionId,
    decimal OriginalAmount,
    string OriginalCurrency,
    decimal ExchangeRate,
    decimal SettlementAmount,
    string SettlementCurrency,
    DateTime CreatedAt,
    DateTime? AvailableAt);

public sealed record PaymentAttemptDto(
    Guid Id, string Provider, string IdempotencyKey, PaymentAttemptStatus Status, string? ProviderReference, string? Message,
    DateTime CreatedAt, DateTime? UpdatedAt);

public sealed record PayoutItemDetailDto(
    Guid BatchId,
    string BatchReference,
    string PeriodKey,
    PayoutBatchStatus BatchStatus,
    PayoutItemDto Item,
    decimal EarningsTotal,
    IReadOnlyList<ItemEarningDto> Earnings,
    IReadOnlyList<PaymentAttemptDto> PaymentAttempts);

public sealed class HoldItemRequest
{
    [Required, MinLength(5), MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class UnholdItemRequest
{
    [MaxLength(1000)]
    public string? Note { get; set; }
}

public sealed class RegenerateBatchRequest
{
    [Required, MinLength(5), MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class CancelBatchRequest
{
    [Required, MinLength(5), MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;

    public bool Confirm { get; set; }
}

public sealed class FinalizeBatchRequest
{
    public bool Confirm { get; set; }

    [MaxLength(1000)]
    public string? Reason { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class RecordPaymentRequest
{
    [Required, MinLength(3), MaxLength(120)]
    public string PaymentReference { get; set; } = string.Empty;

    [Required]
    public DateTime? PaidAt { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    /// <summary>
    /// Required to record a payment for a participant with an active payout hold (e.g. the transfer already left the
    /// account before the hold was placed). Audited as <c>payout.payment_hold_overridden</c>.
    /// </summary>
    [MaxLength(1000)]
    public string? OverrideReason { get; set; }
}

public sealed class BulkPaymentLine
{
    [Required]
    public Guid? ItemId { get; set; }

    public string PaymentReference { get; set; } = string.Empty;

    public DateTime? PaidAt { get; set; }
}

public sealed record BulkPaymentResultDto(Guid ItemId, string Status, string Message);

public sealed class MarkFailedRequest
{
    [Required, MinLength(5), MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

public sealed record PaymentRecordedDto(PayoutItemDto Item, PayoutBatchStatus BatchStatus);

public sealed record FinalizeBatchResponse(PayoutBatchSummaryDto Batch, IReadOnlyList<DispatchResultDto> Dispatch);

public sealed record DispatchResultDto(Guid ItemId, PaymentAttemptStatus Status, string? ProviderReference, string Message, bool Reused);

// ---------- Reconciliation ----------

public sealed record ReconciliationDiscrepancyDto(string Type, string Severity, Guid? ItemId, Guid? UserId, Guid? EarningId, string Message);

public sealed record ReconciliationItemDto(
    Guid ItemId, PayoutUserDto User, PayoutItemStatus Status, decimal Amount, decimal EarningsTotal, int EarningCount,
    int LinkedEarningCount, string? PaymentReference, DateTime? PaidAt, bool Ok);

public sealed record ReconciliationDto(
    Guid BatchId,
    string Reference,
    string PeriodKey,
    PayoutBatchStatus Status,
    string Currency,
    decimal Expected,
    decimal RecordedPaid,
    decimal Awaiting,
    decimal Failed,
    decimal Held,
    decimal Cancelled,
    int PaidCount,
    int AwaitingCount,
    bool IsBalanced,
    IReadOnlyList<ReconciliationDiscrepancyDto> Discrepancies,
    IReadOnlyList<ReconciliationItemDto> Items);

// ---------- Participant ----------

public sealed record MyPayoutDto(
    Guid ItemId,
    string BatchReference,
    string PeriodKey,
    DateTime CutoffAt,
    DateOnly PaymentDate,
    decimal Amount,
    string Currency,
    PayoutItemStatus Status,
    DateTime? PaidAt,
    string? PaymentReference,
    int EarningCount);

public sealed record MyPayoutDetailDto(MyPayoutDto Payout, IReadOnlyList<EarningDto> Earnings);
