using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.SocialMedia;

public enum WorkflowAction
{
    Submit,
    ApproveInternal,
    RequestChanges,
    ClientApprove,
    ClientRequestChanges,
    Schedule,
    Unschedule,
    Retry,
}

/// <summary>
/// The content approval workflow (posts and ad creatives):
/// Draft → InternalReview → ClientApproval (only when the client requires it) → Approved → Scheduled → Publishing →
/// Published / Failed. Only the publishing job moves Scheduled → Publishing → Published/Failed.
/// </summary>
public static class PostWorkflow
{
    /// <summary>States whose content may be edited. Editing anything after Draft sends the post back to Draft.</summary>
    public static readonly IReadOnlySet<SocialPostStatus> Editable = new HashSet<SocialPostStatus>
    {
        SocialPostStatus.Draft, SocialPostStatus.InternalReview, SocialPostStatus.ClientApproval, SocialPostStatus.Approved,
        SocialPostStatus.Scheduled, SocialPostStatus.Failed,
    };

    /// <summary>States whose planned/scheduled time may be moved (drag on the calendar).</summary>
    public static readonly IReadOnlySet<SocialPostStatus> Reschedulable = new HashSet<SocialPostStatus>
    {
        SocialPostStatus.Draft, SocialPostStatus.InternalReview, SocialPostStatus.ClientApproval, SocialPostStatus.Approved,
        SocialPostStatus.Scheduled, SocialPostStatus.Failed,
    };

    /// <summary>Target state of an action, or a DomainException (409) when it is not allowed from <paramref name="current"/>.</summary>
    public static SocialPostStatus Next(SocialPostStatus current, WorkflowAction action, bool requireClientApproval)
    {
        SocialPostStatus? next = (current, action) switch
        {
            (SocialPostStatus.Draft, WorkflowAction.Submit) => SocialPostStatus.InternalReview,
            (SocialPostStatus.InternalReview, WorkflowAction.ApproveInternal) =>
                requireClientApproval ? SocialPostStatus.ClientApproval : SocialPostStatus.Approved,
            (SocialPostStatus.InternalReview, WorkflowAction.RequestChanges) => SocialPostStatus.Draft,
            (SocialPostStatus.ClientApproval, WorkflowAction.RequestChanges) => SocialPostStatus.Draft,
            (SocialPostStatus.ClientApproval, WorkflowAction.ClientApprove) => SocialPostStatus.Approved,
            (SocialPostStatus.ClientApproval, WorkflowAction.ClientRequestChanges) => SocialPostStatus.Draft,
            (SocialPostStatus.Approved, WorkflowAction.Schedule) => SocialPostStatus.Scheduled,
            (SocialPostStatus.Scheduled, WorkflowAction.Unschedule) => SocialPostStatus.Approved,
            (SocialPostStatus.Failed, WorkflowAction.Retry) => SocialPostStatus.Scheduled,
            _ => null,
        };
        return next ?? throw DomainException.Conflict("social.invalid_transition",
            $"A post in {current} cannot be moved with '{action}'.");
    }

    public static bool CanTransition(SocialPostStatus current, WorkflowAction action, bool requireClientApproval)
    {
        try
        {
            Next(current, action, requireClientApproval);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }

    /// <summary>Retry backoff after a transient failure: 2, 10, 30 minutes; null once <paramref name="maxAttempts"/> is reached.</summary>
    public static TimeSpan? RetryDelay(int attemptsSoFar, int maxAttempts = 4) => attemptsSoFar >= maxAttempts
        ? null
        : attemptsSoFar switch
        {
            <= 1 => TimeSpan.FromMinutes(2),
            2 => TimeSpan.FromMinutes(10),
            _ => TimeSpan.FromMinutes(30),
        };

    /// <summary>The post status that follows from its variants after a publishing pass.</summary>
    public static SocialPostStatus Aggregate(IReadOnlyCollection<VariantPublishStatus> variants, bool anyRetryPending)
    {
        if (variants.Count > 0 && variants.All(v => v == VariantPublishStatus.Published)) return SocialPostStatus.Published;
        if (anyRetryPending) return SocialPostStatus.Scheduled;
        if (variants.Any(v => v == VariantPublishStatus.Publishing)) return SocialPostStatus.Publishing;
        return SocialPostStatus.Failed;
    }
}

/// <summary>Evergreen recycling rule: when the next copy of a published evergreen post is due.</summary>
public static class EvergreenRules
{
    /// <summary>
    /// The repeat number to create now, or null when the post is not evergreen, reached its maximum, or is not due.
    /// <paramref name="lastPublishedAt"/> is the latest publish time of the original or any of its copies.
    /// </summary>
    public static int? DueRepeat(bool isEvergreen, int repeatCount, int maxRepeats, int intervalDays, DateTime? lastPublishedAt, DateTime now)
    {
        if (!isEvergreen || lastPublishedAt is null || intervalDays < 1) return null;
        if (repeatCount >= maxRepeats) return null;
        return lastPublishedAt.Value.AddDays(intervalDays) <= now ? repeatCount + 1 : null;
    }
}

/// <summary>Computes queue times from weekly slots in the client's time zone.</summary>
public static class QueueScheduler
{
    /// <summary>
    /// The first slot strictly after <paramref name="afterUtc"/> that is not already taken (within one minute) by a post
    /// in <paramref name="takenUtc"/>. Searches up to eight weeks ahead; null when there are no slots.
    /// </summary>
    public static DateTime? NextFreeSlot(IReadOnlyCollection<(DayOfWeek Day, int Minute)> slots, TimeZoneInfo zone,
        DateTime afterUtc, IReadOnlyCollection<DateTime> takenUtc)
    {
        if (slots.Count == 0) return null;
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), zone).Date;
        for (var d = 0; d < 7 * 8; d++)
        {
            var date = localStart.AddDays(d);
            foreach (var slot in slots.Where(s => s.Day == date.DayOfWeek).OrderBy(s => s.Minute))
            {
                var local = DateTime.SpecifyKind(date.AddMinutes(slot.Minute), DateTimeKind.Unspecified);
                if (zone.IsInvalidTime(local)) continue; // skipped by a DST jump
                var utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
                if (utc <= afterUtc) continue;
                if (takenUtc.Any(t => Math.Abs((t - utc).TotalMinutes) < 1)) continue;
                return utc;
            }
        }
        return null;
    }

    /// <summary>Resolves an IANA/Windows zone id, falling back to UTC.</summary>
    public static TimeZoneInfo Zone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
