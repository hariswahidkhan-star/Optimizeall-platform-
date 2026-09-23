using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Seo.Controllers;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seo;

public sealed record SiteKpiDto(
    Guid SiteId, string Name, string Domain, int? HealthScore, int? HealthScoreChange, DateTime? LastAuditAt, int TrackedKeywords,
    int Top3, int Top10, double? AveragePosition, double? AveragePositionChange, int Clicks, int Impressions, int LiveBacklinks,
    int LostBacklinks, double? ShareOfVoice);

public sealed record LeadPageDto(Guid PageId, string Name, string Slug, int Views, int Submissions, double? ConversionRate);

public sealed record LeadStatsDto(int Views, int Submissions, double? ConversionRate, IReadOnlyList<LeadPageDto> Pages, IReadOnlyList<LeadDayDto> Daily);

public sealed record LeadDayDto(DateOnly Date, int Submissions);

public sealed record ClientSeoKpisDto(Guid ClientAccountId, string ClientName, DateOnly From, DateOnly To, IReadOnlyList<SiteKpiDto> Sites, LeadStatsDto Leads);

/// <summary>Rankings/Search Console aggregation and client-level KPIs (agency reports and the client portal).</summary>
public static class SeoKpiService
{
    public static async Task<RankingsOverviewDto> RankingsAsync(AppDbContext db, SeoSite site, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var own = RankMath.BareHost(site.Domain);
        var keywords = await db.Set<SeoKeyword>().AsNoTracking().Where(k => k.SiteId == site.Id && k.IsTracked)
            .Select(k => new { k.Id, k.Keyword, k.SearchVolume }).ToListAsync(ct);
        var ids = keywords.Select(k => k.Id).ToList();
        var lookback = from.AddDays(-30);
        var snapshots = await db.Set<SeoRankSnapshot>().AsNoTracking()
            .Where(s => s.SiteId == site.Id && ids.Contains(s.KeywordId) && s.Date >= lookback && s.Date <= to)
            .Select(s => new { s.KeywordId, s.Date, s.Domain, s.Position, s.SerpFeatures, s.Source }).ToListAsync(ct);
        var ownSnaps = snapshots.Where(s => s.Domain == own).ToList();

        var latest = ownSnaps.GroupBy(s => s.KeywordId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Date).First());
        var atStart = ownSnaps.Where(s => s.Date <= from).GroupBy(s => s.KeywordId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Date).First());

        var positions = latest.Values.Select(s => s.Position).ToList();
        var distribution = new DistributionDto(
            positions.Count(p => p <= 3), positions.Count(p => p <= 10), positions.Count(p => p <= 20), positions.Count(p => p <= 100),
            keywords.Count - positions.Count(p => p <= 100));
        var avg = Average(positions);
        var avgStart = Average(atStart.Values.Select(s => s.Position));
        var trend = ownSnaps.Where(s => s.Date >= from).GroupBy(s => s.Date).OrderBy(g => g.Key)
            .Select(g => new TrendPointDto(g.Key, Average(g.Select(s => s.Position)), g.Count(s => s.Position <= 10), g.Count(s => s.Position is not null)))
            .ToList();

        var names = keywords.ToDictionary(k => k.Id, k => k.Keyword);
        var (winners, losers) = RankMath.Movers(latest.Where(l => atStart.ContainsKey(l.Key) && atStart[l.Key].Date < l.Value.Date)
            .Select(l => (l.Key, atStart[l.Key].Position, l.Value.Position)));

        var volume = keywords.ToDictionary(k => k.Id, k => k.SearchVolume);
        var sovDate = snapshots.Where(s => s.Domain == own).Select(s => (DateOnly?)s.Date).Max();
        var sov = sovDate is null ? new List<ShareOfVoiceEntry>()
            : RankMath.ShareOfVoice(snapshots.Where(s => s.Date == sovDate).Select(s => (s.Domain, volume.GetValueOrDefault(s.KeywordId), s.Position))).ToList();
        var competitors = site.Competitors.Select(RankMath.BareHost).Append(own).ToHashSet();
        sov = sov.Where(e => competitors.Contains(e.Domain)).ToList();
        if (sov.Count > 0 && sov.All(e => e.Domain != own)) sov.Add(new ShareOfVoiceEntry(own, 0, 0));

        var features = latest.Values.SelectMany(s => s.SerpFeatures).GroupBy(f => f).OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
        var lastSource = ownSnaps.OrderByDescending(s => s.Date).Select(s => (RankSource?)s.Source).FirstOrDefault();

        return new RankingsOverviewDto(from, to, keywords.Count, distribution, avg,
            avg is not null && avgStart is not null ? Math.Round(avgStart.Value - avg.Value, 1) : null, trend,
            winners.Select(m => new MoverDto(m.KeywordId, names[m.KeywordId], m.Previous, m.Current, m.Change)).ToList(),
            losers.Select(m => new MoverDto(m.KeywordId, names[m.KeywordId], m.Previous, m.Current, m.Change)).ToList(),
            sov, features, lastSource?.ToString() ?? "No data yet");
    }

    public static async Task<SearchPerformanceDto> SearchPerformanceAsync(AppDbContext db, Guid siteId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await db.Set<SeoSearchPerformance>().AsNoTracking().Where(p => p.SiteId == siteId && p.Date >= from && p.Date <= to)
            .Select(p => new { p.Date, p.Query, p.Clicks, p.Impressions, p.Position }).ToListAsync(ct);
        var clicks = rows.Sum(r => r.Clicks);
        var impressions = rows.Sum(r => r.Impressions);
        var weighted = impressions == 0 ? (double?)null : Math.Round(rows.Sum(r => r.Position * r.Impressions) / impressions, 1);
        return new SearchPerformanceDto(from, to, clicks, impressions, impressions == 0 ? null : Math.Round((double)clicks / impressions, 4), weighted,
            rows.GroupBy(r => r.Date).OrderBy(g => g.Key).Select(g => new SearchDayDto(g.Key, g.Sum(r => r.Clicks), g.Sum(r => r.Impressions))).ToList(),
            rows.GroupBy(r => r.Query).Select(g =>
                {
                    var imp = g.Sum(r => r.Impressions);
                    return new SearchQueryDto(g.Key, g.Sum(r => r.Clicks), imp, imp == 0 ? 0 : Math.Round((double)g.Sum(r => r.Clicks) / imp, 4),
                        imp == 0 ? 0 : Math.Round(g.Sum(r => r.Position * r.Impressions) / imp, 1));
                })
                .OrderByDescending(q => q.Clicks).ThenByDescending(q => q.Impressions).Take(20).ToList());
    }

    public static async Task<ClientSeoKpisDto> ClientKpisAsync(AppDbContext db, Guid clientId, string clientName, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var sites = await db.Set<SeoSite>().AsNoTracking().Where(s => s.ClientAccountId == clientId && !s.IsArchived).OrderBy(s => s.Name).ToListAsync(ct);
        var result = new List<SiteKpiDto>();
        var fromTime = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toTime = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        foreach (var site in sites)
        {
            var audits = await db.Set<SeoAudit>().AsNoTracking()
                .Where(a => a.SiteId == site.Id && a.Status == SeoAuditStatus.Completed && a.FinishedAt <= toTime)
                .OrderByDescending(a => a.FinishedAt).Select(a => new { a.HealthScore, a.FinishedAt }).Take(20).ToListAsync(ct);
            var latest = audits.FirstOrDefault();
            var baseline = audits.FirstOrDefault(a => a.FinishedAt < fromTime) ?? audits.LastOrDefault();
            var rankings = await RankingsAsync(db, site, from, to, ct);
            var perf = await SearchPerformanceAsync(db, site.Id, from, to, ct);
            var backlinks = await db.Set<SeoBacklink>().AsNoTracking().Where(b => b.SiteId == site.Id)
                .GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var own = RankMath.BareHost(site.Domain);
            result.Add(new SiteKpiDto(site.Id, site.Name, site.Domain, latest?.HealthScore,
                latest?.HealthScore is { } h && baseline?.HealthScore is { } b && !ReferenceEquals(latest, baseline) ? h - b : null,
                latest?.FinishedAt, rankings.TrackedKeywords, rankings.Distribution.Top3, rankings.Distribution.Top10, rankings.AveragePosition,
                rankings.AveragePositionChange, perf.Clicks, perf.Impressions,
                backlinks.Where(x => x.Key is BacklinkStatus.Live or BacklinkStatus.Nofollow).Sum(x => x.Count),
                backlinks.Where(x => x.Key == BacklinkStatus.Lost).Sum(x => x.Count),
                rankings.ShareOfVoice.FirstOrDefault(s => s.Domain == own)?.Share));
        }
        return new ClientSeoKpisDto(clientId, clientName, from, to, result, await LeadsAsync(db, clientId, fromTime, toTime, ct));
    }

    public static async Task<LeadStatsDto> LeadsAsync(AppDbContext db, Guid clientId, DateTime from, DateTime to, CancellationToken ct)
    {
        var pages = await db.Set<LandingPage>().AsNoTracking().Where(p => p.ClientAccountId == clientId && p.Status != LandingPageStatus.Draft)
            .Select(p => new { p.Id, p.Name, p.Slug }).ToListAsync(ct);
        var views = await db.Set<LandingPageView>().AsNoTracking().Where(v => v.ClientAccountId == clientId && v.ViewedAt >= from && v.ViewedAt <= to)
            .GroupBy(v => v.PageId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var submissions = await db.Set<FormSubmission>().AsNoTracking().Where(s => s.ClientAccountId == clientId && s.SubmittedAt >= from && s.SubmittedAt <= to)
            .Select(s => new { s.LandingPageId, s.SubmittedAt }).ToListAsync(ct);
        var byPage = submissions.Where(s => s.LandingPageId != null).GroupBy(s => s.LandingPageId!.Value).ToDictionary(g => g.Key, g => g.Count());
        var pageStats = pages.Select(p =>
        {
            var v = views.GetValueOrDefault(p.Id);
            var s = byPage.GetValueOrDefault(p.Id);
            return new LeadPageDto(p.Id, p.Name, p.Slug, v, s, v == 0 ? null : Math.Round((double)s / v, 4));
        }).OrderByDescending(p => p.Submissions).ThenByDescending(p => p.Views).ToList();
        var totalViews = views.Values.Sum();
        var landingSubmissions = byPage.Values.Sum();
        return new LeadStatsDto(totalViews, submissions.Count, totalViews == 0 ? null : Math.Round((double)landingSubmissions / totalViews, 4), pageStats,
            submissions.GroupBy(s => DateOnly.FromDateTime(s.SubmittedAt)).OrderBy(g => g.Key).Select(g => new LeadDayDto(g.Key, g.Count())).ToList());
    }

    private static double? Average(IEnumerable<int?> positions)
    {
        var ranked = positions.Where(p => p is not null).Select(p => p!.Value).ToList();
        return ranked.Count == 0 ? null : Math.Round(ranked.Average(), 1);
    }
}
