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

        var replay = await FindReplayAsync(requestId, invoiceId, amount, reference, request.Method, paidOn, ct);
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
                return await FindReplayAsync(requestId, invoiceId, amount, reference, request.Method, paidOn, ct)
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
            if (await db.Set<Payment>().AnyAsync(p => p.InvoiceId == invoiceId && p.ActiveReference == reference, ct))
                throw DomainException.Conflict("billing.duplicate_reference", "A payment with this reference was already recorded on this invoice.");

            payment = new Payment
            {
                InvoiceId = invoiceId,
                ClientAccountId = invoice.ClientAccountId,
                Amount = amount,
                Currency = invoice.Currency,
                Method = request.Method,
                Reference = reference,
                ActiveReference = reference,
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

    /// <summary>
    /// The payment an earlier request with this id recorded (a retry), or null. A request id reused with different data
    /// (another invoice, amount, reference, method or date — or a reversal's id) is refused, never answered with the old
    /// payment as if it were this one.
    /// </summary>
    private async Task<PaymentRecordedDto?> FindReplayAsync(Guid requestId, Guid invoiceId, decimal amount, string reference,
        PaymentMethod method, DateOnly paidOn, CancellationToken ct)
    {
        var existing = await db.Set<Payment>().AsNoTracking().FirstOrDefaultAsync(p => p.RequestId == requestId, ct);
        if (existing is null) return null;
        if (existing.IsReversal || existing.InvoiceId != invoiceId || existing.Amount != amount || existing.Reference != reference ||
            existing.Method != method || existing.PaidOn != paidOn)
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
            p.Notes, p.RequestId, recordedBy, p.CreatedAt, p.ReversalOfPaymentId, p.ReversedAt, p.ReversalKind, p.ReversalReason,
            p.UpdatedAt, p.ConcurrencyStamp);

    // ------------------------------------------------------------------ Corrections (used by the Payments hub)

    /// <summary>Loads a payment the caller may see (through its client scope), or 404.</summary>
    public async Task<Payment> LoadScopedAsync(Guid paymentId, CancellationToken ct)
    {
        var query = await scope.ApplyAsync(db.Set<Payment>().AsNoTracking(), p => p.ClientAccountId, ct);
        return await query.FirstOrDefaultAsync(p => p.Id == paymentId, ct) ?? throw DomainException.NotFound("Payment");
    }

    private async Task RequireNotClientMemberAsync(Guid clientAccountId, Guid actor, CancellationToken ct)
    {
        if (await db.Set<ClientMember>().AnyAsync(m => m.ClientAccountId == clientAccountId && m.UserId == actor, ct))
            throw DomainException.Forbidden("billing.self_payment", "You can't change payments on an invoice of your own organization.");
    }

    private static DomainException StalePayment() =>
        DomainException.Conflict("concurrency.conflict", "This payment was changed by someone else. Reload and try again.");

    /// <summary>
    /// Edits a payment's reference, date, method or notes (never the amount: an amount correction is a reversal plus a
    /// new payment). A stale stamp → 409, unless the payment already holds exactly the requested values (a retry).
    /// </summary>
    public async Task<PaymentDto> UpdateDetailsAsync(Guid paymentId, UpdatePaymentDetailsRequest request, CancellationToken ct)
    {
        var visible = await LoadScopedAsync(paymentId, ct);
        var actor = currentUser.Id;
        await RequireNotClientMemberAsync(visible.ClientAccountId, actor, ct);
        var reference = request.Reference?.Trim();
        if (reference is not null && reference.Length is 0 or > 120)
            throw new DomainException("billing.invalid_reference", "Enter the payment reference (1–120 characters).",
                errors: LineBuilder.Errors("reference", "Enter the payment reference."));
        var today = BillingDates.Today(clock);
        if (request.PaidOn is { } day && day > today.AddDays(1))
            throw new DomainException("billing.paid_on_in_future", "The payment date can't be in the future.",
                errors: LineBuilder.Errors("paidOn", "The payment date can't be in the future."));
        var notes = request.Notes is null ? null : request.Notes.Trim();

        bool Matches(Payment p) =>
            (reference is null || p.Reference == reference) && (request.PaidOn is null || p.PaidOn == request.PaidOn) &&
            (request.Method is null || p.Method == request.Method) && (notes is null || (p.Notes ?? string.Empty) == notes);

        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            if (!await dialect.LockRowAsync(db, "invoices", visible.InvoiceId, ct)) throw DomainException.NotFound("Invoice");
            var payment = await db.Set<Payment>().FirstAsync(p => p.Id == paymentId, ct);
            if (payment.IsReversal || payment.ReversedAt is not null)
                throw DomainException.Conflict("billing.payment_reversed", "A reversed payment (or a reversal) can't be edited.");
            if (request.ConcurrencyStamp != payment.ConcurrencyStamp)
            {
                if (!Matches(payment)) throw StalePayment();
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return await ToDtoAsync(paymentId, ct); // the same edit was already saved (retry / double click)
            }
            db.Entry(payment).Property(p => p.ConcurrencyStamp).OriginalValue = request.ConcurrencyStamp!.Value;
            // Same date rule as recording: an edit can't move a payment to before the invoice existed.
            if (request.PaidOn is { } newDate &&
                await db.Set<Invoice>().AsNoTracking().Where(i => i.Id == payment.InvoiceId).Select(i => i.IssueDate).FirstOrDefaultAsync(ct) is { } issued &&
                newDate < issued.AddYears(-1))
                throw new DomainException("billing.paid_on_invalid", "The payment date is too far before the invoice date.",
                    errors: LineBuilder.Errors("paidOn", "Check the payment date."));
            if (reference is not null && reference != payment.Reference &&
                await db.Set<Payment>().AnyAsync(p => p.InvoiceId == payment.InvoiceId && p.ActiveReference == reference && p.Id != paymentId, ct))
                throw DomainException.Conflict("billing.duplicate_reference", "A payment with this reference was already recorded on this invoice.");

            var before = new { payment.Reference, payment.PaidOn, payment.Method, payment.Notes };
            if (reference is not null)
            {
                payment.Reference = reference;
                payment.ActiveReference = reference;
            }
            if (request.PaidOn is { } paidOn) payment.PaidOn = paidOn;
            if (request.Method is { } method) payment.Method = method;
            if (notes is not null) payment.Notes = notes.Length == 0 ? null : notes;
            payment.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            payment.UpdatedByUserId = actor;
            audit.Record("billing.payment_updated", nameof(Payment), paymentId, before,
                new { payment.Reference, payment.PaidOn, payment.Method, payment.Notes, payment.InvoiceId }, request.Reason.Trim());
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw StalePayment();
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                throw DomainException.Conflict("billing.duplicate_reference", "A payment with this reference was already recorded on this invoice.");
            }
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        return await ToDtoAsync(paymentId, ct);
    }

    /// <summary>
    /// Reverses a payment — a refund to the client, or a payment recorded in error: adds a negative reversal row, marks
    /// the original reversed and restores the invoice balance (a Paid invoice reopens). Idempotent by <c>requestId</c>;
    /// a payment is reversed at most once (unique index). A refund (money leaves the agency) must be recorded by someone
    /// other than the person who recorded the payment (four-eyes); correcting one's own entry error is allowed and audited.
    /// </summary>
    public async Task<PaymentReversedDto> ReverseAsync(Guid paymentId, ReversePaymentRequest request, CancellationToken ct)
    {
        if (!request.Confirm)
            throw new DomainException("request.confirm_required", "Confirm this action by sending \"confirm\": true.");
        var requestId = request.RequestId!.Value;
        var kind = request.Kind!.Value;
        var reason = request.Reason.Trim();
        var visible = await LoadScopedAsync(paymentId, ct);
        var actor = currentUser.Id;
        await RequireNotClientMemberAsync(visible.ClientAccountId, actor, ct);
        var replay = await FindReversalReplayAsync(requestId, paymentId, kind, reason, ct);
        if (replay is not null) return replay;
        if (visible.IsReversal)
            throw DomainException.Conflict("billing.payment_is_reversal", "This row is itself a reversal and can't be reversed.");
        var today = BillingDates.Today(clock);
        var reversedOn = request.ReversedOn ?? today;
        if (reversedOn > today.AddDays(1))
            throw new DomainException("billing.paid_on_in_future", "The refund date can't be in the future.",
                errors: LineBuilder.Errors("reversedOn", "The date can't be in the future."));
        if (kind == PaymentReversalKind.Refund && visible.RecordedByUserId == actor)
            throw DomainException.Forbidden("billing.four_eyes", "You recorded this payment, so another finance user must record its refund.");

        Payment reversal;
        Guid invoiceId;
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            if (!await dialect.LockRowAsync(db, "invoices", visible.InvoiceId, ct)) throw DomainException.NotFound("Invoice");
            if (await db.Set<Payment>().AsNoTracking().AnyAsync(p => p.RequestId == requestId, ct))
            {
                await tx.RollbackAsync(ct);
                return await FindReversalReplayAsync(requestId, paymentId, kind, reason, ct)
                       ?? throw DomainException.Conflict("billing.request_id_reused", "This request id was already used for another payment.");
            }
            var payment = await db.Set<Payment>().FirstAsync(p => p.Id == paymentId, ct);
            if (payment.ReversedAt is not null)
                throw DomainException.Conflict("billing.payment_already_reversed", "This payment was already reversed.");
            if (request.ConcurrencyStamp != payment.ConcurrencyStamp) throw StalePayment();
            db.Entry(payment).Property(p => p.ConcurrencyStamp).OriginalValue = request.ConcurrencyStamp!.Value;
            var invoice = await db.Set<Invoice>().FirstAsync(i => i.Id == payment.InvoiceId, ct);
            invoiceId = invoice.Id;
            if (invoice.Status is InvoiceStatus.Void or InvoiceStatus.WrittenOff or InvoiceStatus.Draft)
                throw DomainException.Conflict("billing.invoice_not_reversible", $"Payments on a {invoice.Status} invoice can't be reversed.");

            var now = clock.GetUtcNow().UtcDateTime;
            reversal = new Payment
            {
                InvoiceId = payment.InvoiceId,
                ClientAccountId = payment.ClientAccountId,
                Amount = -payment.Amount,
                Currency = payment.Currency,
                Method = payment.Method,
                Reference = payment.Reference,
                ActiveReference = null,
                PaidOn = reversedOn,
                Notes = (kind == PaymentReversalKind.Refund ? "Refund: " : "Reversal: ") + reason,
                RequestId = requestId,
                RecordedByUserId = actor,
                CreatedAt = now,
                ReversalOfPaymentId = payment.Id,
                ReversalKind = kind,
                ReversalReason = reason,
            };
            db.Set<Payment>().Add(reversal);
            payment.ReversedAt = now;
            payment.ReversedByUserId = actor;
            payment.ReversalKind = kind;
            payment.ReversalReason = reason;
            payment.ActiveReference = null;

            var before = new { invoice.Status, invoice.AmountPaid, invoice.Balance };
            invoice.AmountPaid -= payment.Amount;
            invoice.RecalculateBalance(invoice.Currency);
            invoice.Status = invoice.SettlementStatus(today);
            if (invoice.Status != InvoiceStatus.Paid) invoice.PaidAt = null;
            var action = kind == PaymentReversalKind.Refund ? "billing.payment_refunded" : "billing.payment_reversed";
            audit.Record(action, nameof(Invoice), invoice.Id, before,
                new { invoice.Status, invoice.AmountPaid, invoice.Balance, PaymentId = payment.Id, payment.Amount, invoice.Currency, Kind = kind, ReversedOn = reversedOn },
                reason);
            audit.Record(action, nameof(Payment), payment.Id, new { Reversed = false },
                new { Reversed = true, Kind = kind, ReversalId = reversal.Id, payment.Amount, payment.Currency }, reason);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw StalePayment();
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                throw DomainException.Conflict("billing.payment_already_reversed", "This payment was already reversed.");
            }
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        var dto = await invoices.GetAsync(invoiceId, ct);
        return new PaymentReversedDto(dto.Payments.First(p => p.Id == paymentId), dto.Payments.First(p => p.Id == reversal.Id), dto, Replayed: false);
    }

    private async Task<PaymentReversedDto?> FindReversalReplayAsync(Guid requestId, Guid paymentId, PaymentReversalKind kind, string reason,
        CancellationToken ct)
    {
        var existing = await db.Set<Payment>().AsNoTracking().FirstOrDefaultAsync(p => p.RequestId == requestId, ct);
        if (existing is null) return null;
        // A retry must be the same request: a refund retried as an "error" reversal (or with another reason) is refused,
        // never reported as done.
        if (existing.ReversalOfPaymentId != paymentId || existing.ReversalKind != kind || existing.ReversalReason != reason)
            throw DomainException.Conflict("billing.request_id_reused",
                "This request id was already used for a different payment. Start a new request instead.");
        var dto = await invoices.GetAsync(existing.InvoiceId, ct);
        return new PaymentReversedDto(dto.Payments.First(p => p.Id == paymentId), dto.Payments.First(p => p.Id == existing.Id), dto, Replayed: true);
    }

    private async Task<PaymentDto> ToDtoAsync(Guid paymentId, CancellationToken ct)
    {
        var row = await db.Set<Payment>().AsNoTracking().FirstAsync(p => p.Id == paymentId, ct);
        return (await ToDtosAsync(new[] { row }, ct))[0];
    }
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
