using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Ads;

public sealed record TotalsDto(decimal Spend, long Impressions, long Clicks, decimal Conversions, decimal ConversionValue, long Reach, long VideoViews)
{
    public static TotalsDto From(AdTotals t) => new(t.Spend, t.Impressions, t.Clicks, t.Conversions, t.ConversionValue, t.Reach, t.VideoViews);
}

public sealed record OriginalTotalsDto(string Currency, TotalsDto Totals);

public sealed record PlatformKpisDto(AdPlatform Platform, string Currency, TotalsDto Totals, AdKpis Kpis, IReadOnlyList<OriginalTotalsDto> Original, string SourceLabel);

/// <summary>Client-level ads KPIs in the client's reporting currency (originals kept per currency).</summary>
public sealed record ClientAdsKpisDto(
    Guid ClientAccountId, DateOnly From, DateOnly To, string ReportingCurrency, TotalsDto Totals, AdKpis Kpis, decimal? BlendedRoas,
    IReadOnlyList<PlatformKpisDto> Platforms, IReadOnlyList<string> FxMissing, string SourceLabel, string Definitions);

public sealed record DailyPointDto(DateOnly Date, decimal Spend, decimal ConversionValue, decimal Conversions, long Clicks, long Impressions);

/// <summary>A metric row as written by imports and syncs (values in the account currency).</summary>
public sealed record AdMetricRow(
    DateOnly Date, AdLevel Level, string CampaignKey, string CampaignName, string? AdGroupKey, string? AdGroupName, string? AdKey, string? AdName,
    string Currency, decimal Spend, long Impressions, long Clicks, decimal Conversions, decimal ConversionValue, long? Reach, long? VideoViews,
    AdEntityStatus? CampaignStatus = null)
{
    public static AdMetricRow From(ParsedAdRow r, string currency) => new(r.Date, r.Level, r.CampaignKey, r.CampaignName, r.AdGroupKey, r.AdGroupName,
        r.AdKey, r.AdName, r.Currency ?? currency, r.Spend, r.Impressions, r.Clicks, r.Conversions, r.ConversionValue, r.Reach, r.VideoViews);
}

public sealed record UpsertResult(int Inserted, int Updated);

public static class AdsDefinitions
{
    public const string Kpis =
        "CTR = clicks ÷ impressions; CPC = spend ÷ clicks; CPM = spend ÷ impressions × 1,000; CPA = spend ÷ conversions; " +
        "ROAS = conversion value ÷ spend; conversion rate = conversions ÷ clicks; frequency = impressions ÷ reach (reach is summed " +
        "per day, so it is an upper bound). A KPI is empty when its denominator is zero. Money is converted to the client's " +
        "reporting currency at the finance exchange rate effective at the end of each month; original amounts are kept.";
}

/// <summary>
/// Aggregates daily ad metrics and converts money into a reporting currency with <see cref="IExchangeRateProvider"/>.
/// Conversion happens per (currency, month) at the rate effective at the end of that month (or the report end), so the
/// stored original values are never changed. Missing rates are reported and excluded from converted totals.
/// </summary>
public sealed class AdsKpiService(AppDbContext db, IExchangeRateProvider rates)
{
    private readonly Dictionary<(string, string, DateTime), decimal?> _rateCache = new();

    public async Task<decimal?> RateAsync(string from, string to, DateTime atUtc, CancellationToken ct)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return 1m;
        var key = (from, to, atUtc);
        if (_rateCache.TryGetValue(key, out var cached)) return cached;
        decimal? rate;
        try
        {
            rate = (await rates.GetRateAsync(from, to, atUtc, ct)).Rate;
        }
        catch (DomainException ex) when (ex.Code == "fx.rate_missing")
        {
            rate = null;
        }
        _rateCache[key] = rate;
        return rate;
    }

    /// <summary>Converts totals grouped by currency and month into <paramref name="target"/>; returns missing pairs.</summary>
    public async Task<(AdTotals Converted, List<string> Missing)> ConvertAsync(IEnumerable<AdDailyMetric> rows, string target, DateOnly to, CancellationToken ct)
    {
        var total = AdTotals.Zero;
        var missing = new List<string>();
        foreach (var g in rows.GroupBy(r => (r.Currency, r.Date.Year, r.Date.Month)))
        {
            var monthEnd = new DateOnly(g.Key.Year, g.Key.Month, DateTime.DaysInMonth(g.Key.Year, g.Key.Month));
            var at = (monthEnd < to ? monthEnd : to).AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddTicks(-1);
            var rate = await RateAsync(g.Key.Currency, target, at, ct);
            var sum = Sum(g);
            if (rate is null)
            {
                var pair = $"{g.Key.Currency}→{target}";
                if (!missing.Contains(pair)) missing.Add(pair);
                continue;
            }
            total = total.Add(sum.Convert(rate.Value, target));
        }
        return (total, missing);
    }

    public static AdTotals Sum(IEnumerable<AdDailyMetric> rows) => rows.Aggregate(AdTotals.Zero, (t, r) => t.Add(new AdTotals(
        r.Spend, r.Impressions, r.Clicks, r.Conversions, r.ConversionValue, r.Reach ?? 0, r.VideoViews ?? 0)));

    /// <summary>Campaign-level rows (the level used for totals, so ad group/ad rows are not double counted).</summary>
    public IQueryable<AdDailyMetric> CampaignRows(DateOnly from, DateOnly to) =>
        db.Set<AdDailyMetric>().AsNoTracking().Where(m => m.Level == AdLevel.Campaign && m.Date >= from && m.Date <= to);

    public async Task<ClientAdsKpisDto> ClientKpisAsync(Guid clientId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var client = await db.Set<ClientAccount>().AsNoTracking().FirstAsync(c => c.Id == clientId, ct);
        var rows = await CampaignRows(from, to).Where(m => m.ClientAccountId == clientId).ToListAsync(ct);
        return await BuildAsync(client, rows, from, to, ct);
    }

    public Task<ClientAdsKpisDto> ClientSummaryAsync(Guid clientId, DateOnly from, DateOnly to, CancellationToken ct) => ClientKpisAsync(clientId, from, to, ct);

    public async Task<ClientAdsKpisDto> BuildAsync(ClientAccount client, IReadOnlyList<AdDailyMetric> rows, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var currency = client.Currency;
        var platforms = new List<PlatformKpisDto>();
        var missingAll = new List<string>();
        foreach (var g in rows.GroupBy(r => r.Platform).OrderBy(g => g.Key))
        {
            var (converted, missing) = await ConvertAsync(g, currency, to, ct);
            missingAll.AddRange(missing.Where(m => !missingAll.Contains(m)));
            var original = g.GroupBy(r => r.Currency).Select(c => new OriginalTotalsDto(c.Key, TotalsDto.From(Sum(c)))).ToList();
            platforms.Add(new PlatformKpisDto(g.Key, currency, TotalsDto.From(converted), AdKpis.From(converted), original,
                Label(g.Select(r => r.Source))));
        }
        var total = platforms.Aggregate(AdTotals.Zero, (t, p) => t.Add(new AdTotals(p.Totals.Spend, p.Totals.Impressions, p.Totals.Clicks,
            p.Totals.Conversions, p.Totals.ConversionValue, p.Totals.Reach, p.Totals.VideoViews)));
        var kpis = AdKpis.From(total);
        return new ClientAdsKpisDto(client.Id, from, to, currency, TotalsDto.From(total), kpis, kpis.Roas, platforms, missingAll,
            Label(rows.Select(r => r.Source)), AdsDefinitions.Kpis);
    }

    public static string Label(IEnumerable<AdMetricSource> sources)
    {
        var labels = sources.Select(AdMetricSources.Label).Distinct().ToList();
        return labels.Count switch { 0 => "No data", 1 => labels[0], _ => "Mixed (Measured and Manual)" };
    }

    /// <summary>Daily spend of the rows converted into <paramref name="currency"/> (for pacing and sparklines).</summary>
    public async Task<Dictionary<DateOnly, decimal>> DailySpendAsync(IEnumerable<AdDailyMetric> rows, string currency, CancellationToken ct)
    {
        var result = new Dictionary<DateOnly, decimal>();
        foreach (var g in rows.GroupBy(r => (r.Currency, r.Date)))
        {
            var monthEnd = new DateOnly(g.Key.Date.Year, g.Key.Date.Month, DateTime.DaysInMonth(g.Key.Date.Year, g.Key.Date.Month));
            var rate = await RateAsync(g.Key.Currency, currency, monthEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddTicks(-1), ct);
            if (rate is null) continue;
            result[g.Key.Date] = result.GetValueOrDefault(g.Key.Date) + Money.Convert(g.Sum(r => r.Spend), rate.Value, currency);
        }
        return result;
    }
}

/// <summary>
/// Writes daily metrics idempotently: one row per (account, date, level, entity key); a re-import or re-sync updates the
/// row instead of adding one. Creates the campaign / ad group / ad mirror rows it references when missing.
/// </summary>
public sealed class AdMetricWriter(AppDbContext db, IDatabaseDialect dialect, TimeProvider clock)
{
    public async Task<UpsertResult> UpsertAsync(AdAccount account, IReadOnlyList<AdMetricRow> rows, AdMetricSource source, Guid? batchId,
        AdEntitySource entitySource, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        int inserted = 0, updated = 0;

        // Serialize writers of the same ad account (a double-submitted import, an import racing the daily sync): both
        // would read "no row yet" and insert, so one would fail on the unique metric key and campaigns/ad groups would
        // be created twice. The named lock is taken before the transaction, and the mirror rows are read inside it.
        await using var accountLock = await AcquireAccountLockAsync(account.Id, ct);
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        var campaigns = await db.Set<AdCampaign>().Where(c => c.AdAccountId == account.Id).ToListAsync(ct);
        var groups = await db.Set<AdGroup>().Where(g => g.AdAccountId == account.Id).ToListAsync(ct);
        var ads = await db.Set<Ad>().Where(a => a.AdAccountId == account.Id).ToListAsync(ct);
        foreach (var chunk in rows.Chunk(500))
        {
            var minDate = chunk.Min(r => r.Date);
            var maxDate = chunk.Max(r => r.Date);
            var keys = chunk.Select(r => EntityKey(r)).Distinct().ToList();
            var existing = await db.Set<AdDailyMetric>()
                .Where(m => m.AdAccountId == account.Id && m.Date >= minDate && m.Date <= maxDate && keys.Contains(m.EntityKey)).ToListAsync(ct);
            foreach (var r in chunk)
            {
                var campaign = FindOrCreateCampaign(account, campaigns, r, entitySource);
                AdGroup? group = null;
                Ad? ad = null;
                if (r.Level >= AdLevel.AdGroup && r.AdGroupKey is not null) group = FindOrCreateGroup(account, campaign, groups, r.AdGroupKey, r.AdGroupName, entitySource);
                if (r.Level == AdLevel.Ad && r.AdKey is not null && group is not null) ad = FindOrCreateAd(account, campaign, group, ads, r.AdKey, r.AdName, entitySource);

                var key = EntityKey(r);
                var metric = existing.FirstOrDefault(m => m.Date == r.Date && m.Level == r.Level && m.EntityKey == key);
                if (metric is null)
                {
                    metric = new AdDailyMetric
                    {
                        ClientAccountId = account.ClientAccountId, AdAccountId = account.Id, Platform = account.Platform, Level = r.Level,
                        EntityKey = key, Date = r.Date,
                    };
                    db.Set<AdDailyMetric>().Add(metric);
                    existing.Add(metric);
                    inserted++;
                }
                else updated++;
                metric.EntityName = Truncate(r.Level switch { AdLevel.Ad => r.AdName ?? r.AdKey!, AdLevel.AdGroup => r.AdGroupName ?? r.AdGroupKey!, _ => r.CampaignName }, 300);
                metric.CampaignId = campaign.Id;
                metric.AdGroupId = group?.Id;
                metric.AdId = ad?.Id;
                metric.Currency = r.Currency;
                metric.Spend = r.Spend;
                metric.Impressions = r.Impressions;
                metric.Clicks = r.Clicks;
                metric.Conversions = r.Conversions;
                metric.ConversionValue = r.ConversionValue;
                metric.Reach = r.Reach;
                metric.VideoViews = r.VideoViews;
                metric.Source = source;
                metric.ImportBatchId = batchId;
                metric.UpdatedAt = now;
            }
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new UpsertResult(inserted, updated);
    }

    private async Task<IAsyncDisposable> AcquireAccountLockAsync(Guid accountId, CancellationToken ct)
    {
        try
        {
            return await dialect.AcquireNamedLockAsync(db, $"ads:{accountId:N}", TimeSpan.FromSeconds(60), ct);
        }
        catch (TimeoutException)
        {
            throw DomainException.Conflict("ads.write_busy", "Metrics for this ad account are being written right now. Try again in a moment.");
        }
    }

    public static string EntityKey(AdMetricRow r) => Truncate(r.Level switch
    {
        AdLevel.Ad => r.AdKey!,
        AdLevel.AdGroup => r.AdGroupKey!,
        _ => r.CampaignKey,
    }, 200);

    private AdCampaign FindOrCreateCampaign(AdAccount account, List<AdCampaign> campaigns, AdMetricRow r, AdEntitySource source)
    {
        var isName = r.CampaignKey.StartsWith("name:", StringComparison.Ordinal);
        var match = campaigns.FirstOrDefault(c => isName
            ? NamingConvention.Slug(c.Name) == r.CampaignKey[5..]
            : c.ExternalId == r.CampaignKey)
            ?? (isName ? null : campaigns.FirstOrDefault(c => c.ExternalId == null && c.Name == r.CampaignName));
        if (match is not null)
        {
            if (!isName) match.ExternalId ??= r.CampaignKey;
            if (r.CampaignStatus is { } status && match.Source != AdEntitySource.Plan) match.Status = status;
            return match;
        }
        var created = new AdCampaign
        {
            ClientAccountId = account.ClientAccountId, AdAccountId = account.Id, ExternalId = isName ? null : r.CampaignKey,
            Name = Truncate(string.IsNullOrWhiteSpace(r.CampaignName) ? r.CampaignKey : r.CampaignName, 300), Status = r.CampaignStatus ?? AdEntityStatus.Active,
            Currency = account.Currency, Source = source, NamingCompliant = true,
        };
        db.Set<AdCampaign>().Add(created);
        campaigns.Add(created);
        return created;
    }

    private AdGroup FindOrCreateGroup(AdAccount account, AdCampaign campaign, List<AdGroup> groups, string key, string? name, AdEntitySource source)
    {
        var isName = key.StartsWith("name:", StringComparison.Ordinal);
        var match = groups.FirstOrDefault(g => g.CampaignId == campaign.Id && (isName ? NamingConvention.Slug(g.Name) == key[5..] : g.ExternalId == key));
        if (match is not null) return match;
        var created = new AdGroup
        {
            ClientAccountId = account.ClientAccountId, AdAccountId = account.Id, CampaignId = campaign.Id, ExternalId = isName ? null : key,
            Name = Truncate(name ?? key, 300), Status = AdEntityStatus.Active, Source = source,
        };
        db.Set<AdGroup>().Add(created);
        groups.Add(created);
        return created;
    }

    private Ad FindOrCreateAd(AdAccount account, AdCampaign campaign, AdGroup group, List<Ad> ads, string key, string? name, AdEntitySource source)
    {
        var isName = key.StartsWith("name:", StringComparison.Ordinal);
        var match = ads.FirstOrDefault(a => a.AdGroupId == group.Id && (isName ? NamingConvention.Slug(a.Name) == key[5..] : a.ExternalId == key));
        if (match is not null) return match;
        var created = new Ad
        {
            ClientAccountId = account.ClientAccountId, AdAccountId = account.Id, CampaignId = campaign.Id, AdGroupId = group.Id,
            ExternalId = isName ? null : key, Name = Truncate(name ?? key, 300), Status = AdEntityStatus.Active, Source = source,
        };
        db.Set<Ad>().Add(created);
        ads.Add(created);
        return created;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
