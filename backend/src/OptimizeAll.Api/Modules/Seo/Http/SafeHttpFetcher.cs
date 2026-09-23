using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace OptimizeAll.Api.Modules.Seo.Http;

/// <summary>
/// Crawler/fetcher settings (<c>Seo:Crawler</c>). <see cref="AllowLoopback"/> exists only so tests can crawl a local
/// test site; it never opens private, link-local or metadata ranges.
/// </summary>
public sealed class SeoCrawlerOptions
{
    public const string Section = "Seo:Crawler";

    public string UserAgent { get; set; } = "Mozilla/5.0 (compatible; OptimizeAllBot/1.0; +https://optimizeall.app/bot)";

    /// <summary>Product token matched against robots.txt User-agent groups.</summary>
    public string RobotsToken { get; set; } = "OptimizeAllBot";

    /// <summary>
    /// Permit 127.0.0.0/8 and ::1 (tests only). Private, link-local and metadata addresses stay blocked. Honoured only in
    /// the "Testing" and "Development" environments (see <see cref="LoopbackPermittedIn"/>); elsewhere it is forced off.
    /// </summary>
    public bool AllowLoopback { get; set; }

    /// <summary>Environments in which <see cref="AllowLoopback"/> may take effect.</summary>
    public static bool LoopbackPermittedIn(IHostEnvironment environment) =>
        environment.IsEnvironment("Testing") || environment.IsDevelopment();

    public int RequestTimeoutSeconds { get; set; } = 15;
    public long MaxBodyBytes { get; set; } = 2 * 1024 * 1024;
    public int MaxRedirects { get; set; } = 5;
    public int PerHostConcurrency { get; set; } = 2;

    /// <summary>Pause between request batches to the same host (robots.txt Crawl-delay wins when larger).</summary>
    public int DelayMilliseconds { get; set; } = 500;
    public int MaxCrawlDelaySeconds { get; set; } = 10;
    public int DefaultMaxPages { get; set; } = 500;
    public int AbsoluteMaxPages { get; set; } = 2000;
    public int MaxExternalLinkChecks { get; set; } = 100;
    public int MaxSitemapUrls { get; set; } = 5000;
}

/// <summary>Blocks non-public destinations (SSRF). Applied to every resolved address, before and at connect time.</summary>
public static class IpPolicy
{
    private static readonly (IPAddress Network, int Prefix)[] BlockedV4 =
    {
        (IPAddress.Parse("0.0.0.0"), 8),        // "this" network
        (IPAddress.Parse("10.0.0.0"), 8),       // private
        (IPAddress.Parse("100.64.0.0"), 10),    // carrier-grade NAT (also Alibaba metadata 100.100.100.200)
        (IPAddress.Parse("169.254.0.0"), 16),   // link-local, incl. cloud metadata 169.254.169.254
        (IPAddress.Parse("172.16.0.0"), 12),    // private
        (IPAddress.Parse("192.0.0.0"), 24),     // IETF protocol assignments
        (IPAddress.Parse("192.0.2.0"), 24),     // TEST-NET-1
        (IPAddress.Parse("192.88.99.0"), 24),   // 6to4 relay anycast
        (IPAddress.Parse("192.168.0.0"), 16),   // private
        (IPAddress.Parse("198.18.0.0"), 15),    // benchmarking
        (IPAddress.Parse("198.51.100.0"), 24),  // TEST-NET-2
        (IPAddress.Parse("203.0.113.0"), 24),   // TEST-NET-3
        (IPAddress.Parse("224.0.0.0"), 4),      // multicast
        (IPAddress.Parse("240.0.0.0"), 4),      // reserved + broadcast
    };

    private static readonly (IPAddress Network, int Prefix)[] BlockedV6 =
    {
        (IPAddress.Parse("::"), 96),            // unspecified + deprecated IPv4-compatible
        (IPAddress.Parse("64:ff9b::"), 96),     // NAT64 (could reach internal IPv4)
        (IPAddress.Parse("64:ff9b:1::"), 48),   // local-use NAT64
        (IPAddress.Parse("100::"), 64),         // discard
        (IPAddress.Parse("2001::"), 32),        // Teredo
        (IPAddress.Parse("2001:db8::"), 32),    // documentation
        (IPAddress.Parse("2002::"), 16),        // 6to4 (embeds IPv4)
        (IPAddress.Parse("fc00::"), 7),         // unique local, incl. fd00:ec2::254 (AWS metadata)
        (IPAddress.Parse("fe80::"), 10),        // link-local
        (IPAddress.Parse("fec0::"), 10),        // site-local (deprecated)
        (IPAddress.Parse("ff00::"), 8),         // multicast
    };

    public static bool IsBlocked(IPAddress address, bool allowLoopback)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return !allowLoopback;
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return address.Equals(IPAddress.Broadcast) || BlockedV4.Any(n => InNetwork(address, n.Network, n.Prefix));
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
                   BlockedV6.Any(n => InNetwork(address, n.Network, n.Prefix));
        return true;
    }

    private static bool InNetwork(IPAddress address, IPAddress network, int prefix)
    {
        if (address.AddressFamily != network.AddressFamily) return false;
        var a = address.GetAddressBytes();
        var n = network.GetAddressBytes();
        var fullBytes = prefix / 8;
        for (var i = 0; i < fullBytes; i++)
            if (a[i] != n[i]) return false;
        var rest = prefix % 8;
        if (rest == 0) return true;
        var mask = (byte)(0xFF << (8 - rest));
        return (a[fullBytes] & mask) == (n[fullBytes] & mask);
    }
}

/// <summary>Raised when a destination resolves to a blocked address.</summary>
public sealed class SsrfBlockedException(string message) : Exception(message);

/// <summary>DNS lookup seam (tests substitute a resolver that maps names to chosen addresses).</summary>
public interface IHostResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct);
}

public sealed class DnsHostResolver : IHostResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) =>
        IPAddress.TryParse(host.Trim('[', ']'), out var literal)
            ? Task.FromResult(new[] { literal })
            : Dns.GetHostAddressesAsync(host, ct);
}

public enum FetchErrorKind
{
    None,
    InvalidUrl,
    Blocked,
    Timeout,
    Network,
    TooManyRedirects,
    RedirectLoop,
}

public sealed record RedirectHop(string From, int StatusCode, string To);

public sealed record FetchResult(
    string RequestedUrl, string FinalUrl, int? StatusCode, string? ContentType, byte[] Body, bool Truncated,
    long ContentLength, int ElapsedMs, IReadOnlyList<RedirectHop> Redirects, FetchErrorKind ErrorKind, string? Error,
    string? XRobotsTag)
{
    public bool IsSuccess => ErrorKind == FetchErrorKind.None && StatusCode is >= 200 and < 300;
    public bool IsHtml => ContentType is not null && (ContentType.Contains("text/html", StringComparison.OrdinalIgnoreCase) ||
                                                      ContentType.Contains("application/xhtml", StringComparison.OrdinalIgnoreCase));

    public string BodyText => System.Text.Encoding.UTF8.GetString(Body);
}

/// <summary>
/// SSRF-safe HTTP fetcher shared by the crawler, on-page analyzer and backlink checker. Only http(s) URLs without
/// credentials; the host is resolved and every address must be public (private, loopback, link-local, CGNAT,
/// multicast, documentation, NAT64/6to4/Teredo and metadata ranges are refused — IPv4 and IPv6). The connection itself
/// is opened to a validated address by <see cref="CreateHandler"/>'s ConnectCallback, so DNS rebinding between the check
/// and the connect cannot reach an internal host. Redirects are followed manually (each hop re-validated), loops and
/// long chains are reported, responses are capped at <see cref="SeoCrawlerOptions.MaxBodyBytes"/> and time out.
/// </summary>
public sealed class SafeHttpFetcher(HttpClient http, IHostResolver resolver, IOptionsMonitor<SeoCrawlerOptions> options)
{
    public const string HttpClientName = "seo-fetcher";

    private SeoCrawlerOptions O => options.CurrentValue;

    /// <summary>The primary handler for the typed client: no proxy, no cookies, manual redirects, validated connects.</summary>
    public static SocketsHttpHandler CreateHandler(IServiceProvider sp)
    {
        var monitor = sp.GetRequiredService<IOptionsMonitor<SeoCrawlerOptions>>();
        var resolver = sp.GetRequiredService<IHostResolver>();
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = async (context, ct) =>
            {
                var addresses = await ResolvePublicAsync(resolver, context.DnsEndPoint.Host, monitor.CurrentValue.AllowLoopback, ct);
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
    }

    /// <summary>Resolves a host and returns its addresses only when every one of them is public.</summary>
    public static async Task<IPAddress[]> ResolvePublicAsync(IHostResolver resolver, string host, bool allowLoopback, CancellationToken ct)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await resolver.ResolveAsync(host, ct);
        }
        catch (SocketException ex)
        {
            throw new HttpRequestException($"DNS lookup failed for {host}: {ex.SocketErrorCode}.", ex);
        }
        if (addresses.Length == 0) throw new HttpRequestException($"{host} has no addresses.");
        var blocked = addresses.FirstOrDefault(a => IpPolicy.IsBlocked(a, allowLoopback));
        if (blocked is not null)
            throw new SsrfBlockedException($"{host} resolves to a non-public address ({blocked}); requests to internal networks are not allowed.");
        return addresses;
    }

    /// <summary>Validates scheme/credentials/host literal; returns an error message or null.</summary>
    public static string? ValidateUrl(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri) return "Not an absolute URL.";
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return "Only http and https URLs can be fetched.";
        if (!string.IsNullOrEmpty(uri.UserInfo)) return "URLs with credentials are not allowed.";
        if (string.IsNullOrEmpty(uri.Host)) return "The URL has no host.";
        return null;
    }

    /// <summary>
    /// GET with optional <c>mayFollow</c> gate for redirect targets (the crawler passes its robots.txt rules). When it refuses
    /// a target, the target is not requested and the redirect response itself is returned (3xx, refused hop in Redirects).
    /// </summary>
    public Task<FetchResult> GetAsync(string url, CancellationToken ct, bool followRedirects = true, Func<Uri, bool>? mayFollow = null) =>
        FetchAsync(url, HttpMethod.Get, followRedirects, ct, mayFollow);

    public Task<FetchResult> HeadAsync(string url, CancellationToken ct) => FetchAsync(url, HttpMethod.Head, true, ct);

    public async Task<FetchResult> FetchAsync(string url, HttpMethod method, bool followRedirects, CancellationToken ct,
        Func<Uri, bool>? mayFollow = null)
    {
        var o = O;
        var redirects = new List<RedirectHop>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stopwatch = Stopwatch.StartNew();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var current))
            return Failure(url, url, FetchErrorKind.InvalidUrl, "Not an absolute URL.", redirects, stopwatch);
        if (ValidateUrl(current) is { } invalid)
            return Failure(url, url, FetchErrorKind.InvalidUrl, invalid, redirects, stopwatch);

        while (true)
        {
            visited.Add(current.AbsoluteUri);
            try
            {
                // Resolve up front for a clear "blocked" outcome; the handler re-checks at connect time.
                await ResolvePublicAsync(resolver, current.IdnHost, o.AllowLoopback, ct);
            }
            catch (SsrfBlockedException ex)
            {
                return Failure(url, current.AbsoluteUri, FetchErrorKind.Blocked, ex.Message, redirects, stopwatch);
            }
            catch (HttpRequestException ex)
            {
                return Failure(url, current.AbsoluteUri, FetchErrorKind.Network, ex.Message, redirects, stopwatch);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, o.RequestTimeoutSeconds)));
            using var request = new HttpRequestMessage(method, current);
            request.Headers.UserAgent.ParseAdd(o.UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Failure(url, current.AbsoluteUri, FetchErrorKind.Timeout, $"No response within {o.RequestTimeoutSeconds} s.", redirects, stopwatch);
            }
            catch (HttpRequestException ex) when (FindBlocked(ex) is { } blocked)
            {
                return Failure(url, current.AbsoluteUri, FetchErrorKind.Blocked, blocked.Message, redirects, stopwatch);
            }
            catch (HttpRequestException ex)
            {
                return Failure(url, current.AbsoluteUri, FetchErrorKind.Network, Short(ex.Message), redirects, stopwatch);
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (status is >= 300 and < 400 && response.Headers.Location is { } location && followRedirects)
                {
                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    redirects.Add(new RedirectHop(current.AbsoluteUri, status, next.AbsoluteUri));
                    if (ValidateUrl(next) is { } bad)
                        return Failure(url, next.OriginalString, FetchErrorKind.InvalidUrl, $"Redirect to an unsupported URL: {bad}", redirects, stopwatch, status);
                    if (visited.Contains(next.AbsoluteUri))
                        return Failure(url, next.AbsoluteUri, FetchErrorKind.RedirectLoop, "The redirects loop back to an earlier URL.", redirects, stopwatch, status);
                    if (redirects.Count > o.MaxRedirects)
                        return Failure(url, next.AbsoluteUri, FetchErrorKind.TooManyRedirects, $"More than {o.MaxRedirects} redirects.", redirects, stopwatch, status);
                    if (mayFollow is not null && !mayFollow(next))
                        return new FetchResult(url, current.AbsoluteUri, status, response.Content.Headers.ContentType?.ToString(), Array.Empty<byte>(),
                            false, 0, (int)stopwatch.ElapsedMilliseconds, redirects, FetchErrorKind.None, null,
                            response.Headers.TryGetValues("X-Robots-Tag", out var hopRobots) ? string.Join(", ", hopRobots) : null);
                    current = next;
                    continue;
                }

                byte[] body;
                bool truncated;
                try
                {
                    (body, truncated) = method == HttpMethod.Head
                        ? (Array.Empty<byte>(), false)
                        : await ReadBodyAsync(response, o.MaxBodyBytes, timeout.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return Failure(url, current.AbsoluteUri, FetchErrorKind.Timeout, $"The response did not finish within {o.RequestTimeoutSeconds} s.",
                        redirects, stopwatch, status);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    return Failure(url, current.AbsoluteUri, FetchErrorKind.Network, Short(ex.Message), redirects, stopwatch, status);
                }
                var xRobots = response.Headers.TryGetValues("X-Robots-Tag", out var values) ? string.Join(", ", values) : null;
                return new FetchResult(url, current.AbsoluteUri, status, response.Content.Headers.ContentType?.ToString(), body, truncated,
                    response.Content.Headers.ContentLength ?? body.LongLength, (int)stopwatch.ElapsedMilliseconds, redirects,
                    FetchErrorKind.None, null, xRobots);
            }
        }
    }

    private static async Task<(byte[] Body, bool Truncated)> ReadBodyAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read == 0) break;
            var allowed = (int)Math.Min(read, maxBytes - buffer.Length);
            buffer.Write(chunk, 0, allowed);
            if (buffer.Length >= maxBytes)
                return (buffer.ToArray(), true);
        }
        return (buffer.ToArray(), false);
    }

    private static SsrfBlockedException? FindBlocked(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is SsrfBlockedException blocked) return blocked;
        return null;
    }

    private static FetchResult Failure(string requested, string final, FetchErrorKind kind, string message,
        IReadOnlyList<RedirectHop> redirects, Stopwatch sw, int? status = null) =>
        new(requested, final, status, null, Array.Empty<byte>(), false, 0, (int)sw.ElapsedMilliseconds, redirects, kind, message, null);

    private static string Short(string s) => s.Length <= 300 ? s : s[..300];
}
