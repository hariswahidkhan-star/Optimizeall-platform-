using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Projects;

namespace OptimizeAll.Api.Modules.Projects;

/// <summary>Agency home dashboard (role-aware). Any delivery staff (projects.view).</summary>
[ApiController]
[HasPermission(Permissions.ProjectsView)]
[Route("api/v1/agency/dashboard")]
public sealed class AgencyDashboardController(DashboardService dashboard) : ControllerBase
{
    [HttpGet]
    public Task<AgencyDashboardDto> Get(CancellationToken ct) => dashboard.AgencyAsync(ct);
}

[ApiController]
[HasPermission(Permissions.ProjectsView)]
[Route("api/v1/agency/projects")]
public sealed class AgencyProjectsController(ProjectService projects, TaskService tasks, DeliverableService deliverables, TimeService time) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ProjectSummaryDto>> List([FromQuery] ProjectListQuery query, CancellationToken ct) => projects.ListAsync(query, ct);

    /// <summary>Creates a project; with <c>templateKey</c> the template's milestones, tasks and recurring tasks are created too.</summary>
    [HttpPost]
    [HasPermission(Permissions.ProjectsManage)]
    [ProducesResponseType(typeof(ProjectDetailDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateProjectRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await projects.CreateAsync(request, ct));

    [HttpGet("{id:guid}")]
    public Task<ProjectDetailDto> Get(Guid id, CancellationToken ct) => projects.GetAsync(id, ct);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<ProjectDetailDto> Update(Guid id, UpdateProjectRequest request, CancellationToken ct) => projects.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/status")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<ProjectDetailDto> Status(Guid id, ChangeProjectStatusRequest request, CancellationToken ct) => projects.ChangeStatusAsync(id, request, ct);

    [HttpPost("{id:guid}/milestones")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<IReadOnlyList<MilestoneDto>> AddMilestone(Guid id, MilestoneRequest request, CancellationToken ct) =>
        projects.AddMilestoneAsync(id, request, ct);

    [HttpPut("{id:guid}/milestones/{milestoneId:guid}")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<IReadOnlyList<MilestoneDto>> UpdateMilestone(Guid id, Guid milestoneId, MilestoneRequest request, CancellationToken ct) =>
        projects.UpdateMilestoneAsync(id, milestoneId, request, ct);

    [HttpDelete("{id:guid}/milestones/{milestoneId:guid}")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<IReadOnlyList<MilestoneDto>> DeleteMilestone(Guid id, Guid milestoneId, CancellationToken ct) =>
        projects.DeleteMilestoneAsync(id, milestoneId, ct);

    [HttpGet("{id:guid}/tasks")]
    public Task<IReadOnlyList<TaskSummaryDto>> Tasks(Guid id, [FromQuery] TaskListQuery query, CancellationToken ct) =>
        tasks.ListForProjectAsync(id, query, ct);

    [HttpPost("{id:guid}/tasks")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTask(Guid id, CreateTaskRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await tasks.CreateAsync(id, request, ct));

    [HttpGet("{id:guid}/recurring-tasks")]
    public Task<IReadOnlyList<RecurringRuleDto>> Recurring(Guid id, CancellationToken ct) => tasks.RecurringAsync(id, ct);

    [HttpPost("{id:guid}/recurring-tasks")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<IReadOnlyList<RecurringRuleDto>> AddRecurring(Guid id, RecurringRuleRequest request, CancellationToken ct) =>
        tasks.SaveRecurringAsync(id, null, request, ct);

    [HttpPut("{id:guid}/recurring-tasks/{ruleId:guid}")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<IReadOnlyList<RecurringRuleDto>> UpdateRecurring(Guid id, Guid ruleId, RecurringRuleRequest request, CancellationToken ct) =>
        tasks.SaveRecurringAsync(id, ruleId, request, ct);

    [HttpDelete("{id:guid}/recurring-tasks/{ruleId:guid}")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<IReadOnlyList<RecurringRuleDto>> DeleteRecurring(Guid id, Guid ruleId, CancellationToken ct) =>
        tasks.DeleteRecurringAsync(id, ruleId, ct);

    /// <summary>Files of the project (deliverable versions and task attachments).</summary>
    [HttpGet("{id:guid}/files")]
    public Task<IReadOnlyList<ProjectFileDto>> Files(Guid id, CancellationToken ct) => projects.FilesAsync(id, ct);

    /// <summary>Budget burn (hours and amount at the user → role → project default hourly rate).</summary>
    [HttpGet("{id:guid}/budget")]
    public async Task<BudgetBurn> Budget(Guid id, CancellationToken ct) => await projects.BudgetAsync(await projects.LoadAsync(id, ct), ct);

    /// <summary>Time logged on the project (time.view_all).</summary>
    [HttpGet("{id:guid}/time")]
    [HasPermission(Permissions.TimeViewAll)]
    public Task<IReadOnlyList<TimeEntryDto>> Time(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        return time.ListAsync(new TimeEntryQuery { ProjectId = id, From = from ?? end.AddDays(-365), To = end }, ct);
    }
}

[ApiController]
[HasPermission(Permissions.ProjectsView)]
[Route("api/v1/agency/tasks")]
public sealed class AgencyTasksController(TaskService tasks) : ControllerBase
{
    /// <summary>Tasks assigned to me. <c>filter</c>: open (default), overdue, today, week, done.</summary>
    [HttpGet("mine")]
    public Task<IReadOnlyList<TaskSummaryDto>> Mine([FromQuery] MyTasksQuery query, CancellationToken ct) => tasks.MyTasksAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<TaskDetailDto> Get(Guid id, CancellationToken ct) => tasks.GetAsync(id, ct);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    public Task<TaskDetailDto> Update(Guid id, UpdateTaskRequest request, CancellationToken ct) => tasks.UpdateAsync(id, request, ct);

    /// <summary>Kanban move (status column + position). Also used by the keyboard move controls.</summary>
    [HttpPost("{id:guid}/move")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    public Task<TaskSummaryDto> Move(Guid id, MoveTaskRequest request, CancellationToken ct) => tasks.MoveAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.ProjectsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await tasks.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Comment with @mentions (mentioned staff are notified in-app and by email).</summary>
    [HttpPost("{id:guid}/comments")]
    public Task<TaskDetailDto> Comment(Guid id, TaskCommentRequest request, CancellationToken ct) => tasks.CommentAsync(id, request, ct);

    [HttpPost("{id:guid}/watch")]
    public Task<TaskDetailDto> Watch(Guid id, CancellationToken ct) => tasks.WatchAsync(id, true, ct);

    [HttpDelete("{id:guid}/watch")]
    public Task<TaskDetailDto> Unwatch(Guid id, CancellationToken ct) => tasks.WatchAsync(id, false, ct);

    [HttpPost("{id:guid}/checklist")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    public Task<TaskDetailDto> AddChecklistItem(Guid id, ChecklistItemRequest request, CancellationToken ct) => tasks.AddChecklistItemAsync(id, request, ct);

    [HttpPut("{id:guid}/checklist/{itemId:guid}")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    public Task<TaskDetailDto> UpdateChecklistItem(Guid id, Guid itemId, ChecklistItemRequest request, CancellationToken ct) =>
        tasks.UpdateChecklistItemAsync(id, itemId, request, ct);

    [HttpDelete("{id:guid}/checklist/{itemId:guid}")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    public Task<TaskDetailDto> DeleteChecklistItem(Guid id, Guid itemId, CancellationToken ct) => tasks.DeleteChecklistItemAsync(id, itemId, ct);

    /// <summary>Attaches a file uploaded with POST /agency/clients/{clientId}/files.</summary>
    [HttpPost("{id:guid}/attachments")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    public Task<TaskDetailDto> Attach(Guid id, AttachFileRequest request, CancellationToken ct) => tasks.AttachAsync(id, request.FileId!.Value, ct);

    [HttpDelete("{id:guid}/attachments/{attachmentId:guid}")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    public Task<TaskDetailDto> Detach(Guid id, Guid attachmentId, CancellationToken ct) => tasks.DetachAsync(id, attachmentId, ct);
}

[ApiController]
[HasPermission(Permissions.ProjectsView)]
[Route("api/v1/agency/templates")]
public sealed class AgencyTemplatesController(ProjectService projects) : ControllerBase
{
    [HttpGet("projects")]
    public Task<IReadOnlyList<ProjectTemplateDto>> ProjectTemplates([FromQuery] bool includeInactive, CancellationToken ct) =>
        projects.TemplatesAsync(includeInactive, ct);

    [HttpPost("projects")]
    [HasPermission(Permissions.ProjectsManage)]
    public async Task<IActionResult> CreateProjectTemplate(ProjectTemplateRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await projects.SaveTemplateAsync(null, request, ct));

    [HttpPut("projects/{id:guid}")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<ProjectTemplateDto> UpdateProjectTemplate(Guid id, ProjectTemplateRequest request, CancellationToken ct) =>
        projects.SaveTemplateAsync(id, request, ct);

    [HttpGet("briefs")]
    public Task<IReadOnlyList<BriefTemplateDto>> BriefTemplates(CancellationToken ct) => projects.BriefTemplatesAsync(ct);

    [HttpGet("reports")]
    public Task<IReadOnlyList<ReportTemplateDto>> ReportTemplates(CancellationToken ct) => projects.ReportTemplatesAsync(ct);
}

[ApiController]
[HasPermission(Permissions.ProjectsView)]
[Route("api/v1/agency/deliverables")]
public sealed class AgencyDeliverablesController(DeliverableService deliverables) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<DeliverableSummaryDto>> List([FromQuery] DeliverableListQuery query, CancellationToken ct) => deliverables.ListAsync(query, ct);

    [HttpPost]
    [HasPermission(Permissions.DeliverablesSubmit)]
    [ProducesResponseType(typeof(DeliverableDetailDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateDeliverableRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await deliverables.CreateAsync(request, ct));

    [HttpGet("{id:guid}")]
    public Task<DeliverableDetailDto> Get(Guid id, CancellationToken ct) => deliverables.GetAsync(id, ct);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    public Task<DeliverableDetailDto> Update(Guid id, UpdateDeliverableRequest request, CancellationToken ct) => deliverables.UpdateAsync(id, request, ct);

    /// <summary>New version: multipart with a file (PNG/JPEG/WebP/PDF/MP4, max 50 MB) and/or a link and/or text, plus notes.</summary>
    [HttpPost("{id:guid}/versions")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    [RequestSizeLimit(DeliveryFileValidator.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = DeliveryFileValidator.MaxBytes + 1024 * 1024)]
    public Task<DeliverableDetailDto> AddVersion(Guid id, [FromForm] DeliverableVersionForm form, CancellationToken ct) =>
        deliverables.AddVersionAsync(id, form, ct);

    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    public Task<DeliverableDetailDto> Submit(Guid id, DeliverableActionRequest request, CancellationToken ct) => deliverables.SubmitAsync(id, request, ct);

    /// <summary>Internal approval: sends the version to the client for review.</summary>
    [HttpPost("{id:guid}/internal-approve")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    public Task<DeliverableDetailDto> InternalApprove(Guid id, DeliverableActionRequest request, CancellationToken ct) =>
        deliverables.InternalApproveAsync(id, request, ct);

    [HttpPost("{id:guid}/internal-request-changes")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    public Task<DeliverableDetailDto> InternalRequestChanges(Guid id, DeliverableActionRequest request, CancellationToken ct) =>
        deliverables.InternalRequestChangesAsync(id, request, ct);

    [HttpPost("{id:guid}/publish")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<DeliverableDetailDto> Publish(Guid id, DeliverableActionRequest request, CancellationToken ct) => deliverables.PublishAsync(id, request, ct);

    [HttpPost("{id:guid}/comments")]
    public Task<DeliverableDetailDto> Comment(Guid id, DeliverableCommentRequest request, CancellationToken ct) =>
        deliverables.StaffCommentAsync(id, request, ct);
}

[ApiController]
[HasPermission(Permissions.TimeTrack)]
[Route("api/v1/agency/time")]
public sealed class AgencyTimeController(TimeService time) : ControllerBase
{
    [HttpGet("timer")]
    public async Task<IActionResult> Timer(CancellationToken ct) => Ok(await time.RunningAsync(ct));

    /// <summary>Starts a timer (409 time.timer_running when one is already running).</summary>
    [HttpPost("timer/start")]
    public Task<TimeEntryDto> Start(StartTimerRequest request, CancellationToken ct) => time.StartAsync(request, ct);

    [HttpPost("timer/stop")]
    public Task<TimeEntryDto> Stop(CancellationToken ct) => time.StopAsync(ct);

    /// <summary>My entries (default: this week). With time.view_all: <c>userId</c>, <c>projectId</c> or <c>clientId</c>.</summary>
    [HttpGet("entries")]
    public Task<IReadOnlyList<TimeEntryDto>> Entries([FromQuery] TimeEntryQuery query, CancellationToken ct) => time.ListAsync(query, ct);

    [HttpPost("entries")]
    public async Task<IActionResult> Create(TimeEntryRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await time.CreateAsync(request, ct));

    [HttpPut("entries/{id:guid}")]
    public Task<TimeEntryDto> Update(Guid id, TimeEntryRequest request, CancellationToken ct) => time.UpdateAsync(id, request, ct);

    [HttpDelete("entries/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await time.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("entries/export.csv")]
    public Task<FileContentResult> Export([FromQuery] TimeEntryQuery query, CancellationToken ct) => time.ExportCsvAsync(query, ct);

    /// <summary>The week containing <c>date</c> (default today) for me, or <c>userId</c> (managers).</summary>
    [HttpGet("timesheets/week")]
    public Task<TimesheetDto> Week([FromQuery] DateOnly? date, [FromQuery] Guid? userId, CancellationToken ct) =>
        time.WeekAsync(userId, date ?? DateOnly.FromDateTime(DateTime.UtcNow), ct);

    [HttpPost("timesheets/submit")]
    public Task<TimesheetDto> Submit([FromQuery] DateOnly date, CancellationToken ct) => time.SubmitAsync(date, ct);

    [HttpGet("timesheets/pending")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<IReadOnlyList<TimesheetDto>> Pending(CancellationToken ct) => time.PendingAsync(ct);

    [HttpPost("timesheets/{id:guid}/approve")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<TimesheetDto> Approve(Guid id, TimesheetDecisionRequest request, CancellationToken ct) => time.DecideAsync(id, true, request, ct);

    [HttpPost("timesheets/{id:guid}/reject")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<TimesheetDto> Reject(Guid id, TimesheetDecisionRequest request, CancellationToken ct) => time.DecideAsync(id, false, request, ct);

    [HttpGet("utilization")]
    [HasPermission(Permissions.TimeViewAll)]
    public Task<UtilizationDto> Utilization([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) => time.UtilizationAsync(from, to, ct);

    [HttpGet("rates")]
    [HasPermission(Permissions.TimeViewAll)]
    public Task<IReadOnlyList<HourlyRateDto>> Rates(CancellationToken ct) => time.RatesAsync(ct);

    [HttpPut("rates")]
    [HasPermission(Permissions.ProjectsManage)]
    [HasPermission(Permissions.TimeViewAll)]
    public Task<IReadOnlyList<HourlyRateDto>> SaveRate(HourlyRateRequest request, CancellationToken ct) => time.SaveRateAsync(request, ct);

    [HttpDelete("rates/{id:guid}")]
    [HasPermission(Permissions.ProjectsManage)]
    [HasPermission(Permissions.TimeViewAll)]
    public Task<IReadOnlyList<HourlyRateDto>> DeleteRate(Guid id, CancellationToken ct) => time.DeleteRateAsync(id, ct);
}

[ApiController]
[HasPermission(Permissions.ReportsManage)]
[Route("api/v1/agency/reports")]
public sealed class AgencyReportsController(ReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ReportSummaryDto>> List([FromQuery] ReportListQuery query, CancellationToken ct) => reports.ListAsync(query, ct);

    /// <summary>Registered KPI section providers (<see cref="IClientReportSection"/>).</summary>
    [HttpGet("providers")]
    public IReadOnlyList<ReportProviderDto> Providers() => reports.Providers;

    [HttpPost]
    [ProducesResponseType(typeof(ReportDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateReportRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await reports.CreateAsync(request, ct));

    [HttpGet("{id:guid}")]
    public Task<ReportDto> Get(Guid id, CancellationToken ct) => reports.GetAsync(id, ct);

    [HttpPut("{id:guid}")]
    public Task<ReportDto> Update(Guid id, UpdateReportRequest request, CancellationToken ct) => reports.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/refresh-section")]
    public Task<ReportDto> Refresh(Guid id, RefreshSectionRequest request, CancellationToken ct) => reports.RefreshSectionAsync(id, request, ct);

    /// <summary>Publishes to the client portal and notifies the client's users.</summary>
    [HttpPost("{id:guid}/publish")]
    public Task<ReportDto> Publish(Guid id, PublishReportRequest request, CancellationToken ct) => reports.PublishAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await reports.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[HasPermission(Permissions.ClientsView)]
[Route("api/v1/agency")]
public sealed class AgencyCommunicationController(CommunicationService communication) : ControllerBase
{
    [HttpGet("briefs")]
    public Task<IReadOnlyList<BriefDto>> Briefs([FromQuery] Guid? clientId, [FromQuery] BriefStatus? status, CancellationToken ct) =>
        communication.BriefsAsync(clientId, status, ct);

    [HttpPost("briefs")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    public async Task<IActionResult> CreateBrief(SubmitBriefRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await communication.SubmitBriefAsync(
            request.ClientId ?? throw DeliveryRules.Invalid("brief.client_required", "clientId", "Choose a client."), request, false, ct));

    [HttpGet("briefs/{id:guid}")]
    public Task<BriefDto> Brief(Guid id, CancellationToken ct) => communication.BriefAsync(id, null, ct);

    [HttpPost("briefs/{id:guid}/status")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<BriefDto> BriefStatus(Guid id, BriefStatusRequest request, CancellationToken ct) => communication.SetBriefStatusAsync(id, request, ct);

    /// <summary>Converts a brief into tasks and deliverables on a project.</summary>
    [HttpPost("briefs/{id:guid}/convert")]
    [HasPermission(Permissions.ProjectsManage)]
    public Task<BriefDto> Convert(Guid id, ConvertBriefRequest request, CancellationToken ct) => communication.ConvertBriefAsync(id, request, ct);

    [HttpGet("clients/{clientId:guid}/threads")]
    public Task<IReadOnlyList<ThreadSummaryDto>> Threads(Guid clientId, CancellationToken ct) => communication.ThreadsAsync(clientId, ct);

    [HttpPost("clients/{clientId:guid}/threads")]
    public async Task<IActionResult> NewThread(Guid clientId, NewThreadRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await communication.NewThreadAsync(clientId, request, ct));

    [HttpGet("clients/{clientId:guid}/threads/{threadId:guid}")]
    public Task<ThreadDto> Thread(Guid clientId, Guid threadId, CancellationToken ct) => communication.ThreadAsync(clientId, threadId, ct);

    [HttpPost("clients/{clientId:guid}/threads/{threadId:guid}/messages")]
    public Task<ThreadDto> Reply(Guid clientId, Guid threadId, NewMessageRequest request, CancellationToken ct) =>
        communication.ReplyAsync(clientId, threadId, request, ct);

    [HttpGet("meetings")]
    public Task<IReadOnlyList<MeetingDto>> Meetings([FromQuery] MeetingQuery query, CancellationToken ct) => communication.MeetingsAsync(query, ct);

    [HttpPost("meetings")]
    [HasPermission(Permissions.ProjectsView)]
    public async Task<IActionResult> CreateMeeting(MeetingRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await communication.SaveMeetingAsync(null, request, ct));

    [HttpPut("meetings/{id:guid}")]
    [HasPermission(Permissions.ProjectsView)]
    public Task<MeetingDto> UpdateMeeting(Guid id, MeetingRequest request, CancellationToken ct) => communication.SaveMeetingAsync(id, request, ct);

    /// <summary>Turns a meeting action item into a task.</summary>
    [HttpPost("meetings/{id:guid}/action-items/{itemId:guid}/convert")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    public Task<MeetingDto> ConvertActionItem(Guid id, Guid itemId, ConvertActionItemRequest request, CancellationToken ct) =>
        communication.ConvertActionItemAsync(id, itemId, request, ct);
}

/// <summary>Client portal: projects, approvals, briefs, reports, messages and meetings of one organization.</summary>
[ApiController]
[HasPermission(Permissions.ClientPortal)]
[Route("api/v1/client/orgs/{clientId:guid}")]
public sealed class ClientPortalDeliveryController(
    ClientPortalService portal, DeliverableService deliverables, ReportService reports, CommunicationService communication, ProjectService projects)
    : ControllerBase
{
    [HttpGet("home")]
    public Task<ClientHomeDto> Home(Guid clientId, CancellationToken ct) => portal.HomeAsync(clientId, ct);

    [HttpGet("projects")]
    public Task<IReadOnlyList<ClientProjectSummaryDto>> Projects(Guid clientId, CancellationToken ct) => portal.ProjectsAsync(clientId, ct);

    [HttpGet("projects/{projectId:guid}")]
    public Task<ClientProjectDetailDto> Project(Guid clientId, Guid projectId, CancellationToken ct) => portal.ProjectAsync(clientId, projectId, ct);

    /// <summary><c>view</c>: awaiting (my review), approved, or all.</summary>
    [HttpGet("deliverables")]
    public Task<IReadOnlyList<DeliverableSummaryDto>> Deliverables(Guid clientId, [FromQuery] string? view, CancellationToken ct) =>
        deliverables.ClientListAsync(clientId, view, ct);

    [HttpGet("deliverables/{id:guid}")]
    public Task<DeliverableDetailDto> Deliverable(Guid clientId, Guid id, CancellationToken ct) => deliverables.ClientGetAsync(clientId, id, ct);

    /// <summary>Approve the reviewed version (Approver/Owner duty). 409 deliverable.stale_version when a newer version exists.</summary>
    [HttpPost("deliverables/{id:guid}/approve")]
    public Task<DeliverableDetailDto> Approve(Guid clientId, Guid id, DeliverableActionRequest request, CancellationToken ct) =>
        deliverables.ClientApproveAsync(clientId, id, request, ct);

    [HttpPost("deliverables/{id:guid}/request-changes")]
    public Task<DeliverableDetailDto> RequestChanges(Guid clientId, Guid id, DeliverableActionRequest request, CancellationToken ct) =>
        deliverables.ClientRequestChangesAsync(clientId, id, request, ct);

    [HttpPost("deliverables/{id:guid}/comments")]
    public Task<DeliverableDetailDto> Comment(Guid clientId, Guid id, DeliverableCommentRequest request, CancellationToken ct) =>
        deliverables.ClientCommentAsync(clientId, id, request, ct);

    [HttpGet("reports")]
    public Task<IReadOnlyList<ReportSummaryDto>> Reports(Guid clientId, CancellationToken ct) => reports.ClientListAsync(clientId, ct);

    [HttpGet("reports/{id:guid}")]
    public Task<ReportDto> Report(Guid clientId, Guid id, CancellationToken ct) => reports.ClientGetAsync(clientId, id, ct);

    [HttpGet("brief-templates")]
    public Task<IReadOnlyList<BriefTemplateDto>> BriefTemplates(Guid clientId, CancellationToken ct) => projects.BriefTemplatesAsync(ct);

    [HttpGet("briefs")]
    public Task<IReadOnlyList<BriefDto>> Briefs(Guid clientId, CancellationToken ct) => communication.BriefsAsync(clientId, null, ct);

    /// <summary>Submit a brief (Approver/Owner duty).</summary>
    [HttpPost("briefs")]
    public async Task<IActionResult> SubmitBrief(Guid clientId, SubmitBriefRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await communication.SubmitBriefAsync(clientId, request, true, ct));

    [HttpGet("briefs/{id:guid}")]
    public Task<BriefDto> Brief(Guid clientId, Guid id, CancellationToken ct) => communication.BriefAsync(id, clientId, ct);

    [HttpGet("threads")]
    public Task<IReadOnlyList<ThreadSummaryDto>> Threads(Guid clientId, CancellationToken ct) => communication.ThreadsAsync(clientId, ct);

    [HttpPost("threads")]
    public async Task<IActionResult> NewThread(Guid clientId, NewThreadRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await communication.NewThreadAsync(clientId, request, ct));

    [HttpGet("threads/{threadId:guid}")]
    public Task<ThreadDto> Thread(Guid clientId, Guid threadId, CancellationToken ct) => communication.ThreadAsync(clientId, threadId, ct);

    [HttpPost("threads/{threadId:guid}/messages")]
    public Task<ThreadDto> Reply(Guid clientId, Guid threadId, NewMessageRequest request, CancellationToken ct) =>
        communication.ReplyAsync(clientId, threadId, request, ct);

    [HttpGet("meetings")]
    public Task<IReadOnlyList<MeetingDto>> Meetings(Guid clientId, CancellationToken ct) =>
        communication.MeetingsAsync(new MeetingQuery { ClientId = clientId }, ct);
}
