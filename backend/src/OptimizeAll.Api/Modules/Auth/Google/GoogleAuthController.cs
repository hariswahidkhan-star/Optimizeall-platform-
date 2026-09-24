using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Auth.Google;

/// <summary>
/// Sign in with Google (OpenID Connect authorization-code flow with PKCE, handled server side). The SPA calls
/// <c>start</c>, sends the browser to the returned Google URL, and Google redirects back to the SPA route
/// <c>/auth/google/callback</c>, which posts the code and state to <c>callback</c>.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class GoogleAuthController(GoogleSignInService google, ICurrentUser currentUser, IOptions<SecurityOptions> security) : ControllerBase
{
    /// <summary>HttpOnly cookie holding the encrypted nonce + PKCE verifier of the current attempt (10 minutes).</summary>
    public const string FlowCookie = "oa_google_flow";
    private const string FlowCookiePath = "/api/v1/auth/google";

    /// <summary>External sign-in providers available on this deployment (unconfigured ones are reported disabled).</summary>
    [AllowAnonymous]
    [HttpGet("providers")]
    [EnableRateLimiting(RateLimitPolicies.Public)]
    public AuthProvidersResponse Providers() => new(new ProviderStatus(google.Enabled));

    /// <summary>Starts a Google sign-in: returns the Google authorization URL and sets the flow cookie.</summary>
    [AllowAnonymous]
    [HttpPost("google/start")]
    public GoogleStartResponse Start(GoogleStartRequest request) =>
        StartFlow(GoogleFlowProtector.FlowModeSignIn, null, request.ReturnTo);

    /// <summary>
    /// Finishes a Google sign-in: exchanges the code, validates the ID token and signs in (sets the refresh cookie),
    /// reports <c>needsTerms</c> for a new user, or <c>linked</c> for the profile linking flow.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("google/callback")]
    public async Task<GoogleCallbackResponse> Callback(GoogleCallbackRequest request, CancellationToken ct)
    {
        var flowCookie = Request.Cookies[FlowCookie];
        // Single use: whatever happens, this attempt's nonce and verifier are gone.
        ClearFlowCookie();
        var result = await google.CallbackAsync(request, flowCookie, ct);
        if (result.Session is { } session) AuthController.SetRefreshCookie(Response, security.Value, session.RefreshToken, session.RefreshExpiresAt);
        return result.Response;
    }

    /// <summary>Creates the account of a new Google user once the terms are accepted, and signs them in.</summary>
    [AllowAnonymous]
    [HttpPost("google/complete")]
    public async Task<AuthResponse> Complete(GoogleCompleteRequest request, CancellationToken ct)
    {
        var session = await google.CompleteAsync(request, ct);
        AuthController.SetRefreshCookie(Response, security.Value, session.RefreshToken, session.RefreshExpiresAt);
        return session.Response;
    }

    /// <summary>The caller's sign-in methods (password yes/no, connected providers).</summary>
    [Authorize]
    [HttpGet("external-logins")]
    [DisableRateLimiting]
    public Task<SignInMethodsResponse> SignInMethods(CancellationToken ct) => google.GetSignInMethodsAsync(currentUser.Id, ct);

    /// <summary>Starts connecting Google to the signed-in account (profile).</summary>
    [DeniedWhileImpersonating] // a linked Google account is a credential
    [Authorize]
    [HttpPost("external-logins/google/start")]
    public GoogleStartResponse StartLink(GoogleStartRequest request) =>
        StartFlow(GoogleFlowProtector.FlowModeLink, currentUser.Id, request.ReturnTo);

    /// <summary>Disconnects Google (refused when it is the account's only sign-in method).</summary>
    [DeniedWhileImpersonating] // a linked Google account is a credential
    [Authorize]
    [HttpDelete("external-logins/google")]
    public async Task<IActionResult> Unlink(CancellationToken ct)
    {
        await google.UnlinkAsync(currentUser.Id, ct);
        return NoContent();
    }

    private GoogleStartResponse StartFlow(string mode, Guid? userId, string? returnTo)
    {
        var start = google.Start(mode, userId, returnTo);
        Response.Cookies.Append(FlowCookie, start.FlowCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = security.Value.SecureCookies,
            // The callback is a same-origin fetch from the SPA page Google redirected to, so Strict still sends it.
            SameSite = SameSiteMode.Strict,
            Path = FlowCookiePath,
            Expires = start.FlowExpiresAt,
            IsEssential = true,
        });
        return new GoogleStartResponse(start.AuthorizationUrl);
    }

    private void ClearFlowCookie() =>
        Response.Cookies.Delete(FlowCookie, new CookieOptions
        {
            HttpOnly = true, Secure = security.Value.SecureCookies, SameSite = SameSiteMode.Strict, Path = FlowCookiePath,
        });
}
