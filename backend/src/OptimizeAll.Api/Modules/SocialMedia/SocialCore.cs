using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.SocialMedia;

/// <summary>Configuration section "SocialMedia".</summary>
public sealed class SocialMediaOptions
{
    public const string Section = "SocialMedia";

    /// <summary>Public base URL of this API (e.g. https://api.example.com), used to give Meta absolute media URLs.</summary>
    public string? PublicApiBaseUrl { get; set; }

    /// <summary>Web-app URL of the OAuth callback page (default {Email:AppBaseUrl}/agency/social/connect/callback).</summary>
    public string? OAuthRedirectUri { get; set; }

    /// <summary>HMAC key for OAuth state. When empty a key is derived from Jwt:SigningKey.</summary>
    public string? OAuthStateSecret { get; set; }
    public string GraphApiBaseUrl { get; set; } = "https://graph.facebook.com/v20.0";
    public string XApiBaseUrl { get; set; } = "https://api.x.com";

    /// <summary>Seconds between Instagram container status checks (video processing).</summary>
    public int InstagramPollSeconds { get; set; } = 5;
    public int InstagramMaxPolls { get; set; } = 24;
    public int MaxPublishAttempts { get; set; } = 4;
}

/// <summary>Where app credentials (developer app id/secret) for each network live in the credential vault.</summary>
public static class SocialProviders
{
    /// <summary>Vault provider key for the developer app of a network (agency-wide connection).</summary>
    public static string AppProvider(SocialNetwork network) => network switch
    {
        SocialNetwork.Facebook or SocialNetwork.Instagram => "meta",
        SocialNetwork.X => "x",
        SocialNetwork.LinkedIn => "linkedin",
        SocialNetwork.TikTok => "tiktok",
        SocialNetwork.YouTube or SocialNetwork.GoogleBusiness => "google",
        SocialNetwork.Pinterest => "pinterest",
        _ => network.ToString().ToLowerInvariant(),
    };

    /// <summary>Vault provider key of a brand profile's own token row.</summary>
    public static string ProfileTokenProvider(SocialNetwork network) => "social-" + network.ToString().ToLowerInvariant();

    /// <summary>Settings key holding the app/client id, per app provider.</summary>
    public static string ClientIdKey(string appProvider) => appProvider switch
    {
        "meta" => "appId",
        "tiktok" => "clientKey",
        _ => "clientId",
    };

    public static string ClientSecretKey(string appProvider) => appProvider switch
    {
        "meta" => "appSecret",
        _ => "clientSecret",
    };
}

/// <summary>Tenant-scoped loading helpers shared by the social controllers.</summary>
public sealed class SocialAccess(AppDbContext db, IClientScope scope)
{
    public IClientScope Scope => scope;

    public async Task<ClientAccount> ClientAsync(Guid clientId, CancellationToken ct, ClientMemberRole role = ClientMemberRole.Viewer)
    {
        await scope.EnsureAccessAsync(clientId, role, ct);
        return await db.Set<ClientAccount>().AsNoTracking().FirstAsync(c => c.Id == clientId, ct);
    }

    public async Task<IQueryable<T>> ScopedAsync<T>(System.Linq.Expressions.Expression<Func<T, Guid>> clientId, CancellationToken ct) where T : class =>
        await scope.ApplyAsync(db.Set<T>().AsQueryable(), clientId, ct);

    public async Task<SocialPost> PostAsync(Guid id, CancellationToken ct, bool track = true)
    {
        var q = await scope.ApplyAsync(db.Set<SocialPost>().Include(p => p.Variants).AsQueryable(), p => p.ClientAccountId, ct);
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound("Post");
    }

    public async Task<BrandProfile> ProfileAsync(Guid id, CancellationToken ct)
    {
        var q = await scope.ApplyAsync(db.Set<BrandProfile>().AsQueryable(), p => p.ClientAccountId, ct);
        return await q.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound("Profile");
    }

    public async Task<T> OwnedAsync<T>(Guid id, System.Linq.Expressions.Expression<Func<T, Guid>> clientId, string what, CancellationToken ct)
        where T : Entity
    {
        var q = await scope.ApplyAsync(db.Set<T>().AsQueryable(), clientId, ct);
        return await q.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw DomainException.NotFound(what);
    }

    public async Task<SocialClientSettings> SettingsAsync(Guid clientId, CancellationToken ct) =>
        await db.Set<SocialClientSettings>().AsNoTracking().FirstOrDefaultAsync(s => s.ClientAccountId == clientId, ct)
        ?? new SocialClientSettings { ClientAccountId = clientId };
}

/// <summary>Reads the stored network presets (falling back to the built-in defaults for a missing network).</summary>
public sealed class NetworkPresetProvider(AppDbContext db)
{
    private Dictionary<SocialNetwork, NetworkRules>? _cache;

    public async Task<IReadOnlyDictionary<SocialNetwork, NetworkRules>> AllAsync(CancellationToken ct)
    {
        if (_cache is not null) return _cache;
        var stored = await db.Set<SocialNetworkPreset>().AsNoTracking().ToListAsync(ct);
        _cache = NetworkPresets.Defaults.ToDictionary(d => d.Network, d => d);
        foreach (var p in stored) _cache[p.Network] = NetworkRules.From(p);
        return _cache;
    }

    public async Task<NetworkRules> ForAsync(SocialNetwork network, CancellationToken ct) => (await AllAsync(ct))[network];
}

/// <summary>Checks whether the developer app credentials of a network are configured in the vault.</summary>
public sealed class SocialAppCredentials(ICredentialVault vault)
{
    private readonly Dictionary<string, IntegrationCredentials?> _cache = new();

    public async Task<IntegrationCredentials?> GetAsync(SocialNetwork network, CancellationToken ct)
    {
        var provider = SocialProviders.AppProvider(network);
        if (_cache.TryGetValue(provider, out var cached)) return cached;
        var creds = await vault.GetAsync(provider, null, ct);
        if (creds is not null && (!creds.Settings.ContainsKey(SocialProviders.ClientIdKey(provider))
                                  || !creds.Secrets.ContainsKey(SocialProviders.ClientSecretKey(provider))))
            creds = null;
        _cache[provider] = creds;
        return creds;
    }

    public async Task<bool> ConfiguredAsync(SocialNetwork network, CancellationToken ct) => await GetAsync(network, ct) is not null;
}

public sealed record StoredToken(Guid ConnectionId, string AccessToken, string? RefreshToken, DateTime? ExpiresAt);

/// <summary>Stores and reads a brand profile's token through the encrypted integration store.</summary>
public sealed class ProfileTokenStore(AppDbContext db, ICredentialVault vault, TimeProvider clock)
{
    public const string AccessTokenKey = "accessToken";
    public const string RefreshTokenKey = "refreshToken";

    /// <summary>Stages (does not save) the token row and links it to the profile.</summary>
    public async Task StoreAsync(BrandProfile profile, string accessToken, string? refreshToken, DateTime? expiresAt,
        IReadOnlyDictionary<string, string>? settings, CancellationToken ct)
    {
        var secrets = new Dictionary<string, string> { [AccessTokenKey] = accessToken };
        if (!string.IsNullOrWhiteSpace(refreshToken)) secrets[RefreshTokenKey] = refreshToken;
        var connection = profile.IntegrationConnectionId is { } id
            ? await db.Set<IntegrationConnection>().FirstOrDefaultAsync(c => c.Id == id, ct)
            : null;
        if (connection is null)
        {
            connection = new IntegrationConnection
            {
                Provider = SocialProviders.ProfileTokenProvider(profile.Network),
                ClientAccountId = profile.ClientAccountId,
            };
            db.Set<IntegrationConnection>().Add(connection);
            profile.IntegrationConnectionId = connection.Id;
        }
        connection.DisplayName = Truncate($"{PostValidator.Label(profile.Network)}: {profile.DisplayName}", 150);
        connection.SettingsJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>(settings ?? new Dictionary<string, string>())
        {
            ["profileId"] = profile.Id.ToString(),
            ["externalId"] = profile.ExternalId ?? string.Empty,
        });
        connection.EncryptedSecrets = vault.Protect(secrets);
        connection.Status = IntegrationStatus.Connected;
        connection.StatusMessage = null;
        connection.LastVerifiedAt = clock.GetUtcNow().UtcDateTime;
        connection.ExpiresAt = expiresAt;

        profile.ConnectionStatus = ProfileConnectionStatus.Connected;
        profile.StatusMessage = null;
        profile.TokenExpiresAt = expiresAt;
        profile.ConnectedAt = clock.GetUtcNow().UtcDateTime;
    }

    public async Task<StoredToken?> ReadAsync(BrandProfile profile, CancellationToken ct)
    {
        if (profile.IntegrationConnectionId is not { } id) return null;
        var connection = await db.Set<IntegrationConnection>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (connection is null || connection.Status == IntegrationStatus.Disconnected) return null;
        var secrets = vault.Unprotect(connection.EncryptedSecrets);
        if (!secrets.TryGetValue(AccessTokenKey, out var token) || string.IsNullOrWhiteSpace(token)) return null;
        return new StoredToken(connection.Id, token, secrets.GetValueOrDefault(RefreshTokenKey), connection.ExpiresAt);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

/// <summary>Derives the OAuth state HMAC key.</summary>
public sealed class OAuthStateKey(IOptions<SocialMediaOptions> options, IConfiguration configuration)
{
    public byte[] Key
    {
        get
        {
            var secret = options.Value.OAuthStateSecret;
            if (!string.IsNullOrWhiteSpace(secret)) return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
            var jwt = configuration["Jwt:SigningKey"];
            if (string.IsNullOrWhiteSpace(jwt))
                throw new InvalidOperationException("Configure SocialMedia:OAuthStateSecret or Jwt:SigningKey.");
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(jwt), 32,
                info: Encoding.UTF8.GetBytes("optimizeall.social.oauth-state.v1"));
        }
    }
}

/// <summary>Web-app paths linked from social/ads notifications (module-local until AppLinks gains agency entries).</summary>
public static class SocialLinks
{
    public static string Post(Guid id) => $"/agency/social/posts/{id}";
    public const string Publishing = "/agency/social/publishing";
    public const string ClientApprovals = "/client/social/approvals";
}

internal static class UserNames
{
    public static async Task<Dictionary<Guid, string>> LookupAsync(AppDbContext db, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, string>();
        return await db.Set<User>().AsNoTracking().Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }
}
