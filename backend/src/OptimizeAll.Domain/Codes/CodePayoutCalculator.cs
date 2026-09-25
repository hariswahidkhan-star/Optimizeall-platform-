using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Codes;

/// <summary>A per-sale rate: a flat amount or a percentage of the net order value.</summary>
public sealed record CodeRate(CodePayoutType Type, decimal? FlatAmount, decimal? Percent)
{
    public string Describe(string currency) => Type == CodePayoutType.FlatPerSale
        ? $"{Money.Format(FlatAmount ?? 0m, currency)} per sale"
        : $"{(Percent ?? 0m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}% of net";
}

public sealed record CodeTierRule(int ThresholdSales, decimal? FlatAmount, decimal? Percent, decimal? BonusAmount);

/// <summary>Where the per-sale rate came from.</summary>
public enum CodeRateSource
{
    PersonOverride,
    GroupOverride,
    Tier,
    Program,
}

/// <summary>Everything the calculator needs; amounts in the program currency.</summary>
public sealed record CodePayoutInput(
    string Currency,
    decimal NetAmount,
    CodeRate ProgramRate,
    IReadOnlyList<CodeTierRule> Tiers,
    CodeRate? OverrideRate,
    CodeRateSource? OverrideSource,
    string? OverrideLabel,
    int PriorApprovedSales,
    IReadOnlySet<int> TierBonusesAlreadyEarned,
    decimal? DailyCap,
    decimal EarnedToday,
    decimal? ProgramCapPerPerson,
    decimal EarnedInProgram,
    decimal? Budget,
    decimal SpentFromBudget);

public enum CodePayoutLineKind
{
    Commission,
    TierBonus,
}

public sealed record CodePayoutLine(CodePayoutLineKind Kind, decimal Amount, decimal UncappedAmount, int? TierThreshold, string Label);

public sealed record CodePayoutResult(
    IReadOnlyList<CodePayoutLine> Lines,
    decimal Total,
    decimal UncappedTotal,
    IReadOnlyList<string> AppliedCaps,
    CodeRateSource RateSource,
    string RateLabel,
    int SaleNumber);

/// <summary>
/// Pure commission calculation for an approved code sale (docs/DISCOUNT_CODES.md "Payout rules"):
/// <list type="number">
/// <item>The sale is the person's k-th approved sale in the program (k = prior + 1).</item>
/// <item>Per-sale rate: a person override, else a group override, else the tier with the highest threshold below k
///   that sets a rate, else the program rate. Flat amount, or percent × net order value, rounded to the currency's minor unit.</item>
/// <item>Tier bonuses: every tier with a bonus whose threshold ≤ k and that the person has not earned yet (once each).</item>
/// <item>Caps, line by line (commission first): per-person daily cap, per-person program cap and the program budget.
///   A capped amount is rounded down to the minor unit; lines that end at 0 are dropped.</item>
/// </list>
/// </summary>
public static class CodePayoutCalculator
{
    public const decimal MaxAmount = 1_000_000m;

    public static CodePayoutResult Calculate(CodePayoutInput input)
    {
        var currency = Money.Normalize(input.Currency);
        var k = input.PriorApprovedSales + 1;

        CodeRate rate;
        CodeRateSource source;
        string label;
        if (input.OverrideRate is { } o)
        {
            rate = o;
            source = input.OverrideSource ?? CodeRateSource.PersonOverride;
            label = input.OverrideLabel ?? (source == CodeRateSource.GroupOverride ? "Group rate" : "Personal rate");
        }
        else if (input.Tiers.Where(t => k > t.ThresholdSales && (t.FlatAmount is not null || t.Percent is not null))
                     .OrderByDescending(t => t.ThresholdSales).FirstOrDefault() is { } tier)
        {
            rate = tier.FlatAmount is not null
                ? new CodeRate(CodePayoutType.FlatPerSale, tier.FlatAmount, null)
                : new CodeRate(CodePayoutType.PercentOfNet, null, tier.Percent);
            source = CodeRateSource.Tier;
            label = $"Tier after {tier.ThresholdSales} sales";
        }
        else
        {
            rate = input.ProgramRate;
            source = CodeRateSource.Program;
            label = "Program rate";
        }
        label = $"{label} · {rate.Describe(currency)}";

        var raw = new List<(CodePayoutLineKind Kind, decimal Amount, int? Tier, string Label)>
        {
            (CodePayoutLineKind.Commission, Commission(rate, input.NetAmount, currency), null, "Sale commission"),
        };
        foreach (var t in input.Tiers.Where(t => t.BonusAmount is > 0 && t.ThresholdSales <= k && !input.TierBonusesAlreadyEarned.Contains(t.ThresholdSales))
                     .OrderBy(t => t.ThresholdSales))
            raw.Add((CodePayoutLineKind.TierBonus, Money.Round(t.BonusAmount!.Value, currency), t.ThresholdSales, $"Bonus for {t.ThresholdSales} sales"));

        var daily = input.DailyCap is { } d ? Math.Max(0m, d - input.EarnedToday) : (decimal?)null;
        var program = input.ProgramCapPerPerson is { } p ? Math.Max(0m, p - input.EarnedInProgram) : (decimal?)null;
        var budget = input.Budget is { } b ? Math.Max(0m, b - input.SpentFromBudget) : (decimal?)null;
        var applied = new List<string>();
        var lines = new List<CodePayoutLine>();
        foreach (var (kind, amount, tier, lineLabel) in raw)
        {
            var value = amount;
            if (daily is { } dh && value > dh) { value = dh; Add(applied, "daily_cap"); }
            if (program is { } ph && value > ph) { value = ph; Add(applied, "program_cap"); }
            if (budget is { } bh && value > bh) { value = bh; Add(applied, "program_budget"); }
            if (value != amount) value = RoundDown(value, currency);
            if (daily is not null) daily -= value;
            if (program is not null) program -= value;
            if (budget is not null) budget -= value;
            if (value > 0) lines.Add(new CodePayoutLine(kind, value, amount, tier, lineLabel));
        }
        return new CodePayoutResult(lines, lines.Sum(l => l.Amount), raw.Sum(r => r.Amount), applied, source, label, k);
    }

    /// <summary>The informational estimate shown before approval (no caps).</summary>
    public static decimal Estimate(CodePayoutInput input) => Calculate(input with
    {
        DailyCap = null, ProgramCapPerPerson = null, Budget = null,
    }).Total;

    public static decimal Commission(CodeRate rate, decimal net, string currency) => rate.Type == CodePayoutType.FlatPerSale
        ? Money.Round(rate.FlatAmount ?? 0m, currency)
        : Money.Round(net * (rate.Percent ?? 0m) / 100m, currency);

    public static decimal RoundDown(decimal amount, string currency)
    {
        var factor = (decimal)Math.Pow(10, Money.MinorUnitDigits(currency));
        return Math.Floor(amount * factor) / factor;
    }

    private static void Add(List<string> applied, string cap)
    {
        if (!applied.Contains(cap)) applied.Add(cap);
    }

    // ------------------------------------------------------------------ validation

    /// <summary>Validates a rate; returns field errors (empty when valid).</summary>
    public static Dictionary<string, string[]> ValidateRate(CodePayoutType type, decimal? flat, decimal? percent, string prefix = "")
    {
        var errors = new Dictionary<string, string[]>();
        if (type == CodePayoutType.FlatPerSale)
        {
            if (flat is not > 0 || flat > MaxAmount) errors[prefix + "flatAmount"] = new[] { "Enter an amount per sale greater than 0." };
            if (percent is not null) errors[prefix + "percent"] = new[] { "A flat payout has no percentage." };
        }
        else
        {
            if (percent is not > 0 || percent > 100) errors[prefix + "percent"] = new[] { "Enter a percentage between 0 and 100." };
            if (flat is not null) errors[prefix + "flatAmount"] = new[] { "A percentage payout has no flat amount." };
        }
        return errors;
    }

    public static Dictionary<string, string[]> ValidateTiers(IReadOnlyList<CodeTierRule> tiers)
    {
        var errors = new Dictionary<string, string[]>();
        if (tiers.Count > 20) errors["tiers"] = new[] { "At most 20 tiers." };
        if (tiers.GroupBy(t => t.ThresholdSales).Any(g => g.Count() > 1)) errors["tiers"] = new[] { "Each tier needs a different number of sales." };
        for (var i = 0; i < tiers.Count; i++)
        {
            var t = tiers[i];
            if (t.ThresholdSales is < 1 or > 100_000) errors[$"tiers[{i}].thresholdSales"] = new[] { "Between 1 and 100,000 sales." };
            if (t.FlatAmount is null && t.Percent is null && t.BonusAmount is null)
                errors[$"tiers[{i}]"] = new[] { "Set a new rate, a bonus, or both." };
            if (t.FlatAmount is not null && t.Percent is not null) errors[$"tiers[{i}]"] = new[] { "Use a flat amount or a percentage, not both." };
            if (t.FlatAmount is { } f && (f <= 0 || f > MaxAmount)) errors[$"tiers[{i}].flatAmount"] = new[] { "Must be greater than 0." };
            if (t.Percent is { } p && (p <= 0 || p > 100)) errors[$"tiers[{i}].percent"] = new[] { "Between 0 and 100." };
            if (t.BonusAmount is { } bo && (bo <= 0 || bo > MaxAmount)) errors[$"tiers[{i}].bonusAmount"] = new[] { "Must be greater than 0." };
        }
        return errors;
    }
}

/// <summary>
/// Generates codes from a pattern: <c>#</c> = digit, <c>?</c> = letter, <c>*</c> = letter or digit, anything else literal
/// (e.g. <c>GLOW-????-##</c>). Ambiguous characters (0/O, 1/I/L) are never generated.
/// </summary>
public static partial class CodePattern
{
    private const string Letters = "ABCDEFGHJKMNPQRSTUVWXYZ";
    private const string Digits = "23456789";

    [GeneratedRegex("^[A-Za-z0-9#?*_-]{3,40}$")]
    private static partial Regex Allowed();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{1,63}$")]
    private static partial Regex CodeShape();

    public static bool IsValidPattern(string pattern) => Allowed().IsMatch(pattern) && pattern.Count(c => c is '#' or '?' or '*') >= 4;

    /// <summary>Codes are letters, digits, '-' and '_' (2–64 characters, starting with a letter or digit).</summary>
    public static bool IsValidCode(string code) => CodeShape().IsMatch(code.Trim());

    /// <summary>How many distinct codes the pattern can produce (capped at long.MaxValue).</summary>
    public static double Capacity(string pattern) => pattern.Aggregate(1d, (acc, c) => c switch
    {
        '#' => acc * Digits.Length,
        '?' => acc * Letters.Length,
        '*' => acc * (Letters.Length + Digits.Length),
        _ => acc,
    });

    public static string Generate(string pattern)
    {
        var sb = new StringBuilder(pattern.Length);
        foreach (var c in pattern)
        {
            sb.Append(c switch
            {
                '#' => Digits[RandomNumberGenerator.GetInt32(Digits.Length)],
                '?' => Letters[RandomNumberGenerator.GetInt32(Letters.Length)],
                '*' => (Letters + Digits)[RandomNumberGenerator.GetInt32(Letters.Length + Digits.Length)],
                _ => char.ToUpperInvariant(c),
            });
        }
        return sb.ToString();
    }
}
