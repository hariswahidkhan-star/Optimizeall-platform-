using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.SocialMedia;

// ---------------------------------------------------------------- DTOs

public sealed record SocialTotalsDto(long Impressions, long Reach, long Engagements, long Clicks, long VideoViews, decimal? EngagementRate,
    long? FollowersStart, long? FollowersEnd, long? FollowersGrowth);

public sealed record ProfileKpiDto(Guid ProfileId, SocialNetwork Network, string Handle, SocialTotalsDto Totals, string SourceLabel);

public sealed record SocialKpisDto(
    Guid ClientAccountId, DateOnly From, DateOnly To, SocialTotalsDto Totals, int PostsPublished, IReadOnlyList<ProfileKpiDto> Profiles,
    string SourceLabel, IReadOnlyList<string> Sources, string Definitions);

public sealed record SeriesPointDto(DateOnly Date, long Impressions, long Engagements, long Followers, long Reach);

public sealed record TopPostDto(
    string PostKey, Guid? PostId, string? Title, SocialNetwork Network, string ProfileHandle, DateTime? PublishedAt, long Impressions, long Reach,
    long Engagements, long Clicks, long VideoViews, decimal? EngagementRate, string SourceLabel, string? Url);

public sealed record BestTimeSlotDto(DayOfWeek Day, int Hour, int Posts, decimal? AvgEngagementRate);

public sealed record BestTimesDto(bool EnoughData, int PostsAnalysed, IReadOnlyList<BestTimeSlotDto> Slots, string SourceLabel, string Note,
    IReadOnlyList<BestTimeDto> PresetGuidance);

public sealed class SocialImportInput
{
    /// <summary>"profile-daily" or "post".</summary>
    [Required, RegularExpression("^(profile-daily|post)$")] public string Kind { get; set; } = "profile-daily";
    [Required, MaxLength(255)] public string FileName { get; set; } = "import.csv";
    [Required, MaxLength(5_000_000)] public string Csv { get; set; } = string.Empty;

    /// <summary>Target field → CSV header. Omit on preview to get a suggested mapping.</summary>
    public Dictionary<string, string>? Mapping { get; set; }

    /// <summary>PlatformExport (a file exported from the network: "Measured") or Manual (typed-in figures: "Manual").</summary>
    public MetricSource Source { get; set; } = MetricSource.PlatformExport;
}

public sealed record SocialImportPreviewDto(IReadOnlyList<string> Headers, IReadOnlyDictionary<string, string> SuggestedMapping,
    IReadOnlyList<string> TargetFields, IReadOnlyList<string> RequiredFields, IReadOnlyList<IReadOnlyList<string>> SampleRows,
    int RowCount, IReadOnlyList<string> Errors);

public sealed record SocialImportResultDto(Guid ImportId, int RowsTotal, int RowsImported, int RowsUpdated, int RowsSkipped, IReadOnlyList<string> Errors, string SourceLabel);

// ---------------------------------------------------------------- service

public sealed class SocialAnalyticsService(AppDbContext db, NetworkPresetProvider presets, TimeProvider clock)
{
    public const string Definitions =
        "Engagement rate = engagements ÷ impressions. Followers growth = followers on the last day with data − first day with data. " +
        "Sources: Measured = network API or a file exported from the network; Manual = figures typed in by staff.";

    public async Task<SocialKpisDto> KpisAsync(Guid clientId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var profiles = await db.Set<BrandProfile>().AsNoTracking().Where(p => p.ClientAccountId == clientId).ToListAsync(ct);
        var rows = await db.Set<SocialProfileMetric>().AsNoTracking()
            .Where(m => m.ClientAccountId == clientId && m.Date >= from && m.Date <= to).ToListAsync(ct);
        var fromDt = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDt = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var published = await db.Set<SocialPostVariant>().AsNoTracking()
            .CountAsync(v => v.ClientAccountId == clientId && v.PublishStatus == VariantPublishStatus.Published && v.PublishedAt >= fromDt && v.PublishedAt < toDt, ct);

        var perProfile = profiles.Select(p =>
        {
            var r = rows.Where(x => x.ProfileId == p.Id).ToList();
            return new ProfileKpiDto(p.Id, p.Network, p.Handle, Totals(r), Label(r.Select(x => x.Source)));
        }).Where(p => p.Totals.Impressions > 0 || p.Totals.FollowersEnd is not null).ToList();

        var sources = rows.Select(r => MetricSources.Label(r.Source)).Distinct().OrderBy(s => s).ToList();
        var followersStart = perProfile.Sum(p => p.Totals.FollowersStart ?? 0);
        var followersEnd = perProfile.Sum(p => p.Totals.FollowersEnd ?? 0);
        var totals = new SocialTotalsDto(rows.Sum(r => r.Impressions), rows.Sum(r => r.Reach), rows.Sum(r => r.Engagements), rows.Sum(r => r.Clicks),
            rows.Sum(r => r.VideoViews), AdKpis.Ratio(rows.Sum(r => r.Engagements), rows.Sum(r => r.Impressions), 6),
            perProfile.Count == 0 ? null : followersStart, perProfile.Count == 0 ? null : followersEnd,
            perProfile.Count == 0 ? null : followersEnd - followersStart);
        return new SocialKpisDto(clientId, from, to, totals, published, perProfile, Label(rows.Select(r => r.Source)), sources, Definitions);
    }

    public async Task<IReadOnlyList<SeriesPointDto>> SeriesAsync(Guid clientId, DateOnly from, DateOnly to, Guid? profileId, CancellationToken ct)
    {
        var q = db.Set<SocialProfileMetric>().AsNoTracking().Where(m => m.ClientAccountId == clientId && m.Date >= from && m.Date <= to);
        if (profileId is { } p) q = q.Where(m => m.ProfileId == p);
        var rows = await q.ToListAsync(ct);
        return rows.GroupBy(r => r.Date).OrderBy(g => g.Key)
            .Select(g => new SeriesPointDto(g.Key, g.Sum(x => x.Impressions), g.Sum(x => x.Engagements), g.Sum(x => x.Followers), g.Sum(x => x.Reach)))
            .ToList();
    }

    public async Task<IReadOnlyList<TopPostDto>> TopPostsAsync(Guid clientId, DateOnly from, DateOnly to, int limit, CancellationToken ct)
    {
        var latest = await LatestPostMetricsAsync(clientId, from, to, ct);
        var profiles = await db.Set<BrandProfile>().AsNoTracking().Where(p => p.ClientAccountId == clientId).ToDictionaryAsync(p => p.Id, p => p.Handle, ct);
        var postIds = latest.Where(m => m.PostId != null).Select(m => m.PostId!.Value).Distinct().ToList();
        var titles = await db.Set<SocialPost>().AsNoTracking().Where(p => postIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Title, ct);
        var variantIds = latest.Where(m => m.VariantId != null).Select(m => m.VariantId!.Value).ToList();
        var urls = await db.Set<SocialPostVariant>().AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.PublishedUrl, ct);
        return latest.OrderByDescending(m => m.Engagements).ThenByDescending(m => m.Impressions).Take(Math.Clamp(limit, 1, 50))
            .Select(m => new TopPostDto(m.PostKey, m.PostId, m.PostId is { } pid ? titles.GetValueOrDefault(pid) : null, m.Network,
                profiles.GetValueOrDefault(m.ProfileId, ""), m.PublishedAt, m.Impressions, m.Reach, m.Engagements, m.Clicks, m.VideoViews,
                AdKpis.Ratio(m.Engagements, m.Impressions, 6), MetricSources.Label(m.Source),
                m.VariantId is { } vid ? urls.GetValueOrDefault(vid) : m.PostKey.StartsWith("https://", StringComparison.Ordinal) ? m.PostKey : null))
            .ToList();
    }

    /// <summary>Engagement rate by local weekday/hour of the client's own published posts (at least 3 posts per slot).</summary>
    public async Task<BestTimesDto> BestTimesAsync(ClientAccount client, CancellationToken ct)
    {
        var to = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var latest = await LatestPostMetricsAsync(client.Id, to.AddDays(-365), to, ct);
        var zone = QueueScheduler.Zone(client.TimeZone);
        var withTime = latest.Where(m => m.PublishedAt is not null && m.Impressions > 0).ToList();
        var slots = withTime
            .GroupBy(m =>
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(m.PublishedAt!.Value, DateTimeKind.Utc), zone);
                return (local.DayOfWeek, local.Hour);
            })
            .Where(g => g.Count() >= 3)
            .Select(g => new BestTimeSlotDto(g.Key.DayOfWeek, g.Key.Hour, g.Count(),
                AdKpis.Ratio(g.Sum(x => x.Engagements), g.Sum(x => x.Impressions), 6)))
            .OrderByDescending(s => s.AvgEngagementRate).Take(6).ToList();
        var guidance = (await presets.AllAsync(ct)).Values.OrderBy(r => r.Network)
            .Select(r => new BestTimeDto(r.Network, r.RecommendedTimes, NetworkPresets.TimesSource)).ToList();
        var enough = withTime.Count >= 10 && slots.Count > 0;
        return new BestTimesDto(enough, withTime.Count, slots, Label(withTime.Select(m => m.Source)),
            enough
                ? $"From {withTime.Count} of this client's posts (engagements ÷ impressions, {client.TimeZone})."
                : "Not enough of this client's own post data yet (at least 10 posts with metrics); showing general guidance.",
            guidance);
    }

    private async Task<List<SocialPostMetric>> LatestPostMetricsAsync(Guid clientId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await db.Set<SocialPostMetric>().AsNoTracking()
            .Where(m => m.ClientAccountId == clientId && m.Date >= from && m.Date <= to.AddDays(60)).ToListAsync(ct);
        var fromDt = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDt = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return rows.GroupBy(m => (m.ProfileId, m.PostKey)).Select(g => g.OrderByDescending(x => x.Date).First())
            .Where(m => m.PublishedAt is null ? m.Date >= from && m.Date <= to : m.PublishedAt >= fromDt && m.PublishedAt < toDt).ToList();
    }

    private static SocialTotalsDto Totals(IReadOnlyList<SocialProfileMetric> r)
    {
        var withFollowers = r.Where(x => x.Followers > 0).OrderBy(x => x.Date).ToList();
        long? start = withFollowers.Count > 0 ? withFollowers[0].Followers : null;
        long? end = withFollowers.Count > 0 ? withFollowers[^1].Followers : null;
        return new SocialTotalsDto(r.Sum(x => x.Impressions), r.Sum(x => x.Reach), r.Sum(x => x.Engagements), r.Sum(x => x.Clicks), r.Sum(x => x.VideoViews),
            AdKpis.Ratio(r.Sum(x => x.Engagements), r.Sum(x => x.Impressions), 6), start, end, end - start);
    }

    public static string Label(IEnumerable<MetricSource> sources)
    {
        var labels = sources.Select(MetricSources.Label).Distinct().ToList();
        return labels.Count switch
        {
            0 => "No data",
            1 => labels[0],
            _ => "Mixed (Measured and Manual)",
        };
    }
}

/// <summary>CSV import of social metrics with column mapping; idempotent upserts by (profile, date) / (profile, post, date).</summary>
public sealed class SocialMetricImporter(AppDbContext db, IDatabaseDialect dialect, ICurrentUser currentUser, IAuditLogger audit, TimeProvider clock)
{
    public static readonly IReadOnlyList<string> ProfileFields = new[] { "date", "followers", "impressions", "reach", "engagements", "clicks", "videoViews" };
    public static readonly IReadOnlyList<string> PostFields = new[] { "postKey", "date", "publishedAt", "impressions", "reach", "engagements", "clicks", "videoViews" };

    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["date"] = new[] { "Date", "Day", "Data date" },
        ["followers"] = new[] { "Followers", "Page followers", "Total followers", "Follower count" },
        ["impressions"] = new[] { "Impressions", "Views", "Post impressions" },
        ["reach"] = new[] { "Reach", "Accounts reached", "Unique impressions" },
        ["engagements"] = new[] { "Engagements", "Engagement", "Interactions", "Total interactions" },
        ["clicks"] = new[] { "Clicks", "Link clicks" },
        ["videoViews"] = new[] { "Video views", "Plays", "3-second video views" },
        ["postKey"] = new[] { "Post ID", "Permalink", "Post link", "URL", "Tweet id", "Media ID" },
        ["publishedAt"] = new[] { "Publish time", "Published", "Created time", "Date published" },
    };

    public SocialImportPreviewDto Preview(SocialImportInput input)
    {
        var rows = CsvReader.Parse(input.Csv);
        if (rows.Count == 0) return new SocialImportPreviewDto(Array.Empty<string>(), new Dictionary<string, string>(), Fields(input.Kind), Required(input.Kind),
            Array.Empty<IReadOnlyList<string>>(), 0, new[] { "The file is empty." });
        var headers = rows[0].Select(h => h.Trim()).ToList();
        var suggested = new Dictionary<string, string>();
        foreach (var field in Fields(input.Kind))
        {
            var match = Aliases.GetValueOrDefault(field, Array.Empty<string>()).Select(a => headers.FirstOrDefault(h => string.Equals(h, a, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(h => h is not null);
            if (match is not null) suggested[field] = match;
        }
        var errors = Required(input.Kind).Where(f => !suggested.ContainsKey(f)).Select(f => $"No column found for \"{f}\"; map it manually.").ToList();
        return new SocialImportPreviewDto(headers, suggested, Fields(input.Kind), Required(input.Kind),
            rows.Skip(1).Take(5).Select(r => (IReadOnlyList<string>)r.ToList()).ToList(), rows.Count - 1, errors);
    }

    public async Task<SocialImportResultDto> ImportAsync(BrandProfile profile, SocialImportInput input, CancellationToken ct)
    {
        if (input.Source == MetricSource.Api) throw new DomainException("social.source_invalid", "Imports are PlatformExport or Manual.");
        var mapping = input.Mapping ?? throw new DomainException("import.mapping_required", "Provide the column mapping.");
        var rows = CsvReader.Parse(input.Csv);
        if (rows.Count < 2) throw new DomainException("import.empty", "The file has no data rows.");
        var headers = rows[0].Select(h => h.Trim()).ToArray();
        var errors = new List<string>();
        foreach (var f in Required(input.Kind))
            if (!mapping.ContainsKey(f)) errors.Add($"Map a column to \"{f}\".");
        var index = new Dictionary<string, int>();
        foreach (var (field, header) in mapping)
        {
            if (!Fields(input.Kind).Contains(field)) { errors.Add($"Unknown field \"{field}\"."); continue; }
            var i = Array.FindIndex(headers, h => string.Equals(h, header, StringComparison.OrdinalIgnoreCase));
            if (i < 0) errors.Add($"Column \"{header}\" is not in the file."); else index[field] = i;
        }
        if (errors.Count > 0) throw new DomainException("import.mapping_invalid", string.Join(" ", errors), errors: new Dictionary<string, string[]> { ["mapping"] = errors.ToArray() });

        var now = clock.GetUtcNow().UtcDateTime;
        var batch = new SocialMetricImport
        {
            ClientAccountId = profile.ClientAccountId, ProfileId = profile.Id, Kind = input.Kind, FileName = input.FileName, Source = input.Source,
            CreatedByUserId = currentUser.Id, CreatedAt = now,
        };
        var today = DateOnly.FromDateTime(now);
        var seen = new HashSet<string>();
        // Concurrent imports (and the insights sync) of this profile would both insert the same (profile, date) rows.
        await using var writeLock = await SocialWriteLocks.AcquireAsync(dialect, db, SocialWriteLocks.Metrics(profile.Id), ct);
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            batch.RowsTotal++;
            string Cell(string f) => index.TryGetValue(f, out var i) && i < row.Length ? row[i].Trim() : string.Empty;
            var rowErrors = new List<string>();
            var dateText = Cell("date");
            DateOnly date = today;
            if (dateText.Length > 0 && !AdImportParser.TryParseDate(dateText.Length > 10 && dateText[4] == '-' ? dateText[..10] : dateText, out date))
                rowErrors.Add($"invalid date \"{dateText}\"");
            if (input.Kind == "profile-daily" && dateText.Length == 0) rowErrors.Add("date is empty");
            long W(string f) => index.ContainsKey(f) ? AdImportParser.Whole(Cell(f), f, rowErrors) : 0;
            var impressions = W("impressions");
            var reach = W("reach");
            var engagements = W("engagements");
            var clicks = W("clicks");
            var views = W("videoViews");
            if (rowErrors.Count > 0)
            {
                batch.RowsSkipped++;
                if (errors.Count < 100) errors.Add($"Row {r + 1}: {string.Join("; ", rowErrors)}.");
                continue;
            }

            if (input.Kind == "profile-daily")
            {
                if (!seen.Add(date.ToString("O"))) { batch.RowsSkipped++; errors.Add($"Row {r + 1}: duplicate date {date:yyyy-MM-dd} in file."); continue; }
                var followers = W("followers");
                var existing = await db.Set<SocialProfileMetric>().FirstOrDefaultAsync(m => m.ProfileId == profile.Id && m.Date == date, ct);
                if (existing is not null && existing.Source == MetricSource.Api && input.Source == MetricSource.Manual)
                {
                    batch.RowsSkipped++;
                    errors.Add($"Row {r + 1}: {date:yyyy-MM-dd} already has API data; manual figures do not overwrite it.");
                    continue;
                }
                if (existing is null)
                {
                    existing = new SocialProfileMetric { ClientAccountId = profile.ClientAccountId, ProfileId = profile.Id, Date = date };
                    db.Set<SocialProfileMetric>().Add(existing);
                    batch.RowsImported++;
                }
                else batch.RowsUpdated++;
                existing.Followers = followers; existing.Impressions = impressions; existing.Reach = reach; existing.Engagements = engagements;
                existing.Clicks = clicks; existing.VideoViews = views; existing.Source = input.Source; existing.ImportBatchId = batch.Id; existing.UpdatedAt = now;
            }
            else
            {
                var key = Cell("postKey");
                if (key.Length is 0 or > 500) { batch.RowsSkipped++; errors.Add($"Row {r + 1}: post id/link is empty or too long."); continue; }
                if (!seen.Add(key + "|" + date.ToString("O"))) { batch.RowsSkipped++; continue; }
                DateTime? publishedAt = DateTime.TryParse(Cell("publishedAt"), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var p) ? p : null;
                var variant = await db.Set<SocialPostVariant>().AsNoTracking()
                    .Where(v => v.ProfileId == profile.Id && (v.ExternalPostId == key || v.PublishedUrl == key)).Select(v => new { v.Id, v.PostId, v.PublishedAt })
                    .FirstOrDefaultAsync(ct);
                var existing = await db.Set<SocialPostMetric>().FirstOrDefaultAsync(m => m.ProfileId == profile.Id && m.PostKey == key && m.Date == date, ct);
                if (existing is null)
                {
                    existing = new SocialPostMetric { ClientAccountId = profile.ClientAccountId, ProfileId = profile.Id, Network = profile.Network, PostKey = key, Date = date };
                    db.Set<SocialPostMetric>().Add(existing);
                    batch.RowsImported++;
                }
                else batch.RowsUpdated++;
                existing.PostId = variant?.PostId; existing.VariantId = variant?.Id;
                existing.PublishedAt = publishedAt ?? variant?.PublishedAt ?? existing.PublishedAt;
                existing.Impressions = impressions; existing.Reach = reach; existing.Engagements = engagements; existing.Clicks = clicks;
                existing.VideoViews = views; existing.Source = input.Source; existing.ImportBatchId = batch.Id; existing.UpdatedAt = now;
            }
        }
        batch.Errors = errors.Take(100).ToList();
        db.Set<SocialMetricImport>().Add(batch);
        audit.Record("social.metrics.imported", nameof(SocialMetricImport), batch.Id,
            after: new { batch.Kind, batch.RowsImported, batch.RowsUpdated, batch.RowsSkipped, batch.Source, profile = profile.Id });
        await db.SaveChangesAsync(ct);
        return new SocialImportResultDto(batch.Id, batch.RowsTotal, batch.RowsImported, batch.RowsUpdated, batch.RowsSkipped, batch.Errors,
            MetricSources.Label(input.Source));
    }

    private static IReadOnlyList<string> Fields(string kind) => kind == "post" ? PostFields : ProfileFields;

    private static IReadOnlyList<string> Required(string kind) => kind == "post" ? new[] { "postKey", "impressions", "engagements" } : new[] { "date", "impressions", "engagements" };
}

/// <summary>
/// Meta Insights sync (behind credentials): Page daily impressions/reach/engagements, Instagram daily reach, follower
/// counts and lifetime metrics of posts we published. Other networks are not configured and are skipped (CSV import).
/// </summary>
public sealed class SocialMetricsSyncJob(AppDbContext db, IDatabaseDialect dialect, MetaGraphClient graph, ProfileTokenStore tokens,
    ICredentialVault vault, TimeProvider clock, ILogger<SocialMetricsSyncJob> logger) : IJob
{
    public string Name => nameof(SocialMetricsSyncJob);

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var profiles = await db.Set<BrandProfile>().AsNoTracking()
            .Where(p => p.IsActive && p.ConnectionStatus == ProfileConnectionStatus.Connected
                        && (p.Network == SocialNetwork.Facebook || p.Network == SocialNetwork.Instagram) && p.ExternalId != null)
            .ToListAsync(ct);
        int synced = 0, failed = 0;
        foreach (var profile in profiles)
        {
            var token = await tokens.ReadAsync(profile, ct);
            if (token is null) continue;
            try
            {
                var ok = await SyncProfileAsync(profile, token, ct);
                if (ok) synced++; else failed++;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested
                                       || ex is DomainException { Code: "social.write_busy" })
            {
                logger.LogWarning(ex, "Insights sync failed for profile {Profile}", profile.Id);
                failed++;
            }
            db.ChangeTracker.Clear();
        }
        var notConfigured = await db.Set<BrandProfile>().CountAsync(p => p.IsActive, ct) - profiles.Count;
        return $"profiles synced {synced}, failed {failed}, not configured/skipped {notConfigured}";
    }

    public async Task<bool> SyncProfileAsync(BrandProfile profile, StoredToken token, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var since = new DateTimeOffset(now.Date.AddDays(-7)).ToUnixTimeSeconds();
        var until = new DateTimeOffset(now.Date).ToUnixTimeSeconds();
        var id = Uri.EscapeDataString(profile.ExternalId!);
        var metrics = profile.Network == SocialNetwork.Facebook
            ? "page_impressions,page_impressions_unique,page_post_engagements"
            : "reach";
        var insights = await graph.GetAsync($"{id}/insights?metric={metrics}&period=day&since={since}&until={until}", token.AccessToken, ct);
        if (!insights.Success) return await FailAsync(profile, token, insights, ct);

        var daily = new Dictionary<DateOnly, SocialProfileMetric>();
        if (insights.Body.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var metric in data.EnumerateArray())
            {
                var name = MetaGraphClient.Str(metric, "name");
                if (!metric.TryGetProperty("values", out var values)) continue;
                foreach (var v in values.EnumerateArray())
                {
                    if (!v.TryGetProperty("value", out var val) || !val.TryGetInt64(out var n)) continue;
                    if (!DateTime.TryParse(MetaGraphClient.Str(v, "end_time"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var end)) continue;
                    var date = DateOnly.FromDateTime(end.AddDays(-1));
                    if (!daily.TryGetValue(date, out var row)) daily[date] = row = new SocialProfileMetric();
                    switch (name)
                    {
                        case "page_impressions": row.Impressions = n; break;
                        case "page_impressions_unique" or "reach": row.Reach = n; break;
                        case "page_post_engagements": row.Engagements = n; break;
                    }
                }
            }
        }
        var info = await graph.GetAsync($"{id}?fields=followers_count", token.AccessToken, ct);
        long? followers = info.Success && info.Body.TryGetProperty("followers_count", out var fc) && fc.TryGetInt64(out var f) ? f : null;
        if (followers is not null && !daily.ContainsKey(today)) daily[today] = new SocialProfileMetric();

        // Lifetime metrics of recent posts we published (fetched before taking the write lock).
        var cutoff = now.AddDays(-30);
        var variants = await db.Set<SocialPostVariant>().AsNoTracking()
            .Where(v => v.ProfileId == profile.Id && v.PublishStatus == VariantPublishStatus.Published && v.ExternalPostId != null && v.PublishedAt >= cutoff)
            .OrderByDescending(v => v.PublishedAt).ThenBy(v => v.Id)
            .Take(50).ToListAsync(ct);
        var postValues = new List<(SocialPostVariant Variant, Dictionary<string, long> Values, long Engagements)>();
        foreach (var v in variants)
        {
            var pid = Uri.EscapeDataString(v.ExternalPostId!);
            var postMetrics = profile.Network == SocialNetwork.Facebook ? "post_impressions,post_impressions_unique,post_clicks" : "reach,total_interactions,views";
            var res = await graph.GetAsync($"{pid}/insights?metric={postMetrics}", token.AccessToken, ct);
            if (!res.Success) continue;
            var values = new Dictionary<string, long>();
            if (res.Body.TryGetProperty("data", out var pd) && pd.ValueKind == JsonValueKind.Array)
                foreach (var m in pd.EnumerateArray())
                    if (MetaGraphClient.Str(m, "name") is { } n && m.TryGetProperty("values", out var vals) && vals.GetArrayLength() > 0
                        && vals[0].TryGetProperty("value", out var val) && val.TryGetInt64(out var num))
                        values[n] = num;
            long engagements = values.GetValueOrDefault("total_interactions");
            if (profile.Network == SocialNetwork.Facebook)
            {
                var social = await graph.GetAsync($"{pid}?fields=reactions.summary(total_count).limit(0),comments.summary(total_count).limit(0),shares", token.AccessToken, ct);
                if (social.Success)
                    engagements = Count(social.Body, "reactions") + Count(social.Body, "comments")
                                  + (social.Body.TryGetProperty("shares", out var sh) && sh.TryGetProperty("count", out var sc) && sc.TryGetInt64(out var s) ? s : 0);
            }
            postValues.Add((v, values, engagements));
        }

        // Upserts by (profile, date) / (profile, post, date), serialized with CSV imports of the same profile.
        await using var writeLock = await SocialWriteLocks.AcquireAsync(dialect, db, SocialWriteLocks.Metrics(profile.Id), ct);
        foreach (var (date, values) in daily)
        {
            var row = await db.Set<SocialProfileMetric>().FirstOrDefaultAsync(m => m.ProfileId == profile.Id && m.Date == date, ct);
            if (row is null)
            {
                row = new SocialProfileMetric { ClientAccountId = profile.ClientAccountId, ProfileId = profile.Id, Date = date };
                db.Set<SocialProfileMetric>().Add(row);
            }
            row.Impressions = values.Impressions; row.Reach = values.Reach; row.Engagements = values.Engagements;
            if (date == today && followers is not null) row.Followers = followers.Value;
            row.Source = MetricSource.Api; row.UpdatedAt = now;
        }

        var seenPosts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (v, values, engagements) in postValues)
        {
            if (!seenPosts.Add(v.ExternalPostId!)) continue; // one row per (profile, post, date)
            var row = await db.Set<SocialPostMetric>().FirstOrDefaultAsync(m => m.ProfileId == profile.Id && m.PostKey == v.ExternalPostId && m.Date == today, ct);
            if (row is null)
            {
                row = new SocialPostMetric { ClientAccountId = profile.ClientAccountId, ProfileId = profile.Id, Network = profile.Network, PostKey = v.ExternalPostId!, Date = today };
                db.Set<SocialPostMetric>().Add(row);
            }
            row.PostId = v.PostId; row.VariantId = v.Id; row.PublishedAt = v.PublishedAt;
            row.Impressions = values.GetValueOrDefault("post_impressions", values.GetValueOrDefault("views"));
            row.Reach = values.GetValueOrDefault("post_impressions_unique", values.GetValueOrDefault("reach"));
            row.Clicks = values.GetValueOrDefault("post_clicks");
            row.VideoViews = values.GetValueOrDefault("views");
            row.Engagements = engagements; row.Source = MetricSource.Api; row.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> FailAsync(BrandProfile profile, StoredToken token, MetaGraphClient.GraphResponse response, CancellationToken ct)
    {
        if (response.TokenExpired || response.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            var message = "Meta rejected the token during insights sync; reconnect the profile.";
            await vault.MarkStatusAsync(token.ConnectionId, IntegrationStatus.Error, message, ct);
            await db.Set<BrandProfile>().Where(p => p.Id == profile.Id).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ConnectionStatus, ProfileConnectionStatus.Error)
                .SetProperty(p => p.StatusMessage, message), ct);
        }
        return false;
    }

    private static long Count(JsonElement body, string field) =>
        body.TryGetProperty(field, out var f) && f.TryGetProperty("summary", out var s) && s.TryGetProperty("total_count", out var t) && t.TryGetInt64(out var n) ? n : 0;
}

/// <summary>Social analytics dashboards, CSV import and KPIs for client reports.</summary>
[ApiController]
[Route("api/v1/agency/social")]
public sealed class SocialAnalyticsController(
    AppDbContext db, SocialAccess access, SocialAnalyticsService analytics, SocialMetricImporter importer, ICurrentUser currentUser, TimeProvider clock)
    : ControllerBase
{
    [HttpGet("clients/{clientId:guid}/analytics")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<object> Analytics(Guid clientId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? profileId, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var (f, t) = DateRange(from, to);
        return new
        {
            kpis = await analytics.KpisAsync(clientId, f, t, ct),
            series = await analytics.SeriesAsync(clientId, f, t, profileId, ct),
            topPosts = await analytics.TopPostsAsync(clientId, f, t, 10, ct),
        };
    }

    /// <summary>
    /// Social KPIs of a client for reports (the Projects report builder can call this; see docs/api/social-ads.md).
    /// Allowed with reports.manage or social.manage.
    /// </summary>
    [HttpGet("clients/{clientId:guid}/kpis")]
    [RequireAnyPermission(Permissions.ReportsManage, Permissions.SocialManage)]
    public async Task<SocialKpisDto> Kpis(Guid clientId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (!currentUser.HasPermission(Permissions.ReportsManage) && !currentUser.HasPermission(Permissions.SocialManage))
            throw DomainException.Forbidden("auth.forbidden", "You do not have permission to perform this action.");
        await access.ClientAsync(clientId, ct);
        var (f, t) = DateRange(from, to);
        return await analytics.KpisAsync(clientId, f, t, ct);
    }

    [HttpGet("clients/{clientId:guid}/best-times")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<BestTimesDto> BestTimes(Guid clientId, CancellationToken ct) =>
        await analytics.BestTimesAsync(await access.ClientAsync(clientId, ct), ct);

    [HttpPost("profiles/{profileId:guid}/metrics/import/preview")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<SocialImportPreviewDto> ImportPreview(Guid profileId, SocialImportInput input, CancellationToken ct)
    {
        await access.ProfileAsync(profileId, ct);
        return importer.Preview(input);
    }

    [HttpPost("profiles/{profileId:guid}/metrics/import")]
    [HasPermission(Permissions.SocialManage)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<SocialImportResultDto> Import(Guid profileId, SocialImportInput input, CancellationToken ct)
    {
        var profile = await access.ProfileAsync(profileId, ct);
        return await importer.ImportAsync(profile, input, ct);
    }

    [HttpGet("clients/{clientId:guid}/metric-imports")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IReadOnlyList<SocialMetricImport>> Imports(Guid clientId, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        return await db.Set<SocialMetricImport>().AsNoTracking().Where(i => i.ClientAccountId == clientId).OrderByDescending(i => i.CreatedAt).Take(50).ToListAsync(ct);
    }

    private (DateOnly From, DateOnly To) DateRange(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var t = to ?? today;
        var f = from ?? t.AddDays(-29);
        if (f > t) throw new DomainException("social.invalid_range", "'from' must not be after 'to'.");
        if (t.DayNumber - f.DayNumber > 400) throw new DomainException("social.range_too_long", "Choose at most 400 days.");
        return (f, t);
    }
}
