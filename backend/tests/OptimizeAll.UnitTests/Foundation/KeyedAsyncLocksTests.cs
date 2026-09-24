using OptimizeAll.Api.Common.Persistence;

namespace OptimizeAll.UnitTests.Foundation;

/// <summary>The in-process named locks behind the SQLite dialect's AcquireNamedLockAsync.</summary>
public sealed class KeyedAsyncLocksTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Entries_are_removed_once_released_so_distinct_names_do_not_accumulate()
    {
        var locks = new KeyedAsyncLocks();
        for (var i = 0; i < 1000; i++)
        {
            var handle = await locks.TryAcquireAsync($"time:{Guid.NewGuid()}", Long, CancellationToken.None);
            Assert.NotNull(handle);
            Assert.Equal(1, locks.Count);
            await handle!.DisposeAsync();
            Assert.Equal(0, locks.Count);
        }

        var held = new List<IAsyncDisposable>();
        for (var i = 0; i < 100; i++) held.Add((await locks.TryAcquireAsync($"ads:{i}", Long, CancellationToken.None))!);
        Assert.Equal(100, locks.Count);
        foreach (var h in held) await h.DisposeAsync();
        Assert.Equal(0, locks.Count);
    }

    [Fact]
    public async Task Contention_still_serializes_and_the_entry_is_removed_afterwards()
    {
        var locks = new KeyedAsyncLocks();
        var inside = 0;
        var maxInside = 0;
        var total = 0;

        var workers = Enumerable.Range(0, 40).Select(_ => Task.Run(async () =>
        {
            await using var handle = await locks.TryAcquireAsync("shared", Long, CancellationToken.None)
                                     ?? throw new TimeoutException();
            var now = Interlocked.Increment(ref inside);
            InterlockedMax(ref maxInside, now);
            var read = total;
            await Task.Delay(1);
            total = read + 1; // lost updates unless serialized
            Interlocked.Decrement(ref inside);
        })).ToArray();
        await Task.WhenAll(workers);

        Assert.Equal(1, maxInside);
        Assert.Equal(40, total);
        Assert.Equal(0, locks.Count);
    }

    [Fact]
    public async Task Timeouts_and_cancellations_release_their_reference()
    {
        var locks = new KeyedAsyncLocks();
        var holder = await locks.TryAcquireAsync("k", Long, CancellationToken.None);
        Assert.NotNull(holder);

        Assert.Null(await locks.TryAcquireAsync("k", TimeSpan.FromMilliseconds(50), CancellationToken.None));
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => locks.TryAcquireAsync("k", Long, cts.Token));
        Assert.Equal(1, locks.Count); // still held

        // A waiter queued behind the holder gets the same lock once it is released.
        var waiter = locks.TryAcquireAsync("k", Long, CancellationToken.None);
        Assert.False(waiter.IsCompleted);
        await holder!.DisposeAsync();
        await using (var second = await waiter)
        {
            Assert.NotNull(second);
            Assert.True(locks.Contains("k"));
        }
        Assert.Equal(0, locks.Count);

        // Disposing twice does not release someone else's lock.
        var again = (await locks.TryAcquireAsync("k", Long, CancellationToken.None))!;
        await again.DisposeAsync();
        var third = (await locks.TryAcquireAsync("k", Long, CancellationToken.None))!;
        await again.DisposeAsync();
        Assert.Null(await locks.TryAcquireAsync("k", TimeSpan.FromMilliseconds(20), CancellationToken.None));
        await third.DisposeAsync();
        Assert.Equal(0, locks.Count);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value &&
               Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
