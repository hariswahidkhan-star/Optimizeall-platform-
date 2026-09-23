using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Payouts;

public enum PayoutFrequency
{
    Weekly,
    Biweekly,
    Monthly,
}

/// <summary>
/// Payout schedule configuration. Versioned: a change inserts a new row and the latest active row whose
/// EffectiveFrom has passed is used. Default is biweekly.
/// </summary>
public class PayoutSchedule : Entity
{
    public PayoutFrequency Frequency { get; set; } = PayoutFrequency.Biweekly;

    /// <summary>A known cutoff date (local to <see cref="TimeZone"/>); periods repeat from it.</summary>
    public DateOnly AnchorCutoffDate { get; set; }

    /// <summary>Local time on the cutoff date after which earnings roll into the next period.</summary>
    public TimeOnly CutoffLocalTime { get; set; } = new(23, 59, 59);
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Days between cutoff and the target payment date.</summary>
    public int PaymentDelayDays { get; set; } = 5;

    /// <summary>Participants whose payable balance is below this amount are carried over to the next batch.</summary>
    public decimal MinimumPayoutAmount { get; set; } = 10m;
    public string SettlementCurrency { get; set; } = "USD";

    /// <summary>Days an approved earning is held before it becomes payable (reversal buffer).</summary>
    public int EarningHoldDays { get; set; } = 3;

    /// <summary>When true the background job prepares a draft batch automatically at each cutoff.</summary>
    public bool AutoPrepareBatches { get; set; } = true;

    public DateTime EffectiveFrom { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? ChangeReason { get; set; }
}

public enum PayoutBatchStatus
{
    /// <summary>Prepared and open for finance review; items may be held/removed; can be regenerated.</summary>
    Draft,
    /// <summary>Approved for payment. Contents are frozen. Payments are recorded per item.</summary>
    Finalized,
    /// <summary>Every item is Paid, Failed or Cancelled.</summary>
    Completed,
    Cancelled,
}

public class PayoutBatch : AuditedEntity, IConcurrencyStamped
{
    public string Reference { get; set; } = string.Empty;

    /// <summary>Unique key preventing duplicate batches for the same period (e.g. "period:2026-09-20").</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Cutoff local date of the period (yyyy-MM-dd), see PayoutPeriodCalculator.</summary>
    public string PeriodKey { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime CutoffAt { get; set; }
    public DateOnly ScheduledPaymentDate { get; set; }
    public string Currency { get; set; } = "USD";
    public PayoutBatchStatus Status { get; set; } = PayoutBatchStatus.Draft;

    public int ItemCount { get; set; }
    public decimal TotalAmount { get; set; }

    public Guid? PreparedByUserId { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public Guid? FinalizedByUserId { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancelReason { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// JSON snapshot (array) of participants with eligible earnings who were NOT included when the batch was prepared
    /// (payout hold, inactive account, non-positive balance, below minimum), with their carried-over amounts.
    /// </summary>
    public string? ExclusionsJson { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<PayoutItem> Items { get; set; } = new();
}

public enum PayoutItemStatus
{
    /// <summary>In a draft batch.</summary>
    Pending,
    /// <summary>Excluded from payment by finance (earnings returned to Approved on finalize).</summary>
    Held,
    /// <summary>Batch finalized; waiting for finance to pay and record the payment reference.</summary>
    AwaitingPayment,
    Paid,
    /// <summary>Payment attempt failed; earnings are returned to Approved for a later batch.</summary>
    Failed,
    Cancelled,
}

public class PayoutItem : AuditedEntity, IConcurrencyStamped
{
    public Guid BatchId { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public int EarningCount { get; set; }
    public PayoutItemStatus Status { get; set; } = PayoutItemStatus.Pending;

    /// <summary>Payment provider key; "manual" until a provider integration is configured.</summary>
    public string PaymentProvider { get; set; } = "manual";
    public string? PaymentReference { get; set; }
    public string? ProviderTransactionId { get; set; }
    public DateTime? PaidAt { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public string? FailureReason { get; set; }
    public string? HoldReason { get; set; }

    /// <summary>Snapshot of the payout destination hint at batch time (masked).</summary>
    public string? DestinationHint { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public enum PaymentAttemptStatus
{
    Created,
    Submitted,
    Succeeded,
    Failed,
    /// <summary>The provider cannot send money itself (manual provider): a person must pay and record it.</summary>
    RequiresManualAction,
}

/// <summary>Integration-layer log of every attempt to pay an item through a provider. Idempotent per key.</summary>
public class PaymentAttempt : Entity
{
    public Guid PayoutItemId { get; set; }
    public string Provider { get; set; } = "manual";
    public string IdempotencyKey { get; set; } = string.Empty;
    public PaymentAttemptStatus Status { get; set; }
    public string? ProviderReference { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Stops a participant's earnings from being paid until released (fraud review, KYC, dispute).</summary>
public class PayoutHold : Entity
{
    public Guid UserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public Guid? ReleasedByUserId { get; set; }
    public string? ReleaseNote { get; set; }

    public bool IsActive => ReleasedAt is null;
}
