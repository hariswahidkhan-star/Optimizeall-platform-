using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Rewards;

/// <summary>
/// An immutable, versioned set of reward rules for a campaign. Any change to rates, bonuses or caps creates a
/// new version; submissions record the version in force when they were made, and approved earnings are
/// computed from that recorded version, so edits never silently change existing earnings.
/// </summary>
public class RewardRuleSet : Entity
{
    public Guid CampaignId { get; set; }
    public int Version { get; set; }

    /// <summary>Currency every amount in this rule set is expressed in (the "original reward currency").</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Per-participant earning caps for this campaign, in <see cref="Currency"/>. Null = no cap.</summary>
    public decimal? DailyCapPerParticipant { get; set; }
    public decimal? WeeklyCapPerParticipant { get; set; }
    public decimal? CampaignCapPerParticipant { get; set; }

    /// <summary>Whether person-level rates (rate cards, groups, custom rates) may replace this campaign's post rate.</summary>
    public PersonalRatesMode PersonalRatesMode { get; set; } = PersonalRatesMode.Allowed;

    /// <summary>
    /// Optional ceiling on a person-level rate, as a multiple of the campaign rate the post would otherwise get
    /// (e.g. 3 = at most three times the campaign rate). Null = no ceiling. Caps and the budget always still apply.
    /// </summary>
    public decimal? PersonalRateMaxMultiplier { get; set; }

    public DateTime EffectiveFrom { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string ChangeReason { get; set; } = string.Empty;

    public List<RewardRule> Rules { get; set; } = new();
}

public enum RewardRuleType
{
    /// <summary>Flat amount per approved post. Every rule set has exactly one unconditioned base rate.</summary>
    BaseRate,
    /// <summary>Replaces the base rate when its Platform/Country/Tier conditions match (most specific wins).</summary>
    RateOverride,
    /// <summary>Additional amount for posts made between ValidFrom and ValidTo.</summary>
    TimeLimitedBonus,
    /// <summary>Additional amount on a participant's first approved post in the campaign.</summary>
    FirstPostBonus,
    /// <summary>Discretionary bonus a reviewer may propose for exceptional posts, up to Amount.</summary>
    QualityBonus,
}

public enum BonusApprovalMode
{
    /// <summary>Payable as soon as the underlying post is approved.</summary>
    Automatic,
    /// <summary>Recorded as pending and needs a separate approval by a user with the rewards.approve_bonus permission.</summary>
    ManualApproval,
}

public class RewardRule : Entity
{
    public Guid RuleSetId { get; set; }
    public RewardRuleType Type { get; set; }
    public decimal Amount { get; set; }

    public SocialPlatform? Platform { get; set; }
    public string? CountryCode { get; set; }
    public ParticipantTier? Tier { get; set; }

    /// <summary>Window for time-limited bonuses (UTC, inclusive start, exclusive end).</summary>
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    public BonusApprovalMode ApprovalMode { get; set; } = BonusApprovalMode.Automatic;

    /// <summary>Tie-breaker among equally specific overrides (higher wins).</summary>
    public int Priority { get; set; }
    public string? Label { get; set; }
}
