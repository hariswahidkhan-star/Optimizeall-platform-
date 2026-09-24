using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Billing;

namespace OptimizeAll.Api.Modules.PaymentsHub;

public enum PaymentDirection
{
    /// <summary>Money the agency receives (client invoice payments).</summary>
    Incoming,
    /// <summary>Money the agency pays out (participant payouts).</summary>
    Outgoing,
}

/// <summary>Unified status of a payment record across incoming and outgoing money.</summary>
public enum PaymentHubStatus
{
    /// <summary>Planned but not yet payable (payout item in a draft batch).</summary>
    Scheduled,
    /// <summary>Expected / awaiting action: an open invoice balance, a client's unconfirmed "I've paid", a payout awaiting payment.</summary>
    Pending,
    Paid,
    /// <summary>A payout that bounced or could not be made (its earnings roll into the next batch).</summary>
    Failed,
    /// <summary>An incoming payment returned to the client.</summary>
    Refunded,
    /// <summary>Cancelled: a payment reversed as recorded in error, a rejected claim, a cancelled or released payout item.</summary>
    Voided,
}

public enum PaymentRecordKind
{
    /// <summary>A payment recorded on an invoice.</summary>
    InvoicePayment,
    /// <summary>The outstanding balance of an open invoice (a receivable).</summary>
    InvoiceDue,
    /// <summary>A client's "I've paid" report waiting for staff confirmation.</summary>
    PaymentClaim,
    /// <summary>A participant payout (one item of a payout batch).</summary>
    PayoutItem,
}

public sealed class PaymentHubQuery : PageQuery
{
    public PaymentDirection? Direction { get; set; }
    public PaymentHubStatus? Status { get; set; }
    public PaymentRecordKind? Kind { get; set; }

    [StringLength(3)]
    public string? Currency { get; set; }

    /// <summary>Incoming payment method (payout records have no method and are excluded when this is set).</summary>
    public PaymentMethod? Method { get; set; }

    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public Guid? ClientAccountId { get; set; }

    /// <summary>Participant (payout beneficiary).</summary>
    public Guid? UserId { get; set; }

    public Guid? InvoiceId { get; set; }
    public Guid? BatchId { get; set; }

    /// <summary>Only open invoices past their due date (incoming receivables).</summary>
    public bool? OverdueOnly { get; set; }
}

public sealed record PaymentPartyDto(string Type, Guid Id, string Name, string? Email);

/// <summary>
/// One row of the Payments hub. <see cref="Key"/> is "{kind}:{id}". <see cref="ConcurrencyStamp"/> is the stamp of the row's
/// own entity (payment, invoice, claim or payout item); <see cref="InvoiceConcurrencyStamp"/> the invoice stamp needed to
/// record a payment. <see cref="Actions"/> lists what the caller may do with the row now.
/// </summary>
public sealed record PaymentRecordDto(
    string Key,
    PaymentRecordKind Kind,
    Guid Id,
    PaymentDirection Direction,
    PaymentHubStatus Status,
    string SourceStatus,
    PaymentPartyDto Party,
    decimal Amount,
    string Currency,
    string? Method,
    string? Reference,
    DateOnly Date,
    DateOnly? DueDate,
    DateTime? PaidAt,
    DateTime? ReversedAt,
    string? ReversalReason,
    Guid? InvoiceId,
    string? InvoiceNumber,
    decimal? InvoiceBalance,
    Guid? BatchId,
    string? BatchReference,
    string? Notes,
    string? RecordedBy,
    int DaysOverdue,
    bool HasProof,
    Guid ConcurrencyStamp,
    Guid? InvoiceConcurrencyStamp,
    DateTime CreatedAt,
    IReadOnlyList<string> Actions);

/// <summary>Action names returned in <see cref="PaymentRecordDto.Actions"/>.</summary>
public static class PaymentActions
{
    public const string RecordPayment = "record_payment";
    public const string MarkPaidInFull = "mark_paid_in_full";
    public const string SendReminder = "send_reminder";
    public const string Edit = "edit";
    public const string Reverse = "reverse";
    public const string Refund = "refund";
    public const string UploadProof = "upload_proof";
    public const string ConfirmClaim = "confirm_claim";
    public const string RejectClaim = "reject_claim";
    public const string MarkPayoutPaid = "mark_payout_paid";
    public const string MarkPayoutFailed = "mark_payout_failed";
}

public sealed record PaymentHubHistoryDto(DateTime At, string Action, string? Actor, string? Reason, string? Details);

public sealed record PaymentProofDto(Guid Id, string FileName, string ContentType, long SizeBytes, DateTime CreatedAt, string Url);

public sealed record ReminderHistoryDto(string Kind, DateTime SentAt, bool Manual, string? SentBy);

public sealed record PaymentRecordDetailDto(
    PaymentRecordDto Record,
    IReadOnlyList<PaymentDto> InvoicePayments,
    IReadOnlyList<PaymentProofDto> Proofs,
    IReadOnlyList<ReminderHistoryDto> Reminders,
    IReadOnlyList<PaymentHubHistoryDto> History);

// ---------------------------------------------------------------- Summary

public sealed record IncomingSummaryDto(
    IReadOnlyList<CurrencyAmount> ReceivedThisMonth,
    IReadOnlyList<CurrencyAmount> RefundedThisMonth,
    IReadOnlyList<AgingRowDto> Outstanding,
    int OpenInvoices,
    int OverdueInvoices,
    int PendingClaims);

public sealed record NextPayoutCycleDto(string PeriodKey, DateTime CutoffAt, DateOnly PaymentDate, IReadOnlyList<CurrencyAmount> EstimatedAvailable);

public sealed record OutgoingSummaryDto(
    IReadOnlyList<CurrencyAmount> PaidOutThisMonth,
    IReadOnlyList<CurrencyAmount> DueInBatches,
    int AwaitingPayment,
    int FailedThisMonth,
    NextPayoutCycleDto NextCycle);

/// <summary>KPIs per currency (never converted or mixed). A section is null when the caller lacks its permission.</summary>
public sealed record PaymentsSummaryDto(DateOnly Today, DateOnly MonthStart, IncomingSummaryDto? Incoming, OutgoingSummaryDto? Outgoing);

// ---------------------------------------------------------------- Actions

public sealed class RecordInvoicePaymentRequest
{
    [Required]
    public Guid? RequestId { get; set; }

    [Range(typeof(decimal), "0.001", "1000000000")]
    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;

    [Required, StringLength(120, MinimumLength = 1)]
    public string Reference { get; set; } = string.Empty;

    [Required]
    public DateOnly? PaidOn { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>The invoice stamp the user saw.</summary>
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

/// <summary>Records the invoice's remaining balance as one payment.</summary>
public sealed class MarkInvoicePaidRequest
{
    [Required]
    public Guid? RequestId { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;

    [Required, StringLength(120, MinimumLength = 1)]
    public string Reference { get; set; } = string.Empty;

    [Required]
    public DateOnly? PaidOn { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>The balance the user saw (optional): the request fails (409) if the balance changed meanwhile.</summary>
    public decimal? ExpectedBalance { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class MarkPayoutPaidRequest
{
    [Required, StringLength(120, MinimumLength = 3)]
    public string PaymentReference { get; set; } = string.Empty;

    [Required]
    public DateTime? PaidAt { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    /// <summary>Required to pay a participant who has an active payout hold (audited).</summary>
    [MaxLength(1000)]
    public string? OverrideReason { get; set; }
}

public enum PayoutFailureKind
{
    /// <summary>The transfer could not be made.</summary>
    Failed,
    /// <summary>The bank returned the transfer.</summary>
    Returned,
}

public sealed class MarkPayoutFailedRequest
{
    public PayoutFailureKind Kind { get; set; } = PayoutFailureKind.Failed;

    [Required, StringLength(900, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class MarkBatchPaidRequest
{
    /// <summary>The bank's bulk-transfer reference, used for every item without its own reference.</summary>
    [Required, StringLength(120, MinimumLength = 3)]
    public string PaymentReference { get; set; } = string.Empty;

    [Required]
    public DateTime? PaidAt { get; set; }

    public bool Confirm { get; set; }
}

public sealed record PayoutActionResultDto(PaymentRecordDto Record, string BatchStatus, bool Replayed, bool Requeued);

public sealed record BatchPaidLineDto(Guid ItemId, string Outcome, string Message);

public sealed record BatchPaidResultDto(Guid BatchId, string BatchStatus, int Recorded, int AlreadyRecorded, int Invalid, IReadOnlyList<BatchPaidLineDto> Lines);

// ---------------------------------------------------------------- Claims ("I've paid")

public sealed class SubmitPaymentClaimRequest
{
    [Required]
    public Guid? RequestId { get; set; }

    [Range(typeof(decimal), "0.001", "1000000000")]
    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;

    [Required, StringLength(120, MinimumLength = 3)]
    public string Reference { get; set; } = string.Empty;

    [Required]
    public DateOnly? PaidOn { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }
}

public sealed class ConfirmPaymentClaimRequest
{
    /// <summary>Amount actually received (defaults to the claimed amount).</summary>
    [Range(typeof(decimal), "0.001", "1000000000")]
    public decimal? Amount { get; set; }

    /// <summary>Date the money arrived (defaults to the claimed date).</summary>
    public DateOnly? PaidOn { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>The invoice stamp the user saw.</summary>
    [Required]
    public Guid? InvoiceConcurrencyStamp { get; set; }
}

public sealed class RejectPaymentClaimRequest
{
    [Required, StringLength(1000, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record PaymentClaimDto(
    Guid Id, Guid InvoiceId, string? InvoiceNumber, Guid ClientAccountId, string ClientName, string SubmittedBy, decimal Amount,
    string Currency, PaymentMethod Method, string Reference, DateOnly PaidOn, string? Note, PaymentClaimStatus Status,
    DateTime CreatedAt, DateTime? ReviewedAt, string? ReviewNote, Guid? PaymentId, bool HasProof, Guid ConcurrencyStamp);

public sealed class PaymentClaimQuery : PageQuery
{
    public PaymentClaimStatus? Status { get; set; }
    public Guid? InvoiceId { get; set; }
}

/// <summary>Client portal: what was paid on one invoice, and the client's own "I've paid" reports.</summary>
public sealed record ClientInvoicePaymentDto(Guid Id, decimal Amount, string Currency, PaymentMethod Method, string Reference, DateOnly PaidOn,
    string Status, DateTime? ReversedAt);

public sealed record ClientInvoicePaymentsDto(
    Guid InvoiceId, string? Number, InvoiceStatus Status, string Currency, decimal Total, decimal AmountPaid, decimal AmountCredited,
    decimal Balance, IReadOnlyList<ClientInvoicePaymentDto> Payments, IReadOnlyList<PaymentClaimDto> Claims, bool CanReportPayment);

// ---------------------------------------------------------------- Reminders

public sealed class SendReminderRequest
{
    [Required]
    public Guid? RequestId { get; set; }
}

public sealed record ReminderSentDto(Guid InvoiceId, string Kind, DateTime SentAt, bool Replayed);

public sealed record ReminderPolicyDto(Guid ClientAccountId, string ClientName, bool UsesAgencyDefault, bool Enabled, IReadOnlyList<int> OffsetsDays,
    bool AgencyEnabled, IReadOnlyList<int> AgencyOffsetsDays, Guid? ConcurrencyStamp);

public sealed class UpdateReminderPolicyRequest
{
    /// <summary>True removes the client's override (the agency schedule applies again).</summary>
    public bool UseAgencyDefault { get; set; }

    public bool Enabled { get; set; } = true;

    public List<int> OffsetsDays { get; set; } = new();

    [Required, StringLength(1000, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Stamp of the existing override (null when there is none yet).</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

/// <summary>What the reminder job would send today (nothing is sent).</summary>
public sealed record ReminderPreviewRowDto(Guid InvoiceId, string? InvoiceNumber, string ClientName, DateOnly DueDate, decimal Balance, string Currency,
    string Kind, bool AlreadySent);
