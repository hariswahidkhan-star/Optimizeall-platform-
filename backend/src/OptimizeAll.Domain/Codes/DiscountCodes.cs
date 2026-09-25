using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Codes;

// Discount-code (affiliate) sales: a brand gives the platform discount codes, the codes are assigned to people or rate
// groups, participants report the sales made with their code, a reviewer approves them and the approval writes a
// commission to the ledger. See docs/DISCOUNT_CODES.md. Everything here is data plus the pure CodePayoutCalculator.

public enum CodeProgramStatus
{
    /// <summary>Being set up: codes can be added and assigned, but participants can't see it or report sales.</summary>
    Draft,
    /// <summary>Participants see their codes and report sales (orders inside the program window).</summary>
    Active,
    /// <summary>Temporarily closed to new sales; pending sales can still be reviewed.</summary>
    Paused,
    /// <summary>Retired. Read-only; approved earnings are unaffected.</summary>
    Archived,
}

/// <summary>How a sale's commission is computed.</summary>
public enum CodePayoutType
{
    /// <summary>A fixed amount per approved sale.</summary>
    FlatPerSale,
    /// <summary>A percentage of the order value net of the discount.</summary>
    PercentOfNet,
}

/// <summary>
/// Code status. <see cref="Expired"/> is never stored: it is derived from the code's (or program's) end date so a code
/// "expires" without a job; filters and the API report it.
/// </summary>
public enum DiscountCodeStatus
{
    Available,
    Assigned,
    Paused,
    Expired,
    Retired,
}

public enum DiscountCodeSource
{
    Manual,
    Import,
    Generated,
}

public enum CodeAssignmentTarget
{
    /// <summary>A personal code: only this person may report sales with it.</summary>
    Person,
    /// <summary>A shared code: every member of the rate group may report sales with it (attributed to whoever submits).</summary>
    Group,
}

public enum CodeSaleStatus
{
    /// <summary>Waiting for review.</summary>
    Pending,
    /// <summary>A reviewer asked the participant for more information; the participant edits the sale to resubmit.</summary>
    NeedsInfo,
    /// <summary>Commission recorded in the ledger.</summary>
    Approved,
    Rejected,
    /// <summary>Withdrawn by the participant before a decision.</summary>
    Withdrawn,
    /// <summary>The brand reported the order refunded/cancelled before it was approved (no earnings).</summary>
    Cancelled,
    /// <summary>Refunded after approval: its earnings were reversed (a clawback when already paid).</summary>
    Refunded,
}

public enum CodeSaleSource
{
    /// <summary>Reported by the participant.</summary>
    Participant,
    /// <summary>Entered by staff.</summary>
    Admin,
    /// <summary>Created from the brand's sales report for a use nobody claimed (attributed to the code's assignee).</summary>
    Import,
}

/// <summary>Reconciliation against the brand's sales report.</summary>
public enum CodeSaleVerification
{
    /// <summary>Not in any imported report yet.</summary>
    Unverified,
    /// <summary>The brand's report has the order with the same code, amount (±1 %) and date (±1 day).</summary>
    Matched,
    /// <summary>The brand's report has the order but something differs (see the verification note).</summary>
    Mismatch,
    /// <summary>Created from the brand's report (nobody had claimed it).</summary>
    ReportedByBrand,
}

public enum CodeImportKind
{
    Codes,
    Sales,
}

/// <summary>A brand's discount-code program: codes, validity, terms and payout rules (docs/DISCOUNT_CODES.md).</summary>
public class CodeProgram : AuditedEntity, IConcurrencyStamped
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The company that issued the codes (shown to participants).</summary>
    public string BrandName { get; set; } = string.Empty;

    /// <summary>Optional link to the agency client (company) the program is run for.</summary>
    public Guid? ClientAccountId { get; set; }

    /// <summary>Optional campaign the program belongs to; its tracking links count clicks for conversion reporting.</summary>
    public Guid? CampaignId { get; set; }

    public string? Description { get; set; }
    public string? Terms { get; set; }

    /// <summary>Store / landing page; participants' share links point here with their code.</summary>
    public string? StoreUrl { get; set; }

    /// <summary>What the customer gets, e.g. "15% off your first order" (display only).</summary>
    public string? DiscountLabel { get; set; }

    /// <summary>Currency of commissions, caps and budget. Orders in another currency are converted at the order date.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Orders placed in [StartsAt, EndsAt] count. EndsAt null = open-ended.</summary>
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    public CodeProgramStatus Status { get; set; } = CodeProgramStatus.Draft;

    // ---- payout rules (money: audited, bump PayoutVersion)
    public CodePayoutType PayoutType { get; set; } = CodePayoutType.FlatPerSale;
    public decimal? FlatAmount { get; set; }

    /// <summary>Percent of the net order value (0–100).</summary>
    public decimal? Percent { get; set; }

    /// <summary>Per-person limit on commissions for sales reported on one (UTC) day.</summary>
    public decimal? DailyCapPerPerson { get; set; }

    /// <summary>Per-person limit on commissions over the whole program.</summary>
    public decimal? ProgramCapPerPerson { get; set; }

    /// <summary>Total the program may pay out (all people). Null = unlimited.</summary>
    public decimal? BudgetAmount { get; set; }

    /// <summary>Orders older than this many days can't be reported.</summary>
    public int MaxOrderAgeDays { get; set; } = 60;

    /// <summary>Participants must attach a screenshot/receipt image.</summary>
    public bool RequireProof { get; set; }

    /// <summary>Incremented whenever the payout rules (rates, tiers, caps, budget) change; recorded on every commission.</summary>
    public int PayoutVersion { get; set; } = 1;

    public Guid CreatedByUserId { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<CodeProgramTier> Tiers { get; set; } = new();

    public bool IsWithinWindow(DateTime atUtc) => atUtc >= StartsAt && (EndsAt is null || atUtc <= EndsAt);
}

/// <summary>
/// A tier: from the (<see cref="ThresholdSales"/> + 1)-th approved sale of a person the per-sale rate becomes
/// <see cref="FlatAmount"/> / <see cref="Percent"/> (when set), and reaching <see cref="ThresholdSales"/> approved sales
/// pays <see cref="BonusAmount"/> once (when set).
/// </summary>
public class CodeProgramTier : Entity
{
    public Guid ProgramId { get; set; }
    public int ThresholdSales { get; set; }
    public decimal? FlatAmount { get; set; }
    public decimal? Percent { get; set; }
    public decimal? BonusAmount { get; set; }
}

/// <summary>A negotiated per-person or per-rate-group payout for one program (replaces the program's per-sale rate).</summary>
public class CodePayoutOverride : Entity
{
    public Guid ProgramId { get; set; }
    public CodeAssignmentTarget Target { get; set; }
    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }
    public CodePayoutType PayoutType { get; set; }
    public decimal? FlatAmount { get; set; }
    public decimal? Percent { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? EndedAt { get; set; }
    public Guid? EndedByUserId { get; set; }
    public string? EndReason { get; set; }
}

/// <summary>One discount code of a program.</summary>
public class DiscountCode : AuditedEntity, IConcurrencyStamped
{
    public const int MaxLength = 64;

    public Guid ProgramId { get; set; }

    /// <summary>The code as the brand wrote it.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Upper-cased, trimmed: unique per program (codes are case-insensitive at checkout).</summary>
    public string NormalizedCode { get; set; } = string.Empty;

    /// <summary>Stored status (Available / Assigned / Paused / Retired); Expired is derived.</summary>
    public DiscountCodeStatus Status { get; set; } = DiscountCodeStatus.Available;

    public DiscountCodeSource Source { get; set; } = DiscountCodeSource.Manual;

    /// <summary>Optional per-code validity inside the program window.</summary>
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    public string? Note { get; set; }
    public Guid? ImportBatchId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public static string Normalize(string code) => code.Trim().ToUpperInvariant();

    /// <summary>The status to show: Expired once the code's (or the program's) end has passed, unless retired.</summary>
    public DiscountCodeStatus EffectiveStatus(DateTime nowUtc, DateTime? programEndsAt)
    {
        if (Status == DiscountCodeStatus.Retired) return Status;
        var end = ValidTo ?? programEndsAt;
        return end is { } e && e < nowUtc ? DiscountCodeStatus.Expired : Status;
    }

    /// <summary>True when an order placed at <paramref name="atUtc"/> may use this code (per-code window only).</summary>
    public bool IsValidOn(DateTime atUtc) => (ValidFrom is null || atUtc >= ValidFrom) && (ValidTo is null || atUtc <= ValidTo);
}

/// <summary>
/// Who may report sales with a code, and when. At most one live assignment per code; ending one keeps the row (history).
/// Orders are attributed by order date: the assignment whose window contains it.
/// </summary>
public class DiscountCodeAssignment : Entity
{
    public Guid CodeId { get; set; }
    public Guid ProgramId { get; set; }
    public CodeAssignmentTarget Target { get; set; }
    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? EndedAt { get; set; }
    public Guid? EndedByUserId { get; set; }
    public string? EndReason { get; set; }

    /// <summary>The earlier of the planned end and the moment it was ended.</summary>
    public DateTime? EffectiveTo => EndedAt is { } ended && (ValidTo is null || ended < ValidTo) ? ended : ValidTo;

    /// <summary>True when an order placed at <paramref name="atUtc"/> falls inside the assignment.</summary>
    public bool Covers(DateTime atUtc) => atUtc >= ValidFrom && (EffectiveTo is null || atUtc < EffectiveTo);

    public bool IsLive(DateTime nowUtc) => EndedAt is null && (ValidTo is null || ValidTo > nowUtc);
}

/// <summary>A sale made with a discount code: reported by a participant, entered by staff or created from the brand's report.</summary>
public class CodeSale : AuditedEntity, IConcurrencyStamped
{
    public const int MaxOrderReference = 100;

    public Guid ProgramId { get; set; }
    public Guid CodeId { get; set; }

    /// <summary>The participant the sale (and its commission) is attributed to.</summary>
    public Guid UserId { get; set; }

    /// <summary>The assignment that covered the order date (personal, or the group's shared code).</summary>
    public Guid? AssignmentId { get; set; }
    public Guid? GroupId { get; set; }

    public string OrderReference { get; set; } = string.Empty;

    /// <summary>Upper-cased, trimmed order reference.</summary>
    public string NormalizedOrderReference { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="NormalizedOrderReference"/> while the sale holds the order (Pending, NeedsInfo, Approved, Refunded, Cancelled);
    /// null once withdrawn or rejected (the order may then be reported again). Unique per program: the final guard against the same order being claimed twice.
    /// </summary>
    public string? ActiveOrderKey { get; set; }

    public DateTime OrderDate { get; set; }

    /// <summary>Order value paid by the customer, net of the discount, in <see cref="Currency"/>.</summary>
    public decimal NetAmount { get; set; }

    /// <summary>Discount given, in <see cref="Currency"/> (0 when unknown).</summary>
    public decimal DiscountAmount { get; set; }

    public string Currency { get; set; } = "USD";

    /// <summary>Order currency → program currency at the order date (1 when equal).</summary>
    public decimal ExchangeRate { get; set; } = 1m;
    public Guid? ExchangeRateId { get; set; }

    /// <summary><see cref="NetAmount"/> in the program currency (commission base).</summary>
    public decimal ProgramNetAmount { get; set; }
    public decimal ProgramDiscountAmount { get; set; }

    public string? ProductNote { get; set; }
    public Guid? ProofFileId { get; set; }

    public CodeSaleStatus Status { get; set; } = CodeSaleStatus.Pending;
    public CodeSaleSource Source { get; set; } = CodeSaleSource.Participant;
    public Guid CreatedByUserId { get; set; }
    public DateTime SubmittedAt { get; set; }

    /// <summary>Informational commission estimate at submission (before caps).</summary>
    public decimal? EstimatedCommission { get; set; }

    public DateTime? DecidedAt { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public string? DecisionReason { get; set; }

    // ---- priced at approval
    public decimal? CommissionAmount { get; set; }
    public string? PayoutSourceLabel { get; set; }
    public string? AppliedCaps { get; set; }
    public int? PayoutVersion { get; set; }

    // ---- reconciliation with the brand's report
    public CodeSaleVerification Verification { get; set; } = CodeSaleVerification.Unverified;
    public string? VerificationNote { get; set; }
    public decimal? ReportedNetAmount { get; set; }
    public DateTime? ReportedOrderDate { get; set; }
    public Guid? ImportBatchId { get; set; }

    public DateTime? RefundedAt { get; set; }
    public string? RefundReason { get; set; }

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public static string NormalizeOrderReference(string reference) => reference.Trim().ToUpperInvariant();

    public bool IsOpen => Status is CodeSaleStatus.Pending or CodeSaleStatus.NeedsInfo;

    /// <summary>Statuses whose order reference stays claimed.</summary>
    public static bool HoldsOrder(CodeSaleStatus status) =>
        status is CodeSaleStatus.Pending or CodeSaleStatus.NeedsInfo or CodeSaleStatus.Approved or CodeSaleStatus.Refunded or CodeSaleStatus.Cancelled;
}

/// <summary>Status history of a sale (participant timeline and audit).</summary>
public class CodeSaleEvent : Entity
{
    public Guid SaleId { get; set; }
    public CodeSaleStatus? FromStatus { get; set; }
    public CodeSaleStatus ToStatus { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public string? Reason { get; set; }
    public DateTime At { get; set; }
}

/// <summary>A committed CSV import (codes from the brand, or the brand's sales report) and its outcome.</summary>
public class CodeImportBatch : Entity
{
    public Guid ProgramId { get; set; }
    public CodeImportKind Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int Rows { get; set; }
    public int Created { get; set; }
    public int Matched { get; set; }
    public int Flagged { get; set; }
    public int Rejected { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
}
