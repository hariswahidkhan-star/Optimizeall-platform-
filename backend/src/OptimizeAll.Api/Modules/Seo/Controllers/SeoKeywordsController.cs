using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Seo.Ranking;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seo.Controllers;

public sealed class AddKeywordsRequest
{
    /// <summary>One keyword per entry (max 500 per request).</summary>
    [Required, MinLength(1), MaxLength(500)]
    public List<string> Keywords { get; set; } = new();

    public KeywordIntent Intent { get; set; } = KeywordIntent.Unknown;

    [MaxLength(10)]
    public List<string> Tags { get; set; } = new();

    [MaxLength(1000)]
    public string? TargetUrl { get; set; }
}

public sealed class KeywordRequest
{
    public KeywordIntent Intent { get; set; }

    [Range(0, 100_000_000)]
    public int? SearchVolume { get; set; }

    [Range(0, 100)]
    public int? Difficulty { get; set; }

    [MaxLength(1000)]
    public string? TargetUrl { get; set; }

    [MaxLength(10)]
    public List<string> Tags { get; set; } = new();

    public bool IsTracked { get; set; } = true;
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class ManualRankRequest
{
    [Required]
    public DateOnly? Date { get; set; }

    /// <summary>Organic position; null = not ranking in the top 100.</summary>
    [Range(1, 1000)]
    public int? Position { get; set; }

    [MaxLength(1000)]
    public string? Url { get; set; }

    /// <summary>Domain the position belongs to (default: the site's domain; use a competitor's domain for share of voice).</summary>
    [MaxLength(253)]
    public string? Domain { get; set; }

    [MaxLength(15)]
    public List<string> SerpFeatures { get; set; } = new();
}

public sealed record KeywordDto(
    Guid Id, Guid SiteId, string Keyword, KeywordIntent Intent, int? SearchVolume, int? Difficulty, string? TargetUrl,
    IReadOnlyList<string> Tags, bool IsTracked, int? Position, int? PreviousPosition, int? Change, int? BestPosition, string? RankingUrl,
    DateOnly? LastCheckedOn, IReadOnlyList<string> SerpFeatures, Guid ConcurrencyStamp);

public sealed record RankPointDto(DateOnly Date, string Domain, int? Position, string? Url, IReadOnlyList<string> SerpFeatures, RankSource Source);

public sealed record DistributionDto(int Top3, int Top10, int Top20, int Top100, int NotRanking);

public sealed record TrendPointDto(DateOnly Date, double? AveragePosition, int Top10, int Ranked);

public sealed record MoverDto(Guid KeywordId, string Keyword, int? Previous, int? Current, int Change);

public sealed record RankingsOverviewDto(
    DateOnly From, DateOnly To, int TrackedKeywords, DistributionDto Distribution, double? AveragePosition, double? AveragePositionChange,
    IReadOnlyList<TrendPointDto> Trend, IReadOnlyList<MoverDto> Winners, IReadOnlyList<MoverDto> Losers,
    IReadOnlyList<ShareOfVoiceEntry> ShareOfVoice, IReadOnlyDictionary<string, int> SerpFeatures, string ProviderStatus);

public sealed record SearchPerformanceDto(
    DateOnly From, DateOnly To, int Clicks, int Impressions, double? Ctr, double? AveragePosition,
    IReadOnlyList<SearchDayDto> Daily, IReadOnlyList<SearchQueryDto> TopQueries);

public sealed record SearchDayDto(DateOnly Date, int Clicks, int Impressions);

public sealed record SearchQueryDto(string Query, int Clicks, int Impressions, double Ctr, double Position);

public sealed record ProviderRunDto(string Outcome, string? Message, int Checked);

/// <summary>Keyword lists, rank tracking (provider, manual, CSV), rankings overview and Search Console data.</summary>
[ApiController]
[HasPermission(Permissions.SeoManage)]
[Route("api/v1/agency/seo")]
public sealed class SeoKeywordsController(
    AppDbContext db, SeoAccess access, RankStore store, IRankTrackingProvider provider, ISearchConsoleClient searchConsole,
    IAuditLogger audit, TimeProvider clock) : ControllerBase
{
    private const long MaxCsvBytes = 5 * 1024 * 1024;
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    [HttpGet("sites/{siteId:guid}/keywords")]
    public async Task<List<KeywordDto>> List(Guid siteId, [FromQuery] string? tag, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        var keywords = await db.Set<SeoKeyword>().AsNoTracking().Where(k => k.SiteId == siteId).OrderBy(k => k.NormalizedKeyword).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(tag)) keywords = keywords.Where(k => k.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList();
        return await ToDtosAsync(site, keywords, ct);
    }

    [HttpPost("sites/{siteId:guid}/keywords")]
    public async Task<List<KeywordDto>> Add(Guid siteId, AddKeywordsRequest request, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        var tags = CleanTags(request.Tags);
        ValidateUrl(request.TargetUrl, "targetUrl");
        var added = new List<SeoKeyword>();
        foreach (var raw in request.Keywords.Select(k => k.Trim()).Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (raw.Length > 200) throw new DomainException("seo.keyword_too_long", $"Keywords are at most 200 characters ('{raw[..40]}…').");
            var (keyword, created) = await store.EnsureKeywordAsync(site, raw, ct);
            if (!created) continue;
            keyword.Intent = request.Intent;
            keyword.Tags = tags;
            keyword.TargetUrl = string.IsNullOrWhiteSpace(request.TargetUrl) ? null : request.TargetUrl.Trim();
            added.Add(keyword);
        }
        if (added.Count > 0) audit.Record("seo.keywords_added", nameof(SeoSite), site.Id, after: new { Count = added.Count, Keywords = added.Take(50).Select(k => k.Keyword) });
        await db.SaveChangesAsync(ct);
        return await ToDtosAsync(site, added, ct);
    }

    [HttpPut("keywords/{id:guid}")]
    public async Task<KeywordDto> Update(Guid id, KeywordRequest request, CancellationToken ct)
    {
        var keyword = await access.OwnedAsync<SeoKeyword>(id, k => k.ClientAccountId, "Keyword", ct);
        if (request.ConcurrencyStamp is { } stamp)
        {
            if (stamp != keyword.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "This keyword was changed by someone else. Reload and try again.");
            db.Entry(keyword).Property(k => k.ConcurrencyStamp).OriginalValue = stamp;
        }
        ValidateUrl(request.TargetUrl, "targetUrl");
        keyword.Intent = request.Intent;
        keyword.SearchVolume = request.SearchVolume;
        keyword.Difficulty = request.Difficulty;
        keyword.TargetUrl = string.IsNullOrWhiteSpace(request.TargetUrl) ? null : request.TargetUrl.Trim();
        keyword.Tags = CleanTags(request.Tags);
        keyword.IsTracked = request.IsTracked;
        await db.SaveChangesAsync(ct);
        var site = await access.SiteAsync(keyword.SiteId, ct);
        return (await ToDtosAsync(site, new List<SeoKeyword> { keyword }, ct))[0];
    }

    [HttpDelete("keywords/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var keyword = await access.OwnedAsync<SeoKeyword>(id, k => k.ClientAccountId, "Keyword", ct);
        db.Remove(keyword);
        audit.Record("seo.keyword_deleted", nameof(SeoKeyword), id, before: new { keyword.Keyword, keyword.SiteId });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("keywords/{id:guid}/ranks")]
    public async Task<RankPointDto> AddRank(Guid id, ManualRankRequest request, CancellationToken ct)
    {
        var keyword = await access.OwnedAsync<SeoKeyword>(id, k => k.ClientAccountId, "Keyword", ct);
        var site = await access.SiteAsync(keyword.SiteId, ct);
        if (request.Date > Today) throw new DomainException("seo.rank_future", "The date cannot be in the future.");
        ValidateUrl(request.Url, "url");
        var domain = string.IsNullOrWhiteSpace(request.Domain) ? site.Domain : request.Domain;
        if (SeoAccess.NormalizeDomain(domain) is null) throw new DomainException("seo.invalid_domain", "Enter a valid domain.");
        var features = request.SerpFeatures.Select(f => f.Trim().ToLowerInvariant().Replace(' ', '_')).Where(f => f.Length is > 0 and <= 40).Distinct().ToList();
        await store.UpsertSnapshotAsync(keyword, request.Date!.Value, domain, request.Position, request.Url?.Trim(), features, RankSource.Manual, ct);
        return new RankPointDto(request.Date.Value, RankMath.BareHost(domain), request.Position, request.Url, features, RankSource.Manual);
    }

    [HttpGet("keywords/{id:guid}/history")]
    public async Task<List<RankPointDto>> History(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var keyword = await access.OwnedAsync<SeoKeyword>(id, k => k.ClientAccountId, "Keyword", ct, tracked: false);
        var (start, end) = Range(from, to, 90);
        return await db.Set<SeoRankSnapshot>().AsNoTracking()
            .Where(s => s.KeywordId == keyword.Id && s.Date >= start && s.Date <= end)
            .OrderBy(s => s.Date).ThenBy(s => s.Domain)
            .Select(s => new RankPointDto(s.Date, s.Domain, s.Position, s.Url, s.SerpFeatures, s.Source)).ToListAsync(ct);
    }

    /// <summary>CSV import of positions (idempotent per keyword/date/domain). Multipart: file (+ optional date for files without a date column).</summary>
    [HttpPost("sites/{siteId:guid}/ranks/import")]
    [RequestSizeLimit(MaxCsvBytes + 64 * 1024)]
    public async Task<ImportResult> ImportRanks(Guid siteId, IFormFile file, [FromForm] DateOnly? date, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        var csv = await ReadCsvAsync(file, ct);
        var result = await store.ImportRanksAsync(site, csv, date, ct);
        audit.Record("seo.ranks_imported", nameof(SeoSite), site.Id, after: new { result.RowsRead, result.Created, result.Updated, result.KeywordsCreated, file.FileName });
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Runs the rank-tracking provider now for this site (keywords without today's position).</summary>
    [HttpPost("sites/{siteId:guid}/ranks/refresh")]
    public async Task<ProviderRunDto> Refresh(Guid siteId, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        var (outcome, message, count) = await store.RefreshSiteAsync(site, provider, 100, ct);
        return new ProviderRunDto(outcome.ToString(), message, count);
    }

    [HttpGet("sites/{siteId:guid}/rankings")]
    public async Task<RankingsOverviewDto> Overview(Guid siteId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        var (start, end) = Range(from, to, 30);
        return await SeoKpiService.RankingsAsync(db, site, start, end, ct);
    }

    [HttpPost("sites/{siteId:guid}/search-console/import")]
    [RequestSizeLimit(MaxCsvBytes + 64 * 1024)]
    public async Task<ImportResult> ImportSearchConsole(Guid siteId, IFormFile file, [FromForm] DateOnly? date, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        var csv = await ReadCsvAsync(file, ct);
        var result = await store.ImportSearchConsoleAsync(site, csv, date, ct);
        audit.Record("seo.search_console_imported", nameof(SeoSite), site.Id, after: new { result.RowsRead, result.Created, result.Updated, file.FileName });
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Pulls the last <paramref name="days"/> days from the Search Console API (NotConfigured unless connected).</summary>
    [HttpPost("sites/{siteId:guid}/search-console/sync")]
    public async Task<ProviderRunDto> SyncSearchConsole(Guid siteId, [FromQuery] int days, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        days = Math.Clamp(days == 0 ? 28 : days, 1, 90);
        var result = await searchConsole.FetchAsync(site.ClientAccountId, site.BaseUrl, Today.AddDays(-days), Today.AddDays(-1), ct);
        foreach (var row in result.Rows) await store.UpsertPerformanceAsync(site, row, RankSource.SearchConsole, ct);
        return new ProviderRunDto(result.Outcome.ToString(), result.Message, result.Rows.Count);
    }

    [HttpGet("sites/{siteId:guid}/search-console")]
    public async Task<SearchPerformanceDto> SearchPerformance(Guid siteId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        var (start, end) = Range(from, to, 28);
        return await SeoKpiService.SearchPerformanceAsync(db, site.Id, start, end, ct);
    }

    private (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to, int defaultDays)
    {
        var end = to ?? Today;
        var start = from ?? end.AddDays(-defaultDays);
        if (start > end) throw new DomainException("range.invalid", "'from' must be before or equal to 'to'.");
        if (end.DayNumber - start.DayNumber > 366) throw new DomainException("range.too_long", "The date range may span at most 366 days.");
        return (start, end);
    }

    private static async Task<string> ReadCsvAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new DomainException("seo.import_empty", "Choose a CSV file to import.");
        if (file.Length > MaxCsvBytes) throw new DomainException("seo.import_too_large", "CSV files are limited to 5 MB.");
        using var reader = new StreamReader(file.OpenReadStream());
        return await reader.ReadToEndAsync(ct);
    }

    private static List<string> CleanTags(IEnumerable<string> tags) =>
        tags.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length is > 0 and <= 40).Distinct().Take(10).ToList();

    private static void ValidateUrl(string? url, string field)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
            throw new DomainException("validation.failed", "Enter an absolute http(s) URL.", DomainErrorKind.Validation,
                new Dictionary<string, string[]> { [field] = new[] { "Enter an absolute http(s) URL." } });
    }

    private async Task<List<KeywordDto>> ToDtosAsync(SeoSite site, List<SeoKeyword> keywords, CancellationToken ct)
    {
        var ids = keywords.Select(k => k.Id).ToList();
        var own = RankMath.BareHost(site.Domain);
        var snapshots = (await db.Set<SeoRankSnapshot>().AsNoTracking()
                .Where(s => ids.Contains(s.KeywordId) && s.Domain == own)
                .Select(s => new { s.KeywordId, s.Date, s.Position, s.Url, s.SerpFeatures }).ToListAsync(ct))
            .GroupBy(s => s.KeywordId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Date).ToList());
        return keywords.Select(k =>
        {
            var history = snapshots.GetValueOrDefault(k.Id);
            var latest = history?.FirstOrDefault();
            var previous = history?.Skip(1).FirstOrDefault();
            var best = history?.Where(h => h.Position is not null).Select(h => h.Position).Min();
            int? change = latest is not null && previous is not null ? RankMath.Change(previous.Position, latest.Position) : null;
            return new KeywordDto(k.Id, k.SiteId, k.Keyword, k.Intent, k.SearchVolume, k.Difficulty, k.TargetUrl, k.Tags, k.IsTracked,
                latest?.Position, previous?.Position, change, best, latest?.Url, latest?.Date,
                latest?.SerpFeatures ?? new List<string>(), k.ConcurrencyStamp);
        }).ToList();
    }
}
