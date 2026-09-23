using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Domain.Agency;

namespace OptimizeAll.Api.Modules.Clients;

public sealed record PersonDto(Guid Id, string DisplayName, string Email);

// ---------------------------------------------------------------- client accounts

public sealed class ClientListQuery : PageQuery
{
    public ClientAccountStatus? Status { get; set; }

    /// <summary>A user id or "me".</summary>
    [MaxLength(40)]
    public string? AccountManager { get; set; }
}

public sealed record ClientSummaryDto(
    Guid Id, string Name, string Slug, string? Industry, ClientAccountStatus Status, string Currency, string CountryCode,
    PersonDto? AccountManager, string? LogoUrl, int ActiveProjects, int MemberCount, DateTime CreatedAt);

public sealed record ClientDetailDto(
    Guid Id, string Name, string Slug, string? Summary, string? Industry, string? Website, string CountryCode, string TimeZone,
    string Currency, ClientAccountStatus Status, string? StatusReason, DateTime? StatusChangedAt, PersonDto? AccountManager,
    Guid? LogoFileId, string? LogoUrl, string? BillingContactName, string? BillingEmail, string? BillingAddress, string? TaxId,
    string? Notes, int ApprovalSlaDays, int? AutoApproveAfterDays, DateTime? LastInvoicePaidAt, DateTime CreatedAt,
    DateTime UpdatedAt, Guid ConcurrencyStamp);

public class ClientProfileRequest
{
    [Required, MinLength(2), MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Summary { get; set; }

    [MaxLength(100)]
    public string? Industry { get; set; }

    [MaxLength(500)]
    public string? Website { get; set; }

    [Required, StringLength(2, MinimumLength = 2)]
    public string CountryCode { get; set; } = "US";

    [Required, MaxLength(64)]
    public string TimeZone { get; set; } = "UTC";

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";

    public Guid? AccountManagerUserId { get; set; }

    [MaxLength(200)]
    public string? BillingContactName { get; set; }

    [EmailAddress, MaxLength(254)]
    public string? BillingEmail { get; set; }

    [MaxLength(1000)]
    public string? BillingAddress { get; set; }

    [MaxLength(64)]
    public string? TaxId { get; set; }

    [MaxLength(4000)]
    public string? Notes { get; set; }

    [Range(1, 30)]
    public int ApprovalSlaDays { get; set; } = 3;

    /// <summary>Null = auto-approve off (default).</summary>
    [Range(1, 60)]
    public int? AutoApproveAfterDays { get; set; }
}

public sealed class CreateClientRequest : ClientProfileRequest
{
    /// <summary>Optional; generated from the name when empty. Lower-case letters, digits and hyphens.</summary>
    [MaxLength(120)]
    public string? Slug { get; set; }

    public ClientAccountStatus Status { get; set; } = ClientAccountStatus.Onboarding;
}

public sealed class UpdateClientRequest : ClientProfileRequest
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class ChangeClientStatusRequest
{
    [Required]
    public ClientAccountStatus? Status { get; set; }

    /// <summary>Required for Paused and Churned.</summary>
    [MaxLength(1000)]
    public string? Reason { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

// ---------------------------------------------------------------- team

public sealed record TeamAssignmentDto(Guid Id, PersonDto User, ClientServiceRole ServiceRole, bool IsPrimary, DateTime AssignedAt);

public sealed class AddTeamMemberRequest
{
    [Required]
    public Guid? UserId { get; set; }

    [Required]
    public ClientServiceRole? ServiceRole { get; set; }

    public bool IsPrimary { get; set; }
}

/// <summary>The account team as a client user sees it (contact details of their agency people).</summary>
public sealed record AccountTeamMemberDto(Guid UserId, string DisplayName, string Email, IReadOnlyList<ClientServiceRole> Roles, bool IsAccountManager, bool IsPrimary);

// ---------------------------------------------------------------- client users

public sealed record ClientMemberDto(Guid UserId, string DisplayName, string Email, ClientMemberRole Role, DateTime AddedAt, DateTime? LastLoginAt, bool HasSignedIn);

public sealed class InviteClientUserRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(2), MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    public ClientMemberRole? Role { get; set; }
}

public sealed record InviteResultDto(ClientMemberDto Member, bool UserCreated);

public sealed class ChangeMemberRoleRequest
{
    [Required]
    public ClientMemberRole? Role { get; set; }
}

public sealed record MyOrganizationDto(Guid ClientId, string Name, string Slug, ClientAccountStatus Status, ClientMemberRole Role, string? LogoUrl, string Currency, string TimeZone);

// ---------------------------------------------------------------- onboarding

public sealed record OnboardingItemDto(
    Guid Id, string Key, string Title, string? Description, string Category, OnboardingOwner Owner, int SortOrder,
    OnboardingItemStatus Status, DateTime? CompletedAt, string? CompletedBy, string? Note);

public sealed record OnboardingDto(IReadOnlyList<OnboardingItemDto> Items, int Done, int Total, int PercentComplete);

public sealed class UpdateOnboardingItemRequest
{
    [Required]
    public OnboardingItemStatus? Status { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }
}

public sealed class AddOnboardingItemRequest
{
    [Required, MinLength(2), MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(64)]
    public string Category { get; set; } = "Other";

    public OnboardingOwner Owner { get; set; } = OnboardingOwner.Agency;
}

// ---------------------------------------------------------------- brand kit

public sealed record BrandAssetDto(Guid Id, BrandAssetKind Kind, string Label, Guid FileId, string FileName, string ContentType, long SizeBytes, string StaffUrl, string ClientUrl, DateTime CreatedAt);

public sealed record BrandKitDto(
    Guid ClientAccountId, IReadOnlyList<BrandColor> Colors, IReadOnlyList<string> Fonts, string? ToneOfVoice,
    IReadOnlyList<BrandPersona> Personas, IReadOnlyList<string> Competitors, IReadOnlyList<string> Dos, IReadOnlyList<string> Donts,
    IReadOnlyList<string> KeyMessages, IReadOnlyList<BrandAssetDto> Assets, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class UpdateBrandKitRequest
{
    public List<BrandColor> Colors { get; set; } = new();
    public List<string> Fonts { get; set; } = new();

    [MaxLength(4000)]
    public string? ToneOfVoice { get; set; }

    public List<BrandPersona> Personas { get; set; } = new();
    public List<string> Competitors { get; set; } = new();
    public List<string> Dos { get; set; } = new();
    public List<string> Donts { get; set; } = new();
    public List<string> KeyMessages { get; set; } = new();

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class BrandAssetForm
{
    public IFormFile? File { get; set; }
    public BrandAssetKind Kind { get; set; } = BrandAssetKind.Other;

    [MaxLength(200)]
    public string? Label { get; set; }
}

// ---------------------------------------------------------------- feedback

public sealed class SubmitNpsRequest
{
    [Required, Range(0, 10)]
    public int? Score { get; set; }

    [MaxLength(2000)]
    public string? Comment { get; set; }
}

public sealed class SubmitCsatRequest
{
    [Required, Range(1, 5)]
    public int? Score { get; set; }

    [MaxLength(2000)]
    public string? Comment { get; set; }
}

public sealed record FeedbackItemDto(Guid Id, ClientFeedbackKind Kind, int Score, string? Comment, string? Period, Guid? DeliverableId, string? DeliverableTitle, PersonDto User, DateTime CreatedAt);

public sealed record FeedbackSummaryDto(
    double? AverageCsat, int CsatResponses, int? NpsScore, int NpsResponses, int Promoters, int Passives, int Detractors,
    IReadOnlyList<FeedbackItemDto> Recent);

public sealed record NpsStatusDto(string Period, bool Due, int? MyScore);

// ---------------------------------------------------------------- health

public sealed record ClientHealthDto(Guid ClientId, string ClientName, ClientAccountStatus Status, int Score, HealthLevel Level,
    IReadOnlyList<HealthReason> Reasons, int OverdueTasks, int PendingApprovals, PersonDto? AccountManager);
