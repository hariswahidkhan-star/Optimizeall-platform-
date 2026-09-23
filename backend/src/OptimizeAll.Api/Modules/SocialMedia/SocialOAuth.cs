using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.SocialMedia;

namespace OptimizeAll.Api.Modules.SocialMedia;

public sealed record OAuthTokenResult(
    bool Success, string? AccessToken, string? RefreshToken, DateTime? ExpiresAt, string? ExternalId, string? DisplayName, string? Error)
{
    public static OAuthTokenResult Fail(string error) => new(false, null, null, null, null, null, error);
}

/// <summary>OAuth 2.0 authorization-code flow for one or more networks (authorization URL, code exchange, refresh).</summary>
public interface ISocialOAuthClient
{
    IReadOnlyCollection<SocialNetwork> Networks { get; }

    string AuthorizationUrl(SocialNetwork network, IntegrationCredentials app, string redirectUri, string state, string pkceChallenge);

    Task<OAuthTokenResult> ExchangeAsync(BrandProfile profile, IntegrationCredentials app, string code, string redirectUri, string codeVerifier,
        CancellationToken ct);

    Task<OAuthTokenResult> RefreshAsync(BrandProfile profile, IntegrationCredentials app, string refreshToken, CancellationToken ct);
}

internal static class OAuthHelpers
{
    public static string Q(IDictionary<string, string> values) =>
        string.Join('&', values.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

    public static JsonElement Parse(string raw)
    {
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw).RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }

    public static string? Str(JsonElement e, string name) => MetaGraphClient.Str(e, name);

    public static long? Long(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : null;

    public static string ClientId(IntegrationCredentials app) => app.Settings[SocialProviders.ClientIdKey(app.Provider)];

    public static string ClientSecret(IntegrationCredentials app) => app.Secrets[SocialProviders.ClientSecretKey(app.Provider)];
}

/// <summary>
/// Meta (Facebook Login for Business): exchanges the code for a user token, upgrades it to a long-lived token and picks
/// the Page (or the Page's Instagram business account) that matches the profile, storing the Page access token.
/// </summary>
public sealed class MetaOAuthClient(IHttpClientFactory factory, IOptions<SocialMediaOptions> options) : ISocialOAuthClient
{
    public const string Scopes = "pages_show_list,pages_read_engagement,pages_manage_posts,pages_manage_engagement,read_insights," +
                                 "instagram_basic,instagram_content_publish,instagram_manage_comments,instagram_manage_insights,business_management";

    public IReadOnlyCollection<SocialNetwork> Networks => new[] { SocialNetwork.Facebook, SocialNetwork.Instagram };

    private string Graph => options.Value.GraphApiBaseUrl.TrimEnd('/');

    public string AuthorizationUrl(SocialNetwork network, IntegrationCredentials app, string redirectUri, string state, string pkceChallenge)
    {
        var version = Graph.Split('/').LastOrDefault(s => s.StartsWith('v')) ?? "v20.0";
        return $"https://www.facebook.com/{version}/dialog/oauth?" + OAuthHelpers.Q(new Dictionary<string, string>
        {
            ["client_id"] = OAuthHelpers.ClientId(app),
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["response_type"] = "code",
            ["scope"] = Scopes,
        });
    }

    public async Task<OAuthTokenResult> ExchangeAsync(BrandProfile profile, IntegrationCredentials app, string code, string redirectUri,
        string codeVerifier, CancellationToken ct)
    {
        var http = factory.CreateClient(MetaGraphClient.HttpClientName);
        var shortToken = await GetAsync(http, $"{Graph}/oauth/access_token?" + OAuthHelpers.Q(new Dictionary<string, string>
        {
            ["client_id"] = OAuthHelpers.ClientId(app),
            ["client_secret"] = OAuthHelpers.ClientSecret(app),
            ["redirect_uri"] = redirectUri,
            ["code"] = code,
        }), ct);
        var userToken = OAuthHelpers.Str(shortToken, "access_token");
        if (userToken is null) return OAuthTokenResult.Fail("Meta did not return an access token: " + Error(shortToken));

        var longToken = await GetAsync(http, $"{Graph}/oauth/access_token?" + OAuthHelpers.Q(new Dictionary<string, string>
        {
            ["grant_type"] = "fb_exchange_token",
            ["client_id"] = OAuthHelpers.ClientId(app),
            ["client_secret"] = OAuthHelpers.ClientSecret(app),
            ["fb_exchange_token"] = userToken,
        }), ct);
        userToken = OAuthHelpers.Str(longToken, "access_token") ?? userToken;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Graph}/me/accounts?fields=id,name,access_token,instagram_business_account{{id,username}}&limit=100");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        using var response = await http.SendAsync(request, ct);
        var pages = OAuthHelpers.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!pages.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
            return OAuthTokenResult.Fail("No Facebook Page was granted. Choose the client's Page in the Meta dialog: " + Error(pages));

        foreach (var page in data.EnumerateArray())
        {
            var pageId = OAuthHelpers.Str(page, "id");
            var pageToken = OAuthHelpers.Str(page, "access_token");
            var name = OAuthHelpers.Str(page, "name");
            if (pageId is null || pageToken is null) continue;
            if (profile.Network == SocialNetwork.Facebook)
            {
                if (profile.ExternalId is { Length: > 0 } wanted && wanted != pageId) continue;
                return new OAuthTokenResult(true, pageToken, null, null, pageId, name, null);
            }
            if (page.TryGetProperty("instagram_business_account", out var ig) && OAuthHelpers.Str(ig, "id") is { } igId)
            {
                var username = OAuthHelpers.Str(ig, "username");
                if (profile.ExternalId is { Length: > 0 } wantedIg && wantedIg != igId) continue;
                if (profile.ExternalId is null or "" && username is not null
                    && !string.Equals(username, profile.Handle.TrimStart('@'), StringComparison.OrdinalIgnoreCase) && data.GetArrayLength() > 1) continue;
                return new OAuthTokenResult(true, pageToken, null, null, igId, username, null);
            }
        }
        return OAuthTokenResult.Fail(profile.Network == SocialNetwork.Facebook
            ? "The granted Pages do not include this profile's Page id."
            : "No granted Page has this Instagram business account linked.");
    }

    public Task<OAuthTokenResult> RefreshAsync(BrandProfile profile, IntegrationCredentials app, string refreshToken, CancellationToken ct) =>
        Task.FromResult(OAuthTokenResult.Fail("Page tokens from a long-lived user token do not expire; reconnect if Meta revoked it."));

    private static async Task<JsonElement> GetAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        return OAuthHelpers.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    private static string Error(JsonElement e) =>
        e.TryGetProperty("error", out var err) ? OAuthHelpers.Str(err, "message") ?? err.ToString() : e.ToString();
}

/// <summary>X OAuth 2.0 with PKCE (confidential client): code exchange, refresh and the account id from <c>/2/users/me</c>.</summary>
public sealed class XOAuthClient(IHttpClientFactory factory, IOptions<SocialMediaOptions> options, TimeProvider clock) : ISocialOAuthClient
{
    public const string Scopes = "tweet.read tweet.write users.read offline.access";

    public IReadOnlyCollection<SocialNetwork> Networks => new[] { SocialNetwork.X };

    private string Api => options.Value.XApiBaseUrl.TrimEnd('/');

    public string AuthorizationUrl(SocialNetwork network, IntegrationCredentials app, string redirectUri, string state, string pkceChallenge) =>
        "https://x.com/i/oauth2/authorize?" + OAuthHelpers.Q(new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = OAuthHelpers.ClientId(app),
            ["redirect_uri"] = redirectUri,
            ["scope"] = Scopes,
            ["state"] = state,
            ["code_challenge"] = pkceChallenge,
            ["code_challenge_method"] = "S256",
        });

    public async Task<OAuthTokenResult> ExchangeAsync(BrandProfile profile, IntegrationCredentials app, string code, string redirectUri,
        string codeVerifier, CancellationToken ct)
    {
        var token = await TokenAsync(app, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
            ["client_id"] = OAuthHelpers.ClientId(app),
        }, ct);
        if (!token.Success) return token;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Api}/2/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var response = await factory.CreateClient(XPublisher.HttpClientName).SendAsync(request, ct);
        var me = OAuthHelpers.Parse(await response.Content.ReadAsStringAsync(ct));
        var data = me.TryGetProperty("data", out var d) ? d : default;
        return token with
        {
            ExternalId = data.ValueKind == JsonValueKind.Object ? OAuthHelpers.Str(data, "id") : null,
            DisplayName = data.ValueKind == JsonValueKind.Object ? OAuthHelpers.Str(data, "username") : null,
        };
    }

    public Task<OAuthTokenResult> RefreshAsync(BrandProfile profile, IntegrationCredentials app, string refreshToken, CancellationToken ct) =>
        TokenAsync(app, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = OAuthHelpers.ClientId(app),
        }, ct);

    private async Task<OAuthTokenResult> TokenAsync(IntegrationCredentials app, Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Api}/2/oauth2/token") { Content = new FormUrlEncodedContent(form) };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{OAuthHelpers.ClientId(app)}:{OAuthHelpers.ClientSecret(app)}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        using var response = await factory.CreateClient(XPublisher.HttpClientName).SendAsync(request, ct);
        var body = OAuthHelpers.Parse(await response.Content.ReadAsStringAsync(ct));
        var access = OAuthHelpers.Str(body, "access_token");
        if (!response.IsSuccessStatusCode || access is null)
            return OAuthTokenResult.Fail($"X token request failed ({(int)response.StatusCode}): {OAuthHelpers.Str(body, "error_description") ?? body.ToString()}");
        var expires = OAuthHelpers.Long(body, "expires_in") is { } seconds ? clock.GetUtcNow().UtcDateTime.AddSeconds(seconds) : (DateTime?)null;
        return new OAuthTokenResult(true, access, OAuthHelpers.Str(body, "refresh_token"), expires, null, null, null);
    }
}

/// <summary>
/// Builds the authorization URL for networks whose token exchange is not implemented yet (LinkedIn, TikTok, YouTube,
/// Pinterest, Google Business Profile). The exchange reports "not supported": paste a token instead (integrations.manage).
/// </summary>
public sealed class AuthorizeOnlyOAuthClient : ISocialOAuthClient
{
    public IReadOnlyCollection<SocialNetwork> Networks => new[]
    {
        SocialNetwork.LinkedIn, SocialNetwork.TikTok, SocialNetwork.YouTube, SocialNetwork.Pinterest, SocialNetwork.GoogleBusiness,
    };

    public string AuthorizationUrl(SocialNetwork network, IntegrationCredentials app, string redirectUri, string state, string pkceChallenge)
    {
        var id = OAuthHelpers.ClientId(app);
        return network switch
        {
            SocialNetwork.LinkedIn => "https://www.linkedin.com/oauth/v2/authorization?" + OAuthHelpers.Q(new Dictionary<string, string>
            {
                ["response_type"] = "code", ["client_id"] = id, ["redirect_uri"] = redirectUri, ["state"] = state,
                ["scope"] = "w_organization_social r_organization_social rw_organization_admin",
            }),
            SocialNetwork.TikTok => "https://www.tiktok.com/v2/auth/authorize/?" + OAuthHelpers.Q(new Dictionary<string, string>
            {
                ["client_key"] = id, ["response_type"] = "code", ["redirect_uri"] = redirectUri, ["state"] = state,
                ["scope"] = "user.info.basic,video.upload,video.publish", ["code_challenge"] = pkceChallenge, ["code_challenge_method"] = "S256",
            }),
            SocialNetwork.Pinterest => "https://www.pinterest.com/oauth/?" + OAuthHelpers.Q(new Dictionary<string, string>
            {
                ["client_id"] = id, ["redirect_uri"] = redirectUri, ["response_type"] = "code", ["state"] = state,
                ["scope"] = "boards:read,pins:read,pins:write,user_accounts:read",
            }),
            _ => "https://accounts.google.com/o/oauth2/v2/auth?" + OAuthHelpers.Q(new Dictionary<string, string>
            {
                ["client_id"] = id, ["redirect_uri"] = redirectUri, ["response_type"] = "code", ["state"] = state,
                ["access_type"] = "offline", ["prompt"] = "consent", ["code_challenge"] = pkceChallenge, ["code_challenge_method"] = "S256",
                ["scope"] = network == SocialNetwork.YouTube
                    ? "https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube.readonly"
                    : "https://www.googleapis.com/auth/business.manage",
            }),
        };
    }

    public Task<OAuthTokenResult> ExchangeAsync(BrandProfile profile, IntegrationCredentials app, string code, string redirectUri,
        string codeVerifier, CancellationToken ct) =>
        Task.FromResult(OAuthTokenResult.Fail(
            $"Token exchange for {PostValidator.Label(profile.Network)} is not implemented in this release. " +
            "An administrator can paste an access token for the profile instead (integrations.manage)."));

    public Task<OAuthTokenResult> RefreshAsync(BrandProfile profile, IntegrationCredentials app, string refreshToken, CancellationToken ct) =>
        Task.FromResult(OAuthTokenResult.Fail("Token refresh is not implemented for this network."));
}

public sealed class SocialOAuthRegistry(IEnumerable<ISocialOAuthClient> clients)
{
    public ISocialOAuthClient For(SocialNetwork network) =>
        clients.LastOrDefault(c => c.Networks.Contains(network)) ?? new AuthorizeOnlyOAuthClient();
}
