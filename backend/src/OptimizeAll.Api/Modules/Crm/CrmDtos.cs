using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Domain.Crm;

namespace OptimizeAll.Api.Modules.Crm;

public sealed record UserRefDto(Guid Id, string DisplayName, string Email);

public sealed record UtmDto(string? Source, string? Medium, string? Campaign, DateTime? At);

// ---------------------------------------------------------------- Companies

public sealed class CompanyRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Domain { get; set; }

    [MaxLength(100)]
    public string? Industry { get; set; }

    public CompanySize Size { get; set; } = CompanySize.Unknown;

    [StringLength(2, MinimumLength = 2)]
    public string? CountryCode { get; set; }

    public Guid? OwnerUserId { get; set; }

    [MaxLength(20)]
    public List<string>? Tags { get; set; }

    /// <summary>A flat JSON object (text, number, boolean or null values).</summary>
    [MaxLength(20000)]
    public string? CustomFields { get; set; }

    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class CompanyQuery : PageQuery
{
    public Guid? OwnerUserId { get; set; }
    public string? Industry { get; set; }
    public string? Tag { get; set; }

    /// <summary>false/omitted: active records only; true: archived records only.</summary>
    public bool? Archived { get; set; }
}

public sealed record CompanySummaryDto(
    Guid Id, string Name, string? Domain, string? Industry, CompanySize Size, string? CountryCode, UserRefDto? Owner,
    IReadOnlyList<string> Tags, int Contacts, int OpenDeals, Guid? ClientAccountId, DateTime CreatedAt, DateTime? ArchivedAt = null,
    Guid ConcurrencyStamp = default);

public sealed record CompanyDto(
    Guid Id, string Name, string? Domain, string? Industry, CompanySize Size, string? CountryCode, UserRefDto? Owner,
    IReadOnlyList<string> Tags, string CustomFields, Guid? ClientAccountId, IReadOnlyList<ContactSummaryDto> Contacts,
    IReadOnlyList<DealSummaryDto> Deals, DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp, DateTime? ArchivedAt = null);

// ---------------------------------------------------------------- Contacts

public sealed class ContactRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? LastName { get; set; }

    [MaxLength(254), EmailAddress]
    public string? Email { get; set; }

    [MaxLength(40)]
    public string? Phone { get; set; }

    [MaxLength(120)]
    public string? JobTitle { get; set; }

    public Guid? CompanyId { get; set; }
    public LifecycleStage LifecycleStage { get; set; } = LifecycleStage.Lead;
    public Guid? OwnerUserId { get; set; }
    public ConsentStatus ConsentStatus { get; set; } = ConsentStatus.Unknown;

    [MaxLength(20)]
    public List<string>? Tags { get; set; }

    [MaxLength(100)]
    public string? Source { get; set; }

    [MaxLength(60)]
    public string? BudgetRange { get; set; }

    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class ContactQuery : PageQuery
{
    public LifecycleStage? LifecycleStage { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Guid? CompanyId { get; set; }
    public string? Tag { get; set; }
    public ConsentStatus? ConsentStatus { get; set; }
    public int? MinScore { get; set; }

    /// <summary>false/omitted: active records only; true: archived records only.</summary>
    public bool? Archived { get; set; }
}

public sealed record ContactSummaryDto(
    Guid Id, string FirstName, string? LastName, string DisplayName, string? Email, string? Phone, string? JobTitle, Guid? CompanyId,
    string? CompanyName, LifecycleStage LifecycleStage, UserRefDto? Owner, ConsentStatus ConsentStatus, IReadOnlyList<string> Tags,
    string? Source, int Score, DateTime CreatedAt, DateTime? ArchivedAt = null, Guid ConcurrencyStamp = default);

public sealed record ScoreLineDto(string Rule, ScoringCategory Category, int Points);

public sealed record ContactDto(
    Guid Id, string FirstName, string? LastName, string DisplayName, string? Email, string? Phone, string? JobTitle, Guid? CompanyId,
    string? CompanyName, LifecycleStage LifecycleStage, UserRefDto? Owner, ConsentStatus ConsentStatus, DateTime? ConsentChangedAt,
    IReadOnlyList<string> Tags, string? Source, string? BudgetRange, int Score, IReadOnlyList<ScoreLineDto> ScoreBreakdown,
    UtmDto FirstTouch, UtmDto LastTouch, IReadOnlyList<DealSummaryDto> Deals, IReadOnlyDictionary<string, int> Engagement,
    DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp, DateTime? ArchivedAt = null);

// ---------------------------------------------------------------- Pipeline & deals

public sealed record StageDto(Guid Id, string Name, int Position, int WinProbability, StageKind Kind, bool IsActive, Guid ConcurrencyStamp);

public sealed class StageRequest
{
    /// <summary>Existing stage id (null for a new stage).</summary>
    public Guid? Id { get; set; }

    [Required, StringLength(80, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 100)]
    public int WinProbability { get; set; }

    public StageKind Kind { get; set; } = StageKind.Open;
    public bool IsActive { get; set; } = true;

    /// <summary>Optional: the stamp of the stage as loaded (a stale stamp answers 409 instead of overwriting another edit).</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class UpdatePipelineRequest
{
    /// <summary>The complete ordered list of stages (exactly one Won and one Lost stage).</summary>
    [Required, MinLength(3), MaxLength(30)]
    public List<StageRequest> Stages { get; set; } = new();
}

public sealed class DealRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    public Guid? CompanyId { get; set; }
    public Guid? PrimaryContactId { get; set; }

    /// <summary>Defaults to the first open stage on create; ignored on update (use the move endpoint).</summary>
    public Guid? StageId { get; set; }

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal Value { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";

    public DateOnly? ExpectedCloseDate { get; set; }

    [MaxLength(20)]
    public List<string>? ServiceSlugs { get; set; }

    public Guid? OwnerUserId { get; set; }
    public DealSource Source { get; set; } = DealSource.Other;

    [MaxLength(100)]
    public string? SourceDetail { get; set; }

    [MaxLength(60)]
    public string? BudgetRange { get; set; }

    public UtmDto? FirstTouch { get; set; }
    public UtmDto? LastTouch { get; set; }
    public Guid? ClientAccountId { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class MoveDealRequest
{
    [Required]
    public Guid? StageId { get; set; }

    /// <summary>Required when moving to the Lost stage.</summary>
    [MaxLength(500)]
    public string? LostReason { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class DealQuery : PageQuery
{
    public Guid? StageId { get; set; }
    public DealStatus? Status { get; set; }
    public Guid? OwnerUserId { get; set; }
    public DealSource? Source { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? ContactId { get; set; }

    /// <summary>false/omitted: active records only; true: archived records only.</summary>
    public bool? Archived { get; set; }
}

public sealed record DealSummaryDto(
    Guid Id, string Title, Guid StageId, string StageName, DealStatus Status, decimal Value, string Currency, int WinProbability,
    decimal WeightedValue, DateOnly? ExpectedCloseDate, Guid? CompanyId, string? CompanyName, Guid? PrimaryContactId, string? ContactName,
    UserRefDto? Owner, DealSource Source, IReadOnlyList<string> ServiceSlugs, int? Score, DateTime StageChangedAt, DateTime CreatedAt,
    Guid ConcurrencyStamp, DateTime? ArchivedAt = null);

public sealed record DealContactDto(Guid ContactId, string DisplayName, string? Email, string? JobTitle, string? Role, bool Primary);

public sealed record DealProposalDto(Guid Id, string Number, string Title, ProposalStatus Status, int CurrentVersion, decimal Total,
    string Currency, DateTime? SentAt, DateTime? AcceptedAt);

public sealed record DealDto(
    Guid Id, string Title, Guid StageId, string StageName, StageKind StageKind, DealStatus Status, decimal Value, string Currency,
    int WinProbability, decimal WeightedValue, DateOnly? ExpectedCloseDate, Guid? CompanyId, string? CompanyName, Guid? PrimaryContactId,
    UserRefDto? Owner, DealSource Source, string? SourceDetail, string? BudgetRange, IReadOnlyList<string> ServiceSlugs, UtmDto FirstTouch,
    UtmDto LastTouch, string? LostReason, DateTime? ClosedAt, Guid? ClientAccountId, IReadOnlyList<DealContactDto> Contacts,
    IReadOnlyList<DealProposalDto> Proposals, DateTime StageChangedAt, DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp,
    DateTime? ArchivedAt = null);

public sealed record BoardColumnDto(StageDto Stage, int Count, IReadOnlyList<CurrencyValue> Totals, IReadOnlyList<DealSummaryDto> Deals);

public sealed record CurrencyValue(string Currency, decimal Amount);

public sealed record BoardDto(IReadOnlyList<BoardColumnDto> Columns);

public sealed class DealContactRequest
{
    [Required]
    public Guid? ContactId { get; set; }

    [MaxLength(80)]
    public string? Role { get; set; }
}

// ---------------------------------------------------------------- Activities

public sealed class ActivityRequest
{
    public ActivityType Type { get; set; } = ActivityType.Note;

    [Required, StringLength(200, MinimumLength = 1)]
    public string Subject { get; set; } = string.Empty;

    [MaxLength(10000)]
    public string? Body { get; set; }

    public Guid? ContactId { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? DealId { get; set; }
    public DateTime? OccursAt { get; set; }

    [Range(1, 1440)]
    public int? DurationMinutes { get; set; }

    public DateTime? DueAt { get; set; }
    public DateTime? RemindAt { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class ActivityQuery : PageQuery
{
    public Guid? ContactId { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? DealId { get; set; }
    public ActivityType? Type { get; set; }

    /// <summary>"me" or a user id.</summary>
    public string? Assignee { get; set; }

    /// <summary>open, overdue, today, completed.</summary>
    public string? Due { get; set; }
}

public sealed record ActivityDto(
    Guid Id, ActivityType Type, string Subject, string? Body, Guid? ContactId, string? ContactName, Guid? CompanyId, string? CompanyName,
    Guid? DealId, string? DealTitle, DateTime? OccursAt, int? DurationMinutes, DateTime? DueAt, DateTime? RemindAt, UserRefDto? Assignee,
    DateTime? CompletedAt, bool IsOverdue, bool IsSystem, UserRefDto? CreatedBy, DateTime CreatedAt, Guid ConcurrencyStamp);

// ---------------------------------------------------------------- Scoring

public sealed class ScoringRuleRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    public ScoringCategory Category { get; set; }

    [Required, StringLength(60, MinimumLength = 1)]
    public string Field { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? MatchValue { get; set; }

    [Range(-500, 500)]
    public int Points { get; set; }

    [Range(1, 100)]
    public int? MaxOccurrences { get; set; }

    public bool IsActive { get; set; } = true;
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record ScoringRuleDto(
    Guid Id, string Name, ScoringCategory Category, string Field, string? MatchValue, int Points, int? MaxOccurrences, bool IsActive,
    Guid ConcurrencyStamp);

public sealed record ScoringMetaDto(IReadOnlyList<string> FitFields, IReadOnlyList<string> EngagementTypes);

// ---------------------------------------------------------------- Saved views, dashboard, import

public sealed class SavedViewRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression("^(contacts|companies|deals)$")]
    public string Entity { get; set; } = "contacts";

    /// <summary>Flat map of list filters (search, lifecycleStage, ownerUserId, tag, stageId, …).</summary>
    [Required]
    public Dictionary<string, string> Filters { get; set; } = new();

    public bool Shared { get; set; }
}

public sealed record SavedViewDto(Guid Id, string Name, string Entity, IReadOnlyDictionary<string, string> Filters, bool Shared, bool Mine, DateTime CreatedAt);

public sealed record StageValueDto(Guid StageId, string StageName, int WinProbability, int Count, IReadOnlyList<CurrencyValue> Value,
    IReadOnlyList<CurrencyValue> Weighted);

public sealed record SourceCountDto(DealSource Source, int Deals, int Won);

public sealed record CrmDashboardDto(
    IReadOnlyList<StageValueDto> Pipeline, IReadOnlyList<CurrencyValue> OpenValue, IReadOnlyList<CurrencyValue> WeightedForecast,
    int OpenDeals, int WonLast90Days, int LostLast90Days, decimal WinRate, IReadOnlyList<SourceCountDto> LeadSources,
    IReadOnlyList<ActivityDto> DueToday, int OverdueTasks, int NewLeadsLast30Days);

public sealed record ImportRowResultDto(int Row, string Status, string? Email, Guid? ContactId, IReadOnlyList<string> Errors);

public sealed record ImportResultDto(bool DryRun, int TotalRows, int Created, int Updated, int Skipped, int Failed, IReadOnlyList<ImportRowResultDto> Rows);

public sealed record TimelineEntryDto(string Kind, DateTime At, string Title, string? Body, ActivityDto? Activity);

// ---------------------------------------------------------------- Archive, bulk actions, options

/// <summary>A bulk action on up to 200 contacts, companies or deals.</summary>
public sealed class CrmBulkRequest
{
    [Required, MinLength(1), MaxLength(200)]
    public List<Guid> Ids { get; set; } = new();

    /// <summary>archive, restore, assignOwner (ownerUserId; null = unassign), setLifecycle (contacts), addTag / removeTag (contacts, companies).</summary>
    [Required, RegularExpression("^(archive|restore|assignOwner|setLifecycle|addTag|removeTag)$")]
    public string Action { get; set; } = string.Empty;

    public Guid? OwnerUserId { get; set; }
    public LifecycleStage? LifecycleStage { get; set; }

    [MaxLength(40)]
    public string? Tag { get; set; }
}

public sealed record CrmBulkResultDto(int Requested, int Updated, int NotFound);

public sealed class UpdateSavedViewRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Null keeps the saved filters.</summary>
    public Dictionary<string, string>? Filters { get; set; }

    public bool Shared { get; set; }
}

/// <summary>Agency-editable CRM option lists (setting <c>crm.options</c>).</summary>
public sealed class CrmOptions
{
    /// <summary>Reasons offered when a deal is moved to Lost (free text is always allowed as "Other").</summary>
    public List<string> LostReasons { get; set; } = new()
    {
        "Budget", "Chose a competitor", "No decision / went silent", "Timing: not a priority now", "Not a fit for our services", "Price too high",
    };

    /// <summary>Budget ranges offered on contacts and deals (and matched by fit scoring rules).</summary>
    public List<string> BudgetRanges { get; set; } = new() { "<2k", "2k-5k", "5k-10k", "10k-25k", "25k+" };

    /// <summary>Industries suggested on companies (free text is still accepted).</summary>
    public List<string> Industries { get; set; } = new()
    {
        "Retail & e-commerce", "Health & fitness", "Travel & hospitality", "Beauty & skincare", "Food & beverage", "Professional services",
        "Technology & SaaS", "Real estate", "Education", "Finance",
    };
}

public sealed class CrmOptionsRequest
{
    [Required, MaxLength(50)]
    public List<string> LostReasons { get; set; } = new();

    [Required, MaxLength(50)]
    public List<string> BudgetRanges { get; set; } = new();

    [Required, MaxLength(100)]
    public List<string> Industries { get; set; } = new();

    /// <summary>The <see cref="CrmOptionsDto.Version"/> that was edited (409 when someone saved in between).</summary>
    [Required, StringLength(64)]
    public string? Version { get; set; }
}

public sealed record CrmOptionsDto(IReadOnlyList<string> LostReasons, IReadOnlyList<string> BudgetRanges, IReadOnlyList<string> Industries, string Version);
