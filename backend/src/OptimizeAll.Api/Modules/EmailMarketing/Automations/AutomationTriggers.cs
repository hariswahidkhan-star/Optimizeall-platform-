using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Automations;

/// <summary>
/// Enrolls contacts into active journeys when a trigger fires. Enrollment is idempotent: the unique
/// (automation, subscriber, iteration) index plus the re-entry rules mean a duplicate event never enrolls twice.
/// </summary>
public sealed class AutomationTriggers(AppDbContext db, TimeProvider clock, IDatabaseDialect dialect, ILogger<AutomationTriggers> logger)
{
    public Task<int> OnListSubscribedAsync(Guid? clientId, Guid listId, Guid subscriberId, CancellationToken ct) =>
        EnrollMatchingAsync(clientId, AutomationTrigger.ListSubscribed, c => c.ListId == listId, subscriberId, null, ct);

    public Task<int> OnTagAddedAsync(Guid? clientId, string tag, Guid subscriberId, CancellationToken ct) =>
        EnrollMatchingAsync(clientId, AutomationTrigger.TagAdded, c => string.Equals(ContactRulesTag(c.Tag), tag, StringComparison.Ordinal), subscriberId, null, ct);

    public Task<int> OnFormSubmittedAsync(Guid? clientId, Guid formId, Guid subscriberId, string? dataJson, CancellationToken ct) =>
        EnrollMatchingAsync(clientId, AutomationTrigger.FormSubmitted, c => c.FormId is null || c.FormId == formId, subscriberId, dataJson, ct);

    public Task<int> OnNewsletterConfirmedAsync(Guid? clientId, Guid subscriberId, CancellationToken ct) =>
        EnrollMatchingAsync(clientId, AutomationTrigger.NewsletterConfirmed, _ => true, subscriberId, null, ct);

    public Task<int> OnCustomEventAsync(Guid? clientId, string eventName, Guid subscriberId, string? dataJson, CancellationToken ct) =>
        EnrollMatchingAsync(clientId, AutomationTrigger.CustomEvent, c => string.Equals(c.EventName, eventName, StringComparison.OrdinalIgnoreCase), subscriberId, dataJson, ct);

    private static string? ContactRulesTag(string? tag) => ContactRules.NormalizeTag(tag);

    private async Task<int> EnrollMatchingAsync(Guid? clientId, AutomationTrigger trigger, Func<TriggerConfig, bool> matches, Guid subscriberId,
        string? dataJson, CancellationToken ct)
    {
        var key = Workspace.Key(clientId);
        var automations = await db.Set<Automation>().AsNoTracking()
            .Where(a => a.ScopeKey == key && a.Status == AutomationStatus.Active && a.Trigger == trigger).ToListAsync(ct);
        var enrolled = 0;
        foreach (var automation in automations)
        {
            TriggerConfig config;
            try { config = AutomationRules.Parse<TriggerConfig>(automation.TriggerConfigJson); }
            catch (FormatException) { continue; }
            if (!matches(config)) continue;
            if (await EnrollAsync(automation, subscriberId, null, dataJson, ct)) enrolled++;
        }
        return enrolled;
    }

    /// <summary>
    /// Enrolls one contact, honouring the re-entry policy. <paramref name="fixedIteration"/> pins the iteration (the year
    /// for anniversaries) so the same occasion can never enroll twice. Returns false when not enrolled.
    /// </summary>
    public async Task<bool> EnrollAsync(Automation automation, Guid subscriberId, int? fixedIteration, string? dataJson, CancellationToken ct)
    {
        if (automation.Status != AutomationStatus.Active || automation.EntryStepKey is null) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        var previous = await db.Set<AutomationEnrollment>().AsNoTracking()
            .Where(e => e.AutomationId == automation.Id && e.SubscriberId == subscriberId)
            .OrderByDescending(e => e.EnteredAt).FirstOrDefaultAsync(ct);
        int iteration;
        if (fixedIteration is { } fixedValue)
        {
            if (await db.Set<AutomationEnrollment>().AnyAsync(e => e.AutomationId == automation.Id && e.SubscriberId == subscriberId && e.Iteration == fixedValue, ct))
                return false;
            if (previous?.Status == EnrollmentStatus.Active) return false;
            iteration = fixedValue;
        }
        else if (previous is null) iteration = 1;
        else
        {
            if (automation.Reentry == ReentryPolicy.Never || previous.Status == EnrollmentStatus.Active) return false;
            if (automation.ReentryCooldownDays > 0 && previous.FinishedAt is { } finished && finished > now.AddDays(-automation.ReentryCooldownDays)) return false;
            iteration = await db.Set<AutomationEnrollment>().Where(e => e.AutomationId == automation.Id && e.SubscriberId == subscriberId)
                .MaxAsync(e => e.Iteration, ct) + 1;
        }

        var enrollment = new AutomationEnrollment
        {
            AutomationId = automation.Id,
            ClientAccountId = automation.ClientAccountId,
            SubscriberId = subscriberId,
            Iteration = iteration,
            CurrentStepKey = automation.EntryStepKey,
            NextRunAt = now,
            EnteredAt = now,
            TriggerDataJson = dataJson is { Length: > 4000 } ? null : dataJson,
        };
        db.Set<AutomationEnrollment>().Add(enrollment);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            logger.LogDebug("Duplicate enrollment for automation {Automation} subscriber {Subscriber} ignored", automation.Id, subscriberId);
            return false;
        }
        finally
        {
            db.Entry(enrollment).State = EntityState.Detached;
        }
    }
}
