using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Integrations;

public enum IntegrationStatus
{
    /// <summary>Credentials saved but never verified against the provider.</summary>
    Unverified,
    Connected,
    /// <summary>The last verification or API call was rejected (expired token, revoked access).</summary>
    Error,
    Disconnected,
}

/// <summary>
/// Credentials for a third-party platform (e.g. "meta", "x", "linkedin", "tiktok", "google-ads", "twilio",
/// "dataforseo", "stripe"). Agency-wide when <see cref="ClientAccountId"/> is null, otherwise for one client.
/// Secrets are encrypted with ASP.NET Data Protection and never returned by the API.
/// </summary>
public class IntegrationConnection : AuditedEntity, IConcurrencyStamped
{
    public string Provider { get; set; } = string.Empty;
    public Guid? ClientAccountId { get; set; }

    /// <summary>
    /// Uniqueness key of a live registry connection: the workspace key (client id or "agency", see
    /// <c>Workspace.Key</c>) while the connection is not disconnected, otherwise null. Unique together with
    /// <see cref="Provider"/>, so there is at most one live connection per provider and workspace even under concurrent
    /// creates (a nullable <see cref="ClientAccountId"/> cannot be part of a portable unique index: NULLs are distinct).
    /// Null for disconnected history rows and for per-profile social token rows, which may repeat.
    /// </summary>
    public string? ActiveScopeKey { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Non-secret settings (account ids, page ids, sender ids) as JSON.</summary>
    public string SettingsJson { get; set; } = "{}";

    /// <summary>Data-protection-encrypted JSON object of secret values (tokens, API keys).</summary>
    public string EncryptedSecrets { get; set; } = string.Empty;
    public IntegrationStatus Status { get; set; } = IntegrationStatus.Unverified;
    public string? StatusMessage { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
