using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Payouts;

/// <summary>
/// Atomic, race-safe database operations shared by the payout services. Every state transition is a conditional
/// update with the expected current state in the WHERE clause; callers compare affected-row counts.
/// ExecuteUpdate bypasses the EarningEntry immutability guard; that is safe here because only lifecycle columns
/// (Status, PayoutItemId, PaidAt, ConcurrencyStamp) are ever set.
/// </summary>
public static class PayoutStore
{
    /// <summary>
    /// Serializes batch preparation across API instances and the background job with a MySQL named lock scoped to
    /// the current database. Opens the connection explicitly so GET_LOCK/RELEASE_LOCK run on the same session.
    /// </summary>
    public static async Task<IAsyncDisposable> AcquirePrepareLockAsync(AppDbContext db, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var result = await ScalarAsync(db, "SELECT GET_LOCK(LEFT(CONCAT('oa:payout-prepare:', DATABASE()), 64), 30)", ct);
            if (result is null || Convert.ToInt64(result) != 1)
                throw DomainException.Conflict("payout.prepare_busy",
                    "Another payout batch preparation is in progress. Try again in a moment.");
            return new NamedLock(db);
        }
        catch
        {
            await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    private sealed class NamedLock(AppDbContext db) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await ScalarAsync(db, "SELECT RELEASE_LOCK(LEFT(CONCAT('oa:payout-prepare:', DATABASE()), 64))", CancellationToken.None);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static async Task<object?> ScalarAsync(AppDbContext db, string sql, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        var value = await command.ExecuteScalarAsync(ct);
        return value is DBNull ? null : value;
    }

    /// <summary>Takes a row lock on the batch for the rest of the transaction and returns its current status.</summary>
    public static async Task<PayoutBatchStatus?> LockBatchAsync(AppDbContext db, Guid batchId, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQuery<string>($"SELECT `Status` AS `Value` FROM payout_batches WHERE `Id` = {batchId.ToString()} FOR UPDATE")
            .ToListAsync(ct);
        return rows.Count == 0 ? null : Enum.Parse<PayoutBatchStatus>(rows[0]);
    }

    /// <summary>
    /// Conditional Draft check that also invalidates the batch's concurrency stamp (so a reviewer's pending finalize
    /// with the old stamp fails) and row-locks the batch until commit.
    /// </summary>
    public static async Task ClaimDraftAsync(AppDbContext db, Guid batchId, DateTime now, CancellationToken ct)
    {
        var claimed = await db.Set<PayoutBatch>()
            .Where(b => b.Id == batchId && b.Status == PayoutBatchStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.ConcurrencyStamp, Guid.NewGuid()).SetProperty(b => b.UpdatedAt, now), ct);
        if (claimed == 1) return;
        if (!await db.Set<PayoutBatch>().AnyAsync(b => b.Id == batchId, ct)) throw DomainException.NotFound("PayoutBatch");
        throw DomainException.Conflict("payout.not_draft", "Only draft batches can be changed.");
    }

    /// <summary>Attaches earnings to an item: Approved → Scheduled, only if still Approved and unclaimed.</summary>
    public static Task<int> AttachEarningsAsync(AppDbContext db, Guid itemId, IReadOnlyCollection<Guid> earningIds, CancellationToken ct) =>
        db.Set<EarningEntry>()
            .Where(e => earningIds.Contains(e.Id) && e.Status == EarningStatus.Approved && e.PayoutItemId == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, EarningStatus.Scheduled)
                .SetProperty(e => (Guid?)e.PayoutItemId, itemId)
                .SetProperty(e => e.ConcurrencyStamp, Guid.NewGuid()), ct);

    /// <summary>Returns the scheduled earnings of the given items to Approved (payable in a later batch).</summary>
    public static Task<int> ReleaseEarningsAsync(AppDbContext db, IReadOnlyCollection<Guid> itemIds, CancellationToken ct) =>
        itemIds.Count == 0
            ? Task.FromResult(0)
            : db.Set<EarningEntry>()
                .Where(e => e.PayoutItemId != null && itemIds.Contains(e.PayoutItemId.Value) && e.Status == EarningStatus.Scheduled)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, EarningStatus.Approved)
                    .SetProperty(e => e.PayoutItemId, (Guid?)null)
                    .SetProperty(e => e.ConcurrencyStamp, Guid.NewGuid()), ct);

    /// <summary>Recomputes ItemCount/TotalAmount from the payable (not Held/Cancelled) items.</summary>
    public static async Task RecomputeTotalsAsync(AppDbContext db, Guid batchId, DateTime now, CancellationToken ct)
    {
        var payable = await db.Set<PayoutItem>().AsNoTracking()
            .Where(i => i.BatchId == batchId && i.Status != PayoutItemStatus.Held && i.Status != PayoutItemStatus.Cancelled)
            .Select(i => i.Amount).ToListAsync(ct);
        var count = payable.Count;
        var total = payable.Sum();
        await db.Set<PayoutBatch>().Where(b => b.Id == batchId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.ItemCount, count).SetProperty(b => b.TotalAmount, total)
                .SetProperty(b => b.UpdatedAt, now), ct);
    }

    /// <summary>Finalized → Completed once no item is Pending or AwaitingPayment. Returns true when it transitioned.</summary>
    public static async Task<bool> CompleteIfDoneAsync(AppDbContext db, Guid batchId, DateTime now, CancellationToken ct)
    {
        var open = await db.Set<PayoutItem>().AnyAsync(i => i.BatchId == batchId &&
            (i.Status == PayoutItemStatus.Pending || i.Status == PayoutItemStatus.AwaitingPayment), ct);
        if (open) return false;
        var updated = await db.Set<PayoutBatch>()
            .Where(b => b.Id == batchId && b.Status == PayoutBatchStatus.Finalized)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, PayoutBatchStatus.Completed)
                .SetProperty(b => b.CompletedAt, now).SetProperty(b => b.UpdatedAt, now)
                .SetProperty(b => b.ConcurrencyStamp, Guid.NewGuid()), ct);
        return updated == 1;
    }

    /// <summary>
    /// Holds a draft item because one of its earnings must be reversed, and releases ALL its earnings back to Approved
    /// (exactly what finalize does for held items), so the caller can reverse the earning in the same transaction.
    /// The batch is claimed as a Draft first (row-locked, concurrency stamp rotated); a batch finalized meanwhile makes
    /// this fail with 409 <c>payout.not_draft</c>. Returns true when the item was Pending and is now Held.
    /// </summary>
    public static async Task<bool> HoldAndReleaseDraftItemAsync(
        AppDbContext db, Guid batchId, Guid itemId, string reason, DateTime now, CancellationToken ct)
    {
        await ClaimDraftAsync(db, batchId, now, ct);
        var newlyHeld = await db.Set<PayoutItem>()
            .Where(i => i.Id == itemId && i.BatchId == batchId && i.Status == PayoutItemStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, PayoutItemStatus.Held).SetProperty(i => i.HoldReason, reason)
                .SetProperty(i => i.UpdatedAt, now).SetProperty(i => i.ConcurrencyStamp, Guid.NewGuid()), ct) == 1;
        var status = await db.Set<PayoutItem>().Where(i => i.Id == itemId).Select(i => i.Status).FirstAsync(ct);
        if (status != PayoutItemStatus.Held)
            throw DomainException.Conflict("payout.item_state", $"The payout item is {status} and cannot be held.");
        await ReleaseEarningsAsync(db, new[] { itemId }, ct);
        await RecomputeTotalsAsync(db, batchId, now, ct);
        return newlyHeld;
    }

    /// <summary>
    /// Participants among <paramref name="userIds"/> who must not be paid right now, with the reason: an active payout
    /// hold ("Payout hold: …") or an account that is not Active ("Account not active (Suspended)").
    /// </summary>
    public static async Task<Dictionary<Guid, string>> PayoutBlockersAsync(AppDbContext db, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, string>();
        if (userIds.Count == 0) return result;
        var holds = await db.Set<PayoutHold>().AsNoTracking()
            .Where(h => userIds.Contains(h.UserId) && h.ReleasedAt == null)
            .OrderBy(h => h.CreatedAt).Select(h => new { h.UserId, h.Reason }).ToListAsync(ct);
        foreach (var h in holds) result.TryAdd(h.UserId, Truncate("Payout hold: " + h.Reason));
        var inactive = await db.Set<User>().AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.Status != UserStatus.Active)
            .Select(u => new { u.Id, u.Status }).ToListAsync(ct);
        foreach (var u in inactive) result.TryAdd(u.Id, $"Account not active ({u.Status})");
        return result;
    }

    private static string Truncate(string value) => value.Length <= 1000 ? value : value[..1000];

    /// <summary>Scheduled earnings among <paramref name="earningIds"/> with the item and batch that hold them.</summary>
    public static Task<List<ScheduledEarningRef>> FindScheduledAsync(AppDbContext db, IReadOnlyCollection<Guid> earningIds, CancellationToken ct) =>
        (from e in db.Set<EarningEntry>().AsNoTracking()
         join i in db.Set<PayoutItem>() on e.PayoutItemId equals i.Id
         join b in db.Set<PayoutBatch>() on i.BatchId equals b.Id
         where earningIds.Contains(e.Id) && e.Status == EarningStatus.Scheduled
         select new ScheduledEarningRef(e.Id, i.Id, b.Id, b.Reference, b.Status, i.Status, i.UserId)).ToListAsync(ct);

    public static Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
}

/// <summary>Payouts-module implementation of <see cref="IPayoutReversalCoordinator"/>.</summary>
public sealed class PayoutReversalCoordinator(AppDbContext db, IAuditLogger audit, TimeProvider clock) : IPayoutReversalCoordinator
{
    public async Task<IReadOnlyList<ScheduledEarningRef>> FindScheduledAsync(IReadOnlyCollection<Guid> earningIds, CancellationToken ct = default) =>
        earningIds.Count == 0 ? Array.Empty<ScheduledEarningRef>() : await PayoutStore.FindScheduledAsync(db, earningIds, ct);

    public async Task HoldForReversalAsync(ScheduledEarningRef earning, string holdReason, CancellationToken ct = default)
    {
        if (earning.BatchStatus != PayoutBatchStatus.Draft)
            throw DomainException.Conflict("payout.not_draft", "Only items of draft batches can be held.");
        var now = clock.GetUtcNow().UtcDateTime;
        var newlyHeld = await PayoutStore.HoldAndReleaseDraftItemAsync(db, earning.BatchId, earning.ItemId, holdReason, now, ct);
        audit.Record("payout.item_held", nameof(PayoutItem), earning.ItemId,
            after: new { earning.BatchId, earning.BatchReference, Automatic = true, WasPending = newlyHeld, ReleasedForEarning = earning.EarningId },
            reason: holdReason);
    }
}
