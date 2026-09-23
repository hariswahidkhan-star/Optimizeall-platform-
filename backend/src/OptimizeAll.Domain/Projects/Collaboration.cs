using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Projects;

// ---------------------------------------------------------------- reports

/// <summary>How a KPI value was obtained. Shown next to every number in reports.</summary>
public enum KpiMeasurement
{
    /// <summary>From a connected platform's reported data.</summary>
    Measured,
    /// <summary>Modelled or projected (e.g. estimated reach, attributed revenue).</summary>
    Estimated,
    /// <summary>Typed in by the agency.</summary>
    Manual,
}

public sealed record ReportKpi(
    string Key, string Label, decimal? Value, string? Unit, decimal? PreviousValue, string Source, KpiMeasurement Measurement,
    string? Note = null);

/// <summary>
/// A report section. <see cref="Kind"/>: "summary", "kpis", "channel", "wins", "plan" or "custom". <see cref="ProviderKey"/>
/// names the <c>IClientReportSection</c> provider that filled it (null for manual sections).
/// </summary>
public sealed record ReportSection(
    string Key, string Kind, string Title, string? Body, List<ReportKpi> Kpis, string? ProviderKey = null, string? ProviderNote = null);

public enum ReportStatus
{
    Draft,
    Published,
}

/// <summary>A monthly client performance report. Drafts are internal; published reports appear in the client portal.</summary>
public class ClientReport : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public Guid? ProjectId { get; set; }

    /// <summary>First day of the reported month.</summary>
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string Title { get; set; } = string.Empty;
    public ReportStatus Status { get; set; } = ReportStatus.Draft;
    public string? TemplateKey { get; set; }
    public List<ReportSection> Sections { get; set; } = new();
    public DateTime? PublishedAt { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public Guid? CreatedByUserId { get; set; }

    /// <summary>Set on drafts created by the monthly job: "{clientId}:{yyyy-MM}" (unique → one per client and month).</summary>
    public string? AutoKey { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public sealed record ReportTemplateSection(string Key, string Kind, string Title, string? ProviderKey, string? Prompt);

public class ReportTemplate : AuditedEntity
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<ReportTemplateSection> Sections { get; set; } = new();
}

// ---------------------------------------------------------------- briefs

public enum BriefFieldType
{
    Text,
    LongText,
    Date,
    Url,
    List,
    Select,
}

public sealed record BriefField(string Key, string Label, BriefFieldType Type, bool Required, string? Help, List<string> Options);

/// <summary>A creative brief form for one service line, with dynamic fields.</summary>
public class BriefTemplate : AuditedEntity
{
    public string Key { get; set; } = string.Empty;
    public string ServiceLine { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<BriefField> Fields { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

public sealed record BriefAnswer(string Key, string Label, string Value);

public enum BriefStatus
{
    Submitted,
    InReview,
    Accepted,
    Converted,
    Declined,
}

public class Brief : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public Guid? ProjectId { get; set; }
    public string TemplateKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public BriefStatus Status { get; set; } = BriefStatus.Submitted;
    public List<BriefAnswer> Answers { get; set; } = new();
    public DateOnly? Deadline { get; set; }
    public Guid SubmittedByUserId { get; set; }
    public bool SubmittedByClient { get; set; }
    public DateTime? ConvertedAt { get; set; }
    public string? StaffNote { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

// ---------------------------------------------------------------- messages

/// <summary>A conversation between a client's users and its account team (separate from participant support tickets).</summary>
public class MessageThread : AuditedEntity
{
    public Guid ClientAccountId { get; set; }
    public Guid? ProjectId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime LastMessageAt { get; set; }
    public int MessageCount { get; set; }
    public string? LastMessagePreview { get; set; }
    public Guid? LastAuthorUserId { get; set; }
}

public class ThreadMessage : Entity
{
    public Guid ThreadId { get; set; }
    public Guid ClientAccountId { get; set; }
    public Guid AuthorUserId { get; set; }
    public bool FromClient { get; set; }
    public string Body { get; set; } = string.Empty;
    public List<Guid> AttachmentFileIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

/// <summary>Read receipt: the last time a user read a thread.</summary>
public class ThreadReadState
{
    public Guid ThreadId { get; set; }
    public Guid UserId { get; set; }
    public DateTime LastReadAt { get; set; }
}

// ---------------------------------------------------------------- meetings

public enum MeetingKind
{
    Kickoff,
    MonthlyReview,
    Strategy,
    Creative,
    Other,
}

public enum MeetingStatus
{
    Scheduled,
    Held,
    Cancelled,
}

public sealed record MeetingActionItem(Guid Id, string Text, Guid? AssigneeUserId, DateOnly? DueDate, Guid? TaskId);

public class Meeting : AuditedEntity, IConcurrencyStamped
{
    public Guid ClientAccountId { get; set; }
    public Guid? ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public MeetingKind Kind { get; set; }
    public DateTime StartsAt { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public string? Location { get; set; }
    public string? Agenda { get; set; }
    public string? Notes { get; set; }
    public MeetingStatus Status { get; set; } = MeetingStatus.Scheduled;
    public List<Guid> AttendeeUserIds { get; set; } = new();
    public List<MeetingActionItem> ActionItems { get; set; } = new();
    public Guid CreatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
