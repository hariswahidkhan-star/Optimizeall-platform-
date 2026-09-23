using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Eligibility;
using OptimizeAll.Domain.Social;

namespace OptimizeAll.Api.Modules.Social;

public sealed record SocialAccountDto(
    Guid Id,
    SocialPlatform Platform,
    string Handle,
    string ProfileUrl,
    DateTime AccountCreatedAt,
    int AccountAgeDays,
    int FollowerCount,
    string? PrimaryLanguage,
    string? AudienceCountryCode,
    SocialAccountVerificationStatus VerificationStatus,
    string? VerificationNote,
    bool IsActive,
    bool Qualifies,
    DateTime? EligibleFrom,
    IReadOnlyList<EligibilityReason> Reasons,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid ConcurrencyStamp);

public sealed record SocialAccountListDto(int MinAccountAgeDays, int MinFollowers, int MaxActiveAccounts, IReadOnlyList<SocialAccountDto> Items);

public sealed record SocialAccountChangeResponse(SocialAccountDto Account, bool VerificationReset, string Message);

public sealed class CreateSocialAccountRequest
{
    [Required]
    public SocialPlatform? Platform { get; set; }

    [Required, MaxLength(101)]
    public string Handle { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string ProfileUrl { get; set; } = string.Empty;

    [Required]
    public DateTime? AccountCreatedAt { get; set; }

    [Range(0, SocialProfileRules.MaxFollowers)]
    public int FollowerCount { get; set; }

    [MaxLength(10)]
    public string? PrimaryLanguage { get; set; }

    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter country code.")]
    public string? AudienceCountryCode { get; set; }
}

public sealed class UpdateSocialAccountRequest
{
    [Required, MaxLength(101)]
    public string Handle { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string ProfileUrl { get; set; } = string.Empty;

    [Required]
    public DateTime? AccountCreatedAt { get; set; }

    [Range(0, SocialProfileRules.MaxFollowers)]
    public int FollowerCount { get; set; }

    [MaxLength(10)]
    public string? PrimaryLanguage { get; set; }

    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter country code.")]
    public string? AudienceCountryCode { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

// ---------- Staff review ----------

public sealed class ReviewSocialAccountQuery : PageQuery
{
    public SocialAccountVerificationStatus? Status { get; set; }
    public SocialPlatform? Platform { get; set; }
}

public sealed record SocialAccountOwnerDto(Guid Id, string DisplayName, string Email, string CountryCode);

public sealed record ReviewSocialAccountDto(
    Guid Id,
    SocialPlatform Platform,
    string Handle,
    string ProfileUrl,
    DateTime AccountCreatedAt,
    int AccountAgeDays,
    int FollowerCount,
    string? PrimaryLanguage,
    string? AudienceCountryCode,
    SocialAccountVerificationStatus VerificationStatus,
    string? VerificationNote,
    DateTime? VerifiedAt,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    SocialAccountOwnerDto Owner,
    Guid ConcurrencyStamp);

public sealed record ReviewSocialAccountDetailDto(
    ReviewSocialAccountDto Account,
    bool Qualifies,
    DateTime? EligibleFrom,
    IReadOnlyList<EligibilityReason> Reasons,
    SocialAccountActorDto? VerifiedBy,
    int OwnerActiveAccountCount,
    IReadOnlyList<SocialAccountHistoryDto> History);

public sealed record SocialAccountActorDto(Guid Id, string DisplayName);

public sealed record SocialAccountHistoryDto(DateTime At, string Action, Guid? ActorUserId, string? ActorDisplayName, string? Reason);

public enum SocialVerificationDecision
{
    Verified,
    Rejected,
}

public sealed class SocialAccountDecisionRequest
{
    [Required]
    public SocialVerificationDecision? Decision { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>Corrects the participant-declared creation date after checking the profile.</summary>
    public DateTime? VerifiedAccountCreatedAt { get; set; }

    [Range(0, SocialProfileRules.MaxFollowers)]
    public int? VerifiedFollowerCount { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}
