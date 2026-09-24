using System.Globalization;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Ledger;

namespace OptimizeAll.Domain.Rewards;

/// <summary>
/// Everything the reward engine needs to price one post. All amounts are in the rule set's currency.
/// "Today" / "this week" aggregates are computed by the caller in the campaign's time zone (weeks start Monday).
/// </summary>
public sealed record RewardContext
{
    public SocialPlatform Platform { get; init; }
    public string CountryCode { get; init; } = string.Empty;
    public ParticipantTier Tier { get; init; }
    public DateTime PostedAtUtc { get; init; }
    public bool IsFirstApprovedPostInCampaign { get; init; }
    public decimal EarnedTodayInCampaign { get; init; }
    public decimal EarnedThisWeekInCampaign { get; init; }
    public decimal EarnedInCampaignTotal { get; init; }

    /// <summary>Remaining campaign budget. Null = the campaign has no budget limit.</summary>
    public decimal? CampaignBudgetRemaining { get; init; }

    /// <summary>Discretionary quality bonus a reviewer proposes; null or 0 = none.</summary>
    public decimal? QualityBonusRequested { get; init; }

    /// <summary>
    /// The person-level rate resolved for this post (already converted to the rule set currency), or null when none
    /// applies. Resolved outside the engine (see PersonalRateResolver) so the engine stays pure.
    /// </summary>
    public PersonalRateInput? PersonalRate { get; init; }
}

/// <summary>
/// A person-level post rate in the rule set currency. <see cref="SourceId"/> identifies the rate card line; caps are
/// the card's per-participant caps (converted) and apply in addition to the campaign's.
/// </summary>
public sealed record PersonalRateInput(
    decimal Amount,
    Guid SourceId,
    string Label,
    bool StackCampaignBonuses = true,
    decimal? DailyCap = null,
    decimal? WeeklyCap = null,
    decimal? CampaignCap = null);

/// <summary>One priced component of a reward. <see cref="Amount"/> is after caps; <see cref="UncappedAmount"/> before.</summary>
public sealed record RewardLine(
    EarningType Type,
    Guid RuleId,
    decimal Amount,
    decimal UncappedAmount,
    bool RequiresApproval,
    string Label,
    bool FromPersonalRate = false);

/// <summary>
/// How the post rate was chosen: the campaign rule that would apply, and whether a person-level rate replaced it
/// (<see cref="PersonalIgnoredReason"/> says why a supplied personal rate was not used).
/// </summary>
public sealed record RewardPostRate(
    decimal CampaignAmount,
    Guid CampaignRuleId,
    bool PersonalApplied,
    decimal? PersonalAmount,
    bool PersonalLimited,
    string? PersonalIgnoredReason);

public sealed record RewardQuote(
    string Currency,
    IReadOnlyList<RewardLine> Lines,
    decimal Total,
    IReadOnlyList<string> AppliedCaps,
    string RuleSetSummary,
    RewardPostRate? PostRate = null);

/// <summary>Cap identifiers reported in <see cref="RewardQuote.AppliedCaps"/>.</summary>
public static class RewardCaps
{
    public const string Daily = "daily_cap";
    public const string Weekly = "weekly_cap";
    public const string Campaign = "campaign_cap";
    public const string Budget = "campaign_budget";

    /// <summary>The person-level rate was limited to the campaign's maximum multiplier.</summary>
    public const string PersonalRateLimit = "personal_rate_limit";
    public const string CardDaily = "rate_card_daily_cap";
    public const string CardWeekly = "rate_card_weekly_cap";
    public const string CardCampaign = "rate_card_campaign_cap";
}

/// <summary>
/// Pure reward calculation. Given an immutable <see cref="RewardRuleSet"/> version and a <see cref="RewardContext"/>,
/// produces the reward lines for one post. No I/O and no clock: the same inputs always give the same quote, which is
/// what makes historical rates reproducible (earnings are always priced from the version recorded on the submission).
/// See docs/REWARD_ENGINE.md.
/// </summary>
public static class RewardEngine
{
    /// <summary>Returns human-readable validation errors; empty when the rule set can be saved.</summary>
    public static IReadOnlyList<string> Validate(RewardRuleSet ruleSet)
    {
        var errors = new List<string>();
        if (!Money.IsSupported(ruleSet.Currency))
            errors.Add($"Currency '{ruleSet.Currency}' is not supported.");

        CheckCap(ruleSet.DailyCapPerParticipant, "Daily cap", errors);
        CheckCap(ruleSet.WeeklyCapPerParticipant, "Weekly cap", errors);
        CheckCap(ruleSet.CampaignCapPerParticipant, "Campaign cap", errors);
        if (ruleSet.PersonalRateMaxMultiplier is { } multiplier && (multiplier <= 0m || multiplier > 100m))
            errors.Add("The maximum personal rate multiplier must be greater than 0 and at most 100.");

        var baseRates = ruleSet.Rules.Where(r => r.Type == RewardRuleType.BaseRate).ToList();
        if (baseRates.Count == 0)
            errors.Add("A base rate is required.");
        else if (baseRates.Count > 1)
            errors.Add("Only one base rate is allowed.");

        foreach (var (rule, index) in ruleSet.Rules.Select((r, i) => (r, i + 1)))
        {
            var name = $"Rule {index} ({rule.Type})";
            if (rule.Amount < 0) errors.Add($"{name}: amount must not be negative.");
            if (rule.CountryCode is not null && (rule.CountryCode.Length != 2 || !rule.CountryCode.All(char.IsAsciiLetter)))
                errors.Add($"{name}: country code must be a two-letter ISO code.");
            if (rule.ValidFrom.HasValue && rule.ValidTo.HasValue && rule.ValidTo <= rule.ValidFrom)
                errors.Add($"{name}: the end of the window must be after its start.");
            if (rule.Label is { Length: > 150 })
                errors.Add($"{name}: label must be at most 150 characters.");

            var hasConditions = rule.Platform.HasValue || rule.CountryCode is not null || rule.Tier.HasValue;
            var hasWindow = rule.ValidFrom.HasValue || rule.ValidTo.HasValue;
            switch (rule.Type)
            {
                case RewardRuleType.BaseRate:
                    if (hasConditions || hasWindow)
                        errors.Add($"{name}: the base rate cannot have platform, country, tier or date conditions; use a rate override.");
                    if (rule.ApprovalMode != BonusApprovalMode.Automatic)
                        errors.Add($"{name}: the base rate is always paid automatically on approval.");
                    break;
                case RewardRuleType.RateOverride:
                    if (!hasConditions && !hasWindow)
                        errors.Add($"{name}: an override needs at least one platform, country, tier or date condition.");
                    if (rule.ApprovalMode != BonusApprovalMode.Automatic)
                        errors.Add($"{name}: rate overrides are always paid automatically on approval.");
                    break;
                case RewardRuleType.TimeLimitedBonus:
                    if (!rule.ValidFrom.HasValue || !rule.ValidTo.HasValue)
                        errors.Add($"{name}: a time-limited bonus needs both a start and an end.");
                    break;
                case RewardRuleType.FirstPostBonus:
                case RewardRuleType.QualityBonus:
                    if (hasConditions || hasWindow)
                        errors.Add($"{name}: this bonus cannot have platform, country, tier or date conditions.");
                    break;
            }
        }

        if (ruleSet.Rules.Count(r => r.Type == RewardRuleType.FirstPostBonus) > 1)
            errors.Add("Only one first-post bonus is allowed.");
        if (ruleSet.Rules.Count(r => r.Type == RewardRuleType.QualityBonus) > 1)
            errors.Add("Only one quality bonus is allowed.");

        return errors;
    }

    /// <summary>Validates and throws a 400 <see cref="DomainException"/> ("reward.invalid_rules") listing every error.</summary>
    public static void EnsureValid(RewardRuleSet ruleSet)
    {
        var errors = Validate(ruleSet);
        if (errors.Count > 0)
            throw new DomainException("reward.invalid_rules", "The reward rules are not valid: " + string.Join(" ", errors),
                errors: new Dictionary<string, string[]> { ["rules"] = errors.ToArray() });
    }

    /// <summary>Prices one post. Throws "reward.invalid_rules" or "reward.quality_bonus_not_configured" (400).</summary>
    public static RewardQuote Quote(RewardRuleSet ruleSet, RewardContext context)
    {
        EnsureValid(ruleSet);
        var currency = Money.Normalize(ruleSet.Currency);
        var candidates = new List<(RewardRule Rule, EarningType Type, decimal Amount, string Label)>();
        var applied = new List<string>();

        // 1. Post rate: most specific matching override, else the base rate — unless a person-level rate applies.
        var baseRate = ruleSet.Rules.Single(r => r.Type == RewardRuleType.BaseRate);
        var rateRule = SelectRateRule(ruleSet, context) ?? baseRate;
        var personal = context.PersonalRate;
        string? ignored = null;
        if (personal is not null && ruleSet.PersonalRatesMode == PersonalRatesMode.CampaignRatesOnly)
        {
            ignored = "This campaign uses campaign rates only.";
            personal = null;
        }

        RewardRule? personalRule = null;
        var limited = false;
        if (personal is not null)
        {
            var amount = Math.Max(personal.Amount, 0m);
            // The campaign's ceiling on person-level rates, relative to the campaign rate the post would otherwise get.
            if (ruleSet.PersonalRateMaxMultiplier is { } multiplier)
            {
                var ceiling = FloorToMinorUnit(rateRule.Amount * multiplier, currency);
                if (amount > ceiling)
                {
                    amount = ceiling;
                    limited = true;
                    applied.Add(RewardCaps.PersonalRateLimit);
                }
            }
            personalRule = new RewardRule { Id = personal.SourceId, Type = RewardRuleType.RateOverride, Amount = amount };
            candidates.Add((personalRule, EarningType.PostReward, amount, personal.Label));
        }
        else
        {
            candidates.Add((rateRule, EarningType.PostReward, rateRule.Amount,
                rateRule.Label ?? (ReferenceEquals(rateRule, baseRate) ? "Post reward" : DescribeOverride(rateRule))));
        }

        // A person-level rate may be configured not to stack the campaign's first-post and time-limited bonuses.
        var stackBonuses = personal is null || personal.StackCampaignBonuses;

        // 2. Time-limited bonuses whose window contains the post time.
        foreach (var bonus in ruleSet.Rules
                     .Where(r => stackBonuses && r.Type == RewardRuleType.TimeLimitedBonus && ConditionsMatch(r, context) && WindowContains(r, context.PostedAtUtc))
                     .OrderBy(r => r.ValidFrom).ThenBy(r => r.Id))
        {
            candidates.Add((bonus, EarningType.TimeLimitedBonus, bonus.Amount, bonus.Label ?? "Time-limited bonus"));
        }

        // 3. First approved post in the campaign.
        if (context.IsFirstApprovedPostInCampaign && stackBonuses)
        {
            var first = ruleSet.Rules.FirstOrDefault(r => r.Type == RewardRuleType.FirstPostBonus);
            if (first is not null)
                candidates.Add((first, EarningType.FirstPostBonus, first.Amount, first.Label ?? "First post bonus"));
        }

        // 4. Discretionary quality bonus, bounded by the configured maximum.
        if (context.QualityBonusRequested is > 0m)
        {
            var quality = ruleSet.Rules.FirstOrDefault(r => r.Type == RewardRuleType.QualityBonus)
                ?? throw new DomainException("reward.quality_bonus_not_configured",
                    "This campaign's reward rules have no quality bonus, so none can be awarded.");
            candidates.Add((quality, EarningType.QualityBonus, Math.Min(context.QualityBonusRequested.Value, quality.Amount),
                quality.Label ?? "Quality bonus"));
        }

        // 5. Caps, applied line by line in order against the remaining headroom of each limit.
        var headrooms = new List<(string Name, decimal Remaining)>();
        if (ruleSet.DailyCapPerParticipant is { } daily)
            headrooms.Add((RewardCaps.Daily, daily - context.EarnedTodayInCampaign));
        if (ruleSet.WeeklyCapPerParticipant is { } weekly)
            headrooms.Add((RewardCaps.Weekly, weekly - context.EarnedThisWeekInCampaign));
        if (ruleSet.CampaignCapPerParticipant is { } campaignCap)
            headrooms.Add((RewardCaps.Campaign, campaignCap - context.EarnedInCampaignTotal));
        // Caps of the person's rate card apply on top of the campaign's (same per-participant campaign aggregates).
        if (personal?.DailyCap is { } cardDaily)
            headrooms.Add((RewardCaps.CardDaily, cardDaily - context.EarnedTodayInCampaign));
        if (personal?.WeeklyCap is { } cardWeekly)
            headrooms.Add((RewardCaps.CardWeekly, cardWeekly - context.EarnedThisWeekInCampaign));
        if (personal?.CampaignCap is { } cardCampaign)
            headrooms.Add((RewardCaps.CardCampaign, cardCampaign - context.EarnedInCampaignTotal));
        if (context.CampaignBudgetRemaining is { } budget)
            headrooms.Add((RewardCaps.Budget, budget));

        var lines = new List<RewardLine>();
        foreach (var candidate in candidates)
        {
            var uncapped = Money.Round(Math.Max(candidate.Amount, 0m), currency);
            var amount = uncapped;
            for (var i = 0; i < headrooms.Count; i++)
            {
                var remaining = Math.Max(headrooms[i].Remaining, 0m);
                if (remaining < amount)
                {
                    amount = remaining;
                    if (!applied.Contains(headrooms[i].Name)) applied.Add(headrooms[i].Name);
                }
            }

            // A capped amount is rounded down so it never exceeds the cap it was limited by.
            if (amount != uncapped) amount = FloorToMinorUnit(amount, currency);
            for (var i = 0; i < headrooms.Count; i++)
                headrooms[i] = (headrooms[i].Name, headrooms[i].Remaining - amount);

            if (amount <= 0m) continue;
            lines.Add(new RewardLine(candidate.Type, candidate.Rule.Id, amount, uncapped,
                candidate.Rule.ApprovalMode == BonusApprovalMode.ManualApproval, candidate.Label,
                FromPersonalRate: ReferenceEquals(candidate.Rule, personalRule)));
        }

        var postRate = new RewardPostRate(Money.Round(rateRule.Amount, currency), rateRule.Id, personalRule is not null,
            context.PersonalRate is null ? null : Money.Round(context.PersonalRate.Amount, currency), limited, ignored);
        return new RewardQuote(currency, lines, lines.Sum(l => l.Amount), applied, Summarize(ruleSet), postRate);
    }

    /// <summary>Most specific matching rate override (ties: higher priority, higher amount, lower id), or null.</summary>
    public static RewardRule? SelectRateRule(RewardRuleSet ruleSet, RewardContext context) =>
        ruleSet.Rules
            .Where(r => r.Type == RewardRuleType.RateOverride && ConditionsMatch(r, context) && WindowContains(r, context.PostedAtUtc))
            .OrderByDescending(Specificity)
            .ThenByDescending(r => r.Priority)
            .ThenByDescending(r => r.Amount)
            .ThenBy(r => r.Id)
            .FirstOrDefault();

    public static int Specificity(RewardRule rule) =>
        (rule.Platform.HasValue ? 1 : 0) + (rule.CountryCode is not null ? 1 : 0) + (rule.Tier.HasValue ? 1 : 0);

    public static bool ConditionsMatch(RewardRule rule, RewardContext context) =>
        (!rule.Platform.HasValue || rule.Platform == context.Platform) &&
        (rule.CountryCode is null || string.Equals(rule.CountryCode, context.CountryCode, StringComparison.OrdinalIgnoreCase)) &&
        (!rule.Tier.HasValue || rule.Tier == context.Tier);

    /// <summary>Inclusive start, exclusive end; an open side is unbounded.</summary>
    public static bool WindowContains(RewardRule rule, DateTime atUtc) =>
        (!rule.ValidFrom.HasValue || atUtc >= rule.ValidFrom.Value) &&
        (!rule.ValidTo.HasValue || atUtc < rule.ValidTo.Value);

    /// <summary>Short description of a rule set version, e.g. "v2 USD: base 5.00; 2 overrides; 1 bonus; daily cap 20.00".</summary>
    public static string Summarize(RewardRuleSet ruleSet)
    {
        var currency = Money.Normalize(ruleSet.Currency);
        var parts = new List<string>();
        var baseRate = ruleSet.Rules.FirstOrDefault(r => r.Type == RewardRuleType.BaseRate);
        parts.Add(baseRate is null ? "no base rate" : $"base {Format(baseRate.Amount, currency)}");
        var overrides = ruleSet.Rules.Count(r => r.Type == RewardRuleType.RateOverride);
        if (overrides > 0) parts.Add($"{overrides} override{(overrides == 1 ? "" : "s")}");
        var bonuses = ruleSet.Rules.Count(r => r.Type is RewardRuleType.TimeLimitedBonus or RewardRuleType.FirstPostBonus or RewardRuleType.QualityBonus);
        if (bonuses > 0) parts.Add($"{bonuses} bonus{(bonuses == 1 ? "" : "es")}");
        if (ruleSet.DailyCapPerParticipant is { } d) parts.Add($"daily cap {Format(d, currency)}");
        if (ruleSet.WeeklyCapPerParticipant is { } w) parts.Add($"weekly cap {Format(w, currency)}");
        if (ruleSet.CampaignCapPerParticipant is { } c) parts.Add($"campaign cap {Format(c, currency)}");
        if (ruleSet.PersonalRatesMode == PersonalRatesMode.CampaignRatesOnly) parts.Add("campaign rates only");
        else if (ruleSet.PersonalRateMaxMultiplier is { } m) parts.Add($"personal rates ≤ {m.ToString("0.##", CultureInfo.InvariantCulture)}× campaign rate");
        return $"v{ruleSet.Version} {currency}: {string.Join("; ", parts)}";
    }

    private static string DescribeOverride(RewardRule rule)
    {
        var parts = new List<string>();
        if (rule.Platform.HasValue) parts.Add(rule.Platform.Value.ToString());
        if (rule.CountryCode is not null) parts.Add(rule.CountryCode.ToUpperInvariant());
        if (rule.Tier.HasValue) parts.Add(rule.Tier.Value + " tier");
        return parts.Count == 0 ? "Post reward (special rate)" : $"Post reward ({string.Join(", ", parts)} rate)";
    }

    private static void CheckCap(decimal? cap, string name, List<string> errors)
    {
        if (cap is <= 0m) errors.Add($"{name} must be greater than zero when set.");
    }

    private static decimal FloorToMinorUnit(decimal amount, string currency)
    {
        var factor = (decimal)Math.Pow(10, Money.MinorUnitDigits(currency));
        return Math.Floor(amount * factor) / factor;
    }

    private static string Format(decimal amount, string currency) =>
        Money.Round(amount, currency).ToString("F" + Money.MinorUnitDigits(currency), CultureInfo.InvariantCulture);
}
