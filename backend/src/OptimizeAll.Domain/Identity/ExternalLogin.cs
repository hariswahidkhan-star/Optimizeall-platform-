using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Identity;

/// <summary>External identity providers a user can sign in with.</summary>
public static class ExternalLoginProviders
{
    public const string Google = "google";
}

/// <summary>
/// A user's sign-in identity at an external provider (e.g. Google). <see cref="Subject"/> is the provider's stable,
/// never-reassigned user id (OpenID Connect <c>sub</c>), unique per provider; the email is only a snapshot taken when
/// the identity was linked (the provider may change it later) and is never used to look the identity up.
/// A user has at most one identity per provider.
/// </summary>
public class ExternalLogin : Entity
{
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    /// <summary>The provider-verified email at link time (display only).</summary>
    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
