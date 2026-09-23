using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Agency;

public enum ClientAccountStatus
{
    Onboarding,
    Active,
    Paused,
    Churned,
}

/// <summary>
/// A client organization of the agency. Every client-owned record (projects, invoices, email lists, social and ad
/// accounts, SEO sites, reports) references a ClientAccount and is scoped through <c>IClientScope</c>.
/// </summary>
public class ClientAccount : AuditedEntity, IConcurrencyStamped
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Industry { get; set; }
    public string? Website { get; set; }
    public string CountryCode { get; set; } = "US";
    public string TimeZone { get; set; } = "UTC";

    /// <summary>ISO currency used for this client's proposals and invoices.</summary>
    public string Currency { get; set; } = "USD";
    public ClientAccountStatus Status { get; set; } = ClientAccountStatus.Onboarding;

    /// <summary>Why the client was paused or churned (required for those states).</summary>
    public string? StatusReason { get; set; }
    public DateTime? StatusChangedAt { get; set; }
    public Guid? AccountManagerUserId { get; set; }

    /// <summary>Client logo: a <c>DeliveryFile</c> (private, served to staff and the client's own users only).</summary>
    public Guid? LogoFileId { get; set; }

    /// <summary>One-line description of the business (shown on the client card and to the delivery team).</summary>
    public string? Summary { get; set; }

    public string? BillingContactName { get; set; }
    public string? BillingEmail { get; set; }
    public string? BillingAddress { get; set; }
    public string? TaxId { get; set; }
    public string? Notes { get; set; }

    /// <summary>CRM company the client was converted from, if any.</summary>
    public Guid? CrmCompanyId { get; set; }

    /// <summary>Business days the client has to give feedback on a deliverable sent for review (SLA; reminders before and after).</summary>
    public int ApprovalSlaDays { get; set; } = 3;

    /// <summary>
    /// When set, a deliverable waiting for client review longer than this many days is approved automatically (audited).
    /// Off (null) by default; only agreed with the client in writing.
    /// </summary>
    public int? AutoApproveAfterDays { get; set; }

    /// <summary>Last time an invoice of this client was fully paid (from the Billing module's InvoicePaid event).</summary>
    public DateTime? LastInvoicePaidAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<ClientMember> Members { get; set; } = new();
}

/// <summary>What a client user may do inside their organization's portal.</summary>
public enum ClientMemberRole
{
    /// <summary>Read-only access to projects, reports and calendars.</summary>
    Viewer,
    /// <summary>Can comment, submit briefs and approve or request changes on deliverables.</summary>
    Approver,
    /// <summary>Sees invoices and payments; accepts proposals.</summary>
    Billing,
    /// <summary>Everything, including inviting and removing client users.</summary>
    Owner,
}

/// <summary>Links a user (with the Client role) to a client organization.</summary>
public class ClientMember
{
    public Guid ClientAccountId { get; set; }
    public Guid UserId { get; set; }
    public ClientMemberRole Role { get; set; } = ClientMemberRole.Viewer;
    public DateTime AddedAt { get; set; }
    public Guid? AddedByUserId { get; set; }
}
