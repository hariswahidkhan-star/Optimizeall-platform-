using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OptimizeAll.Api.Modules.Auth.Google;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Auth;

/// <summary>
/// An API host with Google sign-in configured, talking to <see cref="FakeGoogle"/> (JWKS + token endpoint served by a
/// stub HttpMessageHandler, locally generated RSA key) instead of the internet. Shares the database of <see cref="Api"/>.
/// </summary>
public sealed class GoogleSignInFixture : IAsyncLifetime
{
    public const string ClientId = "test-client.apps.googleusercontent.com";
    public const string ClientSecret = "test-client-secret";

    public ApiFactory Api { get; } = new();
    public FakeGoogle Google { get; } = new();
    public WebApplicationFactory<Program> App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Api.InitializeAsync();
        App = Api.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Google:ClientId"] = ClientId,
                ["Authentication:Google:ClientSecret"] = ClientSecret,
            }));
            builder.ConfigureServices(services =>
                services.AddHttpClient(GoogleEndpoints.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Google));
        });
        _ = App.Services;
    }

    public async Task DisposeAsync()
    {
        await App.DisposeAsync();
        await Api.DisposeAsync();
    }
}

/// <summary>Google's token endpoint and JWKS, in memory. Codes are single use and bound to a PKCE challenge.</summary>
public sealed class FakeGoogle : HttpMessageHandler
{
    public const string ExpectedRedirectUri = "http://app.test/auth/google/callback";

    private readonly RSA _key = RSA.Create(2048);
    private readonly ConcurrentDictionary<string, (string Challenge, string IdToken)> _codes = new();

    public DateTime Now { get; set; } = DateTime.UtcNow;

    /// <summary>Registers an authorization code that Google would redirect back with.</summary>
    public string IssueCode(string codeChallenge, string idToken)
    {
        var code = "4/" + Guid.NewGuid().ToString("N");
        _codes[code] = (codeChallenge, idToken);
        return code;
    }

    public string IdToken(string subject, string email, string nonce, bool emailVerified = true, string? name = "Google User",
        string audience = GoogleSignInFixture.ClientId, RSA? signWith = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = subject, ["email"] = email, ["email_verified"] = emailVerified, ["nonce"] = nonce,
        };
        if (name is not null) claims["name"] = name;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://accounts.google.com",
            Audience = audience,
            IssuedAt = Now,
            NotBefore = Now,
            Expires = Now.AddHours(1),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(signWith ?? _key) { KeyId = "fake-key" }, SecurityAlgorithms.RsaSha256),
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        if (request.Method == HttpMethod.Get && url == GoogleEndpoints.Jwks)
        {
            var p = _key.ExportParameters(false);
            return Json(HttpStatusCode.OK, new
            {
                keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid = "fake-key", n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) } },
            });
        }
        if (request.Method == HttpMethod.Post && url == GoogleEndpoints.Token)
        {
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (form["grant_type"] != "authorization_code" || form["client_id"] != GoogleSignInFixture.ClientId ||
                form["client_secret"] != GoogleSignInFixture.ClientSecret || form["redirect_uri"] != ExpectedRedirectUri)
                return Json(HttpStatusCode.BadRequest, new { error = "invalid_client" });
            if (!_codes.TryRemove(form["code"].ToString(), out var issued))
                return Json(HttpStatusCode.BadRequest, new { error = "invalid_grant" });
            var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString())));
            if (challenge != issued.Challenge)
                return Json(HttpStatusCode.BadRequest, new { error = "invalid_grant", error_description = "PKCE verification failed" });
            return Json(HttpStatusCode.OK, new { access_token = "ya29.fake", id_token = issued.IdToken, token_type = "Bearer", expires_in = 3599 });
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}
