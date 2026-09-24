using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Crm;

/// <summary>
/// Helpers for agency-editable option lists stored as a JSON <c>SystemSetting</c>. The <see cref="Version"/> of the stored
/// value acts as a concurrency stamp: saving with a stale version answers 409 instead of overwriting another edit.
/// </summary>
public static class OptionLists
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Version<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json)))[..16].ToLowerInvariant();

    public static void EnsureVersion(string current, string? given)
    {
        if (!string.Equals(current, given, StringComparison.OrdinalIgnoreCase))
            throw DomainException.Conflict("concurrency.conflict", "These settings were changed by someone else. Reload and try again.");
    }

    /// <summary>Trims, drops blanks and case-insensitive duplicates; records a field error for values that are too long.</summary>
    public static List<string> Clean(IEnumerable<string>? values, string field, int maxLength, int maxCount, IDictionary<string, string[]> errors)
    {
        var result = new List<string>();
        foreach (var raw in values ?? Array.Empty<string>())
        {
            var value = (raw ?? string.Empty).Trim();
            if (value.Length == 0 || result.Contains(value, StringComparer.OrdinalIgnoreCase)) continue;
            if (value.Length > maxLength)
            {
                errors[field] = new[] { $"Each entry can be at most {maxLength} characters." };
                continue;
            }
            result.Add(value);
        }
        if (result.Count > maxCount) errors[field] = new[] { $"At most {maxCount} entries." };
        return result;
    }
}

/// <summary>CRM option lists (lost reasons, budget ranges, industries) edited under CRM settings.</summary>
public sealed class CrmOptionsService(AppDbContext db, ISettingsService settings, IAuditLogger audit, ICurrentUser currentUser)
{
    public const string Key = "crm.options";

    public Task<CrmOptions> GetRawAsync(CancellationToken ct) => settings.GetAsync(Key, new CrmOptions(), ct);

    public async Task<CrmOptionsDto> GetAsync(CancellationToken ct) => ToDto(await GetRawAsync(ct));

    public async Task<CrmOptionsDto> SaveAsync(CrmOptionsRequest r, CancellationToken ct)
    {
        var current = await GetRawAsync(ct);
        OptionLists.EnsureVersion(OptionLists.Version(current), r.Version);
        var errors = new Dictionary<string, string[]>();
        var next = new CrmOptions
        {
            LostReasons = OptionLists.Clean(r.LostReasons, "lostReasons", 120, 50, errors),
            BudgetRanges = OptionLists.Clean(r.BudgetRanges, "budgetRanges", 60, 50, errors),
            Industries = OptionLists.Clean(r.Industries, "industries", 100, 100, errors),
        };
        if (next.LostReasons.Count == 0) errors["lostReasons"] = new[] { "Keep at least one lost reason." };
        if (errors.Count > 0) throw new DomainException("crm.invalid_options", "Some options need attention.", errors: errors);
        await settings.SetAsync(Key, next, currentUser.IdOrNull, "CRM option lists (lost reasons, budget ranges, industries).", ct);
        audit.Record("crm.options_updated", "SystemSetting", Key, current, next);
        await db.SaveChangesAsync(ct);
        return ToDto(next);
    }

    private static CrmOptionsDto ToDto(CrmOptions o) => new(o.LostReasons, o.BudgetRanges, o.Industries, OptionLists.Version(o));
}

// ---------------------------------------------------------------- Proposal templates

public sealed class ProposalTemplateRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)] public string? Description { get; set; }
    [MaxLength(200)] public string? ProposalTitle { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";

    [Range(1, 365)]
    public int ValidForDays { get; set; } = 30;

    [MaxLength(20000)] public string? ExecutiveSummary { get; set; }
    [MaxLength(20000)] public string? Goals { get; set; }
    [MaxLength(20000)] public string? Scope { get; set; }
    [MaxLength(20000)] public string? Deliverables { get; set; }
    [MaxLength(20000)] public string? Timeline { get; set; }
    [MaxLength(20000)] public string? Terms { get; set; }

    [MaxLength(Pricing.MaxLines)]
    public List<PriceLineRequest> Lines { get; set; } = new();

    [Range(0, 10000)]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Required on update.</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record ProposalTemplateDto(
    Guid Id, string Name, string? Description, string? ProposalTitle, string Currency, int ValidForDays, string? ExecutiveSummary,
    string? Goals, string? Scope, string? Deliverables, string? Timeline, string? Terms, IReadOnlyList<ProposalTemplateLine> Lines,
    int SortOrder, bool IsActive, DateTime UpdatedAt, Guid ConcurrencyStamp);

/// <summary>Reusable proposal starting points (sections + price lines). Using a template copies it; editing it never changes proposals.</summary>
public sealed class ProposalTemplateService(AppDbContext db, IDatabaseDialect dialect, IAuditLogger audit, LineBuilder lines)
{
    public async Task<IReadOnlyList<ProposalTemplateDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var query = db.Set<ProposalTemplate>().AsNoTracking();
        if (!includeInactive) query = query.Where(t => t.IsActive);
        return (await query.OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<ProposalTemplateDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.Set<ProposalTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw DomainException.NotFound("ProposalTemplate"));

    public async Task<ProposalTemplateDto> CreateAsync(ProposalTemplateRequest r, CancellationToken ct)
    {
        var template = new ProposalTemplate();
        await ApplyAsync(template, r, ct);
        db.Set<ProposalTemplate>().Add(template);
        audit.Record("crm.proposal_template_created", nameof(ProposalTemplate), template.Id, after: new { template.Name, Lines = template.Lines.Count });
        await SaveAsync(ct);
        return ToDto(template);
    }

    public async Task<ProposalTemplateDto> UpdateAsync(Guid id, ProposalTemplateRequest r, CancellationToken ct)
    {
        var template = await db.Set<ProposalTemplate>().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw DomainException.NotFound("ProposalTemplate");
        CrmService.RequireStamp(db, template, r.ConcurrencyStamp);
        var before = new { template.Name, template.IsActive, Lines = template.Lines.Count };
        await ApplyAsync(template, r, ct);
        audit.Record("crm.proposal_template_updated", nameof(ProposalTemplate), id, before, new { template.Name, template.IsActive, Lines = template.Lines.Count });
        await SaveAsync(ct);
        return ToDto(template);
    }

    /// <summary>Templates are copied into proposals, so deleting one never affects existing proposals.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var template = await db.Set<ProposalTemplate>().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw DomainException.NotFound("ProposalTemplate");
        db.Remove(template);
        audit.Record("crm.proposal_template_deleted", nameof(ProposalTemplate), id, before: new { template.Name });
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(ProposalTemplate t, ProposalTemplateRequest r, CancellationToken ct)
    {
        var currency = LineBuilder.NormalizeCurrency(r.Currency);
        if (r.Lines.Count > 0)
            // Validates descriptions, amounts (for the currency's minor units) and tax rates exactly like a proposal would.
            await lines.BuildAsync(r.Lines, currency, () => new ProposalLine(), ct, allowRecurrence: true);
        t.Name = r.Name.Trim();
        t.Description = CrmService.Trim(r.Description, 1000);
        t.ProposalTitle = CrmService.Trim(r.ProposalTitle, 200);
        t.Currency = currency;
        t.ValidForDays = r.ValidForDays;
        t.ExecutiveSummary = Section(r.ExecutiveSummary);
        t.Goals = Section(r.Goals);
        t.Scope = Section(r.Scope);
        t.Deliverables = Section(r.Deliverables);
        t.Timeline = Section(r.Timeline);
        t.Terms = Section(r.Terms);
        t.Lines = r.Lines.Select(l => new ProposalTemplateLine(l.Description.Trim(),
            string.IsNullOrWhiteSpace(l.ServiceSlug) ? null : l.ServiceSlug.Trim().ToLowerInvariant(), l.Quantity, l.UnitPrice, l.DiscountType,
            l.DiscountType == DiscountType.None ? 0 : l.DiscountValue, l.TaxRateId, l.Recurrence)).ToList();
        t.SortOrder = r.SortOrder;
        t.IsActive = r.IsActive;
    }

    private static string? Section(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("crm.duplicate_template", "Another proposal template already has this name.");
        }
    }

    private static ProposalTemplateDto ToDto(ProposalTemplate t) => new(t.Id, t.Name, t.Description, t.ProposalTitle, t.Currency, t.ValidForDays,
        t.ExecutiveSummary, t.Goals, t.Scope, t.Deliverables, t.Timeline, t.Terms, t.Lines, t.SortOrder, t.IsActive, t.UpdatedAt, t.ConcurrencyStamp);
}
