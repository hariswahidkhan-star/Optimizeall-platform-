using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Identity;

/// <summary>
/// A time-boxed "view as / log in as" session: a staff member holding <c>users.impersonate</c> acting as another
/// user. The browser holds the raw token in an HttpOnly cookie; only its SHA-256 hash is stored. The session is
/// valid only while it is not ended, not expired, and the impersonator's own account is active with the same
/// security version as when it started (a password change or suspension of the impersonator ends it).
/// </summary>
public class ImpersonationSession : Entity
{
    public Guid ImpersonatorUserId { get; set; }
    public Guid TargetUserId { get; set; }

    /// <summary>Why the staff member needed to see the account (required, audited).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>SHA-256 of the opaque session token held in the impersonation cookie.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>The impersonator's <see cref="User.SecurityVersion"/> at the start.</summary>
    public int ImpersonatorSecurityVersion { get; set; }

    public DateTime StartedAt { get; set; }

    /// <summary>Hard end of the session; never extended.</summary>
    public DateTime ExpiresAt { get; set; }
    public DateTime? EndedAt { get; set; }

    /// <summary>Why it ended: "exit", "logout", "replaced", "expired", "admin_session_ended".</summary>
    public string? EndedReason { get; set; }
    public string? IpAddress { get; set; }
}
