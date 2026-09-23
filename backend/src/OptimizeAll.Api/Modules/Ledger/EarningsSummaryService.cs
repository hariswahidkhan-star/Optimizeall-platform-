using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Ledger;

public interface IEarningsSummaryService
{
    Task<EarningsSummaryDto> GetAsync(Guid userId, CancellationToken ct);
}

/// <summary>
/// Balance buckets of one participant, in the settlement currency of the active payout schedule.
/// Definitions (see docs/api/ledger-payouts.md):
/// <list type="bullet">
/// <item>pending — estimated rewards of the participant's submissions in Pending/UnderReview (converted with the
/// current FX rate; currencies without a rate are only reported in pendingByCurrency) + PendingApproval entries.</item>
/// <item>approved — Approved entries (not yet in a batch), credits and clawbacks netted.</item>
/// <item>onHold — the part of <c>approved</c> whose AvailableAt is still in the future (hold period).</item>
/// <item>scheduled — Scheduled entries (in a draft/finalized batch, not yet paid).</item>
/// <item>paid — Paid entries (net of clawbacks recovered through a payout).</item>
/// <item>reversed — each reversed earning counted once: |reversal legs| + Reversed entries without a leg.</item>
/// <item>availableForNextPayout — Approved entries available by the next cutoff (net).</item>
/// <item>lifetimeEarned — net of Approved + Scheduled + Paid entries.</item>
/// </list>
/// Entries whose settlement currency differs from the current one (history before a currency change) are excluded
/// from the buckets and only appear in byCurrency.
/// </summary>
public sealed class EarningsSummaryService(
    AppDbContext db,
    IPayoutScheduleProvider schedules,
    IExchangeRateProvider rates,
    TimeProvider clock) : IEarningsSummaryService
{
    public const string NeutralHoldMessage =
        "Your payouts are temporarily paused while we review your account. Your earnings are safe. Contact support if you have questions.";

    public async Task<EarningsSummaryDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var schedule = await schedules.GetActiveAsync(now, ct);
        var currency = schedule.SettlementCurrency;
        var next = PayoutPeriodCalculator.PeriodContaining(schedule, now);

        var entries = await db.Set<EarningEntry>().AsNoTracking()
            .Where(e => e.UserId == userId)
            .Select(e => new
            {
                e.Status, e.Type, e.Amount, e.Currency, e.SettlementAmount, e.SettlementCurrency, e.AvailableAt,
                HasLeg = e.ReversedByEntryId != null,
            })
            .ToListAsync(ct);

        var inCurrency = entries.Where(e => e.SettlementCurrency == currency).ToList();
        decimal Sum(IEnumerable<decimal> values) => Money.Round(values.Sum(), currency);

        var pendingApproval = Sum(inCurrency.Where(e => e.Status == EarningStatus.PendingApproval).Select(e => e.SettlementAmount));
        var approvedRows = inCurrency.Where(e => e.Status == EarningStatus.Approved).ToList();
        var approved = Sum(approvedRows.Select(e => e.SettlementAmount));
        var onHold = Sum(approvedRows.Where(e => e.AvailableAt is null || e.AvailableAt > now).Select(e => e.SettlementAmount));
        var availableNext = Sum(approvedRows.Where(e => e.AvailableAt is not null && e.AvailableAt <= next.CutoffUtc).Select(e => e.SettlementAmount));
        var scheduled = Sum(inCurrency.Where(e => e.Status == EarningStatus.Scheduled).Select(e => e.SettlementAmount));
        var paid = Sum(inCurrency.Where(e => e.Status == EarningStatus.Paid).Select(e => e.SettlementAmount));
        var reversed = Sum(inCurrency
            .Where(e => e.Type == EarningType.Reversal || (e.Status == EarningStatus.Reversed && !e.HasLeg))
            .Select(e => Math.Abs(e.SettlementAmount)));
        var lifetime = Sum(inCurrency
            .Where(e => e.Status is EarningStatus.Approved or EarningStatus.Scheduled or EarningStatus.Paid)
            .Select(e => e.SettlementAmount));

        // Open submissions (estimate only; the real earning is created on approval).
        var openSubmissions = await db.Set<Submission>().AsNoTracking()
            .Where(s => s.UserId == userId && (s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.UnderReview))
            .GroupBy(s => s.RewardCurrency)
            .Select(g => new { Currency = g.Key, Amount = g.Sum(s => s.EstimatedRewardAmount) })
            .ToListAsync(ct);
        var pendingByCurrency = new List<PendingCurrencyDto>();
        var pendingSubmissions = 0m;
        foreach (var group in openSubmissions.OrderBy(g => g.Currency))
        {
            try
            {
                var rate = await rates.GetRateAsync(group.Currency, currency, now, ct);
                pendingSubmissions += Money.Convert(group.Amount, rate.Rate, currency);
                pendingByCurrency.Add(new PendingCurrencyDto(group.Currency, group.Amount, true));
            }
            catch (DomainException ex) when (ex.Code == "fx.rate_missing")
            {
                pendingByCurrency.Add(new PendingCurrencyDto(group.Currency, group.Amount, false));
            }
        }

        var byCurrency = entries
            .GroupBy(e => e.Currency)
            .OrderBy(g => g.Key)
            .Select(g => new CurrencyBreakdownDto(
                g.Key,
                Money.Round(g.Where(e => e.Status == EarningStatus.PendingApproval).Sum(e => e.Amount), g.Key),
                Money.Round(g.Where(e => e.Status == EarningStatus.Approved).Sum(e => e.Amount), g.Key),
                Money.Round(g.Where(e => e.Status == EarningStatus.Scheduled).Sum(e => e.Amount), g.Key),
                Money.Round(g.Where(e => e.Status == EarningStatus.Paid).Sum(e => e.Amount), g.Key),
                Money.Round(g.Where(e => e.Type == EarningType.Reversal || (e.Status == EarningStatus.Reversed && !e.HasLeg))
                    .Sum(e => Math.Abs(e.Amount)), g.Key)))
            .ToList();

        var activeHold = await db.Set<PayoutHold>().AsNoTracking().AnyAsync(h => h.UserId == userId && h.ReleasedAt == null, ct);
        var meetsMinimum = availableNext > 0 && availableNext >= schedule.MinimumPayoutAmount;
        var estimated = meetsMinimum && !activeHold ? availableNext : 0m;

        return new EarningsSummaryDto(
            currency,
            Money.Round(pendingSubmissions + pendingApproval, currency),
            approved,
            onHold,
            scheduled,
            paid,
            reversed,
            availableNext,
            lifetime,
            new NextPayoutDto(next.PeriodKey, next.CutoffUtc, next.PaymentDate, schedule.MinimumPayoutAmount, meetsMinimum, estimated),
            activeHold,
            activeHold ? NeutralHoldMessage : null,
            pendingByCurrency,
            byCurrency);
    }
}
