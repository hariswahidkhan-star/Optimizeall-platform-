using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Modules.Auth.Google;

/// <summary>The claims of a validated Google ID token that sign-in relies on.</summary>
public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? Name, string? HostedDomain);

/// <summary>
/// Google's signing keys (JWKS), cached for the response's <c>Cache-Control: max-age</c> (clamped to 5 min – 24 h,
/// default 1 h). A token signed with an unknown key id triggers at most one forced refresh per minute (key rotation),
/// so forged tokens cannot turn the API into a JWKS request amplifier.
/// </summary>
public sealed class GoogleJwksProvider(IHttpClientFactory httpClients, TimeProvider clock, ILogger<GoogleJwksProvider> logger)
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ForcedRefreshInterval = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<SecurityKey>? _keys;
    private DateTimeOffset _expiresAt;
    private DateTimeOffset _lastFetch = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<SecurityKey>> GetKeysAsync(bool forceRefresh, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (_keys is not null && now < _expiresAt && (!forceRefresh || now - _lastFetch < ForcedRefreshInterval))
            return _keys;

        await _gate.WaitAsync(ct);
        try
        {
            now = clock.GetUtcNow();
            if (_keys is not null && now < _expiresAt && (!forceRefresh || now - _lastFetch < ForcedRefreshInterval))
                return _keys;

            using var response = await httpClients.CreateClient(GoogleEndpoints.HttpClientName).GetAsync(GoogleEndpoints.Jwks, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Fetching Google signing keys failed with {Status}", (int)response.StatusCode);
                if (_keys is not null) return KeepStaleKeys(now); // keep serving the last good set
                throw Unavailable();
            }
            var json = await response.Content.ReadAsStringAsync(ct);
            var keys = new JsonWebKeySet(json).GetSigningKeys();
            var lifetime = response.Headers.CacheControl?.MaxAge ?? DefaultLifetime;
            lifetime = lifetime < MinLifetime ? MinLifetime : lifetime > MaxLifetime ? MaxLifetime : lifetime;
            _keys = keys.ToArray();
            _lastFetch = now;
            _expiresAt = now + lifetime;
            return _keys;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException or System.Text.Json.JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            logger.LogWarning(ex, "Fetching Google signing keys failed");
            if (_keys is not null) return KeepStaleKeys(clock.GetUtcNow());
            throw Unavailable();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Google's JWKS endpoint is failing: keep the last good keys and wait before trying again, so an outage doesn't
    /// make every sign-in queue behind a fresh (timing-out) fetch.
    /// </summary>
    private IReadOnlyList<SecurityKey> KeepStaleKeys(DateTimeOffset now)
    {
        _lastFetch = now;
        if (_expiresAt < now + ForcedRefreshInterval) _expiresAt = now + ForcedRefreshInterval;
        return _keys!;
    }

    private static DomainException Unavailable() =>
        new("auth.google_unavailable", "Google sign-in is temporarily unavailable. Try again in a moment.");
}

/// <summary>
/// Validates a Google ID token: RS256 signature against Google's JWKS, issuer (<c>accounts.google.com</c> or
/// <c>https://accounts.google.com</c>), audience = only the configured client id (and <c>azp</c>, when present), expiry (against the app clock, 60 s skew),
/// the nonce bound to this sign-in attempt, <c>email_verified = true</c> and, when configured, the hosted domain.
/// </summary>
public sealed class GoogleIdTokenValidator(GoogleJwksProvider jwks, IOptions<GoogleAuthOptions> options, TimeProvider clock)
{
    private static readonly JsonWebTokenHandler Handler = new() { MapInboundClaims = false };

    public async Task<GoogleIdentity> ValidateAsync(string idToken, string expectedNonce, CancellationToken ct)
    {
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.ClientId)) throw Invalid();
        if (string.IsNullOrWhiteSpace(idToken) || idToken.Length > 16_384) throw Invalid();

        string? kid;
        try
        {
            kid = new JsonWebToken(idToken).Kid;
        }
        catch (ArgumentException)
        {
            throw Invalid();
        }

        var keys = await jwks.GetKeysAsync(false, ct);
        // An unknown key id usually means Google rotated its keys: refresh once (rate limited by the provider).
        if (kid is not null && !keys.Any(k => k.KeyId == kid))
            keys = await jwks.GetKeysAsync(true, ct);
        var result = await ValidateWithKeysAsync(idToken, config.ClientId, keys);
        if (!result.IsValid) throw Invalid();

        if (result.SecurityToken is not JsonWebToken claims) throw Invalid();
        // OIDC Core 3.1.3.7: the token must not list audiences this client doesn't trust, and an authorized party, when
        // present, must be this client (the library only checks that the client id is among the audiences).
        if (claims.Audiences.Any(a => !string.Equals(a, config.ClientId, StringComparison.Ordinal))) throw Invalid();
        var azp = StringClaim(claims, "azp");
        if (azp is not null && !string.Equals(azp, config.ClientId, StringComparison.Ordinal)) throw Invalid();
        var nonce = StringClaim(claims, "nonce");
        if (nonce is null || !FixedTimeEquals(nonce, expectedNonce)) throw Invalid();

        var subject = StringClaim(claims, "sub");
        var email = StringClaim(claims, "email");
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 255 || string.IsNullOrWhiteSpace(email) || email.Length > 254)
            throw Invalid();

        if (!BoolClaim(claims, "email_verified"))
            throw new DomainException("auth.google_email_unverified",
                "Your Google account's email address isn't verified. Verify it with Google, or register with a password.");

        var hostedDomain = StringClaim(claims, "hd");
        if (config.AllowedHostedDomains.Length > 0 &&
            (hostedDomain is null || !config.AllowedHostedDomains.Any(d => string.Equals(d.Trim(), hostedDomain, StringComparison.OrdinalIgnoreCase))))
            throw DomainException.Forbidden("auth.google_domain_not_allowed",
                "This Google account's organization isn't allowed to sign in here.");

        var name = StringClaim(claims, "name");
        return new GoogleIdentity(subject, email.Trim(), true, string.IsNullOrWhiteSpace(name) ? null : name.Trim(), hostedDomain);
    }

    private Task<TokenValidationResult> ValidateWithKeysAsync(string token, string clientId, IReadOnlyList<SecurityKey> keys) =>
        Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuers = GoogleEndpoints.Issuers,
            ValidAudience = clientId,
            IssuerSigningKeys = keys,
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(60),
            LifetimeValidator = (notBefore, expires, _, parameters) =>
            {
                var now = clock.GetUtcNow().UtcDateTime;
                return expires is not null &&
                       (notBefore is null || notBefore.Value <= now.Add(parameters.ClockSkew)) &&
                       expires.Value >= now.Subtract(parameters.ClockSkew);
            },
        });

    private static string? StringClaim(JsonWebToken token, string name) =>
        token.TryGetPayloadValue<string>(name, out var value) ? value : null;

    /// <summary>A JSON boolean true, or the string "true" (Google has used both).</summary>
    private static bool BoolClaim(JsonWebToken token, string name) =>
        (token.TryGetPayloadValue<bool>(name, out var flag) && flag) ||
        (token.TryGetPayloadValue<string>(name, out var text) && string.Equals(text, "true", StringComparison.OrdinalIgnoreCase));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    internal static DomainException Invalid() =>
        new("auth.google_token_invalid", "Google sign-in could not be verified. Please try again.");
}
