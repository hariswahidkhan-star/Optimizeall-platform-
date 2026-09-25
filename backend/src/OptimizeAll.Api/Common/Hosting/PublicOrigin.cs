using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Modules.Website.Settings;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Hosting;

/// <summary>
/// The public origin of the web app (<c>https://host[:port]</c>, no trailing slash) used for every absolute link the API
/// builds: canonical URLs, sitemaps, llms.txt, JSON-LD, Open Graph, certificates and Open Badges, LinkedIn links, and
/// the links in emails. Resolved in this order:
/// <list type="number">
/// <item>the admin site setting <c>Seo.SiteUrl</c> (Website → Settings → SEO);</item>
/// <item>the configured <c>Email:AppBaseUrl</c>, when set and non-empty;</item>
/// <item>the origin of the current request as the visitor saw it (scheme and host after the ForwardedHeaders
/// middleware), but only when the connection came from a trusted reverse proxy (<c>Hosting:TrustedProxies</c> /
/// <c>Hosting:TrustedNetworks</c>) and the host is a plain, valid host name (and, when <c>Hosting:PublicHosts</c> is set,
/// one of those names): a client can never put its own Host or X-Forwarded-Host into links;</item>
/// <item>for background jobs and emails sent outside a request: the last such request origin, remembered in the
/// system setting row <see cref="PublicOrigin.SettingKey"/> (written at most once a day);</item>
/// <item>otherwise an empty string (links stay root-relative) and a warning in the log.</item>
/// </list>
/// Documented in docs/RENDER.md and docs/DEPLOYMENT.md.
/// </summary>
public interface IPublicOrigin
{
    /// <summary>The public origin (or "" when none is known). May load the site settings synchronously the first time.</summary>
    string Current { get; }

    /// <summary>The public origin (or "" when none is known).</summary>
    Task<string> GetAsync(CancellationToken ct = default);

    /// <summary>The public origin for a caller that already loaded the site settings (their <c>Seo.SiteUrl</c>).</summary>
    string Resolve(string? siteUrl);

    /// <summary>Called when the site settings are saved, so this instance uses the new site URL at once.</summary>
    void SiteUrlChanged(string? siteUrl);
}

public sealed partial class PublicOrigin(
    IConfiguration configuration, IHttpContextAccessor http, IServiceScopeFactory scopes, TimeProvider clock, ILogger<PublicOrigin> logger)
    : IPublicOrigin
{
    /// <summary>
    /// <c>system_settings</c> row (a JSON string such as "https://www.example.com") holding the last public origin seen on a
    /// request through a trusted proxy. Written by the API, not an administrator setting (not in <see cref="SettingKeys"/>).
    /// </summary>
    public const string SettingKey = "hosting.publicOrigin";

    /// <summary>HttpContext item set (before UseForwardedHeaders) when the connection came from a trusted proxy.</summary>
    public const string TrustedProxyItem = "OptimizeAll.PublicOrigin.TrustedProxy";

    /// <summary>How long the site URL and the remembered origin are cached (the site settings save updates it at once).</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    /// <summary>The remembered origin is written at most this often.</summary>
    public static readonly TimeSpan RememberEvery = TimeSpan.FromDays(1);

    private static readonly TimeSpan WarnEvery = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record State(string? SiteUrl, string? Remembered, DateTime? RememberedAt, long LoadedAt);

    private volatile State? _state;
    private int _refreshing;
    private int _persisting;
    private long _lastWarning = long.MinValue;

    public string Current
    {
        get
        {
            var state = _state;
            if (state is null)
                state = LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            else if (IsStale(state) && Interlocked.CompareExchange(ref _refreshing, 1, 0) == 0)
                _ = Task.Run(async () =>
                {
                    try { await LoadAsync(CancellationToken.None); }
                    finally { Volatile.Write(ref _refreshing, 0); }
                });
            return Resolve(state.SiteUrl);
        }
    }

    public async Task<string> GetAsync(CancellationToken ct = default)
    {
        var state = _state;
        if (state is null || IsStale(state)) state = await LoadAsync(ct);
        return Resolve(state.SiteUrl);
    }

    public string Resolve(string? siteUrl)
    {
        if (NormalizeBaseUrl(siteUrl) is { } site) return site;
        if (ConfiguredBaseUrl is { } configured) return configured;
        if (RequestOrigin(http.HttpContext) is { } request && IsAllowedHost(request.Host))
        {
            Remember(request.Origin, request.Host);
            return request.Origin;
        }
        if (_state?.Remembered is { } remembered) return remembered;
        Warn();
        return string.Empty;
    }

    public void SiteUrlChanged(string? siteUrl)
    {
        if (_state is { } state) _state = state with { SiteUrl = siteUrl, LoadedAt = Environment.TickCount64 };
    }

    /// <summary><c>Email:AppBaseUrl</c> when explicitly configured (non-empty, absolute http(s)); otherwise null.</summary>
    public string? ConfiguredBaseUrl
    {
        get
        {
            var raw = configuration["Email:AppBaseUrl"];
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var normalized = NormalizeBaseUrl(raw);
            if (normalized is null) logger.LogWarning("Email:AppBaseUrl {Value} is not an absolute http(s) URL; it is ignored", raw);
            return normalized;
        }
    }

    /// <summary>
    /// Optional allow-list <c>Hosting:PublicHosts</c> (host names, or <c>*.example.com</c> for any subdomain) for the request
    /// origin. Empty (the default): any valid host a trusted proxy forwards. Set it when the proxy passes on whatever Host
    /// header a visitor sends (nginx reachable directly rather than behind a load balancer that routes by host name).
    /// </summary>
    public bool IsAllowedHost(string host)
    {
        var allowed = configuration.GetSection("Hosting:PublicHosts").Get<string[]>();
        if (allowed is null || allowed.Length == 0) return true;
        return allowed.Select(a => a.Trim().ToLowerInvariant()).Where(a => a.Length > 0).Any(a =>
            a.StartsWith("*.", StringComparison.Ordinal)
                ? host.EndsWith(a[1..], StringComparison.Ordinal) && host.Length > a.Length - 1
                : host == a);
    }

    /// <summary>
    /// The origin of <paramref name="context"/>'s request (scheme + host after ForwardedHeaders), or null when the connection
    /// did not come from a trusted proxy or the host is not a plain host name (no user info, path or odd characters).
    /// </summary>
    public static (string Origin, string Host)? RequestOrigin(HttpContext? context)
    {
        if (context is null || context.Items[TrustedProxyItem] is not true) return null;
        var request = context.Request;
        var scheme = request.Scheme;
        if (scheme is not ("http" or "https")) return null;
        var host = request.Host;
        if (!host.HasValue || !IsValidHost(host.Host)) return null;
        var name = host.Host.ToLowerInvariant();
        var port = host.Port;
        if (port is < 1 or > 65535) return null;
        var isDefault = port is null || (scheme == "https" && port == 443) || (scheme == "http" && port == 80);
        return ($"{scheme}://{name}{(isDefault ? string.Empty : ":" + port)}", name);
    }

    /// <summary>A DNS host name (letters, digits, hyphens, dots; at most 253 characters) or an IPv4 address.</summary>
    public static bool IsValidHost(string? host) =>
        !string.IsNullOrEmpty(host) && host.Length <= 253 && HostNameRegex().IsMatch(host);

    /// <summary>Whether the connection's peer (before X-Forwarded-For is applied) is a trusted reverse proxy.</summary>
    public static bool IsTrustedProxy(IPAddress? remote, ForwardedHeadersOptions options)
    {
        if (remote is null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        return options.KnownProxies.Any(p => p.Equals(remote)) || options.KnownNetworks.Any(n => n.Contains(remote));
    }

    /// <summary>"https://Example.com/" → "https://Example.com"; null unless an absolute http(s) URL without user info.</summary>
    public static string? NormalizeBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim().TrimEnd('/');
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0 &&
               string.IsNullOrEmpty(uri.UserInfo) && uri.Query.Length == 0 && uri.Fragment.Length == 0
            ? trimmed
            : null;
    }

    private static bool IsStale(State state) => Environment.TickCount64 - state.LoadedAt > (long)CacheFor.TotalMilliseconds;

    private async Task<State> LoadAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var siteJson = await db.Set<SiteSettingsDocument>().AsNoTracking().Where(d => d.Key == SiteSettingsDocument.DefaultKey)
                .Select(d => d.Json).FirstOrDefaultAsync(ct);
            var row = await db.Set<SystemSetting>().AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKey, ct);
            var state = new State(SiteSettingsService.Parse(siteJson).Seo.SiteUrl, ParseRemembered(row?.ValueJson), row?.UpdatedAt,
                Environment.TickCount64);
            _state = state;
            return state;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database is not reachable (starting, migrating): keep what is known and try again on the next use.
            logger.LogDebug(ex, "Loading the public origin settings failed");
            return _state ?? new State(null, null, null, 0);
        }
    }

    private static string? ParseRemembered(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var value = JsonSerializer.Deserialize<string>(json, Json);
            return NormalizeBaseUrl(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Remembers a trusted request origin for background jobs: at most one write a day, never an IP address.</summary>
    private void Remember(string origin, string host)
    {
        if (IPAddress.TryParse(host, out _)) return;
        var state = _state;
        var now = clock.GetUtcNow().UtcDateTime;
        if (state is { Remembered: not null, RememberedAt: { } at } && now >= at && now - at < RememberEvery) return;
        if (Interlocked.CompareExchange(ref _persisting, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await PersistAsync(origin);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Remembering the public origin failed");
            }
            finally
            {
                Volatile.Write(ref _persisting, 0);
            }
        });
    }

    private async Task PersistAsync(string origin)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;
        var row = await db.Set<SystemSetting>().FirstOrDefaultAsync(s => s.Key == SettingKey);
        if (row is not null && ParseRemembered(row.ValueJson) is { } existing && now >= row.UpdatedAt && now - row.UpdatedAt < RememberEvery)
        {
            // Another instance (or an earlier run) wrote it today.
            UpdateRemembered(existing, row.UpdatedAt);
            return;
        }
        var json = JsonSerializer.Serialize(origin, Json);
        if (row is null)
        {
            db.Set<SystemSetting>().Add(new SystemSetting
            {
                Key = SettingKey, ValueJson = json, UpdatedAt = now,
                Description = "Last public origin seen on a request through a trusted proxy (links in background jobs and emails when no site URL is configured).",
            });
        }
        else
        {
            row.ValueJson = json;
            row.UpdatedAt = now;
        }
        await db.SaveChangesAsync();
        UpdateRemembered(origin, now);
        logger.LogInformation("Remembered the public origin {Origin} for links built outside a request", origin);
    }

    private void UpdateRemembered(string origin, DateTime at) =>
        _state = (_state ?? new State(null, null, null, 0)) with { Remembered = origin, RememberedAt = at };

    private void Warn()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastWarning);
        if (last != long.MinValue && now - last < (long)WarnEvery.TotalMilliseconds) return;
        if (Interlocked.CompareExchange(ref _lastWarning, now, last) != last) return;
        logger.LogWarning(
            "No public URL is known: set the site URL (Website → Settings → SEO) or Email:AppBaseUrl (Email__AppBaseUrl). Links are root-relative until a request arrives through a trusted proxy (Hosting:TrustedNetworks).");
    }

    [GeneratedRegex(@"^(?=.{1,253}$)[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$", RegexOptions.CultureInvariant)]
    private static partial Regex HostNameRegex();
}

public static class PublicOriginMiddleware
{
    /// <summary>
    /// Marks requests whose connection comes from a trusted reverse proxy. Must run before <c>UseForwardedHeaders</c>, which
    /// replaces the remote address with the client's.
    /// </summary>
    public static IApplicationBuilder UsePublicOriginTrust(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        return app.Use(async (context, next) =>
        {
            if (PublicOrigin.IsTrustedProxy(context.Connection.RemoteIpAddress, options)) context.Items[PublicOrigin.TrustedProxyItem] = true;
            await next();
        });
    }
}
