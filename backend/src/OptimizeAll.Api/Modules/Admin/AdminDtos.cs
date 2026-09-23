using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Jobs;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Social;

namespace OptimizeAll.Api.Modules.Admin;

// ---------- Users ----------

public sealed class AdminUserQuery : PageQuery
{
    public Role? Role { get; set; }
    public UserStatus? Status { get; set; }

    [RegularExpression("^[A-Za-z]{2}$")]
    public string? Country { get; set; }

    public ParticipantTier? Tier { get; set; }
}

public sealed record AdminUserListItemDto(
    Guid Id, string Email, string DisplayName, string CountryCode, UserStatus Status, ParticipantTier Tier,
    IReadOnlyList<Role> Roles, bool EmailVerified, DateTime CreatedAt, DateTime? LastActiveAt);

public sealed record AdminUserProfileDto(
    Guid Id, string Email, string DisplayName, string CountryCode, string LanguageCode, string TimeZone,
    IReadOnlyList<string> Interests, UserStatus Status, string? StatusReason, DateTime? StatusChangedAt, ParticipantTier Tier,
    string ReferralCode, bool EmailVerified, DateTime? EmailVerifiedAt, bool MarketingEmailOptIn, bool WhatsAppOptIn,
    string? WhatsAppNumberHint, DateTime? LastLoginAt, DateTime? LastActiveAt, DateTime CreatedAt);

public sealed record StatusHistoryDto(DateTime At, string Action, Guid? ActorUserId, string? ActorDisplayName, string? Reason);

public sealed record AdminSocialAccountDto(
    Guid Id, SocialPlatform Platform, string Handle, string ProfileUrl, DateTime AccountCreatedAt, int AccountAgeDays,
    int FollowerCount, SocialAccountVerificationStatus VerificationStatus, bool IsActive);

public sealed record EarningTotalDto(EarningStatus Status, string Currency, decimal Amount, int Count);

public sealed record PayoutHoldDto(Guid Id, string Reason, DateTime CreatedAt, Guid CreatedByUserId);

public sealed record PayoutProfileSummaryDto(PayoutMethod Method, string DestinationHint, string PreferredCurrency, DateTime UpdatedAt);

public sealed record AdminUserDetailDto(
    AdminUserProfileDto Profile,
    IReadOnlyList<Role> Roles,
    IReadOnlyList<StatusHistoryDto> StatusHistory,
    IReadOnlyList<AdminSocialAccountDto> SocialAccounts,
    SubmissionCountsDto SubmissionCounts,
    IReadOnlyList<EarningTotalDto> Earnings,
    IReadOnlyList<PayoutHoldDto> ActivePayoutHolds,
    PayoutProfileSummaryDto? PayoutProfile,
    IReadOnlyList<AuditLogDto> RecentAudit,
    Guid ConcurrencyStamp);

public class ReasonRequest
{
    [Required, MinLength(3), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class SuspendUserRequest : ReasonRequest
{
    public bool Confirm { get; set; }
}

public sealed class ReactivateUserRequest : ReasonRequest
{
}

public sealed class SetRolesRequest : ReasonRequest
{
    [Required, MinLength(1), MaxLength(5)]
    public List<Role> Roles { get; set; } = new();

    public bool Confirm { get; set; }
}

public sealed class SetTierRequest : ReasonRequest
{
    [Required]
    public ParticipantTier? Tier { get; set; }
}

public sealed class CreateStaffRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(2), MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter country code.")]
    public string CountryCode { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(5)]
    public List<Role> Roles { get; set; } = new();
}

// ---------- Settings ----------

public sealed record SettingActorDto(Guid Id, string DisplayName);

public sealed record SettingDto(
    string Key, JsonElement Value, JsonElement DefaultValue, bool IsDefault, string ValueType, string Description,
    DateTime? UpdatedAt, SettingActorDto? UpdatedBy);

public sealed class UpdateSettingRequest : ReasonRequest
{
    /// <summary>The new value as JSON (number, boolean or object depending on the key).</summary>
    [Required]
    public JsonElement? Value { get; set; }

    public bool Confirm { get; set; }
}

// ---------- Audit ----------

public sealed class AuditLogQuery : PageQuery
{
    /// <summary>Action prefix, e.g. "admin." or "admin.user_suspended".</summary>
    [MaxLength(100)]
    public string? Action { get; set; }

    [MaxLength(60)]
    public string? EntityType { get; set; }

    [MaxLength(64)]
    public string? EntityId { get; set; }

    public Guid? ActorUserId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public sealed record AuditLogDto(
    long Id, DateTime CreatedAt, Guid? ActorUserId, string? ActorEmail, string? ActorDisplayName, string ActorType,
    string Action, string EntityType, string EntityId, JsonElement? Before, JsonElement? After, string? Reason,
    string? IpAddress, string? CorrelationId);

// ---------- Jobs ----------

public sealed record JobRunDto(
    Guid Id, string JobName, string RunKey, JobRunStatus Status, int Attempt, DateTime StartedAt, DateTime? FinishedAt,
    string? Summary, string? Error, string? InstanceId);

public sealed record JobDto(string Name, string JobName, int IntervalSeconds, JobRunDto? LastRun);

public sealed class JobRunQuery : PageQuery
{
    [MaxLength(100)]
    public string? JobName { get; set; }

    public JobRunStatus? Status { get; set; }
}
