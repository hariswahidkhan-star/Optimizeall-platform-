using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seo.Ranking;

/// <summary>Minimal RFC 4180 CSV reader (quoted fields, escaped quotes, CRLF/LF, optional BOM).</summary>
public static class CsvReader
{
    /// <summary>Data rows (after the header) one import may contain; each row costs database round trips.</summary>
    public const int MaxImportRows = 10_000;

    /// <summary>Parses an uploaded import file; rejects files with more than <see cref="MaxImportRows"/> data rows.</summary>
    public static List<string[]> ParseImport(string content)
    {
        var rows = Parse(content);
        if (rows.Count == 0) throw new DomainException("seo.import_empty", "The file is empty.");
        if (rows.Count - 1 > MaxImportRows)
            throw new DomainException("seo.import_too_many_rows", $"An import may contain at most {MaxImportRows:N0} rows; split the file and import the parts.");
        return rows;
    }

    public static List<string[]> Parse(string content)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        content = content.TrimStart('﻿');
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());
                    row.Clear();
                    break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());
        }
        return rows;
    }

    /// <summary>Header → column index, with names lower-cased and spaces/dashes turned into underscores.</summary>
    public static Dictionary<string, int> Header(string[] header) =>
        header.Select((h, i) => (Name: h.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_'), Index: i))
            .GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().Index);

    public static string? Get(string[] row, Dictionary<string, int> header, params string[] names)
    {
        foreach (var name in names)
            if (header.TryGetValue(name, out var i) && i < row.Length && row[i].Trim().Length > 0) return row[i].Trim();
        return null;
    }
}

public sealed record ImportResult(int RowsRead, int Created, int Updated, int Unchanged, int KeywordsCreated, IReadOnlyList<string> Errors);

public enum UpsertOutcome
{
    Created,
    Updated,
    Unchanged,
}

/// <summary>
/// Writes rank snapshots and Search Console rows idempotently: one row per (keyword, date, domain) / (site, date,
/// query+page). Re-imports and re-runs update the existing row; a concurrent insert that hits the unique index is
/// retried as an update.
/// </summary>
public sealed class RankStore(AppDbContext db, IDatabaseDialect dialect, TimeProvider clock)
{
    public async Task<UpsertOutcome> UpsertSnapshotAsync(SeoKeyword keyword, DateOnly date, string domain, int? position, string? url,
        IReadOnlyList<string> features, RankSource source, CancellationToken ct)
    {
        domain = RankMath.BareHost(domain);
        url = url is { Length: > 1000 } ? url[..1000] : url;
        for (var attempt = 0; ; attempt++)
        {
            var existing = await db.Set<SeoRankSnapshot>()
                .FirstOrDefaultAsync(s => s.KeywordId == keyword.Id && s.Date == date && s.Domain == domain, ct);
            if (existing is not null)
            {
                var same = existing.Position == position && existing.Url == url && existing.SerpFeatures.SequenceEqual(features);
                if (same) return UpsertOutcome.Unchanged;
                existing.Position = position;
                existing.Url = url;
                existing.SerpFeatures = features.ToList();
                existing.Source = source;
                existing.RecordedAt = clock.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(ct);
                return UpsertOutcome.Updated;
            }
            var row = new SeoRankSnapshot
            {
                KeywordId = keyword.Id, SiteId = keyword.SiteId, ClientAccountId = keyword.ClientAccountId, Date = date, Domain = domain,
                Position = position, Url = url, SerpFeatures = features.ToList(), Source = source, RecordedAt = clock.GetUtcNow().UtcDateTime,
            };
            db.Set<SeoRankSnapshot>().Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
                return UpsertOutcome.Created;
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex) && attempt == 0)
            {
                db.Entry(row).State = EntityState.Detached;
            }
        }
    }

    public async Task<(SeoKeyword Keyword, bool Created)> EnsureKeywordAsync(SeoSite site, string keyword, CancellationToken ct)
    {
        var normalized = SeoText.NormalizeKeyword(keyword);
        var existing = await db.Set<SeoKeyword>().FirstOrDefaultAsync(k => k.SiteId == site.Id && k.NormalizedKeyword == normalized, ct);
        if (existing is not null) return (existing, false);
        var created = new SeoKeyword
        {
            SiteId = site.Id, ClientAccountId = site.ClientAccountId, Keyword = keyword.Trim(), NormalizedKeyword = normalized,
        };
        db.Set<SeoKeyword>().Add(created);
        await db.SaveChangesAsync(ct);
        return (created, true);
    }

    /// <summary>
    /// Imports a rank CSV: keyword|query|top_queries, position|avg_position|average_position, optional date, url|page,
    /// domain, serp_features|features (";"- or "|"-separated), search_volume|volume.
    /// </summary>
    public async Task<ImportResult> ImportRanksAsync(SeoSite site, string csv, DateOnly? defaultDate, CancellationToken ct)
    {
        var rows = CsvReader.ParseImport(csv);
        var header = CsvReader.Header(rows[0]);
        if (!header.Keys.Any(k => k is "keyword" or "query" or "top_queries" or "queries") ||
            !header.Keys.Any(k => k is "position" or "avg_position" or "average_position"))
            throw new DomainException("seo.import_columns", "The CSV needs a keyword (or query) column and a position column.");

        int created = 0, updated = 0, unchanged = 0, keywordsCreated = 0;
        var errors = new List<string>();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = i + 1;
            var keywordText = CsvReader.Get(row, header, "keyword", "query", "top_queries", "queries");
            if (keywordText is null || keywordText.Length > 200) { Error($"row {line}: keyword missing or longer than 200 characters"); continue; }
            var positionText = CsvReader.Get(row, header, "position", "avg_position", "average_position");
            int? position = null;
            if (positionText is not null && !positionText.Equals("-", StringComparison.Ordinal) && !positionText.Equals("not ranking", StringComparison.OrdinalIgnoreCase))
            {
                if (!decimal.TryParse(positionText, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) || p < 1 || p > 1000)
                { Error($"row {line}: position '{positionText}' is not a number between 1 and 1000"); continue; }
                position = (int)Math.Round(p, MidpointRounding.AwayFromZero);
            }
            var dateText = CsvReader.Get(row, header, "date", "day");
            DateOnly date;
            if (dateText is null) date = defaultDate ?? today;
            else if (!DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) { Error($"row {line}: invalid date '{dateText}'"); continue; }
            if (date > today) { Error($"row {line}: date is in the future"); continue; }
            var url = CsvReader.Get(row, header, "url", "page", "landing_page", "ranking_url");
            if (url is not null && !Uri.TryCreate(url, UriKind.Absolute, out _)) { Error($"row {line}: url is not absolute"); continue; }
            var domain = CsvReader.Get(row, header, "domain") ?? site.Domain;
            var features = (CsvReader.Get(row, header, "serp_features", "features") ?? string.Empty)
                .Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(f => f.ToLowerInvariant().Replace(' ', '_')).Where(f => f.Length <= 40).Distinct().Take(15).ToList();

            var (keyword, isNew) = await EnsureKeywordAsync(site, keywordText, ct);
            if (isNew) keywordsCreated++;
            if (CsvReader.Get(row, header, "search_volume", "volume") is { } vol && int.TryParse(vol.Replace(",", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var volume) && volume >= 0 && keyword.SearchVolume != volume)
            {
                keyword.SearchVolume = volume;
                await db.SaveChangesAsync(ct);
            }
            switch (await UpsertSnapshotAsync(keyword, date, domain, position, url, features, RankSource.CsvImport, ct))
            {
                case UpsertOutcome.Created: created++; break;
                case UpsertOutcome.Updated: updated++; break;
                default: unchanged++; break;
            }
        }
        return new ImportResult(rows.Count - 1, created, updated, unchanged, keywordsCreated, errors);

        void Error(string message)
        {
            if (errors.Count < 50) errors.Add(message);
        }
    }

    /// <summary>Imports a Search Console Performance export (query, page, clicks, impressions, ctr, position; date column or <paramref name="defaultDate"/>).</summary>
    public async Task<ImportResult> ImportSearchConsoleAsync(SeoSite site, string csv, DateOnly? defaultDate, CancellationToken ct)
    {
        var rows = CsvReader.ParseImport(csv);
        var header = CsvReader.Header(rows[0]);
        if (!header.Keys.Any(k => k is "query" or "top_queries" or "queries" or "keyword") || !header.ContainsKey("clicks") || !header.ContainsKey("impressions"))
            throw new DomainException("seo.import_columns", "The CSV needs query, clicks and impressions columns (a Search Console Performance export).");

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        int created = 0, updated = 0, unchanged = 0;
        var errors = new List<string>();
        var batch = new List<SearchConsoleRow>();
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var query = CsvReader.Get(row, header, "query", "top_queries", "queries", "keyword");
            if (query is null) { if (errors.Count < 50) errors.Add($"row {i + 1}: query missing"); continue; }
            var dateText = CsvReader.Get(row, header, "date", "day");
            DateOnly date;
            if (dateText is null) date = defaultDate ?? today;
            else if (!DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) { if (errors.Count < 50) errors.Add($"row {i + 1}: invalid date"); continue; }
            var clicks = ParseInt(CsvReader.Get(row, header, "clicks"));
            var impressions = ParseInt(CsvReader.Get(row, header, "impressions"));
            var ctrText = CsvReader.Get(row, header, "ctr");
            var ctr = ctrText is null ? (impressions > 0 ? (double)clicks / impressions : 0)
                : double.TryParse(ctrText.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var c) ? (ctrText.EndsWith('%') ? c / 100 : c) : 0;
            var position = double.TryParse(CsvReader.Get(row, header, "position", "avg_position", "average_position"), NumberStyles.Float, CultureInfo.InvariantCulture, out var pos) ? pos : 0;
            batch.Add(new SearchConsoleRow(date, query, CsvReader.Get(row, header, "page", "top_pages", "url", "landing_page") ?? string.Empty,
                clicks, impressions, Math.Round(ctr, 4), Math.Round(position, 2)));
        }
        foreach (var r in batch)
        {
            switch (await UpsertPerformanceAsync(site, r, RankSource.CsvImport, ct))
            {
                case UpsertOutcome.Created: created++; break;
                case UpsertOutcome.Updated: updated++; break;
                default: unchanged++; break;
            }
        }
        return new ImportResult(rows.Count - 1, created, updated, unchanged, 0, errors);

        static int ParseInt(string? s) =>
            int.TryParse(s?.Replace(",", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : 0;
    }

    public async Task<UpsertOutcome> UpsertPerformanceAsync(SeoSite site, SearchConsoleRow r, RankSource source, CancellationToken ct)
    {
        var query = r.Query.Length > 500 ? r.Query[..500] : r.Query;
        var page = r.Page.Length > 1000 ? r.Page[..1000] : r.Page;
        var hash = Normalization.Sha256Hex(query.ToLowerInvariant() + "\n" + page);
        var existing = await db.Set<SeoSearchPerformance>().FirstOrDefaultAsync(p => p.SiteId == site.Id && p.Date == r.Date && p.RowHash == hash, ct);
        if (existing is not null)
        {
            if (existing.Clicks == r.Clicks && existing.Impressions == r.Impressions && Math.Abs(existing.Ctr - r.Ctr) < 1e-9 && Math.Abs(existing.Position - r.Position) < 1e-9)
                return UpsertOutcome.Unchanged;
            existing.Clicks = r.Clicks;
            existing.Impressions = r.Impressions;
            existing.Ctr = r.Ctr;
            existing.Position = r.Position;
            existing.Source = source;
            await db.SaveChangesAsync(ct);
            return UpsertOutcome.Updated;
        }
        var row = new SeoSearchPerformance
        {
            SiteId = site.Id, ClientAccountId = site.ClientAccountId, Date = r.Date, Query = query, Page = page, RowHash = hash,
            Clicks = r.Clicks, Impressions = r.Impressions, Ctr = r.Ctr, Position = r.Position, Source = source,
        };
        db.Set<SeoSearchPerformance>().Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return UpsertOutcome.Created;
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            db.Entry(row).State = EntityState.Detached;
            return UpsertOutcome.Unchanged;
        }
    }

    /// <summary>Runs the rank provider for a site's tracked keywords that have no own-domain snapshot for today.</summary>
    public async Task<(ProviderOutcome Outcome, string? Message, int Checked)> RefreshSiteAsync(
        SeoSite site, IRankTrackingProvider provider, int maxKeywords, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var ownDomain = RankMath.BareHost(site.Domain);
        var done = db.Set<SeoRankSnapshot>().Where(s => s.SiteId == site.Id && s.Date == today && s.Domain == ownDomain).Select(s => s.KeywordId);
        var keywords = await db.Set<SeoKeyword>().Where(k => k.SiteId == site.Id && k.IsTracked && !done.Contains(k.Id))
            .OrderBy(k => k.NormalizedKeyword).Take(maxKeywords).ToListAsync(ct);
        if (keywords.Count == 0) return (ProviderOutcome.Ok, "All tracked keywords already have today's position.", 0);

        var result = await provider.CheckAsync(new RankCheckRequest(site.ClientAccountId, site.Domain, site.TargetCountry, site.TargetLanguage,
            keywords.Select(k => k.Keyword).ToList()), ct);
        var competitors = site.Competitors.Select(RankMath.BareHost).ToHashSet(StringComparer.Ordinal);
        foreach (var serp in result.Results)
        {
            var keyword = keywords.FirstOrDefault(k => k.Keyword == serp.Keyword);
            if (keyword is null) continue;
            await UpsertSnapshotAsync(keyword, today, ownDomain, serp.Position, serp.Url, serp.Features, RankSource.DataForSeo, ct);
            foreach (var competitor in competitors)
            {
                var hit = serp.Organic.Where(o => o.Domain == competitor || o.Domain.EndsWith("." + competitor, StringComparison.Ordinal))
                    .OrderBy(o => o.Position).FirstOrDefault();
                await UpsertSnapshotAsync(keyword, today, competitor, hit?.Position, hit?.Url, Array.Empty<string>(), RankSource.DataForSeo, ct);
            }
        }
        return (result.Outcome, result.Message, result.Results.Count);
    }
}

/// <summary>Daily rank snapshots for every active site (provider permitting). Idempotent: one snapshot per keyword/day/domain.</summary>
public sealed class RankTrackingJob(AppDbContext db, RankStore store, IRankTrackingProvider provider) : IJob
{
    public string Name => "seo.rank-tracking";

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var sites = await db.Set<SeoSite>().Where(s => !s.IsArchived).OrderBy(s => s.Id).ToListAsync(ct);
        int checkedCount = 0, notConfigured = 0, failed = 0;
        foreach (var site in sites)
        {
            var (outcome, _, count) = await store.RefreshSiteAsync(site, provider, 200, ct);
            checkedCount += count;
            if (outcome == ProviderOutcome.NotConfigured) notConfigured++;
            if (outcome == ProviderOutcome.Error) failed++;
        }
        return $"{checkedCount} keyword(s) checked across {sites.Count} site(s); not configured for {notConfigured}, errors for {failed}";
    }
}
