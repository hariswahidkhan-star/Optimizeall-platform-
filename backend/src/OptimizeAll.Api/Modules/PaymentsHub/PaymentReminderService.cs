using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.PaymentsHub;

/// <summary>
/// Payment reminders beyond the scheduled job (<see cref="InvoiceOverdueJob"/>): "Send reminder now" (idempotent by
/// request id, at most one per invoice per <see cref="ManualCooldown"/>), the per-invoice reminder history, per-client
/// schedules (<see cref="ClientReminderPolicy"/>, used by the job) and a dry-run preview of what the job would send
/// today. Delivery reuses the Billing module's invoice notification/email path (notification outbox + the configured
/// email sender, so File/test mode never sends real mail).
/// </summary>
public sealed class PaymentReminderService(
    AppDbContext db,
    IDatabaseDialect dialect,
    IClientScope scope,
    ICurrentUser currentUser,
    IAuditLogger audit,
    InvoiceService invoices,
    BillingSettingsService settingsService,
    TimeProvider clock)
{
    /// <summary>Minimum time between two reminders of one invoice sent by hand (protects clients from repeated emails).</summary>
    public static readonly TimeSpan ManualCooldown = TimeSpan.FromHours(1);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ReminderSentDto> SendNowAsync(Guid invoiceId, SendReminderRequest r, CancellationToken ct)
    {
        var requestId = r.RequestId!.Value;
        var invoice = await invoices.LoadScopedAsync(invoiceId, ct);
        var replay = await ReplayAsync(requestId, invoiceId, ct);
        if (replay is not null) return replay;
        if (!Invoice.IsOpen(invoice.Status) || invoice.Balance <= 0 || invoice.Number is null)
            throw DomainException.Conflict("billing.invoice_not_open", "Reminders can only be sent for issued invoices with a balance.");

        Func<CancellationToken, Task>? afterCommit;
        InvoiceReminder reminder;
        // Serializes manual reminders of one invoice so the cooldown check can't be raced (lock before the transaction).
        await using (await dialect.AcquireNamedLockAsync(db, $"oa:invoice-reminder:{invoiceId:N}", TimeSpan.FromSeconds(15), ct))
        {
            replay = await ReplayAsync(requestId, invoiceId, ct);
            if (replay is not null) return replay;
            var now = Now;
            var last = await db.Set<InvoiceReminder>().AsNoTracking().Where(x => x.InvoiceId == invoiceId)
                .OrderByDescending(x => x.SentAt).Select(x => (DateTime?)x.SentAt).FirstOrDefaultAsync(ct);
            if (last is { } at && now - at < ManualCooldown)
                throw DomainException.Conflict("payments.reminder_too_soon",
                    $"A reminder for this invoice was sent at {at:HH:mm} UTC. Wait at least {ManualCooldown.TotalMinutes:0} minutes before sending another.");

            await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
            reminder = new InvoiceReminder
            {
                InvoiceId = invoiceId, Kind = $"manual-{now:yyMMddHHmmss}", SentAt = now, SentByUserId = currentUser.Id, RequestId = requestId,
            };
            db.Set<InvoiceReminder>().Add(reminder);
            afterCommit = await invoices.DeliverAsync(invoice, reminder.Kind, ct);
            audit.Record("billing.invoice_reminder_sent", nameof(Invoice), invoiceId, after: new { invoice.Number, reminder.Kind, Manual = true });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return await ReplayAsync(requestId, invoiceId, ct)
                       ?? throw DomainException.Conflict("payments.reminder_too_soon", "A reminder for this invoice was just sent.");
            }
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        await afterCommit(ct);
        return new ReminderSentDto(invoiceId, reminder.Kind, reminder.SentAt, Replayed: false);
    }

    private async Task<ReminderSentDto?> ReplayAsync(Guid requestId, Guid invoiceId, CancellationToken ct)
    {
        var existing = await db.Set<InvoiceReminder>().AsNoTracking().FirstOrDefaultAsync(x => x.RequestId == requestId, ct);
        if (existing is null) return null;
        if (existing.InvoiceId != invoiceId)
            throw DomainException.Conflict("billing.request_id_reused", "This request id was already used for another invoice.");
        return new ReminderSentDto(invoiceId, existing.Kind, existing.SentAt, Replayed: true);
    }

    public async Task<IReadOnlyList<ReminderHistoryDto>> HistoryAsync(Guid invoiceId, CancellationToken ct)
    {
        await invoices.LoadScopedAsync(invoiceId, ct);
        var rows = await db.Set<InvoiceReminder>().AsNoTracking().Where(x => x.InvoiceId == invoiceId).OrderByDescending(x => x.SentAt).ToListAsync(ct);
        var ids = rows.Where(x => x.SentByUserId.HasValue).Select(x => x.SentByUserId!.Value).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return rows.Select(x => new ReminderHistoryDto(x.Kind, x.SentAt, x.IsManual, x.SentByUserId is { } u ? names.GetValueOrDefault(u) : null)).ToList();
    }

    // ------------------------------------------------------------------ Per-client schedule

    public async Task<ReminderPolicyDto> GetPolicyAsync(Guid clientAccountId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientAccountId, ct: ct);
        var settings = await settingsService.GetAsync(ct);
        var name = await db.Set<ClientAccount>().Where(c => c.Id == clientAccountId).Select(c => c.Name).FirstAsync(ct);
        var policy = await db.Set<ClientReminderPolicy>().AsNoTracking().FirstOrDefaultAsync(p => p.ClientAccountId == clientAccountId, ct);
        return policy is null
            ? new ReminderPolicyDto(clientAccountId, name, true, settings.RemindersEnabled, settings.ReminderOffsetsDays, settings.RemindersEnabled,
                settings.ReminderOffsetsDays, null)
            : new ReminderPolicyDto(clientAccountId, name, false, policy.Enabled, policy.OffsetsDays, settings.RemindersEnabled,
                settings.ReminderOffsetsDays, policy.ConcurrencyStamp);
    }

    public async Task<ReminderPolicyDto> UpdatePolicyAsync(Guid clientAccountId, UpdateReminderPolicyRequest r, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientAccountId, ct: ct);
        var offsets = r.OffsetsDays.Distinct().OrderBy(d => d).ToList();
        if (!r.UseAgencyDefault && (r.OffsetsDays.Count > 8 || r.OffsetsDays.Count != offsets.Count || offsets.Any(d => d is < -30 or > 180) ||
                                    (r.Enabled && offsets.Count == 0)))
            throw new DomainException("billing.invalid_settings", "Use 1–8 distinct reminder days between -30 and 180.",
                errors: LineBuilder.Errors("offsetsDays", "Use 1–8 distinct days between -30 and 180."));
        var reason = r.Reason.Trim();
        var policy = await db.Set<ClientReminderPolicy>().FirstOrDefaultAsync(p => p.ClientAccountId == clientAccountId, ct);
        if (policy is not null && r.ConcurrencyStamp != policy.ConcurrencyStamp)
            throw DomainException.Conflict("concurrency.conflict", "This reminder schedule was changed by someone else. Reload and try again.");
        var before = policy is null ? null : new { policy.Enabled, policy.OffsetsDays };
        if (r.UseAgencyDefault)
        {
            if (policy is not null)
            {
                db.Set<ClientReminderPolicy>().Remove(policy);
                audit.Record("billing.reminder_policy_removed", nameof(ClientAccount), clientAccountId, before, new { UsesAgencyDefault = true }, reason);
            }
        }
        else
        {
            if (policy is null)
            {
                policy = new ClientReminderPolicy { ClientAccountId = clientAccountId };
                db.Set<ClientReminderPolicy>().Add(policy);
            }
            else
            {
                db.Entry(policy).Property(p => p.ConcurrencyStamp).OriginalValue = r.ConcurrencyStamp!.Value;
            }
            policy.Enabled = r.Enabled;
            policy.OffsetsDays = offsets;
            policy.UpdatedByUserId = currentUser.Id;
            audit.Record("billing.reminder_policy_updated", nameof(ClientAccount), clientAccountId, before, new { policy.Enabled, policy.OffsetsDays }, reason);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainException.Conflict("concurrency.conflict", "This reminder schedule was changed by someone else. Reload and try again.");
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("concurrency.conflict", "This reminder schedule was changed by someone else. Reload and try again.");
        }
        db.ChangeTracker.Clear();
        return await GetPolicyAsync(clientAccountId, ct);
    }

    /// <summary>Dry run of today's scheduled reminders: which invoices would get which reminder (nothing is sent).</summary>
    public async Task<IReadOnlyList<ReminderPreviewRowDto>> PreviewAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(Now);
        var settings = await settingsService.GetAsync(ct);
        var policies = await db.Set<ClientReminderPolicy>().AsNoTracking().ToDictionaryAsync(p => p.ClientAccountId, ct);
        IReadOnlyList<int> agency = settings.RemindersEnabled ? settings.ReminderOffsetsDays : Array.Empty<int>();
        var all = agency.Concat(policies.Values.Where(p => p.Enabled).SelectMany(p => p.OffsetsDays)).ToList();
        if (all.Count == 0) return Array.Empty<ReminderPreviewRowDto>();
        var latestDue = today.AddDays(-all.Min());
        var candidates = await (await scope.ApplyAsync(db.Set<Invoice>().AsNoTracking(), i => i.ClientAccountId, ct))
            .Where(i => Invoice.OpenStatuses.Contains(i.Status) && i.DueDate != null && i.DueDate <= latestDue)
            .OrderBy(i => i.DueDate).Take(500).ToListAsync(ct);
        var ids = candidates.Select(i => i.Id).ToList();
        var sent = (await db.Set<InvoiceReminder>().AsNoTracking().Where(x => ids.Contains(x.InvoiceId)).Select(x => new { x.InvoiceId, x.Kind }).ToListAsync(ct))
            .Select(x => (x.InvoiceId, x.Kind)).ToHashSet();
        var clientIds = candidates.Select(i => i.ClientAccountId).Distinct().ToList();
        var names = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var rows = new List<ReminderPreviewRowDto>();
        foreach (var invoice in candidates)
        {
            if (invoice.IssueDate is { } issued && issued >= today) continue;
            var offsets = InvoiceOverdueJob.OffsetsFor(invoice.ClientAccountId, policies, agency);
            var offset = offsets.Count == 0 ? null : InvoiceOverdueJob.ApplicableOffset(invoice.DueDate!.Value, today, offsets);
            if (offset is null) continue;
            var kind = InvoiceOverdueJob.ReminderKind(offset.Value);
            rows.Add(new ReminderPreviewRowDto(invoice.Id, invoice.Number, names.GetValueOrDefault(invoice.ClientAccountId, "—"), invoice.DueDate!.Value,
                invoice.Balance, invoice.Currency, kind, sent.Contains((invoice.Id, kind))));
        }
        return rows;
    }
}
