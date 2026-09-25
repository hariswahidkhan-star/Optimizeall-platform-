using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Hosting;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.SocialMedia;

public sealed record ClientOptionDto(Guid Id, string Name, string Slug, string CountryCode, string Currency, string TimeZone);

public sealed class ProfileInput
{
    [Required] public SocialNetwork? Network { get; set; }
    [Required, MinLength(1), MaxLength(150)] public string Handle { get; set; } = string.Empty;
    [Required, MinLength(1), MaxLength(200)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(500)] public string? ProfileUrl { get; set; }
    [MaxLength(500)] public string? AvatarUrl { get; set; }
    [MaxLength(100)] public string? ExternalId { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record QueueSlotDto(Guid Id, DayOfWeek Day, string Time);

public sealed record ProfileDto(
    Guid Id, Guid ClientAccountId, SocialNetwork Network, string NetworkLabel, string Handle, string DisplayName, string? ProfileUrl,
    string? AvatarUrl, string? ExternalId, string ConnectionState, ProfileConnectionStatus ConnectionStatus, string? StatusMessage,
    bool AppCredentialsConfigured, bool PublishingSupported, DateTime? TokenExpiresAt, DateTime? ConnectedAt, bool IsActive,
    IReadOnlyList<QueueSlotDto> QueueSlots, Guid ConcurrencyStamp);

public sealed record ConnectStartDto(string AuthorizationUrl, DateTime ExpiresAt);

public sealed class OAuthCallbackInput
{
    [MaxLength(4000)] public string? Code { get; set; }
    [Required, MaxLength(1000)] public string State { get; set; } = string.Empty;
    [MaxLength(500)] public string? Error { get; set; }
}

public sealed class TokenInput
{
    [Required, MinLength(10), MaxLength(4000)] public string AccessToken { get; set; } = string.Empty;
    [MaxLength(4000)] public string? RefreshToken { get; set; }
    [MaxLength(100)] public string? ExternalId { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public sealed class QueueSlotsInput
{
    [MaxLength(70)] public List<QueueSlotInput> Slots { get; set; } = new();
}

public sealed class QueueSlotInput
{
    [Required] public DayOfWeek? Day { get; set; }
    [Required, RegularExpression("^([01][0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Use HH:mm (24h).")] public string Time { get; set; } = "09:00";
}

public sealed record SocialSettingsDto(Guid ClientAccountId, bool RequireClientApproval, string DefaultUtmMedium, Guid ConcurrencyStamp);

public sealed class SocialSettingsInput
{
    public bool RequireClientApproval { get; set; }
    [Required, MaxLength(100)] public string DefaultUtmMedium { get; set; } = "social";
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record SocialCampaignDto(Guid Id, Guid ClientAccountId, string Name, string UtmCampaign, string? UtmSource, string? UtmMedium,
    string? UtmContent, string? UtmTerm, Guid ConcurrencyStamp, bool IsArchived = false);

public sealed class SocialCampaignInput
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string UtmCampaign { get; set; } = string.Empty;
    [MaxLength(100)] public string? UtmSource { get; set; }
    [MaxLength(100)] public string? UtmMedium { get; set; }
    [MaxLength(150)] public string? UtmContent { get; set; }
    [MaxLength(150)] public string? UtmTerm { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

/// <summary>Brand profiles, connections (OAuth / token), queue slots, per-client settings and UTM campaigns.</summary>
[ApiController]
[Route("api/v1/agency/social")]
public sealed class SocialProfilesController(
    AppDbContext db,
    SocialAccess access,
    SocialAppCredentials appCredentials,
    SocialOAuthRegistry oauth,
    ProfileTokenStore tokens,
    SocialPostService posts,
    OAuthStateKey stateKey,
    ICredentialVault vault,
    ICurrentUser currentUser,
    IAuditLogger audit,
    IOptions<SocialMediaOptions> options,
    IPublicOrigin publicOrigin,
    TimeProvider clock) : ControllerBase
{
    public const string OAuthPurpose = "social-profile";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    [HttpGet("clients")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IReadOnlyList<ClientOptionDto>> Clients(CancellationToken ct) => await ClientOptionsAsync(db, access.Scope, ct);

    public static async Task<IReadOnlyList<ClientOptionDto>> ClientOptionsAsync(AppDbContext db, IClientScope scope, CancellationToken ct)
    {
        var q = await scope.ApplyAsync(db.Set<ClientAccount>().AsNoTracking().AsQueryable(), c => c.Id, ct);
        return await q.Where(c => c.Status != ClientAccountStatus.Churned).OrderBy(c => c.Name)
            .Select(c => new ClientOptionDto(c.Id, c.Name, c.Slug, c.CountryCode, c.Currency, c.TimeZone)).Take(500).ToListAsync(ct);
    }

    [HttpGet("clients/{clientId:guid}/profiles")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IReadOnlyList<ProfileDto>> Profiles(Guid clientId, [FromQuery] bool includeArchived, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var profiles = await db.Set<BrandProfile>().AsNoTracking().Where(p => p.ClientAccountId == clientId && (includeArchived || p.IsActive))
            .OrderBy(p => p.Network).ThenBy(p => p.Handle).ToListAsync(ct);
        var result = new List<ProfileDto>();
        foreach (var p in profiles) result.Add(await ToDtoAsync(p, ct));
        return result;
    }

    [HttpPost("clients/{clientId:guid}/profiles")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<ActionResult<ProfileDto>> CreateProfile(Guid clientId, ProfileInput input, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var handle = Normalization.Handle(input.Handle);
        if (await db.Set<BrandProfile>().AnyAsync(p => p.ClientAccountId == clientId && p.Network == input.Network && p.Handle == handle, ct))
            throw DomainException.Conflict("social.profile_exists", "This client already has that profile.");
        var profile = new BrandProfile
        {
            ClientAccountId = clientId,
            Network = input.Network!.Value,
            Handle = handle,
            DisplayName = input.DisplayName.Trim(),
            ProfileUrl = CleanUrl(input.ProfileUrl, "profileUrl"),
            AvatarUrl = CleanUrl(input.AvatarUrl, "avatarUrl"),
            ExternalId = string.IsNullOrWhiteSpace(input.ExternalId) ? null : input.ExternalId.Trim(),
        };
        db.Set<BrandProfile>().Add(profile);
        audit.Record("social.profile.created", nameof(BrandProfile), profile.Id, after: new { profile.Network, profile.Handle });
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/agency/social/profiles/{profile.Id}", await ToDtoAsync(profile, ct));
    }

    [HttpPut("profiles/{id:guid}")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<ProfileDto> UpdateProfile(Guid id, ProfileInput input, CancellationToken ct)
    {
        var profile = await access.ProfileAsync(id, ct);
        Stamp(profile, input.ConcurrencyStamp);
        if (input.Network != profile.Network) throw new DomainException("social.network_immutable", "The network of a profile cannot change.");
        var before = new { profile.Handle, profile.DisplayName, profile.ExternalId };
        profile.Handle = Normalization.Handle(input.Handle);
        profile.DisplayName = input.DisplayName.Trim();
        profile.ProfileUrl = CleanUrl(input.ProfileUrl, "profileUrl");
        profile.AvatarUrl = CleanUrl(input.AvatarUrl, "avatarUrl");
        var externalId = string.IsNullOrWhiteSpace(input.ExternalId) ? null : input.ExternalId.Trim();
        // A connected profile publishes with its stored token to ExternalId: re-pointing it is a credential change.
        if (externalId != profile.ExternalId && profile.IntegrationConnectionId is not null && !currentUser.HasPermission(Permissions.IntegrationsManage))
            throw DomainException.Forbidden("social.external_id_requires_integrations",
                "Changing the account id of a connected profile requires the integrations.manage permission.");
        profile.ExternalId = externalId;
        audit.Record("social.profile.updated", nameof(BrandProfile), profile.Id, before, new { profile.Handle, profile.DisplayName, profile.ExternalId });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(profile, ct);
    }

    /// <summary>Archives a profile (kept for reporting; hidden from the composer).</summary>
    [HttpDelete("profiles/{id:guid}")]
    [HasPermission(Permissions.SocialPublish)]
    public async Task<IActionResult> ArchiveProfile(Guid id, CancellationToken ct)
    {
        var profile = await access.ProfileAsync(id, ct);
        profile.IsActive = false;
        audit.Record("social.profile.archived", nameof(BrandProfile), profile.Id);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Starts the OAuth flow: returns the network's authorization URL with a signed, expiring state. Answers 409
    /// <c>social.app_credentials_required</c> when the developer app id/secret are not configured.
    /// </summary>
    [DeniedWhileImpersonating] // integration credentials (OAuth)
    [HttpPost("profiles/{id:guid}/connect/start")]
    [HasPermission(Permissions.SocialPublish)]
    public async Task<ConnectStartDto> ConnectStart(Guid id, CancellationToken ct)
    {
        var profile = await access.ProfileAsync(id, ct);
        var app = await appCredentials.GetAsync(profile.Network, ct) ?? throw AppCredentialsRequired(profile.Network);
        var expires = Now.Add(OAuthState.Lifetime);
        var nonce = OAuthState.NewNonce();
        var key = stateKey.Key;
        var state = OAuthState.Sign(new OAuthStatePayload(OAuthPurpose, profile.Id, currentUser.Id, expires, nonce), key);
        var url = oauth.For(profile.Network).AuthorizationUrl(profile.Network, app, RedirectUri(), state,
            OAuthState.PkceChallenge(OAuthState.PkceVerifier(key, nonce)));
        audit.Record("social.profile.connect_started", nameof(BrandProfile), profile.Id);
        await db.SaveChangesAsync(ct);
        return new ConnectStartDto(url, expires);
    }

    /// <summary>
    /// Completes the OAuth flow. The web app's callback page posts the provider's <c>code</c> and <c>state</c> here; the
    /// state must be untampered, unexpired and issued to the same signed-in user.
    /// </summary>
    [DeniedWhileImpersonating] // integration credentials (OAuth)
    [HttpPost("oauth/callback")]
    [HasPermission(Permissions.SocialPublish)]
    public async Task<ProfileDto> ConnectCallback(OAuthCallbackInput input, CancellationToken ct)
    {
        var key = stateKey.Key;
        var payload = OAuthState.Verify(input.State, key, currentUser.Id, Now, out var reason);
        if (payload is null || payload.Purpose != OAuthPurpose)
            throw new DomainException("social.oauth_state_invalid",
                reason == "expired" ? "The connection request expired. Start again." : "The connection request is invalid. Start again.");
        var profile = await access.ProfileAsync(payload.TargetId, ct);
        if (!string.IsNullOrWhiteSpace(input.Error) || string.IsNullOrWhiteSpace(input.Code))
        {
            profile.ConnectionStatus = ProfileConnectionStatus.Error;
            profile.StatusMessage = $"Authorization was not granted: {input.Error ?? "no code returned"}.";
            await db.SaveChangesAsync(ct);
            throw new DomainException("social.oauth_denied", profile.StatusMessage);
        }
        var app = await appCredentials.GetAsync(profile.Network, ct) ?? throw AppCredentialsRequired(profile.Network);
        var result = await oauth.For(profile.Network).ExchangeAsync(profile, app, input.Code, RedirectUri(),
            OAuthState.PkceVerifier(key, payload.Nonce), ct);
        if (!result.Success || result.AccessToken is null)
        {
            profile.ConnectionStatus = ProfileConnectionStatus.Error;
            profile.StatusMessage = Truncate(result.Error ?? "The token exchange failed.", 1000);
            audit.Record("social.profile.connect_failed", nameof(BrandProfile), profile.Id, reason: profile.StatusMessage);
            await db.SaveChangesAsync(ct);
            throw DomainException.Conflict("social.oauth_exchange_failed", profile.StatusMessage);
        }
        if (!string.IsNullOrWhiteSpace(result.ExternalId)) profile.ExternalId = result.ExternalId;
        await tokens.StoreAsync(profile, result.AccessToken, result.RefreshToken, result.ExpiresAt, null, ct);
        audit.Record("social.profile.connected", nameof(BrandProfile), profile.Id, after: new { profile.ExternalId, via = "oauth" });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(profile, ct);
    }

    /// <summary>Stores a token obtained outside the OAuth flow (e.g. a Meta system-user Page token). Sensitive: integrations.manage.</summary>
    [DeniedWhileImpersonating] // integration credentials
    [HttpPost("profiles/{id:guid}/token")]
    [HasPermission(Permissions.IntegrationsManage)]
    public async Task<ProfileDto> SetToken(Guid id, TokenInput input, CancellationToken ct)
    {
        var profile = await access.ProfileAsync(id, ct);
        if (!string.IsNullOrWhiteSpace(input.ExternalId)) profile.ExternalId = input.ExternalId.Trim();
        if (profile.Network is SocialNetwork.Facebook or SocialNetwork.Instagram && string.IsNullOrWhiteSpace(profile.ExternalId))
            throw new DomainException("social.external_id_required", "Enter the Page id / Instagram business account id for this token.",
                errors: new Dictionary<string, string[]> { ["externalId"] = new[] { "Required for Meta profiles." } });
        await tokens.StoreAsync(profile, input.AccessToken.Trim(), input.RefreshToken?.Trim(),
            input.ExpiresAt is { } e ? SocialPostService.Utc(e) : null, null, ct);
        audit.Record("social.profile.connected", nameof(BrandProfile), profile.Id, after: new { profile.ExternalId, via = "token" });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(profile, ct);
    }

    [DeniedWhileImpersonating] // integration credentials (OAuth)
    [HttpPost("profiles/{id:guid}/disconnect")]
    [HasPermission(Permissions.SocialPublish)]
    public async Task<ProfileDto> Disconnect(Guid id, CancellationToken ct)
    {
        var profile = await access.ProfileAsync(id, ct);
        if (profile.IntegrationConnectionId is { } connectionId)
            await vault.MarkStatusAsync(connectionId, IntegrationStatus.Disconnected, "Disconnected by staff.", ct);
        profile.ConnectionStatus = ProfileConnectionStatus.Disconnected;
        profile.StatusMessage = null;
        audit.Record("social.profile.disconnected", nameof(BrandProfile), profile.Id);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(profile, ct);
    }

    [HttpPut("profiles/{id:guid}/queue-slots")]
    [HasPermission(Permissions.SocialPublish)]
    public async Task<ProfileDto> SetQueueSlots(Guid id, QueueSlotsInput input, CancellationToken ct)
    {
        var profile = await access.ProfileAsync(id, ct);
        var wanted = input.Slots.Select(s => (Day: s.Day!.Value, Minute: Minutes(s.Time))).Distinct().ToList();
        await db.Set<SocialQueueSlot>().Where(s => s.ProfileId == profile.Id).ExecuteDeleteAsync(ct);
        foreach (var (day, minute) in wanted)
            db.Set<SocialQueueSlot>().Add(new SocialQueueSlot { ClientAccountId = profile.ClientAccountId, ProfileId = profile.Id, DayOfWeek = day, MinuteOfDay = minute });
        audit.Record("social.profile.queue_updated", nameof(BrandProfile), profile.Id, after: new { slots = wanted.Count });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(profile, ct);
    }

    [HttpGet("clients/{clientId:guid}/settings")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<SocialSettingsDto> Settings(Guid clientId, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var s = await access.SettingsAsync(clientId, ct);
        return new SocialSettingsDto(clientId, s.RequireClientApproval, s.DefaultUtmMedium, s.ConcurrencyStamp);
    }

    /// <summary>
    /// Per-client settings; turning client approval on or off is audited. Turning it on sends approved and scheduled posts
    /// that the client has not approved back to the client for approval.
    /// </summary>
    [HttpPut("clients/{clientId:guid}/settings")]
    [HasPermission(Permissions.SocialPublish)]
    public async Task<SocialSettingsDto> UpdateSettings(Guid clientId, SocialSettingsInput input, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var s = await db.Set<SocialClientSettings>().FirstOrDefaultAsync(x => x.ClientAccountId == clientId, ct);
        if (s is null)
        {
            s = new SocialClientSettings { ClientAccountId = clientId };
            db.Set<SocialClientSettings>().Add(s);
        }
        else if (input.ConcurrencyStamp is { } stamp)
        {
            if (stamp != s.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "Settings changed meanwhile; reload.");
            db.Entry(s).Property(x => x.ConcurrencyStamp).OriginalValue = stamp;
        }
        var before = new { s.RequireClientApproval, s.DefaultUtmMedium };
        s.RequireClientApproval = input.RequireClientApproval;
        s.DefaultUtmMedium = input.DefaultUtmMedium.Trim();
        s.UpdatedAt = Now;
        audit.Record("social.settings.updated", nameof(SocialClientSettings), clientId, before, new { s.RequireClientApproval, s.DefaultUtmMedium });
        await db.SaveChangesAsync(ct);
        if (s.RequireClientApproval && !before.RequireClientApproval) await posts.ReopenForClientApprovalAsync(clientId, ct);
        return new SocialSettingsDto(clientId, s.RequireClientApproval, s.DefaultUtmMedium, s.ConcurrencyStamp);
    }

    [HttpGet("clients/{clientId:guid}/campaigns")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IReadOnlyList<SocialCampaignDto>> Campaigns(Guid clientId, [FromQuery] bool includeArchived, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        return await db.Set<SocialCampaign>().AsNoTracking().Where(c => c.ClientAccountId == clientId && (includeArchived || !c.IsArchived))
            .OrderBy(c => c.IsArchived).ThenBy(c => c.Name)
            .Select(c => new SocialCampaignDto(c.Id, c.ClientAccountId, c.Name, c.UtmCampaign, c.UtmSource, c.UtmMedium, c.UtmContent, c.UtmTerm, c.ConcurrencyStamp, c.IsArchived))
            .ToListAsync(ct);
    }

    [HttpPost("clients/{clientId:guid}/campaigns")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<SocialCampaignDto> CreateCampaign(Guid clientId, SocialCampaignInput input, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var c = new SocialCampaign { ClientAccountId = clientId };
        Apply(c, input);
        db.Set<SocialCampaign>().Add(c);
        audit.Record("social.campaign.created", nameof(SocialCampaign), c.Id, after: new { c.Name, c.UtmCampaign });
        await db.SaveChangesAsync(ct);
        return new SocialCampaignDto(c.Id, c.ClientAccountId, c.Name, c.UtmCampaign, c.UtmSource, c.UtmMedium, c.UtmContent, c.UtmTerm, c.ConcurrencyStamp, c.IsArchived);
    }

    [HttpPut("campaigns/{id:guid}")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<SocialCampaignDto> UpdateCampaign(Guid id, SocialCampaignInput input, CancellationToken ct)
    {
        var c = await access.OwnedAsync<SocialCampaign>(id, x => x.ClientAccountId, "Campaign", ct);
        if (input.ConcurrencyStamp is { } stamp)
        {
            if (stamp != c.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "The campaign changed meanwhile; reload.");
            db.Entry(c).Property(x => x.ConcurrencyStamp).OriginalValue = stamp;
        }
        Apply(c, input);
        audit.Record("social.campaign.updated", nameof(SocialCampaign), c.Id, after: new { c.Name, c.UtmCampaign });
        await db.SaveChangesAsync(ct);
        return new SocialCampaignDto(c.Id, c.ClientAccountId, c.Name, c.UtmCampaign, c.UtmSource, c.UtmMedium, c.UtmContent, c.UtmTerm, c.ConcurrencyStamp, c.IsArchived);
    }

    private static void Apply(SocialCampaign c, SocialCampaignInput input)
    {
        c.Name = input.Name.Trim();
        c.UtmCampaign = input.UtmCampaign.Trim();
        c.UtmSource = Blank(input.UtmSource);
        c.UtmMedium = Blank(input.UtmMedium);
        c.UtmContent = Blank(input.UtmContent);
        c.UtmTerm = Blank(input.UtmTerm);
    }

    private async Task<ProfileDto> ToDtoAsync(BrandProfile p, CancellationToken ct)
    {
        var appConfigured = await appCredentials.ConfiguredAsync(p.Network, ct);
        var slots = await db.Set<SocialQueueSlot>().AsNoTracking().Where(s => s.ProfileId == p.Id)
            .OrderBy(s => s.DayOfWeek).ThenBy(s => s.MinuteOfDay).ToListAsync(ct);
        var state = p.ConnectionStatus switch
        {
            ProfileConnectionStatus.Connected => "Connected",
            ProfileConnectionStatus.Error => "Error",
            _ when !appConfigured => "AppCredentialsRequired",
            ProfileConnectionStatus.Disconnected => "Disconnected",
            _ => "NotConnected",
        };
        var supported = p.Network is SocialNetwork.Facebook or SocialNetwork.Instagram or SocialNetwork.X;
        return new ProfileDto(p.Id, p.ClientAccountId, p.Network, PostValidator.Label(p.Network), p.Handle, p.DisplayName, p.ProfileUrl, p.AvatarUrl,
            p.ExternalId, state, p.ConnectionStatus, p.StatusMessage, appConfigured, supported, p.TokenExpiresAt, p.ConnectedAt, p.IsActive,
            slots.Select(s => new QueueSlotDto(s.Id, s.DayOfWeek, $"{s.MinuteOfDay / 60:00}:{s.MinuteOfDay % 60:00}")).ToList(), p.ConcurrencyStamp);
    }

    private string RedirectUri() =>
        options.Value.OAuthRedirectUri is { Length: > 0 } configured
            ? configured
            : publicOrigin.Current + "/agency/social/connect/callback";

    private static DomainException AppCredentialsRequired(SocialNetwork network) => DomainException.Conflict("social.app_credentials_required",
        $"App credentials required: add the {PostValidator.Label(network)} developer app id and secret under Integrations " +
        $"(provider \"{SocialProviders.AppProvider(network)}\") before connecting profiles.");

    private void Stamp(BrandProfile p, Guid? stamp)
    {
        if (stamp is null) return;
        if (stamp != p.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "The profile changed meanwhile; reload.");
        db.Entry(p).Property(x => x.ConcurrencyStamp).OriginalValue = stamp.Value;
    }

    private static int Minutes(string time)
    {
        var t = TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture);
        return t.Hour * 60 + t.Minute;
    }

    private static string? CleanUrl(string? url, string field)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new DomainException("social.invalid_url", "Use an https URL.", errors: new Dictionary<string, string[]> { [field] = new[] { "Use an https URL." } });
        return uri.ToString();
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
