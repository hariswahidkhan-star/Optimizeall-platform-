using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Submissions;

namespace OptimizeAll.UnitTests.Rewards;

/// <summary>Person-level pricing: the engine's handling of a personal rate and the pure precedence resolver.</summary>
public sealed class PersonalRatesTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------- helpers

    private static RewardRuleSet Set(string currency = "USD", decimal baseAmount = 5m, params RewardRule[] extra)
    {
        var set = new RewardRuleSet { Currency = currency, Version = 3 };
        set.Rules.Add(new RewardRule { Type = RewardRuleType.BaseRate, Amount = baseAmount });
        set.Rules.AddRange(extra);
        return set;
    }

    private static RewardContext Ctx(PersonalRateInput? personal = null, SocialPlatform platform = SocialPlatform.Instagram, bool first = false,
        decimal today = 0, decimal total = 0, decimal? budget = null, decimal? quality = null, string country = "PK",
        ParticipantTier tier = ParticipantTier.Standard) => new()
    {
        Platform = platform, CountryCode = country, Tier = tier, PostedAtUtc = T0, IsFirstApprovedPostInCampaign = first,
        EarnedTodayInCampaign = today, EarnedThisWeekInCampaign = today, EarnedInCampaignTotal = total, CampaignBudgetRemaining = budget,
        QualityBonusRequested = quality, PersonalRate = personal,
    };

    private static PersonalRateInput Personal(decimal amount, bool stack = true, decimal? daily = null, decimal? campaignCap = null) =>
        new(amount, Guid.NewGuid(), "Post reward (your rate)", stack, daily, null, campaignCap);

    private static RateCardLine Line(decimal amount, SocialPlatform? platform = null, ContentFormat? format = null, string? country = null) =>
        new() { Amount = amount, Platform = platform, Format = format, CountryCode = country };

    private static RateCard Card(string name, string currency = "USD", RateCardStatus status = RateCardStatus.Active, params RateCardLine[] lines)
    {
        var card = new RateCard { Name = name, Currency = currency, Status = status, CurrentVersion = 1 };
        var v = new RateCardVersion { RateCardId = card.Id, Version = 1, Currency = currency, EffectiveFrom = T0.AddDays(-30), Status = RateCardVersionStatus.Approved };
        v.Lines.AddRange(lines.Length == 0 ? new[] { Line(10m) } : lines);
        card.Versions.Add(v);
        return card;
    }

    private static RateCandidate Candidate(RateSourceLevel level, RateCard card, int priority = 0, RateGroup? group = null,
        DateTime? from = null, DateTime? to = null, Guid? assignmentId = null, string? ineligible = null) =>
        new(level, assignmentId ?? IdGenerator.NewId(), card, card.Versions, group, priority, from, to, ineligible);

    private static RateSubject Subject(SocialPlatform platform = SocialPlatform.Instagram, ContentFormat? format = null, string country = "PK",
        DateTime? at = null) => new(platform, format, country, at ?? T0, at ?? T0);

    // ---------------------------------------------------------------- engine: backward compatibility

    [Fact]
    public void Without_a_personal_rate_the_quote_is_exactly_the_campaign_quote()
    {
        var set = Set(extra: new[]
        {
            new RewardRule { Type = RewardRuleType.RateOverride, Amount = 7m, Platform = SocialPlatform.TikTok },
            new RewardRule { Type = RewardRuleType.FirstPostBonus, Amount = 1.5m },
        });
        foreach (var platform in new[] { SocialPlatform.Instagram, SocialPlatform.TikTok })
        {
            var quote = RewardEngine.Quote(set, Ctx(platform: platform, first: true));
            var post = quote.Lines.Single(l => l.Type == EarningType.PostReward);
            Assert.Equal(platform == SocialPlatform.TikTok ? 7m : 5m, post.Amount);
            Assert.False(post.FromPersonalRate);
            Assert.Equal(post.RuleId, quote.PostRate!.CampaignRuleId);
            Assert.False(quote.PostRate.PersonalApplied);
            Assert.Null(quote.PostRate.PersonalAmount);
            Assert.Contains(quote.Lines, l => l.Type == EarningType.FirstPostBonus);
        }
        // The summary of a rule set that doesn't use the new policy fields is unchanged.
        Assert.Equal("v3 USD: base 5.00; 1 override; 1 bonus", RewardEngine.Summarize(set));
    }

    // ---------------------------------------------------------------- engine: personal rate

    [Fact]
    public void A_personal_rate_replaces_the_campaign_post_rate_even_when_lower()
    {
        var personal = Personal(3.25m);
        var quote = RewardEngine.Quote(Set(extra: new RewardRule { Type = RewardRuleType.RateOverride, Amount = 9m, Platform = SocialPlatform.Instagram }),
            Ctx(personal));
        var line = Assert.Single(quote.Lines);
        Assert.Equal(3.25m, line.Amount);
        Assert.True(line.FromPersonalRate);
        Assert.Equal(personal.SourceId, line.RuleId);
        Assert.Equal("Post reward (your rate)", line.Label);
        Assert.Equal(9m, quote.PostRate!.CampaignAmount);
        Assert.True(quote.PostRate.PersonalApplied);
    }

    [Fact]
    public void Campaign_rates_only_ignores_the_personal_rate()
    {
        var set = Set(baseAmount: 5m);
        set.PersonalRatesMode = PersonalRatesMode.CampaignRatesOnly;
        var quote = RewardEngine.Quote(set, Ctx(Personal(40m)));
        Assert.Equal(5m, quote.Total);
        Assert.False(quote.Lines[0].FromPersonalRate);
        Assert.False(quote.PostRate!.PersonalApplied);
        Assert.Equal(40m, quote.PostRate.PersonalAmount);
        Assert.NotNull(quote.PostRate.PersonalIgnoredReason);
        Assert.Contains("campaign rates only", RewardEngine.Summarize(set));
    }

    [Fact]
    public void The_campaign_multiplier_caps_a_personal_rate_relative_to_the_rate_it_replaces()
    {
        var set = Set("USD", 5m, new RewardRule { Type = RewardRuleType.RateOverride, Amount = 6m, Platform = SocialPlatform.TikTok });
        set.PersonalRateMaxMultiplier = 2.5m;
        var instagram = RewardEngine.Quote(set, Ctx(Personal(40m)));
        Assert.Equal(12.5m, instagram.Total);
        Assert.Contains(RewardCaps.PersonalRateLimit, instagram.AppliedCaps);
        Assert.True(instagram.PostRate!.PersonalLimited);

        var tiktok = RewardEngine.Quote(set, Ctx(Personal(40m), platform: SocialPlatform.TikTok));
        Assert.Equal(15m, tiktok.Total);

        var below = RewardEngine.Quote(set, Ctx(Personal(10m)));
        Assert.Equal(10m, below.Total);
        Assert.Empty(below.AppliedCaps);
        Assert.Contains("personal rates ≤ 2.5× campaign rate", RewardEngine.Summarize(set));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void An_invalid_multiplier_is_rejected(double multiplier)
    {
        var set = Set();
        set.PersonalRateMaxMultiplier = (decimal)multiplier;
        Assert.Contains(RewardEngine.Validate(set), e => e.Contains("multiplier"));
    }

    [Fact]
    public void Campaign_bonuses_stack_on_a_personal_rate_unless_the_card_says_otherwise()
    {
        var window = new RewardRule { Type = RewardRuleType.TimeLimitedBonus, Amount = 2m, ValidFrom = T0.AddDays(-1), ValidTo = T0.AddDays(1) };
        var set = Set(extra: new[]
        {
            window, new RewardRule { Type = RewardRuleType.FirstPostBonus, Amount = 1m },
            new RewardRule { Type = RewardRuleType.QualityBonus, Amount = 4m },
        });
        var stacked = RewardEngine.Quote(set, Ctx(Personal(10m), first: true, quality: 3m));
        Assert.Equal(new[] { EarningType.PostReward, EarningType.TimeLimitedBonus, EarningType.FirstPostBonus, EarningType.QualityBonus },
            stacked.Lines.Select(l => l.Type));
        Assert.Equal(16m, stacked.Total);

        var alone = RewardEngine.Quote(set, Ctx(Personal(10m, stack: false), first: true, quality: 3m));
        // The reviewer's discretionary quality bonus is still allowed.
        Assert.Equal(new[] { EarningType.PostReward, EarningType.QualityBonus }, alone.Lines.Select(l => l.Type));
        Assert.Equal(13m, alone.Total);
    }

    [Fact]
    public void Card_caps_and_campaign_caps_and_budget_all_apply_to_a_personal_rate()
    {
        var set = Set();
        set.DailyCapPerParticipant = 100m;
        var card = RewardEngine.Quote(set, Ctx(Personal(30m, daily: 20m), today: 12m));
        Assert.Equal(8m, card.Total);
        Assert.Equal(new[] { RewardCaps.CardDaily }, card.AppliedCaps);

        var campaignCap = RewardEngine.Quote(set, Ctx(Personal(30m, campaignCap: 500m), today: 90m, total: 90m));
        Assert.Equal(10m, campaignCap.Total);
        Assert.Contains(RewardCaps.Daily, campaignCap.AppliedCaps);

        var budget = RewardEngine.Quote(set, Ctx(Personal(30m), budget: 12.345m));
        Assert.Equal(12.34m, budget.Total);
        Assert.Contains(RewardCaps.Budget, budget.AppliedCaps);

        var exhausted = RewardEngine.Quote(set, Ctx(Personal(1_000m), budget: 0m));
        Assert.Empty(exhausted.Lines);
        Assert.Equal(0m, exhausted.Total);
    }

    [Theory]
    [InlineData("JPY", 1234.6, 1235)]
    [InlineData("KWD", 12.3456, 12.346)]
    [InlineData("USD", 7.125, 7.13)]
    public void Personal_amounts_are_rounded_to_the_currency_minor_unit(string currency, double amount, double expected)
    {
        var quote = RewardEngine.Quote(Set(currency, 1m), Ctx(Personal((decimal)amount)));
        Assert.Equal((decimal)expected, quote.Total);
    }

    // ---------------------------------------------------------------- resolver: precedence

    public static IEnumerable<object[]> Levels() =>
        Enum.GetValues<RateSourceLevel>().Where(l => l != RateSourceLevel.CampaignRules).Select(l => new object[] { l });

    [Theory]
    [MemberData(nameof(Levels))]
    public void The_highest_precedence_level_wins_regardless_of_amount_or_priority(RateSourceLevel expected)
    {
        // One candidate per level; every level below `expected` removed. Lower levels get huge amounts and priorities.
        var candidates = Enum.GetValues<RateSourceLevel>().Where(l => l != RateSourceLevel.CampaignRules && l >= expected)
            .Select(l => Candidate(l, Card(l.ToString(), lines: Line(l == expected ? 1m : 1000m, SocialPlatform.Instagram)),
                priority: l == expected ? -100 : 1000))
            .ToList();
        var result = PersonalRateResolver.Resolve(candidates, Subject());
        Assert.Equal(expected, result.Winner!.Candidate.Level);
        Assert.Equal(1m, result.Winner.Line!.Amount);
        Assert.All(result.Trace.Where(t => t.Outcome == RateOutcome.Outranked), t => Assert.Contains("ranks below", t.Reason));
    }

    [Fact]
    public void Precedence_order_matches_the_documented_table()
    {
        Assert.Equal(new[]
        {
            RateSourceLevel.CampaignPersonalCustom, RateSourceLevel.CampaignPersonalCard, RateSourceLevel.CampaignGroup,
            RateSourceLevel.GlobalPersonalCustom, RateSourceLevel.GlobalPersonalCard, RateSourceLevel.GlobalGroup,
            RateSourceLevel.CampaignSegment, RateSourceLevel.GlobalSegment, RateSourceLevel.CampaignRules,
        }, Enum.GetValues<RateSourceLevel>().Where(l => l <= RateSourceLevel.CampaignRules).OrderBy(l => (int)l));
        // Discount-code payout sources rank after every post-pricing level (they never compete with post rates).
        Assert.All(new[] { RateSourceLevel.CodePersonOverride, RateSourceLevel.CodeGroupOverride, RateSourceLevel.CodeProgramTier,
            RateSourceLevel.CodeProgramRules }, l => Assert.True(l > RateSourceLevel.CampaignRules));
    }

    [Theory]
    [InlineData(RateAssignmentTarget.Person, true, true, null, RateSourceLevel.CampaignPersonalCustom)]
    [InlineData(RateAssignmentTarget.Person, false, true, null, RateSourceLevel.CampaignPersonalCard)]
    [InlineData(RateAssignmentTarget.Group, false, true, RateGroupMembershipMode.Manual, RateSourceLevel.CampaignGroup)]
    [InlineData(RateAssignmentTarget.Person, true, false, null, RateSourceLevel.GlobalPersonalCustom)]
    [InlineData(RateAssignmentTarget.Person, false, false, null, RateSourceLevel.GlobalPersonalCard)]
    [InlineData(RateAssignmentTarget.Group, false, false, RateGroupMembershipMode.Manual, RateSourceLevel.GlobalGroup)]
    [InlineData(RateAssignmentTarget.Group, false, true, RateGroupMembershipMode.Automatic, RateSourceLevel.CampaignSegment)]
    [InlineData(RateAssignmentTarget.Group, false, false, RateGroupMembershipMode.Automatic, RateSourceLevel.GlobalSegment)]
    public void Assignments_map_to_levels(RateAssignmentTarget target, bool custom, bool scoped, RateGroupMembershipMode? mode, RateSourceLevel level) =>
        Assert.Equal(level, RateAssignment.LevelFor(target, custom, scoped, mode));

    // ---------------------------------------------------------------- resolver: within a level

    [Fact]
    public void Within_a_level_the_more_specific_line_beats_a_higher_priority_group()
    {
        var vip = Candidate(RateSourceLevel.GlobalGroup, Card("VIP", lines: Line(50m)), priority: 100, group: new RateGroup { Name = "VIP", Priority = 100 });
        var ig = Candidate(RateSourceLevel.GlobalGroup, Card("Instagram fans", lines: Line(20m, SocialPlatform.Instagram)), priority: 10,
            group: new RateGroup { Name = "Instagram fans", Priority = 10 });
        var result = PersonalRateResolver.Resolve(new[] { vip, ig }, Subject());
        Assert.Equal("Instagram fans", result.Winner!.Candidate.Card.Name);
        Assert.Contains("more specific", result.Winner.Reason);
        var lost = result.Trace.Single(t => t.Outcome == RateOutcome.Outranked);
        Assert.Contains("lost to", lost.Reason);

        // On TikTok only the VIP card has a line.
        Assert.Equal("VIP", PersonalRateResolver.Resolve(new[] { vip, ig }, Subject(SocialPlatform.TikTok)).Winner!.Candidate.Card.Name);
    }

    [Fact]
    public void Group_priority_breaks_a_specificity_tie_then_the_older_assignment_wins()
    {
        var older = IdGenerator.NewId(T0.AddDays(-5));
        var newer = IdGenerator.NewId(T0.AddDays(-1));
        var a = Candidate(RateSourceLevel.GlobalGroup, Card("Micro", lines: Line(10m)), priority: 30, assignmentId: newer);
        var b = Candidate(RateSourceLevel.GlobalGroup, Card("Nano", lines: Line(8m)), priority: 20, assignmentId: older);
        Assert.Equal("Micro", PersonalRateResolver.Resolve(new[] { b, a }, Subject()).Winner!.Candidate.Card.Name);

        var c = Candidate(RateSourceLevel.GlobalGroup, Card("Micro", lines: Line(10m)), priority: 20, assignmentId: newer);
        var result = PersonalRateResolver.Resolve(new[] { c, b }, Subject());
        Assert.Equal("Nano", result.Winner!.Candidate.Card.Name);
        Assert.Contains("older assignment", result.Winner.Reason);
        // Deterministic: the input order doesn't matter.
        Assert.Equal("Nano", PersonalRateResolver.Resolve(new[] { b, c }, Subject()).Winner!.Candidate.Card.Name);
    }

    [Fact]
    public void The_most_specific_line_of_a_card_is_used()
    {
        var card = Card("Macro", lines: new[]
        {
            Line(15m), Line(18m, SocialPlatform.Instagram), Line(22m, SocialPlatform.Instagram, ContentFormat.ShortVideo),
            Line(25m, SocialPlatform.Instagram, ContentFormat.ShortVideo, "AE"),
        });
        var candidates = new[] { Candidate(RateSourceLevel.GlobalGroup, card) };
        Assert.Equal(22m, PersonalRateResolver.Resolve(candidates, Subject(format: ContentFormat.ShortVideo)).Winner!.Line!.Amount);
        Assert.Equal(25m, PersonalRateResolver.Resolve(candidates, Subject(format: ContentFormat.ShortVideo, country: "ae")).Winner!.Line!.Amount);
        // Unknown format (older submissions) only matches format-agnostic lines.
        Assert.Equal(18m, PersonalRateResolver.Resolve(candidates, Subject()).Winner!.Line!.Amount);
        Assert.Equal(15m, PersonalRateResolver.Resolve(candidates, Subject(SocialPlatform.X)).Winner!.Line!.Amount);
    }

    [Fact]
    public void A_higher_level_without_a_matching_line_falls_through_to_the_next_level()
    {
        var personal = Candidate(RateSourceLevel.GlobalPersonalCustom, Card("Deal", lines: Line(40m, SocialPlatform.YouTube)));
        var group = Candidate(RateSourceLevel.GlobalGroup, Card("Micro", lines: Line(10m)));
        var result = PersonalRateResolver.Resolve(new[] { personal, group }, Subject());
        Assert.Equal(RateSourceLevel.GlobalGroup, result.Winner!.Candidate.Level);
        Assert.Contains(result.Trace, t => t.Outcome == RateOutcome.NotApplicable && t.Reason.Contains("no rate for Instagram"));
        Assert.Contains("Won:", result.Explain());
    }

    // ---------------------------------------------------------------- resolver: windows, versions, lifecycle

    [Fact]
    public void Assignment_windows_are_inclusive_start_and_exclusive_end()
    {
        var card = Card("Deal");
        var c = Candidate(RateSourceLevel.GlobalPersonalCard, card, from: T0, to: T0.AddDays(1));
        Assert.NotNull(PersonalRateResolver.Resolve(new[] { c }, Subject(at: T0)).Winner);
        Assert.NotNull(PersonalRateResolver.Resolve(new[] { c }, Subject(at: T0.AddDays(1).AddTicks(-1))).Winner);
        var ended = PersonalRateResolver.Resolve(new[] { c }, Subject(at: T0.AddDays(1)));
        Assert.Null(ended.Winner);
        Assert.Contains("ended", ended.Trace[0].Reason);
        var early = PersonalRateResolver.Resolve(new[] { c }, Subject(at: T0.AddTicks(-1)));
        Assert.Contains("starts", early.Trace[0].Reason);
    }

    [Fact]
    public void Only_the_highest_approved_version_in_force_prices()
    {
        var card = Card("Macro", lines: Line(10m));
        void Add(int version, decimal amount, DateTime from, RateCardVersionStatus status)
        {
            var v = new RateCardVersion { RateCardId = card.Id, Version = version, Currency = "USD", EffectiveFrom = from, Status = status };
            v.Lines.Add(Line(amount));
            card.Versions.Add(v);
        }
        Add(2, 12m, T0.AddDays(-1), RateCardVersionStatus.Approved);
        Add(3, 99m, T0.AddDays(-1), RateCardVersionStatus.PendingApproval);
        Add(4, 98m, T0.AddDays(-1), RateCardVersionStatus.Rejected);
        Add(5, 14m, T0.AddDays(2), RateCardVersionStatus.Approved);
        var candidates = new[] { Candidate(RateSourceLevel.GlobalGroup, card) };
        var now = PersonalRateResolver.Resolve(candidates, Subject());
        Assert.Equal(2, now.Winner!.Version!.Version);
        Assert.Equal(12m, now.Winner.Line!.Amount);
        var later = PersonalRateResolver.Resolve(candidates, new RateSubject(SocialPlatform.Instagram, null, "PK", T0, T0.AddDays(3)));
        Assert.Equal(5, later.Winner!.Version!.Version);
        var before = PersonalRateResolver.Resolve(candidates, new RateSubject(SocialPlatform.Instagram, null, "PK", T0, T0.AddDays(-60)));
        Assert.Null(before.Winner);
        Assert.Contains("no approved version", before.Trace[0].Reason);
    }

    [Theory]
    [InlineData(RateCardStatus.Archived, "archived")]
    [InlineData(RateCardStatus.Draft, "draft")]
    public void Archived_and_draft_cards_never_price(RateCardStatus status, string reason)
    {
        var result = PersonalRateResolver.Resolve(new[] { Candidate(RateSourceLevel.GlobalPersonalCard, Card("X", status: status)) }, Subject());
        Assert.Null(result.Winner);
        Assert.Contains(reason, result.Trace[0].Reason);
    }

    [Fact]
    public void An_ineligible_candidate_is_skipped_with_its_reason()
    {
        var result = PersonalRateResolver.Resolve(new[]
        {
            Candidate(RateSourceLevel.GlobalSegment, Card("Auto"), ineligible: "not in automatic group 'Macro': needs 100,000 followers"),
        }, Subject());
        Assert.Null(result.Winner);
        Assert.Equal(RateOutcome.NotApplicable, result.Trace[0].Outcome);
        Assert.Contains("100,000", result.Explain());
    }

    // ---------------------------------------------------------------- automatic groups

    [Fact]
    public void Automatic_group_rules_check_tier_and_follower_bounds()
    {
        var g = new RateGroup
        {
            MembershipMode = RateGroupMembershipMode.Automatic, AutoTiers = new List<ParticipantTier> { ParticipantTier.Gold, ParticipantTier.Platinum },
            AutoMinFollowers = 10_000, AutoMaxFollowers = 50_000,
        };
        Assert.True(RateCardRules.AutoRuleMatches(g, ParticipantTier.Gold, 10_000, out _));
        Assert.True(RateCardRules.AutoRuleMatches(g, ParticipantTier.Platinum, 49_999, out _));
        Assert.False(RateCardRules.AutoRuleMatches(g, ParticipantTier.Gold, 50_000, out var tooMany));
        Assert.Contains("fewer than 50,000", tooMany);
        Assert.False(RateCardRules.AutoRuleMatches(g, ParticipantTier.Gold, 9_999, out var tooFew));
        Assert.Contains("at least 10,000", tooFew);
        Assert.False(RateCardRules.AutoRuleMatches(g, ParticipantTier.Silver, 20_000, out var tier));
        Assert.Contains("tier", tier);
    }

    // ---------------------------------------------------------------- validation & four-eyes comparison

    [Fact]
    public void Card_versions_are_validated()
    {
        var v = new RateCardVersion { Currency = "XXX", DailyCapPerParticipant = 0m };
        v.Lines.AddRange(new[] { Line(-1m), Line(5m, SocialPlatform.TikTok), Line(6m, SocialPlatform.TikTok), Line(1m, country: "PAK") });
        var errors = RateCardRules.Validate(v);
        Assert.Contains(errors, e => e.Contains("Currency"));
        Assert.Contains(errors, e => e.Contains("Daily cap"));
        Assert.Contains(errors, e => e.Contains("negative"));
        Assert.Contains(errors, e => e.Contains("same platform, format and country"));
        Assert.Contains(errors, e => e.Contains("two-letter"));
        Assert.Contains(RateCardRules.Validate(new RateCardVersion { Currency = "USD" }), e => e.Contains("at least one rate"));
        var ex = Assert.Throws<DomainException>(() => RateCardRules.EnsureValid(v));
        Assert.Equal("rate_card.invalid", ex.Code);
    }

    [Fact]
    public void The_largest_increase_is_measured_line_by_line()
    {
        RateCardVersion V(string currency, params RateCardLine[] lines)
        {
            var v = new RateCardVersion { Currency = currency };
            v.Lines.AddRange(lines);
            return v;
        }
        var previous = V("USD", Line(10m), Line(20m, SocialPlatform.Instagram));
        Assert.Null(RateCardRules.MaxIncreasePercent(null, previous));
        Assert.Null(RateCardRules.MaxIncreasePercent(previous, V("USD", Line(9m), Line(20m, SocialPlatform.Instagram))));
        Assert.Equal(50m, RateCardRules.MaxIncreasePercent(previous, V("USD", Line(15m), Line(21m, SocialPlatform.Instagram))));
        // A new, more specific line is compared with the line that priced those posts before.
        Assert.Equal(25m, RateCardRules.MaxIncreasePercent(previous, V("USD", Line(10m), Line(20m, SocialPlatform.Instagram),
            Line(25m, SocialPlatform.Instagram, ContentFormat.ShortVideo))));
        Assert.Equal(decimal.MaxValue, RateCardRules.MaxIncreasePercent(previous, V("EUR", Line(1m))));
    }

    // ---------------------------------------------------------------- content formats

    [Theory]
    [InlineData(SocialPlatform.Instagram, "https://www.instagram.com/reel/Cabc123/", ContentFormat.ShortVideo)]
    [InlineData(SocialPlatform.Instagram, "https://www.instagram.com/p/Cabc123/", null)]
    [InlineData(SocialPlatform.YouTube, "https://www.youtube.com/shorts/dQw4w9WgXcQ", ContentFormat.ShortVideo)]
    [InlineData(SocialPlatform.YouTube, "https://www.youtube.com/watch?v=dQw4w9WgXcQ", ContentFormat.LongVideo)]
    [InlineData(SocialPlatform.YouTube, "https://youtu.be/dQw4w9WgXcQ", ContentFormat.LongVideo)]
    [InlineData(SocialPlatform.TikTok, "https://www.tiktok.com/@a/video/123", ContentFormat.ShortVideo)]
    [InlineData(SocialPlatform.X, "https://x.com/a/status/1", null)]
    [InlineData(SocialPlatform.X, "not a url", null)]
    public void Formats_are_inferred_only_when_the_url_says_so(SocialPlatform platform, string url, ContentFormat? expected) =>
        Assert.Equal(expected, ContentFormats.Infer(platform, url));

    [Fact]
    public void A_declared_format_that_contradicts_the_url_is_refused()
    {
        Assert.Equal(ContentFormat.Carousel, ContentFormats.Resolve(SocialPlatform.Instagram, "https://www.instagram.com/p/Cabc/", ContentFormat.Carousel));
        Assert.Equal(ContentFormat.ShortVideo, ContentFormats.Resolve(SocialPlatform.YouTube, "https://www.youtube.com/shorts/abc", null));
        var ex = Assert.Throws<DomainException>(() =>
            ContentFormats.Resolve(SocialPlatform.YouTube, "https://www.youtube.com/shorts/abc", ContentFormat.LongVideo));
        Assert.Equal("submission.format_mismatch", ex.Code);
    }
}
