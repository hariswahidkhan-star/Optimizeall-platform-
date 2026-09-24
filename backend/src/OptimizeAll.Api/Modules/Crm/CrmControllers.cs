using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Crm;

[ApiController]
[Route("api/v1/agency/crm")]
[HasPermission(Permissions.CrmView)]
public sealed class CrmController(CrmService crm, ContactImportService import, LeadScoringService scoring, CrmOptionsService options) : ControllerBase
{
    /// <summary>Agency-editable option lists (lost reasons, budget ranges, industries).</summary>
    [HttpGet("options")]
    public Task<CrmOptionsDto> Options(CancellationToken ct) => options.GetAsync(ct);

    [HttpPut("options")]
    [HasPermission(Permissions.CrmManage)]
    public Task<CrmOptionsDto> UpdateOptions(CrmOptionsRequest r, CancellationToken ct) => options.SaveAsync(r, ct);

    [HttpGet("dashboard")]
    public Task<CrmDashboardDto> Dashboard(CancellationToken ct) => crm.DashboardAsync(ct);

    /// <summary>Team members who can own deals and be assigned CRM tasks.</summary>
    [HttpGet("assignees")]
    public Task<IReadOnlyList<UserRefDto>> Assignees(CancellationToken ct) => crm.AssigneesAsync(ct);

    // ---------------- Companies

    [HttpGet("companies")]
    public Task<PagedResult<CompanySummaryDto>> Companies([FromQuery] CompanyQuery q, CancellationToken ct) => crm.ListCompaniesAsync(q, ct);

    [HttpGet("companies/{id:guid}")]
    public Task<CompanyDto> Company(Guid id, CancellationToken ct) => crm.GetCompanyAsync(id, ct);

    [HttpPost("companies")]
    [HasPermission(Permissions.CrmManage)]
    [ProducesResponseType(typeof(CompanyDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCompany(CompanyRequest r, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await crm.CreateCompanyAsync(r, ct));

    [HttpPut("companies/{id:guid}")]
    [HasPermission(Permissions.CrmManage)]
    public Task<CompanyDto> UpdateCompany(Guid id, CompanyRequest r, CancellationToken ct) => crm.UpdateCompanyAsync(id, r, ct);

    /// <summary>Hides the company from lists and pickers (history kept; restorable). A client company can't be archived.</summary>
    [HttpPost("companies/{id:guid}/archive")]
    [HasPermission(Permissions.CrmManage)]
    public Task<CompanyDto> ArchiveCompany(Guid id, StampOnly r, CancellationToken ct) => crm.ArchiveCompanyAsync(id, true, r, ct);

    [HttpPost("companies/{id:guid}/restore")]
    [HasPermission(Permissions.CrmManage)]
    public Task<CompanyDto> RestoreCompany(Guid id, StampOnly r, CancellationToken ct) => crm.ArchiveCompanyAsync(id, false, r, ct);

    /// <summary>archive, restore, assignOwner, addTag or removeTag on up to 200 companies.</summary>
    [HttpPost("companies/bulk")]
    [HasPermission(Permissions.CrmManage)]
    public Task<CrmBulkResultDto> BulkCompanies(CrmBulkRequest r, CancellationToken ct) => crm.BulkCompaniesAsync(r, ct);

    // ---------------- Contacts

    [HttpGet("contacts")]
    public Task<PagedResult<ContactSummaryDto>> Contacts([FromQuery] ContactQuery q, CancellationToken ct) => crm.ListContactsAsync(q, ct);

    [HttpGet("contacts/export.csv")]
    public async Task<FileContentResult> ExportContacts([FromQuery] ContactQuery q, CancellationToken ct)
    {
        var rows = await crm.FilterContacts(q).OrderBy(c => c.CreatedAt).Take(50_000).ToListAsync(ct);
        var summaries = await crm.ContactSummariesAsync(rows, ct);
        return Csv.File($"contacts-{DateTime.UtcNow:yyyyMMdd}.csv",
            new[] { "first_name", "last_name", "email", "phone", "job_title", "company", "lifecycle_stage", "consent", "source", "tags", "score", "owner", "created_at" },
            summaries.Select(c => new object?[] { c.FirstName, c.LastName, c.Email, c.Phone, c.JobTitle, c.CompanyName, c.LifecycleStage.ToString(),
                c.ConsentStatus.ToString(), c.Source, string.Join(';', c.Tags), c.Score, c.Owner?.Email, c.CreatedAt }));
    }

    /// <summary>CSV import (multipart field "file"); validates, dedupes by email and reports a result per row.</summary>
    [HttpPost("contacts/import")]
    [HasPermission(Permissions.CrmManage)]
    [RequestSizeLimit(ContactImportService.MaxBytes + 64 * 1024)]
    // No [FromForm] on the IFormFile itself: [ApiController] infers it, and Swashbuckle rejects the explicit attribute.
    public async Task<ImportResultDto> Import(IFormFile? file, [FromQuery] bool dryRun, [FromQuery] bool updateExisting, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new DomainException("crm.import_empty", "Choose a CSV file to import.");
        if (file.Length > ContactImportService.MaxBytes) throw new DomainException("crm.import_too_large", "The file is larger than 2 MB.");
        await using var stream = file.OpenReadStream();
        return await import.ImportAsync(stream, dryRun, updateExisting, ct);
    }

    [HttpGet("contacts/{id:guid}")]
    public Task<ContactDto> Contact(Guid id, CancellationToken ct) => crm.GetContactAsync(id, ct);

    [HttpPost("contacts")]
    [HasPermission(Permissions.CrmManage)]
    [ProducesResponseType(typeof(ContactDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateContact(ContactRequest r, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await crm.CreateContactAsync(r, ct));

    [HttpPut("contacts/{id:guid}")]
    [HasPermission(Permissions.CrmManage)]
    public Task<ContactDto> UpdateContact(Guid id, ContactRequest r, CancellationToken ct) => crm.UpdateContactAsync(id, r, ct);

    [HttpPost("contacts/{id:guid}/archive")]
    [HasPermission(Permissions.CrmManage)]
    public Task<ContactDto> ArchiveContact(Guid id, StampOnly r, CancellationToken ct) => crm.ArchiveContactAsync(id, true, r, ct);

    [HttpPost("contacts/{id:guid}/restore")]
    [HasPermission(Permissions.CrmManage)]
    public Task<ContactDto> RestoreContact(Guid id, StampOnly r, CancellationToken ct) => crm.ArchiveContactAsync(id, false, r, ct);

    /// <summary>archive, restore, assignOwner, setLifecycle, addTag or removeTag on up to 200 contacts.</summary>
    [HttpPost("contacts/bulk")]
    [HasPermission(Permissions.CrmManage)]
    public Task<CrmBulkResultDto> BulkContacts(CrmBulkRequest r, CancellationToken ct) => crm.BulkContactsAsync(r, ct);

    // ---------------- Pipeline & deals

    [HttpGet("stages")]
    public Task<IReadOnlyList<StageDto>> Stages(CancellationToken ct) => crm.StagesAsync(ct);

    [HttpPut("stages")]
    [HasPermission(Permissions.CrmManage)]
    public Task<IReadOnlyList<StageDto>> UpdateStages(UpdatePipelineRequest r, CancellationToken ct) => crm.UpdatePipelineAsync(r, ct);

    [HttpGet("deals")]
    public Task<PagedResult<DealSummaryDto>> Deals([FromQuery] DealQuery q, CancellationToken ct) => crm.ListDealsAsync(q, ct);

    [HttpGet("deals/board")]
    public Task<BoardDto> Board([FromQuery] DealQuery q, CancellationToken ct) => crm.BoardAsync(q, ct);

    [HttpGet("deals/{id:guid}")]
    public Task<DealDto> Deal(Guid id, CancellationToken ct) => crm.GetDealAsync(id, ct);

    [HttpPost("deals")]
    [HasPermission(Permissions.CrmManage)]
    [ProducesResponseType(typeof(DealDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateDeal(DealRequest r, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await crm.CreateDealAsync(r, ct));

    [HttpPut("deals/{id:guid}")]
    [HasPermission(Permissions.CrmManage)]
    public Task<DealDto> UpdateDeal(Guid id, DealRequest r, CancellationToken ct) => crm.UpdateDealAsync(id, r, ct);

    /// <summary>Archives a deal (not while a proposal on it waits for the client).</summary>
    [HttpPost("deals/{id:guid}/archive")]
    [HasPermission(Permissions.CrmManage)]
    public Task<DealDto> ArchiveDeal(Guid id, StampOnly r, CancellationToken ct) => crm.ArchiveDealAsync(id, true, r, ct);

    [HttpPost("deals/{id:guid}/restore")]
    [HasPermission(Permissions.CrmManage)]
    public Task<DealDto> RestoreDeal(Guid id, StampOnly r, CancellationToken ct) => crm.ArchiveDealAsync(id, false, r, ct);

    /// <summary>archive, restore or assignOwner on up to 200 deals.</summary>
    [HttpPost("deals/bulk")]
    [HasPermission(Permissions.CrmManage)]
    public Task<CrmBulkResultDto> BulkDeals(CrmBulkRequest r, CancellationToken ct) => crm.BulkDealsAsync(r, ct);

    /// <summary>Kanban move (drag or keyboard). Lost requires <c>lostReason</c>.</summary>
    [HttpPost("deals/{id:guid}/move")]
    [HasPermission(Permissions.CrmManage)]
    public Task<DealDto> MoveDeal(Guid id, MoveDealRequest r, CancellationToken ct) => crm.MoveDealAsync(id, r, ct);

    [HttpPost("deals/{id:guid}/contacts")]
    [HasPermission(Permissions.CrmManage)]
    public Task<DealDto> AddDealContact(Guid id, DealContactRequest r, CancellationToken ct) => crm.AddDealContactAsync(id, r, ct);

    [HttpDelete("deals/{id:guid}/contacts/{contactId:guid}")]
    [HasPermission(Permissions.CrmManage)]
    public Task<DealDto> RemoveDealContact(Guid id, Guid contactId, CancellationToken ct) => crm.RemoveDealContactAsync(id, contactId, ct);

    // ---------------- Activities & tasks

    [HttpGet("activities")]
    public Task<PagedResult<ActivityDto>> Activities([FromQuery] ActivityQuery q, CancellationToken ct) => crm.ListActivitiesAsync(q, ct);

    /// <summary>The caller's open tasks (overdue first).</summary>
    [HttpGet("tasks/mine")]
    public Task<PagedResult<ActivityDto>> MyTasks([FromQuery] PageQuery page, [FromQuery] bool includeCompleted, CancellationToken ct) =>
        crm.ListActivitiesAsync(new ActivityQuery
        {
            Assignee = "me", Type = ActivityType.Task, Due = includeCompleted ? null : "open", Page = page.Page, PageSize = page.PageSize,
            Search = page.Search,
        }, ct);

    [HttpPost("activities")]
    [HasPermission(Permissions.CrmManage)]
    [ProducesResponseType(typeof(ActivityDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateActivity(ActivityRequest r, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await crm.CreateActivityAsync(r, ct));

    [HttpPut("activities/{id:guid}")]
    [HasPermission(Permissions.CrmManage)]
    public Task<ActivityDto> UpdateActivity(Guid id, ActivityRequest r, CancellationToken ct) => crm.UpdateActivityAsync(id, r, ct);

    [HttpPost("activities/{id:guid}/complete")]
    [HasPermission(Permissions.CrmManage)]
    public Task<ActivityDto> Complete(Guid id, StampOnly r, CancellationToken ct) => crm.CompleteActivityAsync(id, true, r, ct);

    [HttpPost("activities/{id:guid}/reopen")]
    [HasPermission(Permissions.CrmManage)]
    public Task<ActivityDto> Reopen(Guid id, StampOnly r, CancellationToken ct) => crm.CompleteActivityAsync(id, false, r, ct);

    [HttpDelete("activities/{id:guid}")]
    [HasPermission(Permissions.CrmManage)]
    public async Task<IActionResult> DeleteActivity(Guid id, CancellationToken ct)
    {
        await crm.DeleteActivityAsync(id, ct);
        return NoContent();
    }

    // ---------------- Lead scoring

    [HttpGet("scoring/rules")]
    public Task<IReadOnlyList<ScoringRuleDto>> Rules(CancellationToken ct) => scoring.ListRulesAsync(ct);

    [HttpGet("scoring/meta")]
    public ScoringMetaDto ScoringMeta() => new(LeadScoring.FitFields, LeadScoring.KnownEngagementTypes);

    [HttpPost("scoring/rules")]
    [HasPermission(Permissions.CrmManage)]
    [ProducesResponseType(typeof(ScoringRuleDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateRule(ScoringRuleRequest r, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await scoring.CreateRuleAsync(r, ct));

    [HttpPut("scoring/rules/{id:guid}")]
    [HasPermission(Permissions.CrmManage)]
    public Task<ScoringRuleDto> UpdateRule(Guid id, ScoringRuleRequest r, CancellationToken ct) => scoring.UpdateRuleAsync(id, r, ct);

    [HttpDelete("scoring/rules/{id:guid}")]
    [HasPermission(Permissions.CrmManage)]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken ct)
    {
        await scoring.DeleteRuleAsync(id, ct);
        return NoContent();
    }

    /// <summary>Recomputes every contact's score (after changing rules).</summary>
    [HttpPost("scoring/recompute")]
    [HasPermission(Permissions.CrmManage)]
    public async Task<IActionResult> Recompute(CancellationToken ct) => Ok(new { contacts = await scoring.RecomputeAllAsync(ct) });

    // ---------------- Saved views

    [HttpGet("views")]
    public Task<IReadOnlyList<SavedViewDto>> Views([FromQuery] string? entity, CancellationToken ct) => crm.ViewsAsync(entity, ct);

    [HttpPost("views")]
    [ProducesResponseType(typeof(SavedViewDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateView(SavedViewRequest r, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await crm.CreateViewAsync(r, ct));

    /// <summary>Rename, share/unshare or replace the filters of one of your views.</summary>
    [HttpPut("views/{id:guid}")]
    public Task<SavedViewDto> UpdateView(Guid id, UpdateSavedViewRequest r, CancellationToken ct) => crm.UpdateViewAsync(id, r, ct);

    [HttpDelete("views/{id:guid}")]
    public async Task<IActionResult> DeleteView(Guid id, CancellationToken ct)
    {
        await crm.DeleteViewAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/agency/proposals")]
[HasPermission(Permissions.ProposalsManage)]
public sealed class AgencyProposalsController(ProposalService proposals, InvoiceService invoices) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ProposalSummaryDto>> List([FromQuery] ProposalQuery q, CancellationToken ct) => proposals.ListAsync(q, ct);

    [HttpGet("{id:guid}")]
    public Task<ProposalDto> Get(Guid id, CancellationToken ct) => proposals.GetAsync(id, ct);

    [HttpGet("{id:guid}/versions/{version:int}")]
    public Task<ProposalDto> GetVersion(Guid id, int version, CancellationToken ct) => proposals.GetAsync(id, ct, version);

    /// <summary>Live totals for the builder (nothing is saved).</summary>
    [HttpPost("preview")]
    public Task<PreviewResponse> Preview(PreviewRequest r, CancellationToken ct) => invoices.PreviewAsync(r, allowRecurrence: true, ct);

    [HttpPost]
    [ProducesResponseType(typeof(ProposalDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(ProposalRequest r, CancellationToken ct) => StatusCode(StatusCodes.Status201Created, await proposals.CreateAsync(r, ct));

    /// <summary>Edits the draft, or creates a new version when the current one was already sent.</summary>
    [HttpPut("{id:guid}")]
    public Task<ProposalDto> Update(Guid id, ProposalRequest r, CancellationToken ct) => proposals.UpdateAsync(id, r, ct);

    [HttpPost("{id:guid}/send")]
    public Task<SendProposalResponse> Send(Guid id, SendProposalRequest r, CancellationToken ct) => proposals.SendAsync(id, r, ct);

    [HttpPost("{id:guid}/withdraw")]
    public Task<ProposalDto> Withdraw(Guid id, WithdrawProposalRequest r, CancellationToken ct) => proposals.WithdrawAsync(id, r, ct);

    /// <summary>Deletes a proposal that was never sent (anything sent is withdrawn instead).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid? concurrencyStamp, CancellationToken ct)
    {
        await proposals.DeleteDraftAsync(id, concurrencyStamp, ct);
        return NoContent();
    }

    /// <summary>Copies the latest version into a new draft proposal.</summary>
    [HttpPost("{id:guid}/duplicate")]
    [ProducesResponseType(typeof(ProposalDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Duplicate(Guid id, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await proposals.DuplicateAsync(id, ct));
}

/// <summary>Reusable proposal templates (sections and price lines).</summary>
[ApiController]
[Route("api/v1/agency/proposal-templates")]
[HasPermission(Permissions.ProposalsManage)]
public sealed class ProposalTemplatesController(ProposalTemplateService templates) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ProposalTemplateDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => templates.ListAsync(includeInactive, ct);

    [HttpGet("{id:guid}")]
    public Task<ProposalTemplateDto> Get(Guid id, CancellationToken ct) => templates.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType(typeof(ProposalTemplateDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(ProposalTemplateRequest r, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await templates.CreateAsync(r, ct));

    [HttpPut("{id:guid}")]
    public Task<ProposalTemplateDto> Update(Guid id, ProposalTemplateRequest r, CancellationToken ct) => templates.UpdateAsync(id, r, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await templates.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Public proposal page (<c>/p/{token}</c>): view (counted), accept (typed signature) or decline. Anonymous, rate limited.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/v1/public/proposals/{token}")]
public sealed class PublicProposalsController(ProposalService proposals, ProposalAcceptanceService acceptance, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<PublicProposalDto> Get(string token, CancellationToken ct)
    {
        var proposal = await proposals.FindByTokenAsync(token, ct);
        await proposals.RecordViewAsync(proposal, ct);
        return await proposals.PublicViewAsync(proposal, ct);
    }

    [HttpPost("accept")]
    public async Task<AcceptProposalResponse> Accept(string token, AcceptProposalRequest r, CancellationToken ct)
    {
        var proposal = await proposals.FindByTokenAsync(token, ct);
        return await acceptance.AcceptAsync(proposal.Id, r, new SignerContext(currentUser.IpAddress, currentUser.UserAgent, currentUser.IdOrNull), ct);
    }

    [HttpPost("decline")]
    public async Task<PublicProposalDto> Decline(string token, DeclineProposalRequest r, CancellationToken ct)
    {
        var proposal = await proposals.FindByTokenAsync(token, ct);
        return await acceptance.DeclineAsync(proposal.Id, r, new SignerContext(currentUser.IpAddress, currentUser.UserAgent, currentUser.IdOrNull), ct);
    }
}

/// <summary>Proposals in the client portal (Billing or Owner duty); another organization's proposal answers 404.</summary>
[DeniedWhileImpersonating(WritesOnly = true)] // accepting a proposal is a financial commitment
[ApiController]
[Route("api/v1/client/billing/proposals")]
[HasPermission(Permissions.ClientPortal)]
public sealed class ClientProposalsController(
    AppDbContext db, IClientScope scope, ClientBillingService billing, ProposalService proposals, ProposalAcceptanceService acceptance,
    ICurrentUser currentUser) : ControllerBase
{
    public sealed record ClientProposalSummaryDto(Guid Id, string Number, string Title, ProposalStatus Status, string Currency, decimal Total,
        decimal MonthlyRecurringValue, DateOnly ValidUntil, DateTime? SentAt, DateTime? AcceptedAt);

    [HttpGet]
    public async Task<IReadOnlyList<ClientProposalSummaryDto>> List(CancellationToken ct)
    {
        var ids = await billing.BillingClientIdsAsync(ct);
        var rows = await db.Set<Proposal>().AsNoTracking()
            .Where(p => p.ClientAccountId != null && ids.Contains(p.ClientAccountId.Value) && p.SentVersion != null && p.Status != ProposalStatus.Withdrawn)
            .OrderByDescending(p => p.SentAt).ToListAsync(ct);
        var result = new List<ClientProposalSummaryDto>();
        foreach (var p in rows)
        {
            var view = await proposals.PublicViewAsync(p, ct);
            result.Add(new ClientProposalSummaryDto(p.Id, p.Number, view.Title, view.Status, p.Currency, view.Version.Totals.Total,
                view.Version.Recurring.MonthlyRecurringValue, view.Version.ValidUntil, p.SentAt, p.AcceptedAt));
        }
        return result;
    }

    [HttpGet("{id:guid}")]
    public async Task<PublicProposalDto> Get(Guid id, CancellationToken ct) => await proposals.PublicViewAsync(await LoadAsync(id, ct), ct);

    [HttpPost("{id:guid}/accept")]
    public async Task<AcceptProposalResponse> Accept(Guid id, AcceptProposalRequest r, CancellationToken ct)
    {
        await LoadAsync(id, ct);
        return await acceptance.AcceptAsync(id, r, new SignerContext(currentUser.IpAddress, currentUser.UserAgent, currentUser.Id), ct);
    }

    [HttpPost("{id:guid}/decline")]
    public async Task<PublicProposalDto> Decline(Guid id, DeclineProposalRequest r, CancellationToken ct)
    {
        await LoadAsync(id, ct);
        return await acceptance.DeclineAsync(id, r, new SignerContext(currentUser.IpAddress, currentUser.UserAgent, currentUser.Id), ct);
    }

    private async Task<Proposal> LoadAsync(Guid id, CancellationToken ct)
    {
        var proposal = await db.Set<Proposal>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.SentVersion != null &&
            p.Status != ProposalStatus.Withdrawn, ct);
        if (proposal?.ClientAccountId is not { } clientId) throw DomainException.NotFound("Proposal");
        await scope.EnsureAccessAsync(clientId, ClientMemberRole.Billing, ct);
        return proposal;
    }
}
