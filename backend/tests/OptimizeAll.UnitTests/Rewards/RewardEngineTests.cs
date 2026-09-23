using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Rewards;

namespace OptimizeAll.UnitTests.Rewards;

public sealed class RewardEngineTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static RewardRuleSet Set(string currency = "USD", decimal baseAmount = 5m, int version = 1, params RewardRule[] extra)
    {
        var set = new RewardRuleSet { Currency = currency, Version = version };
        set.Rules.Add(new RewardRule { Type = RewardRuleType.BaseRate, Amount = baseAmount });
        set.Rules.AddRange(extra);
        return set;
    }

    private static RewardRule Override(decimal amount, SocialPlatform? platform = null, string? country = null, ParticipantTier? tier = null,
        int priority = 0, DateTime? from = null, DateTime? to = null, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), Type = RewardRuleType.RateOverride, Amount = amount, Platform = platform, CountryCode = country,
        Tier = tier, Priority = priority, ValidFrom = from, ValidTo = to,
    };

    private static RewardRule Bonus(RewardRuleType type, decimal amount, DateTime? from = null, DateTime? to = null,
        BonusApprovalMode mode = BonusApprovalMode.Automatic, SocialPlatform? platform = null) => new()
    {
        Type = type, Amount = amount, ValidFrom = from, ValidTo = to, ApprovalMode = mode, Platform = platform,
    };

    private static RewardContext Ctx(SocialPlatform platform = SocialPlatform.Instagram, string country = "PK",
        ParticipantTier tier = ParticipantTier.Standard, DateTime? postedAt = null, bool first = false,
        decimal today = 0, decimal week = 0, decimal total = 0, decimal? budget = null, decimal? quality = null) => new()
    {
        Platform = platform, CountryCode = country, Tier = tier, PostedAtUtc = postedAt ?? T0, IsFirstApprovedPostInCampaign = first,
        EarnedTodayInCampaign = today, EarnedThisWeekInCampaign = week, EarnedInCampaignTotal = total,
        CampaignBudgetRemaining = budget, QualityBonusRequested = quality,
    };

    // ---------------------------------------------------------------- base rate & overrides

    [Fact]
    public void Base_rate_applies_when_no_override_matches()
    {
        var quote = RewardEngine.Quote(Set(extra: Override(9m, platform: SocialPlatform.TikTok)), Ctx());
        var line = Assert.Single(quote.Lines);
        Assert.Equal(EarningType.PostReward, line.Type);
        Assert.Equal(5m, line.Amount);
        Assert.Equal(5m, quote.Total);
        Assert.Equal("USD", quote.Currency);
        Assert.Empty(quote.AppliedCaps);
    }

    [Fact]
    public void Most_specific_override_wins()
    {
        var platformOnly = Override(6m, platform: SocialPlatform.Instagram);
        var platformCountry = Override(7m, platform: SocialPlatform.Instagram, country: "PK");
        var all = Override(8m, platform: SocialPlatform.Instagram, country: "PK", tier: ParticipantTier.Gold);
        var set = Set(extra: new[] { platformOnly, platformCountry, all });

        Assert.Equal(7m, RewardEngine.Quote(set, Ctx()).Total);
        Assert.Equal(8m, RewardEngine.Quote(set, Ctx(tier: ParticipantTier.Gold)).Total);
        Assert.Equal(6m, RewardEngine.Quote(set, Ctx(country: "AE")).Total);
        Assert.Equal(5m, RewardEngine.Quote(set, Ctx(platform: SocialPlatform.X)).Total);
    }

    [Fact]
    public void Country_match_is_case_insensitive()
    {
        var set = Set(extra: Override(7m, country: "pk"));
        Assert.Equal(7m, RewardEngine.Quote(set, Ctx(country: "PK")).Total);
    }

    [Fact]
    public void Equally_specific_overrides_tie_break_on_priority_then_amount_then_id()
    {
        var country = Override(6m, country: "PK", priority: 1);
        var platform = Override(9m, platform: SocialPlatform.Instagram, priority: 0);
        Assert.Equal(6m, RewardEngine.Quote(Set(extra: new[] { country, platform }), Ctx()).Total);

        var a = Override(6m, country: "PK");
        var b = Override(9m, platform: SocialPlatform.Instagram);
        Assert.Equal(9m, RewardEngine.Quote(Set(extra: new[] { a, b }), Ctx()).Total);

        var low = Override(7m, country: "PK", id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var high = Override(7m, platform: SocialPlatform.Instagram, id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var quote = RewardEngine.Quote(Set(extra: new[] { high, low }), Ctx());
        Assert.Equal(low.Id, quote.Lines[0].RuleId);
    }

    [Fact]
    public void Override_window_is_inclusive_start_and_exclusive_end()
    {
        var start = T0;
        var end = T0.AddDays(1);
        var set = Set(extra: Override(8m, platform: SocialPlatform.Instagram, from: start, to: end));

        Assert.Equal(5m, RewardEngine.Quote(set, Ctx(postedAt: start.AddTicks(-1))).Total);
        Assert.Equal(8m, RewardEngine.Quote(set, Ctx(postedAt: start)).Total);
        Assert.Equal(8m, RewardEngine.Quote(set, Ctx(postedAt: end.AddTicks(-1))).Total);
        Assert.Equal(5m, RewardEngine.Quote(set, Ctx(postedAt: end)).Total);
    }

    [Fact]
    public void Tier_override_applies_only_to_that_tier()
    {
        var set = Set(extra: Override(12m, tier: ParticipantTier.Platinum));
        Assert.Equal(12m, RewardEngine.Quote(set, Ctx(tier: ParticipantTier.Platinum)).Total);
        Assert.Equal(5m, RewardEngine.Quote(set, Ctx(tier: ParticipantTier.Silver)).Total);
    }

    // ---------------------------------------------------------------- bonuses

    [Fact]
    public void Time_limited_bonus_applies_inside_its_window_only()
    {
        var bonus = Bonus(RewardRuleType.TimeLimitedBonus, 2m, T0, T0.AddHours(48));
        var set = Set(extra: bonus);

        var inside = RewardEngine.Quote(set, Ctx(postedAt: T0));
        Assert.Equal(new[] { EarningType.PostReward, EarningType.TimeLimitedBonus }, inside.Lines.Select(l => l.Type));
        Assert.Equal(7m, inside.Total);
        Assert.Equal(5m, RewardEngine.Quote(set, Ctx(postedAt: T0.AddHours(48))).Total);
        Assert.Equal(5m, RewardEngine.Quote(set, Ctx(postedAt: T0.AddSeconds(-1))).Total);
    }

    [Fact]
    public void Several_time_limited_bonuses_can_stack_and_respect_conditions()
    {
        var weekend = Bonus(RewardRuleType.TimeLimitedBonus, 1m, T0, T0.AddDays(2));
        var launch = Bonus(RewardRuleType.TimeLimitedBonus, 3m, T0.AddHours(-1), T0.AddHours(1));
        var tiktokOnly = Bonus(RewardRuleType.TimeLimitedBonus, 4m, T0, T0.AddDays(2), platform: SocialPlatform.TikTok);
        var quote = RewardEngine.Quote(Set(extra: new[] { weekend, launch, tiktokOnly }), Ctx(postedAt: T0));
        Assert.Equal(9m, quote.Total);
        Assert.Equal(2, quote.Lines.Count(l => l.Type == EarningType.TimeLimitedBonus));
    }

    [Fact]
    public void First_post_bonus_only_for_first_approved_post()
    {
        var set = Set(extra: Bonus(RewardRuleType.FirstPostBonus, 3m));
        Assert.Equal(8m, RewardEngine.Quote(set, Ctx(first: true)).Total);
        Assert.Equal(5m, RewardEngine.Quote(set, Ctx(first: false)).Total);
        Assert.Contains(RewardEngine.Quote(set, Ctx(first: true)).Lines, l => l.Type == EarningType.FirstPostBonus);
    }

    [Theory]
    [InlineData(null, 5.0)]
    [InlineData(0.0, 5.0)]
    [InlineData(1.5, 6.5)]
    [InlineData(4.0, 9.0)]
    [InlineData(100.0, 9.0)]
    public void Quality_bonus_is_bounded_by_rule_amount(double? requested, double expectedTotal)
    {
        var set = Set(extra: Bonus(RewardRuleType.QualityBonus, 4m, mode: BonusApprovalMode.ManualApproval));
        var quote = RewardEngine.Quote(set, Ctx(quality: requested is null ? null : (decimal)requested));
        Assert.Equal((decimal)expectedTotal, quote.Total);
        if (requested is > 0)
        {
            var line = quote.Lines.Single(l => l.Type == EarningType.QualityBonus);
            Assert.True(line.RequiresApproval);
            Assert.False(quote.Lines.Single(l => l.Type == EarningType.PostReward).RequiresApproval);
        }
    }

    [Fact]
    public void Quality_bonus_without_rule_is_a_validation_error()
    {
        var ex = Assert.Throws<DomainException>(() => RewardEngine.Quote(Set(), Ctx(quality: 2m)));
        Assert.Equal("reward.quality_bonus_not_configured", ex.Code);
        Assert.Equal(DomainErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public void Manual_approval_bonus_lines_require_approval()
    {
        var set = Set(extra: Bonus(RewardRuleType.FirstPostBonus, 2m, mode: BonusApprovalMode.ManualApproval));
        var quote = RewardEngine.Quote(set, Ctx(first: true));
        Assert.True(quote.Lines.Single(l => l.Type == EarningType.FirstPostBonus).RequiresApproval);
    }

    // ---------------------------------------------------------------- caps

    [Fact]
    public void Daily_cap_reduces_the_line_to_remaining_headroom()
    {
        var set = Set();
        set.DailyCapPerParticipant = 12m;
        var quote = RewardEngine.Quote(set, Ctx(today: 10m));
        var line = Assert.Single(quote.Lines);
        Assert.Equal(2m, line.Amount);
        Assert.Equal(5m, line.UncappedAmount);
        Assert.Equal(new[] { RewardCaps.Daily }, quote.AppliedCaps);
    }

    [Fact]
    public void Weekly_cap_applies()
    {
        var set = Set();
        set.WeeklyCapPerParticipant = 20m;
        var quote = RewardEngine.Quote(set, Ctx(week: 17m));
        Assert.Equal(3m, quote.Total);
        Assert.Equal(new[] { RewardCaps.Weekly }, quote.AppliedCaps);
    }

    [Fact]
    public void Campaign_cap_applies()
    {
        var set = Set();
        set.CampaignCapPerParticipant = 50m;
        var quote = RewardEngine.Quote(set, Ctx(total: 49m));
        Assert.Equal(1m, quote.Total);
        Assert.Equal(new[] { RewardCaps.Campaign }, quote.AppliedCaps);
    }

    [Fact]
    public void Caps_that_are_not_reached_are_not_reported()
    {
        var set = Set();
        set.DailyCapPerParticipant = 100m;
        set.WeeklyCapPerParticipant = 100m;
        set.CampaignCapPerParticipant = 100m;
        var quote = RewardEngine.Quote(set, Ctx(today: 10m, week: 10m, total: 10m, budget: 1000m));
        Assert.Equal(5m, quote.Total);
        Assert.Empty(quote.AppliedCaps);
    }

    [Fact]
    public void Caps_apply_sequentially_across_lines_and_combine()
    {
        var set = Set(extra: new[]
        {
            Bonus(RewardRuleType.TimeLimitedBonus, 2m, T0.AddDays(-1), T0.AddDays(1)),
            Bonus(RewardRuleType.FirstPostBonus, 3m),
        });
        set.DailyCapPerParticipant = 10m;
        set.WeeklyCapPerParticipant = 30m;

        // Daily headroom 6: post 5, time bonus 1 (of 2), first-post 0 (dropped). Weekly headroom 25 never binds.
        var quote = RewardEngine.Quote(set, Ctx(first: true, today: 4m, week: 5m));
        Assert.Equal(6m, quote.Total);
        Assert.Equal(new[] { EarningType.PostReward, EarningType.TimeLimitedBonus }, quote.Lines.Select(l => l.Type));
        Assert.Equal(1m, quote.Lines[1].Amount);
        Assert.Equal(2m, quote.Lines[1].UncappedAmount);
        Assert.Equal(new[] { RewardCaps.Daily }, quote.AppliedCaps);

        // Weekly binds before daily when it has less headroom; both are reported when both bite.
        var both = RewardEngine.Quote(set, Ctx(first: true, today: 7m, week: 28m));
        Assert.Equal(2m, both.Total);
        Assert.Contains(RewardCaps.Daily, both.AppliedCaps);
        Assert.Contains(RewardCaps.Weekly, both.AppliedCaps);
    }

    [Fact]
    public void Budget_exhaustion_limits_and_then_zeroes_the_reward()
    {
        var set = Set(extra: Bonus(RewardRuleType.FirstPostBonus, 3m));
        var partial = RewardEngine.Quote(set, Ctx(first: true, budget: 6.5m));
        Assert.Equal(6.5m, partial.Total);
        Assert.Equal(1.5m, partial.Lines.Single(l => l.Type == EarningType.FirstPostBonus).Amount);
        Assert.Equal(new[] { RewardCaps.Budget }, partial.AppliedCaps);

        var exhausted = RewardEngine.Quote(set, Ctx(first: true, budget: 0m));
        Assert.Empty(exhausted.Lines);
        Assert.Equal(0m, exhausted.Total);
        Assert.Equal(new[] { RewardCaps.Budget }, exhausted.AppliedCaps);

        var overspent = RewardEngine.Quote(set, Ctx(budget: -4m));
        Assert.Empty(overspent.Lines);
        Assert.Equal(0m, overspent.Total);
    }

    [Fact]
    public void Already_exceeded_caps_never_produce_negative_amounts()
    {
        var set = Set();
        set.DailyCapPerParticipant = 10m;
        var quote = RewardEngine.Quote(set, Ctx(today: 15m));
        Assert.Empty(quote.Lines);
        Assert.Equal(0m, quote.Total);
        Assert.Equal(new[] { RewardCaps.Daily }, quote.AppliedCaps);
    }

    [Fact]
    public void Zero_amount_rules_produce_no_lines()
    {
        var quote = RewardEngine.Quote(Set(baseAmount: 0m, extra: Bonus(RewardRuleType.FirstPostBonus, 0m)), Ctx(first: true));
        Assert.Empty(quote.Lines);
        Assert.Equal(0m, quote.Total);
        Assert.Empty(quote.AppliedCaps);
    }

    // ---------------------------------------------------------------- rounding

    [Theory]
    [InlineData("USD", 1.005, 1.01)]
    [InlineData("USD", 1.004, 1.00)]
    [InlineData("USD", 2.345, 2.35)]
    [InlineData("JPY", 100.5, 101)]
    [InlineData("JPY", 100.4, 100)]
    [InlineData("KWD", 1.2345, 1.235)]
    [InlineData("KWD", 1.2344, 1.234)]
    public void Amounts_round_to_currency_minor_units_away_from_zero(string currency, double amount, double expected)
    {
        var quote = RewardEngine.Quote(Set(currency, (decimal)amount), Ctx());
        Assert.Equal((decimal)expected, quote.Total);
        Assert.Equal((decimal)expected, quote.Lines[0].UncappedAmount);
    }

    [Fact]
    public void Capped_amounts_never_exceed_the_cap_after_rounding()
    {
        var set = Set("USD", 5m);
        set.DailyCapPerParticipant = 10.005m;
        var quote = RewardEngine.Quote(set, Ctx(today: 5m));
        Assert.Equal(5m, quote.Total);

        var tight = Set("JPY", 500m);
        var jq = RewardEngine.Quote(tight, Ctx(budget: 99.9m));
        Assert.Equal(99m, jq.Total);
    }

    [Fact]
    public void Currency_is_normalized_to_upper_case()
    {
        var quote = RewardEngine.Quote(Set("usd"), Ctx());
        Assert.Equal("USD", quote.Currency);
    }

    // ---------------------------------------------------------------- validation

    [Fact]
    public void Valid_rule_set_has_no_errors()
    {
        var set = Set(extra: new[]
        {
            Override(7m, platform: SocialPlatform.TikTok),
            Bonus(RewardRuleType.TimeLimitedBonus, 1m, T0, T0.AddDays(1)),
            Bonus(RewardRuleType.FirstPostBonus, 2m),
            Bonus(RewardRuleType.QualityBonus, 5m, mode: BonusApprovalMode.ManualApproval),
        });
        Assert.Empty(RewardEngine.Validate(set));
    }

    [Fact]
    public void Missing_or_duplicate_base_rate_is_invalid()
    {
        var none = new RewardRuleSet { Currency = "USD" };
        none.Rules.Add(Bonus(RewardRuleType.FirstPostBonus, 1m));
        Assert.Contains(RewardEngine.Validate(none), e => e.Contains("base rate is required"));

        var two = Set();
        two.Rules.Add(new RewardRule { Type = RewardRuleType.BaseRate, Amount = 3m });
        Assert.Contains(RewardEngine.Validate(two), e => e.Contains("Only one base rate"));

        var ex = Assert.Throws<DomainException>(() => RewardEngine.Quote(none, Ctx()));
        Assert.Equal("reward.invalid_rules", ex.Code);
    }

    [Fact]
    public void Conditioned_base_rate_is_invalid()
    {
        var set = new RewardRuleSet { Currency = "USD" };
        set.Rules.Add(new RewardRule { Type = RewardRuleType.BaseRate, Amount = 5m, Platform = SocialPlatform.X });
        Assert.Contains(RewardEngine.Validate(set), e => e.Contains("base rate cannot have"));
    }

    [Theory]
    [InlineData("XXX")]
    [InlineData("US")]
    public void Unsupported_currency_is_invalid(string currency) =>
        Assert.Contains(RewardEngine.Validate(Set(currency)), e => e.Contains("not supported"));

    [Fact]
    public void Other_invalid_rules_are_reported()
    {
        var set = Set(extra: new[]
        {
            Override(5m),                                                         // no condition
            Bonus(RewardRuleType.TimeLimitedBonus, 1m, T0, null),                 // open window
            Bonus(RewardRuleType.TimeLimitedBonus, 1m, T0, T0),                   // end == start
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = -1m, CountryCode = "PAK" },
            Bonus(RewardRuleType.FirstPostBonus, 1m), Bonus(RewardRuleType.FirstPostBonus, 1m),
            Bonus(RewardRuleType.QualityBonus, 1m), Bonus(RewardRuleType.QualityBonus, 1m),
            Bonus(RewardRuleType.FirstPostBonus, 1m, platform: SocialPlatform.X),
        });
        set.DailyCapPerParticipant = 0m;
        var errors = RewardEngine.Validate(set);
        Assert.Contains(errors, e => e.Contains("needs at least one"));
        Assert.Contains(errors, e => e.Contains("needs both a start and an end"));
        Assert.Contains(errors, e => e.Contains("end of the window must be after"));
        Assert.Contains(errors, e => e.Contains("must not be negative"));
        Assert.Contains(errors, e => e.Contains("two-letter"));
        Assert.Contains(errors, e => e.Contains("Only one first-post bonus"));
        Assert.Contains(errors, e => e.Contains("Only one quality bonus"));
        Assert.Contains(errors, e => e.Contains("this bonus cannot have"));
        Assert.Contains(errors, e => e.Contains("Daily cap must be greater than zero"));
    }

    // ---------------------------------------------------------------- historical rates

    [Fact]
    public void Quotes_from_an_old_version_are_unaffected_by_a_newer_version()
    {
        var v1 = Set(baseAmount: 5m, version: 1, extra: Override(6m, platform: SocialPlatform.Instagram));
        var before = RewardEngine.Quote(v1, Ctx());

        // The rate change is a new immutable version; v1 is left as it was.
        var v2 = Set(baseAmount: 9m, version: 2, extra: Override(11m, platform: SocialPlatform.Instagram));
        v2.DailyCapPerParticipant = 100m;

        var after = RewardEngine.Quote(v1, Ctx());
        Assert.Equal(before.Total, after.Total);
        Assert.Equal(6m, after.Total);
        Assert.Equal(11m, RewardEngine.Quote(v2, Ctx()).Total);
        Assert.StartsWith("v1 USD", after.RuleSetSummary);
        Assert.StartsWith("v2 USD", RewardEngine.Quote(v2, Ctx()).RuleSetSummary);
    }

    [Fact]
    public void Summary_describes_the_rule_set()
    {
        var set = Set("JPY", 500m, 3, Override(600m, country: "JP"), Bonus(RewardRuleType.FirstPostBonus, 100m));
        set.DailyCapPerParticipant = 2000m;
        Assert.Equal("v3 JPY: base 500; 1 override; 1 bonus; daily cap 2000", RewardEngine.Summarize(set));
    }

    [Fact]
    public void Labels_default_by_type()
    {
        var set = Set(extra: new[] { Override(6m, platform: SocialPlatform.Instagram, country: "PK"), Bonus(RewardRuleType.FirstPostBonus, 1m) });
        var quote = RewardEngine.Quote(set, Ctx(first: true));
        Assert.Equal("Post reward (Instagram, PK rate)", quote.Lines[0].Label);
        Assert.Equal("First post bonus", quote.Lines[1].Label);
    }
}
