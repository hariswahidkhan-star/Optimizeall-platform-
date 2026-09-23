using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Crm;

namespace OptimizeAll.Api.Modules.Crm;

public sealed class ProposalRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    public Guid? DealId { get; set; }
    public Guid? ClientAccountId { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? ContactId { get; set; }

    /// <summary>Defaults to the client's or the deal's currency.</summary>
    [StringLength(3, MinimumLength = 3)]
    public string? Currency { get; set; }

    [Required]
    public DateOnly? ValidUntil { get; set; }

    [MaxLength(20000)] public string? ExecutiveSummary { get; set; }
    [MaxLength(20000)] public string? Goals { get; set; }
    [MaxLength(20000)] public string? Scope { get; set; }
    [MaxLength(20000)] public string? Deliverables { get; set; }
    [MaxLength(20000)] public string? Timeline { get; set; }
    [MaxLength(20000)] public string? Terms { get; set; }

    [MaxLength(150)]
    public string? RecipientName { get; set; }

    [MaxLength(254), EmailAddress]
    public string? RecipientEmail { get; set; }

    /// <summary>Null = billing setting "invoice on acceptance".</summary>
    public bool? InvoiceOnAcceptance { get; set; }

    [Required, MinLength(1), MaxLength(Domain.Billing.Pricing.MaxLines)]
    public List<PriceLineRequest> Lines { get; set; } = new();

    /// <summary>Required on update.</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class SendProposalRequest
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }

    [MaxLength(2000)]
    public string? Message { get; set; }

    /// <summary>Send the email to the recipient (false = just publish the link to copy).</summary>
    public bool Email { get; set; } = true;
}

public sealed class WithdrawProposalRequest
{
    [Required]
    public Guid? ConcurrencyStamp { get; set; }

    [MaxLength(1000)]
    public string? Reason { get; set; }
}

public sealed class AcceptProposalRequest
{
    /// <summary>The version the client read (a newer version sent meanwhile makes the request fail with 409).</summary>
    [Range(1, 1000)]
    public int Version { get; set; }

    [Required, StringLength(150, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required, StringLength(150, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional; defaults to the proposal's recipient. Used for the client-portal invitation.</summary>
    [MaxLength(254), EmailAddress]
    public string? Email { get; set; }

    /// <summary>"I agree to the terms" — must be true.</summary>
    public bool AgreeToTerms { get; set; }
}

public sealed class DeclineProposalRequest
{
    [Range(1, 1000)]
    public int Version { get; set; }

    [Required, StringLength(1000, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class ProposalQuery : PageQuery
{
    public ProposalStatus? Status { get; set; }
    public Guid? DealId { get; set; }
    public Guid? ClientAccountId { get; set; }
}

public sealed record ProposalVersionDto(
    int VersionNumber, string Title, string Currency, DateOnly ValidUntil, string? ExecutiveSummary, string? Goals, string? Scope,
    string? Deliverables, string? Timeline, string? Terms, IReadOnlyList<PriceLineDto> Lines, TotalsDto Totals, RecurringTotalsDto Recurring,
    DateTime CreatedAt, DateTime? SentAt, bool Locked);

public sealed record ProposalVersionSummaryDto(int VersionNumber, decimal Total, decimal MonthlyRecurringValue, string Currency, DateTime CreatedAt,
    DateTime? SentAt, bool Locked);

public sealed record ProposalSummaryDto(
    Guid Id, string Number, string Title, ProposalStatus Status, Guid? DealId, string? DealTitle, Guid? ClientAccountId, string? ClientName,
    string? CompanyName, string Currency, decimal Total, decimal MonthlyRecurringValue, int CurrentVersion, DateOnly ValidUntil, DateTime? SentAt,
    int ViewCount, DateTime? AcceptedAt, DateTime CreatedAt);

public sealed record ProposalDto(
    Guid Id, string Number, string Title, ProposalStatus Status, Guid? DealId, string? DealTitle, Guid? ClientAccountId, string? ClientName,
    Guid? CompanyId, string? CompanyName, Guid? ContactId, string? ContactName, string Currency, int CurrentVersion, int? SentVersion,
    string? RecipientName, string? RecipientEmail, bool? InvoiceOnAcceptance, string? ShareUrl, DateTime? SentAt, int ViewCount,
    DateTime? FirstViewedAt, DateTime? LastViewedAt, DateTime? AcceptedAt, int? AcceptedVersion, string? SignerName, string? SignerTitle,
    string? SignerEmail, DateTime? DeclinedAt, string? DeclineReason, ProposalVersionDto Version,
    IReadOnlyList<ProposalVersionSummaryDto> Versions, IReadOnlyList<Guid> ContractIds, IReadOnlyList<Guid> InvoiceIds, DateTime CreatedAt,
    Guid ConcurrencyStamp);

public sealed record SendProposalResponse(ProposalDto Proposal, string ShareUrl, bool Emailed);

/// <summary>What the client sees on the public page (<c>/p/{token}</c>) and in the client portal.</summary>
public sealed record PublicProposalDto(
    string Number, string Title, ProposalStatus Status, string AgencyName, string? PreparedFor, string? RecipientName,
    ProposalVersionDto Version, bool CanRespond, bool Expired, bool BeingRevised, DateTime? AcceptedAt, string? SignerName, string? SignerTitle,
    DateTime? DeclinedAt);

public sealed record AcceptProposalResponse(
    PublicProposalDto Proposal, bool ClientAccountCreated, bool InvitationSent, int ContractsCreated, bool InvoiceCreated);
