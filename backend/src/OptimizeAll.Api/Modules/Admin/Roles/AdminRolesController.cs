using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Admin.Roles;

/// <summary>
/// Roles &amp; permissions: read-only built-in roles, custom role CRUD and assignment, and the permission catalog.
/// Guardrails (see <see cref="CustomRoleGuardrails"/>): nobody grants what they don't hold; client.portal never mixes with
/// staff permissions; roles.manage / settings.manage / users.impersonate grants need the built-in Admin role.
/// </summary>
[ApiController]
[HasPermission(Permissions.RolesManage)]
[Route("api/v1/admin/roles")]
[DeniedWhileImpersonating(WritesOnly = true)]
public sealed class AdminRolesController(AdminRolesService roles) : ControllerBase
{
    /// <summary>Built-in roles (read-only, with their permission sets) and custom roles.</summary>
    [HttpGet]
    public Task<RolesOverviewDto> List(CancellationToken ct) => roles.OverviewAsync(ct);

    /// <summary>Every permission grouped by area with labels and descriptions; <c>granted</c> = the caller may grant it.</summary>
    [HttpGet("catalog")]
    public PermissionCatalogDto Catalog() => roles.Catalog();

    [HttpGet("{id:guid}")]
    public Task<CustomRoleDetailDto> Get(Guid id, CancellationToken ct) => roles.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType(typeof(CustomRoleDetailDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveCustomRoleRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await roles.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<CustomRoleDetailDto> Update(Guid id, SaveCustomRoleRequest request, CancellationToken ct) => roles.UpdateAsync(id, request, ct);

    /// <summary>Deletes an unassigned role; an assigned one needs <c>?confirm=true</c> (optionally <c>&amp;reassignTo={roleId}</c>).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] DeleteCustomRoleQuery query, CancellationToken ct)
    {
        await roles.DeleteAsync(id, query, ct);
        return NoContent();
    }

    /// <summary>A user's custom roles and effective permissions.</summary>
    [HttpGet("users/{userId:guid}")]
    public Task<UserCustomRolesDto> UserRoles(Guid userId, CancellationToken ct) => roles.UserRolesAsync(userId, ct);

    [HttpPut("{id:guid}/users/{userId:guid}")]
    public Task<UserCustomRolesDto> Assign(Guid id, Guid userId, CancellationToken ct) => roles.AssignAsync(id, userId, ct);

    [HttpDelete("{id:guid}/users/{userId:guid}")]
    public Task<UserCustomRolesDto> Unassign(Guid id, Guid userId, CancellationToken ct) => roles.UnassignAsync(id, userId, ct);
}
