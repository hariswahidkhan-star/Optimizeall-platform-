using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OptimizeAll.Api.Modules.Auth.Google;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.UnitTests.Auth;

/// <summary>
/// Google ID-token validation against a locally generated RSA key served by a stub JWKS endpoint (no internet).
/// </summary>
public sealed class GoogleIdTokenValidatorTests
{
    private const string ClientId = "client-123.apps.googleusercontent.com";
    private const string Nonce = "nonce-abc";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly StubJwksHandler _handler;
    private readonly RSA _key = RSA.Create(2048);

    public GoogleIdTokenValidatorTests()
    {
        _handler = new StubJwksHandler(() => Jwks((_key, "key-1")));
    }

    private GoogleIdTokenValidator Validator(params string[] hostedDomains)
    {
        var options = Options.Create(new GoogleAuthOptions { ClientId = ClientId, ClientSecret = "secret", AllowedHostedDomains = hostedDomains });
        var jwks = new GoogleJwksProvider(new StubHttpClientFactory(_handler), _clock, NullLogger<GoogleJwksProvider>.Instance);
        return new GoogleIdTokenValidator(jwks, options, _clock);
    }

    private string Token(Action<Dictionary<string, object>>? edit = null, RSA? signWith = null, string kid = "key-1",
        string issuer = "https://accounts.google.com", string audience = ClientId, TimeSpan? lifetime = null)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = "10769150350006150715113082367",
            ["email"] = "sara@example.com",
            ["email_verified"] = true,
            ["name"] = "Sara Khan",
            ["nonce"] = Nonce,
        };
        edit?.Invoke(claims);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(lifetime ?? TimeSpan.FromHours(1)),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(signWith ?? _key) { KeyId = kid }, SecurityAlgorithms.RsaSha256),
        });
    }

    private static async Task<DomainException> Rejects(Task task) => await Assert.ThrowsAsync<DomainException>(() => task);

    [Fact]
    public async Task Valid_token_yields_the_identity()
    {
        var identity = await Validator().ValidateAsync(Token(), Nonce, default);
        Assert.Equal("10769150350006150715113082367", identity.Subject);
        Assert.Equal("sara@example.com", identity.Email);
        Assert.True(identity.EmailVerified);
        Assert.Equal("Sara Khan", identity.Name);
    }

    [Theory]
    [InlineData("accounts.google.com")]
    [InlineData("https://accounts.google.com")]
    public async Task Both_google_issuer_spellings_are_accepted(string issuer)
    {
        var identity = await Validator().ValidateAsync(Token(issuer: issuer), Nonce, default);
        Assert.Equal("sara@example.com", identity.Email);
    }

    [Fact]
    public async Task Other_issuers_are_rejected() =>
        Assert.Equal("auth.google_token_invalid",
            (await Rejects(Validator().ValidateAsync(Token(issuer: "https://evil.example.com"), Nonce, default))).Code);

    [Fact]
    public async Task Tokens_for_another_client_are_rejected() =>
        Assert.Equal("auth.google_token_invalid",
            (await Rejects(Validator().ValidateAsync(Token(audience: "someone-else.apps.googleusercontent.com"), Nonce, default))).Code);

    [Fact]
    public async Task Expired_tokens_are_rejected_after_the_clock_skew()
    {
        var token = Token(lifetime: TimeSpan.FromMinutes(5));
        _clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30));
        await Validator().ValidateAsync(token, Nonce, default); // within the 60 s skew
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("auth.google_token_invalid", (await Rejects(Validator().ValidateAsync(token, Nonce, default))).Code);
    }

    [Fact]
    public async Task The_nonce_must_match_the_sign_in_attempt()
    {
        Assert.Equal("auth.google_token_invalid", (await Rejects(Validator().ValidateAsync(Token(), "another-nonce", default))).Code);
        Assert.Equal("auth.google_token_invalid",
            (await Rejects(Validator().ValidateAsync(Token(c => c.Remove("nonce")), Nonce, default))).Code);
    }

    [Fact]
    public async Task Unverified_google_emails_are_rejected()
    {
        var error = await Rejects(Validator().ValidateAsync(Token(c => c["email_verified"] = false), Nonce, default));
        Assert.Equal("auth.google_email_unverified", error.Code);
        error = await Rejects(Validator().ValidateAsync(Token(c => c.Remove("email_verified")), Nonce, default));
        Assert.Equal("auth.google_email_unverified", error.Code);
        // Google has also sent the flag as a string.
        await Validator().ValidateAsync(Token(c => c["email_verified"] = "true"), Nonce, default);
    }

    [Fact]
    public async Task Tokens_signed_by_another_key_are_rejected()
    {
        using var attacker = RSA.Create(2048);
        Assert.Equal("auth.google_token_invalid",
            (await Rejects(Validator().ValidateAsync(Token(signWith: attacker), Nonce, default))).Code);
        Assert.Equal("auth.google_token_invalid",
            (await Rejects(Validator().ValidateAsync(Token(signWith: attacker, kid: "unknown"), Nonce, default))).Code);
    }

    [Fact]
    public async Task Unsigned_and_symmetric_tokens_are_rejected()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var payload = new Dictionary<string, object>
        {
            ["iss"] = "https://accounts.google.com", ["aud"] = ClientId, ["sub"] = "1", ["email"] = "a@example.com",
            ["email_verified"] = true, ["nonce"] = Nonce, ["exp"] = new DateTimeOffset(now.AddHours(1)).ToUnixTimeSeconds(),
        };
        static string B64(string s) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(s));
        var none = $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64(JsonSerializer.Serialize(payload))}.";
        Assert.Equal("auth.google_token_invalid", (await Rejects(Validator().ValidateAsync(none, Nonce, default))).Code);

        var hs256 = new JsonWebTokenHandler().CreateToken(JsonSerializer.Serialize(payload),
            new SigningCredentials(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = "key-1" }, SecurityAlgorithms.HmacSha256));
        Assert.Equal("auth.google_token_invalid", (await Rejects(Validator().ValidateAsync(hs256, Nonce, default))).Code);
    }

    [Fact]
    public async Task Hosted_domain_restriction_is_enforced_when_configured()
    {
        var error = await Rejects(Validator("example.com").ValidateAsync(Token(), Nonce, default));
        Assert.Equal("auth.google_domain_not_allowed", error.Code);
        error = await Rejects(Validator("example.com").ValidateAsync(Token(c => c["hd"] = "other.com"), Nonce, default));
        Assert.Equal("auth.google_domain_not_allowed", error.Code);
        var identity = await Validator("example.com").ValidateAsync(Token(c => c["hd"] = "Example.com"), Nonce, default);
        Assert.Equal("Example.com", identity.HostedDomain);
    }

    [Fact]
    public async Task Signing_keys_are_cached_and_refreshed_when_google_rotates_them()
    {
        var validator = Validator();
        await validator.ValidateAsync(Token(), Nonce, default);
        await validator.ValidateAsync(Token(), Nonce, default);
        Assert.Equal(1, _handler.Requests);

        // Google rotates to a new key: the unknown kid forces one refresh.
        using var rotated = RSA.Create(2048);
        _handler.Respond = () => Jwks((_key, "key-1"), (rotated, "key-2"));
        _clock.Advance(TimeSpan.FromMinutes(2));
        await validator.ValidateAsync(Token(signWith: rotated, kid: "key-2"), Nonce, default);
        Assert.Equal(2, _handler.Requests);

        // Forged tokens with unknown kids cannot make the API hammer the JWKS endpoint.
        using var attacker = RSA.Create(2048);
        for (var i = 0; i < 3; i++)
            await Rejects(validator.ValidateAsync(Token(signWith: attacker, kid: "key-x"), Nonce, default));
        Assert.Equal(2, _handler.Requests);

        // The cache expires after max-age (1 hour in the stub response).
        _clock.Advance(TimeSpan.FromHours(1));
        await validator.ValidateAsync(Token(), Nonce, default);
        Assert.Equal(3, _handler.Requests);
    }

    [Fact]
    public async Task Tokens_with_extra_audiences_or_another_authorized_party_are_rejected()
    {
        // Multiple audiences: OIDC requires rejecting audiences the client doesn't trust.
        var multi = Token(c => c["aud"] = new[] { ClientId, "someone-else.apps.googleusercontent.com" }, audience: null!);
        Assert.Equal("auth.google_token_invalid", (await Rejects(Validator().ValidateAsync(multi, Nonce, default))).Code);

        var otherAzp = Token(c => c["azp"] = "someone-else.apps.googleusercontent.com");
        Assert.Equal("auth.google_token_invalid", (await Rejects(Validator().ValidateAsync(otherAzp, Nonce, default))).Code);

        var ownAzp = Token(c => c["azp"] = ClientId);
        Assert.Equal("sara@example.com", (await Validator().ValidateAsync(ownAzp, Nonce, default)).Email);
    }

    [Fact]
    public async Task A_jwks_outage_keeps_the_last_keys_and_backs_off()
    {
        var validator = Validator();
        await validator.ValidateAsync(Token(), Nonce, default);
        Assert.Equal(1, _handler.Requests);

        // The cache expires while Google's JWKS endpoint is failing: the last good keys keep working...
        _handler.Status = HttpStatusCode.ServiceUnavailable;
        _clock.Advance(TimeSpan.FromHours(2));
        for (var i = 0; i < 5; i++)
            await validator.ValidateAsync(Token(), Nonce, default);
        // ...and the endpoint is retried at most once a minute, not on every sign-in.
        Assert.Equal(2, _handler.Requests);

        _clock.Advance(TimeSpan.FromMinutes(2));
        _handler.Status = HttpStatusCode.OK;
        await validator.ValidateAsync(Token(), Nonce, default);
        await validator.ValidateAsync(Token(), Nonce, default);
        Assert.Equal(3, _handler.Requests);
    }

    [Theory]
    [InlineData("amina@gmail.com", null, true)]
    [InlineData("Amina@GoogleMail.com", null, true)]
    [InlineData("amina@company.example", "company.example", true)]
    [InlineData("amina@company.example", null, false)]
    [InlineData("amina@gmail.com.evil.example", null, false)]
    [InlineData("amina@evil-gmail.com", null, false)]
    public void Google_is_authoritative_only_for_gmail_and_workspace_addresses(string email, string? hd, bool expected) =>
        Assert.Equal(expected, GoogleSignInService.IsGoogleAuthoritative(new GoogleIdentity("1", email, true, null, hd)));

    [Fact]
    public void Pkce_challenge_follows_rfc7636()
    {
        // RFC 7636 appendix B.
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            GoogleOidcClient.CodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        var verifier = GoogleOidcClient.NewCodeVerifier();
        Assert.Equal(43, verifier.Length);
        Assert.NotEqual(verifier, GoogleOidcClient.NewCodeVerifier());
    }

    [Theory]
    [InlineData("/app/campaigns?tab=open", "/app/campaigns?tab=open")]
    [InlineData("  /app  ", "/app")]
    [InlineData("//evil.example.com", null)]
    [InlineData("/\\evil.example.com", null)]
    [InlineData("https://evil.example.com", null)]
    [InlineData("app", null)]
    [InlineData("/a\nb", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Return_paths_must_be_same_origin(string? input, string? expected) =>
        Assert.Equal(expected, GoogleSignInService.SafeReturnTo(input));

    private static string Jwks(params (RSA Key, string Kid)[] keys) => JsonSerializer.Serialize(new
    {
        keys = keys.Select(k =>
        {
            var p = k.Key.ExportParameters(false);
            return new { kty = "RSA", use = "sig", alg = "RS256", kid = k.Kid, n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) };
        }),
    });

    private sealed class StubJwksHandler(Func<string> respond) : HttpMessageHandler
    {
        public Func<string> Respond { get; set; } = respond;
        public int Requests { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(GoogleEndpoints.Jwks, request.RequestUri!.ToString());
            Requests++;
            if (Status != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(Status));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Respond(), Encoding.UTF8, "application/json") };
            response.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromHours(1) };
            return Task.FromResult(response);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
