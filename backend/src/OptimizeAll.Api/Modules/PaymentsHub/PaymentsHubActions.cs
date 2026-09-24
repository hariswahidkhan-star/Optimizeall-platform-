using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Billing;
using PayoutPaymentService = OptimizeAll.Api.Modules.Payouts.PayoutPaymentService;
using BulkPaymentLine = OptimizeAll.Api.Modules.Payouts.BulkPaymentLine;
using BulkPaymentResultDto = OptimizeAll.Api.Modules.Payouts.BulkPaymentResultDto;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.PaymentsHub;

/// <summary>
/// Manual payment actions of the hub. It owns no money logic: every write goes through the Billing
/// (<see cref="PaymentService"/>) or Payouts (<see cref="PayoutPaymentService"/>) services, so their locks, concurrency
/// stamps, idempotency keys, four-eyes rules, audit entries, notifications and events apply unchanged. What it adds is
/// retry-safety for the payout paths (a repeated request with the same data answers "replayed" instead of 409).
/// </summary>
public sealed class PaymentsHubActions(
    AppDbContext db,
    ICurrentUser currentUser,
    PaymentService payments,
    InvoiceService invoices,
    PayoutPaymentService payouts,
    PaymentsHubQueries queries)
{
    // ------------------------------------------------------------------ Incoming (invoices)

    public async Task<PaymentRecordedDto> RecordInvoicePaymentAsync(Guid invoiceId, RecordInvoicePaymentRequest r, CancellationToken ct) =>
        await payments.RecordAsync(invoiceId, new RecordPaymentRequest
        {
            RequestId = r.RequestId, Amount = r.Amount, Method = r.Method, Reference = r.Reference, PaidOn = r.PaidOn, Notes = r.Notes,
            ConcurrencyStamp = r.ConcurrencyStamp,
        }, ct);

    /// <summary>
    /// Records the remaining balance as one payment. A retry with the same <c>requestId</c> returns the original payment
    /// (checked first: after the first attempt the balance is 0). The balance the user saw must still be the balance.
    /// </summary>
    public async Task<PaymentRecordedDto> MarkInvoicePaidAsync(Guid invoiceId, MarkInvoicePaidRequest r, CancellationToken ct)
    {
        var invoice = await invoices.LoadScopedAsync(invoiceId, ct);
        var existing = await db.Set<Payment>().AsNoTracking().FirstOrDefaultAsync(p => p.RequestId == r.RequestId, ct);
        var amount = existing?.Amount ?? invoice.Balance;
        if (existing is null)
        {
            if (!Invoice.IsOpen(invoice.Status) || invoice.Balance <= 0)
                throw DomainException.Conflict("billing.invoice_not_open",
                    invoice.Status == InvoiceStatus.Paid ? "This invoice is already paid in full." : $"Payments can't be recorded on a {invoice.Status} invoice.");
            if (r.ExpectedBalance is { } expected && invoice.Balance != expected)
                throw DomainException.Conflict("concurrency.conflict",
                    $"The balance changed to {invoice.Balance} {invoice.Currency} meanwhile. Reload and try again.");
        }
        return await payments.RecordAsync(invoiceId, new RecordPaymentRequest
        {
            RequestId = r.RequestId, Amount = amount, Method = r.Method, Reference = r.Reference, PaidOn = r.PaidOn,
            Notes = string.IsNullOrWhiteSpace(r.Notes) ? "Marked as paid in full" : r.Notes, ConcurrencyStamp = r.ConcurrencyStamp,
        }, ct);
    }

    public Task<PaymentDto> EditInvoicePaymentAsync(Guid paymentId, UpdatePaymentDetailsRequest r, CancellationToken ct) =>
        payments.UpdateDetailsAsync(paymentId, r, ct);

    public Task<PaymentReversedDto> ReverseInvoicePaymentAsync(Guid paymentId, ReversePaymentRequest r, CancellationToken ct) =>
        payments.ReverseAsync(paymentId, r, ct);

    // ------------------------------------------------------------------ Outgoing (payouts)

    private async Task<PayoutItem> LoadItemAsync(Guid itemId, CancellationToken ct) =>
        await db.Set<PayoutItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct) ?? throw DomainException.NotFound("PayoutItem");

    private async Task<PayoutActionResultDto> ResultAsync(Guid itemId, bool replayed, bool requeued, CancellationToken ct)
    {
        var record = (await queries.PayoutRecordsAsync(new[] { itemId }, ct)).First();
        var batchStatus = await (from i in db.Set<PayoutItem>() join b in db.Set<PayoutBatch>() on i.BatchId equals b.Id
                                 where i.Id == itemId select b.Status).FirstAsync(ct);
        return new PayoutActionResultDto(record, batchStatus.ToString(), replayed, requeued);
    }

    /// <summary>
    /// Records a manual payout payment through the Payouts flow (four-eyes for system-prepared batches, hold override,
    /// conditional AwaitingPayment → Paid). The same reference sent again for an already paid item is a replay.
    /// </summary>
    public async Task<PayoutActionResultDto> MarkPayoutPaidAsync(Guid itemId, MarkPayoutPaidRequest r, CancellationToken ct)
    {
        var item = await LoadItemAsync(itemId, ct);
        var reference = r.PaymentReference.Trim();
        if (item.Status == PayoutItemStatus.Paid && item.PaymentReference == reference)
            return await ResultAsync(itemId, replayed: true, requeued: false, ct);
        try
        {
            await payouts.RecordPaymentAsync(item.BatchId, itemId, reference, r.PaidAt!.Value, r.Note?.Trim(), currentUser.Id, ct, r.OverrideReason);
        }
        catch (DomainException ex) when (ex.Code == "payout.already_recorded")
        {
            db.ChangeTracker.Clear();
            var now = await LoadItemAsync(itemId, ct);
            if (now.PaymentReference != reference) throw; // someone else recorded a different payment: a real conflict
            return await ResultAsync(itemId, replayed: true, requeued: false, ct);
        }
        return await ResultAsync(itemId, replayed: false, requeued: false, ct);
    }

    /// <summary>
    /// Marks an item awaiting payment as failed or returned by the bank. Through the Payouts flow its earnings return to
    /// Approved, so they are re-queued automatically into the next batch.
    /// </summary>
    public async Task<PayoutActionResultDto> MarkPayoutFailedAsync(Guid itemId, MarkPayoutFailedRequest r, CancellationToken ct)
    {
        var item = await LoadItemAsync(itemId, ct);
        var reason = (r.Kind == PayoutFailureKind.Returned ? "Returned by the bank: " : "Failed: ") + r.Reason.Trim();
        if (item.Status == PayoutItemStatus.Failed && item.FailureReason == reason)
            return await ResultAsync(itemId, replayed: true, requeued: true, ct);
        try
        {
            await payouts.MarkFailedAsync(item.BatchId, itemId, reason, currentUser.Id, ct);
        }
        catch (DomainException ex) when (ex.Code == "payout.item_state")
        {
            db.ChangeTracker.Clear();
            var now = await LoadItemAsync(itemId, ct);
            if (now.Status != PayoutItemStatus.Failed || now.FailureReason != reason) throw;
            return await ResultAsync(itemId, replayed: true, requeued: true, ct);
        }
        return await ResultAsync(itemId, replayed: false, requeued: true, ct);
    }

    /// <summary>
    /// Marks every item of a finalized batch that is awaiting payment as paid with one bank reference, through the
    /// Payouts bulk flow (each line independently: recorded / already_recorded / invalid, same rules as a single item).
    /// </summary>
    public async Task<BatchPaidResultDto> MarkBatchPaidAsync(Guid batchId, MarkBatchPaidRequest r, CancellationToken ct)
    {
        if (!r.Confirm)
            throw new DomainException("request.confirm_required", "Confirm this action by sending \"confirm\": true.");
        var batch = await db.Set<PayoutBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct) ?? throw DomainException.NotFound("PayoutBatch");
        if (batch.Status != PayoutBatchStatus.Finalized)
            throw DomainException.Conflict("payout.batch_not_finalized", "Only a finalized batch with items awaiting payment can be marked paid.");
        var itemIds = await db.Set<PayoutItem>().AsNoTracking()
            .Where(i => i.BatchId == batchId && i.Status == PayoutItemStatus.AwaitingPayment).Select(i => i.Id).ToListAsync(ct);
        var lines = itemIds.Select(id => new BulkPaymentLine { ItemId = id, PaymentReference = r.PaymentReference.Trim(), PaidAt = r.PaidAt }).ToList();
        var results = lines.Count == 0 ? new List<BulkPaymentResultDto>() : (await payouts.RecordBulkAsync(batchId, lines, currentUser.Id, ct)).ToList();
        var status = await db.Set<PayoutBatch>().AsNoTracking().Where(b => b.Id == batchId).Select(b => b.Status).FirstAsync(ct);
        return new BatchPaidResultDto(batchId, status.ToString(), results.Count(x => x.Status == "recorded"),
            results.Count(x => x.Status == "already_recorded"), results.Count(x => x.Status == "invalid"),
            results.Select(x => new BatchPaidLineDto(x.ItemId, x.Status, x.Message)).ToList());
    }
}
