using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Modules.Seo.Crawling;
using OptimizeAll.Api.Modules.Seo.Http;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seo.Backlinks;

public sealed record BacklinkVerdict(BacklinkStatus Status, int? StatusCode, string? Rel, string? Anchor, string Message);

/// <summary>
/// Verifies backlinks by fetching the linking page through the SSRF-safe fetcher and looking for an &lt;a href&gt; to the
/// target (scheme, "www." and a trailing slash are ignored). Live = present and followed; Nofollow = present with
/// rel nofollow/ugc/sponsored; Lost = page gone or link removed; Error = page unreachable.
/// </summary>
public sealed class BacklinkChecker(AppDbContext db, SafeHttpFetcher fetcher, TimeProvider clock)
{
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(7);

    public async Task<BacklinkVerdict> VerifyAsync(string sourceUrl, string targetUrl, CancellationToken ct)
    {
        var fetch = await fetcher.GetAsync(sourceUrl, ct);
        if (fetch.ErrorKind == FetchErrorKind.Blocked)
            return new BacklinkVerdict(BacklinkStatus.Error, null, null, null, "The linking page resolves to a private address and was not fetched.");
        if (fetch.ErrorKind != FetchErrorKind.None)
            return new BacklinkVerdict(BacklinkStatus.Error, fetch.StatusCode, null, null, fetch.Error ?? fetch.ErrorKind.ToString());
        if (fetch.StatusCode >= 400)
            return new BacklinkVerdict(BacklinkStatus.Lost, fetch.StatusCode, null, null, $"The linking page answers {fetch.StatusCode}.");
        if (!fetch.IsHtml)
            return new BacklinkVerdict(BacklinkStatus.Lost, fetch.StatusCode, null, null, "The linking page is not HTML.");

        var data = HtmlPageExtractor.Extract(fetch.BodyText, new Uri(fetch.FinalUrl));
        var target = Comparable(targetUrl);
        var link = data.Links.FirstOrDefault(l => Comparable(l.Href) == target);
        if (link is null)
            return new BacklinkVerdict(BacklinkStatus.Lost, fetch.StatusCode, null, null, "The link to the target was not found on the page.");
        var rel = link.Rel?.Trim().ToLowerInvariant();
        var noFollow = rel is not null && rel.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(r => r is "nofollow" or "ugc" or "sponsored");
        var pageNofollow = data.MetaRobots?.Contains("nofollow", StringComparison.OrdinalIgnoreCase) ?? false;
        return noFollow || pageNofollow
            ? new BacklinkVerdict(BacklinkStatus.Nofollow, fetch.StatusCode, rel ?? "nofollow", link.Text, pageNofollow ? "The page is marked nofollow." : $"Link is rel=\"{rel}\".")
            : new BacklinkVerdict(BacklinkStatus.Live, fetch.StatusCode, rel, link.Text, "Live and followed.");
    }

    /// <summary>Checks backlinks that were never checked or not in the last 7 days (optionally one site); returns the count.</summary>
    public async Task<int> CheckDueAsync(Guid? siteId, int max, CancellationToken ct)
    {
        var due = clock.GetUtcNow().UtcDateTime - RecheckAfter;
        var q = db.Set<SeoBacklink>().Where(b => b.LastCheckedAt == null || b.LastCheckedAt < due);
        if (siteId is { } id) q = q.Where(b => b.SiteId == id);
        var batch = await q.OrderBy(b => b.LastCheckedAt).ThenBy(b => b.Id).Take(max).ToListAsync(ct);
        foreach (var backlink in batch)
        {
            var verdict = await VerifyAsync(backlink.SourceUrl, backlink.TargetUrl, ct);
            backlink.Status = verdict.Status;
            backlink.LastStatusCode = verdict.StatusCode;
            backlink.LastCheckedAt = clock.GetUtcNow().UtcDateTime;
            backlink.CheckMessage = verdict.Message.Length > 500 ? verdict.Message[..500] : verdict.Message;
            if (verdict.Status is BacklinkStatus.Live or BacklinkStatus.Nofollow)
            {
                backlink.Rel = verdict.Rel is { Length: > 100 } r ? r[..100] : verdict.Rel;
                if (!string.IsNullOrWhiteSpace(verdict.Anchor)) backlink.AnchorText = verdict.Anchor.Length > 500 ? verdict.Anchor[..500] : verdict.Anchor;
            }
            await db.SaveChangesAsync(ct);
        }
        return batch.Count;
    }

    /// <summary>host without "www." + path without trailing slash + query (scheme and fragment ignored).</summary>
    public static string Comparable(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return url.Trim().ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        var path = uri.AbsolutePath.TrimEnd('/');
        return host + path + uri.Query;
    }
}

/// <summary>Daily link checker (up to 200 due backlinks per run).</summary>
public sealed class BacklinkCheckJob(BacklinkChecker checker) : IJob
{
    public string Name => "seo.backlink-check";

    public async Task<string> ExecuteAsync(CancellationToken ct) => $"{await checker.CheckDueAsync(null, 200, ct)} backlink(s) checked";
}
