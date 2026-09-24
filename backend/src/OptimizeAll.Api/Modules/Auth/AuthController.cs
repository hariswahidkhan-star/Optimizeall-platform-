using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Modules.Auth;

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController(IAuthService auth, ICurrentUser currentUser, IOptions<SecurityOptions> security) : ControllerBase
{
    public const string RefreshCookie = "oa_refresh";
    private const string CsrfHeader = "X-Requested-With";

    /// <summary>Creates a participant account and sends a verification email. Always 202 (no account enumeration).</summary>
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        await auth.RegisterAsync(request, ct);
        return Accepted(new MessageResponse("Check your inbox to verify your email address."));
    }

    [AllowAnonymous]
    [HttpPost("verify-email")]
    public async Task<ActionResult<MessageResponse>> VerifyEmail(TokenRequest request, CancellationToken ct)
    {
        await auth.VerifyEmailAsync(request.Token, ct);
        return new MessageResponse("Your email address is verified.");
    }

    [AllowAnonymous]
    [HttpPost("resend-verification")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResendVerification(EmailRequest request, CancellationToken ct)
    {
        await auth.ResendVerificationAsync(request.Email, ct);
        return Accepted(new MessageResponse("If the account exists and is unverified, a new link is on its way."));
    }

    /// <summary>Returns a short-lived access token and sets the rotating refresh token as an HttpOnly cookie.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);
        SetRefreshCookie(result.RefreshToken, result.RefreshExpiresAt);
        return result.Response;
    }

    /// <summary>Rotates the refresh cookie. Requires the X-Requested-With header (CSRF defence in depth with SameSite=Strict).</summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        RequireCsrfHeader();
        try
        {
            var result = await auth.RefreshAsync(Request.Cookies[RefreshCookie], ct);
            SetRefreshCookie(result.RefreshToken, result.RefreshExpiresAt);
            return result.Response;
        }
        catch (DomainException ex) when (ex.Kind == DomainErrorKind.Unauthorized && ex.Code != AuthService.RefreshRaceCode)
        {
            // A lost rotation race keeps the cookie: the winning response already set the new one.
            ClearRefreshCookie();
            throw;
        }
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        RequireCsrfHeader();
        await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct);
        ClearRefreshCookie();
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken ct)
    {
        await auth.ForgotPasswordAsync(request.Email, ct);
        return Accepted(new MessageResponse("If an account exists for that email, we've sent a reset link."));
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<ActionResult<MessageResponse>> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(request, ct);
        ClearRefreshCookie();
        return new MessageResponse("Your password has been changed. Sign in with your new password.");
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<ActionResult<MessageResponse>> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(currentUser.Id, request, ct);
        ClearRefreshCookie();
        return new MessageResponse("Password changed. Please sign in again on your devices.");
    }

    /// <summary>The signed-in user with roles and effective permissions.</summary>
    [Authorize]
    [HttpGet("me")]
    [DisableRateLimiting]
    public Task<SessionUserDto> Me(CancellationToken ct) => auth.GetSessionUserAsync(currentUser.Id, ct);

    private void RequireCsrfHeader()
    {
        if (!Request.Headers.ContainsKey(CsrfHeader))
            throw DomainException.Forbidden("auth.csrf", "Missing request header.");
    }

    private void SetRefreshCookie(string token, DateTime expiresAt) => SetRefreshCookie(Response, security.Value, token, expiresAt);

    /// <summary>Sets the rotating refresh cookie (shared with the external sign-in controllers).</summary>
    internal static void SetRefreshCookie(HttpResponse response, SecurityOptions security, string token, DateTime expiresAt) =>
        response.Cookies.Append(RefreshCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = security.SecureCookies,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth",
            Expires = expiresAt,
            IsEssential = true,
        });

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookie, new CookieOptions
        {
            HttpOnly = true, Secure = security.Value.SecureCookies, SameSite = SameSiteMode.Strict, Path = "/api/v1/auth",
        });
}
