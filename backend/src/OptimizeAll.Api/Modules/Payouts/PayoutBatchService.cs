using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Errors;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Modules.Ledger;
using OptimizeAll.Api.Modules.Payouts.Providers;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Payouts;

/// <summary>Stored in <see cref="PayoutBatch.ExclusionsJson"/>.</summary>
public sealed record StoredExclusion(Guid UserId, PayoutExclusionReason Reason, decimal Amount, int EarningCount);

public sealed record PrepareOutcome(bool Created, Guid BatchId, IReadOnlyList<StoredExclusion> Exclusions);

/// <summary>
/// Payout batch lifecycle: prepare (draft) → review (hold/unhold items, regenerate) → finalize (four-eyes) →
/// payments recorded per item (see <see cref="PayoutPaymentService"/>) → completed. Cancel releases everything.
/// Generating or finalizing a batch never marks anything paid.
/// </summary>
public sealed class PayoutBatchService(
    AppDbContext db,
    IPayoutScheduleProvider schedules,
    IPaymentProviderRegistry providers,
    PaymentDispatcher dispatcher,
    IAuditLogger audit,
    INotificationService notifications,
    TimeProvider clock,
    ILogger<PayoutBatchService> logger,
    PayoutTestHooks? hooks = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The settlement currency used as the default in batch references (no currency suffix).</summary>
    public static readonly string DefaultCurrency = PayoutScheduleProvider.Default().SettlementCurrency;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public static string IdempotencyKeyFor(string periodKey, string currency) => $"period:{periodKey}:{currency}";

    public async Task<PayoutPeriod> ResolvePeriodAsync(string? periodKey, CancellationToken ct)
    {
        var schedule = await schedules.GetActiveAsync(Now, ct);
        if (string.IsNullOrWhiteSpace(periodKey)) return PayoutPeriodCalculator.LastCompletedPeriod(schedule, Now);
        if (!PayoutPeriodCalculator.TryFromKey(schedule, periodKey, out var period))
            throw new DomainException("payout.invalid_period", $"{periodKey} is not a cutoff date of the current payout schedule.");
        if (period.CutoffUtc >= Now)
            throw new DomainException("payout.period_not_completed", "This period has not reached its cutoff yet.");
        return period;
    }

    /// <summary>
    /// Prepares the draft batch for <paramref name="period"/> in the schedule's settlement currency. Idempotent: a retry
    /// or a concurrent call returns the existing batch (Created=false). Serialized by a MySQL named lock; the unique
    /// index on IdempotencyKey is the final guard. <paramref name="actor"/> null = system (background job).
    /// </summary>
    public async Task<PrepareOutcome> PrepareAsync(PayoutPeriod period, Guid? actor, string? note, CancellationToken ct)
    {
        var schedule = await schedules.GetActiveAsync(Now, ct);
        var currency = schedule.SettlementCurrency;
        var key = IdempotencyKeyFor(period.PeriodKey, currency);

        await using var namedLock = await PayoutStore.AcquirePrepareLockAsync(db, ct);

        var existing = await db.Set<PayoutBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.IdempotencyKey == key, ct);
        if (existing is not null) return new PrepareOutcome(false, existing.Id, ReadExclusions(existing.ExclusionsJson));

        try
        {
            await using var tx = await PayoutStore.BeginAsync(db, ct);
            var batch = new PayoutBatch
            {
                Reference = await NextReferenceAsync(period.PeriodKey, currency, ct),
                IdempotencyKey = key,
                PeriodKey = period.PeriodKey,
                PeriodStart = period.PeriodStartUtc,
                CutoffAt = period.CutoffUtc,
                ScheduledPaymentDate = period.PaymentDate,
                Currency = currency,
                Status = PayoutBatchStatus.Draft,
                PreparedByUserId = actor,
                Notes = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            };
            var plan = await BuildPlanAsync(period, currency, schedule.MinimumPayoutAmount, ct);
            await PayoutTestHooks.InvokeAsync(hooks?.AfterPrepareSelection, ct);
            if (plan.Items.Count == 0)
            {
                throw DomainException.Conflict("payout.no_eligible_earnings",
                    plan.Exclusions.Count == 0
                        ? $"No approved earnings are payable for the period ending {period.PeriodKey}."
                        : $"No participant is payable for the period ending {period.PeriodKey} ({plan.Exclusions.Count} excluded: on hold, inactive or below the minimum).");
            }
            db.Set<PayoutBatch>().Add(batch);
            await PopulateAsync(batch, plan, ct);

            var summary = new { batch.Reference, batch.PeriodKey, batch.CutoffAt, batch.ItemCount, batch.TotalAmount, batch.Currency, Excluded = plan.Exclusions.Count };
            if (actor is null) audit.RecordSystem("payout.batch_prepared", nameof(PayoutBatch), batch.Id, summary);
            else audit.Record("payout.batch_prepared", nameof(PayoutBatch), batch.Id, after: summary);
            await NotifyFinanceAsync(batch, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new PrepareOutcome(true, batch.Id, plan.Exclusions.Select(ToStored).ToList());
        }
        catch (DbUpdateException ex) when (ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            // Final guard: another instance created the batch without our lock (e.g. different lock scope).
            db.ChangeTracker.Clear();
            var winner = await db.Set<PayoutBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.IdempotencyKey == key, ct);
            if (winner is null) throw;
            return new PrepareOutcome(false, winner.Id, ReadExclusions(winner.ExclusionsJson));
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Releases every earning of a draft batch and re-runs selection for the same period (same batch row).</summary>
    public async Task RegenerateAsync(Guid batchId, string reason, Guid actor, CancellationToken ct)
    {
        var schedule = await schedules.GetActiveAsync(Now, ct);
        await using var namedLock = await PayoutStore.AcquirePrepareLockAsync(db, ct);
        try
        {
            await using var tx = await PayoutStore.BeginAsync(db, ct);
            await PayoutStore.ClaimDraftAsync(db, batchId, Now, ct);
            var batch = await db.Set<PayoutBatch>().FirstAsync(b => b.Id == batchId, ct);
            if (batch.Currency != schedule.SettlementCurrency)
                throw DomainException.Conflict("payout.settlement_currency_changed",
                    "The settlement currency changed since this batch was prepared; cancel it and prepare a new one.");

            var before = new { batch.ItemCount, batch.TotalAmount };
            var itemIds = await db.Set<PayoutItem>().Where(i => i.BatchId == batchId).Select(i => i.Id).ToListAsync(ct);
            // Manual holds (and automatic ones such as "Submission reversal pending") survive regeneration: the
            // participant's new item is created Held with the same reason. "No payout details" is re-derived by the planner.
            var carriedHolds = (await db.Set<PayoutItem>().AsNoTracking()
                    .Where(i => i.BatchId == batchId && i.Status == PayoutItemStatus.Held && i.HoldReason != null &&
                                i.HoldReason != PayoutPlanner.MissingPayoutProfileReason)
                    .Select(i => new { i.UserId, i.HoldReason }).ToListAsync(ct))
                .ToDictionary(i => i.UserId, i => i.HoldReason!);
            await PayoutStore.ReleaseEarningsAsync(db, itemIds, ct);
            await db.Set<PaymentAttempt>().Where(a => itemIds.Contains(a.PayoutItemId)).ExecuteDeleteAsync(ct);
            await db.Set<PayoutItem>().Where(i => i.BatchId == batchId).ExecuteDeleteAsync(ct);

            var period = new PayoutPeriod(DateOnly.ParseExact(batch.PeriodKey, PayoutPeriodCalculator.PeriodKeyFormat),
                batch.PeriodStart, batch.CutoffAt, batch.ScheduledPaymentDate, batch.PeriodKey);
            var plan = await BuildPlanAsync(period, batch.Currency, schedule.MinimumPayoutAmount, ct);
            batch.PreparedByUserId = actor;
            await PopulateAsync(batch, plan, ct, carriedHolds);

            audit.Record("payout.batch_regenerated", nameof(PayoutBatch), batch.Id, before,
                new { batch.ItemCount, batch.TotalAmount, Excluded = plan.Exclusions.Count, HoldsCarriedOver = carriedHolds.Count }, reason);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task<PayoutPlan> BuildPlanAsync(PayoutPeriod period, string currency, decimal minimum, CancellationToken ct)
    {
        var cutoff = period.CutoffUtc;
        var earnings = await db.Set<EarningEntry>().AsNoTracking()
            .Where(e => e.Status == EarningStatus.Approved && e.PayoutItemId == null && e.AvailableAt != null &&
                        e.AvailableAt <= cutoff && e.SettlementCurrency == currency)
            .Select(e => new PlannerEarning(e.Id, e.UserId, e.SettlementAmount))
            .ToListAsync(ct);
        var userIds = earnings.Select(e => e.UserId).Distinct().ToList();

        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Status }).ToListAsync(ct);
        var held = (await db.Set<PayoutHold>().AsNoTracking()
            .Where(h => userIds.Contains(h.UserId) && h.ReleasedAt == null).Select(h => h.UserId).ToListAsync(ct)).ToHashSet();
        var withProfile = (await db.Set<PayoutProfile>().AsNoTracking()
            .Where(p => userIds.Contains(p.UserId)).Select(p => p.UserId).ToListAsync(ct)).ToHashSet();

        var participants = users.ToDictionary(u => u.Id,
            u => new PlannerParticipant(u.Id, u.Status == UserStatus.Active, held.Contains(u.Id), withProfile.Contains(u.Id)));
        return PayoutPlanner.Plan(earnings, participants, minimum, currency);
    }

    /// <summary>Creates items for the plan, attaches earnings with one conditional update per item, sets totals.</summary>
    private async Task PopulateAsync(PayoutBatch batch, PayoutPlan plan, CancellationToken ct,
        IReadOnlyDictionary<Guid, string>? carriedHolds = null)
    {
        var userIds = plan.Items.Select(i => i.UserId).ToList();
        var profiles = (await db.Set<PayoutProfile>().AsNoTracking()
                .Where(p => userIds.Contains(p.UserId))
                .Select(p => new { p.UserId, p.MaskedDestination, p.UpdatedAt })
                .ToListAsync(ct))
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.UpdatedAt).First().MaskedDestination);

        var providerKey = providers.Active.Key;
        var items = plan.Items.Select(planned =>
        {
            var holdReason = planned.HeldForMissingPayoutProfile
                ? PayoutPlanner.MissingPayoutProfileReason
                : carriedHolds?.GetValueOrDefault(planned.UserId);
            return (planned, item: new PayoutItem
            {
                BatchId = batch.Id,
                UserId = planned.UserId,
                Amount = planned.Amount,
                Currency = batch.Currency,
                EarningCount = planned.EarningIds.Count,
                Status = holdReason is null ? PayoutItemStatus.Pending : PayoutItemStatus.Held,
                HoldReason = holdReason,
                PaymentProvider = providerKey,
                DestinationHint = profiles.TryGetValue(planned.UserId, out var hint) ? Truncate(hint, 64) : null,
            });
        }).ToList();

        db.Set<PayoutItem>().AddRange(items.Select(x => x.item));
        var payable = items.Where(x => x.item.Status != PayoutItemStatus.Held).ToList();
        batch.ItemCount = payable.Count;
        batch.TotalAmount = payable.Sum(x => x.item.Amount);
        batch.ExclusionsJson = JsonSerializer.Serialize(plan.Exclusions.Select(ToStored).ToList(), Json);
        await db.SaveChangesAsync(ct);

        foreach (var (planned, item) in items)
        {
            var attached = await PayoutStore.AttachEarningsAsync(db, item.Id, planned.EarningIds, ct);
            if (attached != planned.EarningIds.Count)
            {
                logger.LogWarning("Batch {Batch}: expected to attach {Expected} earnings to item {Item} but attached {Actual}",
                    batch.Reference, planned.EarningIds.Count, item.Id, attached);
                throw DomainException.Conflict("payout.earnings_changed",
                    "Some earnings changed while the batch was being prepared (e.g. a reversal). Nothing was saved; try again.");
            }
        }
    }

    private async Task<string> NextReferenceAsync(string periodKey, string currency, CancellationToken ct)
    {
        var baseReference = currency == DefaultCurrency ? $"PB-{periodKey}" : $"PB-{periodKey}-{currency}";
        var taken = await db.Set<PayoutBatch>().AsNoTracking()
            .Where(b => b.Reference == baseReference || b.Reference.StartsWith(baseReference + "-R"))
            .Select(b => b.Reference).ToListAsync(ct);
        if (!taken.Contains(baseReference)) return baseReference;
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseReference}-R{n}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private async Task NotifyFinanceAsync(PayoutBatch batch, CancellationToken ct)
    {
        var financeUsers = await db.Set<User>().AsNoTracking()
            .Where(u => u.Status == UserStatus.Active && u.Roles.Any(r => r.Role == Role.Finance))
            .Select(u => u.Id).ToListAsync(ct);
        foreach (var userId in financeUsers)
        {
            await notifications.StageAsync(new NotificationRequest(userId, NotificationTypes.BatchPrepared,
                $"Payout batch {batch.Reference} is ready for review",
                $"{batch.ItemCount} participant(s), {batch.TotalAmount:0.00} {batch.Currency} for the period ending {batch.PeriodKey}.",
                $"/finance/payouts/batches/{batch.Id}"), ct);
        }
    }

    // ---------- Draft review ----------

    public async Task HoldItemAsync(Guid batchId, Guid itemId, string reason, CancellationToken ct)
    {
        await using var tx = await PayoutStore.BeginAsync(db, ct);
        await PayoutStore.ClaimDraftAsync(db, batchId, Now, ct);
        var updated = await db.Set<PayoutItem>()
            .Where(i => i.Id == itemId && i.BatchId == batchId && i.Status == PayoutItemStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, PayoutItemStatus.Held).SetProperty(i => i.HoldReason, reason)
                .SetProperty(i => i.UpdatedAt, Now).SetProperty(i => i.ConcurrencyStamp, Guid.NewGuid()), ct);
        if (updated == 0) await ThrowItemStateAsync(batchId, itemId, "Only pending items can be held.", ct);
        await PayoutStore.RecomputeTotalsAsync(db, batchId, Now, ct);
        audit.Record("payout.item_held", nameof(PayoutItem), itemId, after: new { BatchId = batchId }, reason: reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task UnholdItemAsync(Guid batchId, Guid itemId, string? note, CancellationToken ct)
    {
        await using var tx = await PayoutStore.BeginAsync(db, ct);
        await PayoutStore.ClaimDraftAsync(db, batchId, Now, ct);
        var item = await db.Set<PayoutItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.BatchId == batchId, ct)
                   ?? throw DomainException.NotFound("PayoutItem");
        if (item.Status != PayoutItemStatus.Held)
            throw DomainException.Conflict("payout.item_not_held", "Only held items can be released.");
        if (await db.Set<EarningEntry>().CountAsync(e => e.PayoutItemId == itemId, ct) != item.EarningCount)
            throw DomainException.Conflict("payout.item_released",
                "This item's earnings were released (e.g. for a reversal). Regenerate the batch to include the participant again.");
        if (!await db.Set<PayoutProfile>().AnyAsync(p => p.UserId == item.UserId, ct))
            throw DomainException.Conflict("payout.no_payout_profile", "The participant has no payout details on file.");
        if (await db.Set<PayoutHold>().AnyAsync(h => h.UserId == item.UserId && h.ReleasedAt == null, ct))
            throw DomainException.Conflict("payout.user_on_hold", "The participant has an active payout hold; release it first.");
        if (await db.Set<User>().AnyAsync(u => u.Id == item.UserId && u.Status != UserStatus.Active, ct))
            throw DomainException.Conflict("payout.user_inactive", "The participant's account is not active.");

        var destination = await db.Set<PayoutProfile>().AsNoTracking().Where(p => p.UserId == item.UserId)
            .OrderByDescending(p => p.UpdatedAt).Select(p => p.MaskedDestination).FirstAsync(ct);
        destination = Truncate(destination, 64);
        var updated = await db.Set<PayoutItem>()
            .Where(i => i.Id == itemId && i.Status == PayoutItemStatus.Held)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, PayoutItemStatus.Pending).SetProperty(i => i.HoldReason, (string?)null)
                .SetProperty(i => i.DestinationHint, destination)
                .SetProperty(i => i.UpdatedAt, Now).SetProperty(i => i.ConcurrencyStamp, Guid.NewGuid()), ct);
        if (updated == 0) throw DomainException.Conflict("payout.item_not_held", "Only held items can be released.");
        await PayoutStore.RecomputeTotalsAsync(db, batchId, Now, ct);
        audit.Record("payout.item_unheld", nameof(PayoutItem), itemId, after: new { BatchId = batchId }, reason: note);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task ThrowItemStateAsync(Guid batchId, Guid itemId, string message, CancellationToken ct)
    {
        if (!await db.Set<PayoutItem>().AnyAsync(i => i.Id == itemId && i.BatchId == batchId, ct))
            throw DomainException.NotFound("PayoutItem");
        throw DomainException.Conflict("payout.item_state", message);
    }

    // ---------- Cancel ----------

    public async Task CancelAsync(Guid batchId, string reason, Guid actor, CancellationToken ct)
    {
        List<(Guid UserId, decimal Amount, string Currency)> notify;
        await using (var tx = await PayoutStore.BeginAsync(db, ct))
        {
            var status = await PayoutStore.LockBatchAsync(db, batchId, ct) ?? throw DomainException.NotFound("PayoutBatch");
            if (status is not (PayoutBatchStatus.Draft or PayoutBatchStatus.Finalized))
                throw DomainException.Conflict("payout.cannot_cancel", $"A {status} batch cannot be cancelled.");
            if (await db.Set<PayoutItem>().AnyAsync(i => i.BatchId == batchId && i.Status == PayoutItemStatus.Paid, ct))
                throw DomainException.Conflict("payout.has_paid_items", "Payments were already recorded for this batch; it cannot be cancelled.");
            if (status == PayoutBatchStatus.Finalized)
            {
                var attempts = from a in db.Set<PaymentAttempt>()
                               join i in db.Set<PayoutItem>() on a.PayoutItemId equals i.Id
                               where i.BatchId == batchId
                               select a.Status;
                if (await attempts.AnyAsync(a => a == PaymentAttemptStatus.Submitted, ct))
                    throw DomainException.Conflict("payout.attempt_in_flight",
                        "A payment of this batch was submitted to the payment provider and has no outcome yet. " +
                        "Wait for the provider's confirmation or failure before cancelling.");
                var exportedAt = await db.Set<PayoutBatch>().Where(b => b.Id == batchId).Select(b => b.InstructionsExportedAt).FirstAsync(ct);
                if (exportedAt is not null || await attempts.AnyAsync(a => a == PaymentAttemptStatus.Succeeded, ct))
                    throw DomainException.Conflict("payout.instructions_exported",
                        "Payment instructions for this batch were already exported (or a payment went through), so money may be on its way. " +
                        "The batch can no longer be cancelled: mark each unpaid item failed with a reason instead.");
            }

            var open = await db.Set<PayoutItem>().AsNoTracking()
                .Where(i => i.BatchId == batchId && (i.Status == PayoutItemStatus.Pending || i.Status == PayoutItemStatus.Held ||
                                                     i.Status == PayoutItemStatus.AwaitingPayment))
                .Select(i => new { i.Id, i.UserId, i.Amount, i.Currency, i.Status }).ToListAsync(ct);
            var openIds = open.Select(i => i.Id).ToList();
            await PayoutStore.ReleaseEarningsAsync(db, openIds, ct);
            await db.Set<PayoutItem>().Where(i => openIds.Contains(i.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, PayoutItemStatus.Cancelled)
                    .SetProperty(i => i.UpdatedAt, Now).SetProperty(i => i.ConcurrencyStamp, Guid.NewGuid()), ct);
            await db.Set<PaymentAttempt>()
                .Where(a => openIds.Contains(a.PayoutItemId) && a.Status != PaymentAttemptStatus.Succeeded && a.Status != PaymentAttemptStatus.Failed)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, PaymentAttemptStatus.Failed)
                    .SetProperty(a => a.Message, "Batch cancelled").SetProperty(a => a.UpdatedAt, Now), ct);

            // Free the period's idempotency key so the period can be prepared again deliberately.
            var batch = await db.Set<PayoutBatch>().FirstAsync(b => b.Id == batchId, ct);
            batch.Status = PayoutBatchStatus.Cancelled;
            batch.CancelledAt = Now;
            batch.CancelledByUserId = actor;
            batch.CancelReason = reason;
            batch.IdempotencyKey = $"{batch.IdempotencyKey}:cancelled:{batch.Id:N}";
            batch.ItemCount = 0;
            batch.TotalAmount = 0;
            audit.Record("payout.batch_cancelled", nameof(PayoutBatch), batchId,
                before: new { Status = status, Items = open.Count }, after: new { Status = PayoutBatchStatus.Cancelled }, reason: reason);

            notify = open.Where(i => i.Status == PayoutItemStatus.AwaitingPayment).Select(i => (i.UserId, i.Amount, i.Currency)).ToList();
            foreach (var (userId, amount, currency) in notify)
            {
                await notifications.StageAsync(new NotificationRequest(userId, NotificationTypes.PayoutScheduled,
                    "Your scheduled payout was rescheduled",
                    $"Your payout of {amount:0.00} {currency} could not go ahead in this batch. Your earnings are safe and will be included in an upcoming payout.",
                    "/earnings/payouts", new[] { NotificationChannel.Email }), ct);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
    }

    // ---------- Finalize ----------

    public async Task<IReadOnlyList<DispatchResultDto>> FinalizeAsync(Guid batchId, Guid stamp, string? reason, Guid actor, CancellationToken ct)
    {
        var batch = await db.Set<PayoutBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct)
                    ?? throw DomainException.NotFound("PayoutBatch");
        if (batch.PreparedByUserId is { } preparer && preparer == actor)
            throw DomainException.Forbidden("payout.self_finalize", "A batch must be finalized by someone other than the person who prepared it.");

        List<Guid> awaiting;
        await using (var tx = await PayoutStore.BeginAsync(db, ct))
        {
            var now = Now;
            var won = await db.Set<PayoutBatch>()
                .Where(b => b.Id == batchId && b.Status == PayoutBatchStatus.Draft && b.ConcurrencyStamp == stamp)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, PayoutBatchStatus.Finalized)
                    .SetProperty(b => b.FinalizedAt, now).SetProperty(b => b.FinalizedByUserId, actor)
                    .SetProperty(b => b.UpdatedAt, now).SetProperty(b => b.ConcurrencyStamp, Guid.NewGuid()), ct);
            if (won == 0)
            {
                var current = await db.Set<PayoutBatch>().AsNoTracking().Where(b => b.Id == batchId).Select(b => b.Status).FirstAsync(ct);
                throw current == PayoutBatchStatus.Draft
                    ? DomainException.Conflict("concurrency.conflict", "The batch changed since you loaded it. Reload, review and try again.")
                    : DomainException.Conflict("payout.not_draft", $"The batch is already {current}.");
            }

            await EnsureNoConflictOfInterestAsync(batchId, actor, ct);

            // Safety net for payout holds and suspensions placed after the batch was prepared (or racing with it): a
            // pending item of a participant who is on hold or not active is held here, never moved to AwaitingPayment.
            var pending = await db.Set<PayoutItem>().AsNoTracking()
                .Where(i => i.BatchId == batchId && i.Status == PayoutItemStatus.Pending)
                .Select(i => new { i.Id, i.UserId }).ToListAsync(ct);
            var blockers = await PayoutStore.PayoutBlockersAsync(db, pending.Select(i => i.UserId).Distinct().ToList(), ct);
            var heldAtFinalize = new List<Guid>();
            foreach (var item in pending.Where(i => blockers.ContainsKey(i.UserId)))
            {
                var holdReason = blockers[item.UserId];
                var held = await db.Set<PayoutItem>().Where(i => i.Id == item.Id && i.Status == PayoutItemStatus.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, PayoutItemStatus.Held).SetProperty(i => i.HoldReason, holdReason)
                        .SetProperty(i => i.UpdatedAt, now).SetProperty(i => i.ConcurrencyStamp, Guid.NewGuid()), ct);
                if (held == 0) continue;
                heldAtFinalize.Add(item.Id);
                audit.Record("payout.item_held", nameof(PayoutItem), item.Id,
                    after: new { BatchId = batchId, Automatic = true, AtFinalize = true }, reason: holdReason);
            }

            // Held items (manual or above) release their earnings exactly as before.
            var heldIds = await db.Set<PayoutItem>().Where(i => i.BatchId == batchId && i.Status == PayoutItemStatus.Held)
                .Select(i => i.Id).ToListAsync(ct);
            await PayoutStore.ReleaseEarningsAsync(db, heldIds, ct);
            await db.Set<PayoutItem>().Where(i => i.BatchId == batchId && i.Status == PayoutItemStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, PayoutItemStatus.AwaitingPayment)
                    .SetProperty(i => i.UpdatedAt, now).SetProperty(i => i.ConcurrencyStamp, Guid.NewGuid()), ct);
            await PayoutStore.RecomputeTotalsAsync(db, batchId, now, ct);
            await PayoutStore.CompleteIfDoneAsync(db, batchId, now, ct);

            var toNotify = await db.Set<PayoutItem>().AsNoTracking()
                .Where(i => i.BatchId == batchId && i.Status == PayoutItemStatus.AwaitingPayment)
                .Select(i => new { i.Id, i.UserId, i.Amount, i.Currency }).ToListAsync(ct);
            awaiting = toNotify.Select(i => i.Id).ToList();
            foreach (var item in toNotify)
            {
                await notifications.StageAsync(new NotificationRequest(item.UserId, NotificationTypes.PayoutScheduled,
                    "Your payout is scheduled",
                    $"A payout of {item.Amount:0.00} {item.Currency} is scheduled. Expected payment date: {batch.ScheduledPaymentDate:yyyy-MM-dd}.",
                    "/earnings/payouts", new[] { NotificationChannel.Email }), ct);
            }

            audit.Record("payout.batch_finalized", nameof(PayoutBatch), batchId,
                before: new { Status = PayoutBatchStatus.Draft, batch.ItemCount, batch.TotalAmount },
                after: new { Status = PayoutBatchStatus.Finalized, AwaitingPayment = awaiting.Count, HeldReleased = heldIds.Count, HeldAtFinalize = heldAtFinalize },
                reason: reason);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();

        // After commit: hand each item to the payment provider layer (manual provider → RequiresManualAction).
        return await dispatcher.DispatchAsync(awaiting, ct);
    }

    /// <summary>
    /// Segregation of duties at finalize: the finalizer must not be the beneficiary of any item of the batch, nor the
    /// person who created or approved any earning in it (403 <c>payout.conflict_of_interest</c>).
    /// </summary>
    private async Task EnsureNoConflictOfInterestAsync(Guid batchId, Guid actor, CancellationToken ct)
    {
        if (await db.Set<PayoutItem>().AnyAsync(i => i.BatchId == batchId && i.UserId == actor, ct))
            throw DomainException.Forbidden("payout.conflict_of_interest",
                "You are the beneficiary of an item in this batch; another finance user must finalize it.");
        var itemIds = db.Set<PayoutItem>().Where(i => i.BatchId == batchId).Select(i => i.Id);
        if (await db.Set<EarningEntry>().AnyAsync(e => e.PayoutItemId != null && itemIds.Contains(e.PayoutItemId.Value) &&
                                                       (e.CreatedByUserId == actor || e.ApprovedByUserId == actor), ct))
            throw DomainException.Forbidden("payout.conflict_of_interest",
                "You created or approved an earning included in this batch; another finance user must finalize it.");
    }

    public static IReadOnlyList<StoredExclusion> ReadExclusions(string? json) =>
        string.IsNullOrEmpty(json) ? Array.Empty<StoredExclusion>()
            : JsonSerializer.Deserialize<List<StoredExclusion>>(json, Json) ?? new List<StoredExclusion>();

    private static StoredExclusion ToStored(PlannedExclusion e) => new(e.UserId, e.Reason, e.Amount, e.EarningCount);

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
