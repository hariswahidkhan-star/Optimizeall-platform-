using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Seo;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Integrations;

public sealed class CreateConnectionRequest
{
    [Required, MaxLength(40)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>Null = agency-wide connection.</summary>
    public Guid? ClientAccountId { get; set; }

    [Required, MinLength(2), MaxLength(150)]
    public string DisplayName { get; set; } = string.Empty;

    public Dictionary<string, string> Settings { get; set; } = new();

    /// <summary>Write-only secret values (never returned).</summary>
    public Dictionary<string, string> Secrets { get; set; } = new();

    public DateTime? ExpiresAt { get; set; }
}

public sealed class UpdateConnectionRequest
{
    [Required, MinLength(2), MaxLength(150)]
    public string DisplayName { get; set; } = string.Empty;

    public Dictionary<string, string> Settings { get; set; } = new();

    /// <summary>Only the secrets to replace; omitted or empty values keep what is saved.</summary>
    public Dictionary<string, string> Secrets { get; set; } = new();

    /// <summary>Optional secrets to remove (e.g. a refresh token that is no longer used).</summary>
    public List<string> ClearSecrets { get; set; } = new();

    public DateTime? ExpiresAt { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record ProviderFieldDto(string Key, string Label, bool Required, string? Help, string? Pattern, int MaxLength, string? Placeholder);

public sealed record ProviderDto(
    string Key, string Name, IntegrationCategory Category, string Description, string HelpText, string? DocsUrl,
    IReadOnlyList<ProviderFieldDto> Settings, IReadOnlyList<ProviderFieldDto> Secrets, bool AgencyWide, bool PerClient, bool SupportsVerification,
    bool TokensExpire);

/// <summary>A secret slot: whether a value is saved — never the value.</summary>
public sealed record SecretStateDto(string Key, string Label, bool Required, bool Saved);

public sealed record ConnectionDto(
    Guid Id, string Provider, string ProviderName, Guid? ClientAccountId, string? ClientName, string DisplayName,
    IReadOnlyDictionary<string, string> Settings, IReadOnlyList<SecretStateDto> Secrets, IntegrationStatus Status, string? StatusMessage,
    DateTime? LastVerifiedAt, DateTime? ExpiresAt, bool ExpiringSoon, Guid ConcurrencyStamp, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>
/// Third-party credential management (integrations.manage). Secrets are encrypted by the credential vault, are write-only
/// (responses only say which secrets are saved) and are never written to the audit log.
/// </summary>
[ApiController]
[HasPermission(Permissions.IntegrationsManage)]
[Route("api/v1/agency/integrations")]
public sealed class IntegrationsController(
    AppDbContext db, SeoAccess access, ICredentialVault vault, IntegrationVerifier verifier, IAuditLogger audit, TimeProvider clock) : ControllerBase
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    [HttpGet("providers")]
    public IEnumerable<ProviderDto> Providers() => ProviderRegistry.All.Select(ToDto);

    [HttpGet("client-options")]
    public Task<List<ClientOptionDto>> ClientOptions(CancellationToken ct) => access.ClientOptionsAsync(ct);

    [HttpGet("connections")]
    public async Task<List<ConnectionDto>> List([FromQuery] Guid? clientId, [FromQuery] string? provider, [FromQuery] string? scope, CancellationToken ct)
    {
        var q = db.Set<IntegrationConnection>().AsNoTracking();
        if (clientId is { } c)
        {
            await access.EnsureAsync(c, "Client", ct);
            q = q.Where(x => x.ClientAccountId == c);
        }
        else if (scope == "agency") q = q.Where(x => x.ClientAccountId == null);
        if (!string.IsNullOrWhiteSpace(provider)) q = q.Where(x => x.Provider == provider);
        var rows = await q.OrderBy(x => x.Provider).ThenBy(x => x.DisplayName).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    [HttpGet("connections/{id:guid}")]
    public async Task<ConnectionDto> Get(Guid id, CancellationToken ct) => (await ToDtosAsync(new List<IntegrationConnection> { await LoadAsync(id, ct, false) }, ct))[0];

    [HttpPost("connections")]
    public async Task<ActionResult<ConnectionDto>> Create(CreateConnectionRequest request, CancellationToken ct)
    {
        var descriptor = ProviderRegistry.Find(request.Provider)
                         ?? throw new DomainException("integrations.unknown_provider", $"Unknown provider '{request.Provider}'.");
        if (request.ClientAccountId is { } clientId)
        {
            if (!descriptor.PerClient) throw new DomainException("integrations.agency_only", $"{descriptor.Name} is configured agency-wide only.");
            await access.EnsureAsync(clientId, "Client", ct);
        }
        else if (!descriptor.AgencyWide) throw new DomainException("integrations.client_only", $"{descriptor.Name} is connected per client — choose a client.");

        var settings = Clean(request.Settings);
        var secrets = Clean(request.Secrets);
        Validate(descriptor, settings, secrets, new HashSet<string>());
        if (await db.Set<IntegrationConnection>().AnyAsync(x => x.Provider == descriptor.Key && x.ClientAccountId == request.ClientAccountId &&
                                                               x.Status != IntegrationStatus.Disconnected, ct))
            throw DomainException.Conflict("integrations.exists", $"A {descriptor.Name} connection already exists here; update it instead.");

        var row = new IntegrationConnection
        {
            Provider = descriptor.Key, ClientAccountId = request.ClientAccountId, DisplayName = request.DisplayName.Trim(),
            SettingsJson = JsonSerializer.Serialize(settings), EncryptedSecrets = vault.Protect(secrets),
            Status = IntegrationStatus.Unverified, StatusMessage = "Saved — not verified yet.", ExpiresAt = request.ExpiresAt?.ToUniversalTime(),
        };
        db.Add(row);
        audit.Record("integration.created", nameof(IntegrationConnection), row.Id,
            after: new { row.Provider, row.ClientAccountId, row.DisplayName, Settings = settings, SecretsSaved = secrets.Keys.OrderBy(k => k), row.ExpiresAt });
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = row.Id }, (await ToDtosAsync(new List<IntegrationConnection> { row }, ct))[0]);
    }

    [HttpPut("connections/{id:guid}")]
    public async Task<ConnectionDto> Update(Guid id, UpdateConnectionRequest request, CancellationToken ct)
    {
        var row = await LoadAsync(id, ct, true);
        if (request.ConcurrencyStamp is { } stamp)
        {
            if (stamp != row.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "This connection was changed by someone else. Reload and try again.");
            db.Entry(row).Property(x => x.ConcurrencyStamp).OriginalValue = stamp;
        }
        var descriptor = ProviderRegistry.Find(row.Provider) ?? throw new DomainException("integrations.unknown_provider", "Unknown provider.");
        var existing = vault.Unprotect(row.EncryptedSecrets).ToDictionary(k => k.Key, k => k.Value);
        var settings = Clean(request.Settings);
        var incoming = Clean(request.Secrets);
        var cleared = request.ClearSecrets.Where(k => descriptor.Secrets.Any(f => f.Key == k && !f.Required)).ToHashSet();
        var kept = existing.Keys.Where(k => !cleared.Contains(k)).ToHashSet();
        Validate(descriptor, settings, incoming, kept);

        var beforeSettings = JsonSerializer.Deserialize<Dictionary<string, string>>(row.SettingsJson) ?? new();
        foreach (var key in cleared) existing.Remove(key);
        foreach (var (key, value) in incoming) existing[key] = value;
        var secretsChanged = incoming.Count > 0 || cleared.Count > 0;

        row.DisplayName = request.DisplayName.Trim();
        row.SettingsJson = JsonSerializer.Serialize(settings);
        row.EncryptedSecrets = vault.Protect(existing);
        row.ExpiresAt = request.ExpiresAt?.ToUniversalTime();
        if (secretsChanged || row.Status == IntegrationStatus.Disconnected || !beforeSettings.OrderBy(k => k.Key).SequenceEqual(settings.OrderBy(k => k.Key)))
        {
            row.Status = IntegrationStatus.Unverified;
            row.StatusMessage = "Updated — not verified yet.";
        }
        audit.Record("integration.updated", nameof(IntegrationConnection), row.Id,
            before: new { Settings = beforeSettings },
            after: new { row.DisplayName, Settings = settings, SecretsReplaced = incoming.Keys.OrderBy(k => k), SecretsCleared = cleared.OrderBy(k => k), row.ExpiresAt });
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync(new List<IntegrationConnection> { row }, ct))[0];
    }

    /// <summary>Calls the provider's verifier (where implemented) and records the outcome.</summary>
    [HttpPost("connections/{id:guid}/test")]
    public async Task<ConnectionDto> Test(Guid id, CancellationToken ct)
    {
        var row = await LoadAsync(id, ct, true);
        if (row.Status == IntegrationStatus.Disconnected) throw DomainException.Conflict("integrations.disconnected", "Reconnect this integration before testing it.");
        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(row.SettingsJson) ?? new();
        var result = await verifier.VerifyAsync(row.Provider, settings, vault.Unprotect(row.EncryptedSecrets), ct);
        row.Status = result.Status;
        row.StatusMessage = result.Message.Length > 1000 ? result.Message[..1000] : result.Message;
        row.LastVerifiedAt = Now;
        audit.Record("integration.tested", nameof(IntegrationConnection), row.Id, after: new { row.Provider, row.Status, row.StatusMessage });
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync(new List<IntegrationConnection> { row }, ct))[0];
    }

    /// <summary>Disconnects: secrets are wiped and adapters stop using the connection; the record stays for history.</summary>
    [HttpPost("connections/{id:guid}/disconnect")]
    public async Task<ConnectionDto> Disconnect(Guid id, CancellationToken ct)
    {
        var row = await LoadAsync(id, ct, true);
        row.Status = IntegrationStatus.Disconnected;
        row.StatusMessage = "Disconnected — credentials removed.";
        row.EncryptedSecrets = vault.Protect(new Dictionary<string, string>());
        audit.Record("integration.disconnected", nameof(IntegrationConnection), row.Id, after: new { row.Provider, row.ClientAccountId });
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync(new List<IntegrationConnection> { row }, ct))[0];
    }

    [HttpDelete("connections/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var row = await LoadAsync(id, ct, true);
        db.Remove(row);
        audit.Record("integration.deleted", nameof(IntegrationConnection), row.Id, before: new { row.Provider, row.ClientAccountId, row.DisplayName });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<IntegrationConnection> LoadAsync(Guid id, CancellationToken ct, bool tracked)
    {
        var q = db.Set<IntegrationConnection>().Where(x => x.Id == id);
        if (!tracked) q = q.AsNoTracking();
        var row = await q.FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("Connection");
        if (row.ClientAccountId is { } clientId) await access.EnsureAsync(clientId, "Connection", ct);
        return row;
    }

    private static Dictionary<string, string> Clean(Dictionary<string, string>? values) =>
        (values ?? new()).Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim());

    private static void Validate(ProviderDescriptor d, Dictionary<string, string> settings, Dictionary<string, string> secrets, IReadOnlySet<string> saved)
    {
        var errors = ProviderRegistry.Validate(d, settings, secrets, saved);
        if (errors.Count > 0) throw new DomainException("integrations.invalid", $"The {d.Name} settings are incomplete or invalid.", DomainErrorKind.Validation, errors);
    }

    private async Task<List<ConnectionDto>> ToDtosAsync(List<IntegrationConnection> rows, CancellationToken ct)
    {
        var clientIds = rows.Where(r => r.ClientAccountId != null).Select(r => r.ClientAccountId!.Value).Distinct().ToList();
        var clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var now = Now;
        return rows.Select(r =>
        {
            var d = ProviderRegistry.Find(r.Provider);
            IReadOnlyDictionary<string, string> saved;
            try { saved = vault.Unprotect(r.EncryptedSecrets); }
            catch (System.Security.Cryptography.CryptographicException) { saved = new Dictionary<string, string>(); }
            var secretStates = (d?.Secrets ?? Array.Empty<ProviderField>())
                .Select(f => new SecretStateDto(f.Key, f.Label, f.Required, saved.TryGetValue(f.Key, out var v) && v.Length > 0)).ToList();
            return new ConnectionDto(r.Id, r.Provider, d?.Name ?? r.Provider, r.ClientAccountId,
                r.ClientAccountId is { } c ? clients.GetValueOrDefault(c) : null, r.DisplayName,
                JsonSerializer.Deserialize<Dictionary<string, string>>(r.SettingsJson) ?? new(), secretStates, r.Status, r.StatusMessage,
                r.LastVerifiedAt, r.ExpiresAt, r.ExpiresAt is { } e && e > now && e <= now + IntegrationExpiryJob.WarnWithin,
                r.ConcurrencyStamp, r.CreatedAt, r.UpdatedAt);
        }).ToList();
    }

    private static ProviderDto ToDto(ProviderDescriptor d) => new(d.Key, d.Name, d.Category, d.Description, d.HelpText, d.DocsUrl,
        d.Settings.Select(F).ToList(), d.Secrets.Select(F).ToList(), d.AgencyWide, d.PerClient, IntegrationVerifier.CanVerify(d.Key), d.TokensExpire);

    private static ProviderFieldDto F(ProviderField f) => new(f.Key, f.Label, f.Required, f.Help, f.Pattern, f.MaxLength, f.Placeholder);
}
