using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Billing;

public enum InvoiceStatus
{
    Draft,
    Issued,
    PartiallyPaid,
    Paid,
    Overdue,
    Void,
    WrittenOff,
}

public enum PaymentMethod
{
    BankTransfer,
    Card,
    Cash,
    Cheque,
    PayPal,
    Stripe,
    Other,
}

public enum ContractStatus
{
    Draft,
    Active,
    Paused,
    Cancelled,
    Ended,
}

/// <summary>
/// A configurable tax rate (VAT, GST, sales tax). Rates are snapshotted onto lines when used, so editing a rate never
/// changes an issued invoice. Seeded examples are marked <see cref="NeedsReview"/> until finance confirms them.
/// </summary>
public class TaxRate : AuditedEntity, IConcurrencyStamped
{
    public string Name { get; set; } = string.Empty;
    public decimal RatePercent { get; set; }

    /// <summary>Prices entered with this rate already include the tax.</summary>
    public bool Inclusive { get; set; }
    public string? CountryCode { get; set; }
    public bool IsActive { get; set; } = true;
    public bool NeedsReview { get; set; }
    public string? Notes { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Gapless document number counter (one row per series and year, e.g. "invoice:2026"). Incremented under a row lock in the
/// same transaction that uses the number, so a rolled-back transaction also rolls back the counter (no gaps).
/// </summary>
public class NumberSequence : Entity
{
    public string Key { get; set; } = string.Empty;
    public long LastValue { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Shared shape of a priced line on contracts and invoices (snapshot of the tax rate used).</summary>
public abstract class PricedLine : Entity
{
    public int Position { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>Service slug (website service catalog) for revenue-by-service reporting.</summary>
    public string? ServiceSlug { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public DiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public Guid? TaxRateId { get; set; }
    public string? TaxName { get; set; }
    public decimal TaxPercent { get; set; }
    public bool TaxInclusive { get; set; }

    // Computed server-side (Pricing.Compute) and stored for reporting.
    public decimal DiscountAmount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }

    public PriceLineInput ToInput(Recurrence recurrence = Recurrence.OneTime) =>
        new(Quantity, UnitPrice, DiscountType, DiscountValue, TaxPercent, TaxInclusive, recurrence, TaxName);

    public void ApplyAmounts(PriceLineAmounts amounts)
    {
        DiscountAmount = amounts.Discount;
        Subtotal = amounts.Subtotal;
        TaxAmount = amounts.Tax;
        Total = amounts.Total;
    }
}

/// <summary>
/// A client invoice. Drafts are editable; once issued the amounts and lines are immutable (corrections go through credit
/// notes) and only the status, payments, credits and reminders change.
/// </summary>
public class Invoice : AuditedEntity, IConcurrencyStamped
{
    /// <summary>Assigned when issued (gapless per series/year, e.g. OA-2026-0001).</summary>
    public string? Number { get; set; }
    public Guid ClientAccountId { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public string Currency { get; set; } = "USD";
    public DateOnly? IssueDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public int PaymentTermsDays { get; set; } = 14;

    public decimal GrossTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountCredited { get; set; }
    public decimal AmountWrittenOff { get; set; }

    /// <summary>Total − paid − credited − written off.</summary>
    public decimal Balance { get; set; }

    public string? Notes { get; set; }
    public string? Reference { get; set; }

    public Guid? ContractId { get; set; }
    public Guid? ProposalId { get; set; }
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }

    /// <summary>Unique key for system-generated invoices (e.g. <c>contract:{id}:{periodStart}</c>, <c>proposal:{id}:initial</c>).</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>SHA-256 of the public view token (<c>/i/{token}</c>); the raw token is kept encrypted for re-sending.</summary>
    public string? PublicTokenHash { get; set; }
    public string? PublicTokenProtected { get; set; }

    public DateTime? IssuedAt { get; set; }
    public Guid? IssuedByUserId { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? VoidedAt { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
    public DateTime? WrittenOffAt { get; set; }
    public Guid? WrittenOffByUserId { get; set; }
    public string? WriteOffReason { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<InvoiceLine> Lines { get; set; } = new();

    public static bool IsOpen(InvoiceStatus status) =>
        status is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid or InvoiceStatus.Overdue;

    public static readonly InvoiceStatus[] OpenStatuses = { InvoiceStatus.Issued, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue };

    public void RecalculateBalance(string currency) =>
        Balance = Money.Round(Total - AmountPaid - AmountCredited - AmountWrittenOff, currency);

    /// <summary>Status after a payment/credit: Paid when settled, Overdue when past due, otherwise (Partially)Issued.</summary>
    public InvoiceStatus SettlementStatus(DateOnly today) =>
        Balance <= 0 ? InvoiceStatus.Paid
        : DueDate is { } due && due < today ? InvoiceStatus.Overdue
        : AmountPaid > 0 || AmountCredited > 0 ? InvoiceStatus.PartiallyPaid
        : InvoiceStatus.Issued;
}

public class InvoiceLine : PricedLine
{
    public Guid InvoiceId { get; set; }
}

/// <summary>Why a payment was reversed: money returned to the client (refund) or a payment recorded in error (void).</summary>
public enum PaymentReversalKind
{
    /// <summary>The money was returned to the client.</summary>
    Refund,
    /// <summary>The payment was recorded by mistake (wrong invoice, wrong amount, never received).</summary>
    Error,
}

/// <summary>
/// A payment recorded against an invoice (manually by finance, or confirmed by a payment gateway). The amount never
/// changes after insert: a correction is a reversal row (negative amount, <see cref="ReversalOfPaymentId"/> set) that
/// marks the original as reversed, followed by a new payment. Sums over payments are therefore always net.
/// </summary>
public class Payment : Entity, IConcurrencyStamped
{
    public Guid InvoiceId { get; set; }
    public Guid ClientAccountId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public PaymentMethod Method { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateOnly PaidOn { get; set; }
    public string? Notes { get; set; }

    /// <summary>Client-generated idempotency key: a retried request returns the original payment.</summary>
    public Guid RequestId { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The bank reference while the payment counts (unique per invoice); null once reversed and on reversal rows, so the
    /// same bank reference can be recorded again after a correction.
    /// </summary>
    public string? ActiveReference { get; set; }

    /// <summary>On a reversal row (negative amount): the payment it reverses.</summary>
    public Guid? ReversalOfPaymentId { get; set; }

    public DateTime? ReversedAt { get; set; }
    public Guid? ReversedByUserId { get; set; }
    public string? ReversalReason { get; set; }
    public PaymentReversalKind? ReversalKind { get; set; }

    /// <summary>Last edit of the payment's details (reference, date, method, notes). The amount is never edited.</summary>
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public bool IsReversal => ReversalOfPaymentId is not null;
}

public enum CreditNoteStatus
{
    /// <summary>Issued with credit still available to apply.</summary>
    Open,
    /// <summary>All of its amount has been applied to invoices.</summary>
    Applied,
}

/// <summary>A credit note: reduces what a client owes. Applied to one or more open invoices of the same client and currency.</summary>
public class CreditNote : AuditedEntity, IConcurrencyStamped
{
    public string Number { get; set; } = string.Empty;
    public Guid ClientAccountId { get; set; }
    public Guid? InvoiceId { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal Amount { get; set; }
    public decimal AmountApplied { get; set; }
    public string Reason { get; set; } = string.Empty;
    public CreditNoteStatus Status { get; set; } = CreditNoteStatus.Open;
    public DateOnly IssueDate { get; set; }
    public Guid RequestId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class CreditNoteApplication : Entity
{
    public Guid CreditNoteId { get; set; }
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public DateTime AppliedAt { get; set; }
    public Guid? AppliedByUserId { get; set; }
}

/// <summary>A payment reminder that was sent for an invoice. Unique per (invoice, kind) so a reminder is never sent twice.</summary>
public class InvoiceReminder : Entity
{
    public Guid InvoiceId { get; set; }

    /// <summary>"before-3", "due", "after-7", "after-14" (days relative to the due date); "manual-yyMMddHHmmss" when sent by staff.</summary>
    public string Kind { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }

    /// <summary>Staff member who sent it with "Send reminder now" (null for the scheduled job).</summary>
    public Guid? SentByUserId { get; set; }

    /// <summary>Idempotency key of a manual reminder (a retried request never sends twice).</summary>
    public Guid? RequestId { get; set; }

    public bool IsManual => SentByUserId is not null;
}

/// <summary>
/// Per-client override of the agency reminder schedule (<c>BillingSettings.ReminderOffsetsDays</c>). Without a row the
/// agency settings apply.
/// </summary>
public class ClientReminderPolicy : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Days relative to the due date (negative = before), e.g. 3, 7, 14.</summary>
    public List<int> OffsetsDays { get; set; } = new();
    public Guid? UpdatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public enum PaymentClaimStatus
{
    /// <summary>Submitted by the client, waiting for staff to check the bank statement.</summary>
    Pending,
    /// <summary>Staff confirmed the money arrived; a payment was recorded.</summary>
    Confirmed,
    /// <summary>Staff could not match the transfer.</summary>
    Rejected,
}

/// <summary>
/// "I've paid": a client user reports a transfer (reference, amount, date). Nothing changes on the invoice until staff
/// confirm it, which records a payment through the normal payment rules (idempotent: the claim id is the request id).
/// </summary>
public class PaymentClaim : AuditedEntity, IConcurrencyStamped
{
    public Guid InvoiceId { get; set; }
    public Guid ClientAccountId { get; set; }
    public Guid SubmittedByUserId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;
    public string Reference { get; set; } = string.Empty;
    public DateOnly PaidOn { get; set; }
    public string? Note { get; set; }
    public PaymentClaimStatus Status { get; set; } = PaymentClaimStatus.Pending;

    /// <summary>Client-generated idempotency key (a double click creates one claim).</summary>
    public Guid RequestId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewNote { get; set; }

    /// <summary>The payment recorded when the claim was confirmed.</summary>
    public Guid? PaymentId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Proof of payment (bank slip, receipt: PNG/JPEG/WebP/PDF) attached to a payment or a client's payment claim.</summary>
public class PaymentProof : Entity
{
    public Guid ClientAccountId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? PaymentClaimId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public Guid UploadedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A client retainer/contract: recurring lines invoiced every billing period by the recurring invoice job.</summary>
public class Contract : AuditedEntity, IConcurrencyStamped
{
    public string Number { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid ClientAccountId { get; set; }
    public ContractStatus Status { get; set; } = ContractStatus.Draft;
    public string Currency { get; set; } = "USD";
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public BillingFrequency BillingFrequency { get; set; } = BillingFrequency.Monthly;
    public bool AutoRenew { get; set; }
    public int RenewalTermMonths { get; set; } = 12;
    public int NoticePeriodDays { get; set; } = 30;
    public int PaymentTermsDays { get; set; } = 14;

    /// <summary>Issue generated invoices automatically instead of leaving them as drafts (null = billing setting).</summary>
    public bool? AutoIssueInvoices { get; set; }

    /// <summary>Index of the next billing period to invoice (0 = the period starting on <see cref="StartDate"/>).</summary>
    public int NextPeriodIndex { get; set; }

    public Guid? ProposalId { get; set; }
    public int? ProposalVersion { get; set; }
    public string? Notes { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<ContractLine> Lines { get; set; } = new();
}

/// <summary>A contract line, priced per billing period of its contract.</summary>
public class ContractLine : PricedLine
{
    public Guid ContractId { get; set; }
}
