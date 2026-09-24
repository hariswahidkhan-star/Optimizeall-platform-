using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Seo.Backlinks;
using OptimizeAll.Api.Modules.Seo.Http;
using OptimizeAll.Api.Modules.Seo.OnPage;
using OptimizeAll.Api.Modules.Seo.Ranking;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seo.Controllers;

public sealed class AnalyzeRequest
{
    /// <summary>Page to fetch (SSRF-safe). Provide exactly one of url, html or text.</summary>
    [MaxLength(2000)]
    public string? Url { get; set; }

    [MaxLength(2_000_000)]
    public string? Html { get; set; }

    [MaxLength(200_000)]
    public string? Text { get; set; }

    [Required, MinLength(1), MaxLength(200)]
    public string Keyword { get; set; } = string.Empty;
}

public sealed class BacklinkRequest
{
    [Required, MaxLength(2000)]
    public string SourceUrl { get; set; } = string.Empty;

    [Required, MaxLength(2000)]
    public string TargetUrl { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? AnchorText { get; set; }

    [MaxLength(100)]
    public string? Rel { get; set; }

    public DateTime? FirstSeenAt { get; set; }
}

public sealed record BacklinkDto(
    Guid Id, Guid SiteId, string SourceUrl, string SourceDomain, string TargetUrl, string? AnchorText, string? Rel, DateTime FirstSeenAt,
    DateTime? LastCheckedAt, BacklinkStatus Status, int? LastStatusCode, string? CheckMessage);

public sealed record BacklinkSummaryDto(int Total, int Live, int Nofollow, int Lost, int Error, int Unchecked, int ReferringDomains);

public sealed record BacklinkListDto(PagedResult<BacklinkDto> Page, BacklinkSummaryDto Summary);

public sealed class BacklinkQuery : PageQuery
{
    public BacklinkStatus? Status { get; set; }
}

public sealed class OutreachRequest
{
    [Required, MaxLength(1000)]
    public string ProspectUrl { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? ContactName { get; set; }

    [EmailAddress, MaxLength(254)]
    public string? ContactEmail { get; set; }

    public OutreachStatus Status { get; set; }

    [MaxLength(4000)]
    public string? Notes { get; set; }

    public DateTime? LastContactedAt { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record OutreachDto(
    Guid Id, Guid SiteId, string ProspectUrl, string? ContactName, string? ContactEmail, OutreachStatus Status, string? Notes,
    DateTime? LastContactedAt, Guid ConcurrencyStamp, DateTime UpdatedAt);

/// <summary>On-page analyzer, backlinks (manual/CSV + link checker) and outreach tracking.</summary>
[ApiController]
[HasPermission(Permissions.SeoManage)]
[Route("api/v1/agency/seo")]
public sealed class SeoLinksController(
    AppDbContext db, SeoAccess access, SafeHttpFetcher fetcher, BacklinkChecker checker, IDatabaseDialect dialect, IAuditLogger audit,
    TimeProvider clock) : ControllerBase
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    [HttpPost("analyze")]
    public async Task<OnPageResult> Analyze(AnalyzeRequest request, CancellationToken ct)
    {
        var provided = new[] { request.Url, request.Html, request.Text }.Count(s => !string.IsNullOrWhiteSpace(s));
        if (provided != 1) throw new DomainException("seo.analyze_input", "Provide exactly one of a URL, HTML or text.");
        if (!string.IsNullOrWhiteSpace(request.Text)) return OnPageAnalyzer.AnalyzeText(request.Text, request.Keyword);
        if (!string.IsNullOrWhiteSpace(request.Html)) return OnPageAnalyzer.AnalyzeHtml(request.Html, request.Keyword, null);

        var fetch = await fetcher.GetAsync(request.Url!.Trim(), ct);
        if (fetch.ErrorKind == FetchErrorKind.Blocked)
            throw new DomainException("seo.url_blocked", "That URL points to a private or internal address and cannot be fetched.");
        if (fetch.ErrorKind != FetchErrorKind.None)
            throw new DomainException("seo.fetch_failed", $"The page could not be fetched: {fetch.Error}");
        if (fetch.StatusCode != 200 || !fetch.IsHtml)
            throw new DomainException("seo.fetch_failed", $"The page answered {fetch.StatusCode} ({fetch.ContentType ?? "no content type"}); an HTML 200 page is needed.");
        return OnPageAnalyzer.AnalyzeHtml(fetch.BodyText, request.Keyword, fetch.FinalUrl);
    }

    // ---------------------------------------------------------------- Backlinks

    [HttpGet("sites/{siteId:guid}/backlinks")]
    public async Task<BacklinkListDto> Backlinks(Guid siteId, [FromQuery] BacklinkQuery query, CancellationToken ct)
    {
        await access.SiteAsync(siteId, ct);
        var all = db.Set<SeoBacklink>().AsNoTracking().Where(b => b.SiteId == siteId);
        var q = all;
        if (query.Status is { } s) q = q.Where(b => b.Status == s);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            q = q.Where(b => EF.Functions.Like(b.SourceUrl, like, "\\") || EF.Functions.Like(b.AnchorText!, like, "\\"));
        }
        var page = await q.OrderByDescending(b => b.FirstSeenAt).ThenBy(b => b.Id).ToPagedAsync(query, ct);
        var counts = await all.GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var sources = await all.Select(b => b.SourceUrl).ToListAsync(ct);
        var summary = new BacklinkSummaryDto(counts.Values.Sum(), counts.GetValueOrDefault(BacklinkStatus.Live), counts.GetValueOrDefault(BacklinkStatus.Nofollow),
            counts.GetValueOrDefault(BacklinkStatus.Lost), counts.GetValueOrDefault(BacklinkStatus.Error), counts.GetValueOrDefault(BacklinkStatus.Unchecked),
            sources.Select(RankMath.BareHost).Distinct().Count());
        return new BacklinkListDto(new PagedResult<BacklinkDto>(page.Items.Select(ToDto).ToList(), page.Total, page.Page, page.PageSize), summary);
    }

    [HttpPost("sites/{siteId:guid}/backlinks")]
    public async Task<BacklinkDto> AddBacklink(Guid siteId, BacklinkRequest request, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        var (created, row, error) = await UpsertBacklinkAsync(site, request.SourceUrl, request.TargetUrl, request.AnchorText, request.Rel, request.FirstSeenAt, ct);
        if (error is not null) throw new DomainException("validation.failed", error);
        if (!created) throw DomainException.Conflict("seo.backlink_exists", "This backlink is already listed.");
        audit.Record("seo.backlink_added", nameof(SeoBacklink), row!.Id, after: new { row.SourceUrl, row.TargetUrl });
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    /// <summary>CSV: source_url, target_url, anchor(_text), rel, first_seen. Idempotent per (source, target).</summary>
    [HttpPost("sites/{siteId:guid}/backlinks/import")]
    [RequestSizeLimit(5 * 1024 * 1024 + 64 * 1024)]
    public async Task<ImportResult> ImportBacklinks(Guid siteId, IFormFile file, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        if (file is null || file.Length == 0) throw new DomainException("seo.import_empty", "Choose a CSV file to import.");
        using var reader = new StreamReader(file.OpenReadStream());
        var rows = CsvReader.ParseImport(await reader.ReadToEndAsync(ct));
        var header = CsvReader.Header(rows[0]);
        if (!header.Keys.Any(k => k is "source_url" or "source" or "referring_page_url" or "from_url"))
            throw new DomainException("seo.import_columns", "The CSV needs a source_url column (and target_url).");
        int created = 0, unchanged = 0;
        var errors = new List<string>();
        for (var i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            var source = CsvReader.Get(r, header, "source_url", "source", "referring_page_url", "from_url");
            var target = CsvReader.Get(r, header, "target_url", "target", "to_url") ?? site.BaseUrl;
            DateTime? firstSeen = DateTime.TryParse(CsvReader.Get(r, header, "first_seen", "first_seen_at", "date"), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var fs) ? fs : null;
            if (source is null) { if (errors.Count < 50) errors.Add($"row {i + 1}: source_url missing"); continue; }
            var (isNew, _, error) = await UpsertBacklinkAsync(site, source, target, CsvReader.Get(r, header, "anchor", "anchor_text"),
                CsvReader.Get(r, header, "rel", "link_type"), firstSeen, ct);
            if (error is not null) { if (errors.Count < 50) errors.Add($"row {i + 1}: {error}"); continue; }
            if (isNew) created++; else unchanged++;
        }
        audit.Record("seo.backlinks_imported", nameof(SeoSite), site.Id, after: new { Rows = rows.Count - 1, created, file.FileName });
        await db.SaveChangesAsync(ct);
        return new ImportResult(rows.Count - 1, created, 0, unchanged, 0, errors);
    }

    [HttpDelete("backlinks/{id:guid}")]
    public async Task<IActionResult> DeleteBacklink(Guid id, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoBacklink>(id, b => b.ClientAccountId, "Backlink", ct);
        db.Remove(row);
        audit.Record("seo.backlink_deleted", nameof(SeoBacklink), id, before: new { row.SourceUrl, row.TargetUrl });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Edits a backlink's URLs, anchor text or rel. Changing a URL resets the check status.</summary>
    [HttpPut("backlinks/{id:guid}")]
    public async Task<BacklinkDto> UpdateBacklink(Guid id, BacklinkRequest request, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoBacklink>(id, b => b.ClientAccountId, "Backlink", ct);
        var site = await db.Set<SeoSite>().AsNoTracking().FirstAsync(s => s.Id == row.SiteId, ct);
        var source = request.SourceUrl.Trim();
        var target = request.TargetUrl.Trim();
        if (!Uri.TryCreate(source, UriKind.Absolute, out var su) || SafeHttpFetcher.ValidateUrl(su) is not null)
            throw new DomainException("validation.failed", "The source URL must be an absolute http(s) URL.",
                errors: new Dictionary<string, string[]> { ["sourceUrl"] = new[] { "Enter an absolute http(s) URL." } });
        if (!Uri.TryCreate(target, UriKind.Absolute, out var tu) || SafeHttpFetcher.ValidateUrl(tu) is not null)
            throw new DomainException("validation.failed", "The target URL must be an absolute http(s) URL.",
                errors: new Dictionary<string, string[]> { ["targetUrl"] = new[] { "Enter an absolute http(s) URL." } });
        if (!RegistrableDomain.SameSite(tu.Host, site.Domain.Split(':')[0]))
            throw new DomainException("validation.failed", $"The target must be on {site.Domain}.",
                errors: new Dictionary<string, string[]> { ["targetUrl"] = new[] { $"The target must be on {site.Domain}." } });
        var hash = Normalization.Sha256Hex(BacklinkChecker.Comparable(source) + "\n" + BacklinkChecker.Comparable(target));
        if (hash != row.LinkHash && await db.Set<SeoBacklink>().AnyAsync(b => b.SiteId == row.SiteId && b.LinkHash == hash && b.Id != row.Id, ct))
            throw DomainException.Conflict("seo.backlink_exists", "This backlink is already listed.");
        var before = new { row.SourceUrl, row.TargetUrl, row.AnchorText, row.Rel };
        if (hash != row.LinkHash)
        {
            row.Status = BacklinkStatus.Unchecked;
            row.LastCheckedAt = null;
            row.LastStatusCode = null;
            row.CheckMessage = null;
        }
        row.SourceUrl = source;
        row.TargetUrl = target;
        row.LinkHash = hash;
        row.AnchorText = string.IsNullOrWhiteSpace(request.AnchorText) ? null : request.AnchorText.Trim();
        row.Rel = string.IsNullOrWhiteSpace(request.Rel) ? null : request.Rel.Trim();
        if (request.FirstSeenAt is { } seen) row.FirstSeenAt = seen.ToUniversalTime();
        audit.Record("seo.backlink_updated", nameof(SeoBacklink), row.Id, before, new { row.SourceUrl, row.TargetUrl, row.AnchorText, row.Rel });
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    /// <summary>Checks this site's due backlinks now (up to 25; the daily job checks the rest).</summary>
    [HttpPost("sites/{siteId:guid}/backlinks/check")]
    public async Task<object> CheckBacklinks(Guid siteId, CancellationToken ct)
    {
        await access.SiteAsync(siteId, ct);
        return new { @checked = await checker.CheckDueAsync(siteId, 25, ct) };
    }

    // ---------------------------------------------------------------- Outreach

    [HttpGet("sites/{siteId:guid}/outreach")]
    public async Task<List<OutreachDto>> Outreach(Guid siteId, CancellationToken ct)
    {
        await access.SiteAsync(siteId, ct);
        return (await db.Set<SeoOutreachProspect>().AsNoTracking().Where(o => o.SiteId == siteId)
            .OrderBy(o => o.Status).ThenByDescending(o => o.UpdatedAt).ToListAsync(ct)).Select(ToDto).ToList();
    }

    [HttpPost("sites/{siteId:guid}/outreach")]
    public async Task<OutreachDto> AddOutreach(Guid siteId, OutreachRequest request, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        var row = new SeoOutreachProspect { SiteId = site.Id, ClientAccountId = site.ClientAccountId };
        Apply(row, request);
        db.Add(row);
        audit.Record("seo.outreach_added", nameof(SeoOutreachProspect), row.Id, after: new { row.ProspectUrl, row.Status });
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    [HttpPut("outreach/{id:guid}")]
    public async Task<OutreachDto> UpdateOutreach(Guid id, OutreachRequest request, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoOutreachProspect>(id, o => o.ClientAccountId, "Prospect", ct);
        if (request.ConcurrencyStamp is { } stamp)
        {
            if (stamp != row.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "This prospect was changed by someone else. Reload and try again.");
            db.Entry(row).Property(o => o.ConcurrencyStamp).OriginalValue = stamp;
        }
        var before = new { row.Status };
        Apply(row, request);
        audit.Record("seo.outreach_updated", nameof(SeoOutreachProspect), row.Id, before, new { row.Status });
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    [HttpDelete("outreach/{id:guid}")]
    public async Task<IActionResult> DeleteOutreach(Guid id, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoOutreachProspect>(id, o => o.ClientAccountId, "Prospect", ct);
        db.Remove(row);
        audit.Record("seo.outreach_deleted", nameof(SeoOutreachProspect), id, before: new { row.ProspectUrl, row.Status });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private void Apply(SeoOutreachProspect row, OutreachRequest r)
    {
        if (!Uri.TryCreate(r.ProspectUrl.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
            throw new DomainException("validation.failed", "The prospect URL must be an absolute http(s) URL.");
        if (row.Status != r.Status && r.Status is OutreachStatus.Contacted or OutreachStatus.FollowedUp && r.LastContactedAt is null)
            row.LastContactedAt = Now;
        row.ProspectUrl = r.ProspectUrl.Trim();
        row.ContactName = r.ContactName?.Trim();
        row.ContactEmail = r.ContactEmail?.Trim();
        row.Status = r.Status;
        row.Notes = r.Notes?.Trim();
        if (r.LastContactedAt is { } at) row.LastContactedAt = at.ToUniversalTime();
    }

    private async Task<(bool Created, SeoBacklink? Row, string? Error)> UpsertBacklinkAsync(
        SeoSite site, string source, string target, string? anchor, string? rel, DateTime? firstSeen, CancellationToken ct)
    {
        source = source.Trim();
        target = target.Trim();
        if (!Uri.TryCreate(source, UriKind.Absolute, out var s) || SafeHttpFetcher.ValidateUrl(s) is not null) return (false, null, "The source URL must be an absolute http(s) URL.");
        if (!Uri.TryCreate(target, UriKind.Absolute, out var t) || SafeHttpFetcher.ValidateUrl(t) is not null) return (false, null, "The target URL must be an absolute http(s) URL.");
        if (!RegistrableDomain.SameSite(t.Host, site.Domain.Split(':')[0])) return (false, null, $"The target must be on {site.Domain}.");
        if (source.Length > 2000 || target.Length > 2000) return (false, null, "URLs are limited to 2000 characters.");
        var hash = Normalization.Sha256Hex(BacklinkChecker.Comparable(source) + "\n" + BacklinkChecker.Comparable(target));
        var existing = await db.Set<SeoBacklink>().FirstOrDefaultAsync(b => b.SiteId == site.Id && b.LinkHash == hash, ct);
        if (existing is not null) return (false, existing, null);
        var row = new SeoBacklink
        {
            SiteId = site.Id, ClientAccountId = site.ClientAccountId, SourceUrl = source, TargetUrl = target, LinkHash = hash,
            AnchorText = anchor is { Length: > 500 } ? anchor[..500] : anchor, Rel = rel is { Length: > 100 } ? rel[..100] : rel,
            FirstSeenAt = firstSeen?.ToUniversalTime() ?? Now, Status = BacklinkStatus.Unchecked,
        };
        db.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            db.Entry(row).State = EntityState.Detached;
            return (false, null, null);
        }
        return (true, row, null);
    }

    private static BacklinkDto ToDto(SeoBacklink b) => new(b.Id, b.SiteId, b.SourceUrl, RankMath.BareHost(b.SourceUrl), b.TargetUrl, b.AnchorText, b.Rel,
        b.FirstSeenAt, b.LastCheckedAt, b.Status, b.LastStatusCode, b.CheckMessage);

    private static OutreachDto ToDto(SeoOutreachProspect o) => new(o.Id, o.SiteId, o.ProspectUrl, o.ContactName, o.ContactEmail, o.Status, o.Notes,
        o.LastContactedAt, o.ConcurrencyStamp, o.UpdatedAt);
}
