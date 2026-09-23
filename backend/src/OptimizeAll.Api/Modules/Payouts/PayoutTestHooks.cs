namespace OptimizeAll.Api.Modules.Payouts;

/// <summary>
/// Deterministic interleaving points for integration tests of race conditions. Registered as a singleton; every hook
/// is null in production, so invoking it is a no-op.
/// </summary>
public sealed class PayoutTestHooks
{
    /// <summary>
    /// Runs inside <see cref="PayoutBatchService.PrepareAsync"/> after eligibility (payout holds, account status) was
    /// read and before the batch is written, while the prepare named lock and transaction are held.
    /// </summary>
    public Func<CancellationToken, Task>? AfterPrepareSelection { get; set; }

    internal static Task InvokeAsync(Func<CancellationToken, Task>? hook, CancellationToken ct) =>
        hook is null ? Task.CompletedTask : hook(ct);
}
