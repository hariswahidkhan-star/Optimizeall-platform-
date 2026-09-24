using Microsoft.AspNetCore.Mvc.Testing;

namespace OptimizeAll.IntegrationTests.Infrastructure;

/// <summary>
/// Starts test hosts without blocking xUnit's worker threads.
/// <para>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> only starts a host synchronously (the first <c>Services</c> or
/// <c>CreateClient()</c> blocks until startup, i.e. migrations and seeding, has finished). With
/// <c>xUnit.MaxParallelThreads=4</c> every test continuation runs on one of four xUnit worker threads, so four
/// fixtures booting MySQL hosts at once (minutes each under load) left no thread to resume any running test: requests
/// whose response had already been written timed out after <c>HttpClient.Timeout</c> waiting to be read. Hosts are
/// therefore started on a dedicated thread (never a worker or thread-pool thread), awaited asynchronously, and at most
/// a few start at a time so a whole test run does not migrate every database at once.
/// </para>
/// </summary>
public static class HostStartup
{
    private static readonly SemaphoreSlim Concurrent = new(Math.Max(2, Environment.ProcessorCount));

    /// <summary>Starts the host (if it isn't running yet) without blocking the calling thread.</summary>
    public static async Task StartAsync(this WebApplicationFactory<Program> factory)
    {
        await Concurrent.WaitAsync();
        try
        {
            await Task.Factory.StartNew(() => _ = factory.Services, CancellationToken.None,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }
        finally
        {
            Concurrent.Release();
        }
    }
}
