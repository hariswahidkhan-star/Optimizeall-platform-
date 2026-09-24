using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Hosting;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Auth;

public sealed record DevTestAccountDto(Guid Id, string Email, string DisplayName, IReadOnlyList<Role> Roles, bool IsTestAccount);

public sealed class DevTestLoginRequest
{
    [Required]
    public Guid? UserId { get; set; }
}

/// <summary>
/// Non-production testers' shortcut: the login page's "Test accounts" panel lists active test accounts and seeded demo
/// accounts and signs in as one with a click (a normal session, audited as <c>auth.test_login</c>). Answers 404
/// unless <c>DevTools:TestLoginEnabled</c> is true AND the environment is not Production; accounts that are neither
/// test nor demo accounts are refused (404), whatever the configuration.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/dev")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class DevTestLoginController(
    AppDbContext db, IOptions<DevToolsOptions> devTools, IOptions<SecurityOptions> security, IHostEnvironment env)
    : ControllerBase
{
    private const int MaxAccounts = 200;

    private bool Enabled => TestAccounts.TestLoginAllowed(devTools.Value.TestLoginEnabled, env.EnvironmentName);

    [HttpGet("test-accounts")]
    public async Task<IActionResult> Accounts(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        var demoPattern = "%@" + TestAccounts.DemoDomain;
        var demoSubdomainPattern = "%." + TestAccounts.DemoDomain;
        var users = await db.Set<User>().AsNoTracking().Include(u => u.Roles)
            .Where(u => u.Status == UserStatus.Active && (u.IsTestAccount ||
                        EF.Functions.Like(u.NormalizedEmail, demoPattern) || EF.Functions.Like(u.NormalizedEmail, demoSubdomainPattern)))
            .OrderByDescending(u => u.IsTestAccount).ThenBy(u => u.Email).Take(MaxAccounts).ToListAsync(ct);
        return Ok(users.Where(u => u.IsTestAccount || TestAccounts.IsDemoEmail(u.Email))
            .Select(u => new DevTestAccountDto(u.Id, u.Email, u.DisplayName, u.Roles.Select(r => r.Role).OrderBy(r => r).ToList(), u.IsTestAccount))
            .ToList());
    }

    [HttpPost("test-login")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Login(DevTestLoginRequest request, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        var user = await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (user is null || !(user.IsTestAccount || TestAccounts.IsDemoEmail(user.Email))) return NotFound();

        // Resolved only once enabled, so a disabled endpoint never builds the auth pipeline (e.g. the email sender).
        var auth = HttpContext.RequestServices.GetRequiredService<IAuthService>();
        var result = await auth.SignInWithoutPasswordAsync(user.Id, ct);
        AuthCookies.Set(Response, security.Value, AuthCookies.Refresh, result.RefreshToken, result.RefreshExpiresAt);
        return Ok(result.Response);
    }
}
