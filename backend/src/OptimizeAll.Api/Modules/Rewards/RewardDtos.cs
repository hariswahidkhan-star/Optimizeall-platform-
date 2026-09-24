using System.ComponentModel.DataAnnotations;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Rewards;

namespace OptimizeAll.Api.Modules.Rewards;

public sealed class RewardRuleInput
{
    public RewardRuleType Type { get; set; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal Amount { get; set; }

    public SocialPlatform? Platform { get; set; }

    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter country code.")]
    public string? CountryCode { get; set; }

    public ParticipantTier? Tier { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public BonusApprovalMode ApprovalMode { get; set; } = BonusApprovalMode.Automatic;

    [Range(-1000, 1000)]
    public int Priority { get; set; }

    [MaxLength(150)]
    public string? Label { get; set; }
}

/// <summary>A complete rule set (rates, bonuses and caps). Saving it always creates a new immutable version.</summary>
public class RewardRuleSetInput
{
    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";

    [Range(typeof(decimal), "0.0001", "100000000")]
    public decimal? DailyCapPerParticipant { get; set; }

    [Range(typeof(decimal), "0.0001", "100000000")]
    public decimal? WeeklyCapPerParticipant { get; set; }

    [Range(typeof(decimal), "0.0001", "100000000")]
    public decimal? CampaignCapPerParticipant { get; set; }

    /// <summary>Whether person-level rates (rate cards, groups, custom deals) may replace the campaign rate.</summary>
    public PersonalRatesMode PersonalRatesMode { get; set; } = PersonalRatesMode.Allowed;

    /// <summary>Optional ceiling on person-level rates as a multiple of the campaign rate (e.g. 3).</summary>
    [Range(typeof(decimal), "0.01", "100")]
    public decimal? PersonalRateMaxMultiplier { get; set; }

    [Required, MinLength(1), MaxLength(50)]
    public List<RewardRuleInput> Rules { get; set; } = new();
}

public sealed class CreateRewardRuleSetRequest : RewardRuleSetInput
{
    [Required, MinLength(5), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Must be true: changing rates is a sensitive action.</summary>
    public bool Confirm { get; set; }

    /// <summary>
    /// The version the editor was showing (optional). When set and a newer version has been saved since, the request is
    /// refused with 409 reward.version_conflict instead of silently replacing the other person's change.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int? BaseVersion { get; set; }
}

public sealed class RewardPreviewRequest
{
    public SocialPlatform Platform { get; set; }

    [RegularExpression("^[A-Za-z]{2}$")]
    public string CountryCode { get; set; } = "US";

    public ParticipantTier Tier { get; set; }
    public DateTime? PostedAt { get; set; }
    public bool IsFirstApprovedPost { get; set; }

    [Range(typeof(decimal), "0", "100000000")]
    public decimal EarnedToday { get; set; }

    [Range(typeof(decimal), "0", "100000000")]
    public decimal EarnedThisWeek { get; set; }

    [Range(typeof(decimal), "0", "100000000")]
    public decimal EarnedInCampaign { get; set; }

    [Range(typeof(decimal), "0", "100000000")]
    public decimal? QualityBonusRequested { get; set; }

    /// <summary>Saved version to price with; default = the current version.</summary>
    public int? RuleSetVersion { get; set; }

    /// <summary>Unsaved rules to try out instead of a saved version.</summary>
    public RewardRuleSetInput? Draft { get; set; }

    /// <summary>Budget remaining to assume; default = the campaign's actual remaining budget.</summary>
    [Range(typeof(decimal), "0", "1000000000")]
    public decimal? CampaignBudgetRemaining { get; set; }
}

public sealed record RewardRuleDto(
    Guid Id, RewardRuleType Type, decimal Amount, SocialPlatform? Platform, string? CountryCode, ParticipantTier? Tier,
    DateTime? ValidFrom, DateTime? ValidTo, BonusApprovalMode ApprovalMode, int Priority, string? Label)
{
    public static RewardRuleDto From(RewardRule r) =>
        new(r.Id, r.Type, r.Amount, r.Platform, r.CountryCode, r.Tier, r.ValidFrom, r.ValidTo, r.ApprovalMode, r.Priority, r.Label);
}

public sealed record UserRefDto(Guid Id, string DisplayName);

public sealed record RewardRuleSetDto(
    Guid Id, int Version, string Currency,
    decimal? DailyCapPerParticipant, decimal? WeeklyCapPerParticipant, decimal? CampaignCapPerParticipant,
    DateTime EffectiveFrom, DateTime CreatedAt, UserRefDto? CreatedBy, string Reason, string Summary,
    bool IsCurrent, int InUseBySubmissions, IReadOnlyList<RewardRuleDto> Rules,
    PersonalRatesMode PersonalRatesMode = PersonalRatesMode.Allowed, decimal? PersonalRateMaxMultiplier = null);

public sealed record RewardLineDto(EarningType Type, Guid RuleId, decimal Amount, decimal UncappedAmount, bool RequiresApproval, string Label,
    bool FromPersonalRate = false);

/// <summary>
/// Where the post rate came from. <see cref="Label"/> names the card/group (staff with rates.view only; otherwise the
/// generic <see cref="LevelLabel"/>). Card amount/currency and the exchange rate are those locked on the submission.
/// </summary>
public sealed record RateSourceDto(
    RateSourceLevel Level, string LevelLabel, string Label, decimal CampaignRateAmount, decimal? PersonalAmount, bool Limited,
    string? IgnoredReason, decimal? CardAmount, string? CardCurrency, decimal? ExchangeRate, DateTime? ValidTo, Guid? RateCardId,
    int? RateCardVersion, Guid? RateGroupId, Guid? RateAssignmentId);

public sealed record RewardQuoteDto(
    Guid? RuleSetId, int? RuleSetVersion, string Currency, IReadOnlyList<RewardLineDto> Lines, decimal Total,
    IReadOnlyList<string> AppliedCaps, string RuleSetSummary, RateSourceDto? RateSource = null)
{
    public static RewardQuoteDto From(RewardQuote q, Guid? ruleSetId, int? ruleSetVersion, SubmissionRate? rate = null, bool showNames = true) => new(
        ruleSetId, ruleSetVersion, q.Currency,
        q.Lines.Select(l => new RewardLineDto(l.Type, l.RuleId, l.Amount, l.UncappedAmount, l.RequiresApproval, l.Label, l.FromPersonalRate)).ToList(),
        q.Total, q.AppliedCaps, q.RuleSetSummary, Source(q, ruleSetVersion, rate, showNames));

    private static RateSourceDto? Source(RewardQuote q, int? version, SubmissionRate? rate, bool showNames)
    {
        if (q.PostRate is not { } p) return null;
        if (rate is null || !p.PersonalApplied)
            return new RateSourceDto(RateSourceLevel.CampaignRules, RateSources.Describe(RateSourceLevel.CampaignRules),
                version is null ? "Campaign rules" : $"Campaign rules v{version}", p.CampaignAmount, p.PersonalAmount, false,
                p.PersonalIgnoredReason, null, null, null, null, null, null, null, null);
        var levelLabel = RateSources.Describe(rate.Level);
        return new RateSourceDto(rate.Level, levelLabel, showNames ? rate.SourceLabel : levelLabel, p.CampaignAmount, p.PersonalAmount,
            p.PersonalLimited, null, rate.CardAmount, rate.CardCurrency, rate.ExchangeRate, rate.AssignmentValidTo,
            showNames ? rate.RateCardId : null, rate.RateCardVersion, showNames ? rate.RateGroupId : null,
            showNames ? rate.RateAssignmentId : null);
    }
}
