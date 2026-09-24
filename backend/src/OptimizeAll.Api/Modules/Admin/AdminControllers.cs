using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Admin;

[ApiController]
[Route("api/v1/admin/users")]
[DeniedWhileImpersonating(WritesOnly = true)]
public sealed class AdminUsersController(AdminUsersService users) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.UsersView)]
    public Task<PagedResult<AdminUserListItemDto>> List([FromQuery] AdminUserQuery query, CancellationToken ct) => users.ListAsync(query, ct);

    [HttpGet("export.csv")]
    [HasPermission(Permissions.UsersView)]
    public Task<Microsoft.AspNetCore.Mvc.FileContentResult> Export([FromQuery] AdminUserQuery query, CancellationToken ct) =>
        users.ExportCsvAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.UsersView)]
    public Task<AdminUserDetailDto> Get(Guid id, CancellationToken ct) => users.GetAsync(id, ct);

    [HttpPost("{id:guid}/suspend")]
    [HasPermission(Permissions.UsersSuspend)]
    public Task<AdminUserDetailDto> Suspend(Guid id, SuspendUserRequest request, CancellationToken ct) => users.SuspendAsync(id, request, ct);

    [HttpPost("{id:guid}/reactivate")]
    [HasPermission(Permissions.UsersSuspend)]
    public Task<AdminUserDetailDto> Reactivate(Guid id, ReactivateUserRequest request, CancellationToken ct) =>
        users.ReactivateAsync(id, request, ct);

    [HttpPut("{id:guid}/roles")]
    [HasPermission(Permissions.RolesAssign)]
    public Task<AdminUserDetailDto> SetRoles(Guid id, SetRolesRequest request, CancellationToken ct) => users.SetRolesAsync(id, request, ct);

    [HttpPut("{id:guid}/tier")]
    [HasPermission(Permissions.UsersManage)]
    public Task<AdminUserDetailDto> SetTier(Guid id, SetTierRequest request, CancellationToken ct) => users.SetTierAsync(id, request, ct);

    /// <summary>Creates a verified staff account and emails a set-password (reset) link.</summary>
    [HttpPost("staff")]
    [HasPermission(Permissions.RolesAssign)]
    [ProducesResponseType(typeof(AdminUserDetailDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateStaff(CreateStaffRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await users.CreateStaffAsync(request, ct));
}

[DeniedWhileImpersonating(WritesOnly = true)] // platform settings incl. the referral reward
[ApiController]
[HasPermission(Permissions.SettingsManage)]
[Route("api/v1/admin/settings")]
public sealed class AdminSettingsController(AdminSettingsService settings) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SettingDto>> List(CancellationToken ct) => settings.ListAsync(ct);

    [HttpPut("{key}")]
    public Task<SettingDto> Update(string key, UpdateSettingRequest request, CancellationToken ct) => settings.UpdateAsync(key, request, ct);

    /// <summary>Restores the built-in default value.</summary>
    [HttpPost("{key}/reset")]
    public Task<SettingDto> Reset(string key, ResetSettingRequest request, CancellationToken ct) => settings.ResetAsync(key, request, ct);
}

[ApiController]
[HasPermission(Permissions.AuditView)]
[Route("api/v1/admin/audit-logs")]
public sealed class AdminAuditLogsController(AuditLogService audit) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AuditLogDto>> List([FromQuery] AuditLogQuery query, CancellationToken ct) => audit.ListAsync(query, ct);

    [HttpGet("export.csv")]
    public Task<Microsoft.AspNetCore.Mvc.FileContentResult> Export([FromQuery] AuditLogQuery query, CancellationToken ct) =>
        audit.ExportCsvAsync(query, ct);
}

[ApiController]
[HasPermission(Permissions.JobsView)]
[Route("api/v1/admin/jobs")]
public sealed class AdminJobsController(AdminJobsService jobs) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<JobDto>> List(CancellationToken ct) => jobs.ListAsync(ct);

    [HttpGet("runs")]
    public Task<PagedResult<JobRunDto>> Runs([FromQuery] JobRunQuery query, CancellationToken ct) => jobs.RunsAsync(query, ct);

    /// <summary>Runs a job now (requires settings.manage in addition to jobs.view).</summary>
    [DeniedWhileImpersonating] // runs jobs such as payout preparation
    [HttpPost("{name}/run")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<JobRunDto> Run(string name, CancellationToken ct) => jobs.RunNowAsync(name, ct);
}
