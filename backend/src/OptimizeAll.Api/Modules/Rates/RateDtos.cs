using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;

namespace OptimizeAll.Api.Modules.Rates;

// ------------------------------------------------------------------ rate cards

public sealed class RateCardLineInput
{
    public SocialPlatform? Platform { get; set; }
    public ContentFormat? Format { get; set; }

    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter country code.")]
    public string? CountryCode { get; set; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal Amount { get; set; }

    [MaxLength(150)]
    public string? Label { get; set; }
}

/// <summary>A card version's rates: currency, lines, optional caps and whether campaign bonuses stack.</summary>
public class RateCardRatesInput
{
    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";

    [Range(typeof(decimal), "0.0001", "100000000")]
    public decimal? DailyCapPerParticipant { get; set; }

    [Range(typeof(decimal), "0.0001", "100000000")]
    public decimal? WeeklyCapPerParticipant { get; set; }

    [Range(typeof(decimal), "0.0001", "100000000")]
    public decimal? CampaignCapPerParticipant { get; set; }

    public bool StackCampaignBonuses { get; set; } = true;

    [Required, MinLength(1), MaxLength(RateCardRules.MaxLines)]
    public List<RateCardLineInput> Lines { get; set; } = new();
}

public sealed class CreateRateCardRequest : RateCardRatesInput
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Activate immediately (assignable) instead of leaving the card as a draft.</summary>
    public bool Activate { get; set; }
}

public sealed class UpdateRateCardRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class CreateRateCardVersionRequest : RateCardRatesInput
{
    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>When the version takes effect (UTC; default now). Must not be in the past.</summary>
    public DateTime? EffectiveFrom { get; set; }

    /// <summary>The version the editor started from; a newer saved version answers 409 rate_card.version_conflict.</summary>
    [Range(0, int.MaxValue)]
    public int? BaseVersion { get; set; }

    /// <summary>Must be true: changing rates is a sensitive action.</summary>
    public bool Confirm { get; set; }
}

public sealed class ApproveRateCardVersionRequest
{
    [MaxLength(500)]
    public string? Note { get; set; }
}

public sealed class RejectRateCardVersionRequest
{
    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class ArchiveRateCardRequest
{
    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>End the card's active assignments too (otherwise a card in use cannot be archived).</summary>
    public bool EndAssignments { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class ActivateRateCardRequest
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class DuplicateRateCardRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;
}

public sealed class RateCardQuery : PageQuery
{
    public RateCardStatus? Status { get; set; }

    [RegularExpression("^[A-Za-z]{3}$")]
    public string? Currency { get; set; }
}

public sealed record RateCardLineDto(Guid Id, SocialPlatform? Platform, ContentFormat? Format, string? CountryCode, decimal Amount, string? Label)
{
    public static RateCardLineDto From(RateCardLine l) => new(l.Id, l.Platform, l.Format, l.CountryCode, l.Amount, l.Label);
}

public sealed record RateCardVersionDto(
    Guid Id, int Version, string Currency, decimal? DailyCapPerParticipant, decimal? WeeklyCapPerParticipant,
    decimal? CampaignCapPerParticipant, bool StackCampaignBonuses, DateTime EffectiveFrom, DateTime CreatedAt, UserRefDto? CreatedBy,
    string Reason, RateCardVersionStatus Status, UserRefDto? DecidedBy, DateTime? DecidedAt, string? DecisionNote,
    decimal? MaxIncreasePercent, bool IsCurrent, IReadOnlyList<RateCardLineDto> Lines);

public sealed record RateCardListItemDto(
    Guid Id, string Name, string? Description, RateCardKind Kind, RateCardStatus Status, string Currency, int CurrentVersion,
    int LineCount, decimal? MinAmount, decimal? MaxAmount, int ActiveAssignments, bool PendingApproval, DateTime UpdatedAt);

public sealed record RateCardRefDto(Guid Id, string Name, RateCardKind Kind, RateCardStatus Status, string Currency, int CurrentVersion);

public sealed record RateCardDto(
    Guid Id, string Name, string? Description, RateCardKind Kind, RateCardStatus Status, string Currency, int CurrentVersion,
    UserRefDto? Owner, DateTime CreatedAt, DateTime UpdatedAt, DateTime? ArchivedAt, string? ArchiveReason, Guid ConcurrencyStamp,
    IReadOnlyList<RateCardVersionDto> Versions, IReadOnlyList<RateAssignmentDto> Assignments, int UsedBySubmissions,
    bool FourEyesRequiredAbovePercent, int FourEyesThresholdPercent);

// ------------------------------------------------------------------ rate groups

public class RateGroupInput
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Range(-1000, 1000)]
    public int Priority { get; set; }

    public RateGroupMembershipMode MembershipMode { get; set; } = RateGroupMembershipMode.Manual;

    [MaxLength(4)]
    public List<ParticipantTier>? AutoTiers { get; set; }

    [Range(0, 2_000_000_000)]
    public int? AutoMinFollowers { get; set; }

    [Range(1, 2_000_000_000)]
    public int? AutoMaxFollowers { get; set; }

    public bool AutoRequireVerified { get; set; } = true;
}

public sealed class CreateRateGroupRequest : RateGroupInput;

public sealed class UpdateRateGroupRequest : RateGroupInput
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}

public sealed class ArchiveRateGroupRequest
{
    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Remove all members and end the group's assignments (otherwise a group in use cannot be archived).</summary>
    public bool Force { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class AddRateGroupMembersRequest
{
    [Required, MinLength(1), MaxLength(RateGroupLimits.MaxBulk)]
    public List<Guid> UserIds { get; set; } = new();

    [MaxLength(300)]
    public string? Note { get; set; }
}

public sealed class RemoveRateGroupMembersRequest
{
    [Required, MinLength(1), MaxLength(RateGroupLimits.MaxBulk)]
    public List<Guid> UserIds { get; set; } = new();

    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

public static class RateGroupLimits
{
    /// <summary>Most people one bulk request or CSV file may add/remove.</summary>
    public const int MaxBulk = 10_000;

    /// <summary>CSV upload limit (10k rows of emails fit comfortably).</summary>
    public const long MaxCsvBytes = 2 * 1024 * 1024;
}

public sealed class RateGroupQuery : PageQuery
{
    public bool IncludeArchived { get; set; }
    public RateGroupMembershipMode? Mode { get; set; }
}

public sealed class RateGroupMembersQuery : PageQuery;

public sealed record RateGroupListItemDto(
    Guid Id, string Name, string? Description, int Priority, RateGroupMembershipMode MembershipMode, string? AutoRule,
    int? MemberCount, IReadOnlyList<RateCardRefDto> Cards, DateTime? ArchivedAt, DateTime UpdatedAt);

public sealed record RateGroupDto(
    Guid Id, string Name, string? Description, int Priority, RateGroupMembershipMode MembershipMode,
    IReadOnlyList<ParticipantTier> AutoTiers, int? AutoMinFollowers, int? AutoMaxFollowers, bool AutoRequireVerified, string? AutoRule,
    int? MemberCount, DateTime CreatedAt, DateTime UpdatedAt, DateTime? ArchivedAt, string? ArchiveReason, Guid ConcurrencyStamp,
    IReadOnlyList<RateAssignmentDto> Assignments);

public sealed record RateGroupMemberDto(
    Guid UserId, string DisplayName, string Email, string CountryCode, ParticipantTier Tier, UserStatus Status, bool IsTestAccount,
    DateTime? AddedAt, UserRefDto? AddedBy, string? Note, int? Followers);

public sealed record BulkIssueDto(int? Row, string Value, Guid? UserId, string Code, string Message);

public sealed record BulkMembersResultDto(
    int Requested, int Added, int Unchanged, int Removed, IReadOnlyList<BulkIssueDto> Rejected, IReadOnlyList<BulkIssueDto> Warnings);

public sealed record CsvImportResultDto(
    bool DryRun, int Rows, int Valid, int Added, int AlreadyMembers, IReadOnlyList<BulkIssueDto> Rejected, IReadOnlyList<BulkIssueDto> Warnings);

public sealed record RateGroupMemberEventDto(Guid UserId, string DisplayName, RateGroupMemberAction Action, DateTime At, UserRefDto? Actor,
    string Source, string? Reason);

// ------------------------------------------------------------------ assignments

public sealed class CreateRateAssignmentRequest
{
    [Required]
    public Guid? RateCardId { get; set; }

    [Required]
    public RateAssignmentTarget? Target { get; set; }

    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }

    /// <summary>Null = every campaign.</summary>
    public Guid? CampaignId { get; set; }

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class UpdateRateAssignmentRequest
{
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class EndRateAssignmentRequest
{
    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class RateAssignmentQuery : PageQuery
{
    public Guid? UserId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? RateCardId { get; set; }
    public bool ActiveOnly { get; set; }
}

public sealed record RateGroupRefDto(Guid Id, string Name, RateGroupMembershipMode MembershipMode, int Priority);

public sealed record RateCampaignRefDto(Guid Id, string Title);

public sealed record RateAssignmentDto(
    Guid Id, RateSourceLevel Level, string LevelLabel, RateAssignmentTarget Target, RateCardRefDto Card, UserRefDto? Person,
    RateGroupRefDto? Group, RateCampaignRefDto? Campaign, bool IsCustom, DateTime? ValidFrom, DateTime? ValidTo, DateTime? EndedAt,
    string? EndReason, string Note, bool IsActive, DateTime CreatedAt, UserRefDto? CreatedBy, Guid ConcurrencyStamp);

public sealed class CreateCustomRateRequest : RateCardRatesInput
{
    /// <summary>Null = the deal applies in every campaign.</summary>
    public Guid? CampaignId { get; set; }

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    [MaxLength(120)]
    public string? Name { get; set; }

    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

// ------------------------------------------------------------------ effective rates, explain, simulate

public sealed record PersonGroupDto(Guid Id, string Name, RateGroupMembershipMode MembershipMode, int Priority, DateTime? AddedAt,
    IReadOnlyList<SocialPlatform> MatchedPlatforms);

public sealed record EffectiveRateDto(
    SocialPlatform Platform, ContentFormat? Format, RateSourceLevel Level, string LevelLabel, string SourceLabel, decimal Amount,
    string Currency, Guid? AssignmentId, Guid? RateCardId, int? RateCardVersion, DateTime? ValidTo);

public sealed record PersonRatesDto(
    UserRefDto User, string CountryCode, ParticipantTier Tier, UserStatus Status, bool IsTestAccount, RateCampaignRefDto? Campaign,
    IReadOnlyList<PersonGroupDto> Groups, IReadOnlyList<RateAssignmentDto> Assignments, IReadOnlyList<EffectiveRateDto> Effective,
    IReadOnlyList<PrecedenceLevelDto> Precedence);

public sealed record PrecedenceLevelDto(int Rank, RateSourceLevel Level, string Label);

public sealed record ExplainCandidateDto(
    RateSourceLevel Level, string LevelLabel, RateOutcome Outcome, string Reason, Guid AssignmentId, Guid RateCardId, string CardName,
    int? Version, Guid? GroupId, string? GroupName, int Priority, Guid? LineId, string? LineConditions, decimal? Amount, string? Currency,
    DateTime? ValidFrom, DateTime? ValidTo);

public sealed record RateConversionDto(string FromCurrency, string ToCurrency, decimal Rate, Guid? ExchangeRateId, decimal CardAmount,
    decimal Amount);

public sealed record RateExplanationDto(
    SocialPlatform Platform, ContentFormat? Format, string CountryCode, ParticipantTier Tier, int? Followers, DateTime EvaluatedAt,
    RateCampaignRefDto? Campaign, PersonalRatesMode? CampaignPolicy, decimal? MaxMultiplier, RateSourceLevel Winner,
    string Summary, IReadOnlyList<ExplainCandidateDto> Candidates, RateConversionDto? Conversion, string? ConversionError,
    RewardQuoteDto? Quote, IReadOnlyList<PrecedenceLevelDto> Precedence);

public sealed class ExplainRateQuery
{
    [Required]
    public SocialPlatform? Platform { get; set; }

    public ContentFormat? Format { get; set; }
    public Guid? CampaignId { get; set; }
    public DateTime? At { get; set; }
}

public sealed class SimulateRateRequest
{
    [Required]
    public Guid? UserId { get; set; }

    [Required]
    public SocialPlatform? Platform { get; set; }

    public ContentFormat? Format { get; set; }
    public DateTime? PostedAt { get; set; }
    public bool IsFirstApprovedPost { get; set; }
}

public sealed record CampaignRatesDto(
    Guid CampaignId, string Currency, PersonalRatesMode PersonalRatesMode, decimal? PersonalRateMaxMultiplier,
    IReadOnlyList<RateAssignmentDto> CampaignAssignments, IReadOnlyList<RateAssignmentDto> GlobalAssignments,
    IReadOnlyList<string> FxProblems, int PeopleWithPersonalRates);

// ------------------------------------------------------------------ participant

public sealed record YourRateEntryDto(SocialPlatform Platform, ContentFormat? Format, decimal Amount);

/// <summary>
/// The signed-in participant's own person-level rate in a campaign (null when the campaign rates apply). Never names
/// the card or group: Kind is "Personal" for a personal deal and "Special" for a group/segment rate.
/// </summary>
public sealed record YourRateDto(string Currency, decimal MinAmount, decimal MaxAmount, string Kind, DateTime? ValidTo,
    IReadOnlyList<YourRateEntryDto> Entries);
