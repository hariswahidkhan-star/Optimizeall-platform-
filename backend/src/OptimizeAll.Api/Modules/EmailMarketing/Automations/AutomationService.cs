using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Modules.EmailMarketing.Audiences;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Automations;

public static class AutomationSources
{
    /// <summary>Tracked-link source key of an automation email step.</summary>
    public static string Key(Guid automationId, string stepKey) => $"a:{automationId:N}:{stepKey}";
}

public sealed record AutomationListItem(Guid Id, Guid? ClientAccountId, string Name, AutomationStatus Status, AutomationTrigger Trigger, int Active, int Completed,
    int Exited, DateTime UpdatedAt);

public sealed record StepStats(int Runs, int Sent, int Opened, int Clicked, int Skipped, int Failed);

public sealed record StepDto(string Key, AutomationStepType Type, StepConfig Config, string? Next, string? AltNext, StepStats Stats);

public sealed record AutomationDto(Guid Id, Guid? ClientAccountId, string Name, string? Description, AutomationStatus Status, AutomationTrigger Trigger,
    TriggerConfig TriggerConfig, ReentryPolicy Reentry, int ReentryCooldownDays, GoalConfig? Goal, Guid? SenderProfileId, string? EntryStepKey,
    IReadOnlyList<StepDto> Steps, int Active, int Completed, int Exited, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class AutomationRequest
{
    public Guid? ClientAccountId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Description { get; set; }
    public AutomationTrigger Trigger { get; set; }
    public TriggerConfig TriggerConfig { get; set; } = new();
    public ReentryPolicy Reentry { get; set; } = ReentryPolicy.Never;
    [Range(0, 3650)] public int ReentryCooldownDays { get; set; }
    public GoalConfig? Goal { get; set; }
    public Guid? SenderProfileId { get; set; }
    [MaxLength(20)] public string? EntryStepKey { get; set; }
    [Required] public List<StepDefinition> Steps { get; set; } = new();
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record EnrollmentDto(Guid Id, Guid SubscriberId, string? Email, EnrollmentStatus Status, string? CurrentStepKey, DateTime EnteredAt,
    DateTime NextRunAt, DateTime? FinishedAt, string? ExitReason);

public sealed class ManualEnrollRequest
{
    public Guid SubscriberId { get; set; }
}

/// <summary>Journey authoring: validated step graphs, activation checks and per-step statistics.</summary>
public sealed class AutomationService(AppDbContext db, EmailAccess access, IAuditLogger audit, AutomationTriggers triggers)
{
    public async Task<IReadOnlyList<AutomationListItem>> ListAsync(Guid? clientId, CancellationToken ct, bool includeArchived = false)
    {
        await access.EnsureStaffWorkspaceAsync(clientId, ct);
        var key = Workspace.Key(clientId);
        var automations = await db.Set<Automation>().AsNoTracking().Where(a => a.ScopeKey == key && (includeArchived || a.Status != AutomationStatus.Archived))
            .OrderBy(a => a.Name).ToListAsync(ct);
        var ids = automations.Select(a => a.Id).ToList();
        var counts = await db.Set<AutomationEnrollment>().AsNoTracking().Where(e => ids.Contains(e.AutomationId))
            .GroupBy(e => new { e.AutomationId, e.Status }).Select(g => new { g.Key.AutomationId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        int Count(Guid id, EnrollmentStatus s) => counts.Where(c => c.AutomationId == id && c.Status == s).Sum(c => c.Count);
        return automations.Select(a => new AutomationListItem(a.Id, a.ClientAccountId, a.Name, a.Status, a.Trigger, Count(a.Id, EnrollmentStatus.Active),
            Count(a.Id, EnrollmentStatus.Completed), Count(a.Id, EnrollmentStatus.Exited), a.UpdatedAt)).ToList();
    }

    public Task<Automation> LoadAsync(Guid id, CancellationToken ct) =>
        access.LoadAsync(db.Set<Automation>().Where(a => a.Id == id), a => a.ClientAccountId, "Automation", ct);

    public async Task<AutomationDto> GetAsync(Guid id, CancellationToken ct) => await ToDtoAsync(await LoadAsync(id, ct), ct);

    public async Task<AutomationDto> CreateAsync(AutomationRequest r, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(r.ClientAccountId, ct);
        var automation = new Automation { ClientAccountId = r.ClientAccountId, ScopeKey = Workspace.Key(r.ClientAccountId) };
        await ApplyAsync(automation, r, ct);
        db.Set<Automation>().Add(automation);
        ReplaceSteps(automation, r.Steps);
        audit.Record("email.automation.created", nameof(Automation), automation.Id, after: new { automation.Name, automation.Trigger, steps = r.Steps.Count });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(automation, ct);
    }

    public async Task<AutomationDto> UpdateAsync(Guid id, AutomationRequest r, CancellationToken ct)
    {
        var automation = await LoadAsync(id, ct);
        AudienceService.ExpectStamp(automation.ConcurrencyStamp, r.ConcurrencyStamp);
        await ApplyAsync(automation, r, ct);
        var existing = await db.Set<AutomationStep>().Where(s => s.AutomationId == id).ToListAsync(ct);
        db.Set<AutomationStep>().RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        ReplaceSteps(automation, r.Steps);
        audit.Record("email.automation.updated", nameof(Automation), automation.Id, after: new { automation.Name, automation.Trigger, steps = r.Steps.Count });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(automation, ct);
    }

    /// <summary>Activates a journey after validating the graph, templates and sender (emails will really be sent).</summary>
    public async Task<AutomationDto> SetStatusAsync(Guid id, AutomationStatus status, CancellationToken ct)
    {
        var automation = await LoadAsync(id, ct);
        if (status == AutomationStatus.Active)
        {
            var steps = await StepsAsync(id, ct);
            var errors = AutomationRules.Validate(automation.Trigger, AutomationRules.Parse<TriggerConfig>(automation.TriggerConfigJson), steps, automation.EntryStepKey);
            errors.AddRange(await ReferenceErrorsAsync(automation, steps, requireVerifiedSender: true, ct));
            if (errors.Count > 0) throw EmailProblem.Invalid("email.automation_invalid", "The journey cannot be activated yet.", errors, "steps");
        }
        var before = automation.Status;
        automation.Status = status;
        audit.Record("email.automation.status_changed", nameof(Automation), automation.Id, new { Status = before }, new { Status = status });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(automation, ct);
    }

    /// <summary>Brings an archived journey back as Paused (it must be activated again, which re-validates it).</summary>
    public async Task<AutomationDto> RestoreAsync(Guid id, CancellationToken ct)
    {
        var automation = await LoadAsync(id, ct);
        if (automation.Status != AutomationStatus.Archived)
            throw DomainException.Conflict("email.automation_not_archived", "Only archived journeys can be restored.");
        automation.Status = AutomationStatus.Paused;
        audit.Record("email.automation.restored", nameof(Automation), automation.Id, new { Status = AutomationStatus.Archived }, new { automation.Status });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(automation, ct);
    }

    /// <summary>Copies a journey (trigger, goal and steps) into a new draft of the same workspace.</summary>
    public async Task<AutomationDto> DuplicateAsync(Guid id, CancellationToken ct)
    {
        var source = await LoadAsync(id, ct);
        var copy = new Automation
        {
            ClientAccountId = source.ClientAccountId, ScopeKey = source.ScopeKey, Name = Text.Truncate(source.Name + " (copy)", 150),
            Description = source.Description, Status = AutomationStatus.Draft, Trigger = source.Trigger, TriggerConfigJson = source.TriggerConfigJson,
            Reentry = source.Reentry, ReentryCooldownDays = source.ReentryCooldownDays, GoalJson = source.GoalJson, SenderProfileId = source.SenderProfileId,
            EntryStepKey = source.EntryStepKey,
        };
        db.Set<Automation>().Add(copy);
        ReplaceSteps(copy, await StepsAsync(source.Id, ct));
        audit.Record("email.automation.duplicated", nameof(Automation), copy.Id, after: new { From = source.Id, copy.Name });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(copy, ct);
    }

    /// <summary>Deletes a journey nobody ever entered (drafts made by mistake). Journeys with history are archived instead.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var automation = await LoadAsync(id, ct);
        if (automation.Status == AutomationStatus.Active)
            throw DomainException.Conflict("email.automation_active", "Pause or archive the journey before deleting it.");
        if (await db.Set<AutomationEnrollment>().AnyAsync(e => e.AutomationId == id, ct))
            throw DomainException.Conflict("email.automation_has_history", "Contacts have entered this journey; archive it instead so its statistics are kept.");
        await db.Set<AutomationStep>().Where(s => s.AutomationId == id).ExecuteDeleteAsync(ct);
        db.Remove(automation);
        audit.Record("email.automation.deleted", nameof(Automation), id, before: new { automation.Name, automation.Status });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EnrollmentDto>> EnrollmentsAsync(Guid id, CancellationToken ct)
    {
        await LoadAsync(id, ct);
        return await (from e in db.Set<AutomationEnrollment>().AsNoTracking()
                      join s in db.Set<Subscriber>() on e.SubscriberId equals s.Id
                      where e.AutomationId == id
                      orderby e.EnteredAt descending
                      select new EnrollmentDto(e.Id, e.SubscriberId, s.Email, e.Status, e.CurrentStepKey, e.EnteredAt, e.NextRunAt, e.FinishedAt, e.ExitReason))
            .Take(100).ToListAsync(ct);
    }

    public async Task<bool> EnrollAsync(Guid id, ManualEnrollRequest r, CancellationToken ct)
    {
        var automation = await LoadAsync(id, ct);
        if (!await db.Set<Subscriber>().AnyAsync(s => s.Id == r.SubscriberId && s.ScopeKey == automation.ScopeKey, ct)) throw DomainException.NotFound("Subscriber");
        if (automation.Status != AutomationStatus.Active) throw DomainException.Conflict("email.automation_inactive", "Activate the journey first.");
        var enrolled = await triggers.EnrollAsync(automation, r.SubscriberId, null, null, ct);
        audit.Record("email.automation.manual_enroll", nameof(Automation), automation.Id, after: new { r.SubscriberId, enrolled });
        await db.SaveChangesAsync(ct);
        return enrolled;
    }

    private async Task ApplyAsync(Automation a, AutomationRequest r, CancellationToken ct)
    {
        var entry = r.EntryStepKey ?? r.Steps.FirstOrDefault()?.Key;
        var errors = AutomationRules.Validate(r.Trigger, r.TriggerConfig, r.Steps, entry);
        a.Name = r.Name.Trim();
        a.Description = Text.Clean(r.Description, 1000);
        a.Trigger = r.Trigger;
        a.TriggerConfigJson = AutomationRules.ToJson(r.TriggerConfig);
        a.Reentry = r.Reentry;
        a.ReentryCooldownDays = r.ReentryCooldownDays;
        a.GoalJson = r.Goal is null ? null : AutomationRules.ToJson(r.Goal);
        if (r.Goal is { } g)
        {
            if (g.Kind is not ("purchased" or "tag_added" or "event")) errors.Add("Goal must be purchased, tag_added or event.");
            if (g.Kind == "tag_added" && ContactRules.NormalizeTag(g.Tag) is null) errors.Add("Goal tag is invalid.");
            if (g.Kind == "event" && !AutomationRules.IsEventName(g.EventName)) errors.Add("Goal event name is invalid.");
        }
        a.SenderProfileId = r.SenderProfileId;
        a.EntryStepKey = entry;
        if (r.TriggerConfig.ListId is { } listId && !await db.Set<EmailList>().AnyAsync(l => l.Id == listId && l.ScopeKey == a.ScopeKey, ct))
            errors.Add("The trigger list does not belong to this workspace.");
        errors.AddRange(await ReferenceErrorsAsync(a, r.Steps, requireVerifiedSender: false, ct));
        if (errors.Count > 0) throw EmailProblem.Invalid("email.automation_invalid", "The journey is invalid.", errors, "steps");
    }

    private async Task<List<string>> ReferenceErrorsAsync(Automation a, IReadOnlyList<StepDefinition> steps, bool requireVerifiedSender, CancellationToken ct)
    {
        var errors = new List<string>();
        var templateIds = steps.Where(s => s.Type == AutomationStepType.SendEmail && s.Config.TemplateId is not null).Select(s => s.Config.TemplateId!.Value).Distinct().ToList();
        var found = await db.Set<EmailTemplate>().AsNoTracking()
            .Where(t => templateIds.Contains(t.Id) && (t.ScopeKey == a.ScopeKey || t.ScopeKey == Workspace.AgencyKey)).Select(t => t.Id).ToListAsync(ct);
        foreach (var missing in templateIds.Except(found)) errors.Add($"Template {missing} is not available in this workspace.");
        if (steps.Any(s => s.Type == AutomationStepType.SendEmail))
        {
            if (a.SenderProfileId is null) errors.Add("Choose the sender for this journey's emails.");
            else
            {
                var sender = await db.Set<SenderProfile>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == a.SenderProfileId && s.ScopeKey == a.ScopeKey, ct);
                if (sender is null) errors.Add("The sender profile does not belong to this workspace.");
                else if (requireVerifiedSender && sender.VerifiedAt is null) errors.Add($"Verify {sender.FromEmail} before activating.");
            }
        }
        return errors;
    }

    private void ReplaceSteps(Automation a, IReadOnlyList<StepDefinition> steps)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            db.Set<AutomationStep>().Add(new AutomationStep
            {
                AutomationId = a.Id, Key = s.Key, Position = i, Type = s.Type, ConfigJson = AutomationRules.ToJson(s.Config), NextKey = s.Next, AltNextKey = s.AltNext,
            });
        }
    }

    public async Task<List<StepDefinition>> StepsAsync(Guid automationId, CancellationToken ct) =>
        (await db.Set<AutomationStep>().AsNoTracking().Where(s => s.AutomationId == automationId).OrderBy(s => s.Position).ToListAsync(ct))
        .Select(s => new StepDefinition { Key = s.Key, Type = s.Type, Config = AutomationRules.Parse<StepConfig>(s.ConfigJson), Next = s.NextKey, AltNext = s.AltNextKey })
        .ToList();

    private async Task<AutomationDto> ToDtoAsync(Automation a, CancellationToken ct)
    {
        var steps = await StepsAsync(a.Id, ct);
        var runStats = await db.Set<AutomationStepRun>().AsNoTracking().Where(r => r.AutomationId == a.Id).GroupBy(r => r.StepKey)
            .Select(g => new
            {
                Key = g.Key,
                Runs = g.Count(),
                Sent = g.Count(r => r.SentAt != null),
                Opened = g.Count(r => r.OpenedAt != null),
                Clicked = g.Count(r => r.ClickedAt != null),
                Skipped = g.Count(r => r.Status == StepRunStatus.Skipped),
                Failed = g.Count(r => r.Status == StepRunStatus.Failed),
            }).ToListAsync(ct);
        var counts = await db.Set<AutomationEnrollment>().AsNoTracking().Where(e => e.AutomationId == a.Id).GroupBy(e => e.Status)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(EnrollmentStatus s) => counts.Where(c => c.Key == s).Sum(c => c.Count);
        return new AutomationDto(a.Id, a.ClientAccountId, a.Name, a.Description, a.Status, a.Trigger, AutomationRules.Parse<TriggerConfig>(a.TriggerConfigJson),
            a.Reentry, a.ReentryCooldownDays, a.GoalJson is null ? null : AutomationRules.Parse<GoalConfig>(a.GoalJson), a.SenderProfileId, a.EntryStepKey,
            steps.Select(s =>
            {
                var st = runStats.FirstOrDefault(x => x.Key == s.Key);
                return new StepDto(s.Key, s.Type, s.Config, s.Next, s.AltNext,
                    new StepStats(st?.Runs ?? 0, st?.Sent ?? 0, st?.Opened ?? 0, st?.Clicked ?? 0, st?.Skipped ?? 0, st?.Failed ?? 0));
            }).ToList(),
            Count(EnrollmentStatus.Active), Count(EnrollmentStatus.Completed), Count(EnrollmentStatus.Exited), a.UpdatedAt, a.ConcurrencyStamp);
    }
}
