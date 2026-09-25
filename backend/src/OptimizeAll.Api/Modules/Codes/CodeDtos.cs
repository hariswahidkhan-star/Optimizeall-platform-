using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Codes;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;

namespace OptimizeAll.Api.Modules.Codes;

public static class CodeLimits
{
    public const int MaxCsvBytes = 10 * 1024 * 1024;
    public const int MaxCodeRows = 50_000;
    public const int MaxSaleRows = 50_000;
    public const int MaxGenerate = 5_000;
    public const int MaxBulk = 200;
    public const long MaxProofBytes = 10 * 1024 * 1024;
}

// ------------------------------------------------------------------ programs

public sealed class CodeTierInput
{
    [Range(1, 100_000)]
    public int ThresholdSales { get; set; }

    [Range(typeof(decimal), "0.0001", "1000000")]
    public decimal? FlatAmount { get; set; }

    [Range(typeof(decimal), "0.0001", "100")]
    public decimal? Percent { get; set; }

    [Range(typeof(decimal), "0.0001", "1000000")]
    public decimal? BonusAmount { get; set; }
}

/// <summary>Payout rules: per-sale rate, tiers, caps and budget (all in the program currency).</summary>
public class CodePayoutRulesInput
{
    [Required, DefinedEnum]
    public CodePayoutType? PayoutType { get; set; }

    [Range(typeof(decimal), "0.0001", "1000000")]
    public decimal? FlatAmount { get; set; }

    [Range(typeof(decimal), "0.0001", "100")]
    public decimal? Percent { get; set; }

    [MaxLength(20)]
    public List<CodeTierInput> Tiers { get; set; } = new();

    [Range(typeof(decimal), "0.0001", "100000000")]
    public decimal? DailyCapPerPerson { get; set; }

    [Range(typeof(decimal), "0.0001", "100000000")]
    public decimal? ProgramCapPerPerson { get; set; }

    [Range(typeof(decimal), "0.0001", "1000000000")]
    public decimal? BudgetAmount { get; set; }
}

public class CodeProgramInput
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(120, MinimumLength = 2)]
    public string BrandName { get; set; } = string.Empty;

    public Guid? ClientAccountId { get; set; }
    public Guid? CampaignId { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(4000)]
    public string? Terms { get; set; }

    [MaxLength(500)]
    public string? StoreUrl { get; set; }

    [MaxLength(120)]
    public string? DiscountLabel { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";

    [Required]
    public DateTime? StartsAt { get; set; }

    public DateTime? EndsAt { get; set; }

    [Range(1, 365)]
    public int MaxOrderAgeDays { get; set; } = 60;

    public bool RequireProof { get; set; }
}

public sealed class CreateCodeProgramRequest : CodeProgramInput
{
    [Required]
    public CodePayoutRulesInput? Payout { get; set; }

    /// <summary>Open the program to participants immediately (otherwise it starts as a draft).</summary>
    public bool Activate { get; set; }
}

public sealed class UpdateCodeProgramRequest : CodeProgramInput
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class UpdatePayoutRulesRequest : CodePayoutRulesInput
{
    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Must be true: payout rules move money.</summary>
    public bool Confirm { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class ChangeProgramStatusRequest
{
    [Required, DefinedEnum]
    public CodeProgramStatus? Status { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class CreatePayoutOverrideRequest
{
    [Required, DefinedEnum]
    public CodeAssignmentTarget? Target { get; set; }

    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }

    [Required, DefinedEnum]
    public CodePayoutType? PayoutType { get; set; }

    [Range(typeof(decimal), "0.0001", "1000000")]
    public decimal? FlatAmount { get; set; }

    [Range(typeof(decimal), "0.0001", "100")]
    public decimal? Percent { get; set; }

    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class EndPayoutOverrideRequest
{
    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class CodeProgramQuery : PageQuery
{
    [DefinedEnum]
    public CodeProgramStatus? Status { get; set; }
}

public sealed record ProgramRefDto(Guid Id, string Name, string BrandName, string Currency);

public sealed record NamedRefDto(Guid Id, string Name);

public sealed record CodeTierDto(int ThresholdSales, decimal? FlatAmount, decimal? Percent, decimal? BonusAmount);

public sealed record CodePayoutOverrideDto(
    Guid Id, CodeAssignmentTarget Target, UserRefDto? Person, NamedRefDto? Group, CodePayoutType PayoutType, decimal? FlatAmount,
    decimal? Percent, string Description, string Reason, DateTime CreatedAt, UserRefDto? CreatedBy, DateTime? EndedAt, string? EndReason, bool IsActive);

public sealed record CodeProgramStatsDto(
    int Codes, int AvailableCodes, int AssignedCodes, int PendingSales, int ApprovedSales, decimal CommissionApproved,
    decimal CommissionPaid, decimal? BudgetRemaining);

public sealed record CodeProgramListItemDto(
    Guid Id, string Name, string BrandName, CodeProgramStatus Status, string Currency, DateTime StartsAt, DateTime? EndsAt,
    string PayoutSummary, int Codes, int AssignedCodes, int PendingSales, int ApprovedSales, decimal CommissionApproved, DateTime UpdatedAt);

public sealed record CodeProgramDto(
    Guid Id, string Name, string BrandName, NamedRefDto? Client, NamedRefDto? Campaign, string? Description, string? Terms, string? StoreUrl,
    string? DiscountLabel, string Currency, DateTime StartsAt, DateTime? EndsAt, CodeProgramStatus Status, CodePayoutType PayoutType,
    decimal? FlatAmount, decimal? Percent, IReadOnlyList<CodeTierDto> Tiers, decimal? DailyCapPerPerson, decimal? ProgramCapPerPerson,
    decimal? BudgetAmount, int MaxOrderAgeDays, bool RequireProof, int PayoutVersion, string PayoutSummary,
    IReadOnlyList<CodePayoutOverrideDto> Overrides, CodeProgramStatsDto Stats, DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp);

// ------------------------------------------------------------------ codes

public sealed class AddCodeRequest
{
    [Required, StringLength(DiscountCode.MaxLength, MinimumLength = 2)]
    public string Code { get; set; } = string.Empty;

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }
}

public sealed class GenerateCodesRequest
{
    /// <summary># = digit, ? = letter, * = letter or digit; anything else literal (e.g. GLOW-????-##).</summary>
    [Required, StringLength(40, MinimumLength = 3)]
    public string Pattern { get; set; } = string.Empty;

    [Range(1, CodeLimits.MaxGenerate)]
    public int Count { get; set; }

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }
}

public sealed class UpdateCodeRequest
{
    /// <summary>Available (resume), Paused or Retired. Assigned/Expired are not set by hand.</summary>
    [DefinedEnum]
    public DiscountCodeStatus? Status { get; set; }

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class DiscountCodeQuery : PageQuery
{
    [DefinedEnum]
    public DiscountCodeStatus? Status { get; set; }

    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }
}

public sealed record CodeAssignmentDto(
    Guid Id, Guid CodeId, string Code, CodeAssignmentTarget Target, UserRefDto? Person, NamedRefDto? Group, DateTime ValidFrom,
    DateTime? ValidTo, DateTime? EndedAt, string? EndReason, string Reason, DateTime CreatedAt, UserRefDto? CreatedBy, bool IsLive);

public sealed record DiscountCodeDto(
    Guid Id, Guid ProgramId, string Code, DiscountCodeStatus Status, DiscountCodeSource Source, DateTime? ValidFrom, DateTime? ValidTo,
    string? Note, CodeAssignmentDto? Assignment, int Sales, DateTime CreatedAt, Guid ConcurrencyStamp);

public sealed record DiscountCodeDetailDto(DiscountCodeDto Code, ProgramRefDto Program, IReadOnlyList<CodeAssignmentDto> History);

public sealed record CodeIssueDto(int? Row, string? Value, string Code, string Message);

public sealed record CodeImportResultDto(
    bool DryRun, int Rows, int Valid, int Created, int Duplicates, IReadOnlyList<CodeIssueDto> Rejected, IReadOnlyList<CodeIssueDto> Warnings,
    IReadOnlyList<string> Sample);

// ------------------------------------------------------------------ assignment

public sealed class AssignCodeRequest
{
    [Required, DefinedEnum]
    public CodeAssignmentTarget? Target { get; set; }

    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }

    /// <summary>Default now. May be in the past (orders from then on are attributed) but not before the program starts.</summary>
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    /// <summary>End the code's current assignment (history is kept) instead of answering 409 code.already_assigned.</summary>
    public bool Reassign { get; set; }

    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class UnassignCodeRequest
{
    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class AutoAssignRequest
{
    /// <summary>Give every member of this rate group without a live personal code in the program one unique available code.</summary>
    [Required]
    public Guid? GroupId { get; set; }

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    public bool DryRun { get; set; }
}

public sealed record AutoAssignResultDto(
    bool DryRun, int Members, int Assigned, int AlreadyHadCode, int Skipped, int AvailableCodes, IReadOnlyList<CodeIssueDto> Issues,
    IReadOnlyList<CodeAssignmentDto> Assignments);

// ------------------------------------------------------------------ sales

/// <summary>multipart/form-data body of POST /me/code-sales.</summary>
public sealed class CreateCodeSaleForm
{
    [Required]
    public Guid? CodeId { get; set; }

    [Required, StringLength(CodeSale.MaxOrderReference, MinimumLength = 2)]
    public string OrderReference { get; set; } = string.Empty;

    [Required]
    public DateTimeOffset? OrderDate { get; set; }

    [Required, Range(typeof(decimal), "0.01", "100000000")]
    public decimal? NetAmount { get; set; }

    [Range(typeof(decimal), "0", "100000000")]
    public decimal? DiscountAmount { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";

    [MaxLength(1000)]
    public string? ProductNote { get; set; }

    public IFormFile? Proof { get; set; }
}

/// <summary>multipart/form-data body of PUT /me/code-sales/{id} (Pending or NeedsInfo). Omitted fields keep their value.</summary>
public sealed class UpdateCodeSaleForm
{
    [StringLength(CodeSale.MaxOrderReference, MinimumLength = 2)]
    public string? OrderReference { get; set; }

    public DateTimeOffset? OrderDate { get; set; }

    [Range(typeof(decimal), "0.01", "100000000")]
    public decimal? NetAmount { get; set; }

    [Range(typeof(decimal), "0", "100000000")]
    public decimal? DiscountAmount { get; set; }

    [StringLength(3, MinimumLength = 3)]
    public string? Currency { get; set; }

    [MaxLength(1000)]
    public string? ProductNote { get; set; }

    public IFormFile? Proof { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class WithdrawCodeSaleRequest
{
    [MaxLength(500)]
    public string? Reason { get; set; }
}

public sealed class AdminCreateSaleRequest
{
    [Required, StringLength(DiscountCode.MaxLength, MinimumLength = 2)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Who the sale is attributed to; default the code's personal assignee on the order date.</summary>
    public Guid? UserId { get; set; }

    [Required, StringLength(CodeSale.MaxOrderReference, MinimumLength = 2)]
    public string OrderReference { get; set; } = string.Empty;

    [Required]
    public DateTimeOffset? OrderDate { get; set; }

    [Required, Range(typeof(decimal), "0.01", "100000000")]
    public decimal? NetAmount { get; set; }

    [Range(typeof(decimal), "0", "100000000")]
    public decimal? DiscountAmount { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";

    [MaxLength(1000)]
    public string? ProductNote { get; set; }

    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

public enum CodeSaleDecision
{
    Approve,
    Reject,
    RequestInfo,
}

public sealed class CodeSaleDecisionRequest
{
    [Required, DefinedEnum]
    public CodeSaleDecision? Decision { get; set; }

    [MaxLength(1000)]
    public string? Reason { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class BulkApproveSalesRequest
{
    [Required, MinLength(1), MaxLength(CodeLimits.MaxBulk)]
    public List<Guid> SaleIds { get; set; } = new();

    [MaxLength(1000)]
    public string? Reason { get; set; }

    /// <summary>Only approve sales the brand's report matched (Verification = Matched); others are skipped.</summary>
    public bool OnlyMatched { get; set; } = true;
}

public sealed class RefundCodeSaleRequest
{
    [Required, StringLength(1000, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    public bool Confirm { get; set; }
}

public sealed class CodeSaleQuery : PageQuery
{
    public Guid? ProgramId { get; set; }

    [DefinedEnum]
    public CodeSaleStatus? Status { get; set; }

    [DefinedEnum]
    public CodeSaleVerification? Verification { get; set; }

    [DefinedEnum]
    public CodeSaleSource? Source { get; set; }

    public Guid? UserId { get; set; }
    public Guid? CodeId { get; set; }
    public Guid? GroupId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public sealed class MyCodeSalesQuery : PageQuery
{
    [DefinedEnum]
    public CodeSaleStatus? Status { get; set; }

    public Guid? CodeId { get; set; }
}

public sealed record CodeSaleEventDto(CodeSaleStatus? FromStatus, CodeSaleStatus ToStatus, string Action, UserRefDto? Actor, string? Reason, DateTime At);

public sealed record CodeSaleEarningDto(Guid Id, EarningType Type, EarningStatus Status, decimal Amount, string Currency, string? RateSourceLabel, DateTime CreatedAt);

public sealed record CodeSaleListItemDto(
    Guid Id, ProgramRefDto Program, NamedRefDto Code, UserRefDto Person, bool SharedCode, string OrderReference, DateTime OrderDate,
    decimal NetAmount, decimal DiscountAmount, string Currency, decimal ProgramNetAmount, CodeSaleStatus Status, CodeSaleSource Source,
    CodeSaleVerification Verification, DateTime SubmittedAt, decimal? EstimatedCommission, decimal? CommissionAmount, bool IsTestAccount,
    UserStatus UserStatus, bool CanDecide, Guid ConcurrencyStamp);

public sealed record CodeSaleDto(
    Guid Id, ProgramRefDto Program, NamedRefDto Code, UserRefDto Person, string? PersonEmail, NamedRefDto? Group, string OrderReference,
    DateTime OrderDate, decimal NetAmount, decimal DiscountAmount, string Currency, decimal ExchangeRate, decimal ProgramNetAmount,
    decimal ProgramDiscountAmount, string? ProductNote, string? ProofUrl, CodeSaleStatus Status, CodeSaleSource Source, UserRefDto? CreatedBy,
    DateTime SubmittedAt, decimal? EstimatedCommission, decimal? CommissionAmount, string? PayoutSourceLabel, IReadOnlyList<string> AppliedCaps,
    int? PayoutVersion, CodeSaleVerification Verification, string? VerificationNote, decimal? ReportedNetAmount, DateTime? ReportedOrderDate,
    DateTime? DecidedAt, UserRefDto? DecidedBy, string? DecisionReason, DateTime? RefundedAt, string? RefundReason, bool IsTestAccount,
    UserStatus UserStatus, bool CanDecide, string? CannotDecideReason, IReadOnlyList<CodeSaleEventDto> Events,
    IReadOnlyList<CodeSaleEarningDto> Earnings, Guid ConcurrencyStamp);

public sealed record BulkDecisionItemDto(Guid SaleId, bool Succeeded, string? Code, string? Message);

public sealed record BulkDecisionResultDto(int Requested, int Approved, int Skipped, IReadOnlyList<BulkDecisionItemDto> Items);

public sealed record SalesImportIssueDto(int Row, string? OrderReference, string? Code, string Outcome, string Message);

public sealed record SalesImportResultDto(
    bool DryRun, int Rows, int Matched, int Mismatched, int Created, int Cancelled, int Refunded, int Unchanged, int Rejected,
    IReadOnlyList<SalesImportIssueDto> Issues);

// ------------------------------------------------------------------ participant

public sealed record MyCodeStatsDto(int Sales, int Pending, int Approved, decimal GrossSales, decimal CommissionPending,
    decimal CommissionApproved, decimal CommissionPaid, int? Clicks);

public sealed record MyCodeDto(
    Guid CodeId, string Code, Guid ProgramId, string ProgramName, string BrandName, string? DiscountLabel, string? Description,
    string? Terms, string? StoreUrl, string? ShareUrl, bool Shared, DateTime AssignedFrom, DateTime? AssignedUntil, bool IsActive,
    string? InactiveReason, string Currency, string YourRate, IReadOnlyList<string> TierPerks, bool RequireProof, int MaxOrderAgeDays,
    DateTime ProgramStartsAt, DateTime? ProgramEndsAt, MyCodeStatsDto Stats);

public sealed record MyCodeSaleDto(
    Guid Id, Guid ProgramId, string ProgramName, string BrandName, Guid CodeId, string Code, string OrderReference, DateTime OrderDate,
    decimal NetAmount, decimal DiscountAmount, string Currency, string? ProductNote, string? ProofUrl, CodeSaleStatus Status, CodeSaleSource Source,
    DateTime SubmittedAt, string? DecisionReason, decimal? EstimatedCommission, decimal? CommissionAmount, string ProgramCurrency,
    bool CanEdit, bool CanWithdraw, IReadOnlyList<CodeSaleEventDto> Events, Guid ConcurrencyStamp);

// ------------------------------------------------------------------ reports

public enum CodeReportGrouping
{
    Program,
    Code,
    Person,
    Group,
}

public sealed class CodeReportQuery
{
    [DefinedEnum]
    public CodeReportGrouping GroupBy { get; set; } = CodeReportGrouping.Person;

    public Guid? ProgramId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public sealed record CodeReportRowDto(
    Guid? Id, string Label, string? Detail, int Uses, int Pending, int Approved, int Rejected, int Refunded, decimal GrossSales,
    decimal DiscountGiven, decimal NetSales, decimal CommissionPending, decimal CommissionApproved, decimal CommissionPaid,
    decimal CommissionReversed, int? Clicks, decimal? ConversionRate);

public sealed record CodeReportDto(
    CodeReportGrouping GroupBy, ProgramRefDto? Program, string? Currency, DateTime? From, DateTime? To, CodeReportRowDto Totals,
    IReadOnlyList<CodeReportRowDto> Rows, string Note);
