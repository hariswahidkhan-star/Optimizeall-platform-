using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Billing;

/// <summary>
/// Credit notes correct issued invoices (which are immutable). A credit note is numbered (gapless), may be applied to the
/// invoice it corrects immediately, and any remaining credit can later be applied to other open invoices of the same
/// client and currency. A credit can never exceed an invoice's balance.
/// </summary>
public sealed class CreditNoteService(
    AppDbContext db,
    IDatabaseDialect dialect,
    IClientScope scope,
    ICurrentUser currentUser,
    IAuditLogger audit,
    IEventPublisher events,
    InvoiceService invoices,
    DocumentNumberService numbers,
    BillingSettingsService settingsService,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<CreditNoteDto> CreateAsync(CreateCreditNoteRequest request, CancellationToken ct)
    {
        var requestId = request.RequestId!.Value;
        var existing = await db.Set<CreditNote>().AsNoTracking().FirstOrDefaultAsync(c => c.RequestId == requestId, ct);
        if (existing is not null)
        {
            if (existing.Amount != request.Amount || existing.InvoiceId != request.InvoiceId)
                throw DomainException.Conflict("billing.request_id_reused", "This request id was already used for a different credit note.");
            return await GetAsync(existing.Id, ct);
        }

        Guid clientId;
        string currency;
        if (request.InvoiceId is { } invoiceId)
        {
            var invoice = await invoices.LoadScopedAsync(invoiceId, ct);
            clientId = invoice.ClientAccountId;
            currency = invoice.Currency;
        }
        else
        {
            clientId = request.ClientAccountId ?? throw new DomainException("billing.client_required",
                "Choose the invoice to credit, or the client for an unapplied credit.", errors: LineBuilder.Errors("clientAccountId", "Choose a client."));
            await scope.EnsureAccessAsync(clientId, ct: ct);
            var client = await db.Set<ClientAccount>().AsNoTracking().FirstAsync(c => c.Id == clientId, ct);
            currency = LineBuilder.NormalizeCurrency(request.Currency ?? client.Currency);
        }
        if (request.Amount <= 0 || Money.Round(request.Amount, currency) != request.Amount)
            throw new DomainException("billing.invalid_amount", $"Enter an amount greater than 0 with at most {Money.MinorUnitDigits(currency)} decimals.",
                errors: LineBuilder.Errors("amount", "Enter a valid amount."));

        var settings = await settingsService.GetAsync(ct);
        await numbers.EnsureAsync(db, DocumentNumberService.CreditNoteSeries, ct);
        CreditNote note;
        var paid = false;
        Invoice? target = null;
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            if (request.InvoiceId is { } id)
            {
                await dialect.LockRowAsync(db, "invoices", id, ct);
                target = await db.Set<Invoice>().FirstAsync(i => i.Id == id, ct);
                if (!Invoice.IsOpen(target.Status))
                    throw DomainException.Conflict("billing.invoice_not_open", $"A {target.Status} invoice can't be credited.");
                if (request.Amount > target.Balance)
                    throw DomainException.Conflict("billing.credit_exceeds_balance",
                        $"The credit is more than the invoice balance ({target.Balance} {target.Currency}).");
            }
            note = new CreditNote
            {
                Number = await numbers.NextAsync(db, DocumentNumberService.CreditNoteSeries, settings.CreditNotePrefix, settings.NumberPadding, ct),
                ClientAccountId = clientId,
                InvoiceId = request.InvoiceId,
                Currency = currency,
                Amount = request.Amount,
                Reason = request.Reason.Trim(),
                IssueDate = BillingDates.Today(clock),
                RequestId = requestId,
                CreatedByUserId = currentUser.IdOrNull,
            };
            db.Set<CreditNote>().Add(note);
            audit.Record("billing.credit_note_issued", nameof(CreditNote), note.Id,
                after: new { note.Number, note.Amount, note.Currency, note.InvoiceId, note.ClientAccountId }, reason: note.Reason);
            if (target is not null) paid = Apply(note, target, request.Amount);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                throw DomainException.Conflict("billing.request_id_reused", "This credit note was already created.");
            }
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        if (paid && target is { AmountPaid: > 0 })
            await events.PublishAsync(new InvoicePaid(target.Id, target.ClientAccountId, target.AmountPaid, target.Currency, Now), ct);
        return await GetAsync(note.Id, ct);
    }

    public async Task<CreditNoteDto> ApplyAsync(Guid creditNoteId, ApplyCreditNoteRequest request, CancellationToken ct)
    {
        await LoadScopedAsync(creditNoteId, ct);
        var invoiceId = request.InvoiceId!.Value;
        await invoices.LoadScopedAsync(invoiceId, ct);
        var paid = false;
        Invoice invoice;
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            // Lock order: credit note, then invoice (same everywhere).
            await dialect.LockRowAsync(db, "credit_notes", creditNoteId, ct);
            await dialect.LockRowAsync(db, "invoices", invoiceId, ct);
            var note = await db.Set<CreditNote>().FirstAsync(c => c.Id == creditNoteId, ct);
            invoice = await db.Set<Invoice>().FirstAsync(i => i.Id == invoiceId, ct);
            if (invoice.ClientAccountId != note.ClientAccountId || invoice.Currency != note.Currency)
                throw DomainException.Conflict("billing.credit_mismatch", "A credit can only be applied to an invoice of the same client and currency.");
            if (!Invoice.IsOpen(invoice.Status))
                throw DomainException.Conflict("billing.invoice_not_open", $"A {invoice.Status} invoice can't be credited.");
            var remaining = note.Amount - note.AmountApplied;
            if (request.Amount <= 0 || Money.Round(request.Amount, note.Currency) != request.Amount)
                throw new DomainException("billing.invalid_amount", "Enter a valid amount.", errors: LineBuilder.Errors("amount", "Enter a valid amount."));
            if (request.Amount > remaining)
                throw DomainException.Conflict("billing.credit_exhausted", $"Only {remaining} {note.Currency} of this credit note is left to apply.");
            if (request.Amount > invoice.Balance)
                throw DomainException.Conflict("billing.credit_exceeds_balance", $"The credit is more than the invoice balance ({invoice.Balance} {invoice.Currency}).");
            paid = Apply(note, invoice, request.Amount);
            audit.Record("billing.credit_note_applied", nameof(CreditNote), note.Id,
                after: new { InvoiceId = invoiceId, request.Amount, note.Currency, note.AmountApplied });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        if (paid && invoice.AmountPaid > 0)
            await events.PublishAsync(new InvoicePaid(invoice.Id, invoice.ClientAccountId, invoice.AmountPaid, invoice.Currency, Now), ct);
        return await GetAsync(creditNoteId, ct);
    }

    /// <summary>Applies part of a tracked credit note to a tracked, locked invoice. Returns true when the invoice became settled.</summary>
    private bool Apply(CreditNote note, Invoice invoice, decimal amount)
    {
        db.Set<CreditNoteApplication>().Add(new CreditNoteApplication
        {
            CreditNoteId = note.Id, InvoiceId = invoice.Id, Amount = amount, AppliedAt = Now, AppliedByUserId = currentUser.IdOrNull,
        });
        note.AmountApplied += amount;
        note.Status = note.AmountApplied >= note.Amount ? CreditNoteStatus.Applied : CreditNoteStatus.Open;
        var before = new { invoice.Status, invoice.Balance, invoice.AmountCredited };
        invoice.AmountCredited += amount;
        invoice.RecalculateBalance(invoice.Currency);
        invoice.Status = invoice.SettlementStatus(BillingDates.Today(clock));
        var settled = invoice.Status == InvoiceStatus.Paid;
        if (settled) invoice.PaidAt = Now;
        audit.Record("billing.invoice_credited", nameof(Invoice), invoice.Id, before,
            new { invoice.Status, invoice.Balance, invoice.AmountCredited, CreditNote = note.Number, Amount = amount });
        return settled;
    }

    public async Task<PagedResult<CreditNoteDto>> ListAsync(CreditNoteQuery query, CancellationToken ct)
    {
        var notes = await scope.ApplyAsync(db.Set<CreditNote>().AsNoTracking(), c => c.ClientAccountId, ct);
        if (query.ClientAccountId is { } clientId) notes = notes.Where(c => c.ClientAccountId == clientId);
        if (query.Status is { } status) notes = notes.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
            notes = notes.Where(c => EF.Functions.Like(c.Number, PagingExtensions.LikePattern(query.Search), "\\"));
        var total = await notes.CountAsync(ct);
        var rows = await notes.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        var dtos = new List<CreditNoteDto>();
        foreach (var row in rows) dtos.Add(await ToDtoAsync(row, ct));
        return new PagedResult<CreditNoteDto>(dtos, total, query.Page, query.PageSize);
    }

    public async Task<CreditNoteDto> GetAsync(Guid id, CancellationToken ct) => await ToDtoAsync(await LoadScopedAsync(id, ct), ct);

    private async Task<CreditNote> LoadScopedAsync(Guid id, CancellationToken ct)
    {
        var notes = await scope.ApplyAsync(db.Set<CreditNote>().AsNoTracking(), c => c.ClientAccountId, ct);
        return await notes.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("CreditNote");
    }

    private async Task<CreditNoteDto> ToDtoAsync(CreditNote note, CancellationToken ct)
    {
        var clientName = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == note.ClientAccountId).Select(c => c.Name).FirstAsync(ct);
        var applications = await (from a in db.Set<CreditNoteApplication>().AsNoTracking()
                                  join i in db.Set<Invoice>().AsNoTracking() on a.InvoiceId equals i.Id
                                  where a.CreditNoteId == note.Id
                                  orderby a.AppliedAt
                                  select new CreditApplicationDto(note.Id, note.Number, i.Id, i.Number, a.Amount, a.AppliedAt)).ToListAsync(ct);
        var invoiceNumber = note.InvoiceId is { } invoiceId
            ? await db.Set<Invoice>().AsNoTracking().Where(i => i.Id == invoiceId).Select(i => i.Number).FirstOrDefaultAsync(ct)
            : null;
        return new CreditNoteDto(note.Id, note.Number, note.ClientAccountId, clientName, note.InvoiceId, invoiceNumber, note.Currency,
            note.Amount, note.AmountApplied, note.Amount - note.AmountApplied, note.Status, note.Reason, note.IssueDate, applications, note.CreatedAt);
    }
}
