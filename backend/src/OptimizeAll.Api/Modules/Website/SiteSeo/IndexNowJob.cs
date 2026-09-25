using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>
/// IndexNow (Bing, Yandex, Seznam, Naver…): when enabled in Website → SEO (off by default), every 10 minutes submits the
/// sitemap URLs whose content changed since the last successful submission, plus the URLs that left the sitemaps since
/// then (unpublished, deleted or moved content, which now answers 404/410/301), so new, updated and removed pages are
/// recrawled quickly. The key file is served at <c>/{key}.txt</c>. Skipped unless the site URL is a public https origin.
/// Idempotent and safe on several instances (named lock; the watermark only advances after a successful submission).
/// </summary>
public sealed class IndexNowJob(
    AppDbContext db, IDatabaseDialect dialect, SeoSettingsService settings, SeoPageResolver resolver, IHttpClientFactory http,
    IConfiguration configuration, ILogger<IndexNowJob> logger, TimeProvider clock) : IJob
{
    public const string HttpClientName = "indexnow";
    public const string StateKey = "seo-indexnow-state";
    public const int MaxUrlsPerRequest = 10_000;

    public string Name => nameof(IndexNowJob);

    /// <summary>At most this many known paths are remembered for removal detection (larger sites only get changes).</summary>
    public const int MaxRememberedPaths = 50_000;

    private sealed record State(DateTime? LastSubmittedAt, IReadOnlyList<string>? Paths = null);

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        if (!s.IndexNow.Enabled || s.IndexNow.Key is null) return "IndexNow is off.";
        await resolver.EnsureLoadedAsync(ct);
        if (!Uri.TryCreate(resolver.BaseUrl, UriKind.Absolute, out var site) || site.Scheme != Uri.UriSchemeHttps || site.IsLoopback)
            return "IndexNow skipped: the site URL is not a public https origin.";

        await using var named = await dialect.AcquireNamedLockAsync(db, "website.indexnow", TimeSpan.FromSeconds(5), ct);
        var doc = await db.Set<SiteSettingsDocument>().FirstOrDefaultAsync(d => d.Key == StateKey, ct);
        var state = doc is null ? new State(null) : JsonSerializer.Deserialize<State>(doc.Json) ?? new State(null);
        var started = clock.GetUtcNow().UtcDateTime;
        var sitemap = await resolver.SitemapUrlsAsync(ct);
        var current = sitemap.Select(u => u.Path).ToHashSet(StringComparer.Ordinal);
        var removed = state.LastSubmittedAt is null ? new List<string>() : (state.Paths ?? Array.Empty<string>()).Where(p => !current.Contains(p)).ToList();
        var urls = sitemap
            .Where(u => state.LastSubmittedAt is null || (u.LastModified ?? DateTime.MinValue) > state.LastSubmittedAt)
            .Select(u => u.Path).Concat(removed)
            .Select(resolver.Absolute).Take(MaxUrlsPerRequest).ToList();
        if (urls.Count > 0)
        {
            var endpoint = configuration["Website:Seo:IndexNowEndpoint"] ?? "https://api.indexnow.org/indexnow";
            var body = new { host = site.Host, key = s.IndexNow.Key, keyLocation = resolver.Absolute($"/{s.IndexNow.Key}.txt"), urlList = urls };
            using var response = await http.CreateClient(HttpClientName).PostAsJsonAsync(endpoint, body, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("IndexNow submission of {Count} URLs failed with HTTP {Status}", urls.Count, (int)response.StatusCode);
                return $"IndexNow failed: HTTP {(int)response.StatusCode}.";
            }
        }

        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        if (doc is null)
        {
            doc = new SiteSettingsDocument { Key = StateKey };
            db.Add(doc);
        }
        doc.Json = JsonSerializer.Serialize(new State(started, current.Count <= MaxRememberedPaths ? current.Order(StringComparer.Ordinal).ToList() : null));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return $"IndexNow: submitted {urls.Count} URLs.";
    }
}
