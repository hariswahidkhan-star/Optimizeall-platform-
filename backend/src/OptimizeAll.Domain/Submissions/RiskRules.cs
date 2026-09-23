namespace OptimizeAll.Domain.Submissions;

/// <summary>Facts gathered about a submission for risk scoring. Counts exclude the submission itself.</summary>
public sealed record RiskSignals
{
    /// <summary>Other submissions (any participant) whose screenshot has the same SHA-256.</summary>
    public int SameScreenshotCount { get; init; }

    /// <summary>Other submissions with the same caption hash by the same participant, or by anyone in the same campaign.</summary>
    public int RepeatedContentCount { get; init; }

    public DateTime PostedAtUtc { get; init; }

    /// <summary>When the submission was (first) made, recorded by the server.</summary>
    public DateTime SubmittedAtUtc { get; init; }

    /// <summary>The URL is a short link whose target can't be resolved offline.</summary>
    public bool IsShortLink { get; init; }
    public DateTime CampaignStartsAtUtc { get; init; }
    public DateTime CampaignEndsAtUtc { get; init; }

    public bool AccountVerified { get; init; }
    public bool CampaignRequiresVerifiedAccount { get; init; }

    /// <summary>The participant's other submissions in the 24 hours before this one.</summary>
    public int SubmissionsInLast24Hours { get; init; }
    public int VelocityLimitPer24Hours { get; init; }

    public DateTime ParticipantCreatedAtUtc { get; init; }
    public DateTime NowUtc { get; init; }
}

public sealed record RiskFlag(SubmissionFlagType Type, string Detail, int Weight);

/// <summary>
/// Pure fraud/risk heuristics. Flags are evidence for human review and never reject a submission on their own;
/// the submission's risk score is the sum of the weights.
/// </summary>
public static class RiskRules
{
    public const int DuplicateScreenshotWeight = 40;
    public const int RepeatedContentWeight = 20;
    public const int OutsideCampaignWindowWeight = 30;
    public const int AccountNotVerifiedWeight = 10;
    public const int HighVelocityWeight = 15;
    public const int NewParticipantWeight = 5;
    public const int PostedLongBeforeSubmissionWeight = 15;
    public const int UnresolvedShortLinkWeight = 10;
    public static readonly TimeSpan NewParticipantPeriod = TimeSpan.FromDays(7);

    public static IReadOnlyList<RiskFlag> Evaluate(RiskSignals s)
    {
        var flags = new List<RiskFlag>();

        if (s.SameScreenshotCount > 0)
            flags.Add(new(SubmissionFlagType.DuplicateScreenshot,
                $"The screenshot is identical to {Plural(s.SameScreenshotCount, "other submission")}.", DuplicateScreenshotWeight));

        if (s.RepeatedContentCount > 0)
            flags.Add(new(SubmissionFlagType.RepeatedContent,
                $"The caption repeats text used in {Plural(s.RepeatedContentCount, "other submission")}.", RepeatedContentWeight));

        if (s.PostedAtUtc < s.CampaignStartsAtUtc || s.PostedAtUtc > s.CampaignEndsAtUtc)
            flags.Add(new(SubmissionFlagType.OutsideCampaignWindow,
                $"Posted at {s.PostedAtUtc:yyyy-MM-dd HH:mm} UTC, outside the campaign window " +
                $"{s.CampaignStartsAtUtc:yyyy-MM-dd HH:mm}–{s.CampaignEndsAtUtc:yyyy-MM-dd HH:mm} UTC.", OutsideCampaignWindowWeight));

        if (s.SubmittedAtUtc - s.PostedAtUtc > SubmissionTiming.LongGapFlagThreshold)
            flags.Add(new(SubmissionFlagType.PostedLongBeforeSubmission,
                $"Declared posted at {s.PostedAtUtc:yyyy-MM-dd HH:mm} UTC, {(s.SubmittedAtUtc - s.PostedAtUtc).TotalHours:0} hours " +
                $"before it was submitted ({s.SubmittedAtUtc:yyyy-MM-dd HH:mm} UTC).", PostedLongBeforeSubmissionWeight));

        if (s.IsShortLink)
            flags.Add(new(SubmissionFlagType.UnresolvedShortLink,
                "The post link is a short link. Open it and check that it leads to a post not already claimed.", UnresolvedShortLinkWeight));

        if (!s.AccountVerified && !s.CampaignRequiresVerifiedAccount)
            flags.Add(new(SubmissionFlagType.AccountNotVerified,
                "The social profile has not been verified by staff.", AccountNotVerifiedWeight));

        var inWindow = s.SubmissionsInLast24Hours + 1;
        if (inWindow > s.VelocityLimitPer24Hours)
            flags.Add(new(SubmissionFlagType.HighSubmissionVelocity,
                $"{inWindow} submissions in 24 hours (limit {s.VelocityLimitPer24Hours}).", HighVelocityWeight));

        if (s.NowUtc - s.ParticipantCreatedAtUtc < NewParticipantPeriod)
            flags.Add(new(SubmissionFlagType.NewParticipant,
                $"Participant joined {s.ParticipantCreatedAtUtc:yyyy-MM-dd} (less than 7 days ago).", NewParticipantWeight));

        return flags;
    }

    public static int Score(IEnumerable<RiskFlag> flags) => flags.Sum(f => f.Weight);

    private static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
