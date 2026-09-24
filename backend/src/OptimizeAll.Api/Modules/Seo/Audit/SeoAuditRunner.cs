using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Seo.Crawling;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seo.Audit;

/// <summary>
/// Executes queued audits. Claiming is a conditional update (Queued → Running), so two workers never run the same audit;
/// results are written after deleting any partial rows of the same audit, so a crashed run can be re-queued and re-run
/// safely. Stale Running audits (worker died) are re-queued after <see cref="StaleAfter"/>. A claim is identified by its
/// StartedAt: a slow worker whose audit was re-queued and claimed again has lost its claim and writes nothing.
/// </summary>
public sealed class SeoAuditRunner(
    AppDbContext db, SiteCrawler crawler, IDatabaseDialect dialect, TimeProvider clock, ILogger<SeoAuditRunner> logger)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(20);
    public const int MaxStoredPages = 2000;
    public const int MaxUrlsPerIssue = 200;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Runs up to <paramref name="max"/> queued audits; returns how many completed or failed.</summary>
    public async Task<(int Completed, int Failed, int Requeued)> RunQueuedAsync(int max, CancellationToken ct)
    {
        var staleBefore = Now - StaleAfter;
        var requeued = await db.Set<SeoAudit>()
            .Where(a => a.Status == SeoAuditStatus.Running && a.StartedAt < staleBefore)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, SeoAuditStatus.Queued).SetProperty(a => a.StartedAt, (DateTime?)null), ct);

        int completed = 0, failed = 0;
        for (var i = 0; i < max; i++)
        {
            var next = await db.Set<SeoAudit>().AsNoTracking().Where(a => a.Status == SeoAuditStatus.Queued)
                .OrderBy(a => a.QueuedAt).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            if (next is null) break;
            var ok = await RunAsync(next.Value, ct);
            if (ok is true) completed++;
            else if (ok is false) failed++;
        }
        return (completed, failed, requeued);
    }

    /// <summary>Claims and runs one audit. Null when another worker claimed it first (or took the claim over mid-run).</summary>
    public async Task<bool?> RunAsync(Guid auditId, CancellationToken ct)
    {
        // Whole milliseconds, so the claim token compares equal after a round trip through any provider's datetime column.
        var now = Now;
        var startedAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        var claimed = await db.Set<SeoAudit>().Where(a => a.Id == auditId && a.Status == SeoAuditStatus.Queued)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, SeoAuditStatus.Running).SetProperty(a => a.StartedAt, startedAt), ct);
        if (claimed == 0) return null;

        var audit = await db.Set<SeoAudit>().AsNoTracking().FirstAsync(a => a.Id == auditId, ct);
        var site = await db.Set<SeoSite>().AsNoTracking().FirstAsync(s => s.Id == audit.SiteId, ct);
        try
        {
            var crawl = await crawler.CrawlAsync(new CrawlRequest(site.BaseUrl, audit.MaxPages, audit.MaxDepth, site.SitemapUrl), ct);
            var rules = await db.Set<SeoAuditRule>().AsNoTracking().ToDictionaryAsync(r => r.Key, ct);
            var outcome = AuditChecks.Run(crawl, rules);
            if (await SaveAsync(audit, startedAt, crawl, outcome, ct)) return true;
            logger.LogWarning("SEO audit {AuditId}: the claim was taken over by another worker; results of this run discarded", auditId);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "SEO audit {AuditId} failed", auditId);
            db.ChangeTracker.Clear();
            var message = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message;
            await db.Set<SeoAudit>().Where(a => a.Id == auditId && a.Status == SeoAuditStatus.Running && a.StartedAt == startedAt)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, SeoAuditStatus.Failed)
                    .SetProperty(a => a.FinishedAt, Now).SetProperty(a => a.FailureMessage, "The crawl could not be completed: " + message), CancellationToken.None);
            return false;
        }
    }

    /// <summary>Writes the results if this run still holds the claim (Running with its StartedAt); false when it lost it.</summary>
    private async Task<bool> SaveAsync(SeoAudit audit, DateTime startedAt, CrawlResult crawl, AuditOutcome outcome, CancellationToken ct)
    {
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        // Completing the audit first both verifies the claim and locks the row (MySQL) for the rest of the transaction,
        // so a concurrent re-queue cannot interleave with the result rows written below.
        var finished = Now;
        var completed = await db.Set<SeoAudit>()
            .Where(a => a.Id == audit.Id && a.Status == SeoAuditStatus.Running && a.StartedAt == startedAt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, SeoAuditStatus.Completed)
                .SetProperty(a => a.FinishedAt, finished)
                .SetProperty(a => a.PagesCrawled, crawl.Pages.Count)
                .SetProperty(a => a.HealthScore, outcome.HealthScore)
                .SetProperty(a => a.ErrorCount, outcome.Errors)
                .SetProperty(a => a.WarningCount, outcome.Warnings)
                .SetProperty(a => a.NoticeCount, outcome.Notices)
                .SetProperty(a => a.RobotsTxtFound, crawl.RobotsFound)
                .SetProperty(a => a.SitemapFound, crawl.SitemapFound)
                .SetProperty(a => a.FailureMessage, crawl.HitTimeLimit ? "Stopped at the time limit; results cover the pages crawled so far." : null), ct);
        if (completed == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }
        await db.Set<SeoAuditIssue>().Where(i => i.AuditId == audit.Id).ExecuteDeleteAsync(ct);
        await db.Set<SeoAuditPage>().Where(p => p.AuditId == audit.Id).ExecuteDeleteAsync(ct);

        foreach (var page in crawl.Pages.Take(MaxStoredPages))
        {
            db.Set<SeoAuditPage>().Add(new SeoAuditPage
            {
                AuditId = audit.Id,
                Url = Truncate(page.Url, 2000)!,
                StatusCode = page.StatusCode,
                Depth = page.Depth,
                ResponseTimeMs = page.Fetch.ElapsedMs,
                ContentLength = page.Fetch.ContentLength,
                ContentType = Truncate(page.Fetch.ContentType, 150),
                Title = Truncate(page.Data?.Title, 1000),
                MetaDescription = Truncate(page.Data?.MetaDescription, 2000),
                H1Count = page.Data?.H1.Count ?? 0,
                WordCount = page.Data?.WordCount ?? 0,
                Canonical = Truncate(page.Data?.Canonical, 2000),
                IsNoindex = page.IsNoindex,
                InSitemap = page.InSitemap,
                InboundLinks = page.InboundLinks,
                RedirectChain = page.Fetch.Redirects.Count == 0 ? null
                    : Truncate(string.Join("\n", page.Fetch.Redirects.Select(r => $"{r.StatusCode} {r.From} → {r.To}")), 4000),
                FetchError = Truncate(page.Fetch.Error, 500),
            });
        }

        // Rules the team marked "ignored" on the previous completed audit stay ignored (with their note) until reopened.
        var previousId = await db.Set<SeoAudit>().AsNoTracking()
            .Where(a => a.SiteId == audit.SiteId && a.Status == SeoAuditStatus.Completed && a.Id != audit.Id && a.QueuedAt <= audit.QueuedAt)
            .OrderByDescending(a => a.QueuedAt).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
        var ignored = previousId is null
            ? new Dictionary<string, SeoAuditIssue>()
            : await db.Set<SeoAuditIssue>().AsNoTracking().Where(i => i.AuditId == previousId && i.Status == SeoIssueStatus.Ignored)
                .ToDictionaryAsync(i => i.RuleKey, ct);
        foreach (var issue in outcome.Issues)
        {
            var hits = issue.Hits.Take(MaxUrlsPerIssue).ToList();
            var carried = ignored.GetValueOrDefault(issue.RuleKey);
            db.Set<SeoAuditIssue>().Add(new SeoAuditIssue
            {
                AuditId = audit.Id,
                RuleKey = issue.RuleKey,
                Severity = issue.Severity,
                AffectedCount = issue.Hits.Count,
                AffectedUrls = hits.Select(h => h.Url).Distinct().ToList(),
                Details = hits.Any(h => h.Detail is not null)
                    ? string.Join("\n", hits.Select(h => $"{h.Url}\t{(h.Detail ?? string.Empty).Replace('\n', ' ').Replace('\t', ' ')}"))
                    : null,
                Status = carried is null ? SeoIssueStatus.Open : SeoIssueStatus.Ignored,
                StatusNote = carried?.StatusNote,
                StatusChangedAt = carried?.StatusChangedAt,
                StatusChangedByUserId = carried?.StatusChangedByUserId,
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return true;
    }

    private static string? Truncate(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>Background worker for queued site audits (every minute; up to 3 audits per run).</summary>
public sealed class SeoAuditJob(SeoAuditRunner runner) : IJob
{
    public string Name => "seo.audits";

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var (completed, failed, requeued) = await runner.RunQueuedAsync(3, ct);
        return $"completed {completed}, failed {failed}, re-queued {requeued}";
    }
}
