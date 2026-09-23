using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Payouts;

/// <summary>
/// Every 15 minutes: when the active schedule has AutoPrepareBatches, prepares the draft batch of the last completed
/// period if no batch (in any status, including cancelled) exists for it yet. Safe to retry: preparation is
/// idempotent per period and currency.
/// </summary>
public sealed class PayoutPreparationJob(
    AppDbContext db, IPayoutScheduleProvider schedules, PayoutBatchService batches, TimeProvider clock) : IJob
{
    public string Name => "payouts.prepare";

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var schedule = await schedules.GetActiveAsync(now, ct);
        if (!schedule.AutoPrepareBatches) return "Skipped: automatic batch preparation is disabled.";

        var period = PayoutPeriodCalculator.LastCompletedPeriod(schedule, now);
        var currency = schedule.SettlementCurrency;
        if (await db.Set<PayoutBatch>().AsNoTracking().AnyAsync(b => b.PeriodKey == period.PeriodKey && b.Currency == currency, ct))
            return $"Batch for period {period.PeriodKey} ({currency}) already exists.";

        try
        {
            var outcome = await batches.PrepareAsync(period, actor: null, note: "Prepared automatically at cutoff", ct);
            return outcome.Created
                ? $"Prepared batch {outcome.BatchId} for period {period.PeriodKey} ({currency})."
                : $"Batch for period {period.PeriodKey} ({currency}) already exists.";
        }
        catch (DomainException ex) when (ex.Code is "payout.no_eligible_earnings" or "payout.prepare_busy")
        {
            return $"Period {period.PeriodKey}: {ex.Message}";
        }
    }
}

/// <summary>Baseline: the default biweekly payout schedule (no exchange rates — FX rates must be entered by finance).</summary>
public sealed class PayoutsBaselineSeeder(TimeProvider clock) : ISeeder
{
    public static readonly DateTime InitialEffectiveFrom = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public string Profile => "Baseline";
    public int Order => 20;

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Set<PayoutSchedule>().AnyAsync(ct)) return;
        var schedule = PayoutScheduleProvider.Default();
        schedule.EffectiveFrom = InitialEffectiveFrom;
        schedule.CreatedAt = clock.GetUtcNow().UtcDateTime;
        schedule.ChangeReason = "Initial schedule";
        db.Set<PayoutSchedule>().Add(schedule);
        await db.SaveChangesAsync(ct);
    }
}
