using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Modules.Payouts.Providers;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Payouts;

/// <summary>
/// Records the outcome of paying a payout item. Every path (manual entry, bulk entry, provider confirmation) goes
/// through the same conditional update AwaitingPayment → Paid/Failed, so an item can never be paid twice even when
/// two finance users, a retried request and a provider webhook race.
/// </summary>
public sealed class PayoutPaymentService(
    AppDbContext db,
    IAuditLogger audit,
    INotificationService notifications,
    IEventPublisher events,
    TimeProvider clock)
{
    /// <summary>Tolerated client clock skew for <c>paidAt</c>.</summary>
    public static readonly TimeSpan PaidAtSkew = TimeSpan.FromMinutes(5);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>AwaitingPayment → Paid. <c>actor</c> is the finance user recording it, or null when a provider confirmed it.</summary>
    public async Task<PaymentRecordedDto> RecordPaymentAsync(
        Guid batchId, Guid itemId, string paymentReference, DateTime paidAt, string? note, Guid? actor, CancellationToken ct)
    {
        paymentReference = paymentReference.Trim();
        if (paymentReference.Length is < 3 or > 120)
            throw new DomainException("payout.invalid_reference", "The payment reference must be 3–120 characters.");
        paidAt = paidAt.ToUniversalTime();
        var now = Now;
        if (paidAt > now.Add(PaidAtSkew))
            throw new DomainException("payout.paid_at_in_future", "The payment date cannot be in the future.");

        PayoutItem item;
        PayoutBatchStatus batchStatus;
        await using (var tx = await PayoutStore.BeginAsync(db, ct))
        {
            var status = await PayoutStore.LockBatchAsync(db, batchId, ct) ?? throw DomainException.NotFound("PayoutBatch");
            item = await db.Set<PayoutItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.BatchId == batchId, ct)
                   ?? throw DomainException.NotFound("PayoutItem");
            if (item.Status == PayoutItemStatus.Paid)
                throw DomainException.Conflict("payout.already_recorded", "A payment was already recorded for this item.");
            if (status != PayoutBatchStatus.Finalized)
                throw DomainException.Conflict("payout.batch_not_finalized", "Payments can only be recorded for finalized batches.");

            var updated = await db.Set<PayoutItem>()
                .Where(i => i.Id == itemId && i.Status == PayoutItemStatus.AwaitingPayment)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, PayoutItemStatus.Paid)
                    .SetProperty(i => i.PaymentReference, paymentReference)
                    .SetProperty(i => i.PaidAt, paidAt)
                    .SetProperty(i => i.RecordedByUserId, actor)
                    .SetProperty(i => i.UpdatedAt, now)
                    .SetProperty(i => i.ConcurrencyStamp, Guid.NewGuid()), ct);
            if (updated == 0)
                throw DomainException.Conflict("payout.item_not_awaiting_payment",
                    $"This item is {item.Status}; only items awaiting payment can be recorded as paid.");

            var paidEarnings = await db.Set<EarningEntry>()
                .Where(e => e.PayoutItemId == itemId && e.Status == EarningStatus.Scheduled)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EarningStatus.Paid)
                    .SetProperty(e => e.PaidAt, paidAt).SetProperty(e => e.ConcurrencyStamp, Guid.NewGuid()), ct);
            if (paidEarnings != item.EarningCount)
                throw DomainException.Conflict("payout.earnings_changed",
                    $"Expected {item.EarningCount} scheduled earnings for this item but found {paidEarnings}. Nothing was recorded; run reconciliation.");

            await UpsertAttemptAsync(item, PaymentAttemptStatus.Succeeded, paymentReference,
                actor is null ? "Confirmed by payment provider" : "Payment recorded by finance", ct);

            audit.Record("payout.payment_recorded", nameof(PayoutItem), itemId,
                before: new { Status = PayoutItemStatus.AwaitingPayment },
                after: new { Status = PayoutItemStatus.Paid, item.Amount, item.Currency, PaymentReference = paymentReference, PaidAt = paidAt, BatchId = batchId },
                reason: note);
            await notifications.StageAsync(new NotificationRequest(item.UserId, NotificationTypes.PayoutPaid,
                "Your payout was sent",
                $"We sent your payout of {item.Amount:0.00} {item.Currency}. Payment reference ending {Tail(paymentReference)}.",
                "/earnings/payouts", new[] { NotificationChannel.Email }), ct);
            await db.SaveChangesAsync(ct);
            await PayoutStore.CompleteIfDoneAsync(db, batchId, now, ct);
            batchStatus = await db.Set<PayoutBatch>().AsNoTracking().Where(b => b.Id == batchId).Select(b => b.Status).FirstAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();

        await events.PublishAsync(new PayoutItemPaid(item.Id, item.UserId, item.Amount, item.Currency, now), ct);
        return new PaymentRecordedDto(await PayoutReadModels.ItemAsync(db, itemId, ct), batchStatus);
    }

    /// <summary>AwaitingPayment → Failed; the item's earnings return to Approved and roll into the next batch.</summary>
    public async Task<PaymentRecordedDto> MarkFailedAsync(Guid batchId, Guid itemId, string reason, Guid? actor, CancellationToken ct)
    {
        var now = Now;
        PayoutBatchStatus batchStatus;
        await using (var tx = await PayoutStore.BeginAsync(db, ct))
        {
            var status = await PayoutStore.LockBatchAsync(db, batchId, ct) ?? throw DomainException.NotFound("PayoutBatch");
            var item = await db.Set<PayoutItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.BatchId == batchId, ct)
                       ?? throw DomainException.NotFound("PayoutItem");
            if (status != PayoutBatchStatus.Finalized)
                throw DomainException.Conflict("payout.batch_not_finalized", "Only items of finalized batches can be marked failed.");

            var updated = await db.Set<PayoutItem>()
                .Where(i => i.Id == itemId && i.Status == PayoutItemStatus.AwaitingPayment)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, PayoutItemStatus.Failed)
                    .SetProperty(i => i.FailureReason, reason).SetProperty(i => i.RecordedByUserId, actor)
                    .SetProperty(i => i.UpdatedAt, now).SetProperty(i => i.ConcurrencyStamp, Guid.NewGuid()), ct);
            if (updated == 0)
                throw item.Status == PayoutItemStatus.Paid
                    ? DomainException.Conflict("payout.already_recorded", "A payment was already recorded for this item.")
                    : DomainException.Conflict("payout.item_state", $"This item is {item.Status}; only items awaiting payment can fail.");

            await PayoutStore.ReleaseEarningsAsync(db, new[] { itemId }, ct);
            await UpsertAttemptAsync(item, PaymentAttemptStatus.Failed, null, reason, ct);
            audit.Record("payout.payment_failed", nameof(PayoutItem), itemId,
                before: new { Status = PayoutItemStatus.AwaitingPayment },
                after: new { Status = PayoutItemStatus.Failed, item.Amount, item.Currency, BatchId = batchId }, reason: reason);
            await notifications.StageAsync(new NotificationRequest(item.UserId, NotificationTypes.PayoutScheduled,
                "We couldn't complete your payout",
                $"Your payout of {item.Amount:0.00} {item.Currency} could not be completed. Please check your payout details; " +
                "your earnings are safe and will be included in the next payout.",
                "/settings/payout", new[] { NotificationChannel.Email }), ct);
            await db.SaveChangesAsync(ct);
            await PayoutStore.CompleteIfDoneAsync(db, batchId, now, ct);
            batchStatus = await db.Set<PayoutBatch>().AsNoTracking().Where(b => b.Id == batchId).Select(b => b.Status).FirstAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        return new PaymentRecordedDto(await PayoutReadModels.ItemAsync(db, itemId, ct), batchStatus);
    }

    /// <summary>Each line is processed independently with the same atomic rules as a single record-payment.</summary>
    public async Task<IReadOnlyList<BulkPaymentResultDto>> RecordBulkAsync(
        Guid batchId, IReadOnlyList<BulkPaymentLine> lines, Guid actor, CancellationToken ct)
    {
        var results = new List<BulkPaymentResultDto>(lines.Count);
        var seen = new HashSet<Guid>();
        foreach (var line in lines)
        {
            var itemId = line.ItemId!.Value;
            if (!seen.Add(itemId))
            {
                results.Add(new BulkPaymentResultDto(itemId, "invalid", "Duplicate line for this item in the request."));
                continue;
            }
            if (line.PaidAt is null || string.IsNullOrWhiteSpace(line.PaymentReference))
            {
                results.Add(new BulkPaymentResultDto(itemId, "invalid", "paymentReference and paidAt are required."));
                continue;
            }
            try
            {
                await RecordPaymentAsync(batchId, itemId, line.PaymentReference, line.PaidAt.Value, null, actor, ct);
                results.Add(new BulkPaymentResultDto(itemId, "recorded", "Payment recorded."));
            }
            catch (DomainException ex) when (ex.Code == "payout.already_recorded")
            {
                results.Add(new BulkPaymentResultDto(itemId, "already_recorded", ex.Message));
            }
            catch (DomainException ex)
            {
                results.Add(new BulkPaymentResultDto(itemId, "invalid", ex.Message));
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
        return results;
    }

    private async Task UpsertAttemptAsync(PayoutItem item, PaymentAttemptStatus status, string? reference, string message, CancellationToken ct)
    {
        var key = PaymentDispatcher.DispatchKey(item.Id);
        var attempt = await db.Set<PaymentAttempt>().FirstOrDefaultAsync(a => a.IdempotencyKey == key, ct);
        if (attempt is null)
        {
            db.Set<PaymentAttempt>().Add(new PaymentAttempt
            {
                PayoutItemId = item.Id,
                Provider = item.PaymentProvider,
                IdempotencyKey = key,
                Status = status,
                ProviderReference = reference,
                Message = message,
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            return;
        }
        attempt.Status = status;
        attempt.ProviderReference = reference ?? attempt.ProviderReference;
        attempt.Message = message;
        attempt.UpdatedAt = Now;
    }

    private static string Tail(string reference) => reference.Length <= 4 ? reference : reference[^4..];
}
