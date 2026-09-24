using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Payouts;

/// <summary>An approved, unpaid earning eligible by date/currency for a batch (input to <see cref="PayoutPlanner"/>).</summary>
public sealed record PlannerEarning(Guid EarningId, Guid UserId, decimal SettlementAmount);

/// <summary>Per-user facts needed to decide whether a participant is paid in a batch.</summary>
public sealed record PlannerParticipant(Guid UserId, bool IsActive, bool HasActiveHold, bool HasPayoutProfile, bool IsTestAccount = false);

public enum PayoutExclusionReason
{
    /// <summary>The participant has an active payout hold.</summary>
    PayoutHold,
    /// <summary>The participant's account is suspended or deactivated.</summary>
    AccountInactive,
    /// <summary>Clawbacks/debits exceed credits; the negative balance carries over.</summary>
    NonPositiveBalance,
    /// <summary>Net balance is below the schedule's minimum payout amount; carried over.</summary>
    BelowMinimum,
    /// <summary>A test account (QA/demo): never paid, whatever its balance.</summary>
    TestAccount,
}

public sealed record PlannedItem(Guid UserId, decimal Amount, IReadOnlyList<Guid> EarningIds, bool HeldForMissingPayoutProfile);

public sealed record PlannedExclusion(Guid UserId, PayoutExclusionReason Reason, decimal Amount, int EarningCount);

public sealed record PayoutPlan(IReadOnlyList<PlannedItem> Items, IReadOnlyList<PlannedExclusion> Exclusions);

/// <summary>
/// Pure grouping rules for a payout batch. Per participant: net = Σ settlement amounts (credits and clawbacks).
/// Precedence: test account → active hold → inactive account → net ≤ 0 → net &lt; minimum → (missing payout profile ⇒ Held item)
/// → payable item. Amounts are rounded with <see cref="Money.Round"/> in the batch currency.
/// </summary>
public static class PayoutPlanner
{
    public const string MissingPayoutProfileReason = "No payout details on file";

    public static PayoutPlan Plan(
        IEnumerable<PlannerEarning> earnings,
        IReadOnlyDictionary<Guid, PlannerParticipant> participants,
        decimal minimumPayoutAmount,
        string currency)
    {
        var items = new List<PlannedItem>();
        var exclusions = new List<PlannedExclusion>();

        foreach (var group in earnings.GroupBy(e => e.UserId).OrderBy(g => g.Key))
        {
            var net = Money.Round(group.Sum(e => e.SettlementAmount), currency);
            var count = group.Count();
            participants.TryGetValue(group.Key, out var p);
            p ??= new PlannerParticipant(group.Key, IsActive: false, HasActiveHold: false, HasPayoutProfile: false);

            PayoutExclusionReason? reason =
                p.IsTestAccount ? PayoutExclusionReason.TestAccount :
                p.HasActiveHold ? PayoutExclusionReason.PayoutHold :
                !p.IsActive ? PayoutExclusionReason.AccountInactive :
                net <= 0 ? PayoutExclusionReason.NonPositiveBalance :
                net < minimumPayoutAmount ? PayoutExclusionReason.BelowMinimum :
                null;

            if (reason is not null)
            {
                exclusions.Add(new PlannedExclusion(group.Key, reason.Value, net, count));
                continue;
            }

            items.Add(new PlannedItem(group.Key, net, group.Select(e => e.EarningId).ToList(), !p.HasPayoutProfile));
        }

        return new PayoutPlan(items, exclusions);
    }
}
