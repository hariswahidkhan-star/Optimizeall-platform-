using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Payouts;

namespace OptimizeAll.UnitTests.Payouts;

public sealed class PayoutPeriodCalculatorTests
{
    private static PayoutSchedule Schedule(
        PayoutFrequency frequency = PayoutFrequency.Biweekly, string anchor = "2026-01-04", string time = "23:59:59",
        string timeZone = "UTC", int paymentDelayDays = 5) => new()
    {
        Frequency = frequency,
        AnchorCutoffDate = DateOnly.Parse(anchor),
        CutoffLocalTime = TimeOnly.Parse(time),
        TimeZone = timeZone,
        PaymentDelayDays = paymentDelayDays,
    };

    private static DateTime Utc(string iso) =>
        DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime;

    [Theory]
    // Around the anchor (2026-01-04, a Sunday) — before and after it.
    [InlineData("2026-01-10T12:00:00Z", "2026-01-18", "2026-01-04T23:59:59Z", "2026-01-18T23:59:59Z")]
    [InlineData("2026-01-04T00:00:00Z", "2026-01-04", "2025-12-21T23:59:59Z", "2026-01-04T23:59:59Z")]
    [InlineData("2025-12-25T08:00:00Z", "2026-01-04", "2025-12-21T23:59:59Z", "2026-01-04T23:59:59Z")]
    [InlineData("2025-12-10T08:00:00Z", "2025-12-21", "2025-12-07T23:59:59Z", "2025-12-21T23:59:59Z")]
    [InlineData("2024-02-29T08:00:00Z", "2024-03-03", "2024-02-18T23:59:59Z", "2024-03-03T23:59:59Z")]
    [InlineData("2026-09-23T13:00:00Z", "2026-09-27", "2026-09-13T23:59:59Z", "2026-09-27T23:59:59Z")]
    public void Biweekly_periods_repeat_every_14_days_from_the_anchor_in_both_directions(
        string instant, string key, string start, string cutoff)
    {
        var period = PayoutPeriodCalculator.PeriodContaining(Schedule(), Utc(instant));
        Assert.Equal(key, period.PeriodKey);
        Assert.Equal(Utc(start), period.PeriodStartUtc);
        Assert.Equal(Utc(cutoff), period.CutoffUtc);
        Assert.Equal(DateTimeKind.Utc, period.CutoffUtc.Kind);
        Assert.True(period.Contains(Utc(instant)));
    }

    [Fact]
    public void An_instant_equal_to_the_cutoff_belongs_to_that_period_and_one_tick_later_to_the_next()
    {
        var schedule = Schedule();
        var cutoff = Utc("2026-01-18T23:59:59Z");
        Assert.Equal("2026-01-18", PayoutPeriodCalculator.PeriodContaining(schedule, cutoff).PeriodKey);
        Assert.Equal("2026-02-01", PayoutPeriodCalculator.PeriodContaining(schedule, cutoff.AddTicks(1)).PeriodKey);
        Assert.Equal("2026-01-18", PayoutPeriodCalculator.PeriodContaining(schedule, cutoff.AddTicks(-1)).PeriodKey);
    }

    [Fact]
    public void Previous_next_and_last_completed_are_consistent()
    {
        var schedule = Schedule();
        var period = PayoutPeriodCalculator.PeriodContaining(schedule, Utc("2026-01-10T00:00:00Z"));
        var next = PayoutPeriodCalculator.Next(schedule, period);
        var previous = PayoutPeriodCalculator.Previous(schedule, period);
        Assert.Equal("2026-02-01", next.PeriodKey);
        Assert.Equal(period.CutoffUtc, next.PeriodStartUtc);
        Assert.Equal("2026-01-04", previous.PeriodKey);
        Assert.Equal(previous.CutoffUtc, period.PeriodStartUtc);

        Assert.Equal("2026-01-04", PayoutPeriodCalculator.LastCompletedPeriod(schedule, Utc("2026-01-10T00:00:00Z")).PeriodKey);
        // At the exact cutoff instant the period is still open.
        Assert.Equal("2026-01-04", PayoutPeriodCalculator.LastCompletedPeriod(schedule, Utc("2026-01-18T23:59:59Z")).PeriodKey);
        Assert.Equal("2026-01-18", PayoutPeriodCalculator.LastCompletedPeriod(schedule, Utc("2026-01-19T00:00:00Z")).PeriodKey);
    }

    [Fact]
    public void Weekly_periods_repeat_every_7_days()
    {
        var schedule = Schedule(PayoutFrequency.Weekly);
        Assert.Equal("2026-01-11", PayoutPeriodCalculator.PeriodContaining(schedule, Utc("2026-01-06T10:00:00Z")).PeriodKey);
        Assert.Equal("2025-12-28", PayoutPeriodCalculator.PeriodContaining(schedule, Utc("2025-12-22T10:00:00Z")).PeriodKey);
        var upcoming = PayoutPeriodCalculator.Upcoming(schedule, Utc("2026-01-06T10:00:00Z"), 3);
        Assert.Equal(new[] { "2026-01-11", "2026-01-18", "2026-01-25" }, upcoming.Select(p => p.PeriodKey));
    }

    [Fact]
    public void Payment_date_is_cutoff_local_date_plus_delay()
    {
        var period = PayoutPeriodCalculator.PeriodContaining(Schedule(paymentDelayDays: 5), Utc("2026-01-10T00:00:00Z"));
        Assert.Equal(new DateOnly(2026, 1, 23), period.PaymentDate);
        var sameDay = PayoutPeriodCalculator.PeriodContaining(Schedule(paymentDelayDays: 0), Utc("2026-01-10T00:00:00Z"));
        Assert.Equal(new DateOnly(2026, 1, 18), sameDay.PaymentDate);
        // Payment date follows the LOCAL cutoff date, not the UTC date of the cutoff instant.
        var newYork = PayoutPeriodCalculator.PeriodContaining(Schedule(timeZone: "America/New_York", paymentDelayDays: 1), Utc("2026-01-10T00:00:00Z"));
        Assert.Equal(new DateOnly(2026, 1, 18), newYork.CutoffLocalDate);
        Assert.Equal(new DateOnly(2026, 1, 19), newYork.PaymentDate);
    }

    [Fact]
    public void Upcoming_returns_the_current_period_and_the_following_ones()
    {
        var schedule = Schedule();
        var now = Utc("2026-09-23T13:00:00Z");
        var upcoming = PayoutPeriodCalculator.Upcoming(schedule, now, 6);
        Assert.Equal(6, upcoming.Count);
        Assert.True(upcoming[0].Contains(now));
        Assert.Equal(new[] { "2026-09-27", "2026-10-11", "2026-10-25", "2026-11-08", "2026-11-22", "2026-12-06" },
            upcoming.Select(p => p.PeriodKey));
        for (var i = 1; i < upcoming.Count; i++) Assert.Equal(upcoming[i - 1].CutoffUtc, upcoming[i].PeriodStartUtc);
        Assert.Empty(PayoutPeriodCalculator.Upcoming(schedule, now, 0));
    }

    [Theory]
    [InlineData("Asia/Karachi", "2026-01-04T18:59:59Z")]   // UTC+5, no DST
    [InlineData("Asia/Kolkata", "2026-01-04T18:29:59Z")]   // UTC+5:30
    [InlineData("UTC", "2026-01-04T23:59:59Z")]
    [InlineData("America/New_York", "2026-01-05T04:59:59Z")] // EST, UTC-5
    [InlineData("Australia/Sydney", "2026-01-04T12:59:59Z")] // AEDT (southern summer), UTC+11
    public void Cutoff_is_the_local_cutoff_time_in_the_schedule_time_zone(string timeZone, string expectedCutoff)
    {
        var schedule = Schedule(timeZone: timeZone);
        var cutoff = Utc(expectedCutoff);
        var period = PayoutPeriodCalculator.PeriodContaining(schedule, cutoff);
        Assert.Equal("2026-01-04", period.PeriodKey);
        Assert.Equal(cutoff, period.CutoffUtc);
        Assert.Equal("2026-01-18", PayoutPeriodCalculator.PeriodContaining(schedule, cutoff.AddTicks(1)).PeriodKey);
    }

    [Fact]
    public void Karachi_boundary_just_before_and_after_local_midnight()
    {
        var schedule = Schedule(timeZone: "Asia/Karachi");
        // 23:59:59 PKT on the 4th is 18:59:59Z; 19:00Z (= 00:00 PKT on the 5th) is the next period.
        Assert.Equal("2026-01-04", PayoutPeriodCalculator.PeriodContaining(schedule, Utc("2026-01-04T18:59:59Z")).PeriodKey);
        Assert.Equal("2026-01-18", PayoutPeriodCalculator.PeriodContaining(schedule, Utc("2026-01-04T19:00:00Z")).PeriodKey);
        // Late on the 4th in UTC is already the 5th in Karachi.
        Assert.Equal("2026-01-18", PayoutPeriodCalculator.PeriodContaining(schedule, Utc("2026-01-04T21:00:00Z")).PeriodKey);
    }

    [Fact]
    public void New_York_summer_and_winter_offsets_are_applied()
    {
        var schedule = Schedule(PayoutFrequency.Weekly, "2026-01-04", "23:59:59", "America/New_York");
        Assert.Equal(Utc("2026-01-12T04:59:59Z"), PayoutPeriodCalculator.PeriodContaining(schedule, Utc("2026-01-10T00:00:00Z")).CutoffUtc);
        Assert.Equal(Utc("2026-07-13T03:59:59Z"), PayoutPeriodCalculator.PeriodContaining(schedule, Utc("2026-07-10T00:00:00Z")).CutoffUtc);
    }

    [Fact]
    public void New_York_spring_forward_gap_resolves_to_the_first_valid_instant_after_it()
    {
        // 2026-03-08 02:30 does not exist in New York (02:00 → 03:00 EDT).
        var schedule = Schedule(PayoutFrequency.Weekly, "2026-03-08", "02:30", "America/New_York");
        var period = PayoutPeriodCalculator.PeriodContaining(schedule, Utc("2026-03-05T00:00:00Z"));
        Assert.Equal("2026-03-08", period.PeriodKey);
        Assert.Equal(Utc("2026-03-08T07:00:00Z"), period.CutoffUtc); // 03:00 EDT
        // Neighbouring weeks use the normal offsets.
        Assert.Equal(Utc("2026-03-01T07:30:00Z"), period.PeriodStartUtc); // 02:30 EST
        Assert.Equal(Utc("2026-03-15T06:30:00Z"), PayoutPeriodCalculator.Next(schedule, period).CutoffUtc); // 02:30 EDT
    }

    [Fact]
    public void New_York_fall_back_ambiguity_resolves_to_the_later_instant()
    {
        // 2026-11-01 01:30 happens twice (EDT then EST); the cutoff is the second one (06:30Z), never the earlier 05:30Z.
        var schedule = Schedule(PayoutFrequency.Weekly, "2026-03-08", "01:30", "America/New_York");
        var period = PayoutPeriodCalculator.PeriodContaining(schedule, Utc("2026-11-01T05:45:00Z"));
        Assert.Equal("2026-11-01", period.PeriodKey);
        Assert.Equal(Utc("2026-11-01T06:30:00Z"), period.CutoffUtc);
        Assert.Equal("2026-11-08", PayoutPeriodCalculator.PeriodContaining(schedule, Utc("2026-11-01T06:30:00.0000001Z")).PeriodKey);
    }

    [Fact]
    public void Sydney_southern_hemisphere_dst_transitions()
    {
        // DST ends 2026-04-05 03:00 → 02:00 (02:30 ambiguous → later, AEST +10); starts 2026-10-04 02:00 → 03:00 (gap).
        var ambiguous = Schedule(PayoutFrequency.Weekly, "2026-04-05", "02:30", "Australia/Sydney");
        Assert.Equal(Utc("2026-04-04T16:30:00Z"), PayoutPeriodCalculator.PeriodContaining(ambiguous, Utc("2026-04-03T00:00:00Z")).CutoffUtc);

        var gap = Schedule(PayoutFrequency.Weekly, "2026-10-04", "02:30", "Australia/Sydney");
        Assert.Equal(Utc("2026-10-03T16:00:00Z"), PayoutPeriodCalculator.PeriodContaining(gap, Utc("2026-10-02T00:00:00Z")).CutoffUtc);

        var normal = Schedule(PayoutFrequency.Weekly, "2026-01-04", "23:59:59", "Australia/Sydney");
        Assert.Equal(Utc("2026-07-05T13:59:59Z"), PayoutPeriodCalculator.PeriodContaining(normal, Utc("2026-07-01T00:00:00Z")).CutoffUtc);
    }

    [Theory]
    [InlineData("2026-02-10T00:00:00Z", "2026-02-28")]
    [InlineData("2026-03-15T00:00:00Z", "2026-03-31")]
    [InlineData("2026-04-15T00:00:00Z", "2026-04-30")]
    [InlineData("2028-02-15T00:00:00Z", "2028-02-29")] // leap year
    [InlineData("2028-03-01T00:00:00Z", "2028-03-31")]
    [InlineData("2025-12-01T00:00:00Z", "2025-12-31")] // before the anchor
    [InlineData("2025-11-15T00:00:00Z", "2025-11-30")]
    [InlineData("2026-01-31T23:59:59Z", "2026-01-31")]
    [InlineData("2026-02-01T00:00:00Z", "2026-02-28")]
    public void Monthly_cutoff_uses_the_anchor_day_clamped_to_the_month_length(string instant, string key)
    {
        var schedule = Schedule(PayoutFrequency.Monthly, "2026-01-31");
        Assert.Equal(key, PayoutPeriodCalculator.PeriodContaining(schedule, Utc(instant)).PeriodKey);
    }

    [Fact]
    public void Monthly_mid_month_anchor()
    {
        var schedule = Schedule(PayoutFrequency.Monthly, "2026-01-15", "18:00", "Asia/Karachi");
        var period = PayoutPeriodCalculator.PeriodContaining(schedule, Utc("2026-03-15T12:59:59Z"));
        Assert.Equal("2026-03-15", period.PeriodKey);
        Assert.Equal(Utc("2026-03-15T13:00:00Z"), period.CutoffUtc);
        Assert.Equal(Utc("2026-02-15T13:00:00Z"), period.PeriodStartUtc);
        Assert.Equal("2026-04-15", PayoutPeriodCalculator.PeriodContaining(schedule, Utc("2026-03-15T13:00:01Z")).PeriodKey);
    }

    [Fact]
    public void Period_keys_round_trip_and_invalid_keys_are_rejected()
    {
        var schedule = Schedule();
        Assert.True(PayoutPeriodCalculator.TryFromKey(schedule, "2026-01-18", out var period));
        Assert.Equal(Utc("2026-01-18T23:59:59Z"), period.CutoffUtc);
        Assert.Equal(Utc("2026-01-04T23:59:59Z"), period.PeriodStartUtc);
        Assert.False(PayoutPeriodCalculator.TryFromKey(schedule, "2026-01-19", out _));
        Assert.False(PayoutPeriodCalculator.TryFromKey(schedule, "not-a-date", out _));
        Assert.False(PayoutPeriodCalculator.TryFromKey(schedule, null, out _));

        var monthly = Schedule(PayoutFrequency.Monthly, "2026-01-31");
        Assert.True(PayoutPeriodCalculator.TryFromKey(monthly, "2026-02-28", out _));
        Assert.False(PayoutPeriodCalculator.TryFromKey(monthly, "2026-02-27", out _));
    }

    [Fact]
    public void Unknown_time_zones_are_rejected()
    {
        var ex = Assert.Throws<DomainException>(() => PayoutPeriodCalculator.PeriodContaining(Schedule(timeZone: "Mars/Olympus"), DateTime.UtcNow));
        Assert.Equal("payout.invalid_time_zone", ex.Code);
    }
}
