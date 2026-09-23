using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.EmailMarketing.Campaigns;
using OptimizeAll.Api.Modules.EmailMarketing.Reporting;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Api.Modules.EmailMarketing.Templates;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing;

public sealed record ClientCampaignItem(Guid Id, Guid ClientAccountId, string ClientName, string Name, MessageChannel Channel, CampaignStatus Status,
    ApprovalStatus ApprovalStatus, string? Subject, ScheduleMode ScheduleMode, DateTime? ScheduledAt, string? ScheduledLocalTime, DateTime? CompletedAt,
    int Sent, int UniqueOpens, int UniqueClicks, bool CanApprove);

public sealed record ClientOrganization(Guid Id, string Name);

public sealed class ClientCampaignQuery : PageQuery
{
    public Guid? ClientId { get; set; }
}

/// <summary>
/// Client portal (<c>client.portal</c>): read-only campaign performance for the caller's organizations, and approval of
/// campaigns waiting for the client (Approver or Owner duty). Other tenants' campaigns answer 404; drafts are never shown.
/// </summary>
[ApiController]
[Route("api/v1/client/email")]
[HasPermission(Permissions.ClientPortal)]
public sealed class ClientEmailController(AppDbContext db, EmailAccess access, IClientScope scope, CampaignService campaigns, ReportService reports) : ControllerBase
{
    /// <summary>The organizations the caller belongs to (for the portal's organization picker and KPI requests).</summary>
    [HttpGet("clients")]
    public async Task<IReadOnlyList<ClientOrganization>> Clients(CancellationToken ct)
    {
        var ids = (await access.ClientMemberIdsAsync(ct)).ToList();
        return await db.Set<ClientAccount>().AsNoTracking().Where(c => ids.Contains(c.Id)).OrderBy(c => c.Name)
            .Select(c => new ClientOrganization(c.Id, c.Name)).ToListAsync(ct);
    }

    [HttpGet("campaigns")]
    public async Task<PagedResult<ClientCampaignItem>> Campaigns([FromQuery] ClientCampaignQuery q, CancellationToken ct)
    {
        var query = await VisibleAsync(q.ClientId, ct);
        var page = await query.Where(c => c.Status != CampaignStatus.Draft).OrderByDescending(c => c.CompletedAt ?? c.UpdatedAt).ThenBy(c => c.Id).ToPagedAsync(q, ct);
        return new PagedResult<ClientCampaignItem>(await ToItemsAsync(page.Items, ct), page.Total, page.Page, page.PageSize);
    }

    /// <summary>Campaigns waiting for the client's approval.</summary>
    [HttpGet("approvals")]
    public async Task<IReadOnlyList<ClientCampaignItem>> Approvals([FromQuery] Guid? clientId, CancellationToken ct)
    {
        var query = await VisibleAsync(clientId, ct);
        var rows = await query.Where(c => c.ApprovalStatus == ApprovalStatus.Pending && (c.Status == CampaignStatus.Scheduled || c.Status == CampaignStatus.Paused))
            .OrderBy(c => c.ScheduledAt).Take(100).ToListAsync(ct);
        return await ToItemsAsync(rows, ct);
    }

    [HttpGet("campaigns/{id:guid}/report")]
    public async Task<CampaignReport> Report(Guid id, CancellationToken ct) => await reports.BuildAsync(await LoadAsync(id, ct), ct);

    /// <summary>What the email looks like (sample data), for approval.</summary>
    [HttpGet("campaigns/{id:guid}/preview")]
    public async Task<RenderResult> Preview(Guid id, CancellationToken ct) => await campaigns.PreviewCampaignAsync(await LoadAsync(id, ct), null, ct);

    [HttpPost("campaigns/{id:guid}/approval")]
    public Task<CampaignDto> Decide(Guid id, ApprovalDecisionRequest request, CancellationToken ct) => campaigns.DecideApprovalAsync(id, request, ct);

    [HttpGet("kpis")]
    public async Task<EmailKpis> Kpis([FromQuery] Guid clientId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ClientMemberRole.Viewer, ct);
        return await reports.ComputeKpisAsync(clientId, from, to, ct);
    }

    private async Task<IQueryable<EmailCampaign>> VisibleAsync(Guid? clientId, CancellationToken ct)
    {
        var ids = await access.ClientMemberIdsAsync(ct);
        if (clientId is { } id)
        {
            if (!ids.Contains(id)) throw DomainException.NotFound("Client");
            ids = new[] { id };
        }
        var list = ids.ToList();
        return db.Set<EmailCampaign>().AsNoTracking().Where(c => c.ClientAccountId != null && list.Contains(c.ClientAccountId.Value));
    }

    private async Task<EmailCampaign> LoadAsync(Guid id, CancellationToken ct)
    {
        var query = await VisibleAsync(null, ct);
        return await query.FirstOrDefaultAsync(c => c.Id == id && c.Status != CampaignStatus.Draft, ct) ?? throw DomainException.NotFound("Campaign");
    }

    private async Task<List<ClientCampaignItem>> ToItemsAsync(IReadOnlyList<EmailCampaign> rows, CancellationToken ct)
    {
        var items = await campaigns.ToListItemsAsync(rows, ct);
        var clientIds = rows.Select(r => r.ClientAccountId!.Value).Distinct().ToList();
        var names = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var roles = new Dictionary<Guid, ClientMemberRole?>();
        foreach (var cid in clientIds) roles[cid] = await scope.MemberRoleAsync(cid, ct);
        return rows.Select((c, i) => new ClientCampaignItem(c.Id, c.ClientAccountId!.Value, names.GetValueOrDefault(c.ClientAccountId!.Value, "Client"), c.Name,
            c.Channel, c.Status, c.ApprovalStatus, c.Channel == MessageChannel.Email ? c.Subject : null, c.ScheduleMode, c.ScheduledAt, c.ScheduledLocalTime,
            c.CompletedAt, items[i].Sent, items[i].UniqueOpens, items[i].UniqueClicks,
            c.ApprovalStatus == ApprovalStatus.Pending && roles[c.ClientAccountId!.Value] is ClientMemberRole.Approver or ClientMemberRole.Owner)).ToList();
    }
}
