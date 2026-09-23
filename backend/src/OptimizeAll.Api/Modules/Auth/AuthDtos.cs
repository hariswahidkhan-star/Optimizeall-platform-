using System.ComponentModel.DataAnnotations;

namespace OptimizeAll.Api.Modules.Auth;

public sealed class RegisterRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(10), MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    [Required, MinLength(2), MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

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

    /// <summary>Opaque client device identifier (random per browser); only its hash is stored, for referral fraud checks.</summary>
    [MaxLength(100)]
    public string? DeviceId { get; set; }

    public bool AcceptTerms { get; set; }
    public bool MarketingEmailOptIn { get; set; }
}

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string Password { get; set; } = string.Empty;
}

public sealed class TokenRequest
{
    [Required, MaxLength(200)]
    public string Token { get; set; } = string.Empty;
}

public sealed class EmailRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required, MaxLength(200)]
    public string Token { get; set; } = string.Empty;

    [Required, MinLength(10), MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required, MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(10), MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed record SessionUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailVerified,
    string CountryCode,
    string LanguageCode,
    string TimeZone,
    string Status,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, SessionUserDto User);

public sealed record MessageResponse(string Message);
