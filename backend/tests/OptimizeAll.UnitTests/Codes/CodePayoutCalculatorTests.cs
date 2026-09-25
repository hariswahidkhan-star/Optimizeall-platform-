using OptimizeAll.Domain.Codes;

namespace OptimizeAll.UnitTests.Codes;

/// <summary>Commission calculation for discount-code sales (docs/DISCOUNT_CODES.md "Payout rules").</summary>
public sealed class CodePayoutCalculatorTests
{
    private static readonly CodeRate Flat5 = new(CodePayoutType.FlatPerSale, 5m, null);
    private static readonly CodeRate Pct10 = new(CodePayoutType.PercentOfNet, null, 10m);

    private static CodePayoutInput Input(decimal net = 100m, CodeRate? rate = null, string currency = "USD", IReadOnlyList<CodeTierRule>? tiers = null,
        CodeRate? over = null, CodeRateSource? overSource = null, int prior = 0, IReadOnlySet<int>? earned = null, decimal? daily = null,
        decimal earnedToday = 0m, decimal? programCap = null, decimal earnedInProgram = 0m, decimal? budget = null, decimal spent = 0m) =>
        new(currency, net, rate ?? Pct10, tiers ?? Array.Empty<CodeTierRule>(), over, overSource, null, prior, earned ?? new HashSet<int>(),
            daily, earnedToday, programCap, earnedInProgram, budget, spent);

    [Fact]
    public void Flat_amount_per_sale_ignores_the_order_value()
    {
        var r = CodePayoutCalculator.Calculate(Input(net: 999.99m, rate: Flat5));
        Assert.Equal(5m, r.Total);
        Assert.Equal(CodeRateSource.Program, r.RateSource);
        Assert.Single(r.Lines);
        Assert.Equal(CodePayoutLineKind.Commission, r.Lines[0].Kind);
        Assert.Equal(1, r.SaleNumber);
    }

    [Theory]
    [InlineData("USD", 123.45, 10, 12.35)] // 12.345 → away from zero
    [InlineData("JPY", 1234, 7.5, 93)] // 92.55 → 0 decimals
    [InlineData("KWD", 10.005, 12.5, 1.251)] // 1.250625 → 3 decimals
    public void Percentage_of_net_is_rounded_to_the_currency_minor_unit(string currency, double net, double percent, double expected)
    {
        var r = CodePayoutCalculator.Calculate(Input(net: (decimal)net, rate: new CodeRate(CodePayoutType.PercentOfNet, null, (decimal)percent), currency: currency));
        Assert.Equal((decimal)expected, r.Total);
    }

    [Fact]
    public void Tier_rate_applies_after_the_threshold_and_the_highest_reached_tier_wins()
    {
        var tiers = new[] { new CodeTierRule(3, null, 12m, null), new CodeTierRule(10, 20m, null, null) };
        Assert.Equal(10m, CodePayoutCalculator.Calculate(Input(tiers: tiers, prior: 2)).Total); // 3rd sale: base 10 %
        var fourth = CodePayoutCalculator.Calculate(Input(tiers: tiers, prior: 3)); // 4th sale: 12 %
        Assert.Equal(12m, fourth.Total);
        Assert.Equal(CodeRateSource.Tier, fourth.RateSource);
        Assert.Equal(20m, CodePayoutCalculator.Calculate(Input(tiers: tiers, prior: 10)).Total); // 11th: flat 20
    }

    [Fact]
    public void Tier_bonus_is_paid_once_when_the_threshold_is_reached()
    {
        var tiers = new[] { new CodeTierRule(3, null, null, 50m) };
        Assert.Single(CodePayoutCalculator.Calculate(Input(tiers: tiers, prior: 1)).Lines);
        var third = CodePayoutCalculator.Calculate(Input(tiers: tiers, prior: 2));
        Assert.Equal(60m, third.Total);
        Assert.Contains(third.Lines, l => l.Kind == CodePayoutLineKind.TierBonus && l.TierThreshold == 3 && l.Amount == 50m);
        // Already earned (e.g. on an earlier sale) → not again.
        Assert.Equal(10m, CodePayoutCalculator.Calculate(Input(tiers: tiers, prior: 5, earned: new HashSet<int> { 3 })).Total);
        // A tier added later is still paid at the next sale.
        Assert.Equal(60m, CodePayoutCalculator.Calculate(Input(tiers: tiers, prior: 5)).Total);
    }

    [Fact]
    public void Person_or_group_override_replaces_program_and_tier_rates_but_bonuses_still_apply()
    {
        var tiers = new[] { new CodeTierRule(1, 50m, null, 7m) };
        var r = CodePayoutCalculator.Calculate(Input(tiers: tiers, prior: 4, over: new CodeRate(CodePayoutType.PercentOfNet, null, 15m),
            overSource: CodeRateSource.GroupOverride));
        Assert.Equal(CodeRateSource.GroupOverride, r.RateSource);
        Assert.Equal(15m + 7m, r.Total);
        Assert.Contains("15% of net", r.RateLabel);
    }

    [Fact]
    public void Daily_program_and_budget_caps_reduce_lines_in_order_and_round_down()
    {
        var daily = CodePayoutCalculator.Calculate(Input(daily: 25m, earnedToday: 20m));
        Assert.Equal(5m, daily.Total);
        Assert.Equal(new[] { "daily_cap" }, daily.AppliedCaps);
        Assert.Equal(10m, daily.UncappedTotal);

        var program = CodePayoutCalculator.Calculate(Input(programCap: 100m, earnedInProgram: 96.6666m));
        Assert.Equal(3.33m, program.Total); // 3.3334 rounded DOWN
        Assert.Equal(new[] { "program_cap" }, program.AppliedCaps);

        var budget = CodePayoutCalculator.Calculate(Input(tiers: new[] { new CodeTierRule(1, null, null, 50m) }, budget: 1000m, spent: 985m));
        // Commission 10 fits, the bonus gets the last 5.
        Assert.Equal(15m, budget.Total);
        Assert.Equal(5m, budget.Lines.Single(l => l.Kind == CodePayoutLineKind.TierBonus).Amount);
        Assert.Equal(new[] { "program_budget" }, budget.AppliedCaps);
    }

    [Fact]
    public void An_exhausted_budget_approves_with_no_lines()
    {
        var r = CodePayoutCalculator.Calculate(Input(budget: 500m, spent: 500m));
        Assert.Empty(r.Lines);
        Assert.Equal(0m, r.Total);
        Assert.Contains("program_budget", r.AppliedCaps);
    }

    [Fact]
    public void Jpy_caps_round_down_to_whole_yen()
    {
        var r = CodePayoutCalculator.Calculate(Input(net: 10_000m, currency: "JPY", daily: 700.9m, earnedToday: 0m));
        Assert.Equal(700m, r.Total);
    }

    [Fact]
    public void Estimate_ignores_caps()
    {
        Assert.Equal(10m, CodePayoutCalculator.Estimate(Input(budget: 1m, spent: 1m)));
    }

    [Fact]
    public void Validation_rejects_bad_rates_and_tiers()
    {
        Assert.Empty(CodePayoutCalculator.ValidateRate(CodePayoutType.FlatPerSale, 5m, null));
        Assert.NotEmpty(CodePayoutCalculator.ValidateRate(CodePayoutType.FlatPerSale, null, null));
        Assert.NotEmpty(CodePayoutCalculator.ValidateRate(CodePayoutType.FlatPerSale, 5m, 10m));
        Assert.NotEmpty(CodePayoutCalculator.ValidateRate(CodePayoutType.PercentOfNet, null, 101m));
        Assert.NotEmpty(CodePayoutCalculator.ValidateRate(CodePayoutType.PercentOfNet, null, 0m));
        Assert.Empty(CodePayoutCalculator.ValidateTiers(new[] { new CodeTierRule(5, null, 12m, 20m) }));
        Assert.NotEmpty(CodePayoutCalculator.ValidateTiers(new[] { new CodeTierRule(5, null, null, null) }));
        Assert.NotEmpty(CodePayoutCalculator.ValidateTiers(new[] { new CodeTierRule(5, 1m, 12m, null) }));
        Assert.NotEmpty(CodePayoutCalculator.ValidateTiers(new[] { new CodeTierRule(5, null, 12m, null), new CodeTierRule(5, null, 15m, null) }));
        Assert.NotEmpty(CodePayoutCalculator.ValidateTiers(new[] { new CodeTierRule(0, null, 12m, null) }));
    }

    [Fact]
    public void Patterns_generate_valid_unambiguous_codes()
    {
        Assert.True(CodePattern.IsValidPattern("GLOW-????-##"));
        Assert.False(CodePattern.IsValidPattern("GLOW-##")); // fewer than four placeholders
        Assert.False(CodePattern.IsValidPattern("GL OW-####"));
        for (var i = 0; i < 200; i++)
        {
            var code = CodePattern.Generate("glow-????-##");
            Assert.Matches("^GLOW-[A-Z]{4}-[2-9]{2}$", code);
            Assert.DoesNotContain('O', code[5..]);
            Assert.DoesNotContain('I', code[5..]);
            Assert.True(CodePattern.IsValidCode(code));
        }
        Assert.Equal(23d * 23 * 8 * 8, CodePattern.Capacity("??##"));
        Assert.False(CodePattern.IsValidCode("-BAD"));
        Assert.False(CodePattern.IsValidCode("has space"));
    }

    [Fact]
    public void Assignment_windows_attribute_orders_by_order_date()
    {
        var start = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var a = new DiscountCodeAssignment { ValidFrom = start, ValidTo = start.AddDays(30) };
        Assert.False(a.Covers(start.AddSeconds(-1)));
        Assert.True(a.Covers(start));
        Assert.False(a.Covers(start.AddDays(30)));
        a.EndedAt = start.AddDays(10);
        Assert.Equal(start.AddDays(10), a.EffectiveTo);
        Assert.False(a.Covers(start.AddDays(11)));
        Assert.False(a.IsLive(start.AddDays(5)));

        var code = new DiscountCode { ValidTo = start.AddDays(3) };
        Assert.Equal(DiscountCodeStatus.Expired, code.EffectiveStatus(start.AddDays(4), null));
        Assert.Equal(DiscountCodeStatus.Available, code.EffectiveStatus(start.AddDays(2), null));
        Assert.Equal(DiscountCodeStatus.Expired, new DiscountCode().EffectiveStatus(start, programEndsAt: start.AddDays(-1)));
        Assert.Equal(DiscountCodeStatus.Retired, new DiscountCode { Status = DiscountCodeStatus.Retired, ValidTo = start }.EffectiveStatus(start.AddDays(1), null));
    }
}
