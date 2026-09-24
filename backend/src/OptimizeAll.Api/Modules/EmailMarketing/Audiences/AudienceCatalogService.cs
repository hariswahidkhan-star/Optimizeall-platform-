using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Audiences;

/// <summary>A tag or custom field key used in a workspace, with how many contacts carry it and how many segments/journeys mention it.</summary>
public sealed record AudienceKeyDto(string Key, int Contacts, int ReferencedBy);

public sealed class RenameKeyRequest
{
    public Guid? ClientAccountId { get; set; }
    [Required, MaxLength(100)] public string From { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string To { get; set; } = string.Empty;
}

public sealed record RenameResult(string From, string To, int Changed, int Merged, int ReferencedBy);

/// <summary>
/// Workspace-level management of contact tags and custom fields: list with usage, rename (merging into an existing key)
/// and delete. Segments and journeys referencing a key are counted so the UI can warn before a change.
/// </summary>
public sealed class AudienceCatalogService(AppDbContext db, EmailAccess access, IAuditLogger audit)
{
    public async Task<IReadOnlyList<AudienceKeyDto>> TagsAsync(Guid? clientId, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(clientId, ct);
        var key = Workspace.Key(clientId);
        var counts = await (from t in db.Set<SubscriberTag>()
                            join s in db.Set<Subscriber>() on t.SubscriberId equals s.Id
                            where s.ScopeKey == key
                            group t by t.Tag into g
                            select new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var references = await ReferenceTextsAsync(key, ct);
        return counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => new AudienceKeyDto(c.Key, c.Count, CountReferences(references, c.Key))).ToList();
    }

    public async Task<IReadOnlyList<AudienceKeyDto>> FieldsAsync(Guid? clientId, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(clientId, ct);
        var key = Workspace.Key(clientId);
        var counts = await (from f in db.Set<SubscriberField>()
                            join s in db.Set<Subscriber>() on f.SubscriberId equals s.Id
                            where s.ScopeKey == key
                            group f by f.Key into g
                            select new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var references = await ReferenceTextsAsync(key, ct);
        return counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => new AudienceKeyDto(c.Key, c.Count, CountReferences(references, c.Key))).ToList();
    }

    public async Task<RenameResult> RenameTagAsync(RenameKeyRequest r, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(r.ClientAccountId, ct);
        var oldKey = ContactRules.NormalizeTag(r.From) ?? throw Invalid("from", "The tag is invalid.");
        var newKey = ContactRules.NormalizeTag(r.To) ?? throw Invalid("to", "Tags are 1–50 letters, digits, spaces, dashes or underscores.");
        if (oldKey == newKey) throw Invalid("to", "Choose a different name.");
        var scope = Workspace.Key(r.ClientAccountId);
        var rows = await (from t in db.Set<SubscriberTag>()
                          join s in db.Set<Subscriber>() on t.SubscriberId equals s.Id
                          where s.ScopeKey == scope && (t.Tag == oldKey || t.Tag == newKey)
                          select t).ToListAsync(ct);
        if (rows.All(t => t.Tag != oldKey)) throw DomainException.NotFound("Tag");
        var hasTarget = rows.Where(t => t.Tag == newKey).Select(t => t.SubscriberId).ToHashSet();
        int changed = 0, merged = 0;
        foreach (var t in rows.Where(t => t.Tag == oldKey))
        {
            db.Remove(t);
            if (hasTarget.Contains(t.SubscriberId)) { merged++; continue; }
            db.Add(new SubscriberTag { SubscriberId = t.SubscriberId, Tag = newKey, AddedAt = t.AddedAt });
            changed++;
        }
        audit.Record("email.tag.renamed", "SubscriberTag", scope, new { Tag = oldKey }, new { Tag = newKey, changed, merged });
        await db.SaveChangesAsync(ct);
        return new RenameResult(oldKey, newKey, changed, merged, await ReferencesAsync(scope, oldKey, ct));
    }

    public async Task<int> DeleteTagAsync(Guid? clientId, string tag, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(clientId, ct);
        var normalized = ContactRules.NormalizeTag(tag) ?? throw DomainException.NotFound("Tag");
        var scope = Workspace.Key(clientId);
        var ids = db.Set<Subscriber>().Where(s => s.ScopeKey == scope).Select(s => s.Id);
        var removed = await db.Set<SubscriberTag>().Where(t => t.Tag == normalized && ids.Contains(t.SubscriberId)).ExecuteDeleteAsync(ct);
        if (removed == 0) throw DomainException.NotFound("Tag");
        audit.Record("email.tag.deleted", "SubscriberTag", scope, new { Tag = normalized, removed });
        await db.SaveChangesAsync(ct);
        return removed;
    }

    public async Task<RenameResult> RenameFieldAsync(RenameKeyRequest r, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(r.ClientAccountId, ct);
        var oldKey = r.From.Trim();
        var newKey = r.To.Trim();
        if (!ContactRules.IsValidFieldKey(newKey) || ContactRules.BuiltInFields.Contains(newKey))
            throw Invalid("to", "Field keys are lower-case letters, digits and underscores and cannot reuse a built-in field.");
        if (oldKey == newKey) throw Invalid("to", "Choose a different name.");
        var scope = Workspace.Key(r.ClientAccountId);
        var rows = await (from f in db.Set<SubscriberField>()
                          join s in db.Set<Subscriber>() on f.SubscriberId equals s.Id
                          where s.ScopeKey == scope && (f.Key == oldKey || f.Key == newKey)
                          select f).ToListAsync(ct);
        if (rows.All(f => f.Key != oldKey)) throw DomainException.NotFound("Field");
        var hasTarget = rows.Where(f => f.Key == newKey).Select(f => f.SubscriberId).ToHashSet();
        int changed = 0, merged = 0;
        foreach (var f in rows.Where(f => f.Key == oldKey))
        {
            db.Remove(f);
            // Contacts that already hold a value under the new key keep that value.
            if (hasTarget.Contains(f.SubscriberId)) { merged++; continue; }
            db.Add(new SubscriberField { SubscriberId = f.SubscriberId, Key = newKey, Value = f.Value });
            changed++;
        }
        audit.Record("email.field.renamed", "SubscriberField", scope, new { Key = oldKey }, new { Key = newKey, changed, merged });
        await db.SaveChangesAsync(ct);
        return new RenameResult(oldKey, newKey, changed, merged, await ReferencesAsync(scope, oldKey, ct));
    }

    public async Task<int> DeleteFieldAsync(Guid? clientId, string fieldKey, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(clientId, ct);
        var scope = Workspace.Key(clientId);
        var ids = db.Set<Subscriber>().Where(s => s.ScopeKey == scope).Select(s => s.Id);
        var removed = await db.Set<SubscriberField>().Where(f => f.Key == fieldKey && ids.Contains(f.SubscriberId)).ExecuteDeleteAsync(ct);
        if (removed == 0) throw DomainException.NotFound("Field");
        audit.Record("email.field.deleted", "SubscriberField", scope, new { Key = fieldKey, removed });
        await db.SaveChangesAsync(ct);
        return removed;
    }

    /// <summary>
    /// The JSON of the workspace's segments and live journeys, loaded once for a whole list (two queries instead of two per
    /// key); <see cref="CountReferences"/> applies the same case-insensitive "quoted key" match as <see cref="ReferencesAsync"/>.
    /// </summary>
    private async Task<List<string>> ReferenceTextsAsync(string scope, CancellationToken ct)
    {
        var texts = await db.Set<Segment>().AsNoTracking().Where(s => s.ScopeKey == scope).Select(s => s.DefinitionJson).ToListAsync(ct);
        var journeys = await db.Set<Automation>().AsNoTracking().Where(a => a.ScopeKey == scope && a.Status != AutomationStatus.Archived)
            .Select(a => new { a.TriggerConfigJson, a.GoalJson }).ToListAsync(ct);
        texts.AddRange(journeys.Select(j => j.TriggerConfigJson + "\n" + (j.GoalJson ?? string.Empty)));
        return texts;
    }

    private static int CountReferences(List<string> texts, string key)
    {
        var quoted = "\"" + key + "\"";
        return texts.Count(t => t.Contains(quoted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Segments and journeys of the workspace whose JSON mentions the key (quoted), e.g. a tag rule or a TagAdded trigger.</summary>
    private async Task<int> ReferencesAsync(string scope, string key, CancellationToken ct)
    {
        var like = "%\"" + key.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "\"%";
        var segments = await db.Set<Segment>().CountAsync(s => s.ScopeKey == scope && EF.Functions.Like(s.DefinitionJson, like, "\\"), ct);
        var journeys = await db.Set<Automation>().CountAsync(a => a.ScopeKey == scope && a.Status != AutomationStatus.Archived
            && (EF.Functions.Like(a.TriggerConfigJson, like, "\\") || (a.GoalJson != null && EF.Functions.Like(a.GoalJson, like, "\\"))), ct);
        return segments + journeys;
    }

    private static DomainException Invalid(string field, string message) =>
        new("validation.failed", message, errors: new Dictionary<string, string[]> { [field] = new[] { message } });
}
