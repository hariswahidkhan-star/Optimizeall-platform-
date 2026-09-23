using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Domain.Content;

namespace OptimizeAll.Api.Modules.Content;

// ---------- Banners ----------

public sealed record BannerDto(
    Guid Id, string Title, string? Body, string? ImageUrl, string? CtaLabel, string? CtaUrl, ContentAudience Audience,
    string? CountryCode, string? LanguageCode, DateTime? StartsAt, DateTime? EndsAt, int SortOrder, bool IsActive,
    DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp);

public class BannerRequest
{
    [Required, MinLength(1), MaxLength(150)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Body { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    [MaxLength(60)]
    public string? CtaLabel { get; set; }

    [MaxLength(500)]
    public string? CtaUrl { get; set; }

    [Required]
    public ContentAudience? Audience { get; set; }

    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter country code.")]
    public string? CountryCode { get; set; }

    [MaxLength(10)]
    public string? LanguageCode { get; set; }

    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    [Range(-100000, 100000)]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class UpdateBannerRequest : BannerRequest
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class BannerQuery : PageQuery
{
    public bool? IsActive { get; set; }
    public ContentAudience? Audience { get; set; }
}

// ---------- Announcements ----------

public sealed record AnnouncementDto(
    Guid Id, string Title, string Body, AnnouncementSeverity Severity, ContentAudience Audience, DateTime PublishAt,
    DateTime? ExpiresAt, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp);

public class AnnouncementRequest
{
    [Required, MinLength(1), MaxLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(5000)]
    public string Body { get; set; } = string.Empty;

    [Required]
    public AnnouncementSeverity? Severity { get; set; }

    [Required]
    public ContentAudience? Audience { get; set; }

    [Required]
    public DateTime? PublishAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class UpdateAnnouncementRequest : AnnouncementRequest
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class AnnouncementQuery : PageQuery
{
    public bool? IsActive { get; set; }
    public ContentAudience? Audience { get; set; }
}

// ---------- FAQ ----------

public sealed record FaqDto(
    Guid Id, string Question, string Answer, string Category, int SortOrder, bool IsPublished,
    DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp);

public class FaqRequest
{
    [Required, MinLength(5), MaxLength(300)]
    public string Question { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(10000)]
    public string Answer { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(60)]
    public string Category { get; set; } = "General";

    [Range(-100000, 100000)]
    public int SortOrder { get; set; }

    public bool IsPublished { get; set; } = true;
}

public sealed class UpdateFaqRequest : FaqRequest
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class FaqQuery : PageQuery
{
    public string? Category { get; set; }
    public bool? IsPublished { get; set; }
}

public sealed record PublicFaqItemDto(Guid Id, string Question, string Answer);

public sealed record PublicFaqCategoryDto(string Category, IReadOnlyList<PublicFaqItemDto> Items);

public sealed record PublicFaqDto(IReadOnlyList<PublicFaqCategoryDto> Categories);

// ---------- Onboarding steps ----------

public sealed record OnboardingStepDto(
    Guid Id, string Key, string Title, string Description, string? ActionLabel, string? ActionUrl,
    OnboardingCompletionRule CompletionRule, int SortOrder, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp);

public class OnboardingStepRequest
{
    [Required, MinLength(2), MaxLength(60)]
    public string Key { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(60)]
    public string? ActionLabel { get; set; }

    [MaxLength(300)]
    public string? ActionUrl { get; set; }

    [Required]
    public OnboardingCompletionRule? CompletionRule { get; set; }

    [Range(-100000, 100000)]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class UpdateOnboardingStepRequest : OnboardingStepRequest
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class OnboardingStepQuery : PageQuery
{
    public bool? IsActive { get; set; }
}

// ---------- Shared ----------

public sealed class ReorderRequest
{
    /// <summary>Ids in the desired display order. Listed items get SortOrder 10, 20, 30…; unlisted items keep theirs.</summary>
    [Required, MinLength(1), MaxLength(500)]
    public List<Guid> Ids { get; set; } = new();
}

public sealed record ReorderResponse(int Updated);
