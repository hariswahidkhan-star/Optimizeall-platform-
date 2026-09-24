using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.PaymentsHub;

public sealed record ClaimReviewedDto(PaymentClaimDto Claim, PaymentRecordedDto? Payment, bool Replayed);

/// <summary>
/// "I've paid": a client's Billing/Owner member reports a transfer; staff with <c>billing.manage</c> confirm it (which
/// records the payment through <see cref="PaymentService"/> with the claim id as the idempotency key, so a retried or
/// concurrent confirmation records it once) or reject it. The invoice balance only changes on confirmation.
/// </summary>
public sealed class PaymentClaimService(
    AppDbContext db,
    IDatabaseDialect dialect,
    IClientScope scope,
    ICurrentUser currentUser,
    IAuditLogger audit,
    INotificationService notifications,
    ClientBillingService clientBilling,
    PaymentService payments,
    TimeProvider clock)
{
    public const string ClaimSubmittedType = "billing.payment_claimed";
    public const string ClaimReviewedType = "billing.payment_claim_reviewed";

    /// <summary>At most this many unconfirmed reports per invoice (a client can't flood finance).</summary>
    public const int MaxPendingPerInvoice = 5;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ Client side

    public async Task<ClientInvoicePaymentsDto> ClientInvoicePaymentsAsync(Guid invoiceId, CancellationToken ct)
    {
        var invoice = await clientBilling.LoadInvoiceAsync(invoiceId, ct);
        var rows = await db.Set<Payment>().AsNoTracking().Where(p => p.InvoiceId == invoiceId && p.ReversalOfPaymentId == null)
            .OrderBy(p => p.PaidOn).ThenBy(p => p.CreatedAt).ToListAsync(ct);
        var claims = await db.Set<PaymentClaim>().AsNoTracking().Where(c => c.InvoiceId == invoiceId).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        return new ClientInvoicePaymentsDto(invoice.Id, invoice.Number, invoice.Status, invoice.Currency, invoice.Total, invoice.AmountPaid,
            invoice.AmountCredited, invoice.Balance,
            rows.Select(p => new ClientInvoicePaymentDto(p.Id, p.Amount, p.Currency, p.Method, p.Reference, p.PaidOn,
                p.ReversedAt is null ? "Paid" : p.ReversalKind == PaymentReversalKind.Refund ? "Refunded" : "Reversed", p.ReversedAt)).ToList(),
            await ToDtosAsync(claims, includeReviewNote: true, ct),
            Invoice.IsOpen(invoice.Status) && invoice.Balance > 0);
    }

    public async Task<(PaymentClaimDto Claim, bool Replayed)> SubmitAsync(Guid invoiceId, SubmitPaymentClaimRequest r, CancellationToken ct)
    {
        var invoice = await clientBilling.LoadInvoiceAsync(invoiceId, ct);
        var requestId = r.RequestId!.Value;
        var reference = r.Reference.Trim();
        var replay = await ReplayAsync(requestId, invoiceId, r.Amount, reference, ct);
        if (replay is not null) return (replay, true);

        if (!Invoice.IsOpen(invoice.Status) || invoice.Balance <= 0)
            throw DomainException.Conflict("billing.invoice_not_open", "This invoice has nothing left to pay.");
        if (r.Amount <= 0 || Money.Round(r.Amount, invoice.Currency) != r.Amount)
            throw new DomainException("billing.invalid_amount",
                $"Enter an amount greater than 0 with at most {Money.MinorUnitDigits(invoice.Currency)} decimals for {invoice.Currency}.",
                errors: LineBuilder.Errors("amount", "Enter a valid amount."));
        if (r.Amount > invoice.Balance)
            throw DomainException.Conflict("billing.overpayment",
                $"The amount is more than the outstanding balance ({invoice.Balance} {invoice.Currency}).");
        var today = DateOnly.FromDateTime(Now);
        var paidOn = r.PaidOn!.Value;
        if (paidOn > today.AddDays(1) || (invoice.IssueDate is { } issued && paidOn < issued.AddYears(-1)))
            throw new DomainException("billing.paid_on_invalid", "Check the payment date (it can't be in the future).",
                errors: LineBuilder.Errors("paidOn", "Check the payment date."));
        var pending = await db.Set<PaymentClaim>().AsNoTracking()
            .Where(c => c.InvoiceId == invoiceId && c.Status == PaymentClaimStatus.Pending).Select(c => c.Reference).ToListAsync(ct);
        if (pending.Contains(reference))
            throw DomainException.Conflict("payments.claim_duplicate", "You already reported a payment with this reference. We'll confirm it shortly.");
        if (pending.Count >= MaxPendingPerInvoice)
            throw DomainException.Conflict("payments.too_many_claims", "Several payment reports are already waiting for confirmation on this invoice.");

        var claim = new PaymentClaim
        {
            InvoiceId = invoiceId,
            ClientAccountId = invoice.ClientAccountId,
            SubmittedByUserId = currentUser.Id,
            Amount = r.Amount,
            Currency = invoice.Currency,
            Method = r.Method,
            Reference = reference,
            PaidOn = paidOn,
            Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim(),
            RequestId = requestId,
        };
        db.Set<PaymentClaim>().Add(claim);
        audit.Record("payments.claim_submitted", nameof(PaymentClaim), claim.Id,
            after: new { InvoiceId = invoiceId, invoice.Number, claim.Amount, claim.Currency, claim.Method, claim.Reference, claim.PaidOn });
        var client = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == invoice.ClientAccountId).Select(c => new { c.Name, c.AccountManagerUserId })
            .FirstAsync(ct);
        foreach (var staff in await StaffToNotifyAsync(client.AccountManagerUserId, ct))
            await notifications.StageAsync(new NotificationRequest(staff, ClaimSubmittedType,
                $"{client.Name} reported a payment for {invoice.Number}",
                $"{client.Name} says they paid {Format(claim.Amount, claim.Currency)} on {claim.PaidOn:d MMM yyyy} (reference {claim.Reference}). " +
                "Check the bank statement, then confirm or reject it under Finance → Payments.",
                AppLinks.FinancePayments, new[] { NotificationChannel.Email }), ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return (await ReplayAsync(requestId, invoiceId, r.Amount, reference, ct)
                    ?? throw DomainException.Conflict("payments.claim_duplicate", "This payment report was already submitted."), true);
        }
        db.ChangeTracker.Clear();
        return ((await ToDtosAsync(new[] { claim }, includeReviewNote: true, ct))[0], false);
    }

    private async Task<PaymentClaimDto?> ReplayAsync(Guid requestId, Guid invoiceId, decimal amount, string reference, CancellationToken ct)
    {
        var existing = await db.Set<PaymentClaim>().AsNoTracking().FirstOrDefaultAsync(c => c.RequestId == requestId, ct);
        if (existing is null) return null;
        if (existing.InvoiceId != invoiceId || existing.Amount != amount || existing.Reference != reference || existing.SubmittedByUserId != currentUser.Id)
            throw DomainException.Conflict("billing.request_id_reused", "This request id was already used for a different payment report.");
        return (await ToDtosAsync(new[] { existing }, includeReviewNote: true, ct))[0];
    }

    /// <summary>The client's own claim (a Billing/Owner member of its organization), or 404.</summary>
    public async Task<PaymentClaim> LoadForClientAsync(Guid claimId, CancellationToken ct)
    {
        var ids = await clientBilling.BillingClientIdsAsync(ct);
        return await db.Set<PaymentClaim>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == claimId && ids.Contains(c.ClientAccountId), ct)
               ?? throw DomainException.NotFound("PaymentClaim");
    }

    // ------------------------------------------------------------------ Staff side

    public async Task<PaymentClaim> LoadScopedAsync(Guid claimId, CancellationToken ct) =>
        await (await scope.ApplyAsync(db.Set<PaymentClaim>().AsNoTracking(), c => c.ClientAccountId, ct)).FirstOrDefaultAsync(c => c.Id == claimId, ct)
        ?? throw DomainException.NotFound("PaymentClaim");

    public async Task<PagedResult<PaymentClaimDto>> ListAsync(PaymentClaimQuery q, CancellationToken ct)
    {
        var rows = await scope.ApplyAsync(db.Set<PaymentClaim>().AsNoTracking(), c => c.ClientAccountId, ct);
        if (q.Status is { } status) rows = rows.Where(c => c.Status == status);
        if (q.InvoiceId is { } invoiceId) rows = rows.Where(c => c.InvoiceId == invoiceId);
        var total = await rows.CountAsync(ct);
        var page = await rows.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).Skip(q.Skip).Take(q.PageSize).ToListAsync(ct);
        return new PagedResult<PaymentClaimDto>(await ToDtosAsync(page, includeReviewNote: true, ct), total, q.Page, q.PageSize);
    }

    /// <summary>
    /// Confirming and rejecting one claim are serialized by this named lock (taken before any transaction) and the claim
    /// is re-read under it. Otherwise a reject committing between "payment recorded" and "claim confirmed" leaves a
    /// payment on the invoice for a report the client was told had been rejected.
    /// </summary>
    private static string ClaimLock(Guid claimId) => $"oa:payment-claim:{claimId:N}";

    private static readonly TimeSpan ClaimLockTimeout = TimeSpan.FromSeconds(15);

    public async Task<ClaimReviewedDto> ConfirmAsync(Guid claimId, ConfirmPaymentClaimRequest r, CancellationToken ct)
    {
        await LoadScopedAsync(claimId, ct); // tenancy: 404 for a claim the caller may not see
        await using (await dialect.AcquireNamedLockAsync(db, ClaimLock(claimId), ClaimLockTimeout, ct))
            return await ConfirmLockedAsync(claimId, r, ct);
    }

    private async Task<ClaimReviewedDto> ConfirmLockedAsync(Guid claimId, ConfirmPaymentClaimRequest r, CancellationToken ct)
    {
        var claim = await db.Set<PaymentClaim>().AsNoTracking().FirstAsync(c => c.Id == claimId, ct);
        if (claim.Status == PaymentClaimStatus.Rejected)
            throw DomainException.Conflict("payments.claim_rejected", "This payment report was rejected; ask the client to report it again.");
        var alreadyConfirmed = claim.Status == PaymentClaimStatus.Confirmed;
        // The claim id is the payment's idempotency key: a retry or a second confirmer replays the same payment.
        var recorded = await payments.RecordAsync(claim.InvoiceId, new RecordPaymentRequest
        {
            RequestId = claim.Id,
            Amount = r.Amount ?? claim.Amount,
            Method = claim.Method,
            Reference = claim.Reference,
            PaidOn = r.PaidOn ?? claim.PaidOn,
            Notes = "Client payment report confirmed." + (string.IsNullOrWhiteSpace(r.Notes) ? string.Empty : " " + r.Notes.Trim()),
            ConcurrencyStamp = r.InvoiceConcurrencyStamp,
        }, ct);
        var paymentId = recorded.Payment.Id;

        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            var updated = await db.Set<PaymentClaim>().Where(c => c.Id == claimId && c.Status == PaymentClaimStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, PaymentClaimStatus.Confirmed).SetProperty(c => c.PaymentId, paymentId)
                    .SetProperty(c => c.ReviewedAt, Now).SetProperty(c => c.ReviewedByUserId, currentUser.Id)
                    .SetProperty(c => c.UpdatedAt, Now).SetProperty(c => c.ConcurrencyStamp, Guid.NewGuid()), ct);
            if (updated == 1)
            {
                // Proofs the client attached now also belong to the payment.
                await db.Set<PaymentProof>().Where(f => f.PaymentClaimId == claimId && f.PaymentId == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(f => f.PaymentId, paymentId), ct);
                audit.Record("payments.claim_confirmed", nameof(PaymentClaim), claimId, new { Status = PaymentClaimStatus.Pending },
                    new { Status = PaymentClaimStatus.Confirmed, PaymentId = paymentId, recorded.Payment.Amount, recorded.Payment.Currency });
                await notifications.StageAsync(new NotificationRequest(claim.SubmittedByUserId, ClaimReviewedType,
                    $"Payment confirmed for invoice {recorded.Invoice.Number}",
                    $"We received your payment of {Format(recorded.Payment.Amount, recorded.Payment.Currency)} (reference {claim.Reference}). Thank you!",
                    BillingLinks.ClientInvoice(claim.InvoiceId), new[] { NotificationChannel.Email }), ct);
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
            if (updated == 0) alreadyConfirmed = true;
        }
        db.ChangeTracker.Clear();
        var current = await db.Set<PaymentClaim>().AsNoTracking().FirstAsync(c => c.Id == claimId, ct);
        if (current.PaymentId != paymentId)
            throw DomainException.Conflict("payments.claim_state", $"This payment report is {current.Status}.");
        return new ClaimReviewedDto((await ToDtosAsync(new[] { current }, includeReviewNote: true, ct))[0], recorded,
            Replayed: alreadyConfirmed || recorded.Replayed);
    }

    public async Task<ClaimReviewedDto> RejectAsync(Guid claimId, RejectPaymentClaimRequest r, CancellationToken ct)
    {
        await LoadScopedAsync(claimId, ct); // tenancy: 404 for a claim the caller may not see
        await using (await dialect.AcquireNamedLockAsync(db, ClaimLock(claimId), ClaimLockTimeout, ct))
            return await RejectLockedAsync(claimId, r, ct);
    }

    private async Task<ClaimReviewedDto> RejectLockedAsync(Guid claimId, RejectPaymentClaimRequest r, CancellationToken ct)
    {
        var claim = await db.Set<PaymentClaim>().AsNoTracking().FirstAsync(c => c.Id == claimId, ct);
        var reason = r.Reason.Trim();
        if (claim.Status == PaymentClaimStatus.Rejected && claim.ReviewNote == reason)
            return new ClaimReviewedDto((await ToDtosAsync(new[] { claim }, includeReviewNote: true, ct))[0], null, Replayed: true);
        if (claim.Status != PaymentClaimStatus.Pending)
            throw DomainException.Conflict("payments.claim_state", $"This payment report is already {claim.Status}.");
        // A confirmation that recorded the payment but stopped before closing the claim (crash, timeout): the money is on
        // the invoice, so the report can't be rejected; confirming it again finishes it (same idempotency key).
        if (await db.Set<Payment>().AsNoTracking().AnyAsync(p => p.RequestId == claimId && p.ReversedAt == null, ct))
            throw DomainException.Conflict("payments.claim_state",
                "A payment was already recorded for this report. Confirm it to close the report, or reverse the payment first.");
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            var stamp = r.ConcurrencyStamp!.Value;
            var updated = await db.Set<PaymentClaim>().Where(c => c.Id == claimId && c.Status == PaymentClaimStatus.Pending && c.ConcurrencyStamp == stamp)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, PaymentClaimStatus.Rejected).SetProperty(c => c.ReviewNote, reason)
                    .SetProperty(c => c.ReviewedAt, Now).SetProperty(c => c.ReviewedByUserId, currentUser.Id)
                    .SetProperty(c => c.UpdatedAt, Now).SetProperty(c => c.ConcurrencyStamp, Guid.NewGuid()), ct);
            if (updated == 0)
                throw DomainException.Conflict("concurrency.conflict", "This payment report was changed by someone else. Reload and try again.");
            audit.Record("payments.claim_rejected", nameof(PaymentClaim), claimId, new { Status = PaymentClaimStatus.Pending },
                new { Status = PaymentClaimStatus.Rejected }, reason);
            var number = await db.Set<Invoice>().Where(i => i.Id == claim.InvoiceId).Select(i => i.Number).FirstAsync(ct);
            await notifications.StageAsync(new NotificationRequest(claim.SubmittedByUserId, ClaimReviewedType,
                $"We couldn't match your payment for invoice {number}",
                $"We couldn't find your payment of {Format(claim.Amount, claim.Currency)} (reference {claim.Reference}): {reason}",
                BillingLinks.ClientInvoice(claim.InvoiceId), new[] { NotificationChannel.Email }), ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        var current = await db.Set<PaymentClaim>().AsNoTracking().FirstAsync(c => c.Id == claimId, ct);
        return new ClaimReviewedDto((await ToDtosAsync(new[] { current }, includeReviewNote: true, ct))[0], null, Replayed: false);
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>The client's account manager and every active Finance user (deduplicated).</summary>
    private async Task<IReadOnlyList<Guid>> StaffToNotifyAsync(Guid? accountManager, CancellationToken ct)
    {
        var finance = await (from r in db.Set<UserRole>().AsNoTracking()
                             join u in db.Set<User>().AsNoTracking() on r.UserId equals u.Id
                             where r.Role == Role.Finance && u.Status == UserStatus.Active
                             select u.Id).Take(50).ToListAsync(ct);
        if (accountManager is { } am && await db.Set<User>().AnyAsync(u => u.Id == am && u.Status == UserStatus.Active, ct))
            finance.Add(am);
        return finance.Distinct().ToList();
    }

    public async Task<IReadOnlyList<PaymentClaimDto>> ToDtosAsync(IReadOnlyList<PaymentClaim> rows, bool includeReviewNote, CancellationToken ct)
    {
        var invoiceIds = rows.Select(r => r.InvoiceId).Distinct().ToList();
        var numbers = await db.Set<Invoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Number, ct);
        var clientIds = rows.Select(r => r.ClientAccountId).Distinct().ToList();
        var clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var userIds = rows.Select(r => r.SubmittedByUserId).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var ids = rows.Select(r => r.Id).ToList();
        var withProof = await db.Set<PaymentProof>().AsNoTracking().Where(f => f.PaymentClaimId != null && ids.Contains(f.PaymentClaimId.Value))
            .Select(f => f.PaymentClaimId!.Value).Distinct().ToListAsync(ct);
        return rows.Select(c => new PaymentClaimDto(c.Id, c.InvoiceId, numbers.GetValueOrDefault(c.InvoiceId), c.ClientAccountId,
            clients.GetValueOrDefault(c.ClientAccountId, "—"), users.GetValueOrDefault(c.SubmittedByUserId, "—"), c.Amount, c.Currency, c.Method,
            c.Reference, c.PaidOn, c.Note, c.Status, c.CreatedAt, c.ReviewedAt, includeReviewNote ? c.ReviewNote : null, c.PaymentId,
            withProof.Contains(c.Id), c.ConcurrencyStamp)).ToList();
    }

    private static string Format(decimal amount, string currency) =>
        $"{amount.ToString("N" + Money.MinorUnitDigits(currency), System.Globalization.CultureInfo.InvariantCulture)} {currency}";
}
