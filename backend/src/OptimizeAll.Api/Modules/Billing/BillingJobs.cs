using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Billing;

/// <summary>
/// Generates retainer invoices: for every active contract, one invoice per billing period whose start date has arrived.
/// Idempotent and retry-safe: each period is processed in its own transaction under a row lock on the contract, the
/// invoice carries the unique key <c>contract:{id}:{periodStart}</c>, and the contract's period index only advances in
/// the same transaction (a crash between the two is healed by the next run, which finds the invoice by its key).
/// </summary>
public sealed class RecurringBillingService(
    AppDbContext db,
    IDatabaseDialect dialect,
    IAuditLogger audit,
    InvoiceService invoices,
    DocumentNumberService numbers,
    BillingSettingsService settingsService,
    TimeProvider clock)
{
    /// <summary>At most this many missed periods are caught up per contract and run.</summary>
    public const int MaxCatchUpPeriods = 12;

    public sealed record RunResult(int InvoicesCreated, int InvoicesIssued, int ContractsEnded, int ContractsRenewed);

    public async Task<RunResult> RunAsync(CancellationToken ct, Guid? onlyContractId = null)
    {
        var today = BillingDates.Today(clock);
        var settings = await settingsService.GetAsync(ct);
        var contracts = await db.Set<Contract>().AsNoTracking()
            .Where(c => c.Status == ContractStatus.Active && (onlyContractId == null || c.Id == onlyContractId))
            .Select(c => new { c.Id, c.StartDate, c.BillingFrequency, c.NextPeriodIndex })
            .ToListAsync(ct);
        int created = 0, issued = 0, ended = 0, renewed = 0;
        var due = contracts.Where(c => BillingPeriods.PeriodStart(c.StartDate, c.BillingFrequency, c.NextPeriodIndex) <= today).ToList();
        if (due.Count > 0) await numbers.EnsureAsync(db, DocumentNumberService.InvoiceSeries, ct);
        foreach (var c in due)
        {
            var index = c.NextPeriodIndex;
            for (var i = 0; i < MaxCatchUpPeriods; i++)
            {
                if (BillingPeriods.PeriodStart(c.StartDate, c.BillingFrequency, index) > today) break;
                var outcome = await ProcessPeriodAsync(c.Id, index, today, settings, ct);
                db.ChangeTracker.Clear();
                created += outcome.Created ? 1 : 0;
                issued += outcome.Issued ? 1 : 0;
                ended += outcome.Ended ? 1 : 0;
                renewed += outcome.Renewed ? 1 : 0;
                if (!outcome.Advanced) break;
                index++;
            }
        }
        return new RunResult(created, issued, ended, renewed);
    }

    private sealed record PeriodOutcome(bool Advanced, bool Created = false, bool Issued = false, bool Ended = false, bool Renewed = false);

    private async Task<PeriodOutcome> ProcessPeriodAsync(Guid contractId, int expectedIndex, DateOnly today, BillingSettings settings, CancellationToken ct)
    {
        Func<CancellationToken, Task>? afterCommit = null;
        PeriodOutcome outcome;
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            if (!await dialect.LockRowAsync(db, "contracts", contractId, ct)) return new PeriodOutcome(false);
            var contract = await db.Set<Contract>().Include(c => c.Lines).FirstAsync(c => c.Id == contractId, ct);
            // Another run (or instance) already processed this period, or the contract was paused/cancelled meanwhile.
            if (contract.Status != ContractStatus.Active || contract.NextPeriodIndex != expectedIndex) return new PeriodOutcome(false);

            var start = BillingPeriods.PeriodStart(contract.StartDate, contract.BillingFrequency, expectedIndex);
            if (start > today) return new PeriodOutcome(false);
            var renewedNow = false;
            if (contract.EndDate is { } end && start > end)
            {
                if (!contract.AutoRenew)
                {
                    contract.Status = ContractStatus.Ended;
                    audit.RecordSystem("billing.contract_ended", nameof(Contract), contract.Id, new { contract.Number, EndDate = end });
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return new PeriodOutcome(false, Ended: true);
                }
                var renewedTo = end;
                while (start > renewedTo) renewedTo = renewedTo.AddMonths(contract.RenewalTermMonths);
                contract.EndDate = renewedTo;
                renewedNow = true;
                audit.RecordSystem("billing.contract_renewed", nameof(Contract), contract.Id,
                    new { contract.Number, PreviousEndDate = end, EndDate = renewedTo });
            }

            var key = BillingPeriods.InvoiceKey(contract.Id, start);
            var exists = await db.Set<Invoice>().AsNoTracking().AnyAsync(i => i.IdempotencyKey == key, ct);
            var issuedNow = false;
            if (!exists && contract.Lines.Count > 0)
            {
                var invoice = new Invoice
                {
                    ClientAccountId = contract.ClientAccountId,
                    Currency = contract.Currency,
                    PaymentTermsDays = contract.PaymentTermsDays,
                    ContractId = contract.Id,
                    PeriodStart = start,
                    PeriodEnd = BillingPeriods.PeriodEnd(contract.StartDate, contract.BillingFrequency, expectedIndex),
                    IdempotencyKey = key,
                    Reference = contract.Number,
                    Notes = $"{contract.Title} — {start:d MMM yyyy} to {BillingPeriods.PeriodEnd(contract.StartDate, contract.BillingFrequency, expectedIndex):d MMM yyyy}",
                    Lines = contract.Lines.OrderBy(l => l.Position).Select(l => LineBuilder.Copy(l, () => new InvoiceLine(), contract.Currency)).ToList(),
                };
                LineBuilder.ApplyTotals(invoice, LineBuilder.TotalsOf(invoice.Lines, invoice.Currency));
                db.Set<Invoice>().Add(invoice);
                audit.RecordSystem("billing.recurring_invoice_created", nameof(Invoice), invoice.Id,
                    new { ContractId = contract.Id, PeriodStart = start, invoice.Total, invoice.Currency });
                if ((contract.AutoIssueInvoices ?? settings.AutoIssueInvoices) && invoice.Total > 0)
                {
                    await invoices.IssueTrackedAsync(invoice, settings, actor: null, ct);
                    invoice.SentAt = clock.GetUtcNow().UtcDateTime;
                    afterCommit = await invoices.DeliverAsync(invoice, reminderKind: null, ct);
                    issuedNow = true;
                }
            }
            contract.NextPeriodIndex = expectedIndex + 1;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                // The period's invoice appeared concurrently; the next run advances the index.
                return new PeriodOutcome(false);
            }
            await tx.CommitAsync(ct);
            outcome = new PeriodOutcome(true, Created: !exists && contract.Lines.Count > 0, Issued: issuedNow, Renewed: renewedNow);
        }
        if (afterCommit is not null) await afterCommit(ct);
        return outcome;
    }
}

public sealed class RecurringInvoiceJob(RecurringBillingService billing) : IJob
{
    public string Name => "billing.recurring-invoices";

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var r = await billing.RunAsync(ct);
        return $"Created {r.InvoicesCreated} invoice(s) ({r.InvoicesIssued} issued); ended {r.ContractsEnded}, renewed {r.ContractsRenewed} contract(s).";
    }
}

/// <summary>
/// Marks issued invoices past their due date as Overdue and sends payment reminders on the configured schedule
/// (default 3 days before, on the due date, 7 and 14 days after). Each reminder kind is sent at most once per invoice
/// (unique row per invoice and kind); when the job missed a day only the latest applicable reminder is sent.
/// </summary>
public sealed class InvoiceOverdueJob(
    AppDbContext db,
    IDatabaseDialect dialect,
    IAuditLogger audit,
    InvoiceService invoices,
    BillingSettingsService settingsService,
    TimeProvider clock) : IJob
{
    public string Name => "billing.overdue-and-reminders";

    public static string ReminderKind(int offsetDays) => offsetDays switch
    {
        < 0 => $"before-{-offsetDays}",
        0 => "due",
        _ => $"after-{offsetDays}",
    };

    /// <summary>The reminder that applies today (the latest offset already reached), or null.</summary>
    public static int? ApplicableOffset(DateOnly dueDate, DateOnly today, IReadOnlyList<int> offsets)
    {
        int? best = null;
        foreach (var offset in offsets)
            if (today >= dueDate.AddDays(offset) && (best is null || offset > best)) best = offset;
        return best;
    }

    /// <summary>The reminder schedule of a client: its own policy when it has one, otherwise the agency schedule.</summary>
    public static IReadOnlyList<int> OffsetsFor(Guid clientAccountId, IReadOnlyDictionary<Guid, ClientReminderPolicy> policies, IReadOnlyList<int> agencyOffsets) =>
        policies.TryGetValue(clientAccountId, out var policy) ? (policy.Enabled ? policy.OffsetsDays : Array.Empty<int>()) : agencyOffsets;

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var today = BillingDates.Today(clock);
        var now = clock.GetUtcNow().UtcDateTime;
        var overdueIds = await db.Set<Invoice>().AsNoTracking()
            .Where(i => (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid) && i.DueDate < today)
            .Select(i => i.Id).ToListAsync(ct);
        var marked = 0;
        foreach (var id in overdueIds)
        {
            var updated = await db.Set<Invoice>()
                .Where(i => i.Id == id && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid) && i.DueDate < today)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvoiceStatus.Overdue).SetProperty(i => i.UpdatedAt, now)
                    .SetProperty(i => i.ConcurrencyStamp, Guid.NewGuid()), ct);
            if (updated == 1)
            {
                marked++;
                audit.RecordSystem("billing.invoice_overdue", nameof(Invoice), id);
            }
        }
        if (marked > 0) await db.SaveChangesAsync(ct);

        var settings = await settingsService.GetAsync(ct);
        // Per-client overrides (ClientReminderPolicy) replace the agency schedule for that client, including turning it off.
        var policies = await db.Set<ClientReminderPolicy>().AsNoTracking().ToDictionaryAsync(p => p.ClientAccountId, ct);
        IReadOnlyList<int> agencyOffsets = settings.RemindersEnabled ? settings.ReminderOffsetsDays : Array.Empty<int>();
        var allOffsets = agencyOffsets.Concat(policies.Values.Where(p => p.Enabled).SelectMany(p => p.OffsetsDays)).ToList();
        if (allOffsets.Count == 0)
            return $"Marked {marked} invoice(s) overdue; reminders are disabled.";

        // Every open invoice whose first reminder date has been reached; invoices that already got their latest applicable
        // reminder are skipped below (unique reminder per kind), so a long outage still sends the last reminder once.
        var latestDue = today.AddDays(-allOffsets.Min());
        var sent = 0;
        // Every candidate is visited, page by page (keyset on due date + id). A fixed "first N by due date" window would
        // fill up with old invoices that already had their last reminder and starve every newer one.
        DateOnly? afterDue = null;
        var afterId = Guid.Empty;
        while (true)
        {
            var query = db.Set<Invoice>().AsNoTracking()
                .Where(i => Invoice.OpenStatuses.Contains(i.Status) && i.DueDate != null && i.DueDate <= latestDue);
            if (afterDue is { } lastDue)
                query = query.Where(i => i.DueDate > lastDue || (i.DueDate == lastDue && i.Id.CompareTo(afterId) > 0));
            var page = await query.OrderBy(i => i.DueDate).ThenBy(i => i.Id).Take(CandidatePageSize).ToListAsync(ct);
            if (page.Count == 0) break;
            afterDue = page[^1].DueDate;
            afterId = page[^1].Id;

            var ids = page.Select(i => i.Id).ToList();
            var already = (await db.Set<InvoiceReminder>().AsNoTracking().Where(r => ids.Contains(r.InvoiceId))
                .Select(r => new { r.InvoiceId, r.Kind }).ToListAsync(ct)).Select(r => (r.InvoiceId, r.Kind)).ToHashSet();
            foreach (var candidate in page)
            {
                if (candidate.IssueDate is { } issued && issued >= today) continue; // just issued: the invoice email itself is the reminder
                var offsets = OffsetsFor(candidate.ClientAccountId, policies, agencyOffsets);
                if (offsets.Count == 0) continue;
                var offset = ApplicableOffset(candidate.DueDate!.Value, today, offsets);
                if (offset is null) continue;
                var kind = ReminderKind(offset.Value);
                if (already.Contains((candidate.Id, kind))) continue;
                if (await SendAsync(candidate.Id, kind, now, ct)) sent++;
            }
            if (page.Count < CandidatePageSize) break;
        }
        return $"Marked {marked} invoice(s) overdue; sent {sent} reminder(s).";
    }

    /// <summary>Invoices read per page while looking for due reminders.</summary>
    public const int CandidatePageSize = 500;

    /// <summary>
    /// Sends one scheduled reminder. The invoice is locked and re-read in the transaction: the candidate list may be
    /// minutes old by now, and an invoice paid, voided or written off meanwhile must not be chased (nor quoted with an
    /// outdated balance).
    /// </summary>
    private async Task<bool> SendAsync(Guid invoiceId, string kind, DateTime now, CancellationToken ct)
    {
        Func<CancellationToken, Task>? afterCommit = null;
        try
        {
            await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
            if (!await dialect.LockRowAsync(db, "invoices", invoiceId, ct)) return false;
            var invoice = await db.Set<Invoice>().AsNoTracking().FirstAsync(i => i.Id == invoiceId, ct);
            if (!Invoice.IsOpen(invoice.Status) || invoice.Balance <= 0) return false;
            if (await db.Set<InvoiceReminder>().AsNoTracking().AnyAsync(r => r.InvoiceId == invoiceId && r.Kind == kind, ct)) return false;
            db.Set<InvoiceReminder>().Add(new InvoiceReminder { InvoiceId = invoiceId, Kind = kind, SentAt = now });
            afterCommit = await invoices.DeliverAsync(invoice, kind, ct);
            audit.RecordSystem("billing.invoice_reminder_sent", nameof(Invoice), invoiceId, new { invoice.Number, Kind = kind, invoice.Balance, invoice.Currency });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            afterCommit = null; // another run (or instance) sent it
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
        if (afterCommit is null) return false;
        await afterCommit(ct);
        return true;
    }
}
