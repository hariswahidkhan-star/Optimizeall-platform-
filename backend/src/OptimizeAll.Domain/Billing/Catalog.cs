using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Billing;

/// <summary>
/// A service catalog entry (line-item preset): what the agency sells and its list price. The proposal, contract and
/// invoice editors offer active items to add as lines; the values are copied, so editing an item never changes existing
/// documents.
/// </summary>
public class ServiceCatalogItem : AuditedEntity, IConcurrencyStamped
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Line description copied onto documents.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Service slug for revenue-by-service reporting (e.g. "seo", "social").</summary>
    public string? ServiceSlug { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; } = 1;
    public Recurrence Recurrence { get; set; } = Recurrence.OneTime;
    public Guid? TaxRateId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
