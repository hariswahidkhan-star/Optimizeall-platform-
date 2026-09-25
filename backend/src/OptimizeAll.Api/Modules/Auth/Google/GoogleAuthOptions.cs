namespace OptimizeAll.Api.Modules.Auth.Google;

/// <summary>
/// "Sign in with Google" (OpenID Connect, authorization-code flow with PKCE). Configuration section
/// <c>Authentication:Google</c>. The feature is enabled only when both the client id and the client secret are set;
/// otherwise <c>GET /api/v1/auth/providers</c> reports it disabled and every Google endpoint answers 404.
/// The client secret must come from the environment (<c>Authentication__Google__ClientSecret</c>) or user-secrets,
/// never from a committed file.
/// </summary>
public sealed class GoogleAuthOptions
{
    public const string Section = "Authentication:Google";

    /// <summary>OAuth client id (the ID token audience).</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth client secret (environment / user-secrets only).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Optional Google Workspace domains (<c>hd</c> claim) allowed to sign in; empty = any Google account.</summary>
    public string[] AllowedHostedDomains { get; set; } = Array.Empty<string>();

    /// <summary>
    /// The redirect URI registered in Google Cloud. Default: <c>{public origin}/auth/google/callback</c> (IPublicOrigin: site URL → Email:AppBaseUrl → request). It is never
    /// taken from the request.
    /// </summary>
    public string? RedirectUri { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>Google's OpenID Connect endpoints (fixed; see https://accounts.google.com/.well-known/openid-configuration).</summary>
public static class GoogleEndpoints
{
    public const string Authorization = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string Token = "https://oauth2.googleapis.com/token";
    public const string Jwks = "https://www.googleapis.com/oauth2/v3/certs";

    /// <summary>Both issuer spellings Google documents for ID tokens.</summary>
    public static readonly string[] Issuers = { "accounts.google.com", "https://accounts.google.com" };

    /// <summary>Named HttpClient used for the token endpoint and the JWKS (tests replace its primary handler).</summary>
    public const string HttpClientName = "google-oidc";
}
