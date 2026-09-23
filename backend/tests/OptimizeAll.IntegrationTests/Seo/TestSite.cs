using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Modules.Seo.Crawling;
using OptimizeAll.Api.Modules.Seo.Http;

namespace OptimizeAll.IntegrationTests.Seo;

/// <summary>
/// A throwaway HTTP site on a random loopback port for crawler tests (no internet access). Responses are registered per
/// path (+query); unknown paths answer 404. Tracks hits and the peak number of concurrent requests.
/// </summary>
public sealed class TestSite : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, Func<HttpContext, Task>> _routes = new();
    private int _inFlight;

    public string BaseUrl { get; private set; } = string.Empty;
    public int Port { get; private set; }
    public ConcurrentDictionary<string, int> Hits { get; } = new();
    public int MaxConcurrent { get; private set; }

    private TestSite()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        _app.Run(async ctx =>
        {
            var key = ctx.Request.Path.Value + ctx.Request.QueryString.Value;
            Hits.AddOrUpdate(key, 1, (_, n) => n + 1);
            var now = Interlocked.Increment(ref _inFlight);
            lock (this) MaxConcurrent = Math.Max(MaxConcurrent, now);
            try
            {
                if (_routes.TryGetValue(key, out var handler) || _routes.TryGetValue(ctx.Request.Path.Value ?? "/", out handler)) await handler(ctx);
                else ctx.Response.StatusCode = 404;
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        });
    }

    public static async Task<TestSite> StartAsync()
    {
        var site = new TestSite();
        await site._app.StartAsync();
        var address = site._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        site.BaseUrl = address.TrimEnd('/');
        site.Port = new Uri(site.BaseUrl).Port;
        return site;
    }

    public TestSite Html(string path, string html, int status = 200, int delayMs = 0, IDictionary<string, string>? headers = null) =>
        Route(path, async ctx =>
        {
            if (delayMs > 0) await Task.Delay(delayMs);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            foreach (var (k, v) in headers ?? new Dictionary<string, string>()) ctx.Response.Headers[k] = v;
            if (!HttpMethods.IsHead(ctx.Request.Method)) await ctx.Response.WriteAsync(html);
        });

    public TestSite Text(string path, string body, string contentType = "text/plain", int status = 200) =>
        Route(path, async ctx =>
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = contentType;
            if (!HttpMethods.IsHead(ctx.Request.Method)) await ctx.Response.WriteAsync(body);
        });

    public TestSite Redirect(string path, string location, int status = 301) =>
        Route(path, ctx =>
        {
            ctx.Response.StatusCode = status;
            ctx.Response.Headers.Location = location;
            return Task.CompletedTask;
        });

    public TestSite Status(string path, int status) => Route(path, ctx => { ctx.Response.StatusCode = status; return Task.CompletedTask; });

    public TestSite Route(string path, Func<HttpContext, Task> handler)
    {
        _routes[path] = handler;
        return this;
    }

    public string Url(string path) => BaseUrl + path;

    public int HitCount(string path) => Hits.TryGetValue(path, out var n) ? n : 0;

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>Maps chosen host names to addresses (falls back to real DNS); can change answers between calls (rebinding).</summary>
public sealed class MapResolver(Func<string, int, IPAddress[]?> map) : IHostResolver
{
    private readonly ConcurrentDictionary<string, int> _calls = new();
    private readonly DnsHostResolver _dns = new();

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        var call = _calls.AddOrUpdate(host, 1, (_, n) => n + 1);
        return map(host, call) is { } addresses ? Task.FromResult(addresses) : _dns.ResolveAsync(host, ct);
    }
}

public static class CrawlerKit
{
    public static SeoCrawlerOptions Options(bool allowLoopback = true) => new()
    {
        AllowLoopback = allowLoopback, DelayMilliseconds = 0, RequestTimeoutSeconds = 60, MaxExternalLinkChecks = 50,
    };

    public static (SafeHttpFetcher Fetcher, SiteCrawler Crawler) Create(SeoCrawlerOptions? options = null, IHostResolver? resolver = null)
    {
        var monitor = new StaticOptionsMonitor<SeoCrawlerOptions>(options ?? Options());
        var services = new ServiceCollection();
        services.AddSingleton<IOptionsMonitor<SeoCrawlerOptions>>(monitor);
        services.AddSingleton(resolver ?? new DnsHostResolver());
        var sp = services.BuildServiceProvider();
        var fetcher = new SafeHttpFetcher(new HttpClient(SafeHttpFetcher.CreateHandler(sp)), sp.GetRequiredService<IHostResolver>(), monitor);
        return (fetcher, new SiteCrawler(fetcher, monitor, NullLogger<SiteCrawler>.Instance));
    }
}
