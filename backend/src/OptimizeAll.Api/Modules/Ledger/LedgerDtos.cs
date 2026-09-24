using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Rewards;

namespace OptimizeAll.Api.Modules.Ledger;

public sealed record CampaignRefDto(Guid Id, string Title);

public sealed record UserRefDto(Guid Id, string DisplayName, string Email);

public sealed record NextPayoutDto(
    string PeriodKey,
    DateTime CutoffAt,
    DateOnly PaymentDate,
    decimal MinimumPayoutAmount,
    bool MeetsMinimum,
    decimal EstimatedAmount);

/// <summary>Estimated rewards of open submissions in their original currency; <c>Converted</c> is false when no FX rate exists.</summary>
public sealed record PendingCurrencyDto(string Currency, decimal Amount, bool Converted);

/// <summary>Original-currency totals of ledger entries per status bucket.</summary>
public sealed record CurrencyBreakdownDto(
    string Currency, decimal PendingApproval, decimal Approved, decimal Scheduled, decimal Paid, decimal Reversed);

public sealed record EarningsSummaryDto(
    string Currency,
    decimal Pending,
    decimal Approved,
    decimal OnHold,
    decimal Scheduled,
    decimal Paid,
    decimal Reversed,
    decimal AvailableForNextPayout,
    decimal LifetimeEarned,
    NextPayoutDto NextPayout,
    bool ActiveHold,
    string? HoldMessage,
    IReadOnlyList<PendingCurrencyDto> PendingByCurrency,
    IReadOnlyList<CurrencyBreakdownDto> ByCurrency);

public sealed record EarningDto(
    Guid Id,
    DateTime CreatedAt,
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
    int? RuleSetVersion,
    DateTime? AvailableAt,
    DateTime? PaidAt,
    string? Reason);

public sealed record LedgerRowDto(
    Guid Id,
    DateTime CreatedAt,
    UserRefDto User,
    EarningType Type,
    EarningStatus Status,
    string Description,
    CampaignRefDto? Campaign,
    Guid? SubmissionId,
    decimal OriginalAmount,
    string OriginalCurrency,
    decimal ExchangeRate,
    Guid? ExchangeRateId,
    decimal SettlementAmount,
    string SettlementCurrency,
    int? RuleSetVersion,
    DateTime? AvailableAt,
    DateTime? ApprovedAt,
    Guid? ApprovedByUserId,
    Guid? CreatedByUserId,
    Guid? PayoutItemId,
    DateTime? PaidAt,
    DateTime? ReversedAt,
    Guid? ReversesEntryId,
    Guid? ReversedByEntryId,
    string? Reason,
    Guid ConcurrencyStamp,
    RateSourceLevel? RateSource = null,
    string? RateSourceLabel = null,
    Guid? RateCardId = null,
    int? RateCardVersion = null,
    Guid? RateGroupId = null,
    Guid? RateAssignmentId = null);

public class MyEarningsQuery : PageQuery
{
    public EarningType? Type { get; set; }
    public EarningStatus? Status { get; set; }
    public Guid? CampaignId { get; set; }

    /// <summary>Inclusive lower bound on CreatedAt (UTC).</summary>
    public DateTime? From { get; set; }

    /// <summary>Exclusive upper bound on CreatedAt (UTC).</summary>
    public DateTime? To { get; set; }
}

public sealed class LedgerQuery : MyEarningsQuery
{
    public Guid? UserId { get; set; }
}

public sealed class CreateAdjustmentRequest
{
    /// <summary>Client-generated id of this request; retries/double-clicks with the same id return the same adjustment.</summary>
    [Required]
    public Guid? RequestId { get; set; }

    [Required]
    public Guid? UserId { get; set; }

    /// <summary>Signed amount: positive credits, negative debits. Must not be zero.</summary>
    [Required, Range(-1_000_000, 1_000_000)]
    public decimal? Amount { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = string.Empty;

    [Required, MinLength(10), MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;

    public Guid? SubmissionId { get; set; }
    public Guid? SupportTicketId { get; set; }
    public bool Confirm { get; set; }
}

public sealed record AdjustmentResultDto(bool Created, LedgerRowDto Earning);

public sealed class ReverseEarningRequest
{
    [Required, MinLength(10), MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;

    public bool Confirm { get; set; }
}

public sealed record ReversalResultDto(LedgerRowDto Original, LedgerRowDto Reversal);

public sealed class PendingEarningsQuery : PageQuery
{
    public EarningType? Type { get; set; }
}

public sealed record PendingEarningDto(
    Guid Id,
    DateTime CreatedAt,
    EarningType Type,
    string Description,
    UserRefDto User,
    CampaignRefDto? Campaign,
    Guid? SubmissionId,
    Guid? ReferralId,
    decimal OriginalAmount,
    string OriginalCurrency,
    decimal SettlementAmount,
    string SettlementCurrency,
    Guid? CreatedByUserId,
    bool AwaitingLiveCheck,
    DateTime? LiveCheckDueAt,
    int? SubmissionRiskScore,
    Guid ConcurrencyStamp);

public sealed class ApprovePendingEarningRequest
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class DeclinePendingEarningRequest
{
    [Required, MinLength(5), MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class ExchangeRateQuery : PageQuery
{
    [StringLength(3, MinimumLength = 3)]
    public string? Base { get; set; }

    [StringLength(3, MinimumLength = 3)]
    public string? Quote { get; set; }
}

public sealed record ExchangeRateDto(
    Guid Id, string BaseCurrency, string QuoteCurrency, decimal Rate, DateTime EffectiveAt, string Source,
    DateTime CreatedAt, Guid? CreatedByUserId);

public sealed class CreateExchangeRateRequest
{
    [Required, StringLength(3, MinimumLength = 3)]
    public string BaseCurrency { get; set; } = string.Empty;

    [Required, StringLength(3, MinimumLength = 3)]
    public string QuoteCurrency { get; set; } = string.Empty;

    [Required]
    public decimal? Rate { get; set; }

    [Required]
    public DateTime? EffectiveAt { get; set; }

    [Required, MinLength(2), MaxLength(50)]
    public string Source { get; set; } = "manual";

    [Required, MinLength(10), MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;

    public bool Confirm { get; set; }
}

/// <summary>Validation helpers shared by the finance endpoints of the Ledger and Payouts modules.</summary>
public static class FinanceGuards
{
    public static void RequireConfirm(bool confirm)
    {
        if (!confirm)
            throw new DomainException("request.confirm_required",
                "This action is sensitive. Resend it with \"confirm\": true to proceed.");
    }

    public static string RequireCurrency(string? currency, string field = "currency")
    {
        if (currency is null || !Money.IsSupported(currency.Trim()))
            throw new DomainException("ledger.currency_unsupported", $"Currency '{currency}' is not supported.",
                errors: new Dictionary<string, string[]> { [field] = new[] { "Unsupported currency." } });
        return Money.Normalize(currency);
    }

    /// <summary>Masks a payment reference to its last four characters (e.g. "••••1234").</summary>
    public static string? MaskReference(string? reference) =>
        string.IsNullOrEmpty(reference) ? reference
        : reference.Length <= 4 ? new string('•', reference.Length)
        : "••••" + reference[^4..];
}
