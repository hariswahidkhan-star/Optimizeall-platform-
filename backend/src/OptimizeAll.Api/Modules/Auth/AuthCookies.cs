using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Auth;

/// <summary>
/// The session cookies. Both are HttpOnly, SameSite=Strict, Secure (production) and scoped to <c>/api/v1/auth</c>, so
/// they only ever reach refresh/logout/exit.
/// </summary>
public static class AuthCookies
{
    public const string Refresh = "oa_refresh";

    /// <summary>Opaque token of a live impersonation session, next to (never replacing) the staff member's refresh cookie.</summary>
    public const string Impersonation = "oa_impersonation";

    public const string Path = "/api/v1/auth";

    public static void Set(HttpResponse response, SecurityOptions security, string name, string value, DateTime expiresAt) =>
        response.Cookies.Append(name, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = security.SecureCookies,
            SameSite = SameSiteMode.Strict,
            Path = Path,
            Expires = expiresAt,
            IsEssential = true,
        });

    public static void Clear(HttpResponse response, SecurityOptions security, string name) =>
        response.Cookies.Delete(name, new CookieOptions
        {
            HttpOnly = true, Secure = security.SecureCookies, SameSite = SameSiteMode.Strict, Path = Path,
        });
}
