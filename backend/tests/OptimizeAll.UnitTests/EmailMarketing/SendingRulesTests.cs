using OptimizeAll.Domain.EmailMarketing;

namespace OptimizeAll.UnitTests.EmailMarketing;

public sealed class SmsSegmentsTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(160, 1)]
    [InlineData(161, 2)]
    [InlineData(306, 2)]
    [InlineData(307, 3)]
    public void Gsm7_messages_hold_160_then_153_per_part(int length, int segments)
    {
        var info = SmsSegments.Calculate(new string('a', length));
        Assert.Equal(SmsEncoding.Gsm7, info.Encoding);
        Assert.Equal(segments, info.Segments);
    }

    [Fact]
    public void Extension_characters_take_two_septets_and_are_not_split()
    {
        var info = SmsSegments.Calculate(new string('€', 80));
        Assert.Equal(SmsEncoding.Gsm7, info.Encoding);
        Assert.Equal(160, info.Units);
        Assert.Equal(1, info.Segments);
        Assert.Equal(2, SmsSegments.Calculate(new string('€', 81)).Segments);
        // 306 septets would fit two 153-septet parts, but the escape pair cannot straddle parts 1 and 2: three parts.
        var straddling = SmsSegments.Calculate(new string('a', 152) + "€" + new string('a', 152));
        Assert.Equal(306, straddling.Units);
        Assert.Equal(3, straddling.Segments);
    }

    [Theory]
    [InlineData(70, 1)]
    [InlineData(71, 2)]
    [InlineData(134, 2)]
    [InlineData(135, 3)]
    public void Ucs2_messages_hold_70_then_67_per_part(int length, int segments)
    {
        var info = SmsSegments.Calculate("ا" + new string('b', length - 1));
        Assert.Equal(SmsEncoding.Ucs2, info.Encoding);
        Assert.Equal(segments, info.Segments);
    }

    [Fact]
    public void One_emoji_switches_to_ucs2_and_counts_two_units()
    {
        var info = SmsSegments.Calculate("Deal 🔥");
        Assert.Equal(SmsEncoding.Ucs2, info.Encoding);
        Assert.Equal(7, info.Units);
        Assert.Equal(1, info.Segments);
    }

    [Fact]
    public void Estimates_cost_and_recognises_keywords()
    {
        Assert.Equal(0.0158m * 100, SmsSegments.EstimateCost(new string('a', 200), 100, 0.0079m));
        Assert.True(SmsSegments.IsStop(" stop "));
        Assert.True(SmsSegments.IsStop("Unsubscribe."));
        Assert.False(SmsSegments.IsStop("please stop by"));
        Assert.True(SmsSegments.IsStart("START"));
        Assert.True(SmsSegments.HasOptOutInstruction("Reply STOP to opt out"));
    }
}

public sealed class SendTimingTests
{
    private static readonly TimeZoneInfo Karachi = SendTiming.Zone("Asia/Karachi"); // UTC+5
    private static readonly TimeZoneInfo NewYork = SendTiming.Zone("America/New_York");

    [Fact]
    public void Quiet_hours_use_the_recipient_time_zone_and_wrap_midnight()
    {
        var at2230Local = new DateTime(2026, 9, 23, 17, 30, 0, DateTimeKind.Utc); // 22:30 in Karachi
        Assert.True(SendTiming.IsQuietHours(at2230Local, Karachi, 21, 8));
        Assert.False(SendTiming.IsQuietHours(at2230Local, NewYork, 21, 8)); // 13:30 in New York
        var release = SendTiming.AfterQuietHours(at2230Local, Karachi, 21, 8);
        Assert.Equal(new DateTime(2026, 9, 24, 3, 0, 0, DateTimeKind.Utc), release); // 08:00 next day in Karachi
        var at0730Local = new DateTime(2026, 9, 24, 2, 30, 0, DateTimeKind.Utc);
        Assert.True(SendTiming.IsQuietHours(at0730Local, Karachi, 21, 8));
        Assert.Equal(new DateTime(2026, 9, 24, 3, 0, 0, DateTimeKind.Utc), SendTiming.AfterQuietHours(at0730Local, Karachi, 21, 8));
    }

    [Fact]
    public void Recipient_time_zone_scheduling_sends_at_local_time_or_now_when_passed()
    {
        var now = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);
        var karachi = SendTiming.DueAt(ScheduleMode.RecipientTimeZone, null, "2026-09-23T09:00", Karachi, now);
        var newYork = SendTiming.DueAt(ScheduleMode.RecipientTimeZone, null, "2026-09-23T09:00", NewYork, now);
        Assert.Equal(new DateTime(2026, 9, 23, 4, 0, 0, DateTimeKind.Utc), karachi);
        Assert.Equal(new DateTime(2026, 9, 23, 13, 0, 0, DateTimeKind.Utc), newYork);
        Assert.Equal(now.AddHours(10), SendTiming.DueAt(ScheduleMode.RecipientTimeZone, null, "2026-09-23T09:00", Karachi, now.AddHours(10)));
        Assert.Equal(now, SendTiming.DueAt(ScheduleMode.Immediate, null, null, Karachi, now));
    }

    [Fact]
    public void Send_windows_move_to_the_next_allowed_hour()
    {
        var at20Utc = new DateTime(2026, 9, 23, 20, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc), SendTiming.NextInWindow(at20Utc, TimeZoneInfo.Utc, 9, 17));
        Assert.Equal(at20Utc, SendTiming.NextInWindow(at20Utc, TimeZoneInfo.Utc, 18, 22));
        Assert.True(SendTiming.InHourRange(23, 22, 6));
        Assert.False(SendTiming.InHourRange(12, 22, 6));
    }

    [Fact]
    public void Dst_gaps_move_forward()
    {
        // 2026-03-08 02:30 does not exist in New York (clocks jump 02:00 → 03:00).
        var utc = SendTiming.LocalToUtc(new DateTime(2026, 3, 8, 2, 30, 0), NewYork);
        Assert.Equal(new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc), utc);
    }
}

public sealed class AbTestingTests
{
    [Fact]
    public void Picks_the_best_open_rate_and_breaks_ties_deterministically()
    {
        var stats = new[] { new VariantStats("A", 100, 20, 5), new VariantStats("B", 100, 30, 2) };
        Assert.Equal("B", AbTesting.PickWinner(stats, AbWinnerMetric.OpenRate));
        Assert.Equal("A", AbTesting.PickWinner(stats, AbWinnerMetric.ClickRate));
        var tie = new[] { new VariantStats("B", 50, 10, 1), new VariantStats("A", 50, 10, 1) };
        Assert.Equal("A", AbTesting.PickWinner(tie, AbWinnerMetric.OpenRate));
        // Rates, not counts: 9/30 beats 20/100.
        Assert.Equal("B", AbTesting.PickWinner(new[] { new VariantStats("A", 100, 20, 0), new VariantStats("B", 30, 9, 0) }, AbWinnerMetric.OpenRate));
        // A variant nobody received cannot win on a tie at zero.
        Assert.Equal("A", AbTesting.PickWinner(new[] { new VariantStats("A", 10, 0, 0), new VariantStats("B", 0, 0, 0) }, AbWinnerMetric.OpenRate));
    }

    [Fact]
    public void Assignment_is_deterministic_and_matches_the_test_percentage()
    {
        var campaign = Guid.NewGuid();
        var keys = new[] { "A", "B" };
        var subscribers = Enumerable.Range(0, 5000).Select(_ => Guid.NewGuid()).ToList();
        var assigned = subscribers.Select(s => AbTesting.Assign(campaign, s, 20, keys)).ToList();
        var cohort = assigned.Count(a => a is not null);
        Assert.InRange(cohort, 850, 1150);
        Assert.InRange(assigned.Count(a => a == "A"), cohort / 2 - 120, cohort / 2 + 120);
        Assert.Equal(assigned, subscribers.Select(s => AbTesting.Assign(campaign, s, 20, keys)).ToList());
    }
}

public sealed class EngagementHeuristicsTests
{
    [Fact]
    public void Separates_machine_opens_from_human_opens()
    {
        Assert.True(EngagementHeuristics.ClassifyOpen("Mozilla/5.0").IsMachine);
        Assert.True(EngagementHeuristics.ClassifyOpen("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko)").IsMachine);
        Assert.True(EngagementHeuristics.ClassifyOpen(null).IsMachine);
        Assert.True(EngagementHeuristics.ClassifyOpen("Barracuda Sentinel (EE)").IsMachine);
        var gmail = EngagementHeuristics.ClassifyOpen("Mozilla/5.0 (Windows NT 5.1; rv:11.0) Gecko Firefox/11.0 (via ggpht.com GoogleImageProxy)");
        Assert.False(gmail.IsMachine);
        Assert.Equal("Gmail", gmail.MailClient);
        var iphone = EngagementHeuristics.ClassifyOpen("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148");
        Assert.False(iphone.IsMachine);
        Assert.Equal(DeviceType.Mobile, iphone.Device);
    }

    [Fact]
    public void Flags_scanner_clicks_and_instant_clicks()
    {
        var sent = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
        const string chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";
        Assert.True(EngagementHeuristics.ClassifyClick(chrome, sent, sent.AddSeconds(3)).IsMachine);
        Assert.False(EngagementHeuristics.ClassifyClick(chrome, sent, sent.AddMinutes(4)).IsMachine);
        Assert.True(EngagementHeuristics.ClassifyClick("Mozilla/5.0 (compatible; Proofpoint URL Defense)", sent, sent.AddHours(1)).IsMachine);
    }

    [Fact]
    public void Engagement_tiers()
    {
        var now = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(EngagementTier.Active, EngagementTiers.Classify(now, now.AddDays(-400), now.AddDays(-3), null));
        Assert.Equal(EngagementTier.Warm, EngagementTiers.Classify(now, now.AddDays(-400), null, now.AddDays(-60)));
        Assert.Equal(EngagementTier.New, EngagementTiers.Classify(now, now.AddDays(-5), null, null));
        Assert.Equal(EngagementTier.Cold, EngagementTiers.Classify(now, now.AddDays(-400), now.AddDays(-200), null));
    }
}

public sealed class SegmentAndAutomationRulesTests
{
    [Fact]
    public void Segment_rules_are_validated()
    {
        var bad = SegmentDefinition.Parse("""
            { "match": "both", "conditions": [
              { "kind": "field", "field": "password", "op": "equals", "value": "x" },
              { "kind": "engagement", "event": "opened", "withinDays": 0 },
              { "kind": "tag", "op": "has", "value": "Bad Tag!" },
              { "kind": "sql", "value": "1=1" } ] }
            """);
        var errors = SegmentRules.Validate(bad);
        Assert.Contains(errors, e => e.Contains("match"));
        Assert.Contains(errors, e => e.Contains("unknown field"));
        Assert.Contains(errors, e => e.Contains("withinDays"));
        Assert.Contains(errors, e => e.Contains("invalid tag"));
        Assert.Contains(errors, e => e.Contains("unknown condition kind"));
        var ok = SegmentDefinition.Parse("""
            { "match": "all", "conditions": [ { "kind": "field", "field": "country", "op": "in", "values": ["US","GB"] } ],
              "groups": [ { "match": "any", "conditions": [ { "kind": "engagement", "event": "opened", "withinDays": 30 }, { "kind": "purchase", "op": "not_purchased", "withinDays": 90 } ] } ] }
            """);
        Assert.Empty(SegmentRules.Validate(ok));
    }

    [Fact]
    public void Journeys_must_be_acyclic_with_valid_references()
    {
        var steps = new[]
        {
            new StepDefinition { Key = "a", Type = AutomationStepType.Wait, Config = new StepConfig { Days = 1 }, Next = "b" },
            new StepDefinition { Key = "b", Type = AutomationStepType.Condition, Config = new StepConfig { Check = "opened" }, Next = "a", AltNext = "c" },
            new StepDefinition { Key = "c", Type = AutomationStepType.Exit },
        };
        Assert.Contains(AutomationRules.Validate(AutomationTrigger.NewsletterConfirmed, new TriggerConfig(), steps, "a"), e => e.Contains("loop"));
        steps[1].Next = "c";
        Assert.Empty(AutomationRules.Validate(AutomationTrigger.NewsletterConfirmed, new TriggerConfig(), steps, "a"));
        steps[0].Next = "zzz";
        Assert.Contains(AutomationRules.Validate(AutomationTrigger.NewsletterConfirmed, new TriggerConfig(), steps, "a"), e => e.Contains("unknown step"));
        Assert.Contains(AutomationRules.Validate(AutomationTrigger.ListSubscribed, new TriggerConfig(), steps, "a"), e => e.Contains("list"));
    }

    [Fact]
    public void Waits_can_end_at_a_local_time_of_day()
    {
        var karachi = SendTiming.Zone("Asia/Karachi");
        var now = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc); // 17:00 local
        var until = AutomationRules.WaitUntil(new StepConfig { Days = 1, UntilTime = "09:00" }, now, karachi);
        Assert.Equal(new DateTime(2026, 9, 25, 4, 0, 0, DateTimeKind.Utc), until); // next day 17:00 → following 09:00 local
    }
}
