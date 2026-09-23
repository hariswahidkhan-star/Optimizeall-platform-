using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Projects;

public enum DeliverableType
{
    Copy,
    Design,
    Video,
    BlogPost,
    AdCreative,
    Report,
    LandingPage,
    SocialPostSet,
    Other,
}

/// <summary>Draft → InternalReview → ClientReview → (ChangesRequested → Draft …) → Approved → Published.</summary>
public enum DeliverableStatus
{
    Draft,
    InternalReview,
    ClientReview,
    ChangesRequested,
    Approved,
    Published,
}

/// <summary>A piece of work the client reviews. Every upload/link is a new <see cref="DeliverableVersion"/>.</summary>
public class Deliverable : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? TaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DeliverableType Type { get; set; }
    public DeliverableStatus Status { get; set; } = DeliverableStatus.Draft;

    /// <summary>Latest version number (0 = no version yet). The client can only act on this version.</summary>
    public int CurrentVersion { get; set; }

    /// <summary>Highest version ever sent to the client (0 = never). Client users only see versions up to this one.</summary>
    public int LastSentVersion { get; set; }
    public Guid? OwnerUserId { get; set; }

    /// <summary>Internal reviewer (null: any staff member with projects.manage).</summary>
    public Guid? ReviewerUserId { get; set; }
    public DateTime? SentToClientAt { get; set; }

    /// <summary>Client feedback due (SLA) for the version under client review.</summary>
    public DateTime? ClientDueAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int? ApprovedVersion { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public bool AutoApproved { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class DeliverableVersion : Entity
{
    public Guid DeliverableId { get; set; }
    public Guid ClientAccountId { get; set; }
    public int Number { get; set; }
    public Guid? FileId { get; set; }

    /// <summary>External link (video on a host, Figma, Google Doc, staging landing page).</summary>
    public string? LinkUrl { get; set; }

    /// <summary>Inline text content (copy, captions, blog draft in Markdown).</summary>
    public string? Body { get; set; }
    public string? Notes { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum ReviewStage
{
    Internal,
    Client,
    System,
}

public enum ReviewDecision
{
    Submitted,
    InternalApproved,
    InternalChangesRequested,
    Approved,
    ChangesRequested,
    AutoApproved,
    Published,
}

/// <summary>Append-only decision log: who did what on which version, when (the approval record).</summary>
public class DeliverableReview : Entity
{
    public Guid DeliverableId { get; set; }
    public Guid ClientAccountId { get; set; }
    public int VersionNumber { get; set; }
    public ReviewStage Stage { get; set; }
    public ReviewDecision Decision { get; set; }
    public Guid? UserId { get; set; }

    /// <summary>Name at the time of the decision (kept even if the user is later renamed or removed).</summary>
    public string? UserName { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A comment pinned to one version. Internal comments are never sent to client users.</summary>
public class DeliverableComment : Entity
{
    public Guid DeliverableId { get; set; }
    public Guid ClientAccountId { get; set; }
    public int VersionNumber { get; set; }
    public Guid AuthorUserId { get; set; }
    public bool FromClient { get; set; }
    public bool IsInternal { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public enum DeliverableAction
{
    Submit,
    InternalApprove,
    InternalRequestChanges,
    ClientApprove,
    ClientRequestChanges,
    AutoApprove,
    Publish,
}

/// <summary>The deliverable approval state machine (pure).</summary>
public static class DeliverableWorkflow
{
    /// <summary>The status after <paramref name="action"/>, or null when the action is not allowed in <paramref name="status"/>.</summary>
    public static DeliverableStatus? Next(DeliverableStatus status, DeliverableAction action) => (status, action) switch
    {
        (DeliverableStatus.Draft or DeliverableStatus.ChangesRequested, DeliverableAction.Submit) => DeliverableStatus.InternalReview,
        (DeliverableStatus.InternalReview, DeliverableAction.InternalApprove) => DeliverableStatus.ClientReview,
        (DeliverableStatus.InternalReview, DeliverableAction.InternalRequestChanges) => DeliverableStatus.Draft,
        (DeliverableStatus.ClientReview, DeliverableAction.ClientApprove) => DeliverableStatus.Approved,
        (DeliverableStatus.ClientReview, DeliverableAction.AutoApprove) => DeliverableStatus.Approved,
        (DeliverableStatus.ClientReview, DeliverableAction.ClientRequestChanges) => DeliverableStatus.ChangesRequested,
        (DeliverableStatus.Approved, DeliverableAction.Publish) => DeliverableStatus.Published,
        _ => null,
    };

    /// <summary>Statuses in which a new version may be added.</summary>
    public static bool CanAddVersion(DeliverableStatus s) =>
        s is DeliverableStatus.Draft or DeliverableStatus.ChangesRequested or DeliverableStatus.InternalReview;

    public static bool CanSubmitForInternalReview(DeliverableStatus s, int currentVersion) =>
        currentVersion > 0 && s is DeliverableStatus.Draft or DeliverableStatus.ChangesRequested;

    public static bool IsAwaitingClient(DeliverableStatus s) => s == DeliverableStatus.ClientReview;

    public static bool IsOpen(DeliverableStatus s) => s is not (DeliverableStatus.Approved or DeliverableStatus.Published);

    /// <summary>Client feedback due date: <paramref name="slaDays"/> business days (Mon–Fri) after sending.</summary>
    public static DateTime DueAt(DateTime sentAtUtc, int slaDays)
    {
        var due = sentAtUtc;
        var added = 0;
        while (added < Math.Max(1, slaDays))
        {
            due = due.AddDays(1);
            if (due.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) added++;
        }
        return due;
    }
}
