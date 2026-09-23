using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Security;

/// <summary>Decrypted view of an integration's credentials, for adapters only (never serialize it to a response).</summary>
public sealed record IntegrationCredentials(
    Guid ConnectionId, string Provider, Guid? ClientAccountId, IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, string> Secrets, IntegrationStatus Status);

/// <summary>
/// Encrypted store for third-party credentials. Adapters call <see cref="GetAsync"/>; when it returns null the
/// integration is not configured and the adapter must report "not configured" instead of pretending to act.
/// </summary>
public interface ICredentialVault
{
    /// <summary>The client-specific connection for the provider if one exists, else the agency-wide one, else null.</summary>
    Task<IntegrationCredentials?> GetAsync(string provider, Guid? clientAccountId, CancellationToken ct = default);

    string Protect(IReadOnlyDictionary<string, string> secrets);

    IReadOnlyDictionary<string, string> Unprotect(string encrypted);

    /// <summary>Records the outcome of a verification or API call (status + message), without touching secrets.</summary>
    Task MarkStatusAsync(Guid connectionId, IntegrationStatus status, string? message, CancellationToken ct = default);
}

public sealed class CredentialVault(AppDbContext db, IDataProtectionProvider protection, TimeProvider clock) : ICredentialVault
{
    private readonly IDataProtector _protector = protection.CreateProtector("OptimizeAll.Integrations.Secrets.v1");

    public async Task<IntegrationCredentials?> GetAsync(string provider, Guid? clientAccountId, CancellationToken ct = default)
    {
        var candidates = await db.Set<IntegrationConnection>().AsNoTracking()
            .Where(c => c.Provider == provider && c.Status != IntegrationStatus.Disconnected
                        && (c.ClientAccountId == clientAccountId || c.ClientAccountId == null))
            .ToListAsync(ct);
        var connection = candidates.FirstOrDefault(c => c.ClientAccountId == clientAccountId && clientAccountId != null)
                         ?? candidates.FirstOrDefault(c => c.ClientAccountId == null);
        if (connection is null) return null;
        return new IntegrationCredentials(connection.Id, connection.Provider, connection.ClientAccountId,
            JsonSerializer.Deserialize<Dictionary<string, string>>(connection.SettingsJson) ?? new(),
            Unprotect(connection.EncryptedSecrets), connection.Status);
    }

    public string Protect(IReadOnlyDictionary<string, string> secrets) =>
        _protector.Protect(JsonSerializer.Serialize(secrets));

    public IReadOnlyDictionary<string, string> Unprotect(string encrypted) =>
        string.IsNullOrEmpty(encrypted)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(_protector.Unprotect(encrypted)) ?? new();

    public async Task MarkStatusAsync(Guid connectionId, IntegrationStatus status, string? message, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var trimmed = message is { Length: > 1000 } ? message[..1000] : message;
        await db.Set<IntegrationConnection>().Where(c => c.Id == connectionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, status)
                .SetProperty(c => c.StatusMessage, trimmed)
                .SetProperty(c => c.LastVerifiedAt, now), ct);
    }
}
