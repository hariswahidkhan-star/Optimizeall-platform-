using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.EmailMarketing.Audiences;
using OptimizeAll.Api.Modules.EmailMarketing.Automations;
using OptimizeAll.Api.Modules.EmailMarketing.Campaigns;
using OptimizeAll.Api.Modules.EmailMarketing.Reporting;
using OptimizeAll.Api.Modules.EmailMarketing.Segments;
using OptimizeAll.Api.Modules.EmailMarketing.Settings;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Api.Modules.EmailMarketing.Templates;
using OptimizeAll.Api.Modules.EmailMarketing.Tracking;
using OptimizeAll.Domain.EmailMarketing;

namespace OptimizeAll.Api.Modules.EmailMarketing;

internal static class Channels
{
    public static readonly IReadOnlyCollection<MessageChannel> Email = new[] { MessageChannel.Email };
    public static readonly IReadOnlyCollection<MessageChannel> Messaging = new[] { MessageChannel.Sms, MessageChannel.WhatsApp };
}

/// <summary>Audiences, segments, templates, settings and reporting for the agency's email programs (<c>email.manage</c>).</summary>
[ApiController]
[Route("api/v1/agency/email")]
[HasPermission(Permissions.EmailManage)]
public sealed class EmailAudienceController(
    AudienceService audiences,
    ImportService imports,
    SegmentService segments,
    TemplateService templates,
    ReportService reports,
    EmailSettingsService settings,
    SignalsService signals,
    EmailAccess access) : ControllerBase
{
    // ----- Workspaces & overview -----

    [HttpGet("workspaces")]
    public Task<IReadOnlyList<WorkspaceDto>> Workspaces(CancellationToken ct) => audiences.WorkspacesAsync(ct);

    [HttpGet("overview")]
    public Task<OverviewDto> Overview([FromQuery] Guid? clientId, CancellationToken ct) => reports.OverviewAsync(clientId, ct);

    /// <summary>Email/SMS KPIs of a client for a period (for client reports).</summary>
    [HttpGet("clients/{id:guid}/kpis")]
    public Task<EmailKpis> Kpis(Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) => reports.KpisAsync(id, from, to, ct);

    [HttpGet("kpis")]
    public Task<EmailKpis> WorkspaceKpis([FromQuery] Guid? clientId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) =>
        reports.KpisAsync(clientId, from, to, ct);

    // ----- Lists -----

    [HttpGet("lists")]
    public Task<IReadOnlyList<EmailListDto>> Lists([FromQuery] Guid? clientId, [FromQuery] bool includeArchived, CancellationToken ct) =>
        audiences.ListsAsync(clientId, includeArchived, ct);

    [HttpPost("lists")]
    public async Task<ActionResult<EmailListDto>> CreateList(EmailListRequest request, CancellationToken ct)
    {
        var list = await audiences.CreateListAsync(request, ct);
        return CreatedAtAction(nameof(GetList), new { id = list.Id }, list);
    }

    [HttpGet("lists/{id:guid}")]
    public Task<EmailListDto> GetList(Guid id, CancellationToken ct) => audiences.GetListAsync(id, ct);

    [HttpPut("lists/{id:guid}")]
    public Task<EmailListDto> UpdateList(Guid id, EmailListRequest request, CancellationToken ct) => audiences.UpdateListAsync(id, request, ct);

    [HttpDelete("lists/{id:guid}")]
    public async Task<IActionResult> ArchiveList(Guid id, CancellationToken ct)
    {
        await audiences.ArchiveListAsync(id, ct);
        return NoContent();
    }

    [HttpGet("lists/{id:guid}/health")]
    public Task<ListHealth> ListHealth(Guid id, [FromQuery] int days = 90, CancellationToken ct = default) => reports.ListHealthAsync(id, days, ct);

    [HttpGet("lists/{id:guid}/export.csv")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var (name, header, rows) = await audiences.ExportListAsync(id, ct);
        return Csv.File(name, header, rows);
    }

    [HttpPost("lists/{id:guid}/imports/preview")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public Task<ImportPreview> PreviewImport(Guid id, ImportPreviewRequest request, CancellationToken ct) => imports.PreviewAsync(id, request, ct);

    [HttpPost("lists/{id:guid}/imports")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<ImportDto>> StartImport(Guid id, ImportRequest request, CancellationToken ct)
    {
        var import = await imports.StartAsync(id, request, ct);
        return import.Status == ImportStatus.Completed ? Ok(import) : Accepted(import);
    }

    [HttpGet("lists/{id:guid}/imports")]
    public Task<IReadOnlyList<ImportDto>> ListImports(Guid id, CancellationToken ct) => imports.ForListAsync(id, ct);

    [HttpGet("imports/{id:guid}")]
    public Task<ImportDto> GetImport(Guid id, CancellationToken ct) => imports.GetAsync(id, ct);

    // ----- Subscribers -----

    [HttpGet("subscribers")]
    public Task<PagedResult<SubscriberListItem>> Subscribers([FromQuery] SubscriberQuery query, CancellationToken ct) => audiences.SubscribersAsync(query, ct);

    [HttpPost("subscribers")]
    public Task<SubscriberDetail> CreateSubscriber(SubscriberRequest request, CancellationToken ct) => audiences.CreateSubscriberAsync(request, ct);

    [HttpGet("subscribers/{id:guid}")]
    public Task<SubscriberDetail> GetSubscriber(Guid id, CancellationToken ct) => audiences.GetSubscriberAsync(id, ct);

    [HttpPut("subscribers/{id:guid}")]
    public Task<SubscriberDetail> UpdateSubscriber(Guid id, SubscriberRequest request, CancellationToken ct) => audiences.UpdateSubscriberAsync(id, request, ct);

    /// <summary>Erases the contact and their history (GDPR erasure).</summary>
    [HttpDelete("subscribers/{id:guid}")]
    public async Task<IActionResult> EraseSubscriber(Guid id, CancellationToken ct)
    {
        await audiences.EraseSubscriberAsync(id, ct);
        return NoContent();
    }

    [HttpPost("subscribers/{id:guid}/tags")]
    public Task<SubscriberDetail> Tags(Guid id, TagChangeRequest request, CancellationToken ct) => audiences.ChangeTagsAsync(id, request, ct);

    [HttpPost("subscribers/{id:guid}/consent")]
    public Task<SubscriberDetail> Consent(Guid id, ConsentChangeRequest request, CancellationToken ct) => audiences.ChangeConsentAsync(id, request, ct);

    [HttpPost("subscribers/{id:guid}/lists")]
    public Task<SubscriberDetail> Membership(Guid id, MembershipChangeRequest request, CancellationToken ct) => audiences.ChangeMembershipAsync(id, request, ct);

    // ----- Suppression -----

    [HttpGet("suppressions")]
    public Task<PagedResult<SuppressionDto>> Suppressions([FromQuery] SuppressionQuery query, CancellationToken ct) => audiences.SuppressionsAsync(query, ct);

    [HttpPost("suppressions")]
    public Task<SuppressionDto> AddSuppression(SuppressionRequest request, CancellationToken ct) => audiences.AddManualSuppressionAsync(request, ct);

    [HttpDelete("suppressions/{id:guid}")]
    public async Task<IActionResult> RemoveSuppression(Guid id, [FromQuery, Required, MaxLength(500)] string reason, CancellationToken ct)
    {
        await audiences.RemoveSuppressionAsync(id, reason, ct);
        return NoContent();
    }

    /// <summary>Manual bounce/complaint import (e.g. from an ESP without webhooks).</summary>
    [HttpPost("suppressions/bounces")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public Task<BounceImportResult> ImportBounces(BounceImportRequest request, CancellationToken ct) => audiences.ImportBouncesAsync(request, ct);

    // ----- Segments -----

    [HttpGet("segments")]
    public Task<IReadOnlyList<SegmentDto>> Segments([FromQuery] Guid? clientId, CancellationToken ct) => segments.ListAsync(clientId, ct);

    [HttpPost("segments")]
    public Task<SegmentDto> CreateSegment(SegmentRequest request, CancellationToken ct) => segments.CreateAsync(request, ct);

    [HttpGet("segments/{id:guid}")]
    public Task<SegmentDto> GetSegment(Guid id, CancellationToken ct) => segments.GetAsync(id, ct);

    [HttpPut("segments/{id:guid}")]
    public Task<SegmentDto> UpdateSegment(Guid id, SegmentRequest request, CancellationToken ct) => segments.UpdateAsync(id, request, ct);

    [HttpDelete("segments/{id:guid}")]
    public async Task<IActionResult> DeleteSegment(Guid id, CancellationToken ct)
    {
        await segments.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Live count of unsaved rules.</summary>
    [HttpPost("segments/preview")]
    public Task<SegmentPreview> PreviewSegment(SegmentPreviewRequest request, CancellationToken ct) => segments.PreviewAsync(request, ct);

    // ----- Templates -----

    [HttpGet("templates")]
    public Task<IReadOnlyList<TemplateListItem>> Templates([FromQuery] Guid? clientId, [FromQuery] bool includeGlobal = true, CancellationToken ct = default) =>
        templates.ListAsync(clientId, includeGlobal, ct);

    [HttpPost("templates")]
    public Task<TemplateDto> CreateTemplate(TemplateRequest request, CancellationToken ct) => templates.CreateAsync(request, ct);

    [HttpGet("templates/{id:guid}")]
    public Task<TemplateDto> GetTemplate(Guid id, CancellationToken ct) => templates.GetAsync(id, ct);

    [HttpPut("templates/{id:guid}")]
    public Task<TemplateDto> UpdateTemplate(Guid id, TemplateRequest request, CancellationToken ct) => templates.UpdateAsync(id, request, ct);

    [HttpDelete("templates/{id:guid}")]
    public async Task<IActionResult> ArchiveTemplate(Guid id, CancellationToken ct)
    {
        await templates.ArchiveAsync(id, ct);
        return NoContent();
    }

    [HttpPost("templates/{id:guid}/duplicate")]
    public Task<TemplateDto> DuplicateTemplate(Guid id, [FromQuery] Guid? clientId, CancellationToken ct) => templates.DuplicateAsync(id, clientId, ct);

    /// <summary>Server-side render of unsaved content (editor preview) with sample merge data.</summary>
    [HttpPost("templates/render")]
    public Task<RenderResult> Render(RenderRequest request, CancellationToken ct) => templates.RenderAsync(request, ct);

    [HttpPost("templates/{id:guid}/test")]
    [EnableRateLimiting(RateLimitPolicies.Submissions)]
    public Task<TestSendResult> TestTemplate(Guid id, TestSendRequest request, CancellationToken ct) => templates.TestSendTemplateAsync(id, request, ct);

    // ----- Custom events (staff API) -----

    [HttpPost("events")]
    public async Task<ActionResult<CustomEventResult>> Event(CustomEventRequest request, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(request.ClientAccountId, ct);
        var result = await signals.RecordEventAsync(request, ct);
        return result.Accepted ? Ok(result) : Accepted(result);
    }

    // ----- Settings & senders -----

    [HttpGet("settings")]
    public Task<WorkspaceSettingsDto> Settings([FromQuery] Guid? clientId, CancellationToken ct) => settings.GetAsync(clientId, ct);

    [HttpPut("settings")]
    public Task<WorkspaceSettingsDto> UpdateSettings(WorkspaceSettingsRequest request, CancellationToken ct) => settings.UpdateAsync(request, ct);

    /// <summary>Choose the email service provider (sensitive).</summary>
    [DeniedWhileImpersonating] // email provider (integration)
    [HttpPut("settings/provider")]
    [HasPermission(Permissions.IntegrationsManage)]
    public Task<WorkspaceSettingsDto> ChooseProvider(ProviderChoiceRequest request, CancellationToken ct) => settings.ChooseProviderAsync(request, ct);

    [HttpGet("senders")]
    public Task<IReadOnlyList<SenderProfileDto>> Senders([FromQuery] Guid? clientId, CancellationToken ct) => settings.SendersAsync(clientId, ct);

    [HttpPost("senders")]
    public Task<SenderProfileDto> CreateSender(SenderProfileRequest request, CancellationToken ct) => settings.CreateSenderAsync(request, ct);

    [HttpPut("senders/{id:guid}")]
    public Task<SenderProfileDto> UpdateSender(Guid id, SenderProfileRequest request, CancellationToken ct) => settings.UpdateSenderAsync(id, request, ct);

    [HttpDelete("senders/{id:guid}")]
    public async Task<IActionResult> DeleteSender(Guid id, CancellationToken ct)
    {
        await settings.DeleteSenderAsync(id, ct);
        return NoContent();
    }

    [HttpPost("senders/{id:guid}/send-verification")]
    [EnableRateLimiting(RateLimitPolicies.Submissions)]
    public Task<SenderProfileDto> SendVerification(Guid id, CancellationToken ct) => settings.SendVerificationAsync(id, ct);

    [HttpPost("senders/{id:guid}/verify")]
    [EnableRateLimiting(RateLimitPolicies.Submissions)]
    public Task<SenderProfileDto> Verify(Guid id, VerifySenderRequest request, CancellationToken ct) => settings.VerifyAsync(id, request, ct);
}

/// <summary>Email campaigns. Authoring needs <c>email.manage</c>; send/schedule/pause/resume/cancel also need <c>email.send</c>.</summary>
[ApiController]
[Route("api/v1/agency/email/campaigns")]
[HasPermission(Permissions.EmailManage)]
public sealed class EmailCampaignsController(CampaignService campaigns, ReportService reports) : CampaignActions(campaigns, reports, Channels.Email);

/// <summary>SMS and WhatsApp campaigns (<c>sms.manage</c>; sending also needs <c>email.send</c>).</summary>
[ApiController]
[Route("api/v1/agency/email/sms/campaigns")]
[HasPermission(Permissions.SmsManage)]
public sealed class SmsCampaignsController(CampaignService campaigns, ReportService reports) : CampaignActions(campaigns, reports, Channels.Messaging)
{
    /// <summary>Segment count (GSM-7 vs UCS-2) and cost for an SMS text.</summary>
    [HttpPost("segments")]
    public SmsSegmentResult Segments(SmsSegmentRequest request) =>
        SmsSegmentResult.From(SmsSegments.Calculate(request.Text), request.Recipients, request.CostPerSegment);
}

public sealed class SmsSegmentRequest
{
    [MaxLength(1600)] public string Text { get; set; } = string.Empty;
    [Range(0, 10_000_000)] public int Recipients { get; set; }
    [Range(0, 10)] public decimal CostPerSegment { get; set; }
}

public sealed record SmsSegmentResult(string Encoding, int Characters, int Segments, int PerSegment, int Remaining, decimal EstimatedCost)
{
    public static SmsSegmentResult From(SmsSegmentInfo i, int recipients, decimal cost) =>
        new(i.Encoding.ToString(), i.Units, i.Segments, i.PerSegment, i.Remaining, Math.Round(i.Segments * (decimal)recipients * cost, 4));
}

/// <summary>Shared campaign endpoints, restricted to the controller's channels.</summary>
public abstract class CampaignActions(CampaignService campaigns, ReportService reports, IReadOnlyCollection<MessageChannel> channels) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CampaignListItem>> List([FromQuery] CampaignQuery query, CancellationToken ct) => campaigns.ListAsync(query, channels, ct);

    [HttpPost]
    public Task<CampaignDto> Create(CampaignRequest request, CancellationToken ct) => campaigns.CreateAsync(request, channels, ct);

    [HttpGet("{id:guid}")]
    public Task<CampaignDto> Get(Guid id, CancellationToken ct) => campaigns.GetAsync(id, channels, ct);

    [HttpPut("{id:guid}")]
    public Task<CampaignDto> Update(Guid id, CampaignRequest request, CancellationToken ct) => campaigns.UpdateAsync(id, request, channels, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await campaigns.DeleteAsync(id, channels, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/duplicate")]
    public Task<CampaignDto> Duplicate(Guid id, CancellationToken ct) => campaigns.DuplicateAsync(id, channels, ct);

    [HttpGet("{id:guid}/checklist")]
    public Task<Checklist> Checklist(Guid id, CancellationToken ct) => campaigns.ChecklistAsync(id, channels, ct);

    [HttpGet("{id:guid}/preview")]
    public Task<RenderResult> Preview(Guid id, [FromQuery] string? variant, CancellationToken ct) => campaigns.PreviewAsync(id, variant, channels, ct);

    [HttpPost("{id:guid}/test")]
    [EnableRateLimiting(RateLimitPolicies.Submissions)]
    public Task<TestSendResult> Test(Guid id, TestSendRequest request, CancellationToken ct) => campaigns.TestSendAsync(id, request, channels, ct);

    [HttpGet("{id:guid}/report")]
    public Task<CampaignReport> Report(Guid id, CancellationToken ct) => reports.CampaignReportAsync(id, channels, ct);

    /// <summary>Sensitive: confirm the send (typed campaign name). The send job does the sending.</summary>
    [HttpPost("{id:guid}/send")]
    [HasPermission(Permissions.EmailSend)]
    public Task<CampaignDto> Send(Guid id, SendCampaignRequest request, CancellationToken ct) => campaigns.SendAsync(id, request, channels, ct);

    [HttpPost("{id:guid}/unschedule")]
    [HasPermission(Permissions.EmailSend)]
    public Task<CampaignDto> Unschedule(Guid id, CampaignActionRequest request, CancellationToken ct) => campaigns.UnscheduleAsync(id, request, channels, ct);

    [HttpPost("{id:guid}/pause")]
    [HasPermission(Permissions.EmailSend)]
    public Task<CampaignDto> Pause(Guid id, CampaignActionRequest request, CancellationToken ct) => campaigns.PauseAsync(id, request, channels, ct);

    [HttpPost("{id:guid}/resume")]
    [HasPermission(Permissions.EmailSend)]
    public Task<CampaignDto> Resume(Guid id, CampaignActionRequest request, CancellationToken ct) => campaigns.ResumeAsync(id, request, channels, ct);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.EmailSend)]
    public Task<CampaignDto> Cancel(Guid id, CampaignActionRequest request, CancellationToken ct) => campaigns.CancelAsync(id, request, channels, ct);
}

/// <summary>Journeys (<c>email.manage</c>).</summary>
[ApiController]
[Route("api/v1/agency/email/automations")]
[HasPermission(Permissions.EmailManage)]
public sealed class EmailAutomationsController(AutomationService automations) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AutomationListItem>> List([FromQuery] Guid? clientId, CancellationToken ct) => automations.ListAsync(clientId, ct);

    [HttpPost]
    public Task<AutomationDto> Create(AutomationRequest request, CancellationToken ct) => automations.CreateAsync(request, ct);

    [HttpGet("{id:guid}")]
    public Task<AutomationDto> Get(Guid id, CancellationToken ct) => automations.GetAsync(id, ct);

    [HttpPut("{id:guid}")]
    public Task<AutomationDto> Update(Guid id, AutomationRequest request, CancellationToken ct) => automations.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/activate")]
    public Task<AutomationDto> Activate(Guid id, CancellationToken ct) => automations.SetStatusAsync(id, AutomationStatus.Active, ct);

    [HttpPost("{id:guid}/pause")]
    public Task<AutomationDto> Pause(Guid id, CancellationToken ct) => automations.SetStatusAsync(id, AutomationStatus.Paused, ct);

    [HttpPost("{id:guid}/archive")]
    public Task<AutomationDto> Archive(Guid id, CancellationToken ct) => automations.SetStatusAsync(id, AutomationStatus.Archived, ct);

    [HttpGet("{id:guid}/enrollments")]
    public Task<IReadOnlyList<EnrollmentDto>> Enrollments(Guid id, CancellationToken ct) => automations.EnrollmentsAsync(id, ct);

    [HttpPost("{id:guid}/enroll")]
    public async Task<IActionResult> Enroll(Guid id, ManualEnrollRequest request, CancellationToken ct) =>
        Ok(new { enrolled = await automations.EnrollAsync(id, request, ct) });
}
