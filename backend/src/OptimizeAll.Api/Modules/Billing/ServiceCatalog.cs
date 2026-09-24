using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Crm;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Billing;

public sealed class ServiceCatalogItemRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(500, MinimumLength = 1)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(100), RegularExpression("^[a-z0-9][a-z0-9-]*$", ErrorMessage = "Use a lower-case slug (letters, digits, dashes).")]
    public string? ServiceSlug { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";

    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; } = 1;
    public Recurrence Recurrence { get; set; } = Recurrence.OneTime;
    public Guid? TaxRateId { get; set; }

    [Range(0, 10000)]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Required on update.</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record ServiceCatalogItemDto(
    Guid Id, string Name, string Description, string? ServiceSlug, string Currency, decimal UnitPrice, decimal Quantity, Recurrence Recurrence,
    Guid? TaxRateId, string? TaxName, int SortOrder, bool IsActive, DateTime UpdatedAt, Guid ConcurrencyStamp);

/// <summary>
/// The agency's service catalog (line-item presets offered in the proposal, contract and invoice editors). Items are
/// copied into documents, so editing or deleting one never changes an existing document.
/// </summary>
public sealed class ServiceCatalogService(AppDbContext db, IAuditLogger audit, LineBuilder lines)
{
    public async Task<IReadOnlyList<ServiceCatalogItemDto>> ListAsync(bool includeInactive, string? currency, CancellationToken ct)
    {
        var items = db.Set<ServiceCatalogItem>().AsNoTracking();
        if (!includeInactive) items = items.Where(i => i.IsActive);
        if (!string.IsNullOrWhiteSpace(currency)) items = items.Where(i => i.Currency == currency.Trim().ToUpper());
        var rows = await items.OrderBy(i => i.SortOrder).ThenBy(i => i.Name).ToListAsync(ct);
        var rateIds = rows.Where(r => r.TaxRateId.HasValue).Select(r => r.TaxRateId!.Value).Distinct().ToList();
        var rates = await db.Set<TaxRate>().AsNoTracking().Where(t => rateIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        return rows.Select(i => ToDto(i, i.TaxRateId is { } r ? rates.GetValueOrDefault(r) : null)).ToList();
    }

    public async Task<ServiceCatalogItemDto> CreateAsync(ServiceCatalogItemRequest r, CancellationToken ct)
    {
        var item = new ServiceCatalogItem();
        await ApplyAsync(item, r, ct);
        db.Set<ServiceCatalogItem>().Add(item);
        audit.Record("billing.catalog_item_created", nameof(ServiceCatalogItem), item.Id, after: new { item.Name, item.UnitPrice, item.Currency });
        await db.SaveChangesAsync(ct);
        return await GetAsync(item.Id, ct);
    }

    public async Task<ServiceCatalogItemDto> UpdateAsync(Guid id, ServiceCatalogItemRequest r, CancellationToken ct)
    {
        var item = await db.Set<ServiceCatalogItem>().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw DomainException.NotFound("CatalogItem");
        CrmService.RequireStamp(db, item, r.ConcurrencyStamp);
        var before = new { item.Name, item.UnitPrice, item.Currency, item.IsActive };
        await ApplyAsync(item, r, ct);
        audit.Record("billing.catalog_item_updated", nameof(ServiceCatalogItem), id, before, new { item.Name, item.UnitPrice, item.Currency, item.IsActive });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var item = await db.Set<ServiceCatalogItem>().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw DomainException.NotFound("CatalogItem");
        db.Remove(item);
        audit.Record("billing.catalog_item_deleted", nameof(ServiceCatalogItem), id, before: new { item.Name, item.UnitPrice, item.Currency });
        await db.SaveChangesAsync(ct);
    }

    private async Task<ServiceCatalogItemDto> GetAsync(Guid id, CancellationToken ct)
    {
        var item = await db.Set<ServiceCatalogItem>().AsNoTracking().FirstAsync(i => i.Id == id, ct);
        var taxName = item.TaxRateId is { } r ? await db.Set<TaxRate>().Where(t => t.Id == r).Select(t => t.Name).FirstOrDefaultAsync(ct) : null;
        return ToDto(item, taxName);
    }

    private async Task ApplyAsync(ServiceCatalogItem item, ServiceCatalogItemRequest r, CancellationToken ct)
    {
        var currency = LineBuilder.NormalizeCurrency(r.Currency);
        // Same checks as a real document line (price precision for the currency, quantity, active tax rate).
        await lines.BuildAsync(new[]
        {
            new PriceLineRequest
            {
                Description = r.Description, ServiceSlug = r.ServiceSlug, Quantity = r.Quantity, UnitPrice = r.UnitPrice, TaxRateId = r.TaxRateId,
                Recurrence = r.Recurrence,
            },
        }, currency, () => new ProposalLine(), ct, allowRecurrence: true);
        item.Name = r.Name.Trim();
        item.Description = r.Description.Trim();
        item.ServiceSlug = string.IsNullOrWhiteSpace(r.ServiceSlug) ? null : r.ServiceSlug.Trim().ToLowerInvariant();
        item.Currency = currency;
        item.UnitPrice = r.UnitPrice;
        item.Quantity = r.Quantity;
        item.Recurrence = r.Recurrence;
        item.TaxRateId = r.TaxRateId;
        item.SortOrder = r.SortOrder;
        item.IsActive = r.IsActive;
    }

    private static ServiceCatalogItemDto ToDto(ServiceCatalogItem i, string? taxName) => new(i.Id, i.Name, i.Description, i.ServiceSlug, i.Currency,
        i.UnitPrice, i.Quantity, i.Recurrence, i.TaxRateId, taxName, i.SortOrder, i.IsActive, i.UpdatedAt, i.ConcurrencyStamp);
}

/// <summary>Service catalog: read by everyone who builds proposals, contracts or invoices; edited under billing settings.</summary>
[DeniedWhileImpersonating(WritesOnly = true)] // prices
[ApiController]
[Route("api/v1/agency/billing/catalog")]
[HasPermission(Permissions.BillingView)]
[DeniedWhileImpersonating(WritesOnly = true)]
public sealed class ServiceCatalogController(ServiceCatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ServiceCatalogItemDto>> List([FromQuery] bool includeInactive, [FromQuery] string? currency, CancellationToken ct) =>
        catalog.ListAsync(includeInactive, currency, ct);

    [HttpPost]
    [HasPermission(Permissions.BillingSettings)]
    [ProducesResponseType(typeof(ServiceCatalogItemDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(ServiceCatalogItemRequest r, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateAsync(r, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.BillingSettings)]
    public Task<ServiceCatalogItemDto> Update(Guid id, ServiceCatalogItemRequest r, CancellationToken ct) => catalog.UpdateAsync(id, r, ct);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.BillingSettings)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await catalog.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Baseline service catalog and proposal templates (only when none exist; edit them in settings).</summary>
public sealed class SalesCatalogSeeder : ISeeder
{
    public string Profile => "Baseline";
    public int Order => 46;

    public static readonly (string Name, string Description, string Slug, decimal Price, Recurrence Recurrence)[] DefaultItems =
    {
        ("SEO retainer", "Monthly SEO retainer: technical fixes, content briefs, link outreach and reporting", "seo", 1500m, Recurrence.Monthly),
        ("Social media management", "Social media management: calendar, 12 posts per month, community management", "social", 1200m, Recurrence.Monthly),
        ("Paid ads management", "Google and Meta ads management (ad spend billed separately)", "ads", 1000m, Recurrence.Monthly),
        ("Email marketing program", "Email marketing: two campaigns and one automation per month", "email", 800m, Recurrence.Monthly),
        ("Website build", "Website design and build (up to 8 pages), CMS setup and launch", "web", 6000m, Recurrence.OneTime),
        ("Brand identity", "Logo, colour palette, typography and brand guidelines", "design", 2500m, Recurrence.OneTime),
        ("Onboarding and audit", "Onboarding, tracking audit and 90-day strategy", "strategy", 900m, Recurrence.OneTime),
    };

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        // Seeded once: an agency that deleted every catalog item or template must not get the defaults back on restart.
        var ledger = await SeedLedger.LoadAsync(db, "sales_catalog", ct);
        if (!ledger.WasSeeded("catalog") && !await db.Set<ServiceCatalogItem>().AnyAsync(ct))
        {
            var order = 10;
            foreach (var (name, description, slug, price, recurrence) in DefaultItems)
            {
                db.Set<ServiceCatalogItem>().Add(new ServiceCatalogItem
                {
                    Name = name, Description = description, ServiceSlug = slug, Currency = "USD", UnitPrice = price, Recurrence = recurrence,
                    SortOrder = order,
                });
                order += 10;
            }
        }
        ledger.Record("catalog");
        if (!ledger.WasSeeded("proposal-templates") && !await db.Set<ProposalTemplate>().AnyAsync(ct))
        {
            db.Set<ProposalTemplate>().Add(new ProposalTemplate
            {
                Name = "SEO retainer", Description = "Onboarding audit plus a monthly SEO retainer.", ProposalTitle = "SEO growth program",
                ExecutiveSummary = "A 12-month SEO program to grow qualified organic traffic and leads.",
                Goals = "- Grow organic sessions\n- Rank for priority commercial keywords\n- Increase organic leads",
                Scope = "Technical SEO, on-page optimisation, content briefs, digital PR and monthly reporting.",
                Deliverables = "- Technical audit and fix list\n- 4 content briefs per month\n- Monthly performance report",
                Timeline = "Month 1: audit and fixes. Months 2-3: content and on-page. Month 4 onwards: scale content and links.",
                Terms = "Monthly retainer billed in advance. 30 days' notice after the first 3 months.",
                Lines = new List<ProposalTemplateLine>
                {
                    new("Onboarding and audit", "strategy", 1, 900m, DiscountType.None, 0, null, Recurrence.OneTime),
                    new("Monthly SEO retainer", "seo", 1, 1500m, DiscountType.None, 0, null, Recurrence.Monthly),
                },
                SortOrder = 10,
            });
            db.Set<ProposalTemplate>().Add(new ProposalTemplate
            {
                Name = "Website build", Description = "Fixed-price website project.", ProposalTitle = "New website",
                ExecutiveSummary = "Design and build a fast, conversion-focused website.",
                Scope = "Discovery, sitemap, design of key templates, build on the CMS, content migration and launch.",
                Deliverables = "- Sitemap and wireframes\n- Designs for up to 8 pages\n- Launched website with CMS training",
                Timeline = "8 weeks from kickoff.",
                Terms = "50% on acceptance, 50% on launch.",
                Lines = new List<ProposalTemplateLine> { new("Website design and build", "web", 1, 6000m, DiscountType.None, 0, null, Recurrence.OneTime) },
                SortOrder = 20,
            });
        }
        ledger.Record("proposal-templates");
        await db.SaveChangesAsync(ct);
    }
}
