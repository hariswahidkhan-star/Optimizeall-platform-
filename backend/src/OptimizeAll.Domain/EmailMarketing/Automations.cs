using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.EmailMarketing;

public enum AutomationStatus
{
    Draft,
    Active,
    Paused,
    Archived,
}

public enum AutomationTrigger
{
    /// <summary>Contact became subscribed to a list (config: listId).</summary>
    ListSubscribed,
    /// <summary>Tag added to a contact (config: tag).</summary>
    TagAdded,
    /// <summary>A landing-page form was submitted (config: formId, optional).</summary>
    FormSubmitted,
    /// <summary>Website newsletter double opt-in completed (agency workspace).</summary>
    NewsletterConfirmed,
    /// <summary>Yearly on a date custom field (config: field, e.g. "birthday").</summary>
    DateAnniversary,
    /// <summary>A custom event posted to the event API (config: eventName).</summary>
    CustomEvent,
}

public enum ReentryPolicy
{
    /// <summary>A contact enters at most once.</summary>
    Never,
    /// <summary>A contact may enter again once their previous run finished (and the cooldown passed).</summary>
    AfterExit,
}

/// <summary>A journey: trigger → steps. Steps are stored in <see cref="AutomationStep"/> rows keyed by a stable step key.</summary>
public class Automation : AuditedEntity, IConcurrencyStamped
{
    public Guid? ClientAccountId { get; set; }
    public string ScopeKey { get; set; } = Workspace.AgencyKey;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AutomationStatus Status { get; set; } = AutomationStatus.Draft;
    public AutomationTrigger Trigger { get; set; }
    /// <summary>JSON <see cref="TriggerConfig"/>.</summary>
    public string TriggerConfigJson { get; set; } = "{}";
    public ReentryPolicy Reentry { get; set; } = ReentryPolicy.Never;
    public int ReentryCooldownDays { get; set; }
    /// <summary>JSON <see cref="GoalConfig"/>; contacts exit as soon as the goal is met. Empty = no goal.</summary>
    public string? GoalJson { get; set; }
    public Guid? SenderProfileId { get; set; }
    /// <summary>Key of the first step.</summary>
    public string? EntryStepKey { get; set; }
    public string? SeedKey { get; set; }
    /// <summary>Last date (yyyy-MM-dd, workspace time zone) the anniversary scan ran.</summary>
    public string? LastAnniversaryScan { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public enum AutomationStepType
{
    SendEmail,
    SendSms,
    Wait,
    Condition,
    AddTag,
    RemoveTag,
    NotifyStaff,
    Exit,
}

public class AutomationStep : Entity
{
    public Guid AutomationId { get; set; }
    /// <summary>Stable key referenced by enrollments and branches (e.g. "s1").</summary>
    public string Key { get; set; } = string.Empty;
    public int Position { get; set; }
    public AutomationStepType Type { get; set; }
    /// <summary>JSON <see cref="StepConfig"/>.</summary>
    public string ConfigJson { get; set; } = "{}";
    /// <summary>Next step for linear steps and the "yes" branch of a condition; null = end.</summary>
    public string? NextKey { get; set; }
    /// <summary>"No" branch of a condition.</summary>
    public string? AltNextKey { get; set; }
}

public enum EnrollmentStatus
{
    Active,
    Completed,
    Exited,
    Failed,
}

/// <summary>One contact's run through an automation (a state machine advanced by the automation job).</summary>
public class AutomationEnrollment : Entity
{
    public Guid AutomationId { get; set; }
    public Guid? ClientAccountId { get; set; }
    public Guid SubscriberId { get; set; }
    /// <summary>Entry number for this contact (1, 2, …; anniversary triggers use the year) — unique with automation + subscriber.</summary>
    public int Iteration { get; set; } = 1;
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Active;
    public string? CurrentStepKey { get; set; }
    public DateTime NextRunAt { get; set; }
    public DateTime EnteredAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? ExitReason { get; set; }
    public Guid? ClaimId { get; set; }
    public DateTime? LockedUntil { get; set; }
    public int StepsExecuted { get; set; }
    /// <summary>Trigger payload (event properties) available to merge tags as <c>event.&lt;key&gt;</c>.</summary>
    public string? TriggerDataJson { get; set; }
}

public enum StepRunStatus
{
    Running,
    Completed,
    Skipped,
    Failed,
}

/// <summary>
/// Execution record of one step for one enrollment; unique per (enrollment, step) so a retried job run never repeats a
/// side effect (a send, a tag, a staff notification). Send steps also keep the message outcome and engagement here.
/// </summary>
public class AutomationStepRun : Entity
{
    public Guid EnrollmentId { get; set; }
    public Guid AutomationId { get; set; }
    public Guid? ClientAccountId { get; set; }
    public Guid SubscriberId { get; set; }
    public string StepKey { get; set; } = string.Empty;
    public AutomationStepType StepType { get; set; }
    public StepRunStatus Status { get; set; } = StepRunStatus.Running;
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Detail { get; set; }
    // Message outcome (send steps).
    public MessageChannel? Channel { get; set; }
    public string? Address { get; set; }
    public string? ProviderMessageId { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime? ClickedAt { get; set; }
    public int Segments { get; set; }
}
