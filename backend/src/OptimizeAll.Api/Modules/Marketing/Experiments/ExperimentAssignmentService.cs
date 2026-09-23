using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Marketing.Shared;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Marketing.Experiments;

/// <summary>
/// Sticky variant assignment. The variant is computed deterministically (<see cref="VariantAssigner"/>) and persisted
/// once per (experiment, subject); a concurrent insert of the same subject hits the unique index and the existing
/// row is read back, so a subject can never see two variants. Callers must not hold other pending changes in the
/// DbContext (the tracker is cleared after a unique-index race).
/// </summary>
public sealed class ExperimentAssignmentService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Running experiments (with variants) of a campaign, optionally restricted to one element.</summary>
    public async Task<List<Experiment>> RunningAsync(Guid campaignId, ExperimentElement? element, CancellationToken ct)
    {
        var q = db.Set<Experiment>().AsNoTracking().Include(e => e.Variants)
            .Where(e => e.CampaignId == campaignId && e.Status == ExperimentStatus.Running);
        if (element is { } el) q = q.Where(e => e.Element == el);
        return await q.OrderBy(e => e.StartedAt).ToListAsync(ct);
    }

    public async Task<ExperimentVariant> AssignAsync(Experiment experiment, string subjectKey, CancellationToken ct)
    {
        var existing = await db.Set<ExperimentAssignment>().AsNoTracking()
            .Where(a => a.ExperimentId == experiment.Id && a.SubjectKey == subjectKey)
            .Select(a => (Guid?)a.VariantId).FirstOrDefaultAsync(ct);
        if (existing is { } variantId && experiment.Variants.FirstOrDefault(v => v.Id == variantId) is { } stuck)
            return stuck;

        var key = VariantAssigner.Assign(experiment.Id, subjectKey,
            experiment.Variants.Select(v => new WeightedVariant(v.Key, v.Weight)).ToList());
        var variant = experiment.Variants.First(v => v.Key == key);

        var assignment = new ExperimentAssignment
        {
            ExperimentId = experiment.Id,
            VariantId = variant.Id,
            SubjectKey = subjectKey,
            AssignedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Set<ExperimentAssignment>().Add(assignment);
        try
        {
            await db.SaveChangesAsync(ct);
            db.Entry(assignment).State = EntityState.Detached;
            return variant;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            var winner = await db.Set<ExperimentAssignment>().AsNoTracking()
                .Where(a => a.ExperimentId == experiment.Id && a.SubjectKey == subjectKey)
                .Select(a => a.VariantId).FirstAsync(ct);
            return experiment.Variants.First(v => v.Id == winner);
        }
    }
}
