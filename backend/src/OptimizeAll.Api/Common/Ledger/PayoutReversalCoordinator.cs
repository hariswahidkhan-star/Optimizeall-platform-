using OptimizeAll.Domain.Payouts;

namespace OptimizeAll.Api.Common.Ledger;

/// <summary>A Scheduled earning and the payout item/batch it is attached to.</summary>
public sealed record ScheduledEarningRef(
    Guid EarningId, Guid ItemId, Guid BatchId, string BatchReference, PayoutBatchStatus BatchStatus, PayoutItemStatus ItemStatus, Guid UserId);

/// <summary>
/// Lets other modules (review, referrals) reverse an earning that is Scheduled in a payout batch. Implemented by the
/// Payouts module, which owns payout items. All operations join the caller's current transaction.
/// </summary>
public interface IPayoutReversalCoordinator
{
    /// <summary>The Scheduled earnings among <paramref name="earningIds"/> with their item and batch.</summary>
    Task<IReadOnlyList<ScheduledEarningRef>> FindScheduledAsync(IReadOnlyCollection<Guid> earningIds, CancellationToken ct = default);

    /// <summary>
    /// For an earning in a <b>Draft</b> batch: holds the participant's item with <paramref name="holdReason"/> and releases
    /// all its earnings back to Approved (unlinked), so the caller can reverse the earning in the same transaction.
    /// Audited as <c>payout.item_held</c>. Throws 409 <c>payout.not_draft</c> if the batch was finalized meanwhile.
    /// Callers holding a tracked copy of a released earning must reload it before changing it.
    /// </summary>
    Task HoldForReversalAsync(ScheduledEarningRef earning, string holdReason, CancellationToken ct = default);
}
