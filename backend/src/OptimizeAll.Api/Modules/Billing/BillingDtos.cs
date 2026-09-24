using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Domain.Billing;

namespace OptimizeAll.Api.Modules.Billing;

// ---------------------------------------------------------------- Lines & totals

/// <summary>A priced line as sent by the UI. The tax rate is referenced by id and snapshotted server-side.</summary>
public sealed class PriceLineRequest
{
    [Required, StringLength(500, MinimumLength = 1)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(100), RegularExpression("^[a-z0-9][a-z0-9-]*$", ErrorMessage = "Use a lower-case slug (letters, digits, dashes).")]
    public string? ServiceSlug { get; set; }

    /// <summary>Proposals only: the service package the line came from.</summary>
    [MaxLength(100), RegularExpression("^[a-z0-9][a-z0-9-]*$", ErrorMessage = "Use a lower-case slug (letters, digits, dashes).")]
    public string? PackageSlug { get; set; }

    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public DiscountType DiscountType { get; set; } = DiscountType.None;
    public decimal DiscountValue { get; set; }
    public Guid? TaxRateId { get; set; }

    /// <summary>Proposals only (contracts and invoices ignore it).</summary>
    public Recurrence Recurrence { get; set; } = Recurrence.OneTime;
}

public sealed record PriceLineDto(
    Guid Id, int Position, string Description, string? ServiceSlug, string? PackageSlug, decimal Quantity, decimal UnitPrice,
    DiscountType DiscountType, decimal DiscountValue, Guid? TaxRateId, string? TaxName, decimal TaxPercent, bool TaxInclusive,
    Recurrence Recurrence, decimal DiscountAmount, decimal Subtotal, decimal TaxAmount, decimal Total);

public sealed record TotalsDto(
    string Currency, decimal GrossTotal, decimal DiscountTotal, decimal Subtotal, decimal TaxTotal, decimal Total,
    IReadOnlyList<TaxBreakdown> Taxes);

public sealed record RecurringTotalsDto(
    decimal OneTimeTotal, decimal MonthlyTotal, decimal QuarterlyTotal, decimal AnnualTotal, decimal MonthlyRecurringValue,
    decimal FirstInvoiceTotal, decimal FirstYearValue);

public sealed class PreviewRequest
{
    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";

    [Required, MaxLength(Pricing.MaxLines)]
    public List<PriceLineRequest> Lines { get; set; } = new();
}

public sealed record PreviewResponse(IReadOnlyList<PriceLineDto> Lines, TotalsDto Totals, RecurringTotalsDto Recurring);

// ---------------------------------------------------------------- Invoices

public sealed class InvoiceDraftRequest
{
    [Required]
    public Guid? ClientAccountId { get; set; }

    [StringLength(3, MinimumLength = 3)]
    public string? Currency { get; set; }

    [Range(0, 365)]
    public int? PaymentTermsDays { get; set; }

    [MaxLength(4000)]
    public string? Notes { get; set; }

    [MaxLength(100)]
    public string? Reference { get; set; }

    [Required, MinLength(1), MaxLength(Pricing.MaxLines)]
    public List<PriceLineRequest> Lines { get; set; } = new();

    /// <summary>Required on update: the stamp the editor loaded.</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class StampRequest
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class IssueInvoiceRequest
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }

    /// <summary>Email the invoice to the client right after issuing.</summary>
    public bool Send { get; set; }
}

/// <summary>Sensitive finance action: needs <c>confirm: true</c>, a reason, and the current stamp.</summary>
public sealed class SensitiveInvoiceActionRequest
{
    [Required, StringLength(1000, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    public bool Confirm { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class InvoiceQuery : PageQuery
{
    public InvoiceStatus? Status { get; set; }
    public Guid? ClientAccountId { get; set; }
    public bool? OpenOnly { get; set; }
    public Guid? ContractId { get; set; }
}

public sealed record InvoiceSummaryDto(
    Guid Id, string? Number, Guid ClientAccountId, string ClientName, InvoiceStatus Status, string Currency, DateOnly? IssueDate,
    DateOnly? DueDate, decimal Total, decimal AmountPaid, decimal Balance, int DaysOverdue, DateTime CreatedAt, Guid? ContractId);

public sealed record PaymentDto(
    Guid Id, Guid InvoiceId, string? InvoiceNumber, Guid ClientAccountId, string? ClientName, decimal Amount, string Currency,
    PaymentMethod Method, string Reference, DateOnly PaidOn, string? Notes, Guid RequestId, string? RecordedBy, DateTime CreatedAt,
    Guid? ReversalOfPaymentId = null, DateTime? ReversedAt = null, PaymentReversalKind? ReversalKind = null, string? ReversalReason = null,
    DateTime? UpdatedAt = null, Guid ConcurrencyStamp = default);

public sealed record CreditApplicationDto(Guid CreditNoteId, string CreditNoteNumber, Guid InvoiceId, string? InvoiceNumber, decimal Amount, DateTime AppliedAt);

public sealed record ReminderDto(string Kind, DateTime SentAt);

public sealed record InvoiceDto(
    Guid Id, string? Number, Guid ClientAccountId, string ClientName, string? ClientBillingEmail, InvoiceStatus Status,
    string Currency, DateOnly? IssueDate, DateOnly? DueDate, int PaymentTermsDays, TotalsDto Totals, decimal AmountPaid,
    decimal AmountCredited, decimal AmountWrittenOff, decimal Balance, int DaysOverdue, string? Notes, string? Reference,
    Guid? ContractId, Guid? ProposalId, DateOnly? PeriodStart, DateOnly? PeriodEnd, IReadOnlyList<PriceLineDto> Lines,
    IReadOnlyList<PaymentDto> Payments, IReadOnlyList<CreditApplicationDto> Credits, IReadOnlyList<ReminderDto> Reminders,
    string? PublicUrl, DateTime? IssuedAt, DateTime? SentAt, DateTime? PaidAt, DateTime? VoidedAt, string? VoidReason,
    DateTime? WrittenOffAt, string? WriteOffReason, Guid? IssuedByUserId, DateTime CreatedAt, Guid ConcurrencyStamp);

public sealed class RecordPaymentRequest
{
    /// <summary>Client-generated idempotency key; retrying with the same id returns the original payment.</summary>
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

    /// <summary>The invoice stamp the user saw: a payment recorded meanwhile by someone else makes this request fail (409).</summary>
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record PaymentRecordedDto(PaymentDto Payment, InvoiceDto Invoice, bool Replayed);

/// <summary>Edit of a payment's details. The amount is not editable (reverse and record again instead).</summary>
public sealed class UpdatePaymentDetailsRequest
{
    [StringLength(120, MinimumLength = 1)]
    public string? Reference { get; set; }

    public DateOnly? PaidOn { get; set; }

    public PaymentMethod? Method { get; set; }

    /// <summary>New notes; an empty string clears them, null leaves them unchanged.</summary>
    [MaxLength(1000)]
    public string? Notes { get; set; }

    [Required, StringLength(1000, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>The payment's stamp the user saw.</summary>
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class ReversePaymentRequest
{
    /// <summary>Idempotency key of the reversal (a retried request never reverses twice).</summary>
    [Required]
    public Guid? RequestId { get; set; }

    [Required]
    public PaymentReversalKind? Kind { get; set; }

    [Required, StringLength(1000, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Date of the refund / correction (defaults to today).</summary>
    public DateOnly? ReversedOn { get; set; }

    public bool Confirm { get; set; }

    /// <summary>The payment's stamp the user saw.</summary>
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record PaymentReversedDto(PaymentDto Payment, PaymentDto Reversal, InvoiceDto Invoice, bool Replayed);


public sealed class PaymentQuery : PageQuery
{
    public Guid? ClientAccountId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

// ---------------------------------------------------------------- Credit notes

public sealed class CreateCreditNoteRequest
{
    [Required]
    public Guid? RequestId { get; set; }

    /// <summary>Invoice being corrected; the credit is applied to it immediately (up to its balance).</summary>
    public Guid? InvoiceId { get; set; }

    /// <summary>Required when no invoice is given (an unapplied credit for the client).</summary>
    public Guid? ClientAccountId { get; set; }

    [StringLength(3, MinimumLength = 3)]
    public string? Currency { get; set; }

    [Range(typeof(decimal), "0.001", "1000000000")]
    public decimal Amount { get; set; }

    [Required, StringLength(1000, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class ApplyCreditNoteRequest
{
    [Required]
    public Guid? InvoiceId { get; set; }

    [Range(typeof(decimal), "0.001", "1000000000")]
    public decimal Amount { get; set; }
}

public sealed record CreditNoteDto(
    Guid Id, string Number, Guid ClientAccountId, string ClientName, Guid? InvoiceId, string? InvoiceNumber, string Currency,
    decimal Amount, decimal AmountApplied, decimal Remaining, CreditNoteStatus Status, string Reason, DateOnly IssueDate,
    IReadOnlyList<CreditApplicationDto> Applications, DateTime CreatedAt);

public sealed class CreditNoteQuery : PageQuery
{
    public Guid? ClientAccountId { get; set; }
    public CreditNoteStatus? Status { get; set; }
}

// ---------------------------------------------------------------- Contracts

public sealed class ContractRequest
{
    [Required]
    public Guid? ClientAccountId { get; set; }

    [Required, StringLength(200, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    [StringLength(3, MinimumLength = 3)]
    public string? Currency { get; set; }

    [Required]
    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }
    public BillingFrequency BillingFrequency { get; set; } = BillingFrequency.Monthly;
    public bool AutoRenew { get; set; }

    [Range(1, 60)]
    public int RenewalTermMonths { get; set; } = 12;

    [Range(0, 365)]
    public int NoticePeriodDays { get; set; } = 30;

    [Range(0, 365)]
    public int? PaymentTermsDays { get; set; }

    public bool? AutoIssueInvoices { get; set; }

    [MaxLength(4000)]
    public string? Notes { get; set; }

    [Required, MinLength(1), MaxLength(Pricing.MaxLines)]
    public List<PriceLineRequest> Lines { get; set; } = new();

    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class CancelContractRequest
{
    [Required, StringLength(1000, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    public bool Confirm { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class ContractQuery : PageQuery
{
    public ContractStatus? Status { get; set; }
    public Guid? ClientAccountId { get; set; }
}

public sealed record ContractSummaryDto(
    Guid Id, string Number, string Title, Guid ClientAccountId, string ClientName, ContractStatus Status, string Currency,
    BillingFrequency BillingFrequency, DateOnly StartDate, DateOnly? EndDate, decimal AmountPerPeriod, decimal MonthlyValue,
    DateOnly? NextInvoiceDate, bool AutoRenew);

public sealed record ContractDto(
    Guid Id, string Number, string Title, Guid ClientAccountId, string ClientName, ContractStatus Status, string Currency,
    DateOnly StartDate, DateOnly? EndDate, BillingFrequency BillingFrequency, bool AutoRenew, int RenewalTermMonths,
    int NoticePeriodDays, int PaymentTermsDays, bool? AutoIssueInvoices, int NextPeriodIndex, DateOnly? NextInvoiceDate,
    Guid? ProposalId, int? ProposalVersion, string? ProposalNumber, string? Notes, IReadOnlyList<PriceLineDto> Lines,
    TotalsDto TotalsPerPeriod, decimal MonthlyValue, IReadOnlyList<InvoiceSummaryDto> Invoices, DateTime? ActivatedAt,
    DateTime? CancelledAt, string? CancelReason, DateTime CreatedAt, Guid ConcurrencyStamp);

// ---------------------------------------------------------------- Tax rates & settings

public sealed class TaxRateRequest
{
    [Required, StringLength(80, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "100")]
    public decimal RatePercent { get; set; }

    public bool Inclusive { get; set; }

    [StringLength(2, MinimumLength = 2)]
    public string? CountryCode { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Set false once finance has confirmed a seeded example rate.</summary>
    public bool NeedsReview { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record TaxRateDto(
    Guid Id, string Name, decimal RatePercent, bool Inclusive, string? CountryCode, bool IsActive, bool NeedsReview, string? Notes,
    Guid ConcurrencyStamp);

public sealed class UpdateBillingSettingsRequest
{
    [Required]
    public BillingSettings? Settings { get; set; }

    [Required, StringLength(1000, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    public bool Confirm { get; set; }
}

public sealed record ClientOptionDto(Guid Id, string Name, string Slug, string Currency, string CountryCode, string Status, string? BillingEmail);

// ---------------------------------------------------------------- Reports

public sealed record CurrencyAmount(string Currency, decimal Amount);

public sealed record AgingRowDto(
    Guid ClientAccountId, string ClientName, string Currency, decimal Current, decimal Days1To30, decimal Days31To60,
    decimal Days61To90, decimal Over90, decimal Total);

public sealed record AgingReportDto(DateOnly AsOf, IReadOnlyList<AgingRowDto> Rows, IReadOnlyList<AgingRowDto> Totals);

public sealed record RevenueRowDto(string Key, string Label, string Currency, decimal Invoiced, decimal Collected);

public sealed record RevenueReportDto(string GroupBy, DateOnly From, DateOnly To, IReadOnlyList<RevenueRowDto> Rows);

public sealed record MrrRowDto(Guid ClientAccountId, string ClientName, string Currency, int ActiveContracts, decimal Mrr, decimal Arr);

public sealed record MrrReportDto(IReadOnlyList<MrrRowDto> Rows, IReadOnlyList<CurrencyAmount> MrrByCurrency, IReadOnlyList<CurrencyAmount> ArrByCurrency);

public sealed record CollectionsRowDto(string Month, string Currency, PaymentMethod Method, int Payments, decimal Amount);

public sealed record CollectionsReportDto(DateOnly From, DateOnly To, IReadOnlyList<CollectionsRowDto> Rows);

public sealed record BillingOverviewDto(
    IReadOnlyList<CurrencyAmount> Outstanding, IReadOnlyList<CurrencyAmount> Overdue, IReadOnlyList<CurrencyAmount> Mrr,
    IReadOnlyList<CurrencyAmount> CollectedLast30Days, int DraftInvoices, int OverdueInvoices, int ActiveContracts,
    IReadOnlyList<InvoiceSummaryDto> RecentlyOverdue, IReadOnlyList<PaymentDto> RecentPayments);

// ---------------------------------------------------------------- Public & client portal

public sealed record PaymentInstructionsDto(
    string CompanyName, string? CompanyAddress, string? CompanyTaxId, string? CompanyEmail, string? BankDetails,
    string? PaymentLinkText, string? PaymentInstructions, string? InvoiceFooter, bool OnlinePaymentAvailable);

public sealed record PublicInvoiceDto(
    string? Number, string ClientName, string? ClientAddress, string? ClientTaxId, InvoiceStatus Status, string Currency,
    DateOnly? IssueDate, DateOnly? DueDate, TotalsDto Totals, decimal AmountPaid, decimal AmountCredited, decimal Balance,
    string? Notes, DateOnly? PeriodStart, DateOnly? PeriodEnd, IReadOnlyList<PriceLineDto> Lines, PaymentInstructionsDto Payment);

public sealed record StatementLineDto(DateOnly Date, string Type, string Reference, string Description, decimal Debit, decimal Credit, decimal Balance);

public sealed record StatementDto(
    Guid ClientAccountId, string ClientName, string Currency, DateOnly From, DateOnly To, decimal OpeningBalance,
    IReadOnlyList<StatementLineDto> Lines, decimal ClosingBalance);

public sealed record ClientBillingSummaryDto(
    IReadOnlyList<ClientOrgBillingDto> Organizations, IReadOnlyList<CurrencyAmount> Outstanding, IReadOnlyList<CurrencyAmount> Overdue,
    int OpenInvoices, int ProposalsAwaitingResponse);

public sealed record ClientOrgBillingDto(Guid ClientAccountId, string Name, string Currency, string Role);

public sealed record OnlinePaymentDto(bool Available, string? RedirectUrl, string Message);
