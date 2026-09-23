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
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Billing;

/// <summary>
/// Records payments against issued invoices. Race-safe: the invoice row is locked for the transaction, the caller must
/// present the invoice stamp it saw (two finance users recording at once → the second gets 409), a payment can never
/// exceed the balance, and a retried request (same <c>requestId</c>) returns the original payment. <see cref="InvoicePaid"/>
/// is published exactly once, by the transaction that settles the invoice.
/// </summary>
public sealed class PaymentService(
    AppDbContext db,
    IDatabaseDialect dialect,
    IClientScope scope,
    ICurrentUser currentUser,
    IAuditLogger audit,
    IEventPublisher events,
    InvoiceService invoices,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<PaymentRecordedDto> RecordAsync(Guid invoiceId, RecordPaymentRequest request, CancellationToken ct)
    {
        var requestId = request.RequestId!.Value;
        var visible = await invoices.LoadScopedAsync(invoiceId, ct);
        var actor = currentUser.Id;

        // Tenant rule: a user who belongs to the invoice's client organization can't record payments on it.
        if (await db.Set<ClientMember>().AnyAsync(m => m.ClientAccountId == visible.ClientAccountId && m.UserId == actor, ct))
            throw DomainException.Forbidden("billing.self_payment", "You can't record a payment on an invoice of your own organization.");

        var amount = request.Amount;
        if (amount <= 0 || Money.Round(amount, visible.Currency) != amount)
            throw new DomainException("billing.invalid_amount",
                $"Enter an amount greater than 0 with at most {Money.MinorUnitDigits(visible.Currency)} decimals for {visible.Currency}.",
                errors: LineBuilder.Errors("amount", "Enter a valid amount."));
        var reference = request.Reference.Trim();
        if (reference.Length is 0 or > 120)
            throw new DomainException("billing.invalid_reference", "Enter the payment reference (1–120 characters).",
                errors: LineBuilder.Errors("reference", "Enter the payment reference."));
        var paidOn = request.PaidOn!.Value;
        var today = BillingDates.Today(clock);
        if (paidOn > today.AddDays(1))
            throw new DomainException("billing.paid_on_in_future", "The payment date can't be in the future.",
                errors: LineBuilder.Errors("paidOn", "The payment date can't be in the future."));
        if (visible.IssueDate is { } issued && paidOn < issued.AddYears(-1))
            throw new DomainException("billing.paid_on_invalid", "The payment date is too far before the invoice date.",
                errors: LineBuilder.Errors("paidOn", "Check the payment date."));

        var replay = await FindReplayAsync(requestId, invoiceId, amount, reference, ct);
        if (replay is not null) return replay;

        Payment payment;
        bool becamePaid;
        Invoice invoice;
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            if (!await dialect.LockRowAsync(db, "invoices", invoiceId, ct)) throw DomainException.NotFound("Invoice");
            // A concurrent retry of the same request may have committed while we waited for the lock.
            if (await db.Set<Payment>().AsNoTracking().AnyAsync(p => p.RequestId == requestId, ct))
            {
                await tx.RollbackAsync(ct);
                return await FindReplayAsync(requestId, invoiceId, amount, reference, ct)
                       ?? throw DomainException.Conflict("billing.request_id_reused", "This request id was already used for another payment.");
            }
            invoice = await db.Set<Invoice>().FirstAsync(i => i.Id == invoiceId, ct);
            invoices.RequireStamp(invoice, request.ConcurrencyStamp);
            if (!Invoice.IsOpen(invoice.Status))
                throw DomainException.Conflict("billing.invoice_not_open",
                    invoice.Status == InvoiceStatus.Paid ? "This invoice is already paid in full." : $"Payments can't be recorded on a {invoice.Status} invoice.");
            if (amount > invoice.Balance)
                throw DomainException.Conflict("billing.overpayment",
                    $"The payment is more than the outstanding balance ({invoice.Balance} {invoice.Currency}). Overpayments aren't accepted.");
            if (await db.Set<Payment>().AnyAsync(p => p.InvoiceId == invoiceId && p.Reference == reference, ct))
                throw DomainException.Conflict("billing.duplicate_reference", "A payment with this reference was already recorded on this invoice.");

            payment = new Payment
            {
                InvoiceId = invoiceId,
                ClientAccountId = invoice.ClientAccountId,
                Amount = amount,
                Currency = invoice.Currency,
                Method = request.Method,
                Reference = reference,
                PaidOn = paidOn,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                RequestId = requestId,
                RecordedByUserId = actor,
                CreatedAt = Now,
            };
            db.Set<Payment>().Add(payment);
            var before = new { invoice.Status, invoice.AmountPaid, invoice.Balance };
            invoice.AmountPaid += amount;
            invoice.RecalculateBalance(invoice.Currency);
            invoice.Status = invoice.SettlementStatus(today);
            becamePaid = invoice.Status == InvoiceStatus.Paid;
            if (becamePaid) invoice.PaidAt = Now;
            audit.Record("billing.payment_recorded", nameof(Invoice), invoiceId, before,
                new { invoice.Status, invoice.AmountPaid, invoice.Balance, Payment = amount, invoice.Currency, request.Method, Reference = reference, PaidOn = paidOn },
                request.Notes);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                throw DomainException.Conflict("billing.duplicate_reference", "A payment with this reference or request id was already recorded.");
            }
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();

        if (becamePaid)
            await events.PublishAsync(new InvoicePaid(invoice.Id, invoice.ClientAccountId, invoice.AmountPaid, invoice.Currency, Now), ct);
        var dto = await invoices.GetAsync(invoiceId, ct);
        return new PaymentRecordedDto(dto.Payments.First(p => p.Id == payment.Id), dto, Replayed: false);
    }

    private async Task<PaymentRecordedDto?> FindReplayAsync(Guid requestId, Guid invoiceId, decimal amount, string reference, CancellationToken ct)
    {
        var existing = await db.Set<Payment>().AsNoTracking().FirstOrDefaultAsync(p => p.RequestId == requestId, ct);
        if (existing is null) return null;
        if (existing.InvoiceId != invoiceId || existing.Amount != amount || existing.Reference != reference)
            throw DomainException.Conflict("billing.request_id_reused",
                "This request id was already used for a different payment. Start a new payment instead.");
        var dto = await invoices.GetAsync(invoiceId, ct);
        return new PaymentRecordedDto(dto.Payments.First(p => p.Id == existing.Id), dto, Replayed: true);
    }

    public async Task<PagedResult<PaymentDto>> ListAsync(PaymentQuery query, CancellationToken ct)
    {
        var payments = await scope.ApplyAsync(db.Set<Payment>().AsNoTracking(), p => p.ClientAccountId, ct);
        if (query.ClientAccountId is { } clientId) payments = payments.Where(p => p.ClientAccountId == clientId);
        if (query.From is { } from) payments = payments.Where(p => p.PaidOn >= from);
        if (query.To is { } to) payments = payments.Where(p => p.PaidOn <= to);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = PagingExtensions.LikePattern(query.Search);
            payments = payments.Where(p => EF.Functions.Like(p.Reference, pattern, "\\"));
        }
        var total = await payments.CountAsync(ct);
        var rows = await payments.OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.CreatedAt)
            .Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<PaymentDto>(await ToDtosAsync(rows, ct), total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<PaymentDto>> ToDtosAsync(IReadOnlyList<Payment> rows, CancellationToken ct)
    {
        var invoiceIds = rows.Select(r => r.InvoiceId).Distinct().ToList();
        var numbers = await db.Set<Invoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Number, ct);
        var clientIds = rows.Select(r => r.ClientAccountId).Distinct().ToList();
        var clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var userIds = rows.Where(r => r.RecordedByUserId.HasValue).Select(r => r.RecordedByUserId!.Value).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return rows.Select(p => ToDto(p, numbers.GetValueOrDefault(p.InvoiceId), clients.GetValueOrDefault(p.ClientAccountId),
            p.RecordedByUserId is { } u ? users.GetValueOrDefault(u) : null)).ToList();
    }

    public static PaymentDto ToDto(Payment p, string? invoiceNumber, string? clientName, string? recordedBy) =>
        new(p.Id, p.InvoiceId, invoiceNumber, p.ClientAccountId, clientName, p.Amount, p.Currency, p.Method, p.Reference, p.PaidOn,
            p.Notes, p.RequestId, recordedBy, p.CreatedAt);
}

/// <summary>Result of asking a payment gateway for a hosted payment page.</summary>
public sealed record PaymentLinkResult(bool Configured, string? RedirectUrl, string Message);

/// <summary>
/// Online payment gateway adapter (Stripe, PayPal, …) for client invoices. The default implementation reports "not
/// configured": the platform never pretends an online payment happened. A real adapter must confirm payments through a
/// verified webhook that calls <see cref="PaymentService"/>'s rules (idempotent by the gateway's payment id).
/// </summary>
public interface IClientPaymentGateway
{
    string Key { get; }
    bool IsConfigured { get; }
    Task<PaymentLinkResult> CreatePaymentLinkAsync(Invoice invoice, CancellationToken ct);
}

public sealed class NotConfiguredPaymentGateway : IClientPaymentGateway
{
    public string Key => "none";
    public bool IsConfigured => false;

    public Task<PaymentLinkResult> CreatePaymentLinkAsync(Invoice invoice, CancellationToken ct) =>
        Task.FromResult(new PaymentLinkResult(false, null,
            "Online payment isn't set up yet. Please pay by bank transfer using the payment instructions on the invoice."));
}
