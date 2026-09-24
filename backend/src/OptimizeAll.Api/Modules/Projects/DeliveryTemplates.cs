using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Projects;

public sealed class BriefTemplateRequest
{
    /// <summary>Stable key (lower-case slug). Required on create; ignored on update.</summary>
    [MaxLength(64)]
    public string? Key { get; set; }

    [Required, StringLength(32, MinimumLength = 2)]
    public string ServiceLine { get; set; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required, MinLength(1), MaxLength(40)]
    public List<BriefField> Fields { get; set; } = new();

    public bool IsActive { get; set; } = true;

    /// <summary>Required on update.</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class ReportTemplateRequest
{
    /// <summary>Stable key (lower-case slug). Required on create; ignored on update.</summary>
    [MaxLength(64)]
    public string? Key { get; set; }

    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required, MinLength(1), MaxLength(30)]
    public List<ReportTemplateSection> Sections { get; set; } = new();

    public bool IsActive { get; set; } = true;

    /// <summary>Required on update.</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class EditCommentRequest
{
    [Required, MinLength(1), MaxLength(10000)]
    public string Body { get; set; } = string.Empty;
}

public sealed class RenameThreadRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Subject { get; set; } = string.Empty;
}

/// <summary>
/// Agency-editable brief and report templates, and deletion of project templates. Built-in templates (seeded by
/// <see cref="DeliveryBaselineSeeder"/>) can be edited and deactivated but not deleted, since the baseline would re-create them.
/// </summary>
public sealed partial class DeliveryTemplateService(
    AppDbContext db, IDatabaseDialect dialect, IAuditLogger audit, IEnumerable<IClientReportSection> providers)
{
    public static readonly string[] SectionKinds = { "summary", "kpis", "channel", "wins", "plan", "custom" };

    private static readonly HashSet<string> BuiltInBriefKeys = DeliveryBaselineSeeder.BriefTemplates().Select(t => t.Key).ToHashSet();
    private static readonly HashSet<string> BuiltInReportKeys = DeliveryBaselineSeeder.ReportTemplates().Select(t => t.Key).ToHashSet();
    private static readonly HashSet<string> BuiltInProjectKeys = DeliveryBaselineSeeder.ProjectTemplates().Select(t => t.Key).ToHashSet();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,63}$")]
    private static partial Regex KeyPattern();

    public static bool IsBuiltInProject(string key) => BuiltInProjectKeys.Contains(key);

    public static BriefTemplateDto ToDto(BriefTemplate t) =>
        new(t.Id, t.Key, t.ServiceLine, t.Name, t.Description, t.Fields, t.IsActive, BuiltInBriefKeys.Contains(t.Key), t.ConcurrencyStamp);

    public static ReportTemplateDto ToDto(ReportTemplate t) =>
        new(t.Id, t.Key, t.Name, t.Description, t.Sections, t.IsActive, BuiltInReportKeys.Contains(t.Key), t.ConcurrencyStamp);

    // ------------------------------------------------------------------ brief templates

    public async Task<BriefTemplateDto> SaveBriefTemplateAsync(Guid? id, BriefTemplateRequest r, CancellationToken ct)
    {
        var fields = ValidateFields(r.Fields);
        var serviceLine = r.ServiceLine.Trim().ToLowerInvariant();
        BriefTemplate t;
        if (id is { } existing)
        {
            t = await db.Set<BriefTemplate>().FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw DomainException.NotFound("BriefTemplate");
            RequireStamp(t, r.ConcurrencyStamp);
        }
        else
        {
            t = new BriefTemplate { Key = ValidKey(r.Key) };
            db.Set<BriefTemplate>().Add(t);
        }
        var before = id is null ? null : new { t.Name, t.IsActive, Fields = t.Fields.Count };
        t.ServiceLine = serviceLine;
        t.Name = r.Name.Trim();
        t.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        t.Fields = fields;
        t.IsActive = r.IsActive;
        audit.Record(id is null ? "template.brief_created" : "template.brief_updated", nameof(BriefTemplate), t.Id, before,
            new { t.Key, t.Name, t.IsActive, Fields = t.Fields.Count });
        await SaveAsync(ct);
        return ToDto(t);
    }

    public async Task DeleteBriefTemplateAsync(Guid id, CancellationToken ct)
    {
        var t = await db.Set<BriefTemplate>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("BriefTemplate");
        if (BuiltInBriefKeys.Contains(t.Key))
            throw DomainException.Conflict("template.built_in", "Built-in templates can't be deleted. Deactivate it instead.");
        if (await db.Set<Brief>().AnyAsync(b => b.TemplateKey == t.Key, ct))
            throw DomainException.Conflict("template.in_use", "Briefs were submitted with this template. Deactivate it instead so they keep their form.");
        db.Remove(t);
        audit.Record("template.brief_deleted", nameof(BriefTemplate), id, before: new { t.Key, t.Name });
        await db.SaveChangesAsync(ct);
    }

    private static List<BriefField> ValidateFields(List<BriefField> fields)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<BriefField>();
        for (var i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            var field = $"fields[{i}]";
            var key = (f.Key ?? string.Empty).Trim();
            if (!Regex.IsMatch(key, "^[a-zA-Z][a-zA-Z0-9_]{0,39}$") || !keys.Add(key))
                throw DeliveryRules.Invalid("template.invalid_field", field, "Each field needs a unique key (letters, digits, underscores).");
            if (string.IsNullOrWhiteSpace(f.Label) || f.Label.Trim().Length > 200)
                throw DeliveryRules.Invalid("template.invalid_field", field, "Each field needs a label (up to 200 characters).");
            if (!Enum.IsDefined(f.Type)) throw DeliveryRules.Invalid("template.invalid_field", field, "Choose a field type.");
            var options = DeliveryRules.CleanList(f.Options, 50, 200, field);
            if (f.Type == BriefFieldType.Select && options.Count == 0)
                throw DeliveryRules.Invalid("template.invalid_field", field, "A choice field needs at least one option.");
            result.Add(new BriefField(key, f.Label.Trim(), f.Type, f.Required, string.IsNullOrWhiteSpace(f.Help) ? null : f.Help.Trim(), options));
        }
        return result;
    }

    // ------------------------------------------------------------------ report templates

    public async Task<ReportTemplateDto> SaveReportTemplateAsync(Guid? id, ReportTemplateRequest r, CancellationToken ct)
    {
        ReportTemplate t;
        List<ReportTemplateSection> sections;
        if (id is { } existing)
        {
            t = await db.Set<ReportTemplate>().FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw DomainException.NotFound("ReportTemplate");
            RequireStamp(t, r.ConcurrencyStamp);
            // Data sources the template already referenced stay valid (built-in templates name sources that light up once connected).
            sections = ValidateSections(r.Sections, t.Sections.Where(s => s.ProviderKey != null).Select(s => s.ProviderKey!).ToHashSet());
            if (!r.IsActive && t.Key == ReportService.DefaultTemplateKey)
                throw DomainException.Conflict("template.default_report",
                    "The monthly performance template is used for the automatic monthly drafts, so it stays active.");
        }
        else
        {
            sections = ValidateSections(r.Sections, new HashSet<string>());
            t = new ReportTemplate { Key = ValidKey(r.Key) };
            db.Set<ReportTemplate>().Add(t);
        }
        var before = id is null ? null : new { t.Name, t.IsActive, Sections = t.Sections.Count };
        t.Name = r.Name.Trim();
        t.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        t.Sections = sections;
        t.IsActive = r.IsActive;
        audit.Record(id is null ? "template.report_created" : "template.report_updated", nameof(ReportTemplate), t.Id, before,
            new { t.Key, t.Name, t.IsActive, Sections = t.Sections.Count });
        await SaveAsync(ct);
        return ToDto(t);
    }

    /// <summary>Reports copy their sections, so deleting a custom template never changes existing reports.</summary>
    public async Task DeleteReportTemplateAsync(Guid id, CancellationToken ct)
    {
        var t = await db.Set<ReportTemplate>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("ReportTemplate");
        if (BuiltInReportKeys.Contains(t.Key))
            throw DomainException.Conflict("template.built_in", "Built-in templates can't be deleted. Deactivate it instead.");
        db.Remove(t);
        audit.Record("template.report_deleted", nameof(ReportTemplate), id, before: new { t.Key, t.Name });
        await db.SaveChangesAsync(ct);
    }

    private List<ReportTemplateSection> ValidateSections(List<ReportTemplateSection> sections, HashSet<string> alreadyUsed)
    {
        var providerKeys = providers.Select(p => p.Key).Concat(alreadyUsed).ToHashSet(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ReportTemplateSection>();
        for (var i = 0; i < sections.Count; i++)
        {
            var s = sections[i];
            var field = $"sections[{i}]";
            var key = (s.Key ?? string.Empty).Trim();
            if (!Regex.IsMatch(key, "^[a-z0-9][a-z0-9-]{0,39}$") || !keys.Add(key))
                throw DeliveryRules.Invalid("template.invalid_section", field, "Each section needs a unique key (lower-case letters, digits, dashes).");
            if (!SectionKinds.Contains(s.Kind))
                throw DeliveryRules.Invalid("template.invalid_section", field, $"The section kind must be one of {string.Join(", ", SectionKinds)}.");
            if (string.IsNullOrWhiteSpace(s.Title) || s.Title.Trim().Length > 200)
                throw DeliveryRules.Invalid("template.invalid_section", field, "Each section needs a title (up to 200 characters).");
            var provider = string.IsNullOrWhiteSpace(s.ProviderKey) ? null : s.ProviderKey.Trim();
            if (provider is not null && !providerKeys.Contains(provider))
                throw DeliveryRules.Invalid("template.invalid_section", field, "Choose a data source that is available.");
            if (s.Prompt is { Length: > 2000 })
                throw DeliveryRules.Invalid("template.invalid_section", field, "The guidance text can be at most 2,000 characters.");
            result.Add(new ReportTemplateSection(key, s.Kind, s.Title.Trim(), provider, string.IsNullOrWhiteSpace(s.Prompt) ? null : s.Prompt.Trim()));
        }
        return result;
    }

    // ------------------------------------------------------------------ project templates

    public async Task DeleteProjectTemplateAsync(Guid id, CancellationToken ct)
    {
        var t = await db.Set<ProjectTemplate>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("ProjectTemplate");
        if (BuiltInProjectKeys.Contains(t.Key))
            throw DomainException.Conflict("template.built_in", "Built-in templates can't be deleted. Deactivate it instead.");
        db.Remove(t);
        audit.Record("template.project_deleted", nameof(ProjectTemplate), id, before: new { t.Key, t.Name });
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ helpers

    private static string ValidKey(string? key)
    {
        var k = (key ?? string.Empty).Trim().ToLowerInvariant();
        if (!KeyPattern().IsMatch(k))
            throw DeliveryRules.Invalid("template.invalid_key", "key", "Use 2–64 lower-case letters, digits or dashes.");
        return k;
    }

    private void RequireStamp(IConcurrencyStamped entity, Guid? stamp)
    {
        if (stamp is null) throw DeliveryRules.Invalid("concurrency.stamp_required", "concurrencyStamp", "Reload the template and try again.");
        DeliveryRules.EnsureStamp(entity, stamp, db);
        db.Entry(entity).Property(nameof(IConcurrencyStamped.ConcurrencyStamp)).IsModified = true;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw DeliveryRules.Invalid("template.key_taken", "key", "Another template uses this key.");
        }
    }
}
