using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Auth;

namespace OptimizeAll.Api.Modules.Admin;

/// <summary>"Log in as user" (see <see cref="ImpersonationService"/> for the design).</summary>
[ApiController]
[Route("api/v1/admin/users/{id:guid}/impersonate")]
public sealed class AdminImpersonationController(ImpersonationService impersonation, IOptions<SecurityOptions> security) : ControllerBase
{
    /// <summary>
    /// Starts a time-boxed session as the user (requires <c>users.impersonate</c>, a reason and <c>"confirm": true</c>).
    /// Returns the target's access token and sets the impersonation cookie; the caller's own refresh cookie is kept, so
    /// <c>POST /api/v1/auth/impersonation/exit</c> returns to it. Admins, other impersonators, yourself and inactive
    /// accounts are refused.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.UsersImpersonate)]
    [DeniedWhileImpersonating]
    public async Task<ActionResult<AuthResponse>> Start(Guid id, ImpersonateRequest request, CancellationToken ct)
    {
        var started = await impersonation.StartAsync(id, request, ct);
        AuthCookies.Set(Response, security.Value, AuthCookies.Impersonation, started.SessionToken, started.SessionExpiresAt);
        return started.Response;
    }
}

/// <summary>Test accounts (QA/demo). Listing uses <c>GET /api/v1/admin/users?isTestAccount=true</c>.</summary>
[ApiController]
[Route("api/v1/admin/test-users")]
[DeniedWhileImpersonating(WritesOnly = true)]
public sealed class AdminTestUsersController(TestUsersService testUsers) : ControllerBase
{
    /// <summary>Creates a verified test user with the given roles; the generated password is in this response only.</summary>
    [HttpPost]
    [HasPermission(Permissions.UsersManage)]
    [ProducesResponseType(typeof(CreatedTestUserDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateTestUserRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await testUsers.CreateAsync(request, ct));

    /// <summary>Deactivates a test account and signs it out everywhere. Real accounts answer 409.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> Delete(Guid id, [FromBody] ReasonRequest request, CancellationToken ct)
    {
        await testUsers.DeleteAsync(id, request.Reason.Trim(), ct);
        return NoContent();
    }
}
