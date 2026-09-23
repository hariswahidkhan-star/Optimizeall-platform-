using System.Globalization;

namespace OptimizeAll.Domain.EmailMarketing;

/// <summary>Time-zone aware scheduling rules: recipient-local send times, send windows and SMS quiet hours.</summary>
public static class SendTiming
{
    /// <summary>Resolves an IANA time zone, falling back to <paramref name="fallback"/> and then UTC.</summary>
    public static TimeZoneInfo Zone(string? timeZone, string? fallback = null)
    {
        foreach (var candidate in new[] { timeZone, fallback })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    public static bool IsValidZone(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone)) return false;
        try { TimeZoneInfo.FindSystemTimeZoneById(timeZone); return true; }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return false; }
    }

    /// <summary>Parses "yyyy-MM-ddTHH:mm" (local wall-clock time).</summary>
    public static bool TryParseLocal(string? value, out DateTime local) =>
        DateTime.TryParseExact(value, new[] { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss" }, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out local);

    /// <summary>
    /// UTC instant of a local wall-clock time in <paramref name="zone"/>. Times skipped by a DST jump move forward by
    /// the gap; ambiguous times use the first (daylight) occurrence.
    /// </summary>
    public static DateTime LocalToUtc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddMinutes(15);
        if (zone.IsAmbiguousTime(unspecified))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(unspecified);
            var max = offsets.Max();
            return DateTime.SpecifyKind(unspecified - max, DateTimeKind.Utc);
        }
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }

    /// <summary>True when <paramref name="hour"/> falls in [start, end), which may wrap midnight (e.g. 21 → 8).</summary>
    public static bool InHourRange(int hour, int start, int end)
    {
        if (start == end) return false;
        return start < end ? hour >= start && hour < end : hour >= start || hour < end;
    }

    /// <summary>
    /// The earliest instant at or after <paramref name="nowUtc"/> whose local hour is inside [start, end). Returns
    /// <paramref name="nowUtc"/> when already inside. Used both for campaign send windows (allowed range) and, with the
    /// quiet-hours range inverted, for SMS quiet hours.
    /// </summary>
    public static DateTime NextInWindow(DateTime nowUtc, TimeZoneInfo zone, int start, int end)
    {
        if (start == end) return nowUtc;
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        if (InHourRange(local.Hour, start, end)) return nowUtc;
        var candidate = local.Date.AddHours(start);
        if (candidate <= local) candidate = candidate.AddDays(1);
        return LocalToUtc(candidate, zone);
    }

    /// <summary>True when the recipient's local time is inside SMS quiet hours.</summary>
    public static bool IsQuietHours(DateTime nowUtc, TimeZoneInfo zone, int quietStart, int quietEnd)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        return InHourRange(local.Hour, quietStart, quietEnd);
    }

    /// <summary>When quiet hours are active, the instant they end (recipient local time); otherwise <paramref name="nowUtc"/>.</summary>
    public static DateTime AfterQuietHours(DateTime nowUtc, TimeZoneInfo zone, int quietStart, int quietEnd) =>
        IsQuietHours(nowUtc, zone, quietStart, quietEnd) ? NextInWindow(nowUtc, zone, quietEnd, quietStart) : nowUtc;

    /// <summary>
    /// When the recipient should receive a campaign: the fixed time, or the local time in their zone. A local time
    /// that already passed in the recipient's zone (they are "ahead" of the send) is sent immediately.
    /// </summary>
    public static DateTime DueAt(ScheduleMode mode, DateTime? scheduledAtUtc, string? localTime, TimeZoneInfo recipientZone, DateTime nowUtc)
    {
        switch (mode)
        {
            case ScheduleMode.FixedTime when scheduledAtUtc is { } fixedAt:
                return fixedAt < nowUtc ? nowUtc : fixedAt;
            case ScheduleMode.RecipientTimeZone when TryParseLocal(localTime, out var local):
                var due = LocalToUtc(local, recipientZone);
                return due < nowUtc ? nowUtc : due;
            default:
                return nowUtc;
        }
    }
}
