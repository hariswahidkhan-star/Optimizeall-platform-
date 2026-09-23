using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Support;

[ApiController]
[HasPermission(Permissions.ParticipantPortal)]
[Route("api/v1/me/support/tickets")]
public sealed class MySupportTicketsController(SupportService support, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<TicketSummaryDto>> List([FromQuery] MyTicketQuery query, CancellationToken ct) =>
        support.ListMineAsync(currentUser.Id, query, ct);

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Submissions)]
    [ProducesResponseType(typeof(ParticipantTicketDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateTicketRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await support.CreateAsync(currentUser.Id, request, ct));

    /// <summary>The ticket with its public conversation. Internal staff notes are never included.</summary>
    [HttpGet("{id:guid}")]
    public Task<ParticipantTicketDto> Get(Guid id, CancellationToken ct) => support.GetMineAsync(currentUser.Id, id, ct);

    [HttpPost("{id:guid}/messages")]
    [EnableRateLimiting(RateLimitPolicies.Submissions)]
    public Task<ParticipantTicketDto> Reply(Guid id, TicketMessageRequest request, CancellationToken ct) =>
        support.ReplyAsParticipantAsync(currentUser.Id, id, request, ct);

    [HttpPost("{id:guid}/close")]
    public Task<ParticipantTicketDto> Close(Guid id, CancellationToken ct) => support.CloseAsParticipantAsync(currentUser.Id, id, ct);
}

[ApiController]
[HasPermission(Permissions.SupportManage)]
[Route("api/v1/admin/support/tickets")]
public sealed class AdminSupportTicketsController(SupportService support, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<StaffTicketSummaryDto>> List([FromQuery] StaffTicketQuery query, CancellationToken ct) =>
        support.ListAllAsync(currentUser.Id, query, ct);

    [HttpGet("{id:guid}")]
    public Task<StaffTicketDto> Get(Guid id, CancellationToken ct) => support.GetForStaffAsync(id, ct);

    /// <summary>Public reply (sets AwaitingParticipant and notifies the participant) or internal note.</summary>
    [HttpPost("{id:guid}/messages")]
    public Task<StaffTicketDto> Reply(Guid id, StaffMessageRequest request, CancellationToken ct) =>
        support.ReplyAsStaffAsync(currentUser.Id, id, request, ct);

    [HttpPut("{id:guid}")]
    public Task<StaffTicketDto> Update(Guid id, UpdateTicketRequest request, CancellationToken ct) => support.UpdateAsync(id, request, ct);
}
