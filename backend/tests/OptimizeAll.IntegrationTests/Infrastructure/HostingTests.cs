using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Sdk;

namespace OptimizeAll.IntegrationTests.Infrastructure;

/// <summary>
/// Threads while API hosts start and run. Full MySQL runs timed out (requests whose response was already written
/// never finished reading) because host startup blocked every xUnit worker thread and every host parked a thread-pool
/// thread for its whole life.
/// </summary>
public sealed class HostingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    /// <summary>Records the synchronous call stack that starts the host's hosted services; optionally waits on a gate.</summary>
    private sealed class ProbeService(Task? gate) : IHostedService
    {
        public StackTrace? StartedFrom { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            StartedFrom = new StackTrace();
            if (gate is not null) await gate.WaitAsync(TimeSpan.FromSeconds(90), cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task The_entry_point_does_not_block_a_thread_for_the_life_of_the_host()
    {
        var probe = new ProbeService(gate: null);
        await using var host = api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IHostedService>(probe)));
        await host.StartAsync();

        // Program must end with `await app.RunAsync()`: the blocking `app.Run()` holds the thread that called it (a
        // thread-pool thread once Main has awaited the asynchronous MySQL initialization) until the host stops.
        var frames = probe.StartedFrom!.GetFrames().Select(f => f.GetMethod()).Where(m => m is not null).ToList();
        Assert.Contains(frames, m => m!.DeclaringType?.Name.StartsWith("<Program>", StringComparison.Ordinal) == true
            || m!.DeclaringType?.DeclaringType?.Name == nameof(Program) || m!.DeclaringType?.Name == nameof(Program));
        var blocking = frames.Where(m => m!.Name == "Run" &&
            (m.DeclaringType == typeof(HostingAbstractionsHostExtensions) || m.DeclaringType == typeof(WebApplication))).ToList();
        Assert.True(blocking.Count == 0, "The host was started by a blocking Run():\n" + probe.StartedFrom);
    }

    [Fact]
    public async Task Starting_a_host_leaves_the_calling_test_thread_free()
    {
        // The host cannot finish starting until the gate opens, and the gate is opened by work queued on the one
        // thread of a single-threaded xUnit context (as with xUnit.MaxParallelThreads, where every test continuation
        // needs one of a few worker threads). Blocking that thread while the host starts would deadlock.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = api.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IHostedService>(new ProbeService(gate.Task))));
        using var context = new MaxConcurrencySyncContext(1);
        var started = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(_ =>
        {
            var start = host.StartAsync();
            context.Post(_ => gate.TrySetResult(), null);
            started.SetResult(start);
        }, null);

        await Task.WhenAny(Task.WhenAll(started.Task, gate.Task), Task.Delay(TimeSpan.FromSeconds(60)));
        Assert.True(started.Task.IsCompleted && gate.Task.IsCompleted, "The single test thread stayed blocked while the host started.");
        await await started.Task; // may queue behind other hosts starting (HostStartup), so no deadline here
        Assert.Equal(401, (int)(await host.CreateClient().GetAsync("/api/v1/auth/me")).StatusCode);
    }
}
