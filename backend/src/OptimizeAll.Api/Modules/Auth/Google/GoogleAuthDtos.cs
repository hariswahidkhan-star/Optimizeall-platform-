using System.ComponentModel.DataAnnotations;

namespace OptimizeAll.Api.Modules.Auth.Google;

/// <summary>Which external sign-in providers are available (the web app hides unconfigured ones).</summary>
public sealed record AuthProvidersResponse(ProviderStatus Google);

public sealed record ProviderStatus(bool Enabled);

public sealed class GoogleStartRequest
{
    /// <summary>App-relative path to return to afterwards (e.g. the login page's <c>next</c>); anything else is ignored.</summary>
    [MaxLength(500)]
    public string? ReturnTo { get; set; }
}

public sealed record GoogleStartResponse(string AuthorizationUrl);

public sealed class GoogleCallbackRequest
{
    [Required, MaxLength(2048)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(4096)]
    public string State { get; set; } = string.Empty;
}

/// <summary>Outcome of the Google callback.</summary>
public static class GoogleCallbackStatus
{
    /// <summary>Signed in: <c>auth</c> holds the session (the refresh cookie is set).</summary>
    public const string SignedIn = "signedIn";

    /// <summary>No account yet: show the terms step, then <c>POST /auth/google/complete</c> with <c>ticket</c>.</summary>
    public const string NeedsTerms = "needsTerms";

    /// <summary>The signed-in user linked their Google account (profile flow).</summary>
    public const string Linked = "linked";
}

public sealed record GoogleCallbackResponse(
    string Status,
    AuthResponse? Auth = null,
    string? Ticket = null,
    string? Email = null,
    string? DisplayName = null,
    string? ReturnTo = null);

/// <summary>Creates the account for a new Google user once they accept the terms (same consent as registration).</summary>
public sealed class GoogleCompleteRequest
{
    [Required, MaxLength(4096)]
    public string Ticket { get; set; } = string.Empty;

    public bool AcceptTerms { get; set; }
    public bool MarketingEmailOptIn { get; set; }

    [MinLength(2), MaxLength(100)]
    public string? DisplayName { get; set; }

    [Required, RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter country code.")]
    public string CountryCode { get; set; } = string.Empty;

    [MaxLength(10)]
    public string LanguageCode { get; set; } = "en";

    [MaxLength(64)]
    public string TimeZone { get; set; } = "UTC";

    [MaxLength(32)]
    public string? ReferralCode { get; set; }

    [MaxLength(32)]
    public string? InviteCode { get; set; }

    [MaxLength(100)]
    public string? DeviceId { get; set; }
}

public sealed record ExternalLoginDto(string Provider, string Email, DateTime CreatedAt, DateTime? LastUsedAt);

/// <summary>The caller's sign-in methods (profile page).</summary>
public sealed record SignInMethodsResponse(bool HasPassword, bool GoogleEnabled, IReadOnlyList<ExternalLoginDto> ExternalLogins);
