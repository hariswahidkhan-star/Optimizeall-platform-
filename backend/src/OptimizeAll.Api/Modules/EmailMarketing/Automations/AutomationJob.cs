using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.EmailMarketing.Audiences;
using OptimizeAll.Api.Modules.EmailMarketing.Campaigns;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Automations;

/// <summary>
/// Advances journey enrollments (a per-contact state machine). Each due enrollment is claimed with a conditional
/// update; each step's side effect is guarded by a unique (enrollment, step) run row, so a retried or concurrent run
/// never sends the same email twice, never re-applies a tag and always takes the same condition branch. Also runs the
/// daily date-anniversary scan.
/// </summary>
public sealed class AutomationJob(
    AppDbContext db,
    AutomationService automations,
    AutomationTriggers triggers,
    AudienceService audiences,
    CampaignAudience audience,
    MessageComposer composer,
    EmailSettingsStore settingsStore,
    EmailProviderResolver emailProviders,
    ISmsProvider sms,
    INotificationService notifications,
    IDatabaseDialect dialect,
    TimeProvider clock,
    ILogger<AutomationJob> logger) : IJob
{
    public const int BatchSize = 200;
    public const int MaxStepsPerRun = 25;

    public string Name => nameof(AutomationJob);

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var anniversaries = await ScanAnniversariesAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await (from e in db.Set<AutomationEnrollment>().AsNoTracking()
                         join a in db.Set<Automation>() on e.AutomationId equals a.Id
                         where e.Status == EnrollmentStatus.Active && a.Status == AutomationStatus.Active && e.NextRunAt <= now &&
                               (e.LockedUntil == null || e.LockedUntil < now)
                         orderby e.NextRunAt
                         select e.Id).Take(BatchSize).ToListAsync(ct);
        var processed = 0;
        foreach (var id in due)
        {
            ct.ThrowIfCancellationRequested();
            var claimNow = clock.GetUtcNow().UtcDateTime;
            var claim = Guid.NewGuid();
            var lease = claimNow.AddMinutes(5);
            var won = await db.Set<AutomationEnrollment>()
                .Where(e => e.Id == id && e.Status == EnrollmentStatus.Active && e.NextRunAt <= claimNow && (e.LockedUntil == null || e.LockedUntil < claimNow))
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.ClaimId, claim).SetProperty(e => e.LockedUntil, lease), ct);
            if (won != 1) continue;
            try
            {
                await ProcessAsync(id, claim, ct);
                processed++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Enrollment {Enrollment} failed", id);
                db.ChangeTracker.Clear();
                var message = Text.Truncate($"{ex.GetType().Name}: {ex.Message}", 500);
                var finished = clock.GetUtcNow().UtcDateTime;
                await db.Set<AutomationEnrollment>().Where(e => e.Id == id && e.ClaimId == claim)
                    .ExecuteUpdateAsync(u => u.SetProperty(e => e.Status, EnrollmentStatus.Failed).SetProperty(e => e.ExitReason, message)
                        .SetProperty(e => e.FinishedAt, finished).SetProperty(e => e.LockedUntil, (DateTime?)null), CancellationToken.None);
            }
        }
        return $"Processed {processed} of {due.Count} due enrollments; {anniversaries} anniversary enrollments.";
    }

    private sealed class State
    {
        public required AutomationEnrollment Enrollment { get; init; }
        public required Automation Automation { get; init; }
        public required Dictionary<string, StepDefinition> Steps { get; init; }
        public required Subscriber Subscriber { get; init; }
        public required EmailWorkspaceSettings Settings { get; init; }
    }

    private async Task ProcessAsync(Guid enrollmentId, Guid claim, CancellationToken ct)
    {
        var enrollment = await db.Set<AutomationEnrollment>().AsNoTracking().FirstAsync(e => e.Id == enrollmentId, ct);
        var automation = await db.Set<Automation>().AsNoTracking().FirstAsync(a => a.Id == enrollment.AutomationId, ct);
        var subscriber = await db.Set<Subscriber>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == enrollment.SubscriberId, ct);
        if (subscriber is null) { await FinishAsync(enrollmentId, claim, EnrollmentStatus.Exited, "The contact was deleted.", ct); return; }
        var state = new State
        {
            Enrollment = enrollment,
            Automation = automation,
            Steps = (await automations.StepsAsync(automation.Id, ct)).ToDictionary(s => s.Key),
            Subscriber = subscriber,
            Settings = await settingsStore.GetAsync(automation.ClientAccountId, ct),
        };

        var current = enrollment.CurrentStepKey;
        for (var i = 0; i < MaxStepsPerRun; i++)
        {
            if (await GoalMetAsync(state, ct)) { await FinishAsync(enrollmentId, claim, EnrollmentStatus.Exited, "Goal reached.", ct); return; }
            if (current is null) { await FinishAsync(enrollmentId, claim, EnrollmentStatus.Completed, null, ct); return; }
            if (!state.Steps.TryGetValue(current, out var step))
            {
                await FinishAsync(enrollmentId, claim, EnrollmentStatus.Exited, $"Step '{current}' no longer exists.", ct);
                return;
            }
            var result = await ExecuteStepAsync(state, step, ct);
            if (result.WaitUntil is { } until)
            {
                await SaveProgressAsync(enrollmentId, claim, current, until, i, ct);
                return;
            }
            if (result.Exit) { await FinishAsync(enrollmentId, claim, EnrollmentStatus.Exited, result.Reason ?? "Exit step.", ct); return; }
            current = result.Next;
            await SaveProgressAsync(enrollmentId, claim, current, null, i + 1, ct);
        }
        // Long chains of instant steps continue on the next run.
        await SaveProgressAsync(enrollmentId, claim, current, clock.GetUtcNow().UtcDateTime, 0, ct);
    }

    private sealed record StepResult(string? Next, DateTime? WaitUntil = null, bool Exit = false, string? Reason = null);

    private async Task<StepResult> ExecuteStepAsync(State s, StepDefinition step, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var zone = SendTiming.Zone(s.Subscriber.TimeZone, s.Settings.DefaultTimeZone);
        switch (step.Type)
        {
            case AutomationStepType.Exit:
                await RecordRunAsync(s, step, StepRunStatus.Completed, "exit", ct);
                return new StepResult(null, Exit: true, Reason: "Exit step reached.");

            case AutomationStepType.Wait:
            {
                var run = await GetOrStartRunAsync(s, step, ct);
                if (run.Status == StepRunStatus.Completed) return new StepResult(step.Next);
                var until = AutomationRules.WaitUntil(step.Config, run.StartedAt, zone);
                if (until > now) return new StepResult(null, WaitUntil: until);
                await CompleteRunAsync(run.Id, StepRunStatus.Completed, "waited", ct);
                return new StepResult(step.Next);
            }

            case AutomationStepType.Condition:
            {
                var existing = await db.Set<AutomationStepRun>().AsNoTracking().FirstOrDefaultAsync(r => r.EnrollmentId == s.Enrollment.Id && r.StepKey == step.Key, ct);
                bool yes;
                if (existing is { Status: StepRunStatus.Completed }) yes = existing.Detail == "yes";
                else
                {
                    yes = await EvaluateAsync(s, step.Config, ct);
                    await RecordRunAsync(s, step, StepRunStatus.Completed, yes ? "yes" : "no", ct);
                    // Re-read in case a concurrent run recorded first: its branch wins.
                    yes = (await db.Set<AutomationStepRun>().AsNoTracking().Where(r => r.EnrollmentId == s.Enrollment.Id && r.StepKey == step.Key)
                        .Select(r => r.Detail).FirstAsync(ct)) == "yes";
                }
                return new StepResult(yes ? step.Next : step.AltNext);
            }

            case AutomationStepType.AddTag:
            case AutomationStepType.RemoveTag:
            {
                if (await TryStartRunAsync(s, step, ct) is { } run)
                {
                    var tag = ContactRules.NormalizeTag(step.Config.Tag)!;
                    var tracked = await db.Set<Subscriber>().FirstAsync(x => x.Id == s.Subscriber.Id, ct);
                    if (step.Type == AutomationStepType.AddTag) await audiences.ChangeTagsAsync(tracked, new[] { tag }, Array.Empty<string>(), ct);
                    else await audiences.ChangeTagsAsync(tracked, Array.Empty<string>(), new[] { tag }, ct);
                    await CompleteRunAsync(run, StepRunStatus.Completed, tag, ct);
                }
                return new StepResult(step.Next);
            }

            case AutomationStepType.NotifyStaff:
            {
                if (await TryStartRunAsync(s, step, ct) is { } run)
                {
                    var recipients = await StaffRecipientsAsync(s, step.Config, ct);
                    var who = s.Subscriber.Email ?? s.Subscriber.Phone ?? "a contact";
                    foreach (var userId in recipients)
                        await notifications.StageAsync(new NotificationRequest(userId, "email.automation_alert", $"Journey: {s.Automation.Name}",
                            $"{step.Config.Message} (contact: {who})", "/agency/email/automations/" + s.Automation.Id), ct);
                    await db.SaveChangesAsync(ct);
                    await CompleteRunAsync(run, StepRunStatus.Completed, $"notified {recipients.Count}", ct);
                }
                return new StepResult(step.Next);
            }

            case AutomationStepType.SendEmail:
            case AutomationStepType.SendSms:
            {
                var existing = await db.Set<AutomationStepRun>().AsNoTracking().FirstOrDefaultAsync(r => r.EnrollmentId == s.Enrollment.Id && r.StepKey == step.Key, ct);
                if (existing is not null)
                {
                    // Already handled (or interrupted mid-send: never re-sent to avoid a duplicate).
                    if (existing.Status == StepRunStatus.Running)
                        await CompleteRunAsync(existing.Id, StepRunStatus.Failed, "Interrupted before the outcome was recorded; not re-sent.", ct);
                    return new StepResult(step.Next);
                }
                if (step.Type == AutomationStepType.SendSms && SendTiming.IsQuietHours(now, zone, s.Settings.QuietHoursStart, s.Settings.QuietHoursEnd))
                    return new StepResult(null, WaitUntil: SendTiming.AfterQuietHours(now, zone, s.Settings.QuietHoursStart, s.Settings.QuietHoursEnd));
                if (await TryStartRunAsync(s, step, ct) is not { } runId) return new StepResult(step.Next);
                var (status, detail) = await SendAsync(s, step, runId, ct);
                await CompleteRunAsync(runId, status, detail, ct);
                return new StepResult(step.Next);
            }
        }
        return new StepResult(step.Next);
    }

    private async Task<(StepRunStatus Status, string Detail)> SendAsync(State s, StepDefinition step, Guid runId, CancellationToken ct)
    {
        var channel = step.Type == AutomationStepType.SendSms ? MessageChannel.Sms : MessageChannel.Email;
        var subscriber = await db.Set<Subscriber>().AsNoTracking().FirstAsync(x => x.Id == s.Subscriber.Id, ct);
        var address = channel == MessageChannel.Email ? subscriber.NormalizedEmail : subscriber.Phone;
        if (address is null) return (StepRunStatus.Skipped, "The contact has no address for this channel.");
        if (await audience.BlockReasonAsync(subscriber, channel, address, ct) is { } reason) return (StepRunStatus.Skipped, reason);

        var values = await composer.ValuesAsync(subscriber, s.Settings, TokenSource.AutomationStepRun, runId, s.Automation.Name, s.Enrollment.TriggerDataJson, ct);
        ProviderResult result;
        var segments = 0;
        if (channel == MessageChannel.Sms)
        {
            var text = MessageComposer.ComposeText(step.Config.Body ?? string.Empty, values);
            segments = SmsSegments.Calculate(text).Segments;
            result = await sms.SendAsync(s.Automation.ClientAccountId, address, text, null, ct);
        }
        else
        {
            var template = await db.Set<EmailTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == step.Config.TemplateId, ct);
            if (template is null) return (StepRunStatus.Failed, "The email template no longer exists.");
            var sender = await db.Set<SenderProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == s.Automation.SenderProfileId, ct);
            if (sender?.VerifiedAt is null) return (StepRunStatus.Failed, "The journey's sender is not verified.");
            var design = EmailDesign.Parse(template.DesignJson);
            var sourceKey = AutomationSources.Key(s.Automation.Id, step.Key);
            var links = await composer.EnsureLinksAsync(s.Automation.ClientAccountId, sourceKey, EmailRenderer.ExtractLinks(design), ct);
            var composed = composer.Compose(design, step.Config.Subject ?? template.Subject, template.PreviewText, values, TokenSource.AutomationStepRun, runId, links,
                feedbackId: $"{s.Automation.Id:N}:{s.Automation.ScopeKey}:oa-journey", language: subscriber.Language ?? "en");
            var provider = await emailProviders.ForWorkspaceAsync(s.Automation.ClientAccountId, ct);
            result = await provider.SendAsync(new OutboundEmail(s.Automation.ClientAccountId, address,
                string.Join(' ', new[] { subscriber.FirstName, subscriber.LastName }.Where(n => !string.IsNullOrWhiteSpace(n))),
                sender.FromEmail, sender.FromName, sender.ReplyTo, composed.Subject, composed.Html, composed.Text, composed.Headers,
                new Dictionary<string, string> { ["oa_ref"] = "a:" + runId.ToString("N") }), ct);
        }

        if (result.Outcome != ProviderOutcome.Accepted)
            return (result.Outcome == ProviderOutcome.NotConfigured ? StepRunStatus.Skipped : StepRunStatus.Failed, Text.Truncate(result.Error ?? result.Outcome.ToString(), 1000));
        var sentAt = clock.GetUtcNow().UtcDateTime;
        var messageId = Text.Truncate(result.MessageId, 200);
        await db.Set<AutomationStepRun>().Where(r => r.Id == runId).ExecuteUpdateAsync(u => u.SetProperty(r => r.SentAt, sentAt)
            .SetProperty(r => r.ProviderMessageId, messageId).SetProperty(r => r.Channel, channel).SetProperty(r => r.Address, address)
            .SetProperty(r => r.Segments, segments), ct);
        await db.Set<Subscriber>().Where(x => x.Id == subscriber.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LastSentAt, sentAt), ct);
        return (StepRunStatus.Completed, "sent");
    }

    private async Task<bool> EvaluateAsync(State s, StepConfig c, CancellationToken ct)
    {
        var enrollmentId = s.Enrollment.Id;
        var subscriberId = s.Subscriber.Id;
        switch (c.Check)
        {
            case "opened":
                return await db.Set<AutomationStepRun>().AnyAsync(r => r.EnrollmentId == enrollmentId && r.OpenedAt != null && (c.StepKey == null || r.StepKey == c.StepKey), ct);
            case "clicked":
                return await db.Set<AutomationStepRun>().AnyAsync(r => r.EnrollmentId == enrollmentId && r.ClickedAt != null && (c.StepKey == null || r.StepKey == c.StepKey), ct);
            case "tag":
                var tag = ContactRules.NormalizeTag(c.Tag);
                return await db.Set<SubscriberTag>().AnyAsync(t => t.SubscriberId == subscriberId && t.Tag == tag, ct);
            case "event":
                var since = s.Enrollment.EnteredAt;
                return await db.Set<EngagementEvent>().AnyAsync(e => e.SubscriberId == subscriberId && e.Type == EngagementType.Custom && e.Name == c.EventName && e.OccurredAt >= since, ct);
            case "field":
                var value = (c.Value ?? string.Empty).Trim();
                var field = c.Field ?? string.Empty;
                if (field.StartsWith("custom.", StringComparison.Ordinal))
                {
                    var key = field["custom.".Length..];
                    return await db.Set<SubscriberField>().AnyAsync(f => f.SubscriberId == subscriberId && f.Key == key && f.Value == value, ct);
                }
                var actual = field switch
                {
                    "country" => s.Subscriber.CountryCode,
                    "language" => s.Subscriber.Language,
                    "source" => s.Subscriber.Source,
                    "first_name" => s.Subscriber.FirstName,
                    "last_name" => s.Subscriber.LastName,
                    _ => null,
                };
                return string.Equals(actual?.Trim(), value, StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }

    private async Task<bool> GoalMetAsync(State s, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.Automation.GoalJson)) return false;
        var goal = AutomationRules.Parse<GoalConfig>(s.Automation.GoalJson);
        var subscriberId = s.Subscriber.Id;
        var since = s.Enrollment.EnteredAt;
        return goal.Kind switch
        {
            "purchased" => await db.Set<EngagementEvent>().AnyAsync(e => e.SubscriberId == subscriberId && e.Type == EngagementType.Conversion && e.OccurredAt >= since, ct),
            "tag_added" => await db.Set<SubscriberTag>().AnyAsync(t => t.SubscriberId == subscriberId && t.Tag == goal.Tag, ct),
            "event" => await db.Set<EngagementEvent>().AnyAsync(e => e.SubscriberId == subscriberId && e.Type == EngagementType.Custom && e.Name == goal.EventName && e.OccurredAt >= since, ct),
            _ => false,
        };
    }

    private async Task<List<Guid>> StaffRecipientsAsync(State s, StepConfig c, CancellationToken ct)
    {
        var requested = c.UserIds ?? new List<Guid>();
        var staff = await db.Set<User>().AsNoTracking().Where(u => requested.Contains(u.Id) && u.Status == UserStatus.Active)
            .Select(u => new { u.Id, Roles = u.Roles.Select(r => r.Role).ToList() }).ToListAsync(ct);
        var ids = staff.Where(u => u.Roles.Any(r => r is not (Role.Participant or Role.Client))).Select(u => u.Id).ToList();
        if (ids.Count == 0 && s.Automation.ClientAccountId is { } clientId &&
            await db.Set<ClientAccount>().AsNoTracking().Where(x => x.Id == clientId).Select(x => x.AccountManagerUserId).FirstOrDefaultAsync(ct) is { } manager)
            ids.Add(manager);
        return ids;
    }

    // ---------- Run rows (idempotency) ----------

    private async Task<AutomationStepRun> GetOrStartRunAsync(State s, StepDefinition step, CancellationToken ct)
    {
        var existing = await db.Set<AutomationStepRun>().AsNoTracking().FirstOrDefaultAsync(r => r.EnrollmentId == s.Enrollment.Id && r.StepKey == step.Key, ct);
        if (existing is not null) return existing;
        await TryStartRunAsync(s, step, ct);
        return await db.Set<AutomationStepRun>().AsNoTracking().FirstAsync(r => r.EnrollmentId == s.Enrollment.Id && r.StepKey == step.Key, ct);
    }

    /// <summary>Inserts the run row; returns its id, or null when another run already owns this step.</summary>
    private async Task<Guid?> TryStartRunAsync(State s, StepDefinition step, CancellationToken ct)
    {
        var run = new AutomationStepRun
        {
            EnrollmentId = s.Enrollment.Id, AutomationId = s.Automation.Id, ClientAccountId = s.Automation.ClientAccountId, SubscriberId = s.Subscriber.Id,
            StepKey = step.Key, StepType = step.Type, Status = StepRunStatus.Running, StartedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Set<AutomationStepRun>().Add(run);
        try
        {
            await db.SaveChangesAsync(ct);
            return run.Id;
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            return null;
        }
        finally
        {
            db.Entry(run).State = EntityState.Detached;
        }
    }

    private async Task RecordRunAsync(State s, StepDefinition step, StepRunStatus status, string detail, CancellationToken ct)
    {
        if (await TryStartRunAsync(s, step, ct) is { } id) await CompleteRunAsync(id, status, detail, ct);
    }

    private async Task CompleteRunAsync(Guid runId, StepRunStatus status, string detail, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var text = Text.Truncate(detail, 1000);
        await db.Set<AutomationStepRun>().Where(r => r.Id == runId && r.Status == StepRunStatus.Running)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, status).SetProperty(r => r.FinishedAt, now).SetProperty(r => r.Detail, text), CancellationToken.None);
    }

    private async Task SaveProgressAsync(Guid id, Guid claim, string? stepKey, DateTime? nextRunAt, int stepsDone, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var next = nextRunAt ?? now;
        var release = nextRunAt is not null;
        await db.Set<AutomationEnrollment>().Where(e => e.Id == id && e.ClaimId == claim)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.CurrentStepKey, stepKey).SetProperty(e => e.NextRunAt, next)
                .SetProperty(e => e.StepsExecuted, e => e.StepsExecuted + (stepsDone > 0 ? 1 : 0))
                .SetProperty(e => e.LockedUntil, e => release ? null : e.LockedUntil), CancellationToken.None);
    }

    private async Task FinishAsync(Guid id, Guid claim, EnrollmentStatus status, string? reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Set<AutomationEnrollment>().Where(e => e.Id == id && e.ClaimId == claim)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.Status, status).SetProperty(e => e.FinishedAt, now).SetProperty(e => e.ExitReason, reason)
                .SetProperty(e => e.LockedUntil, (DateTime?)null), CancellationToken.None);
    }

    // ---------- Anniversaries ----------

    /// <summary>Once a day per journey (workspace time zone): enrolls contacts whose date field has today's month and day (iteration = year).</summary>
    private async Task<int> ScanAnniversariesAsync(CancellationToken ct)
    {
        var journeys = await db.Set<Automation>().AsNoTracking()
            .Where(a => a.Status == AutomationStatus.Active && a.Trigger == AutomationTrigger.DateAnniversary).ToListAsync(ct);
        var enrolled = 0;
        foreach (var a in journeys)
        {
            var settings = await settingsStore.GetAsync(a.ClientAccountId, ct);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, SendTiming.Zone(settings.DefaultTimeZone)));
            var todayKey = today.ToString("yyyy-MM-dd");
            if (a.LastAnniversaryScan == todayKey) continue;
            var claimed = await db.Set<Automation>().Where(x => x.Id == a.Id && (x.LastAnniversaryScan == null || x.LastAnniversaryScan != todayKey))
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.LastAnniversaryScan, todayKey), ct);
            if (claimed != 1) continue;
            var config = AutomationRules.Parse<TriggerConfig>(a.TriggerConfigJson);
            var suffix = today.ToString("-MM-dd");
            var field = config.DateField ?? string.Empty;
            var subscriberIds = await (from f in db.Set<SubscriberField>().AsNoTracking()
                                       join s in db.Set<Subscriber>() on f.SubscriberId equals s.Id
                                       where s.ScopeKey == a.ScopeKey && f.Key == field && f.Value.EndsWith(suffix)
                                       select s.Id).Take(50_000).ToListAsync(ct);
            foreach (var subscriberId in subscriberIds)
                if (await triggers.EnrollAsync(a, subscriberId, today.Year, JsonSerializer.Serialize(new { date_field = field }), ct)) enrolled++;
        }
        return enrolled;
    }
}

// ---------- Domain event handlers ----------

/// <summary>Landing-page form submissions become contacts (consent only when the form collected it) and start form journeys.</summary>
public sealed class FormSubmittedEmailHandler(AudienceService audiences, AutomationTriggers triggers, ILogger<FormSubmittedEmailHandler> logger)
    : IEventHandler<FormSubmitted>
{
    private static readonly string[] ConsentKeys = { "consent", "email_consent", "marketing_consent", "newsletter", "opt_in", "optin", "subscribe", "marketing_opt_in" };
    private static readonly string[] Truthy = { "true", "yes", "on", "1", "checked", "y" };

    public async Task HandleAsync(FormSubmitted e, CancellationToken ct)
    {
        var email = ContactRules.NormalizeEmail(e.Email);
        var phone = ContactRules.NormalizePhone(e.Phone);
        if (email is null && phone is null) return;
        bool Flag(IEnumerable<string> keys) => keys.Any(k => e.Fields.TryGetValue(k, out var v) && Truthy.Contains(v.Trim().ToLowerInvariant()));
        var names = (e.Name ?? string.Empty).Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var input = new ContactInput(email, phone, names.ElementAtOrDefault(0), names.ElementAtOrDefault(1), null, null, null, null, null, "form");
        var emailConsent = email is not null && Flag(ConsentKeys)
            ? new ConsentGrant(ConsentStatus.Granted, "form", null, $"form:{e.FormId:N}", null, "Consent box on the landing-page form") : null;
        var smsConsent = phone is not null && Flag(new[] { "sms_consent", "sms_opt_in" })
            ? new ConsentGrant(ConsentStatus.Granted, "form", null, $"form:{e.FormId:N}", null, "SMS consent box on the landing-page form") : null;
        var (subscriber, _) = await audiences.UpsertContactAsync(e.ClientAccountId, input, emailConsent, smsConsent, allowResubscribe: false, ct);
        var data = JsonSerializer.Serialize(e.Fields.Where(f => ContactRules.IsValidFieldKey(f.Key)).Take(30).ToDictionary(f => f.Key, f => Text.Truncate(f.Value, 200)));
        var enrolled = await triggers.OnFormSubmittedAsync(e.ClientAccountId, e.FormId, subscriber.Id, data.Length > 4000 ? null : data, ct);
        logger.LogInformation("Form {Form} submission linked to contact; {Count} journeys started", e.FormId, enrolled);
    }
}

/// <summary>Website newsletter sign-ups (double opt-in completed) join the agency's "Website newsletter" list.</summary>
public sealed class NewsletterSubscribedEmailHandler(AppDbContext db, AudienceService audiences, AutomationTriggers triggers) : IEventHandler<NewsletterSubscribed>
{
    public const string ListSeedKey = "website-newsletter";

    public async Task HandleAsync(NewsletterSubscribed e, CancellationToken ct)
    {
        var email = ContactRules.NormalizeEmail(e.Email);
        if (email is null) return;
        var list = await db.Set<EmailList>().FirstOrDefaultAsync(l => l.ScopeKey == Workspace.AgencyKey && l.SeedKey == ListSeedKey, ct);
        if (list is null)
        {
            list = new EmailList
            {
                ScopeKey = Workspace.AgencyKey, Name = "Website newsletter", SeedKey = ListSeedKey, DoubleOptIn = true, PublicKey = AudienceService.NewPublicKey(),
                Description = "People who subscribed on the agency website (double opt-in confirmed there).",
            };
            db.Set<EmailList>().Add(list);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                list = await db.Set<EmailList>().FirstAsync(l => l.ScopeKey == Workspace.AgencyKey && l.SeedKey == ListSeedKey, ct);
            }
        }
        var consent = new ConsentGrant(ConsentStatus.Granted, "website", null, "website-newsletter", null, "Website newsletter double opt-in");
        var (subscriber, _) = await audiences.UpsertContactAsync(null, new ContactInput(email, null, null, null, null, null, null, null, null, "website"),
            consent, null, allowResubscribe: true, ct);
        if (subscriber.EmailConsent != ConsentStatus.Granted) return;
        await audiences.SubscribeAsync(list, subscriber, "website", requireConfirmation: false, null, ct);
        await triggers.OnNewsletterConfirmedAsync(null, subscriber.Id, ct);
    }
}
