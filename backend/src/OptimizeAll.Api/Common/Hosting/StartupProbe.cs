using Microsoft.AspNetCore.Hosting.Server;

namespace OptimizeAll.Api.Common.Hosting;

/// <summary>
/// While the database initializes (migrations, a baseline upgrade, the Baseline/Demo seed: about a minute on a 0.5-CPU
/// host), a minimal Kestrel app answers on the API's own addresses: <c>/health/live</c> → 200, everything else → 503
/// with <c>Retry-After</c>. Platforms that probe the port or a liveness path (Render, Kubernetes, the image's
/// HEALTHCHECK) then see a live process instead of a closed port, and nginx gets a clear "starting" answer. It is
/// stopped right before the real server binds the same addresses. Readiness (<c>/health/ready</c>) stays 503 until
/// the real app runs. Only with Kestrel and plain-HTTP addresses (not under TestServer); <c>Hosting:StartupProbe=false</c>
/// switches it off.
/// </summary>
public sealed class StartupProbe : IAsyncDisposable
{
    private readonly WebApplication? _probe;
    private readonly ILogger _logger;

    private StartupProbe(WebApplication? probe, ILogger logger)
    {
        _probe = probe;
        _logger = logger;
    }

    public static async Task<StartupProbe> StartAsync(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        var server = app.Services.GetService<IServer>();
        var urls = Urls(app.Configuration);
        if (!app.Configuration.GetValue("Hosting:StartupProbe", true) || urls.Length == 0
            || server?.GetType().Assembly.GetName().Name != "Microsoft.AspNetCore.Server.Kestrel.Core"
            || urls.Any(u => !u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
            return new StartupProbe(null, logger);

        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = app.Environment.EnvironmentName });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls(urls);
            var probe = builder.Build();
            probe.MapGet("/health/live", () => Results.Text("Healthy", "text/plain"));
            probe.MapFallback("{**path}", async context =>
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.RetryAfter = "10";
                context.Response.Headers.CacheControl = "no-store";
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("The API is starting (database initialization). Try again in a few seconds.\n");
            });
            await probe.StartAsync();
            logger.LogInformation("Startup: answering /health/live on {Urls} while the database initializes", string.Join(", ", urls));
            return new StartupProbe(probe, logger);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // Never block startup on the probe: the real server binds the address later either way.
            logger.LogWarning(ex, "Startup: the startup probe could not listen on {Urls}; continuing without it", string.Join(", ", urls));
            return new StartupProbe(null, logger);
        }
    }

    private static string[] Urls(IConfiguration configuration)
    {
        var urls = configuration[WebHostDefaults.ServerUrlsKey];
        if (!string.IsNullOrWhiteSpace(urls))
            return urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ports = configuration[WebHostDefaults.HttpPortsKey];
        return string.IsNullOrWhiteSpace(ports)
            ? Array.Empty<string>()
            : ports.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => $"http://*:{p}").ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (_probe is null) return;
        await _probe.StopAsync();
        await _probe.DisposeAsync();
        _logger.LogInformation("Startup: startup probe stopped; the API takes over its addresses");
    }
}
