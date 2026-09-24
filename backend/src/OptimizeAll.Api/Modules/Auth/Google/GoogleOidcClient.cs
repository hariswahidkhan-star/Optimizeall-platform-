using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Modules.Auth.Google;

/// <summary>Builds Google authorization URLs and exchanges authorization codes (server side, with PKCE).</summary>
public sealed class GoogleOidcClient(
    IHttpClientFactory httpClients,
    IOptions<GoogleAuthOptions> options,
    IOptions<EmailOptions> emailOptions,
    ILogger<GoogleOidcClient> logger)
{
    /// <summary>The redirect URI registered with Google (configuration, never the request).</summary>
    public string RedirectUri =>
        options.Value.RedirectUri is { Length: > 0 } configured
            ? configured
            : emailOptions.Value.AppBaseUrl.TrimEnd('/') + AppLinks.GoogleCallback;

    public string BuildAuthorizationUrl(string state, string nonce, string codeVerifier)
    {
        var config = options.Value;
        var query = new Dictionary<string, string>
        {
            ["client_id"] = config.ClientId!,
            ["redirect_uri"] = RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = CodeChallenge(codeVerifier),
            ["code_challenge_method"] = "S256",
            ["prompt"] = "select_account",
            ["access_type"] = "online",
        };
        // With exactly one allowed Workspace domain, ask Google to preselect it (the hd claim is still enforced).
        if (config.AllowedHostedDomains is [var onlyDomain]) query["hd"] = onlyDomain.Trim();
        return GoogleEndpoints.Authorization + "?" +
               string.Join('&', query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    /// <summary>Exchanges the authorization code at Google's token endpoint and returns the raw ID token.</summary>
    public async Task<string> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
    {
        var config = options.Value;
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = config.ClientId!,
            ["client_secret"] = config.ClientSecret!,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = codeVerifier,
        });
        try
        {
            using var response = await httpClients.CreateClient(GoogleEndpoints.HttpClientName).PostAsync(GoogleEndpoints.Token, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                // Google answers invalid_grant for a reused/expired code or a PKCE mismatch. Never log the body's tokens.
                logger.LogInformation("Google code exchange failed with {Status}", (int)response.StatusCode);
                throw ExchangeFailed();
            }
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("id_token", out var idToken) && idToken.ValueKind == JsonValueKind.String)
                return idToken.GetString()!;
            throw ExchangeFailed();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Google code exchange failed");
            throw ExchangeFailed();
        }
    }

    /// <summary>A PKCE code verifier: 32 random bytes, base64url (43 characters).</summary>
    public static string NewCodeVerifier() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>The S256 code challenge for a verifier (RFC 7636).</summary>
    public static string CodeChallenge(string codeVerifier) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    private static DomainException ExchangeFailed() =>
        new("auth.google_exchange_failed", "Google sign-in didn't complete. Please try again.");
}
