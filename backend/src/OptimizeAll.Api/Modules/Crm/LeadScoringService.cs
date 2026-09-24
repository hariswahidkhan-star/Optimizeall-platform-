using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Crm;

/// <summary>
/// Lead scoring: configurable rules (<see cref="LeadScoringRule"/>) evaluated by <see cref="LeadScoring"/> over the contact's
/// attributes and recorded engagement. Scores are recomputed whenever a contact, its company or its engagement changes.
/// </summary>
public sealed class LeadScoringService(AppDbContext db, IDatabaseDialect dialect, IAuditLogger audit, TimeProvider clock)
{
    public async Task<ScoreResult> ComputeAsync(Guid contactId, CancellationToken ct)
    {
        var contact = await db.Set<CrmContact>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId, ct);
        if (contact is null) return new ScoreResult(0, Array.Empty<ScoreLine>());
        var company = contact.CompanyId is { } companyId
            ? await db.Set<CrmCompany>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct)
            : null;
        var engagement = await EngagementCountsAsync(contactId, ct);
        var rules = await db.Set<LeadScoringRule>().AsNoTracking().Where(r => r.IsActive).ToListAsync(ct);
        return LeadScoring.Evaluate(rules, new ScoringFacts(company?.Industry, company?.Size.ToString(), contact.BudgetRange,
            company?.CountryCode, contact.Source, contact.LifecycleStage.ToString(), engagement));
    }

    public async Task<Dictionary<string, int>> EngagementCountsAsync(Guid contactId, CancellationToken ct) =>
        await db.Set<CrmEngagement>().AsNoTracking().Where(e => e.ContactId == contactId).GroupBy(e => e.Type)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    /// <summary>Recomputes and stores the score (atomic single-column update; never conflicts with edits of other fields).</summary>
    public async Task<int> RecomputeAsync(Guid contactId, CancellationToken ct)
    {
        var result = await ComputeAsync(contactId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Set<CrmContact>().Where(c => c.Id == contactId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Score, result.Score).SetProperty(c => c.ScoredAt, now), ct);
        return result.Score;
    }

    /// <summary>
    /// Recomputes many contacts with a fixed number of queries (contacts, companies, engagement, rules, then one update per
    /// distinct score) instead of four or five per contact, for bulk actions over up to a few hundred rows.
    /// </summary>
    public async Task RecomputeManyAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct)
    {
        if (contactIds.Count == 0) return;
        var ids = contactIds.Distinct().ToList();
        var contacts = await db.Set<CrmContact>().AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var companyIds = contacts.Where(c => c.CompanyId.HasValue).Select(c => c.CompanyId!.Value).Distinct().ToList();
        var companies = await db.Set<CrmCompany>().AsNoTracking().Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var engagement = (await db.Set<CrmEngagement>().AsNoTracking().Where(e => ids.Contains(e.ContactId))
                .GroupBy(e => new { e.ContactId, e.Type }).Select(g => new { g.Key.ContactId, g.Key.Type, Count = g.Count() }).ToListAsync(ct))
            .GroupBy(x => x.ContactId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Type, x => x.Count));
        var rules = await db.Set<LeadScoringRule>().AsNoTracking().Where(r => r.IsActive).ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var byScore = contacts.GroupBy(contact =>
        {
            var company = contact.CompanyId is { } cid ? companies.GetValueOrDefault(cid) : null;
            return LeadScoring.Evaluate(rules, new ScoringFacts(company?.Industry, company?.Size.ToString(), contact.BudgetRange,
                company?.CountryCode, contact.Source, contact.LifecycleStage.ToString(),
                engagement.GetValueOrDefault(contact.Id) ?? new Dictionary<string, int>())).Score;
        }, contact => contact.Id);
        foreach (var group in byScore)
        {
            var groupIds = group.ToList();
            var score = group.Key;
            await db.Set<CrmContact>().Where(c => groupIds.Contains(c.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Score, score).SetProperty(c => c.ScoredAt, now), ct);
        }
    }

    public async Task RecomputeCompanyAsync(Guid companyId, CancellationToken ct)
    {
        foreach (var id in await db.Set<CrmContact>().AsNoTracking().Where(c => c.CompanyId == companyId).Select(c => c.Id).ToListAsync(ct))
            await RecomputeAsync(id, ct);
    }

    public async Task<int> RecomputeAllAsync(CancellationToken ct)
    {
        var ids = await db.Set<CrmContact>().AsNoTracking().Select(c => c.Id).ToListAsync(ct);
        foreach (var id in ids) await RecomputeAsync(id, ct);
        return ids.Count;
    }

    /// <summary>Records an engagement once per <paramref name="sourceKey"/>. Returns false when it was already recorded.</summary>
    public async Task<bool> RecordEngagementAsync(Guid contactId, string type, string sourceKey, DateTime occurredAt, CancellationToken ct)
    {
        if (await db.Set<CrmEngagement>().AsNoTracking().AnyAsync(e => e.SourceKey == sourceKey, ct)) return false;
        var row = new CrmEngagement { ContactId = contactId, Type = type, SourceKey = sourceKey, OccurredAt = occurredAt };
        db.Set<CrmEngagement>().Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            return false;
        }
        finally
        {
            db.Entry(row).State = EntityState.Detached;
        }
    }

    // ---------------------------------------------------------------- Rules

    public async Task<IReadOnlyList<ScoringRuleDto>> ListRulesAsync(CancellationToken ct) =>
        (await db.Set<LeadScoringRule>().AsNoTracking().ToListAsync(ct))
        .OrderBy(r => r.Category).ThenBy(r => r.Field).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(ToDto).ToList();

    public async Task<ScoringRuleDto> CreateRuleAsync(ScoringRuleRequest request, CancellationToken ct)
    {
        var rule = new LeadScoringRule();
        Apply(rule, request);
        db.Set<LeadScoringRule>().Add(rule);
        audit.Record("crm.scoring_rule_created", nameof(LeadScoringRule), rule.Id, after: ToDto(rule));
        await db.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    public async Task<ScoringRuleDto> UpdateRuleAsync(Guid id, ScoringRuleRequest request, CancellationToken ct)
    {
        var rule = await db.Set<LeadScoringRule>().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw DomainException.NotFound("ScoringRule");
        CrmService.RequireStamp(db, rule, request.ConcurrencyStamp);
        var before = ToDto(rule);
        Apply(rule, request);
        audit.Record("crm.scoring_rule_updated", nameof(LeadScoringRule), id, before, ToDto(rule));
        await db.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    public async Task DeleteRuleAsync(Guid id, CancellationToken ct)
    {
        var rule = await db.Set<LeadScoringRule>().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw DomainException.NotFound("ScoringRule");
        db.Remove(rule);
        audit.Record("crm.scoring_rule_deleted", nameof(LeadScoringRule), id, before: ToDto(rule));
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(LeadScoringRule rule, ScoringRuleRequest r)
    {
        var field = r.Field.Trim();
        if (!LeadScoring.IsValidField(r.Category, field))
            throw new DomainException("crm.invalid_scoring_field",
                r.Category == ScoringCategory.Fit
                    ? $"Fit rules can use: {string.Join(", ", LeadScoring.FitFields)}."
                    : "Engagement rules need an event type (lower-case letters, digits, '_' or '.').",
                errors: new Dictionary<string, string[]> { ["field"] = new[] { "Choose a valid field." } });
        if (r.Category == ScoringCategory.Fit && string.IsNullOrWhiteSpace(r.MatchValue))
            throw new DomainException("crm.invalid_scoring_value", "Fit rules need a value to match (comma-separated, or * for any).",
                errors: new Dictionary<string, string[]> { ["matchValue"] = new[] { "Enter the value(s) to match." } });
        rule.Name = r.Name.Trim();
        rule.Category = r.Category;
        rule.Field = field;
        rule.MatchValue = r.Category == ScoringCategory.Fit ? r.MatchValue!.Trim() : null;
        rule.Points = r.Points;
        rule.MaxOccurrences = r.Category == ScoringCategory.Engagement ? r.MaxOccurrences : null;
        rule.IsActive = r.IsActive;
    }

    public static ScoringRuleDto ToDto(LeadScoringRule r) =>
        new(r.Id, r.Name, r.Category, r.Field, r.MatchValue, r.Points, r.MaxOccurrences, r.IsActive, r.ConcurrencyStamp);
}

/// <summary>Engagement published by other modules (email clicks, meeting bookings, …): recorded once and re-scored.</summary>
public sealed class ContactEngagementHandler(AppDbContext db, LeadScoringService scoring) : IEventHandler<ContactEngagementRecorded>
{
    public async Task HandleAsync(ContactEngagementRecorded e, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.Email) || string.IsNullOrWhiteSpace(e.SourceKey)) return;
        var type = e.Type.Trim().ToLowerInvariant();
        if (!LeadScoring.IsValidField(ScoringCategory.Engagement, type)) return;
        var normalized = Normalization.Email(e.Email);
        var contactId = await db.Set<CrmContact>().AsNoTracking().Where(c => c.NormalizedEmail == normalized).Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (contactId is null) return;
        if (await scoring.RecordEngagementAsync(contactId.Value, type, $"{type}:{e.SourceKey}"[..Math.Min(200, type.Length + 1 + e.SourceKey.Length)],
                e.OccurredAt, ct))
            await scoring.RecomputeAsync(contactId.Value, ct);
    }
}
