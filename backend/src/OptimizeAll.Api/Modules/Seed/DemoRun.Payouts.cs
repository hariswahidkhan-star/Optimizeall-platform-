using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Modules.Payouts;
using OptimizeAll.Api.Modules.Payouts.Providers;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Payouts;

namespace OptimizeAll.Api.Modules.Seed;

internal sealed partial class DemoRun
{
    private readonly Dictionary<string, Guid> _batches = new(); // period key → batch id
    private int _paymentSeq;

    /// <summary>When finance prepares the batch of a period: the morning after its cutoff (the latest period: at most "now").</summary>
    private DateTime PrepareTime(PayoutPeriod period)
    {
        var at = period.CutoffUtc.AddHours(9);
        if (period.PeriodKey == _p0.PeriodKey && at > _now.AddMinutes(-30))
            at = new[] { _now.AddMinutes(-30), period.CutoffUtc.AddMinutes(1) }.Max();
        return at;
    }

    private void PlanPayoutOperations()
    {
        // Two past biweekly batches: prepared by finance1, finalized by finance2 (four-eyes), fully paid.
        foreach (var period in new[] { _p3, _p2 })
        {
            var p = period;
            var prepared = PrepareTime(p);
            At(prepared, () => PrepareBatchAsync(p, Finance1, null));
            At(prepared.AddHours(26), () => FinalizeBatchAsync(p, Finance2, "Totals and exclusions reviewed against the ledger."));
            At(PaymentTime(p), () => RecordPaymentsAsync(p, payShare: 1.0, failOne: p == _p2));
        }

        // The previous period: finalized, about half of the items paid so far.
        var prepared1 = PrepareTime(_p1);
        At(prepared1, () => PrepareBatchAsync(_p1, Finance1, null));
        At(prepared1.AddHours(26), () => FinalizeBatchAsync(_p1, Finance2, "Reviewed; two participants excluded by payout hold / minimum."));
        At(PaymentTime(_p1), () => RecordPaymentsAsync(_p1, payShare: 0.5, failOne: false));

        // The last completed period: a draft ready for the finance demo (finance2 finalizes it).
        At(PrepareTime(_p0), () => PrepareBatchAsync(_p0, Finance1, "Prepared for review — please check the exclusions before finalizing."));
    }

    private static DateTime PaymentTime(PayoutPeriod period) =>
        DateTime.SpecifyKind(period.PaymentDate.ToDateTime(new TimeOnly(10, 30)), DateTimeKind.Utc);

    // ------------------------------------------------------------------ prepare (PayoutBatchService.PrepareAsync)

    private async Task PrepareBatchAsync(PayoutPeriod period, DemoPerson preparer, string? note)
    {
        var schedule = await _schedules.GetActiveAsync(Now);
        var currency = schedule.SettlementCurrency;
        var key = PayoutBatchService.IdempotencyKeyFor(period.PeriodKey, currency);
        if (await _db.Set<PayoutBatch>().AnyAsync(b => b.IdempotencyKey == key))
        {
            _logger.LogWarning("Demo seed: a payout batch for period {Period} already exists; not creating a demo batch", period.PeriodKey);
            return;
        }

        var cutoff = period.CutoffUtc;
        var earnings = await _db.Set<EarningEntry>().AsNoTracking()
            .Where(e => e.Status == EarningStatus.Approved && e.PayoutItemId == null && e.AvailableAt != null &&
                        e.AvailableAt <= cutoff && e.SettlementCurrency == currency)
            .Select(e => new PlannerEarning(e.Id, e.UserId, e.SettlementAmount))
            .ToListAsync();
        var userIds = earnings.Select(e => e.UserId).Distinct().ToList();
        var users = await _db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.Status }).ToListAsync();
        var held = (await _db.Set<PayoutHold>().AsNoTracking()
            .Where(h => userIds.Contains(h.UserId) && h.ReleasedAt == null).Select(h => h.UserId).ToListAsync()).ToHashSet();
        var profiles = await _db.Set<PayoutProfile>().AsNoTracking().Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.MaskedDestination }).ToListAsync();
        var participants = users.ToDictionary(u => u.Id,
            u => new PlannerParticipant(u.Id, u.Status == UserStatus.Active, held.Contains(u.Id), profiles.Any(p => p.UserId == u.Id)));
        var plan = PayoutPlanner.Plan(earnings, participants, schedule.MinimumPayoutAmount, currency);
        if (plan.Items.Count == 0)
        {
            _logger.LogWarning("Demo seed: no payable participants for period {Period}", period.PeriodKey);
            return;
        }

        var reference = currency == PayoutBatchService.DefaultCurrency ? $"PB-{period.PeriodKey}" : $"PB-{period.PeriodKey}-{currency}";
        for (var n = 2; await _db.Set<PayoutBatch>().AnyAsync(b => b.Reference == reference); n++)
            reference = $"PB-{period.PeriodKey}-R{n}";

        var batch = new PayoutBatch
        {
            Id = IdGenerator.NewId(Now),
            Reference = reference,
            IdempotencyKey = key,
            PeriodKey = period.PeriodKey,
            PeriodStart = period.PeriodStartUtc,
            CutoffAt = period.CutoffUtc,
            ScheduledPaymentDate = period.PaymentDate,
            Currency = currency,
            Status = PayoutBatchStatus.Draft,
            PreparedByUserId = preparer.Id,
            Notes = note,
            CreatedAt = Now,
        };
        var items = plan.Items.Select(planned => (planned, item: new PayoutItem
        {
            BatchId = batch.Id,
            UserId = planned.UserId,
            Amount = planned.Amount,
            Currency = currency,
            EarningCount = planned.EarningIds.Count,
            Status = planned.HeldForMissingPayoutProfile ? PayoutItemStatus.Held : PayoutItemStatus.Pending,
            HoldReason = planned.HeldForMissingPayoutProfile ? PayoutPlanner.MissingPayoutProfileReason : null,
            PaymentProvider = ManualPaymentProvider.ProviderKey,
            DestinationHint = profiles.FirstOrDefault(p => p.UserId == planned.UserId)?.MaskedDestination is { } hint
                ? (hint.Length <= 64 ? hint : hint[..64]) : null,
            CreatedAt = Now,
        })).ToList();
        var payable = items.Where(x => x.item.Status != PayoutItemStatus.Held).ToList();
        batch.ItemCount = payable.Count;
        batch.TotalAmount = payable.Sum(x => x.item.Amount);
        batch.ExclusionsJson = JsonSerializer.Serialize(
            plan.Exclusions.Select(e => new StoredExclusion(e.UserId, e.Reason, e.Amount, e.EarningCount)).ToList(), PayoutBatchService.Json);
        _db.Set<PayoutBatch>().Add(batch);
        _db.Set<PayoutItem>().AddRange(items.Select(x => x.item));
        await _db.SaveChangesAsync();

        foreach (var (planned, item) in items)
        {
            var attached = await PayoutStore.AttachEarningsAsync(_db, item.Id, planned.EarningIds, CancellationToken.None);
            if (attached != planned.EarningIds.Count)
                throw new InvalidOperationException($"Demo seed: attached {attached} of {planned.EarningIds.Count} earnings to a payout item.");
        }

        _audit.As(preparer.Id, Role.Finance).Record("payout.batch_prepared", nameof(PayoutBatch), batch.Id,
            after: new { batch.Reference, batch.PeriodKey, batch.CutoffAt, batch.ItemCount, batch.TotalAmount, batch.Currency, Excluded = plan.Exclusions.Count });
        foreach (var finance in new[] { Finance1, Finance2 })
            await NotifyAsync(finance.Id, NotificationTypes.BatchPrepared, $"Payout batch {batch.Reference} is ready for review",
                $"{batch.ItemCount} participant(s), {batch.TotalAmount:0.00} {batch.Currency} for the period ending {batch.PeriodKey}.",
                $"/finance/payouts/batches/{batch.Id}");
        _batches[period.PeriodKey] = batch.Id;
        Count("payout batches");
        Count("payout items", items.Count);
        Count("payout exclusions", plan.Exclusions.Count);
    }

    // ------------------------------------------------------------------ finalize (PayoutBatchService.FinalizeAsync + dispatch)

    private async Task FinalizeBatchAsync(PayoutPeriod period, DemoPerson finalizer, string reason)
    {
        if (!_batches.TryGetValue(period.PeriodKey, out var batchId)) return;
        var batch = await _db.Set<PayoutBatch>().FirstAsync(b => b.Id == batchId);
        if (batch.Status != PayoutBatchStatus.Draft) return;
        // Four-eyes: the finalizer did not prepare the batch, create or approve any earning in it, or benefit from it.
        var involved = (await (from e in _db.Set<EarningEntry>()
                               join i in _db.Set<PayoutItem>() on e.PayoutItemId equals i.Id
                               where i.BatchId == batchId
                               select new { e.CreatedByUserId, e.ApprovedByUserId, e.UserId }).ToListAsync())
            .SelectMany(x => new[] { x.CreatedByUserId, x.ApprovedByUserId, (Guid?)x.UserId }).ToHashSet();
        var candidate = new[] { finalizer, Admin }.FirstOrDefault(p => p.Id != batch.PreparedByUserId && !involved.Contains(p.Id));
        if (candidate is null) throw new InvalidOperationException($"Demo seed: no eligible finalizer for batch {batch.Reference}.");
        finalizer = candidate;
        var before = new { Status = PayoutBatchStatus.Draft, batch.ItemCount, batch.TotalAmount };
        batch.Status = PayoutBatchStatus.Finalized;
        batch.FinalizedAt = Now;
        batch.FinalizedByUserId = finalizer.Id;

        var items = await _db.Set<PayoutItem>().Where(i => i.BatchId == batchId).ToListAsync();
        var heldIds = items.Where(i => i.Status == PayoutItemStatus.Held).Select(i => i.Id).ToList();
        var awaiting = items.Where(i => i.Status == PayoutItemStatus.Pending).ToList();
        foreach (var item in awaiting)
        {
            item.Status = PayoutItemStatus.AwaitingPayment;
            // The manual provider cannot move money: dispatch records that a person must pay and record the reference.
            _db.Set<PaymentAttempt>().Add(new PaymentAttempt
            {
                PayoutItemId = item.Id,
                Provider = item.PaymentProvider,
                IdempotencyKey = PaymentDispatcher.DispatchKey(item.Id),
                Status = PaymentAttemptStatus.RequiresManualAction,
                Message = ManualPaymentProvider.ManualActionMessage,
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            await NotifyAsync(item.UserId, NotificationTypes.PayoutScheduled, "Your payout is scheduled",
                $"A payout of {item.Amount:0.00} {item.Currency} is scheduled. Expected payment date: {batch.ScheduledPaymentDate:yyyy-MM-dd}.",
                "/earnings/payouts");
        }
        _audit.As(finalizer.Id, finalizer.Role).Record("payout.batch_finalized", nameof(PayoutBatch), batchId, before,
            new { Status = PayoutBatchStatus.Finalized, AwaitingPayment = awaiting.Count, HeldReleased = heldIds.Count }, reason);
        await _db.SaveChangesAsync();

        await PayoutStore.ReleaseEarningsAsync(_db, heldIds, CancellationToken.None);
        await PayoutStore.RecomputeTotalsAsync(_db, batchId, Now, CancellationToken.None);
        await PayoutStore.CompleteIfDoneAsync(_db, batchId, Now, CancellationToken.None);
    }

    // ------------------------------------------------------------------ payments (PayoutPaymentService)

    private async Task RecordPaymentsAsync(PayoutPeriod period, double payShare, bool failOne)
    {
        if (!_batches.TryGetValue(period.PeriodKey, out var batchId)) return;
        var items = await _db.Set<PayoutItem>().AsNoTracking()
            .Where(i => i.BatchId == batchId && i.Status == PayoutItemStatus.AwaitingPayment)
            .OrderBy(i => i.UserId == Sara.Id ? 0 : 1).ThenBy(i => i.Amount).ToListAsync();
        var toPay = (int)Math.Ceiling(items.Count * payShare);
        var failed = false;
        var paidCount = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var paidAt = Now.AddMinutes(i * 3);
            if (failOne && !failed && item.UserId != Sara.Id && item.UserId != Person("hamza.qureshi").Id)
            {
                await MarkPaymentFailedAsync(batchId, item.Id, "Bank returned the transfer: the beneficiary account is closed.", Finance1);
                failed = true;
                continue;
            }
            if (paidCount >= toPay) break;
            await RecordPaymentAsync(batchId, item.Id, PaymentReference(item.UserId, paidAt), paidAt, Finance1);
            paidCount++;
        }
        await PayoutStore.CompleteIfDoneAsync(_db, batchId, Now.AddMinutes(items.Count * 3), CancellationToken.None);
    }

    private string PaymentReference(Guid userId, DateTime paidAt)
    {
        var method = _participants.FirstOrDefault(p => p.Id == userId)?.PayoutMethod;
        var seq = ++_paymentSeq;
        return method switch
        {
            PayoutMethod.PayPal => $"PAYPAL-{_rng.Chars("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 10)}",
            PayoutMethod.MobileWallet => $"WLT{paidAt:yyMMdd}{seq:0000}{_rng.Digits(4)}",
            _ => $"FT{paidAt:yyMMdd}{seq:0000}{_rng.Digits(6)}",
        };
    }

    private async Task RecordPaymentAsync(Guid batchId, Guid itemId, string reference, DateTime paidAt, DemoPerson actor)
    {
        var item = await _db.Set<PayoutItem>().FirstAsync(i => i.Id == itemId);
        if (item.Status != PayoutItemStatus.AwaitingPayment) return;
        var paid = await _db.Set<EarningEntry>()
            .Where(e => e.PayoutItemId == itemId && e.Status == EarningStatus.Scheduled)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EarningStatus.Paid)
                .SetProperty(e => e.PaidAt, paidAt).SetProperty(e => e.ConcurrencyStamp, Guid.NewGuid()));
        if (paid != item.EarningCount)
            throw new InvalidOperationException($"Demo seed: expected {item.EarningCount} scheduled earnings for a payout item, found {paid}.");
        item.Status = PayoutItemStatus.Paid;
        item.PaymentReference = reference;
        item.PaidAt = paidAt;
        item.RecordedByUserId = actor.Id;

        var attemptKey = PaymentDispatcher.DispatchKey(itemId);
        var attempt = await _db.Set<PaymentAttempt>().FirstOrDefaultAsync(a => a.IdempotencyKey == attemptKey);
        if (attempt is not null)
        {
            attempt.Status = PaymentAttemptStatus.Succeeded;
            attempt.ProviderReference = reference;
            attempt.Message = "Payment recorded by finance";
            attempt.UpdatedAt = paidAt;
        }
        _audit.As(actor.Id, Role.Finance).Record("payout.payment_recorded", nameof(PayoutItem), itemId,
            before: new { Status = PayoutItemStatus.AwaitingPayment },
            after: new { Status = PayoutItemStatus.Paid, item.Amount, item.Currency, PaymentReference = reference, PaidAt = paidAt, BatchId = batchId },
            reason: "Paid via bank portal bulk upload");
        await NotifyAsync(item.UserId, NotificationTypes.PayoutPaid, "Your payout was sent",
            $"We sent your payout of {item.Amount:0.00} {item.Currency}. Payment reference ending {reference[^4..]}.", "/earnings/payouts");
        await _db.SaveChangesAsync();
        Count("payments recorded");
    }

    private async Task MarkPaymentFailedAsync(Guid batchId, Guid itemId, string reason, DemoPerson actor)
    {
        var item = await _db.Set<PayoutItem>().FirstAsync(i => i.Id == itemId);
        if (item.Status != PayoutItemStatus.AwaitingPayment) return;
        item.Status = PayoutItemStatus.Failed;
        item.FailureReason = reason;
        item.RecordedByUserId = actor.Id;
        var attemptKey = PaymentDispatcher.DispatchKey(itemId);
        var attempt = await _db.Set<PaymentAttempt>().FirstOrDefaultAsync(a => a.IdempotencyKey == attemptKey);
        if (attempt is not null)
        {
            attempt.Status = PaymentAttemptStatus.Failed;
            attempt.Message = reason;
            attempt.UpdatedAt = Now;
        }
        _audit.As(actor.Id, Role.Finance).Record("payout.payment_failed", nameof(PayoutItem), itemId,
            before: new { Status = PayoutItemStatus.AwaitingPayment },
            after: new { Status = PayoutItemStatus.Failed, item.Amount, item.Currency, BatchId = batchId }, reason: reason);
        await NotifyAsync(item.UserId, NotificationTypes.PayoutScheduled, "We couldn't complete your payout",
            $"Your payout of {item.Amount:0.00} {item.Currency} could not be completed. Please check your payout details; your earnings are safe and will be included in the next payout.",
            "/settings/payout");
        await _db.SaveChangesAsync();
        await PayoutStore.ReleaseEarningsAsync(_db, new[] { itemId }, CancellationToken.None);
        Count("payments failed");
    }

    // ------------------------------------------------------------------ holds, suspensions, adjustments

    private void PlanLedgerOperations()
    {
        // Active payout hold (created before the previous period's batch, so it shows up as an exclusion there and in the draft).
        var aisha = Person("hold.participant");
        At(_p1.CutoffUtc.AddDays(-2), async () =>
        {
            var hold = new PayoutHold
            {
                UserId = aisha.Id, CreatedAt = Now, CreatedByUserId = Finance1.Id,
                Reason = "Identity document review (KYC) requested after the payout bank account was changed.",
            };
            _db.Set<PayoutHold>().Add(hold);
            _audit.As(Finance1.Id, Role.Finance).Record("payout.hold_created", nameof(PayoutHold), hold.Id,
                after: new { hold.UserId, HeldDraftItems = 0, AwaitingPaymentItems = 0 }, reason: hold.Reason);
            await NotifyAsync(aisha.Id, NotificationTypes.PayoutHold, "Your payouts are paused",
                Ledger.EarningsSummaryService.NeutralHoldMessage, "/earnings");
            Count("payout holds");
        });

        // A released hold for history.
        var reem = Person("reem.alqahtani");
        At(_p3.CutoffUtc.AddDays(-6), () =>
        {
            _db.Set<PayoutHold>().Add(new PayoutHold
            {
                UserId = reem.Id, CreatedAt = Now, CreatedByUserId = Finance2.Id, Reason = "Bank details mismatch reported by the payment provider.",
                ReleasedAt = Now.AddDays(2), ReleasedByUserId = Finance1.Id, ReleaseNote = "Participant corrected the account holder name; verified.",
            });
            Count("payout holds");
            return Task.CompletedTask;
        });

        // Suspension (before the draft batch is prepared, so the draft lists an "account inactive" exclusion).
        var jack = Person("suspended.participant");
        var suspendAt = _p0.CutoffUtc.AddHours(-12);
        jack.SuspendedAt = suspendAt;
        At(suspendAt, async () =>
        {
            var user = await _db.Set<User>().FirstAsync(u => u.Id == jack.Id);
            var before = new { user.Status, user.StatusReason };
            user.Status = UserStatus.Suspended;
            user.StatusReason = "Repeated submissions with screenshots copied from other creators' posts.";
            user.StatusChangedAt = Now;
            user.SecurityVersion++;
            _audit.As(Admin.Id, Role.Admin).Record("admin.user_suspended", nameof(User), user.Id, before, new { user.Status, user.StatusReason }, user.StatusReason);
            await NotifyAsync(user.Id, NotificationTypes.AccountStatusChanged, "Your Optimize All account has been suspended",
                $"Your account has been suspended. Reason: {user.StatusReason} If you believe this is a mistake, reply to this email or contact support.", null);
            Count("suspensions");
        });

        // Manual adjustments (credits and a debit) with reasons, through the ledger writer.
        var marcus = Person("marcus.johnson");
        Adjust(marcus, 10.00m, "USD", _now.AddDays(-40),
            "Compensation for a LedgerLeaf LinkedIn post removed by the platform in error; confirmed with the brand.", null);
        Adjust(marcus, -2.50m, "USD", _now.AddDays(-39),
            "Correction: the compensation should equal the LinkedIn rate difference (7.50 USD), not 10.00 USD.", null);
        Adjust(Person("layla.haddad"), 50m, "AED", _now.AddDays(-10),
            "Event appearance fee agreed with Desert Bloom for the Autumn Glow launch evening.", null);
        Adjust(Person("noor.khalil"), 15.00m, "USD", _now.AddHours(-20),
            "Creative brief rework fee agreed with Desert Bloom (waiting for a second finance approver).", null, leavePending: true);
    }

    /// <summary>
    /// A manual adjustment through the ledger writer. Positive adjustments follow the four-eyes rule: created by finance1
    /// as PendingApproval and approved by finance2 (or left pending for the approvals demo). Debits are payable at once.
    /// Approvals are timed so no finance2-approved earning lands in the draft batch finance2 is meant to finalize.
    /// </summary>
    private void Adjust(DemoPerson who, decimal amount, string currency, DateTime at, string reason, Func<string?>? ticketReference,
        bool leavePending = false)
    {
        var positive = amount > 0;
        At(at, async () =>
        {
            var reference = ticketReference?.Invoke();
            var description = (positive ? "Manual credit" : "Manual debit") + (reference is null ? string.Empty : $" (support ticket {reference})");
            _audit.As(Finance1.Id, Role.Finance);
            var entry = await _ledger.RecordAsync(new NewEarning(who.Id, EarningType.Adjustment, amount, currency,
                $"adjustment:{IdGenerator.NewId(Now):N}", description, RequiresApproval: positive, Reason: reason, CreatedByUserId: Finance1.Id));
            _audit.Record("ledger.adjustment_created", nameof(EarningEntry), entry.Id,
                after: new { entry.UserId, entry.Amount, entry.Currency, entry.SettlementAmount, entry.SettlementCurrency, entry.Status }, reason: reason);
            Count(leavePending ? "adjustments (pending approval)" : "adjustments");
            if (!positive || leavePending) return;

            var entryId = entry.Id;
            var approveAt = Now.AddHours(2);
            var hold = TimeSpan.FromDays(_schedule.EarningHoldDays);
            if (approveAt + hold > _p1.CutoffUtc && approveAt + hold <= _p0.CutoffUtc)
                approveAt = _p0.CutoffUtc - hold + TimeSpan.FromHours(1); // payable after the draft's cutoff
            At(approveAt, async () =>
            {
                var e = await _db.Set<EarningEntry>().FirstAsync(x => x.Id == entryId);
                if (e.Status != EarningStatus.PendingApproval) return;
                _audit.As(Finance2.Id, Role.Finance);
                await _ledger.ApproveAsync(e, Finance2.Id);
            });
        });
    }
}
