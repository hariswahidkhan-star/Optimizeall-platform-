using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Rewards;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Submissions;

namespace OptimizeAll.Api.Modules.Campaigns;

// ---------------------------------------------------------------- shared

public sealed record CategoryRefDto(Guid Id, string Name, string Slug);

public sealed record ReasonDto(string Code, string Message);

public sealed record CardRewardDto(string Currency, decimal BaseAmount, decimal MaxAmount, bool HasBonuses);

public sealed record CardEligibilityDto(bool IsEligible, IReadOnlyList<ReasonDto> Reasons);

// ---------------------------------------------------------------- participant

public sealed class CampaignBrowseQuery : PageQuery
{
    public SocialPlatform? Platform { get; set; }
    public Guid? CategoryId { get; set; }

    [MaxLength(40)]
    public string? Topic { get; set; }

    [Range(typeof(decimal), "0", "100000000")]
    public decimal? MinReward { get; set; }

    public DateTime? DeadlineBefore { get; set; }
    public bool EligibleOnly { get; set; }
}

public sealed record CampaignCardDto(
    Guid Id, string Slug, string Title, string Summary, CategoryRefDto? Category, IReadOnlyList<string> Topics,
    IReadOnlyList<SocialPlatform> Platforms, CampaignStatus Status, bool Upcoming, DateTime StartsAt, DateTime EndsAt,
    DateTime SubmissionDeadline, string? HeroImageUrl, CardRewardDto? Reward, CardEligibilityDto Eligibility,
    int MySubmissionCount, int RemainingSubmissions);

public sealed record RecommendedCampaignDto(CampaignCardDto Campaign, double Score, string Reason);

public sealed record CampaignAssetDto(Guid Id, CampaignAssetType Type, string Title, string? Url, Guid? FileId, string? Body,
    SocialPlatform? Platform, Guid? TemplateId, int SortOrder)
{
    public static CampaignAssetDto From(CampaignAsset a) =>
        new(a.Id, a.Type, a.Title, a.Url, a.FileId, a.Body, a.Platform, a.TemplateId, a.SortOrder);
}

public sealed record ResolvedDisclosureDto(SocialPlatform Platform, string Text);

public sealed record RewardOverrideTermDto(SocialPlatform? Platform, string? CountryCode, ParticipantTier? Tier, decimal Amount,
    DateTime? ValidFrom, DateTime? ValidTo, string? Label);

public sealed record RewardBonusTermDto(string Type, decimal Amount, string ApprovalMode, DateTime? ValidFrom, DateTime? ValidTo, string? Label);

public sealed record RewardTermsDto(
    string Currency, decimal BaseAmount, IReadOnlyList<RewardOverrideTermDto> Overrides, IReadOnlyList<RewardBonusTermDto> Bonuses,
    decimal? DailyCap, decimal? WeeklyCap, decimal? CampaignCap, int MinPostLiveHours, int MaxSubmissionsPerParticipant,
    bool RequireScreenshot, int RuleSetVersion);

public sealed record AccountEligibilityDto(Guid SocialAccountId, SocialPlatform Platform, string Handle, bool IsEligible,
    DateTime? EligibleFrom, IReadOnlyList<ReasonDto> Reasons);

public sealed record DetailEligibilityDto(bool IsEligible, IReadOnlyList<ReasonDto> Reasons, IReadOnlyList<AccountEligibilityDto> Accounts);

public sealed record MySubmissionRefDto(Guid Id, SubmissionStatus Status, DateTime SubmittedAt);

public sealed record CampaignDetailDto(
    Guid Id, string Slug, string Title, string Summary, CategoryRefDto? Category, IReadOnlyList<string> Topics,
    IReadOnlyList<SocialPlatform> Platforms, CampaignStatus Status, CampaignVisibility Visibility, bool Upcoming,
    bool IsOpenForSubmissions, DateTime StartsAt, DateTime EndsAt, DateTime SubmissionDeadline, string TimeZone,
    string? HeroImageUrl, string? LandingHeadline, string? LandingBody, CardRewardDto? Reward,
    string Description, string PostingInstructions, string? RequiredHashtags, string? RequiredMentions,
    IReadOnlyList<CampaignAssetDto> Assets, IReadOnlyList<ResolvedDisclosureDto> Disclosures, RewardTermsDto? RewardTerms,
    DetailEligibilityDto Eligibility, IReadOnlyList<MySubmissionRefDto> MySubmissions, int MySubmissionCount, int RemainingSubmissions,
    bool TrackingEnabled);

// ---------------------------------------------------------------- staff

public sealed class EligibilityInput
{
    [Range(0, 3650)]
    public int? MinAccountAgeDays { get; set; }

    [Range(0, 1_000_000_000)]
    public int MinFollowers { get; set; }

    public bool RequireVerifiedAccount { get; set; }

    [MaxLength(250)]
    public List<string> Countries { get; set; } = new();

    [MaxLength(50)]
    public List<string> Languages { get; set; } = new();

    [MaxLength(50)]
    public List<string> Interests { get; set; } = new();

    public List<ParticipantTier> Tiers { get; set; } = new();
}

public class CampaignFieldsInput
{
    [Required, MinLength(3), MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Lower-case URL slug; generated from the title when omitted.</summary>
    [MaxLength(100), RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Use lower-case letters, digits and single hyphens.")]
    public string? Slug { get; set; }

    [Required, MaxLength(500)]
    public string Summary { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string Description { get; set; } = string.Empty;

    public Guid? CategoryId { get; set; }

    [MaxLength(20)]
    public List<string> Topics { get; set; } = new();

    public CampaignVisibility Visibility { get; set; } = CampaignVisibility.Public;
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    /// <summary>Defaults to EndsAt + 3 days.</summary>
    public DateTime? SubmissionDeadline { get; set; }

    [Required, MaxLength(64)]
    public string TimeZone { get; set; } = "UTC";

    [MaxLength(20000)]
    public string PostingInstructions { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string DefaultDisclosureText { get; set; } = "#ad";

    [MaxLength(300)]
    public string? RequiredHashtags { get; set; }

    [MaxLength(300)]
    public string? RequiredMentions { get; set; }

    [Range(typeof(decimal), "0.01", "1000000000")]
    public decimal? BudgetAmount { get; set; }

    [StringLength(3, MinimumLength = 3)]
    public string? BudgetCurrency { get; set; }

    [Range(1, 100)]
    public int MaxSubmissionsPerParticipant { get; set; } = 1;

    [Range(0, 720)]
    public int MinPostLiveHours { get; set; }

    public bool RequireScreenshot { get; set; } = true;

    [Required]
    public EligibilityInput Eligibility { get; set; } = new();

    [Required, MinLength(1)]
    public List<SocialPlatform> Platforms { get; set; } = new();

    [MaxLength(200)]
    public string? LandingHeadline { get; set; }

    [MaxLength(20000)]
    public string? LandingBody { get; set; }

    [MaxLength(500)]
    public string? HeroImageUrl { get; set; }

    [MaxLength(1000)]
    public string? TrackingDestinationUrl { get; set; }

    [MaxLength(100)]
    public string? UtmCampaign { get; set; }
}

public sealed class CreateCampaignRequest : CampaignFieldsInput
{
    /// <summary>Initial reward rules (version 1). Requires rewards.edit.</summary>
    [Required]
    public RewardRuleSetInput RewardRules { get; set; } = new();
}

public sealed class UpdateCampaignRequest : CampaignFieldsInput
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }

    /// <summary>Required (true) with a reason when the budget changes.</summary>
    public bool Confirm { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}

public sealed class StatusChangeRequest
{
    [MaxLength(500)]
    public string? Reason { get; set; }
}

public sealed class AdminCampaignQuery : PageQuery
{
    public CampaignStatus? Status { get; set; }
    public Guid? CategoryId { get; set; }
}

/// <summary>An entry of GET /campaigns/options (staff campaign pickers).</summary>
public sealed record CampaignOptionDto(Guid Id, string Title, CampaignStatus Status);

public sealed record SubmissionCountsDto(int Total, int Pending, int Approved, int Rejected);

public sealed record AdminCampaignListItemDto(
    Guid Id, string Slug, string Title, CampaignStatus Status, CampaignVisibility Visibility, CategoryRefDto? Category,
    IReadOnlyList<SocialPlatform> Platforms, DateTime StartsAt, DateTime EndsAt, DateTime SubmissionDeadline,
    SubmissionCountsDto Submissions, string Currency, decimal Spent, decimal? Budget, decimal? BudgetRemaining,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? PublishedAt);

public sealed record EligibilityDto(int? MinAccountAgeDays, int MinFollowers, bool RequireVerifiedAccount,
    IReadOnlyList<string> Countries, IReadOnlyList<string> Languages, IReadOnlyList<string> Interests, IReadOnlyList<ParticipantTier> Tiers)
{
    public static EligibilityDto From(CampaignEligibility e) =>
        new(e.MinAccountAgeDays, e.MinFollowers, e.RequireVerifiedAccount, e.Countries, e.Languages, e.Interests, e.Tiers);
}

public sealed record DisclosureDto(Guid Id, SocialPlatform? Platform, string? CountryCode, string Text);

public sealed record AdminCampaignDto(
    Guid Id, string Slug, string Title, string Summary, string Description, CategoryRefDto? Category, IReadOnlyList<string> Topics,
    CampaignStatus Status, CampaignVisibility Visibility, DateTime StartsAt, DateTime EndsAt, DateTime SubmissionDeadline,
    string TimeZone, string PostingInstructions, string DefaultDisclosureText, string? RequiredHashtags, string? RequiredMentions,
    decimal? BudgetAmount, string BudgetCurrency, decimal Spent, decimal? BudgetRemaining, int MaxSubmissionsPerParticipant,
    int MinPostLiveHours, bool RequireScreenshot, EligibilityDto Eligibility, IReadOnlyList<SocialPlatform> Platforms,
    string? LandingHeadline, string? LandingBody, string? HeroImageUrl, string? TrackingDestinationUrl, string? UtmCampaign,
    IReadOnlyList<CampaignAssetDto> Assets, IReadOnlyList<DisclosureDto> Disclosures, RewardRuleSetDto? CurrentRuleSet,
    SubmissionCountsDto Submissions, Guid CreatedByUserId, DateTime CreatedAt, DateTime UpdatedAt, DateTime? PublishedAt,
    Guid ConcurrencyStamp);

public sealed class AssetInput
{
    public CampaignAssetType Type { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Url { get; set; }

    public Guid? FileId { get; set; }

    [MaxLength(10000)]
    public string? Body { get; set; }

    public SocialPlatform? Platform { get; set; }
    public int? SortOrder { get; set; }
    public Guid? TemplateId { get; set; }
}

public sealed class ReorderAssetsRequest
{
    [Required, MinLength(1)]
    public List<Guid> AssetIds { get; set; } = new();
}

public sealed class DisclosureInput
{
    public SocialPlatform? Platform { get; set; }

    [RegularExpression("^[A-Za-z]{2}$")]
    public string? CountryCode { get; set; }

    [Required, MinLength(1), MaxLength(500)]
    public string Text { get; set; } = string.Empty;
}

public sealed class ReplaceDisclosuresRequest
{
    [Required, MaxLength(200)]
    public List<DisclosureInput> Disclosures { get; set; } = new();
}

public sealed class CategoryInput
{
    [Required, MinLength(2), MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100), RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    public string? Slug { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Icon { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed record CategoryDto(Guid Id, string Name, string Slug, string? Description, string? Icon, int SortOrder, bool IsActive)
{
    public static CategoryDto From(CampaignCategory c) => new(c.Id, c.Name, c.Slug, c.Description, c.Icon, c.SortOrder, c.IsActive);
}

public sealed record AdminCategoryDto(Guid Id, string Name, string Slug, string? Description, string? Icon, int SortOrder, bool IsActive, int CampaignCount);

public sealed record CategoryDeleteResultDto(bool Deleted, bool Deactivated, int CampaignCount);
