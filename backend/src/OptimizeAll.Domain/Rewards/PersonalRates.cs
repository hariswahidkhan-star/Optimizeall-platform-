using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Rewards;

// Person-level pricing: reusable rate cards, rate groups and assignments. See docs/REWARD_ENGINE.md
// ("Person-level rates"). Everything here is data plus pure functions; the API resolves candidates from the
// database and passes the winning rate to RewardEngine through RewardContext.PersonalRate.

public enum RateCardStatus
{
    /// <summary>Being prepared; cannot be assigned and never prices anything.</summary>
    Draft,
    /// <summary>Assignable; its approved versions price posts.</summary>
    Active,
    /// <summary>Retired: no new assignments and it no longer prices new submissions (existing snapshots are kept).</summary>
    Archived,
}

public enum RateCardKind
{
    /// <summary>A named, reusable card listed under Rate cards.</summary>
    Standard,
    /// <summary>A negotiated, per-person rate (hidden from the card list; owned by one participant).</summary>
    Custom,
}

public enum RateCardVersionStatus
{
    /// <summary>In force from <see cref="RateCardVersion.EffectiveFrom"/>.</summary>
    Approved,
    /// <summary>A rate increase above the four-eyes threshold, waiting for a second person.</summary>
    PendingApproval,
    Rejected,
}

public enum RateGroupMembershipMode
{
    /// <summary>Members are added and removed explicitly (bulk, CSV).</summary>
    Manual,
    /// <summary>Everyone matching the group's tier / verified-follower rule is a member (evaluated at pricing time).</summary>
    Automatic,
}

public enum RateGroupMemberAction
{
    Added,
    Removed,
}

public enum RateAssignmentTarget
{
    Person,
    Group,
}

/// <summary>Campaign policy (on the reward rule set version) for person-level rates.</summary>
public enum PersonalRatesMode
{
    /// <summary>Personal, group and segment rates replace the campaign's post rate when one applies.</summary>
    Allowed,
    /// <summary>Everyone is priced with the campaign rules; personal/group rates are ignored.</summary>
    CampaignRatesOnly,
}

/// <summary>
/// Where a post rate came from, in precedence order (lower value wins). Stored on submission snapshots and ledger lines.
/// </summary>
public enum RateSourceLevel
{
    /// <summary>Negotiated custom rate for this person, scoped to this campaign.</summary>
    CampaignPersonalCustom = 1,
    /// <summary>Rate card assigned to this person for this campaign.</summary>
    CampaignPersonalCard = 2,
    /// <summary>Rate card assigned to a (manual) rate group for this campaign.</summary>
    CampaignGroup = 3,
    /// <summary>Negotiated custom rate for this person in every campaign.</summary>
    GlobalPersonalCustom = 4,
    /// <summary>Rate card assigned to this person in every campaign.</summary>
    GlobalPersonalCard = 5,
    /// <summary>Rate card assigned to a (manual) rate group in every campaign.</summary>
    GlobalGroup = 6,
    /// <summary>Rate card assigned to an automatic (tier / follower rule) group for this campaign.</summary>
    CampaignSegment = 7,
    /// <summary>Rate card assigned to an automatic group in every campaign.</summary>
    GlobalSegment = 8,
    /// <summary>No person-level rate applied: the campaign's BaseRate / RateOverride rules.</summary>
    CampaignRules = 9,
}

/// <summary>A reusable, named set of per-post rates in one currency. Rates live in immutable versions.</summary>
public class RateCard : AuditedEntity, IConcurrencyStamped
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public RateCardKind Kind { get; set; } = RateCardKind.Standard;
    public RateCardStatus Status { get; set; } = RateCardStatus.Draft;

    /// <summary>For <see cref="RateCardKind.Custom"/> cards: the participant the negotiated rate belongs to.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Currency of the latest approved version (for lists and filters).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Highest approved version number (0 while the first version awaits approval).</summary>
    public int CurrentVersion { get; set; }

    public Guid CreatedByUserId { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public Guid? ArchivedByUserId { get; set; }
    public string? ArchiveReason { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<RateCardVersion> Versions { get; set; } = new();
}

/// <summary>An immutable version of a card's rates, caps and currency, in force from <see cref="EffectiveFrom"/>.</summary>
public class RateCardVersion : Entity
{
    public Guid RateCardId { get; set; }
    public int Version { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional per-participant caps (in <see cref="Currency"/>) while this card prices the person: evaluated like the
    /// campaign caps (against all of the participant's earnings in the campaign) and applied in addition to them.
    /// </summary>
    public decimal? DailyCapPerParticipant { get; set; }
    public decimal? WeeklyCapPerParticipant { get; set; }
    public decimal? CampaignCapPerParticipant { get; set; }

    /// <summary>When false, the campaign's first-post and time-limited bonuses don't stack on this card's rate.</summary>
    public bool StackCampaignBonuses { get; set; } = true;

    public DateTime EffectiveFrom { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string ChangeReason { get; set; } = string.Empty;

    public RateCardVersionStatus Status { get; set; } = RateCardVersionStatus.Approved;
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    /// <summary>Largest per-line increase against the previous version, in percent (null = none / not comparable).</summary>
    public decimal? MaxIncreasePercent { get; set; }

    public List<RateCardLine> Lines { get; set; } = new();
}

/// <summary>One rate: a flat amount per approved post, with optional platform / format / country conditions.</summary>
public class RateCardLine : Entity
{
    public Guid VersionId { get; set; }
    public SocialPlatform? Platform { get; set; }
    public ContentFormat? Format { get; set; }
    public string? CountryCode { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Participant-facing label of the post reward (defaults to "Post reward").</summary>
    public string? Label { get; set; }
}

/// <summary>A named set of people who share rates (e.g. "Macro influencers").</summary>
public class RateGroup : AuditedEntity, IConcurrencyStamped
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Decides between groups when a person belongs to several (higher wins, after line specificity).</summary>
    public int Priority { get; set; }

    public RateGroupMembershipMode MembershipMode { get; set; } = RateGroupMembershipMode.Manual;

    /// <summary>Automatic groups: participant tiers that qualify (empty = any tier).</summary>
    public List<ParticipantTier> AutoTiers { get; set; } = new();

    /// <summary>Automatic groups: follower bounds (inclusive min, exclusive max) on the post's platform.</summary>
    public int? AutoMinFollowers { get; set; }
    public int? AutoMaxFollowers { get; set; }

    /// <summary>Automatic groups: only count followers of verified social accounts.</summary>
    public bool AutoRequireVerified { get; set; } = true;

    public Guid CreatedByUserId { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public Guid? ArchivedByUserId { get; set; }
    public string? ArchiveReason { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Current membership of a manual group (unique per group and person).</summary>
public class RateGroupMember : Entity
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public DateTime AddedAt { get; set; }
    public Guid? AddedByUserId { get; set; }
    public string? Note { get; set; }
}

/// <summary>Append-only history of group membership changes.</summary>
public class RateGroupMemberEvent : Entity
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public RateGroupMemberAction Action { get; set; }
    public DateTime At { get; set; }
    public Guid? ActorUserId { get; set; }

    /// <summary>"manual", "bulk", "csv", "group_archived".</summary>
    public string Source { get; set; } = "manual";
    public string? Reason { get; set; }
}

/// <summary>Applies a rate card to one person or a group, globally or for one campaign, optionally within a window.</summary>
public class RateAssignment : AuditedEntity, IConcurrencyStamped
{
    public Guid RateCardId { get; set; }
    public RateAssignmentTarget Target { get; set; }
    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }

    /// <summary>Null = every campaign.</summary>
    public Guid? CampaignId { get; set; }

    /// <summary>True when the card is a negotiated custom rate (precedence above personal card assignments).</summary>
    public bool IsCustom { get; set; }

    /// <summary>Window evaluated at the post time (UTC, inclusive start, exclusive end); open sides are unbounded.</summary>
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    public string Note { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime? EndedAt { get; set; }
    public Guid? EndedByUserId { get; set; }
    public string? EndReason { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    /// <summary>End of the effective window: the earlier of ValidTo and EndedAt.</summary>
    public DateTime? EffectiveTo => EndedAt is { } ended && (ValidTo is null || ended < ValidTo) ? ended : ValidTo;

    public static RateSourceLevel LevelFor(RateAssignmentTarget target, bool isCustom, bool campaignScoped, RateGroupMembershipMode? groupMode) =>
        target == RateAssignmentTarget.Person
            ? (isCustom
                ? campaignScoped ? RateSourceLevel.CampaignPersonalCustom : RateSourceLevel.GlobalPersonalCustom
                : campaignScoped ? RateSourceLevel.CampaignPersonalCard : RateSourceLevel.GlobalPersonalCard)
            : groupMode == RateGroupMembershipMode.Automatic
                ? campaignScoped ? RateSourceLevel.CampaignSegment : RateSourceLevel.GlobalSegment
                : campaignScoped ? RateSourceLevel.CampaignGroup : RateSourceLevel.GlobalGroup;
}

/// <summary>
/// The person-level rate locked for a submission when it was created (1:1 with the submission; absent when the campaign
/// rules priced it). Approval prices from this snapshot, so later card, group or assignment changes never re-price it.
/// </summary>
public class SubmissionRate : Entity
{
    public Guid SubmissionId { get; set; }
    public RateSourceLevel Level { get; set; }
    public Guid RateAssignmentId { get; set; }
    public Guid RateCardId { get; set; }
    public Guid RateCardVersionId { get; set; }
    public int RateCardVersion { get; set; }
    public Guid RateCardLineId { get; set; }
    public Guid? RateGroupId { get; set; }
    public string CardName { get; set; } = string.Empty;
    public string? GroupName { get; set; }

    /// <summary>Amount and currency as written on the card line.</summary>
    public decimal CardAmount { get; set; }
    public string CardCurrency { get; set; } = "USD";

    /// <summary>Card currency → campaign reward currency at resolution time (1 when equal).</summary>
    public decimal ExchangeRate { get; set; } = 1m;
    public Guid? ExchangeRateId { get; set; }

    /// <summary>The post rate in the campaign's reward currency (rounded to its minor unit).</summary>
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Card caps converted to the campaign currency.</summary>
    public decimal? DailyCap { get; set; }
    public decimal? WeeklyCap { get; set; }
    public decimal? CampaignCap { get; set; }
    public bool StackCampaignBonuses { get; set; } = true;
    public string? LineLabel { get; set; }
    public DateTime? AssignmentValidTo { get; set; }
    public DateTime ResolvedAt { get; set; }

    /// <summary>Human-readable summary of the resolution (winner and why), kept for audit.</summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>"Group 'Macro influencers' · card 'Macro 2026' v2" — staff-facing label of the source.</summary>
    public string SourceLabel => RateSources.Label(Level, CardName, RateCardVersion, GroupName);
}

public static class RateSources
{
    public static bool IsPersonal(RateSourceLevel level) => level is RateSourceLevel.CampaignPersonalCustom or
        RateSourceLevel.CampaignPersonalCard or RateSourceLevel.GlobalPersonalCustom or RateSourceLevel.GlobalPersonalCard;

    public static bool IsCampaignScoped(RateSourceLevel level) => level is RateSourceLevel.CampaignPersonalCustom or
        RateSourceLevel.CampaignPersonalCard or RateSourceLevel.CampaignGroup or RateSourceLevel.CampaignSegment;

    public static string Describe(RateSourceLevel level) => level switch
    {
        RateSourceLevel.CampaignPersonalCustom => "Custom rate for this campaign",
        RateSourceLevel.CampaignPersonalCard => "Personal rate card for this campaign",
        RateSourceLevel.CampaignGroup => "Group rate for this campaign",
        RateSourceLevel.GlobalPersonalCustom => "Custom rate (all campaigns)",
        RateSourceLevel.GlobalPersonalCard => "Personal rate card (all campaigns)",
        RateSourceLevel.GlobalGroup => "Group rate (all campaigns)",
        RateSourceLevel.CampaignSegment => "Automatic segment rate for this campaign",
        RateSourceLevel.GlobalSegment => "Automatic segment rate (all campaigns)",
        _ => "Campaign rate",
    };

    public static string Label(RateSourceLevel level, string cardName, int version, string? groupName)
    {
        var text = level switch
        {
            RateSourceLevel.CampaignPersonalCustom or RateSourceLevel.GlobalPersonalCustom => $"Custom rate v{version}",
            RateSourceLevel.CampaignPersonalCard or RateSourceLevel.GlobalPersonalCard => $"Personal · card '{cardName}' v{version}",
            RateSourceLevel.CampaignRules => "Campaign rules",
            _ => $"Group '{groupName}' · card '{cardName}' v{version}",
        };
        if (IsCampaignScoped(level)) text += " (this campaign)";
        return text.Length > 200 ? text[..200] : text;
    }
}

/// <summary>Validation and comparison rules for rate card versions and automatic groups (pure).</summary>
public static class RateCardRules
{
    public const int MaxLines = 100;
    public const decimal MaxAmount = 1_000_000m;

    public static IReadOnlyList<string> Validate(RateCardVersion version)
    {
        var errors = new List<string>();
        if (!Money.IsSupported(version.Currency)) errors.Add($"Currency '{version.Currency}' is not supported.");
        foreach (var (cap, name) in new[] { (version.DailyCapPerParticipant, "Daily cap"), (version.WeeklyCapPerParticipant, "Weekly cap"), (version.CampaignCapPerParticipant, "Campaign cap") })
            if (cap is <= 0m) errors.Add($"{name} must be greater than zero when set.");
        if (version.Lines.Count == 0) errors.Add("Add at least one rate.");
        if (version.Lines.Count > MaxLines) errors.Add($"A rate card can have at most {MaxLines} rates.");
        var seen = new HashSet<(SocialPlatform?, ContentFormat?, string?)>();
        foreach (var (line, index) in version.Lines.Select((l, i) => (l, i + 1)))
        {
            var name = $"Rate {index}";
            if (line.Amount < 0) errors.Add($"{name}: the amount must not be negative.");
            if (line.Amount > MaxAmount) errors.Add($"{name}: the amount must be at most {MaxAmount:N0}.");
            if (line.CountryCode is not null && (line.CountryCode.Length != 2 || !line.CountryCode.All(char.IsAsciiLetter)))
                errors.Add($"{name}: the country code must be a two-letter ISO code.");
            if (line.Label is { Length: > 150 }) errors.Add($"{name}: the label must be at most 150 characters.");
            if (!seen.Add((line.Platform, line.Format, line.CountryCode?.ToUpperInvariant())))
                errors.Add($"{name}: another rate already has the same platform, format and country.");
        }
        return errors;
    }

    public static void EnsureValid(RateCardVersion version)
    {
        var errors = Validate(version);
        if (errors.Count > 0)
            throw new DomainException("rate_card.invalid", "The rate card is not valid: " + string.Join(" ", errors),
                errors: new Dictionary<string, string[]> { ["lines"] = errors.ToArray() });
    }

    public static int Specificity(RateCardLine line) =>
        (line.Platform.HasValue ? 1 : 0) + (line.Format.HasValue ? 1 : 0) + (line.CountryCode is not null ? 1 : 0);

    public static bool Matches(RateCardLine line, SocialPlatform platform, ContentFormat? format, string countryCode) =>
        (!line.Platform.HasValue || line.Platform == platform) &&
        (!line.Format.HasValue || line.Format == format) &&
        (line.CountryCode is null || string.Equals(line.CountryCode, countryCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>The most specific line matching the post (ties impossible: keys are unique per version).</summary>
    public static RateCardLine? SelectLine(RateCardVersion version, SocialPlatform platform, ContentFormat? format, string countryCode) =>
        version.Lines.Where(l => Matches(l, platform, format, countryCode))
            .OrderByDescending(Specificity).ThenBy(l => l.Id).FirstOrDefault();

    /// <summary>Highest approved version effective at <paramref name="atUtc"/>.</summary>
    public static RateCardVersion? VersionInForce(IEnumerable<RateCardVersion> versions, DateTime atUtc) =>
        versions.Where(v => v.Status == RateCardVersionStatus.Approved && v.EffectiveFrom <= atUtc)
            .OrderByDescending(v => v.Version).FirstOrDefault();

    /// <summary>
    /// Largest percentage increase of any rate in <paramref name="next"/> over the rate the same post got in
    /// <paramref name="previous"/> (same key, else the most specific previous line covering it). Null when nothing
    /// increased or there is nothing to compare; <see cref="decimal.MaxValue"/> when the currency changed.
    /// </summary>
    public static decimal? MaxIncreasePercent(RateCardVersion? previous, RateCardVersion next)
    {
        if (previous is null) return null;
        if (!string.Equals(previous.Currency, next.Currency, StringComparison.OrdinalIgnoreCase)) return decimal.MaxValue;
        decimal? max = null;
        foreach (var line in next.Lines)
        {
            var before = previous.Lines.FirstOrDefault(p => p.Platform == line.Platform && p.Format == line.Format &&
                                                             string.Equals(p.CountryCode, line.CountryCode, StringComparison.OrdinalIgnoreCase))
                         ?? previous.Lines
                             .Where(p => (!p.Platform.HasValue || p.Platform == line.Platform) && (!p.Format.HasValue || p.Format == line.Format) &&
                                         (p.CountryCode is null || string.Equals(p.CountryCode, line.CountryCode, StringComparison.OrdinalIgnoreCase)))
                             .OrderByDescending(Specificity).FirstOrDefault();
            if (before is null || line.Amount <= before.Amount) continue;
            var pct = before.Amount == 0m ? decimal.MaxValue : Math.Round((line.Amount - before.Amount) / before.Amount * 100m, 2);
            if (max is null || pct > max) max = pct;
        }
        return max;
    }

    /// <summary>Whether a person qualifies for an automatic group; <paramref name="reason"/> explains a mismatch.</summary>
    public static bool AutoRuleMatches(RateGroup group, ParticipantTier tier, int? followers, out string reason)
    {
        if (group.AutoTiers.Count > 0 && !group.AutoTiers.Contains(tier))
        {
            reason = $"needs tier {string.Join(" or ", group.AutoTiers)} (is {tier})";
            return false;
        }
        var count = followers ?? 0;
        var kind = group.AutoRequireVerified ? "verified followers" : "followers";
        if (group.AutoMinFollowers is { } min && count < min)
        {
            reason = $"needs at least {min:N0} {kind} on this platform (has {count:N0})";
            return false;
        }
        if (group.AutoMaxFollowers is { } max && count >= max)
        {
            reason = $"needs fewer than {max:N0} {kind} on this platform (has {count:N0})";
            return false;
        }
        reason = string.Empty;
        return true;
    }
}

/// <summary>
/// What is being priced: one post by one person. Assignment windows are evaluated at <paramref name="WindowAtUtc"/>
/// (the post time, min(PostedAt, SubmittedAt)); card versions are chosen at <paramref name="VersionAtUtc"/> (submission
/// creation).
/// </summary>
public sealed record RateSubject(
    SocialPlatform Platform, ContentFormat? Format, string CountryCode, DateTime WindowAtUtc, DateTime VersionAtUtc);

/// <summary>
/// One assignment that could price the post (built by the API from the database). <paramref name="IneligibleReason"/>
/// is set when the person does not qualify (e.g. an automatic group's rule); the candidate is then skipped.
/// </summary>
public sealed record RateCandidate(
    RateSourceLevel Level,
    Guid AssignmentId,
    RateCard Card,
    IReadOnlyList<RateCardVersion> Versions,
    RateGroup? Group,
    int Priority,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    string? IneligibleReason = null);

public enum RateOutcome
{
    Won,
    NotApplicable,
    Outranked,
}

public sealed record RateCandidateResult(RateCandidate Candidate, RateOutcome Outcome, string Reason, RateCardVersion? Version, RateCardLine? Line);

public sealed record RateResolution(RateCandidateResult? Winner, IReadOnlyList<RateCandidateResult> Trace)
{
    public string Explain()
    {
        var parts = new List<string>();
        if (Winner is { } w)
            parts.Add($"Won: {RateSources.Label(w.Candidate.Level, w.Candidate.Card.Name, w.Version!.Version, w.Candidate.Group?.Name)} " +
                      $"({w.Line!.Amount} {w.Version.Currency}; {w.Reason})");
        else parts.Add("No person-level rate applies; campaign rules price the post.");
        foreach (var r in Trace.Where(t => t.Outcome != RateOutcome.Won))
            parts.Add($"{r.Outcome}: {RateSources.Label(r.Candidate.Level, r.Candidate.Card.Name, r.Version?.Version ?? r.Candidate.Card.CurrentVersion, r.Candidate.Group?.Name)} — {r.Reason}");
        var text = string.Join(" | ", parts);
        return text.Length > 2000 ? text[..2000] : text;
    }
}

/// <summary>
/// Picks the person-level rate for one post. Pure and deterministic: candidates are ranked by precedence level
/// (<see cref="RateSourceLevel"/>), then line specificity (platform + format + country), then priority (group priority
/// for group levels), then the older assignment (lower time-ordered id). See docs/REWARD_ENGINE.md.
/// </summary>
public static class PersonalRateResolver
{
    public static RateResolution Resolve(IEnumerable<RateCandidate> candidates, RateSubject subject)
    {
        var applicable = new List<(RateCandidate C, RateCardVersion V, RateCardLine L)>();
        var trace = new List<RateCandidateResult>();
        foreach (var c in candidates.OrderBy(c => c.Level).ThenBy(c => c.AssignmentId))
        {
            var reason = NotApplicableReason(c, subject, out var version, out var line);
            if (reason is not null) trace.Add(new RateCandidateResult(c, RateOutcome.NotApplicable, reason, version, line));
            else applicable.Add((c, version!, line!));
        }

        var ranked = applicable
            .OrderBy(a => a.C.Level)
            .ThenByDescending(a => RateCardRules.Specificity(a.L))
            .ThenByDescending(a => a.C.Priority)
            .ThenBy(a => a.C.AssignmentId)
            .ToList();
        if (ranked.Count == 0) return new RateResolution(null, trace);

        var win = ranked[0];
        var tied = ranked.Skip(1).Any(r => r.C.Level == win.C.Level);
        var winner = new RateCandidateResult(win.C, RateOutcome.Won, WinReason(win, ranked, tied), win.V, win.L);
        var results = new List<RateCandidateResult> { winner };
        foreach (var other in ranked.Skip(1))
            results.Add(new RateCandidateResult(other.C, RateOutcome.Outranked, OutrankedReason(other, win), other.V, other.L));
        results.AddRange(trace);
        return new RateResolution(winner, results);
    }

    private static string? NotApplicableReason(RateCandidate c, RateSubject s, out RateCardVersion? version, out RateCardLine? line)
    {
        version = null;
        line = null;
        if (c.IneligibleReason is not null) return c.IneligibleReason;
        if (c.Card.Status == RateCardStatus.Archived) return "the rate card is archived";
        if (c.Card.Status == RateCardStatus.Draft) return "the rate card is still a draft";
        if (c.ValidFrom is { } from && s.WindowAtUtc < from) return $"the assignment starts {from:yyyy-MM-dd HH:mm} UTC";
        if (c.ValidTo is { } to && s.WindowAtUtc >= to) return $"the assignment ended {to:yyyy-MM-dd HH:mm} UTC";
        version = RateCardRules.VersionInForce(c.Versions, s.VersionAtUtc);
        if (version is null) return "no approved version of the card is in force yet";
        line = RateCardRules.SelectLine(version, s.Platform, s.Format, s.CountryCode);
        if (line is null)
            return $"the card has no rate for {s.Platform}{(s.Format is { } f ? " " + f : string.Empty)} in {s.CountryCode}";
        return null;
    }

    private static string WinReason((RateCandidate C, RateCardVersion V, RateCardLine L) win,
        List<(RateCandidate C, RateCardVersion V, RateCardLine L)> ranked, bool tiedLevel)
    {
        var text = $"highest precedence: {RateSources.Describe(win.C.Level)}; line matches on {Conditions(win.L)}";
        if (tiedLevel)
        {
            var runnerUp = ranked.Skip(1).First(r => r.C.Level == win.C.Level);
            text += "; " + TieBreak(win, runnerUp);
        }
        return text;
    }

    private static string OutrankedReason((RateCandidate C, RateCardVersion V, RateCardLine L) other,
        (RateCandidate C, RateCardVersion V, RateCardLine L) win) =>
        other.C.Level != win.C.Level
            ? $"{RateSources.Describe(other.C.Level)} ranks below {RateSources.Describe(win.C.Level)}"
            : TieBreak(win, other).Replace("beats", "lost to");

    private static string TieBreak((RateCandidate C, RateCardVersion V, RateCardLine L) a, (RateCandidate C, RateCardVersion V, RateCardLine L) b)
    {
        int sa = RateCardRules.Specificity(a.L), sb = RateCardRules.Specificity(b.L);
        if (sa != sb) return $"more specific line ({sa} vs {sb} conditions) beats the other rate at this level";
        if (a.C.Priority != b.C.Priority) return $"priority {a.C.Priority} beats priority {b.C.Priority}";
        return "same specificity and priority: the older assignment beats the newer one";
    }

    private static string Conditions(RateCardLine l)
    {
        var parts = new List<string>();
        if (l.Platform is { } p) parts.Add(p.ToString());
        if (l.Format is { } f) parts.Add(f.ToString());
        if (l.CountryCode is { } c) parts.Add(c);
        return parts.Count == 0 ? "any post (default rate)" : string.Join(" + ", parts);
    }
}
