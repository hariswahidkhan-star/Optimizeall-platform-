using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Crm;

public enum ProposalStatus
{
    /// <summary>Being written (or revised after it was sent: a new version that has not been sent yet).</summary>
    Draft,
    Sent,
    Viewed,
    Accepted,
    Declined,
    Expired,
    Withdrawn,
}

/// <summary>
/// A proposal to a prospect or client. Content lives in immutable-once-sent <see cref="ProposalVersion"/>s: editing a sent
/// proposal creates a new version. The public link (<c>/p/{token}</c>) shows the latest sent version; accepting locks it.
/// </summary>
public class Proposal : AuditedEntity, IConcurrencyStamped
{
    public string Number { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid? DealId { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? ClientAccountId { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Draft;
    public string Currency { get; set; } = "USD";

    /// <summary>Latest version number (the one being edited, or the one sent).</summary>
    public int CurrentVersion { get; set; } = 1;

    /// <summary>The version the public link shows and the client can accept (null until first sent).</summary>
    public int? SentVersion { get; set; }

    public string? RecipientName { get; set; }
    public string? RecipientEmail { get; set; }

    /// <summary>Create a first invoice for one-time + first-period lines on acceptance (null = billing setting).</summary>
    public bool? InvoiceOnAcceptance { get; set; }

    public string? ShareTokenHash { get; set; }
    public string? ShareTokenProtected { get; set; }
    public DateTime? SentAt { get; set; }
    public Guid? SentByUserId { get; set; }
    public int ViewCount { get; set; }
    public DateTime? FirstViewedAt { get; set; }
    public DateTime? LastViewedAt { get; set; }

    public DateTime? AcceptedAt { get; set; }
    public int? AcceptedVersion { get; set; }
    public string? SignerName { get; set; }
    public string? SignerTitle { get; set; }
    public string? SignerEmail { get; set; }
    public string? SignerIpHash { get; set; }
    public string? SignerUserAgent { get; set; }
    public Guid? AcceptedByUserId { get; set; }

    public DateTime? DeclinedAt { get; set; }
    public string? DeclineReason { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<ProposalVersion> Versions { get; set; } = new();

    public static bool IsAwaitingClient(ProposalStatus status) => status is ProposalStatus.Sent or ProposalStatus.Viewed;
}

public class ProposalVersion : Entity
{
    public Guid ProposalId { get; set; }
    public int VersionNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public DateOnly ValidUntil { get; set; }

    public string? ExecutiveSummary { get; set; }
    public string? Goals { get; set; }
    public string? Scope { get; set; }
    public string? Deliverables { get; set; }
    public string? Timeline { get; set; }
    public string? Terms { get; set; }

    public decimal GrossTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public decimal OneTimeTotal { get; set; }
    public decimal MonthlyRecurringValue { get; set; }
    public decimal FirstYearValue { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime? SentAt { get; set; }

    /// <summary>Accepted versions are locked forever.</summary>
    public bool Locked { get; set; }

    public List<ProposalLine> Lines { get; set; } = new();
}

public class ProposalLine : PricedLine
{
    public Guid ProposalVersionId { get; set; }

    /// <summary>Service package slug the line was created from (null for custom lines).</summary>
    public string? PackageSlug { get; set; }
    public Recurrence Recurrence { get; set; } = Recurrence.OneTime;
}
