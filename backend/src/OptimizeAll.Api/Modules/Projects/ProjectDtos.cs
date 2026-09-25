using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Clients;
using OptimizeAll.Domain.Projects;

namespace OptimizeAll.Api.Modules.Projects;

// ---------------------------------------------------------------- projects

public sealed class ProjectListQuery : PageQuery
{
    public Guid? ClientId { get; set; }
    public ProjectStatus? Status { get; set; }

    /// <summary>"me": projects I own or am a member of.</summary>
    [MaxLength(10)]
    public string? Member { get; set; }
}

public sealed record ProjectSummaryDto(
    Guid Id, Guid ClientId, string ClientName, string Name, ProjectType Type, ProjectStatus Status, IReadOnlyList<string> ServiceLines,
    DateOnly? StartDate, DateOnly? EndDate, PersonDto? Owner, int OpenTasks, int OverdueTasks, int TotalTasks, int DoneTasks,
    decimal? BudgetHours, decimal HoursLogged, bool AtRisk);

public sealed record ProjectFileDto(DeliveryFileDto File, string Source, Guid? DeliverableId, Guid? TaskId);

public sealed record MilestoneDto(Guid Id, string Title, DateOnly? DueDate, MilestoneStatus Status, int SortOrder, bool ClientVisible, int TaskCount, int DoneCount);

public sealed record ProjectDetailDto(
    Guid Id, Guid ClientId, string ClientName, string ClientCurrency, string Name, string? Description, ProjectType Type,
    ProjectStatus Status, IReadOnlyList<string> ServiceLines, DateOnly? StartDate, DateOnly? EndDate, decimal? BudgetHours,
    decimal? BudgetAmount, string Currency, decimal? DefaultHourlyRate, PersonDto? Owner, IReadOnlyList<PersonDto> Members,
    IReadOnlyList<MilestoneDto> Milestones, string? TemplateKey, BudgetBurn Budget, DateTime CreatedAt, Guid ConcurrencyStamp);

public class ProjectRequestBase
{
    [Required, MinLength(2), MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    [Required]
    public ProjectType? Type { get; set; }

    public List<string> ServiceLines { get; set; } = new();
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    [Range(typeof(decimal), "0", "100000")]
    public decimal? BudgetHours { get; set; }

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal? BudgetAmount { get; set; }

    [Range(typeof(decimal), "0", "100000")]
    public decimal? DefaultHourlyRate { get; set; }

    public Guid? OwnerUserId { get; set; }
    public List<Guid> MemberUserIds { get; set; } = new();
}

public sealed class CreateProjectRequest : ProjectRequestBase
{
    [Required]
    public Guid? ClientId { get; set; }

    /// <summary>Service template key: creates its milestones, tasks and recurring tasks.</summary>
    [MaxLength(64)]
    public string? TemplateKey { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Planning;
}

public sealed class UpdateProjectRequest : ProjectRequestBase
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class ChangeProjectStatusRequest
{
    [Required]
    public ProjectStatus? Status { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class MilestoneRequest
{
    [Required, MinLength(2), MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public DateOnly? DueDate { get; set; }
    public MilestoneStatus Status { get; set; } = MilestoneStatus.Open;
    public bool ClientVisible { get; set; } = true;
}

// ---------------------------------------------------------------- tasks

public sealed class TaskListQuery
{
    public ProjectTaskStatus? Status { get; set; }
    public Guid? AssigneeId { get; set; }
    public Guid? MilestoneId { get; set; }

    [MaxLength(200)]
    public string? Search { get; set; }
}

public sealed class MyTasksQuery
{
    /// <summary>"open" (default), "overdue", "today", "week", "done".</summary>
    [MaxLength(20)]
    public string? Filter { get; set; }
}

public sealed record TaskSummaryDto(
    Guid Id, Guid ProjectId, string ProjectName, Guid ClientId, string ClientName, Guid? MilestoneId, string Title,
    ProjectTaskStatus Status, TaskPriority Priority, DateOnly? DueDate, decimal? EstimateHours, IReadOnlyList<string> Labels,
    bool ClientVisible, double SortOrder, IReadOnlyList<PersonDto> Assignees, int ChecklistDone, int ChecklistTotal,
    int CommentCount, bool IsBlocked, bool IsOverdue, Guid ConcurrencyStamp);

public sealed record ChecklistItemDto(Guid Id, string Text, bool IsDone, int SortOrder);

public sealed record TaskCommentDto(Guid Id, PersonDto Author, string Body, IReadOnlyList<PersonDto> Mentions, DateTime CreatedAt, DateTime? EditedAt = null);

public sealed record TaskRefDto(Guid Id, string Title, ProjectTaskStatus Status);

public sealed record AttachmentDto(Guid Id, DeliveryFileDto File, PersonDto AddedBy, DateTime CreatedAt);

public sealed record TaskDetailDto(
    TaskSummaryDto Task, string? Description, IReadOnlyList<ChecklistItemDto> Checklist, IReadOnlyList<TaskCommentDto> Comments,
    IReadOnlyList<PersonDto> Watchers, IReadOnlyList<TaskRefDto> BlockedBy, IReadOnlyList<TaskRefDto> Blocking,
    IReadOnlyList<AttachmentDto> Attachments, decimal HoursLogged, DateTime CreatedAt, DateTime? CompletedAt, bool IWatch);

public class TaskRequestBase
{
    [Required, MinLength(2), MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string? Description { get; set; }

    public ProjectTaskStatus Status { get; set; } = ProjectTaskStatus.Todo;
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public DateOnly? DueDate { get; set; }

    [Range(typeof(decimal), "0", "1000")]
    public decimal? EstimateHours { get; set; }

    public List<string> Labels { get; set; } = new();
    public Guid? MilestoneId { get; set; }
    public bool ClientVisible { get; set; }
    public List<Guid> AssigneeUserIds { get; set; } = new();
    public List<Guid> BlockedByTaskIds { get; set; } = new();
}

public sealed class CreateTaskRequest : TaskRequestBase
{
    public List<string> Checklist { get; set; } = new();
}

public sealed class UpdateTaskRequest : TaskRequestBase
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

/// <summary>Kanban move: to a status column, placed between two neighbours (or at an end).</summary>
public sealed class MoveTaskRequest
{
    [Required]
    public ProjectTaskStatus? Status { get; set; }

    /// <summary>The task that will be directly above (null: top of the column).</summary>
    public Guid? AfterTaskId { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class TaskCommentRequest
{
    [Required, MinLength(1), MaxLength(10000)]
    public string Body { get; set; } = string.Empty;

    /// <summary>Staff users to @mention (each is notified).</summary>
    public List<Guid> MentionUserIds { get; set; } = new();
}

public sealed class ChecklistItemRequest
{
    [Required, MinLength(1), MaxLength(500)]
    public string Text { get; set; } = string.Empty;

    public bool IsDone { get; set; }
}

public sealed class AttachFileRequest
{
    [Required]
    public Guid? FileId { get; set; }
}

public sealed record RecurringRuleDto(
    Guid Id, Guid ProjectId, string Title, string? Description, int DayOfMonth, int DueInDays, PersonDto? Assignee,
    decimal? EstimateHours, IReadOnlyList<string> Labels, bool ClientVisible, bool IsActive);

public sealed class RecurringRuleRequest
{
    [Required, MinLength(2), MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    [Range(1, 28)]
    public int DayOfMonth { get; set; } = 1;

    [Range(0, 60)]
    public int DueInDays { get; set; } = 5;

    public Guid? AssigneeUserId { get; set; }

    [Range(typeof(decimal), "0", "1000")]
    public decimal? EstimateHours { get; set; }

    public List<string> Labels { get; set; } = new();
    public bool ClientVisible { get; set; }
    public bool IsActive { get; set; } = true;
}

// ---------------------------------------------------------------- templates

public sealed record ProjectTemplateDto(
    Guid Id, string Key, string Name, string? Description, ProjectType ProjectType, IReadOnlyList<string> ServiceLines,
    decimal? DefaultBudgetHours, int? DurationDays, IReadOnlyList<TemplateMilestone> Milestones, IReadOnlyList<TemplateTask> Tasks,
    IReadOnlyList<TemplateRecurring> Recurring, bool IsActive, Guid ConcurrencyStamp, bool BuiltIn = false);

public sealed class ProjectTemplateRequest
{
    [Required, RegularExpression("^[a-z0-9-]{2,64}$")]
    public string Key { get; set; } = string.Empty;

    [Required, MinLength(2), MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required]
    public ProjectType? ProjectType { get; set; }

    public List<string> ServiceLines { get; set; } = new();

    [Range(typeof(decimal), "0", "100000")]
    public decimal? DefaultBudgetHours { get; set; }

    [Range(1, 3650)]
    public int? DurationDays { get; set; }

    public List<TemplateMilestone> Milestones { get; set; } = new();
    public List<TemplateTask> Tasks { get; set; } = new();
    public List<TemplateRecurring> Recurring { get; set; } = new();
    public bool IsActive { get; set; } = true;

    /// <summary>Required when updating.</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record BriefTemplateDto(Guid Id, string Key, string ServiceLine, string Name, string? Description, IReadOnlyList<BriefField> Fields,
    bool IsActive = true, bool BuiltIn = false, Guid ConcurrencyStamp = default);

public sealed record ReportTemplateDto(Guid Id, string Key, string Name, string? Description, IReadOnlyList<ReportTemplateSection> Sections,
    bool IsActive = true, bool BuiltIn = false, Guid ConcurrencyStamp = default);

// ---------------------------------------------------------------- deliverables

public sealed class DeliverableListQuery : PageQuery
{
    public Guid? ClientId { get; set; }
    public Guid? ProjectId { get; set; }
    public DeliverableStatus? Status { get; set; }

    /// <summary>"review": waiting for my internal review; "client": waiting on clients; "mine": I own it.</summary>
    [MaxLength(10)]
    public string? View { get; set; }
}

public sealed record DeliverableSummaryDto(
    Guid Id, Guid ClientId, string ClientName, Guid ProjectId, string ProjectName, Guid? TaskId, string Title,
    DeliverableType Type, DeliverableStatus Status, int CurrentVersion, PersonDto? Owner, PersonDto? Reviewer,
    DateTime? SentToClientAt, DateTime? ClientDueAt, bool IsOverdue, DateTime? ApprovedAt, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed record DeliverableVersionDto(
    Guid Id, int Number, DeliveryFileDto? File, string? LinkUrl, string? Body, string? Notes, PersonDto CreatedBy, DateTime CreatedAt);

public sealed record DeliverableCommentDto(
    Guid Id, int VersionNumber, PersonDto Author, bool FromClient, bool IsInternal, string Body, DateTime CreatedAt);

public sealed record DeliverableReviewDto(
    Guid Id, int VersionNumber, ReviewStage Stage, ReviewDecision Decision, string? UserName, string? Comment, DateTime CreatedAt);

public sealed record DeliverableDetailDto(
    DeliverableSummaryDto Deliverable, string? Description, IReadOnlyList<DeliverableVersionDto> Versions,
    IReadOnlyList<DeliverableCommentDto> Comments, IReadOnlyList<DeliverableReviewDto> History, int? ApprovedVersion,
    string? ApprovedByName, bool AutoApproved, IReadOnlyList<string> AllowedActions);

public sealed class CreateDeliverableRequest
{
    [Required]
    public Guid? ProjectId { get; set; }

    public Guid? TaskId { get; set; }

    [Required, MinLength(2), MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    [Required]
    public DeliverableType? Type { get; set; }

    public Guid? OwnerUserId { get; set; }
    public Guid? ReviewerUserId { get; set; }
}

public sealed class UpdateDeliverableRequest
{
    [Required, MinLength(2), MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    public Guid? OwnerUserId { get; set; }
    public Guid? ReviewerUserId { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

/// <summary>A new version: a file (multipart), a link and/or inline text, with notes.</summary>
public sealed class DeliverableVersionForm
{
    public IFormFile? File { get; set; }

    [MaxLength(1000)]
    public string? LinkUrl { get; set; }

    [MaxLength(50000)]
    public string? Body { get; set; }

    [MaxLength(4000)]
    public string? Notes { get; set; }
}

public sealed class DeliverableActionRequest
{
    /// <summary>The version the actor looked at; acting on an outdated version is rejected (409).</summary>
    [Required, Range(1, int.MaxValue)]
    public int? Version { get; set; }

    [MaxLength(4000)]
    public string? Comment { get; set; }
}

public sealed class DeliverableCommentRequest
{
    [Required, Range(1, int.MaxValue)]
    public int? Version { get; set; }

    [Required, MinLength(1), MaxLength(4000)]
    public string Body { get; set; } = string.Empty;

    /// <summary>Staff only: hidden from client users.</summary>
    public bool IsInternal { get; set; }
}

// ---------------------------------------------------------------- time

public sealed record TimeEntryDto(
    Guid Id, Guid UserId, string UserName, Guid ClientId, string ClientName, Guid ProjectId, string ProjectName, Guid? TaskId,
    string? TaskTitle, DateOnly Date, int Minutes, bool Billable, string? Note, DateTime? StartedAt, bool IsRunning, bool Locked,
    Guid ConcurrencyStamp);

public sealed class StartTimerRequest
{
    [Required]
    public Guid? ProjectId { get; set; }

    public Guid? TaskId { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    public bool Billable { get; set; } = true;
}

public sealed class TimeEntryRequest
{
    [Required]
    public Guid? ProjectId { get; set; }

    public Guid? TaskId { get; set; }

    [Required]
    public DateOnly? Date { get; set; }

    [Required, Range(1, 24 * 60)]
    public int? Minutes { get; set; }

    public bool Billable { get; set; } = true;

    [MaxLength(1000)]
    public string? Note { get; set; }

    /// <summary>Required when updating.</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class TimeEntryQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }

    /// <summary>Other users' entries need time.view_all.</summary>
    public Guid? UserId { get; set; }

    public Guid? ProjectId { get; set; }
    public Guid? ClientId { get; set; }
}

public sealed record TimesheetDayDto(DateOnly Date, int Minutes);

public sealed record TimesheetDto(
    Guid? Id, Guid UserId, string UserName, DateOnly WeekStart, TimesheetStatus Status, int TotalMinutes, int BillableMinutes,
    IReadOnlyList<TimesheetDayDto> Days, IReadOnlyList<TimeEntryDto> Entries, DateTime? SubmittedAt, DateTime? DecidedAt,
    string? DecidedBy, string? DecisionComment, Guid? ConcurrencyStamp);

public sealed class TimesheetDecisionRequest
{
    [MaxLength(1000)]
    public string? Comment { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record UtilizationRowDto(Guid UserId, string UserName, int TotalMinutes, int BillableMinutes, int CapacityMinutes, decimal UtilizationPercent, decimal BillablePercent);

public sealed record UtilizationDto(DateOnly From, DateOnly To, IReadOnlyList<UtilizationRowDto> Rows, int TotalMinutes, int BillableMinutes);

public sealed record HourlyRateDto(Guid Id, Guid? UserId, string? UserName, Domain.Identity.Role? Role, decimal Rate, string Currency);

public sealed class HourlyRateRequest
{
    public Guid? UserId { get; set; }
    public Domain.Identity.Role? Role { get; set; }

    [Required, Range(typeof(decimal), "0", "100000")]
    public decimal? Rate { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";
}

// ---------------------------------------------------------------- reports

public sealed class ReportListQuery : PageQuery
{
    public Guid? ClientId { get; set; }
    public ReportStatus? Status { get; set; }
}

public sealed record ReportSummaryDto(
    Guid Id, Guid ClientId, string ClientName, string Title, DateOnly PeriodStart, DateOnly PeriodEnd, ReportStatus Status,
    DateTime? PublishedAt, bool AutoGenerated, DateTime UpdatedAt);

public sealed record ReportDto(
    Guid Id, Guid ClientId, string ClientName, Guid? ProjectId, string Title, DateOnly PeriodStart, DateOnly PeriodEnd,
    ReportStatus Status, string? TemplateKey, IReadOnlyList<ReportSection> Sections, DateTime? PublishedAt, string? PublishedBy,
    IReadOnlyList<ReportProviderDto> AvailableProviders, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed record ReportProviderDto(string Key, string Title, string ServiceLine);

public sealed class CreateReportRequest
{
    [Required]
    public Guid? ClientId { get; set; }

    public Guid? ProjectId { get; set; }

    /// <summary>"yyyy-MM".</summary>
    [Required, RegularExpression(@"^\d{4}-(0[1-9]|1[0-2])$")]
    public string Period { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? TemplateKey { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }
}

public sealed class UpdateReportRequest
{
    [Required, MinLength(2), MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public List<ReportSection> Sections { get; set; } = new();

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class RefreshSectionRequest
{
    [Required, MaxLength(64)]
    public string SectionKey { get; set; } = string.Empty;

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class PublishReportRequest
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }

    /// <summary>Email the client's users (in-app notification is always created).</summary>
    public bool NotifyByEmail { get; set; } = true;
}

// ---------------------------------------------------------------- briefs, messages, meetings

public sealed record BriefDto(
    Guid Id, Guid ClientId, string ClientName, Guid? ProjectId, string? ProjectName, string TemplateKey, string TemplateName,
    string Title, BriefStatus Status, IReadOnlyList<BriefAnswer> Answers, DateOnly? Deadline, PersonDto SubmittedBy,
    bool SubmittedByClient, DateTime CreatedAt, DateTime? ConvertedAt, string? StaffNote, Guid ConcurrencyStamp);

public sealed class SubmitBriefRequest
{
    [Required, MaxLength(64)]
    public string TemplateKey { get; set; } = string.Empty;

    [Required, MinLength(2), MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public Guid? ProjectId { get; set; }
    public DateOnly? Deadline { get; set; }

    /// <summary>Field key → value.</summary>
    public Dictionary<string, string> Answers { get; set; } = new();

    /// <summary>Staff only: the client the brief is for.</summary>
    public Guid? ClientId { get; set; }
}

public sealed class ConvertBriefRequest
{
    [Required]
    public Guid? ProjectId { get; set; }

    /// <summary>Tasks to create (title + optional assignee/due date).</summary>
    public List<ConvertBriefTask> Tasks { get; set; } = new();

    /// <summary>Deliverables to create.</summary>
    public List<ConvertBriefDeliverable> Deliverables { get; set; } = new();

    [MaxLength(2000)]
    public string? StaffNote { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record ConvertBriefTask(string Title, Guid? AssigneeUserId, DateOnly? DueDate);

public sealed record ConvertBriefDeliverable(string Title, DeliverableType Type);

public sealed class BriefStatusRequest
{
    [Required]
    public BriefStatus? Status { get; set; }

    [MaxLength(2000)]
    public string? StaffNote { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

/// <summary>Paging (page, pageSize up to 200) and an optional search of the subject and last message.</summary>
public sealed class ThreadQuery : PageQuery;

public sealed record ThreadSummaryDto(Guid Id, Guid ClientId, string Subject, Guid? ProjectId, DateTime LastMessageAt, int MessageCount, int UnreadCount, string? LastMessagePreview, string? LastAuthor, bool IsInternal);

public sealed record MessageDto(Guid Id, PersonDto Author, bool FromClient, string Body, IReadOnlyList<DeliveryFileDto> Attachments, DateTime CreatedAt, IReadOnlyList<string> ReadBy);

/// <summary><c>IsInternal</c>: staff-only thread (never returned to client users). <c>CanReply</c>: whether the caller may post.</summary>
public sealed record ThreadDto(Guid Id, Guid ClientId, string Subject, Guid? ProjectId, IReadOnlyList<MessageDto> Messages, IReadOnlyList<PersonDto> Participants,
    bool IsInternal, bool CanReply);

public sealed class NewThreadRequest
{
    [Required, MinLength(2), MaxLength(200)]
    public string Subject { get; set; } = string.Empty;

    public Guid? ProjectId { get; set; }

    [Required, MinLength(1), MaxLength(10000)]
    public string Body { get; set; } = string.Empty;

    public List<Guid> AttachmentFileIds { get; set; } = new();

    /// <summary>
    /// Staff only: an internal thread the client's users never see. Fixed at creation. A client user sending true gets
    /// <c>400 message.internal_not_allowed</c>.
    /// </summary>
    public bool IsInternal { get; set; }
}

public sealed class NewMessageRequest
{
    [Required, MinLength(1), MaxLength(10000)]
    public string Body { get; set; } = string.Empty;

    public List<Guid> AttachmentFileIds { get; set; } = new();
}

public sealed record MeetingDto(
    Guid Id, Guid ClientId, string ClientName, Guid? ProjectId, string Title, MeetingKind Kind, DateTime StartsAt, int DurationMinutes,
    string? Location, string? Agenda, string? Notes, MeetingStatus Status, IReadOnlyList<PersonDto> Attendees,
    IReadOnlyList<MeetingActionItem> ActionItems, Guid ConcurrencyStamp);

public sealed class MeetingQuery
{
    public Guid? ClientId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public sealed class MeetingRequest
{
    [Required]
    public Guid? ClientId { get; set; }

    public Guid? ProjectId { get; set; }

    [Required, MinLength(2), MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public MeetingKind Kind { get; set; } = MeetingKind.Other;

    [Required]
    public DateTime? StartsAt { get; set; }

    [Range(5, 600)]
    public int DurationMinutes { get; set; } = 30;

    [MaxLength(500)]
    public string? Location { get; set; }

    [MaxLength(8000)]
    public string? Agenda { get; set; }

    [MaxLength(20000)]
    public string? Notes { get; set; }

    public MeetingStatus Status { get; set; } = MeetingStatus.Scheduled;
    public List<Guid> AttendeeUserIds { get; set; } = new();
    public List<MeetingActionItemRequest> ActionItems { get; set; } = new();

    /// <summary>Required when updating.</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record MeetingActionItemRequest(Guid? Id, string Text, Guid? AssigneeUserId, DateOnly? DueDate);

public sealed class ConvertActionItemRequest
{
    [Required]
    public Guid? ProjectId { get; set; }
}
