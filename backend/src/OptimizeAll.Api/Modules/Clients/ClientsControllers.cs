using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Projects;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Clients;

/// <summary>Client accounts for agency staff (clients.view to read, clients.manage to change).</summary>
[ApiController]
[HasPermission(Permissions.ClientsView)]
[Route("api/v1/agency/clients")]
public sealed class AgencyClientsController(
    ClientService clients, ClientRelationshipService relationship, ClientHealthService health, DeliveryFileService files,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ClientSummaryDto>> List([FromQuery] ClientListQuery query, CancellationToken ct) => clients.ListAsync(query, ct);

    [HttpPost]
    [HasPermission(Permissions.ClientsManage)]
    [ProducesResponseType(typeof(ClientDetailDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateClientRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await clients.CreateAsync(request, ct));

    /// <summary>Health board (red/amber/green with reasons). <c>?mine=true</c>: only clients I manage.</summary>
    [HttpGet("health")]
    public Task<IReadOnlyList<ClientHealthDto>> HealthBoard([FromQuery] bool mine, CancellationToken ct) =>
        health.BoardAsync(mine ? currentUser.Id : null, ct);

    [HttpGet("{id:guid}")]
    public Task<ClientDetailDto> Get(Guid id, CancellationToken ct) => clients.GetAsync(id, ct);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.ClientsManage)]
    public Task<ClientDetailDto> Update(Guid id, UpdateClientRequest request, CancellationToken ct) => clients.UpdateAsync(id, request, ct);

    /// <summary>Onboarding / Active / Paused / Churned (Paused and Churned need a reason). Audited.</summary>
    [HttpPost("{id:guid}/status")]
    [HasPermission(Permissions.ClientsManage)]
    public Task<ClientDetailDto> ChangeStatus(Guid id, ChangeClientStatusRequest request, CancellationToken ct) =>
        clients.ChangeStatusAsync(id, request, ct);

    [HttpPost("{id:guid}/logo")]
    [HasPermission(Permissions.ClientsManage)]
    [RequestSizeLimit(DeliveryFileValidator.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = DeliveryFileValidator.MaxBytes + 1024 * 1024)]
    public Task<ClientDetailDto> Logo(Guid id, [FromForm] DeliveryUploadForm form, CancellationToken ct) =>
        clients.SetLogoAsync(id, form.File, files, ct);

    [HttpGet("{id:guid}/health")]
    public Task<ClientHealthDto> Health(Guid id, CancellationToken ct) => health.ForClientAsync(id, ct);

    // ---------- account team ----------

    [HttpGet("{id:guid}/team")]
    public Task<IReadOnlyList<TeamAssignmentDto>> Team(Guid id, CancellationToken ct) => clients.TeamAsync(id, ct);

    [HttpPost("{id:guid}/team")]
    [HasPermission(Permissions.ClientsManage)]
    public Task<IReadOnlyList<TeamAssignmentDto>> AddTeamMember(Guid id, AddTeamMemberRequest request, CancellationToken ct) =>
        clients.AddTeamMemberAsync(id, request, ct);

    [HttpDelete("{id:guid}/team/{assignmentId:guid}")]
    [HasPermission(Permissions.ClientsManage)]
    public Task<IReadOnlyList<TeamAssignmentDto>> RemoveTeamMember(Guid id, Guid assignmentId, CancellationToken ct) =>
        clients.RemoveTeamMemberAsync(id, assignmentId, ct);

    // ---------- client users ----------

    [HttpGet("{id:guid}/members")]
    public Task<IReadOnlyList<ClientMemberDto>> Members(Guid id, CancellationToken ct) => clients.MembersAsync(id, false, ct);

    /// <summary>Invites a client user by email (new users receive a set-password link).</summary>
    [HttpPost("{id:guid}/members")]
    [HasPermission(Permissions.ClientsManage)]
    public async Task<IActionResult> Invite(Guid id, InviteClientUserRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await clients.InviteAsync(id, request, false, ct));

    [HttpPut("{id:guid}/members/{userId:guid}")]
    [HasPermission(Permissions.ClientsManage)]
    public Task<IReadOnlyList<ClientMemberDto>> ChangeRole(Guid id, Guid userId, ChangeMemberRoleRequest request, CancellationToken ct) =>
        clients.ChangeMemberRoleAsync(id, userId, request, false, ct);

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [HasPermission(Permissions.ClientsManage)]
    public Task<IReadOnlyList<ClientMemberDto>> RemoveMember(Guid id, Guid userId, CancellationToken ct) =>
        clients.RemoveMemberAsync(id, userId, false, ct);

    // ---------- onboarding ----------

    [HttpGet("{id:guid}/onboarding")]
    public Task<OnboardingDto> Onboarding(Guid id, CancellationToken ct) => relationship.OnboardingAsync(id, ct);

    [HttpPost("{id:guid}/onboarding")]
    [HasPermission(Permissions.ClientsManage)]
    public Task<OnboardingDto> AddOnboardingItem(Guid id, AddOnboardingItemRequest request, CancellationToken ct) =>
        relationship.AddOnboardingItemAsync(id, request, ct);

    [HttpPut("{id:guid}/onboarding/{itemId:guid}")]
    [HasPermission(Permissions.ClientsManage)]
    public Task<OnboardingDto> UpdateOnboardingItem(Guid id, Guid itemId, UpdateOnboardingItemRequest request, CancellationToken ct) =>
        relationship.UpdateOnboardingItemAsync(id, itemId, request, ct);

    // ---------- brand kit ----------

    [HttpGet("{id:guid}/brand-kit")]
    public Task<BrandKitDto> BrandKit(Guid id, CancellationToken ct) => relationship.BrandKitAsync(id, ct);

    [HttpPut("{id:guid}/brand-kit")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    public Task<BrandKitDto> UpdateBrandKit(Guid id, UpdateBrandKitRequest request, CancellationToken ct) =>
        relationship.UpdateBrandKitAsync(id, request, ct);

    [HttpPost("{id:guid}/brand-kit/assets")]
    [HasPermission(Permissions.DeliverablesSubmit)]
    [RequestSizeLimit(DeliveryFileValidator.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = DeliveryFileValidator.MaxBytes + 1024 * 1024)]
    public Task<BrandKitDto> AddAsset(Guid id, [FromForm] BrandAssetForm form, CancellationToken ct) => relationship.AddAssetAsync(id, form, ct);

    [HttpDelete("{id:guid}/brand-kit/assets/{assetId:guid}")]
    [HasPermission(Permissions.ClientsManage)]
    public Task<BrandKitDto> RemoveAsset(Guid id, Guid assetId, CancellationToken ct) => relationship.RemoveAssetAsync(id, assetId, ct);

    // ---------- feedback ----------

    [HttpGet("{id:guid}/feedback")]
    public Task<FeedbackSummaryDto> Feedback(Guid id, CancellationToken ct) => relationship.FeedbackSummaryAsync(id, null, ct);
}

public sealed record StaffPersonDto(Guid Id, string DisplayName, string Email, IReadOnlyList<Role> Roles);

/// <summary>Staff directory for assignee/owner/team pickers.</summary>
[ApiController]
[HasPermission(Permissions.ClientsView)]
[Route("api/v1/agency/staff")]
public sealed class AgencyStaffController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<StaffPersonDto>> List([FromQuery] string? search, CancellationToken ct)
    {
        var roles = StaffDirectory.DeliveryRoles;
        var q = db.Set<User>().AsNoTracking().Include(u => u.Roles)
            .Where(u => u.Status == UserStatus.Active && u.Roles.Any(r => roles.Contains(r.Role)));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var p = PagingExtensions.LikePattern(search);
            q = q.Where(u => EF.Functions.Like(u.DisplayName, p) || EF.Functions.Like(u.Email, p));
        }
        var users = await q.OrderBy(u => u.DisplayName).Take(200).ToListAsync(ct);
        return users.Select(u => new StaffPersonDto(u.Id, u.DisplayName, u.Email,
            u.Roles.Select(r => r.Role).Where(r => roles.Contains(r)).OrderBy(r => r).ToList())).ToList();
    }
}

/// <summary>Client portal: the organizations of the signed-in client user and their account-level pages.</summary>
[ApiController]
[HasPermission(Permissions.ClientPortal)]
[Route("api/v1/client")]
public sealed class ClientPortalAccountController(ClientService clients, ClientRelationshipService relationship) : ControllerBase
{
    /// <summary>Organizations I belong to, with my duty in each (org switcher).</summary>
    [HttpGet("orgs")]
    public Task<IReadOnlyList<MyOrganizationDto>> Orgs(CancellationToken ct) => clients.MyOrganizationsAsync(ct);

    [HttpGet("orgs/{clientId:guid}/team")]
    public Task<IReadOnlyList<AccountTeamMemberDto>> AccountTeam(Guid clientId, CancellationToken ct) => clients.AccountTeamAsync(clientId, ct);

    [HttpGet("orgs/{clientId:guid}/members")]
    public Task<IReadOnlyList<ClientMemberDto>> Members(Guid clientId, CancellationToken ct) => clients.MembersAsync(clientId, false, ct);

    /// <summary>Owners invite colleagues.</summary>
    [HttpPost("orgs/{clientId:guid}/members")]
    public async Task<IActionResult> Invite(Guid clientId, InviteClientUserRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await clients.InviteAsync(clientId, request, true, ct));

    [HttpPut("orgs/{clientId:guid}/members/{userId:guid}")]
    public Task<IReadOnlyList<ClientMemberDto>> ChangeRole(Guid clientId, Guid userId, ChangeMemberRoleRequest request, CancellationToken ct) =>
        clients.ChangeMemberRoleAsync(clientId, userId, request, true, ct);

    [HttpDelete("orgs/{clientId:guid}/members/{userId:guid}")]
    public Task<IReadOnlyList<ClientMemberDto>> Remove(Guid clientId, Guid userId, CancellationToken ct) =>
        clients.RemoveMemberAsync(clientId, userId, true, ct);

    [HttpGet("orgs/{clientId:guid}/onboarding")]
    public Task<OnboardingDto> Onboarding(Guid clientId, CancellationToken ct) => relationship.OnboardingAsync(clientId, ct);

    /// <summary>Owners tick off onboarding steps the client owns (e.g. "GA4 access granted").</summary>
    [HttpPut("orgs/{clientId:guid}/onboarding/{itemId:guid}")]
    public Task<OnboardingDto> UpdateOnboarding(Guid clientId, Guid itemId, UpdateOnboardingItemRequest request, CancellationToken ct) =>
        relationship.UpdateOnboardingItemAsync(clientId, itemId, request, ct);

    [HttpGet("orgs/{clientId:guid}/brand-kit")]
    public Task<BrandKitDto> BrandKit(Guid clientId, CancellationToken ct) => relationship.BrandKitAsync(clientId, ct);

    /// <summary>Owners upload brand assets (logos, guidelines, photography).</summary>
    [HttpPost("orgs/{clientId:guid}/brand-kit/assets")]
    [RequestSizeLimit(DeliveryFileValidator.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = DeliveryFileValidator.MaxBytes + 1024 * 1024)]
    public Task<BrandKitDto> AddAsset(Guid clientId, [FromForm] BrandAssetForm form, CancellationToken ct) =>
        relationship.AddAssetAsync(clientId, form, ct);

    [HttpGet("orgs/{clientId:guid}/feedback/nps")]
    public Task<NpsStatusDto> Nps(Guid clientId, CancellationToken ct) => relationship.NpsStatusAsync(clientId, ct);

    [HttpPost("orgs/{clientId:guid}/feedback/nps")]
    public Task<NpsStatusDto> SubmitNps(Guid clientId, SubmitNpsRequest request, CancellationToken ct) =>
        relationship.SubmitNpsAsync(clientId, request, ct);

    [HttpPost("orgs/{clientId:guid}/deliverables/{deliverableId:guid}/csat")]
    public async Task<IActionResult> SubmitCsat(Guid clientId, Guid deliverableId, SubmitCsatRequest request, CancellationToken ct)
    {
        await relationship.SubmitCsatAsync(clientId, deliverableId, request, ct);
        return NoContent();
    }
}
