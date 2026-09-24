using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Modules.Rates;

// Person-level pricing (docs/REWARD_ENGINE.md "Person-level rates"). Every write moves money, so all of them are denied
// while impersonating; reads are allowed (the impersonation audit records them).

/// <summary>Reusable, versioned rate cards.</summary>
[DeniedWhileImpersonating(WritesOnly = true)]
[ApiController]
[Route("api/v1/admin/rate-cards")]
public sealed class RateCardsController(IRateCardsService cards) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.RatesView)]
    public Task<PagedResult<RateCardListItemDto>> List([FromQuery] RateCardQuery query, CancellationToken ct) => cards.ListAsync(query, ct);

    /// <summary>A card with every version (rates, caps, status), its assignments and how many submissions it priced.</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.RatesView)]
    public Task<RateCardDto> Get(Guid id, CancellationToken ct) => cards.GetAsync(id, ct);

    /// <summary>Creates a card with version 1 (draft unless "activate": true). 409 rate_card.name_taken.</summary>
    [HttpPost]
    [HasPermission(Permissions.RatesManage)]
    public async Task<ActionResult<RateCardDto>> Create(CreateRateCardRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await cards.CreateAsync(request, ct));

    /// <summary>Renames / re-describes a card (rates change only through new versions). 409 concurrency.conflict.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.RatesManage)]
    public Task<RateCardDto> Update(Guid id, UpdateRateCardRequest request, CancellationToken ct) => cards.UpdateAsync(id, request, ct);

    /// <summary>
    /// Saves a new immutable version (confirm + reason; optional future effectiveFrom). A raise above the four-eyes
    /// threshold is saved as PendingApproval. 409 rate_card.version_conflict / rate_card.pending_approval / rates.fx_missing.
    /// </summary>
    [HttpPost("{id:guid}/versions")]
    [HasPermission(Permissions.RatesManage)]
    public async Task<ActionResult<RateCardDto>> CreateVersion(Guid id, CreateRateCardVersionRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await cards.CreateVersionAsync(id, request, ct));

    /// <summary>Approves a pending rate increase (four-eyes: not its author, not the rate's owner → 403 rates.self_approval).</summary>
    [HttpPost("{id:guid}/versions/{version:int}/approve")]
    [HasPermission(Permissions.RatesManage)]
    public Task<RateCardDto> Approve(Guid id, int version, ApproveRateCardVersionRequest request, CancellationToken ct) =>
        cards.ApproveVersionAsync(id, version, request, ct);

    [HttpPost("{id:guid}/versions/{version:int}/reject")]
    [HasPermission(Permissions.RatesManage)]
    public Task<RateCardDto> Reject(Guid id, int version, RejectRateCardVersionRequest request, CancellationToken ct) =>
        cards.RejectVersionAsync(id, version, request, ct);

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.RatesManage)]
    public Task<RateCardDto> Activate(Guid id, ActivateRateCardRequest request, CancellationToken ct) => cards.ActivateAsync(id, request, ct);

    /// <summary>Archives a card. In use → 409 rate_card.in_use unless "endAssignments": true. Priced submissions keep their rate.</summary>
    [HttpPost("{id:guid}/archive")]
    [HasPermission(Permissions.RatesManage)]
    public Task<RateCardDto> Archive(Guid id, ArchiveRateCardRequest request, CancellationToken ct) => cards.ArchiveAsync(id, request, ct);

    [HttpPost("{id:guid}/duplicate")]
    [HasPermission(Permissions.RatesManage)]
    public async Task<ActionResult<RateCardDto>> Duplicate(Guid id, DuplicateRateCardRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await cards.DuplicateAsync(id, request, ct));
}

/// <summary>Named groups of people that share rates (manual members or an automatic tier / follower rule).</summary>
[DeniedWhileImpersonating(WritesOnly = true)]
[ApiController]
[Route("api/v1/admin/rate-groups")]
public sealed class RateGroupsController(IRateGroupsService groups) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.RatesView)]
    public Task<PagedResult<RateGroupListItemDto>> List([FromQuery] RateGroupQuery query, CancellationToken ct) => groups.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.RatesView)]
    public Task<RateGroupDto> Get(Guid id, CancellationToken ct) => groups.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.RatesManage)]
    public async Task<ActionResult<RateGroupDto>> Create(CreateRateGroupRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await groups.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.RatesManage)]
    public Task<RateGroupDto> Update(Guid id, UpdateRateGroupRequest request, CancellationToken ct) => groups.UpdateAsync(id, request, ct);

    /// <summary>Archives ("deletes") a group. With members/assignments → 409 rate_group.in_use unless "force": true.</summary>
    [HttpPost("{id:guid}/archive")]
    [HasPermission(Permissions.RatesManage)]
    public Task<RateGroupDto> Archive(Guid id, ArchiveRateGroupRequest request, CancellationToken ct) => groups.ArchiveAsync(id, request, ct);

    /// <summary>Members (manual groups) or people matching the rule (automatic groups), with search and paging.</summary>
    [HttpGet("{id:guid}/members")]
    [HasPermission(Permissions.RatesView)]
    public Task<PagedResult<RateGroupMemberDto>> Members(Guid id, [FromQuery] RateGroupMembersQuery query, CancellationToken ct) =>
        groups.MembersAsync(id, query, ct);

    /// <summary>Adds up to 10,000 people; returns what was added, unchanged, rejected and flagged.</summary>
    [HttpPost("{id:guid}/members")]
    [HasPermission(Permissions.RatesAssign)]
    public Task<BulkMembersResultDto> AddMembers(Guid id, AddRateGroupMembersRequest request, CancellationToken ct) =>
        groups.AddMembersAsync(id, request, ct);

    [HttpPost("{id:guid}/members/remove")]
    [HasPermission(Permissions.RatesAssign)]
    public Task<BulkMembersResultDto> RemoveMembers(Guid id, RemoveRateGroupMembersRequest request, CancellationToken ct) =>
        groups.RemoveMembersAsync(id, request, ct);

    /// <summary>CSV import (multipart field "file"; a column of emails or user ids). dryRun=true only validates.</summary>
    [HttpPost("{id:guid}/members/import")]
    [HasPermission(Permissions.RatesAssign)]
    [RequestSizeLimit(RateGroupLimits.MaxCsvBytes + 64 * 1024)]
    public Task<CsvImportResultDto> Import(Guid id, IFormFile? file, [FromQuery] bool dryRun, [FromQuery] string? note, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new DomainException("csv.empty", "Choose a CSV file to import.");
        return groups.ImportAsync(id, file, dryRun, note, ct);
    }

    [HttpGet("{id:guid}/members/export.csv")]
    [HasPermission(Permissions.RatesView)]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var (name, header, rows) = await groups.ExportAsync(id, ct);
        return Csv.File(name, header, rows);
    }

    /// <summary>Membership history (added/removed, by whom, why), newest first.</summary>
    [HttpGet("{id:guid}/history")]
    [HasPermission(Permissions.RatesView)]
    public Task<PagedResult<RateGroupMemberEventDto>> History(Guid id, [FromQuery] PageQuery query, CancellationToken ct) =>
        groups.HistoryAsync(id, query, ct);
}

/// <summary>Assignments of rate cards to people and groups (global or per campaign, optionally time-boxed).</summary>
[DeniedWhileImpersonating(WritesOnly = true)]
[ApiController]
[Route("api/v1/admin/rate-assignments")]
public sealed class RateAssignmentsController(IRateAssignmentsService assignments) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.RatesView)]
    public Task<PagedResult<RateAssignmentDto>> List([FromQuery] RateAssignmentQuery query, CancellationToken ct) => assignments.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.RatesView)]
    public Task<RateAssignmentDto> Get(Guid id, CancellationToken ct) => assignments.GetAsync(id, ct);

    /// <summary>409 rates.duplicate_assignment (overlapping window, same target/scope), rates.fx_missing, rate_card.not_active.</summary>
    [HttpPost]
    [HasPermission(Permissions.RatesAssign)]
    public async Task<ActionResult<RateAssignmentDto>> Create(CreateRateAssignmentRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await assignments.CreateAsync(request, ct));

    /// <summary>Changes the window (extend or shorten a deal). A start that has passed can't move.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.RatesAssign)]
    public Task<RateAssignmentDto> Update(Guid id, UpdateRateAssignmentRequest request, CancellationToken ct) => assignments.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/end")]
    [HasPermission(Permissions.RatesAssign)]
    public Task<RateAssignmentDto> End(Guid id, EndRateAssignmentRequest request, CancellationToken ct) => assignments.EndAsync(id, request, ct);
}

/// <summary>A person's rates: groups, assignments, effective rates, "explain this rate" and negotiated custom rates.</summary>
[DeniedWhileImpersonating(WritesOnly = true)]
[ApiController]
[Route("api/v1/admin/users/{userId:guid}")]
public sealed class PersonRatesController(IPersonRatesService rates) : ControllerBase
{
    [HttpGet("rates")]
    [HasPermission(Permissions.RatesView)]
    public Task<PersonRatesDto> Rates(Guid userId, [FromQuery] Guid? campaignId, CancellationToken ct) => rates.PersonRatesAsync(userId, campaignId, ct);

    /// <summary>Every candidate rate for one post (platform/format, optional campaign and time), which one won and why.</summary>
    [HttpGet("rates/explain")]
    [HasPermission(Permissions.RatesView)]
    public Task<RateExplanationDto> Explain(Guid userId, [FromQuery] ExplainRateQuery query, CancellationToken ct) => rates.ExplainAsync(userId, query, ct);

    /// <summary>Creates a negotiated custom rate (a private card + assignment) for this person, globally or for one campaign.</summary>
    [HttpPost("custom-rates")]
    [HasPermission(Permissions.RatesManage)]
    public async Task<ActionResult<RateAssignmentDto>> CreateCustomRate(Guid userId, CreateCustomRateRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await rates.CreateCustomRateAsync(userId, request, ct));
}

/// <summary>Person-level rates as seen from a campaign: which apply, currency problems and a price simulator.</summary>
[DeniedWhileImpersonating(WritesOnly = true)]
[ApiController]
[Route("api/v1/admin/campaigns/{campaignId:guid}/rates")]
public sealed class CampaignRatesController(IPersonRatesService rates) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.RatesView)]
    public Task<CampaignRatesDto> Get(Guid campaignId, CancellationToken ct) => rates.CampaignRatesAsync(campaignId, ct);

    /// <summary>"Price for person X on platform Y": the explanation plus the full quote with the person's real caps and budget.</summary>
    [HttpPost("simulate")]
    [HasPermission(Permissions.RatesView)]
    public Task<RateExplanationDto> Simulate(Guid campaignId, SimulateRateRequest request, CancellationToken ct) =>
        rates.SimulateAsync(campaignId, request, ct);
}
