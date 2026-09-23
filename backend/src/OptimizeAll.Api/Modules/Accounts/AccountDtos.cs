using System.ComponentModel.DataAnnotations;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Content;
using OptimizeAll.Domain.Identity;

namespace OptimizeAll.Api.Modules.Accounts;

public sealed record ProfileDto(
    Guid Id,
    string Email,
    bool EmailVerified,
    string DisplayName,
    string CountryCode,
    string LanguageCode,
    string TimeZone,
    IReadOnlyList<string> Interests,
    bool MarketingEmailOptIn,
    string? WhatsAppNumber,
    bool WhatsAppOptIn,
    ParticipantTier Tier,
    string ReferralCode,
    DateTime CreatedAt);

public sealed class UpdateProfileRequest
{
    [Required, MinLength(2), MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter country code.")]
    public string CountryCode { get; set; } = string.Empty;

    [Required, MaxLength(10)]
    public string LanguageCode { get; set; } = "en";

    [Required, MaxLength(64)]
    public string TimeZone { get; set; } = "UTC";

    public List<string>? Interests { get; set; }

    public bool MarketingEmailOptIn { get; set; }

    [MaxLength(20)]
    public string? WhatsAppNumber { get; set; }

    public bool WhatsAppOptIn { get; set; }
}

public sealed record PayoutProfileDto(
    bool Configured,
    PayoutMethod? Method,
    string? AccountHolderName,
    string? DestinationHint,
    string? PreferredCurrency,
    string? CountryCode,
    DateTime? UpdatedAt);

public sealed class UpdatePayoutProfileRequest
{
    [Required]
    public PayoutMethod? Method { get; set; }

    [Required, MinLength(2), MaxLength(150)]
    public string AccountHolderName { get; set; } = string.Empty;

    /// <summary>Raw IBAN / account number / PayPal email / wallet number. Encrypted at rest and never returned.</summary>
    [Required, MaxLength(200)]
    public string Destination { get; set; } = string.Empty;

    [Required, StringLength(3, MinimumLength = 3)]
    public string PreferredCurrency { get; set; } = "USD";

    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter country code.")]
    public string? CountryCode { get; set; }
}

// ---------- Home ----------

public enum HomeState
{
    VerifyEmail,
    AddSocialAccount,
    AwaitingEligibility,
    Ready,
    Active,
}

public sealed record HomeDto(
    HomeUserDto User,
    HomeState State,
    DateTime? EligibleFrom,
    OnboardingDto Onboarding,
    IReadOnlyList<BannerViewDto> Banners,
    IReadOnlyList<AnnouncementViewDto> Announcements,
    SubmissionCountsDto Submissions,
    int UnreadNotificationCount,
    int OpenSupportTicketCount,
    SocialAccountsSummaryDto SocialAccounts);

public sealed record HomeUserDto(
    Guid Id, string DisplayName, bool EmailVerified, ParticipantTier Tier, string TimeZone, string CountryCode, string LanguageCode);

public sealed record OnboardingDto(IReadOnlyList<OnboardingStepViewDto> Steps, int CompletedCount, int TotalCount, int ProgressPercent);

public sealed record OnboardingStepViewDto(
    Guid Id, string Key, string Title, string Description, string? ActionLabel, string? ActionUrl,
    OnboardingCompletionRule CompletionRule, bool IsManual, bool Completed);

public sealed record BannerViewDto(Guid Id, string Title, string? Body, string? ImageUrl, string? CtaLabel, string? CtaUrl);

public sealed record AnnouncementViewDto(Guid Id, string Title, string Body, AnnouncementSeverity Severity, DateTime PublishAt, DateTime? ExpiresAt);

public sealed record SubmissionCountsDto(
    int Total, int Pending, int UnderReview, int Approved, int NeedsCorrection, int Rejected, int Reversed);

public sealed record SocialAccountsSummaryDto(int Total, int Eligible, DateTime? SoonestEligibleFrom, int MinAccountAgeDays);

public sealed record OnboardingCompleteResponse(Guid StepId, bool Completed, DateTime CompletedAt);
