using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.SocialMedia;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seo.Controllers;

public sealed record AuditRuleDto(
    string Key, string Title, string Category, SeoSeverity Severity, SeoSeverity DefaultSeverity, string WhyItMatters, string HowToFix,
    bool IsEnabled, bool IsCustomized, Guid ConcurrencyStamp);

public sealed class AuditRuleRequest
{
    [Required, MaxLength(150)] public string Title { get; set; } = string.Empty;
    [Required] public SeoSeverity? Severity { get; set; }
    [Required, MaxLength(2000)] public string WhyItMatters { get; set; } = string.Empty;
    [Required, MaxLength(2000)] public string HowToFix { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record CitationSourceDto(
    Guid Id, string Key, string Name, string Url, string Category, IReadOnlyList<string> Countries, int SortOrder, bool IsActive, bool IsCustom,
    int CitationCount, Guid ConcurrencyStamp);

public sealed class CitationSourceRequest
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string Url { get; set; } = string.Empty;
    [Required, MaxLength(60)] public string Category { get; set; } = string.Empty;
    [MaxLength(30)] public List<string> Countries { get; set; } = new();
    [Range(0, 100_000)] public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? ConcurrencyStamp { get; set; }
}

/// <summary>
/// Agency-wide SEO settings: audit rule copy, severity and enablement, and the local-SEO directory list. Everyone with
/// seo.manage can read them; changing them affects every client and needs settings.manage as well.
/// </summary>
[ApiController]
[HasPermission(Permissions.SeoManage)]
[Route("api/v1/agency/seo")]
public sealed class SeoSettingsController(AppDbContext db, IAuditLogger audit, TimeProvider clock) : ControllerBase
{
    [HttpGet("rules")]
    public async Task<IReadOnlyList<AuditRuleDto>> Rules(CancellationToken ct)
    {
        var stored = await db.Set<SeoAuditRule>().AsNoTracking().ToDictionaryAsync(r => r.Key, ct);
        return SeoAuditRules.All.Select(d => ToDto(stored.GetValueOrDefault(d.Key) ?? Default(d.Key)!))
            .OrderBy(r => r.Category).ThenBy(r => r.Severity).ThenBy(r => r.Title).ToList();
    }

    [HttpPut("rules/{key}")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<AuditRuleDto> UpdateRule(string key, AuditRuleRequest request, CancellationToken ct)
    {
        var rule = await LoadRuleAsync(key, ct);
        StampGuard.Expect(db, rule, request.ConcurrencyStamp, "rule");
        var before = new { rule.Title, rule.Severity, rule.IsEnabled };
        rule.Title = request.Title.Trim();
        rule.Severity = request.Severity!.Value;
        rule.WhyItMatters = request.WhyItMatters.Trim();
        rule.HowToFix = request.HowToFix.Trim();
        rule.IsEnabled = request.IsEnabled;
        rule.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        audit.Record("seo.rule_updated", nameof(SeoAuditRule), key, before, new { rule.Title, rule.Severity, rule.IsEnabled });
        await db.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    /// <summary>Restores the catalog copy, severity and enablement of a rule.</summary>
    [HttpPost("rules/{key}/reset")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<AuditRuleDto> ResetRule(string key, CancellationToken ct)
    {
        var rule = await LoadRuleAsync(key, ct);
        var d = SeoAuditRules.Find(key)!;
        rule.Title = d.Title;
        rule.Category = d.Category;
        rule.Severity = d.Severity;
        rule.WhyItMatters = d.WhyItMatters;
        rule.HowToFix = d.HowToFix;
        rule.IsEnabled = true;
        rule.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        audit.Record("seo.rule_reset", nameof(SeoAuditRule), key);
        await db.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    // ------------------------------------------------------------------ directories

    [HttpGet("citation-sources")]
    public async Task<IReadOnlyList<CitationSourceDto>> Sources([FromQuery] bool includeHidden = true, CancellationToken ct = default)
    {
        var q = db.Set<SeoCitationSource>().AsNoTracking();
        if (!includeHidden) q = q.Where(s => s.IsActive);
        var rows = await q.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(ct);
        var counts = await db.Set<SeoCitation>().AsNoTracking().GroupBy(c => c.SourceId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return rows.Select(s => ToDto(s, counts.GetValueOrDefault(s.Id))).ToList();
    }

    [HttpPost("citation-sources")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<CitationSourceDto> CreateSource(CitationSourceRequest request, CancellationToken ct)
    {
        var source = new SeoCitationSource { Key = "custom-" + Guid.NewGuid().ToString("N")[..12], IsCustom = true };
        await ApplyAsync(source, request, ct);
        db.Add(source);
        audit.Record("seo.directory_created", nameof(SeoCitationSource), source.Id, after: new { source.Name, source.Url });
        await db.SaveChangesAsync(ct);
        return ToDto(source, 0);
    }

    [HttpPut("citation-sources/{id:guid}")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<CitationSourceDto> UpdateSource(Guid id, CitationSourceRequest request, CancellationToken ct)
    {
        var source = await db.Set<SeoCitationSource>().FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw DomainException.NotFound("Directory");
        StampGuard.Expect(db, source, request.ConcurrencyStamp, "directory");
        var before = new { source.Name, source.Url, source.IsActive };
        await ApplyAsync(source, request, ct);
        audit.Record("seo.directory_updated", nameof(SeoCitationSource), source.Id, before, new { source.Name, source.Url, source.IsActive });
        await db.SaveChangesAsync(ct);
        return ToDto(source, await db.Set<SeoCitation>().CountAsync(c => c.SourceId == id, ct));
    }

    /// <summary>Deletes an agency-added directory nobody tracks yet; seeded or used directories can only be hidden.</summary>
    [HttpDelete("citation-sources/{id:guid}")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> DeleteSource(Guid id, CancellationToken ct)
    {
        var source = await db.Set<SeoCitationSource>().FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw DomainException.NotFound("Directory");
        if (!source.IsCustom)
            throw DomainException.Conflict("seo.directory_seeded", "Built-in directories cannot be deleted; hide it instead.");
        if (await db.Set<SeoCitation>().AnyAsync(c => c.SourceId == id, ct))
            throw DomainException.Conflict("seo.directory_in_use", "Citations are tracked on this directory; hide it instead so their history is kept.");
        db.Remove(source);
        audit.Record("seo.directory_deleted", nameof(SeoCitationSource), id, before: new { source.Name, source.Url });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task ApplyAsync(SeoCitationSource s, CitationSourceRequest r, CancellationToken ct)
    {
        var url = r.Url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
            throw new DomainException("validation.failed", "The directory URL must be an absolute http(s) URL.",
                errors: new Dictionary<string, string[]> { ["url"] = new[] { "Enter an absolute http(s) URL." } });
        var countries = r.Countries.Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length > 0).Distinct().ToList();
        if (countries.Any(c => c.Length != 2 || !c.All(char.IsAsciiLetterUpper)))
            throw new DomainException("validation.failed", "Countries are two-letter ISO codes such as GB or US.",
                errors: new Dictionary<string, string[]> { ["countries"] = new[] { "Use two-letter ISO country codes." } });
        var name = r.Name.Trim();
        if (await db.Set<SeoCitationSource>().AnyAsync(x => x.Id != s.Id && x.Name == name, ct))
            throw DomainException.Conflict("seo.directory_exists", $"A directory named {name} already exists.");
        s.Name = name;
        s.Url = url;
        s.Category = r.Category.Trim();
        s.Countries = countries;
        s.SortOrder = r.SortOrder;
        s.IsActive = r.IsActive;
    }

    private async Task<SeoAuditRule> LoadRuleAsync(string key, CancellationToken ct)
    {
        if (SeoAuditRules.Find(key) is null) throw DomainException.NotFound("Rule");
        var rule = await db.Set<SeoAuditRule>().FirstOrDefaultAsync(r => r.Key == key, ct);
        if (rule is not null) return rule;
        rule = Default(key)!;
        db.Add(rule);
        return rule;
    }

    private static SeoAuditRule? Default(string key) => SeoAuditRules.Find(key) is { } r
        ? new SeoAuditRule { Key = r.Key, Title = r.Title, Category = r.Category, Severity = r.Severity, WhyItMatters = r.WhyItMatters, HowToFix = r.HowToFix }
        : null;

    private static AuditRuleDto ToDto(SeoAuditRule r)
    {
        var d = SeoAuditRules.Find(r.Key);
        var customized = d is null || r.Title != d.Title || r.Severity != d.Severity || r.WhyItMatters != d.WhyItMatters || r.HowToFix != d.HowToFix || !r.IsEnabled;
        return new AuditRuleDto(r.Key, r.Title, r.Category, r.Severity, d?.Severity ?? r.Severity, r.WhyItMatters, r.HowToFix, r.IsEnabled, customized,
            r.ConcurrencyStamp);
    }

    private static CitationSourceDto ToDto(SeoCitationSource s, int count) =>
        new(s.Id, s.Key, s.Name, s.Url, s.Category, s.Countries, s.SortOrder, s.IsActive, s.IsCustom, count, s.ConcurrencyStamp);
}
