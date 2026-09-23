using System.Globalization;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Payouts;

/// <summary>
/// One payout period: the half-open interval (<see cref="PeriodStartUtc"/>, <see cref="CutoffUtc"/>].
/// An instant equal to the cutoff belongs to this period; one tick later belongs to the next.
/// </summary>
public sealed record PayoutPeriod(
    DateOnly CutoffLocalDate,
    DateTime PeriodStartUtc,
    DateTime CutoffUtc,
    DateOnly PaymentDate,
    string PeriodKey)
{
    /// <summary>True when <paramref name="instantUtc"/> falls in (PeriodStartUtc, CutoffUtc].</summary>
    public bool Contains(DateTime instantUtc) => instantUtc > PeriodStartUtc && instantUtc <= CutoffUtc;
}

/// <summary>
/// Pure payout-period arithmetic for a <see cref="PayoutSchedule"/>.
///
/// Rules:
/// * Weekly / Biweekly: cutoff local dates are <c>AnchorCutoffDate + k·7</c> / <c>+ k·14</c> days for any integer k
///   (also before the anchor).
/// * Monthly: the cutoff falls on the anchor's day-of-month, clamped to the month length (anchor 31st → 30 Apr, 28/29 Feb).
/// * The cutoff instant is the cutoff local date at <c>CutoffLocalTime</c> in the schedule's IANA time zone.
///   DST: a local time inside a spring-forward gap resolves to the first valid instant after the gap (the moment
///   clocks jump forward); an ambiguous fall-back time resolves to the LATER occurrence, so an earning is never cut
///   off early.
/// * A period is (previous cutoff, this cutoff]; an earning belongs to the first period whose cutoff ≥ its AvailableAt.
/// * PaymentDate = CutoffLocalDate + PaymentDelayDays; PeriodKey = CutoffLocalDate formatted yyyy-MM-dd.
/// </summary>
public static class PayoutPeriodCalculator
{
    public const string PeriodKeyFormat = "yyyy-MM-dd";

    /// <summary>The period containing <paramref name="instantUtc"/> (the first period whose cutoff ≥ the instant).</summary>
    public static PayoutPeriod PeriodContaining(PayoutSchedule schedule, DateTime instantUtc)
    {
        instantUtc = AsUtc(instantUtc);
        var tz = ResolveTimeZone(schedule.TimeZone);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(instantUtc, tz));

        var index = EstimateIndex(schedule, localDate);
        // Walk to the first cutoff ≥ instant. The estimate is at most one or two steps off (time of day, DST).
        while (CutoffUtc(schedule, tz, index) < instantUtc) index++;
        while (CutoffUtc(schedule, tz, index - 1) >= instantUtc) index--;
        return Build(schedule, tz, index);
    }

    /// <summary>The period immediately before <paramref name="period"/>.</summary>
    public static PayoutPeriod Previous(PayoutSchedule schedule, PayoutPeriod period) =>
        PeriodContaining(schedule, period.PeriodStartUtc);

    /// <summary>The period immediately after <paramref name="period"/>.</summary>
    public static PayoutPeriod Next(PayoutSchedule schedule, PayoutPeriod period) =>
        PeriodContaining(schedule, period.CutoffUtc.AddTicks(1));

    /// <summary>The most recent period whose cutoff is strictly before <paramref name="nowUtc"/>.</summary>
    public static PayoutPeriod LastCompletedPeriod(PayoutSchedule schedule, DateTime nowUtc)
    {
        var current = PeriodContaining(schedule, nowUtc);
        return current.CutoffUtc < AsUtc(nowUtc) ? current : Previous(schedule, current);
    }

    /// <summary>The current period (containing <paramref name="nowUtc"/>) followed by the next <c>count − 1</c> periods.</summary>
    public static IReadOnlyList<PayoutPeriod> Upcoming(PayoutSchedule schedule, DateTime nowUtc, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var result = new List<PayoutPeriod>(count);
        if (count == 0) return result;
        var period = PeriodContaining(schedule, nowUtc);
        result.Add(period);
        while (result.Count < count)
        {
            period = Next(schedule, period);
            result.Add(period);
        }
        return result;
    }

    /// <summary>
    /// Resolves a period from its key (the cutoff local date). Returns false when the date is not a cutoff date of
    /// the schedule.
    /// </summary>
    public static bool TryFromKey(PayoutSchedule schedule, string? periodKey, out PayoutPeriod period)
    {
        period = null!;
        if (!DateOnly.TryParseExact(periodKey, PeriodKeyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;
        var tz = ResolveTimeZone(schedule.TimeZone);
        var index = EstimateIndex(schedule, date);
        if (CutoffLocalDate(schedule, index) != date) return false;
        period = Build(schedule, tz, index);
        return true;
    }

    /// <summary>
    /// Converts a local wall-clock time in <paramref name="tz"/> to UTC. Gap (spring forward) → first valid instant
    /// after the gap; ambiguous (fall back) → the later instant.
    /// </summary>
    public static DateTime LocalToUtc(DateOnly date, TimeOnly time, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local))
        {
            // Clocks jump forward on whole-minute boundaries; the first valid whole minute after the requested
            // time is the transition instant itself.
            var probe = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified)
                .AddMinutes(1);
            var guard = 0;
            while (tz.IsInvalidTime(probe))
            {
                probe = probe.AddMinutes(1);
                if (++guard > 24 * 60) throw new InvalidOperationException($"Unresolvable local time {local:o} in {tz.Id}.");
            }
            return TimeZoneInfo.ConvertTimeToUtc(probe, tz);
        }
        if (tz.IsAmbiguousTime(local))
        {
            // The smaller UTC offset gives the later UTC instant (second occurrence of the wall-clock time).
            var offset = tz.GetAmbiguousTimeOffsets(local).Min();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    /// <summary>Finds an IANA (or Windows) time zone; throws a validation <see cref="DomainException"/> when unknown.</summary>
    public static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new DomainException("payout.invalid_time_zone", "A time zone is required.");
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new DomainException("payout.invalid_time_zone", $"Unknown time zone '{timeZoneId}'.");
        }
    }

    public static string KeyFor(DateOnly cutoffLocalDate) =>
        cutoffLocalDate.ToString(PeriodKeyFormat, CultureInfo.InvariantCulture);

    private static PayoutPeriod Build(PayoutSchedule schedule, TimeZoneInfo tz, long index)
    {
        var cutoffDate = CutoffLocalDate(schedule, index);
        return new PayoutPeriod(
            cutoffDate,
            CutoffUtc(schedule, tz, index - 1),
            CutoffUtc(schedule, tz, index),
            cutoffDate.AddDays(schedule.PaymentDelayDays),
            KeyFor(cutoffDate));
    }

    private static DateTime CutoffUtc(PayoutSchedule schedule, TimeZoneInfo tz, long index) =>
        LocalToUtc(CutoffLocalDate(schedule, index), schedule.CutoffLocalTime, tz);

    private static DateOnly CutoffLocalDate(PayoutSchedule schedule, long index)
    {
        var anchor = schedule.AnchorCutoffDate;
        switch (schedule.Frequency)
        {
            case PayoutFrequency.Weekly:
            case PayoutFrequency.Biweekly:
                return anchor.AddDays(checked((int)(index * StepDays(schedule.Frequency))));
            case PayoutFrequency.Monthly:
                var firstOfMonth = new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(checked((int)index));
                var day = Math.Min(anchor.Day, DateTime.DaysInMonth(firstOfMonth.Year, firstOfMonth.Month));
                return new DateOnly(firstOfMonth.Year, firstOfMonth.Month, day);
            default:
                throw new ArgumentOutOfRangeException(nameof(schedule), schedule.Frequency, "Unknown payout frequency.");
        }
    }

    /// <summary>Index of the last cutoff local date on or before <paramref name="localDate"/>.</summary>
    private static long EstimateIndex(PayoutSchedule schedule, DateOnly localDate)
    {
        var anchor = schedule.AnchorCutoffDate;
        if (schedule.Frequency == PayoutFrequency.Monthly)
        {
            long months = (localDate.Year - anchor.Year) * 12L + (localDate.Month - anchor.Month);
            return CutoffLocalDate(schedule, months) > localDate ? months - 1 : months;
        }
        var step = StepDays(schedule.Frequency);
        long days = localDate.DayNumber - anchor.DayNumber;
        return (long)Math.Floor(days / (double)step);
    }

    private static int StepDays(PayoutFrequency frequency) => frequency == PayoutFrequency.Weekly ? 7 : 14;

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
