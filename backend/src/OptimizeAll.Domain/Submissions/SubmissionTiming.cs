namespace OptimizeAll.Domain.Submissions;

/// <summary>
/// How the participant-declared <c>PostedAt</c> and the server-recorded <c>SubmittedAt</c> are used. PostedAt is
/// chosen by the participant, so it may only move money within a bounded window; everything else is anchored to
/// SubmittedAt (set by the server). Campaign-independent. See docs/REWARD_ENGINE.md ("Post and submission times").
/// </summary>
public static class SubmissionTiming
{
    /// <summary>A post may be declared at most this long before the submission (400 <c>submission.posted_at_too_old</c>).</summary>
    public static readonly TimeSpan MaxPostAgeAtSubmission = TimeSpan.FromDays(7);

    /// <summary>A declared post time further than this before the submission raises <c>PostedLongBeforeSubmission</c>.</summary>
    public static readonly TimeSpan LongGapFlagThreshold = TimeSpan.FromHours(48);

    /// <summary>The server may be this far behind the participant's clock.</summary>
    public static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(10);

    public static bool IsPostedAtTooOld(DateTime postedAtUtc, DateTime submittedAtUtc) =>
        postedAtUtc < submittedAtUtc - MaxPostAgeAtSubmission;

    public static bool IsPostedAtInFuture(DateTime postedAtUtc, DateTime nowUtc) => postedAtUtc > nowUtc + MaxFutureSkew;

    /// <summary>
    /// Instant used for rate-override and time-limited-bonus windows: <c>min(PostedAt, SubmittedAt)</c>. In practice
    /// the post time, but never later than the submission and (by validation) never earlier than SubmittedAt − 7 days.
    /// </summary>
    public static DateTime RewardWindowTime(DateTime postedAtUtc, DateTime submittedAtUtc) =>
        postedAtUtc < submittedAtUtc ? postedAtUtc : submittedAtUtc;

    /// <summary>Instant whose campaign-local day/week the daily/weekly caps count against: the submission time.</summary>
    public static DateTime CapTime(DateTime submittedAtUtc) => submittedAtUtc;

    /// <summary>When a live check may be confirmed: <c>max(PostedAt, SubmittedAt) + minPostLiveHours</c>.</summary>
    public static DateTime LiveCheckDueAt(DateTime postedAtUtc, DateTime submittedAtUtc, int minPostLiveHours) =>
        (postedAtUtc > submittedAtUtc ? postedAtUtc : submittedAtUtc).AddHours(minPostLiveHours);
}
