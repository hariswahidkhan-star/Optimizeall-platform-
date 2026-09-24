using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Ledger;

public enum EarningType
{
    PostReward,
    FirstPostBonus,
    TimeLimitedBonus,
    QualityBonus,
    ReferralReward,
    /// <summary>Manual credit/debit by finance (e.g. dispute outcome). Always carries a reason.</summary>
    Adjustment,
    /// <summary>Negative entry cancelling a previously paid earning (clawback netted against future payouts).</summary>
    Reversal,
}

public enum EarningStatus
{
    /// <summary>Recorded but not yet payable: bonus awaiting approval, or post awaiting its live-duration check.</summary>
    PendingApproval,
    /// <summary>Payable; will be picked up by the next payout batch whose cutoff is after <see cref="EarningEntry.AvailableAt"/>.</summary>
    Approved,
    /// <summary>Included in a payout batch that has not been paid yet.</summary>
    Scheduled,
    Paid,
    /// <summary>Cancelled before payment (or the negative leg cancelling an unpaid entry). Never payable.</summary>
    Reversed,
    /// <summary>Bonus that was declined at approval. Never payable.</summary>
    Declined,
}

/// <summary>
/// Immutable financial record. Amount/currency/rate fields can never change after insert (enforced in
/// AppDbContext); only lifecycle columns (status, payout linkage, timestamps) move forward. Corrections are
/// made with new Adjustment or Reversal entries, never by editing an existing row.
/// </summary>
public class EarningEntry : Entity, IConcurrencyStamped
{
    public Guid UserId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? SubmissionId { get; set; }
    public Guid? ReferralId { get; set; }

    public EarningType Type { get; set; }
    public EarningStatus Status { get; set; }

    /// <summary>Signed amount in the original reward currency.</summary>
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Rate applied to convert <see cref="Amount"/> into <see cref="SettlementCurrency"/> (1 when equal).</summary>
    public decimal ExchangeRate { get; set; } = 1m;
    public Guid? ExchangeRateId { get; set; }
    public decimal SettlementAmount { get; set; }
    public string SettlementCurrency { get; set; } = "USD";

    /// <summary>Reward rule version (and rule) the amount was calculated from.</summary>
    public Guid? RewardRuleSetId { get; set; }
    public int? RewardRuleSetVersion { get; set; }
    public Guid? RewardRuleId { get; set; }

    /// <summary>
    /// For post rewards: where the rate came from (campaign rules or a person-level rate card / group / custom rate) and
    /// which card version, group and assignment. Null on bonuses, manual entries and entries written before rate cards.
    /// </summary>
    public OptimizeAll.Domain.Rewards.RateSourceLevel? RateSource { get; set; }
    public string? RateSourceLabel { get; set; }
    public Guid? RateCardId { get; set; }
    public int? RateCardVersion { get; set; }
    public Guid? RateGroupId { get; set; }
    public Guid? RateAssignmentId { get; set; }

    /// <summary>Unique key that makes creation idempotent, e.g. "submission:{id}:PostReward".</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Mandatory for adjustments, reversals and declined bonuses.</summary>
    public string? Reason { get; set; }

    /// <summary>For Reversal entries: the entry being reversed.</summary>
    public Guid? ReversesEntryId { get; set; }
    public Guid? ReversedByEntryId { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    /// <summary>Earliest moment the entry may be included in a payout (approval time + hold period).</summary>
    public DateTime? AvailableAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    public Guid? PayoutItemId { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? ReversedAt { get; set; }

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    /// <summary>Properties that may never change after insert.</summary>
    public static readonly string[] ImmutableProperties =
    {
        nameof(UserId), nameof(CampaignId), nameof(SubmissionId), nameof(ReferralId), nameof(Type),
        nameof(Amount), nameof(Currency), nameof(ExchangeRate), nameof(ExchangeRateId), nameof(SettlementAmount),
        nameof(SettlementCurrency), nameof(RewardRuleSetId), nameof(RewardRuleSetVersion), nameof(RewardRuleId),
        nameof(RateSource), nameof(RateSourceLabel), nameof(RateCardId), nameof(RateCardVersion), nameof(RateGroupId), nameof(RateAssignmentId),
        nameof(IdempotencyKey), nameof(ReversesEntryId), nameof(CreatedAt), nameof(CreatedByUserId),
    };
}

/// <summary>Exchange rate from <see cref="BaseCurrency"/> to <see cref="QuoteCurrency"/>: 1 base = Rate quote.</summary>
public class ExchangeRate : Entity
{
    public string BaseCurrency { get; set; } = string.Empty;
    public string QuoteCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateTime EffectiveAt { get; set; }
    public string Source { get; set; } = "manual";
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
}
