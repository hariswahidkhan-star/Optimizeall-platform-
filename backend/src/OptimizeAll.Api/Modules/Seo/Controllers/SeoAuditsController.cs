using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seo.Controllers;

public sealed record AuditSummaryDto(
    Guid Id, Guid SiteId, SeoAuditStatus Status, DateTime QueuedAt, DateTime? StartedAt, DateTime? FinishedAt, int PagesCrawled,
    int? HealthScore, int ErrorCount, int WarningCount, int NoticeCount, bool RobotsTxtFound, bool SitemapFound, string? FailureMessage,
    int MaxPages, int MaxDepth);

public sealed record IssueHitDto(string Url, string? Detail);

public sealed record AuditIssueDto(
    string RuleKey, string Title, string Category, SeoSeverity Severity, string WhyItMatters, string HowToFix, int AffectedCount,
    IReadOnlyList<IssueHitDto> Hits, SeoIssueStatus Status, string? StatusNote, DateTime? StatusChangedAt);

public sealed class IssueStatusRequest
{
    [Required, DefinedEnum] public SeoIssueStatus? Status { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
}

public sealed record AuditDetailDto(
    AuditSummaryDto Audit, string SiteName, string SiteBaseUrl, Guid? PreviousAuditId, IReadOnlyList<AuditIssueDto> Issues);

public sealed record AuditPageDto(
    Guid Id, string Url, int? StatusCode, int Depth, int ResponseTimeMs, long ContentLength, string? Title, string? MetaDescription,
    int H1Count, int WordCount, string? Canonical, bool IsNoindex, bool InSitemap, int InboundLinks, string? RedirectChain, string? FetchError);

public sealed record DiffIssueDto(string RuleKey, string Title, SeoSeverity Severity, IReadOnlyList<string> Urls);

public sealed record AuditDiffDto(
    Guid AuditId, Guid? AgainstAuditId, int? HealthScoreChange, int NewCount, int FixedCount,
    IReadOnlyList<DiffIssueDto> NewIssues, IReadOnlyList<DiffIssueDto> FixedIssues);

public sealed class AuditPagesQuery : PageQuery
{
    /// <summary>ok (2xx), redirect, client-error (4xx), server-error (5xx), failed.</summary>
    public string? Status { get; set; }
}

/// <summary>Site audits: queue, history, results grouped by severity, crawled pages, diff between audits, CSV export.</summary>
[ApiController]
[HasPermission(Permissions.SeoManage)]
[Route("api/v1/agency/seo")]
public sealed class SeoAuditsController(
    AppDbContext db, SeoAccess access, IAuditLogger audit, ICurrentUser currentUser, TimeProvider clock) : ControllerBase
{
    [HttpPost("sites/{siteId:guid}/audits")]
    public async Task<ActionResult<AuditSummaryDto>> Queue(Guid siteId, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        if (site.IsArchived) throw DomainException.Conflict("seo.site_archived", "Restore the site before running an audit.");
        if (await db.Set<SeoAudit>().AnyAsync(a => a.SiteId == siteId && (a.Status == SeoAuditStatus.Queued || a.Status == SeoAuditStatus.Running), ct))
            throw DomainException.Conflict("seo.audit_in_progress", "An audit of this site is already queued or running.");
        var row = new SeoAudit
        {
            SiteId = site.Id, ClientAccountId = site.ClientAccountId, Status = SeoAuditStatus.Queued, RequestedByUserId = currentUser.Id,
            QueuedAt = clock.GetUtcNow().UtcDateTime, MaxPages = site.MaxPages, MaxDepth = site.MaxDepth,
        };
        db.Set<SeoAudit>().Add(row);
        audit.Record("seo.audit_queued", nameof(SeoAudit), row.Id, after: new { row.SiteId, site.Domain, row.MaxPages, row.MaxDepth });
        await db.SaveChangesAsync(ct);
        return Accepted(ToSummary(row));
    }

    [HttpGet("sites/{siteId:guid}/audits")]
    public async Task<PagedResult<AuditSummaryDto>> History(Guid siteId, [FromQuery] PageQuery query, CancellationToken ct)
    {
        await access.SiteAsync(siteId, ct);
        var page = await db.Set<SeoAudit>().AsNoTracking().Where(a => a.SiteId == siteId).OrderByDescending(a => a.QueuedAt).ToPagedAsync(query, ct);
        return new PagedResult<AuditSummaryDto>(page.Items.Select(ToSummary).ToList(), page.Total, page.Page, page.PageSize);
    }

    [HttpGet("audits/{id:guid}")]
    public async Task<AuditDetailDto> Get(Guid id, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoAudit>(id, a => a.ClientAccountId, "Audit", ct, tracked: false);
        var site = await db.Set<SeoSite>().AsNoTracking().FirstAsync(s => s.Id == row.SiteId, ct);
        var issues = await db.Set<SeoAuditIssue>().AsNoTracking().Where(i => i.AuditId == id).ToListAsync(ct);
        var rules = await RulesAsync(ct);
        var previous = await PreviousCompletedAsync(row, ct);
        return new AuditDetailDto(ToSummary(row), site.Name, site.BaseUrl, previous?.Id,
            issues.Select(i => ToIssue(i, rules)).OrderBy(i => i.Severity).ThenByDescending(i => i.AffectedCount).ThenBy(i => i.Title).ToList());
    }

    [HttpPost("audits/{id:guid}/cancel")]
    public async Task<AuditSummaryDto> Cancel(Guid id, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoAudit>(id, a => a.ClientAccountId, "Audit", ct, tracked: false);
        var cancelled = await db.Set<SeoAudit>().Where(a => a.Id == id && a.Status == SeoAuditStatus.Queued)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, SeoAuditStatus.Cancelled).SetProperty(a => a.FinishedAt, clock.GetUtcNow().UtcDateTime), ct);
        if (cancelled == 0) throw DomainException.Conflict("seo.audit_not_queued", "Only queued audits can be cancelled.");
        audit.Record("seo.audit_cancelled", nameof(SeoAudit), id);
        await db.SaveChangesAsync(ct);
        return ToSummary(await db.Set<SeoAudit>().AsNoTracking().FirstAsync(a => a.Id == row.Id, ct));
    }

    /// <summary>Triage one issue of an audit: mark it fixed, ignore it (carried over to later audits) or reopen it.</summary>
    [HttpPost("audits/{id:guid}/issues/{ruleKey}/status")]
    public async Task<AuditIssueDto> SetIssueStatus(Guid id, string ruleKey, IssueStatusRequest request, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoAudit>(id, a => a.ClientAccountId, "Audit", ct, tracked: false);
        if (row.Status != SeoAuditStatus.Completed) throw DomainException.Conflict("seo.audit_not_completed", "Issues can be triaged once the audit has completed.");
        var issue = await db.Set<SeoAuditIssue>().FirstOrDefaultAsync(i => i.AuditId == id && i.RuleKey == ruleKey, ct) ?? throw DomainException.NotFound("Issue");
        var status = request.Status!.Value;
        if (status == SeoIssueStatus.Ignored && string.IsNullOrWhiteSpace(request.Note))
            throw new DomainException("seo.issue_note_required", "Say why the issue is ignored (kept for the next audits).",
                errors: new Dictionary<string, string[]> { ["note"] = new[] { "Add a note explaining why this issue is ignored." } });
        var before = new { issue.Status, issue.StatusNote };
        issue.Status = status;
        issue.StatusNote = status == SeoIssueStatus.Open ? null : request.Note?.Trim();
        issue.StatusChangedAt = clock.GetUtcNow().UtcDateTime;
        issue.StatusChangedByUserId = currentUser.Id;
        audit.Record("seo.issue_status_changed", nameof(SeoAudit), id, before, new { issue.RuleKey, issue.Status, issue.StatusNote });
        await db.SaveChangesAsync(ct);
        return ToIssue(issue, await RulesAsync(ct));
    }

    /// <summary>Deletes a finished audit and its results (queued or running audits must be cancelled or finish first).</summary>
    [HttpDelete("audits/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoAudit>(id, a => a.ClientAccountId, "Audit", ct, tracked: false);
        if (row.Status is SeoAuditStatus.Queued or SeoAuditStatus.Running)
            throw DomainException.Conflict("seo.audit_in_progress", "Cancel the audit or wait for it to finish before deleting it.");
        await db.Set<SeoAuditIssue>().Where(i => i.AuditId == id).ExecuteDeleteAsync(ct);
        await db.Set<SeoAuditPage>().Where(p => p.AuditId == id).ExecuteDeleteAsync(ct);
        await db.Set<SeoAudit>().Where(a => a.Id == id).ExecuteDeleteAsync(ct);
        audit.Record("seo.audit_deleted", nameof(SeoAudit), id, before: new { row.SiteId, row.Status, row.HealthScore, row.QueuedAt });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("audits/{id:guid}/pages")]
    public async Task<PagedResult<AuditPageDto>> Pages(Guid id, [FromQuery] AuditPagesQuery query, CancellationToken ct)
    {
        await access.OwnedAsync<SeoAudit>(id, a => a.ClientAccountId, "Audit", ct, tracked: false);
        var q = db.Set<SeoAuditPage>().AsNoTracking().Where(p => p.AuditId == id);
        if (!string.IsNullOrWhiteSpace(query.Search)) q = q.Where(p => EF.Functions.Like(p.Url, PagingExtensions.LikePattern(query.Search)));
        q = query.Status switch
        {
            "ok" => q.Where(p => p.StatusCode >= 200 && p.StatusCode < 300 && p.RedirectChain == null),
            "redirect" => q.Where(p => p.RedirectChain != null),
            "client-error" => q.Where(p => p.StatusCode >= 400 && p.StatusCode < 500),
            "server-error" => q.Where(p => p.StatusCode >= 500),
            "failed" => q.Where(p => p.FetchError != null),
            _ => q,
        };
        var page = await q.OrderBy(p => p.Depth).ThenBy(p => p.Url).ToPagedAsync(query, ct);
        return new PagedResult<AuditPageDto>(page.Items.Select(p => new AuditPageDto(p.Id, p.Url, p.StatusCode, p.Depth, p.ResponseTimeMs,
            p.ContentLength, p.Title, p.MetaDescription, p.H1Count, p.WordCount, p.Canonical, p.IsNoindex, p.InSitemap, p.InboundLinks,
            p.RedirectChain, p.FetchError)).ToList(), page.Total, page.Page, page.PageSize);
    }

    /// <summary>New and fixed (rule, URL) pairs compared with <paramref name="against"/> (default: the previous completed audit).</summary>
    [HttpGet("audits/{id:guid}/diff")]
    public async Task<AuditDiffDto> Diff(Guid id, [FromQuery] Guid? against, CancellationToken ct)
    {
        var current = await access.OwnedAsync<SeoAudit>(id, a => a.ClientAccountId, "Audit", ct, tracked: false);
        SeoAudit? baseline;
        if (against is { } otherId)
        {
            baseline = await access.OwnedAsync<SeoAudit>(otherId, a => a.ClientAccountId, "Audit", ct, tracked: false);
            if (baseline.SiteId != current.SiteId) throw new DomainException("seo.diff_other_site", "Both audits must belong to the same site.");
        }
        else baseline = await PreviousCompletedAsync(current, ct);

        var currentIssues = await db.Set<SeoAuditIssue>().AsNoTracking().Where(i => i.AuditId == current.Id).ToListAsync(ct);
        var baselineIssues = baseline is null ? new List<SeoAuditIssue>()
            : await db.Set<SeoAuditIssue>().AsNoTracking().Where(i => i.AuditId == baseline.Id).ToListAsync(ct);
        var rules = await RulesAsync(ct);
        var (added, fixedIssues) = AuditDiff.Compare(baselineIssues, currentIssues);
        DiffIssueDto Map((string Rule, SeoSeverity Severity, List<string> Urls) x) =>
            new(x.Rule, rules.TryGetValue(x.Rule, out var r) ? r.Title : x.Rule, x.Severity, x.Urls);
        return new AuditDiffDto(current.Id, baseline?.Id,
            current.HealthScore is { } a && baseline?.HealthScore is { } b ? a - b : null,
            added.Sum(x => x.Urls.Count), fixedIssues.Sum(x => x.Urls.Count),
            added.Select(Map).ToList(), fixedIssues.Select(Map).ToList());
    }

    [HttpGet("audits/{id:guid}/export.csv")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoAudit>(id, a => a.ClientAccountId, "Audit", ct, tracked: false);
        var site = await db.Set<SeoSite>().AsNoTracking().FirstAsync(s => s.Id == row.SiteId, ct);
        var issues = await db.Set<SeoAuditIssue>().AsNoTracking().Where(i => i.AuditId == id).ToListAsync(ct);
        var rules = await RulesAsync(ct);
        var lines = issues.Select(i => ToIssue(i, rules)).OrderBy(i => i.Severity).ThenBy(i => i.Title)
            .SelectMany(i => i.Hits.Select(h => new object?[] { i.Severity.ToString(), i.RuleKey, i.Title, i.Category, h.Url, h.Detail, i.HowToFix }));
        return Csv.File($"seo-audit-{site.Domain.Replace(':', '-')}-{(row.FinishedAt ?? row.QueuedAt):yyyyMMdd}.csv",
            new[] { "severity", "rule", "issue", "category", "url", "detail", "how_to_fix" }, lines);
    }

    private async Task<SeoAudit?> PreviousCompletedAsync(SeoAudit row, CancellationToken ct) =>
        await db.Set<SeoAudit>().AsNoTracking()
            .Where(a => a.SiteId == row.SiteId && a.Status == SeoAuditStatus.Completed && a.QueuedAt < row.QueuedAt && a.Id != row.Id)
            .OrderByDescending(a => a.QueuedAt).FirstOrDefaultAsync(ct);

    private async Task<Dictionary<string, SeoAuditRule>> RulesAsync(CancellationToken ct)
    {
        var stored = await db.Set<SeoAuditRule>().AsNoTracking().ToDictionaryAsync(r => r.Key, ct);
        foreach (var r in SeoAuditRules.All.Where(r => !stored.ContainsKey(r.Key)))
            stored[r.Key] = new SeoAuditRule { Key = r.Key, Title = r.Title, Category = r.Category, Severity = r.Severity, WhyItMatters = r.WhyItMatters, HowToFix = r.HowToFix };
        return stored;
    }

    private static AuditIssueDto ToIssue(SeoAuditIssue i, Dictionary<string, SeoAuditRule> rules)
    {
        var rule = rules.GetValueOrDefault(i.RuleKey);
        var details = (i.Details ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\t', 2)).Where(p => p.Length == 2).ToList();
        var hits = details.Count > 0
            ? details.Select(p => new IssueHitDto(p[0], p[1].Length == 0 ? null : p[1])).ToList()
            : i.AffectedUrls.Select(u => new IssueHitDto(u, null)).ToList();
        return new AuditIssueDto(i.RuleKey, rule?.Title ?? i.RuleKey, rule?.Category ?? "Other", i.Severity, rule?.WhyItMatters ?? string.Empty,
            rule?.HowToFix ?? string.Empty, i.AffectedCount, hits, i.Status, i.StatusNote, i.StatusChangedAt);
    }

    internal static AuditSummaryDto ToSummary(SeoAudit a) => new(a.Id, a.SiteId, a.Status, a.QueuedAt, a.StartedAt, a.FinishedAt, a.PagesCrawled,
        a.HealthScore, a.ErrorCount, a.WarningCount, a.NoticeCount, a.RobotsTxtFound, a.SitemapFound, a.FailureMessage, a.MaxPages, a.MaxDepth);
}

/// <summary>Audit comparison at the (rule, URL) level.</summary>
public static class AuditDiff
{
    public static (List<(string Rule, SeoSeverity Severity, List<string> Urls)> New, List<(string Rule, SeoSeverity Severity, List<string> Urls)> Fixed)
        Compare(IReadOnlyCollection<SeoAuditIssue> baseline, IReadOnlyCollection<SeoAuditIssue> current)
    {
        static Dictionary<string, (SeoSeverity Severity, HashSet<string> Urls)> Index(IEnumerable<SeoAuditIssue> issues) =>
            issues.ToDictionary(i => i.RuleKey, i => (i.Severity, i.AffectedUrls.ToHashSet(StringComparer.Ordinal)));

        var before = Index(baseline);
        var after = Index(current);
        var added = after.Select(a => (Rule: a.Key, a.Value.Severity,
                Urls: a.Value.Urls.Where(u => !before.TryGetValue(a.Key, out var b) || !b.Urls.Contains(u)).OrderBy(u => u, StringComparer.Ordinal).ToList()))
            .Where(x => x.Urls.Count > 0).OrderBy(x => x.Severity).ThenBy(x => x.Rule, StringComparer.Ordinal).ToList();
        var fixedIssues = before.Select(b => (Rule: b.Key, b.Value.Severity,
                Urls: b.Value.Urls.Where(u => !after.TryGetValue(b.Key, out var a) || !a.Urls.Contains(u)).OrderBy(u => u, StringComparer.Ordinal).ToList()))
            .Where(x => x.Urls.Count > 0).OrderBy(x => x.Severity).ThenBy(x => x.Rule, StringComparer.Ordinal).ToList();
        return (added, fixedIssues);
    }
}
