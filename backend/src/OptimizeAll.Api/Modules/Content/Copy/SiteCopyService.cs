using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Content;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Content.Copy;

// ---------- DTOs ----------

/// <summary>Public read: only the keys an editor has overridden (the web app ships the defaults).</summary>
public sealed record PublicCopyDto(IReadOnlyDictionary<string, string> Values, DateTime? UpdatedAt);

public sealed record CopyEntryDto(
    string Key, string Label, CopyType Type, string Default, string Value, bool IsCustomized, IReadOnlyList<string> Placeholders,
    int MaxLength, DateTime? UpdatedAt, Guid? ConcurrencyStamp);

public sealed record CopyGroupDto(string Id, string Label, CopyScope Scope, IReadOnlyList<CopyEntryDto> Entries);

public sealed record CopyCatalogDto(IReadOnlyList<CopyGroupDto> Groups);

public sealed class CopyChange
{
    [Required, MaxLength(120)]
    public string Key { get; set; } = string.Empty;

    /// <summary>The new text; <c>null</c> resets the key to its default.</summary>
    public string? Value { get; set; }

    /// <summary>The stamp of the override being replaced; <c>null</c> when the key currently shows its default.</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class UpdateCopyRequest
{
    [Required, MinLength(1), MaxLength(300)]
    public List<CopyChange> Changes { get; set; } = new();
}

/// <summary>Plain-text hygiene for editable copy and email templates: no markup, no control characters, \n line breaks.</summary>
public static partial class PlainText
{
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var v = value.Replace("\r\n", "\n").Replace('\r', '\n');
        // Repeat until nothing changes: a single pass turns "<<b>script>" into "<script>".
        string previous;
        do
        {
            previous = v;
            v = TagRegex().Replace(v, string.Empty);
        } while (v != previous);
        var sb = new StringBuilder(v.Length);
        foreach (var ch in v)
            if (ch == '\n' || ch == '\t' || !char.IsControl(ch)) sb.Append(ch);
        var lines = sb.ToString().Split('\n').Select(l => l.TrimEnd());
        return string.Join('\n', lines).Trim();
    }

    [GeneratedRegex(@"</?[A-Za-z!][^>]*>")]
    private static partial Regex TagRegex();
}

/// <summary>Reads and edits page copy overrides (see <see cref="SiteCopyCatalog"/>).</summary>
public sealed partial class SiteCopyService(AppDbContext db, IAuditLogger audit, ICurrentUser user, IDatabaseDialect dialect)
{
    public async Task<PublicCopyDto> GetPublicAsync(CancellationToken ct)
    {
        var rows = await db.Set<ContentCopyEntry>().AsNoTracking().ToListAsync(ct);
        var values = rows.Where(r => SiteCopyCatalog.ByKey.ContainsKey(r.Key)).ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
        return new PublicCopyDto(values, rows.Count == 0 ? null : rows.Max(r => r.UpdatedAt));
    }

    public async Task<CopyCatalogDto> GetCatalogAsync(CopyScope scope, CancellationToken ct)
    {
        var keys = SiteCopyCatalog.Groups.Where(g => g.Scope == scope).SelectMany(g => g.Entries).Select(e => e.Key).ToList();
        var rows = await db.Set<ContentCopyEntry>().AsNoTracking().Where(r => keys.Contains(r.Key)).ToDictionaryAsync(r => r.Key, ct);
        var groups = SiteCopyCatalog.Groups.Where(g => g.Scope == scope).Select(g => new CopyGroupDto(g.Id, g.Label, g.Scope,
            g.Entries.Select(e => ToDto(e, rows.GetValueOrDefault(e.Key))).ToList())).ToList();
        return new CopyCatalogDto(groups);
    }

    /// <summary>Applies several edits and resets atomically (one save, one concurrency check per key).</summary>
    public async Task<CopyCatalogDto> UpdateAsync(CopyScope scope, UpdateCopyRequest request, CancellationToken ct)
    {
        var errors = new FieldErrors();
        var keys = request.Changes.Select(c => c.Key).ToList();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count) errors.Add("changes", "Each key can appear only once.");
        var rows = await db.Set<ContentCopyEntry>().Where(r => keys.Contains(r.Key)).ToDictionaryAsync(r => r.Key, ct);

        var planned = new List<(CopyEntryDefinition Def, string? Value, CopyChange Change)>();
        for (var i = 0; i < request.Changes.Count; i++)
        {
            var change = request.Changes[i];
            if (!SiteCopyCatalog.ByKey.TryGetValue(change.Key, out var def) || def.Scope != scope)
            {
                errors.Add($"changes[{i}].key", "Unknown copy key.");
                continue;
            }
            var value = change.Value is null ? null : Validate(def, change.Value, change.Key, errors);
            planned.Add((def, value, change));
        }
        errors.ThrowIfAny("content.copy_invalid", "Some texts are invalid.");

        foreach (var (def, value, change) in planned)
        {
            rows.TryGetValue(def.Key, out var row);
            if (row is not null)
                ConcurrencyGuard.Apply(db, row, change.ConcurrencyStamp ?? Guid.Empty);
            else if (change.ConcurrencyStamp is not null)
                throw DomainException.Conflict("concurrency.conflict", "This text was changed by someone else. Reload and try again.");

            // Saving the default text (or null) removes the override, so later changes to the shipped default apply.
            if (value is null || value == def.Default)
            {
                if (row is null) continue;
                audit.Record("content.copy_reset", nameof(ContentCopyEntry), def.Key, new { row.Value });
                db.Set<ContentCopyEntry>().Remove(row);
                continue;
            }
            if (row is null)
            {
                db.Set<ContentCopyEntry>().Add(new ContentCopyEntry { Key = def.Key, Value = value, UpdatedByUserId = user.IdOrNull });
                audit.Record("content.copy_updated", nameof(ContentCopyEntry), def.Key, new { Value = def.Default }, new { Value = value });
            }
            else if (row.Value != value)
            {
                audit.Record("content.copy_updated", nameof(ContentCopyEntry), def.Key, new { row.Value }, new { Value = value });
                row.Value = value;
                row.UpdatedByUserId = user.IdOrNull;
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("concurrency.conflict", "This text was changed by someone else. Reload and try again.");
        }
        return await GetCatalogAsync(scope, ct);
    }

    /// <summary>Normalizes and checks one value against its type; returns the cleaned text.</summary>
    public static string Validate(CopyEntryDefinition def, string raw, string field, FieldErrors errors)
    {
        var value = PlainText.Clean(raw);
        if (value.Length == 0)
        {
            errors.Add(field, "Enter the text, or reset it to the default.");
            return value;
        }
        if (value.Length > SiteCopyCatalog.MaxLength(def.Type))
            errors.Add(field, $"Keep this under {SiteCopyCatalog.MaxLength(def.Type):N0} characters.");

        switch (def.Type)
        {
            case CopyType.Text:
                if (value.Contains('\n')) errors.Add(field, "Use a single line.");
                break;
            case CopyType.List:
            case CopyType.Pairs:
                var lines = value.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                if (lines.Count > 30) errors.Add(field, "Add at most 30 items.");
                if (lines.Any(l => l.Length > 1000)) errors.Add(field, "Each item can be at most 1,000 characters.");
                if (def.Type == CopyType.Pairs && lines.Any(l => !IsPair(l)))
                    errors.Add(field, "Write each item as 'Title | Text' on its own line.");
                value = string.Join('\n', lines);
                break;
        }

        var unknown = PlaceholderRegex().Matches(value).Select(m => m.Groups[1].Value)
            .Where(p => !def.Placeholders.Contains(p, StringComparer.Ordinal)).Distinct().ToList();
        if (unknown.Count > 0)
            errors.Add(field, def.Placeholders.Count == 0
                ? $"This text has no placeholders; remove {{{unknown[0]}}}."
                : $"Unknown placeholder {{{unknown[0]}}}. Available: {string.Join(", ", def.Placeholders.Select(p => "{" + p + "}"))}.");
        return value;
    }

    private static bool IsPair(string line)
    {
        var i = line.IndexOf('|');
        return i > 0 && line[..i].Trim().Length > 0 && line[(i + 1)..].Trim().Length > 0;
    }

    private static CopyEntryDto ToDto(CopyEntryDefinition e, ContentCopyEntry? row) => new(
        e.Key, e.Label, e.Type, e.Default, row?.Value ?? e.Default, row is not null, e.Placeholders, SiteCopyCatalog.MaxLength(e.Type),
        row?.UpdatedAt, row?.ConcurrencyStamp);

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9]*)\}")]
    private static partial Regex PlaceholderRegex();
}
