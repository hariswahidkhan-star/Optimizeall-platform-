using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Ads;

/// <summary>Additive totals of ad metrics in one currency.</summary>
public sealed record AdTotals(decimal Spend, long Impressions, long Clicks, decimal Conversions, decimal ConversionValue, long Reach, long VideoViews)
{
    public static readonly AdTotals Zero = new(0, 0, 0, 0, 0, 0, 0);

    public AdTotals Add(AdTotals o) => new(Spend + o.Spend, Impressions + o.Impressions, Clicks + o.Clicks,
        Conversions + o.Conversions, ConversionValue + o.ConversionValue, Reach + o.Reach, VideoViews + o.VideoViews);

    /// <summary>Money converted with <paramref name="rate"/> and rounded to the target currency; counts unchanged.</summary>
    public AdTotals Convert(decimal rate, string currency) => this with
    {
        Spend = Money.Convert(Spend, rate, currency),
        ConversionValue = Money.Convert(ConversionValue, rate, currency),
    };
}

/// <summary>Derived KPIs. Every ratio is null when its denominator is zero (never a division-by-zero or a fake 0).</summary>
public sealed record AdKpis(
    decimal? Ctr, decimal? Cpc, decimal? Cpm, decimal? Cpa, decimal? Roas, decimal? ConversionRate, decimal? Frequency)
{
    public static AdKpis From(AdTotals t) => new(
        Ratio(t.Clicks, t.Impressions, 6),
        Ratio(t.Spend, t.Clicks, 4),
        t.Impressions == 0 ? null : Math.Round(t.Spend * 1000m / t.Impressions, 4),
        Ratio(t.Spend, t.Conversions, 4),
        Ratio(t.ConversionValue, t.Spend, 4),
        Ratio(t.Conversions, t.Clicks, 6),
        t.Reach == 0 ? null : Ratio(t.Impressions, t.Reach, 4));

    public static decimal? Ratio(decimal numerator, decimal denominator, int decimals) =>
        denominator == 0 ? null : Math.Round(numerator / denominator, decimals, MidpointRounding.AwayFromZero);
}

public enum PacingState
{
    NoBudget,
    NotStarted,
    OnTrack,
    Over,
    Under,
}

public sealed record PacingResult(
    decimal Budget, decimal ActualToDate, decimal ExpectedToDate, decimal? PacingRatio, decimal ProjectedMonthEnd,
    decimal? ProjectedVsBudget, int DaysElapsed, int DaysInMonth, decimal DailyRunRate, PacingState State);

/// <summary>
/// Budget pacing for a calendar month. <c>expected = budget × daysElapsed / daysInMonth</c> (days elapsed counts complete
/// days of data, i.e. through <c>asOf</c> inclusive); <c>pacing = actual / expected</c>; the month-end projection adds the
/// recent daily run-rate (average of the last up-to-7 elapsed days) for the remaining days.
/// </summary>
public static class Pacing
{
    public static PacingResult Compute(decimal budget, DateOnly month, DateOnly asOf, IReadOnlyDictionary<DateOnly, decimal> dailySpend,
        decimal overThreshold, decimal underThreshold, string currency)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        var last = first.AddDays(daysInMonth - 1);
        var elapsed = asOf < first ? 0 : asOf >= last ? daysInMonth : asOf.DayNumber - first.DayNumber + 1;

        var actual = dailySpend.Where(kv => kv.Key >= first && kv.Key <= last && kv.Key <= asOf).Sum(kv => kv.Value);
        var expected = elapsed == 0 ? 0m : Money.Round(budget * elapsed / daysInMonth, currency);

        var window = Math.Min(7, elapsed);
        var runRate = 0m;
        if (window > 0)
        {
            var windowStart = first.AddDays(elapsed - window);
            var windowEnd = first.AddDays(elapsed - 1);
            runRate = dailySpend.Where(kv => kv.Key >= windowStart && kv.Key <= windowEnd).Sum(kv => kv.Value) / window;
        }
        var projected = Money.Round(actual + runRate * (daysInMonth - elapsed), currency);
        decimal? ratio = expected == 0 ? null : Math.Round(actual / expected, 4);
        decimal? projectedVsBudget = budget == 0 ? null : Math.Round(projected / budget, 4);

        var state = budget <= 0 ? PacingState.NoBudget
            : elapsed == 0 ? PacingState.NotStarted
            : ratio > overThreshold ? PacingState.Over
            : ratio < underThreshold ? PacingState.Under
            : PacingState.OnTrack;
        return new PacingResult(budget, Money.Round(actual, currency), expected, ratio, projected, projectedVsBudget, elapsed, daysInMonth,
            Math.Round(runRate, 4), state);
    }
}
