using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Partners;

/// <summary>
/// Impressions and clicks of partner placements, per partner, slot, page and UTC day (<see cref="WebsitePartnerStat"/>).
/// Lightweight by design: no cookies, no visitor ids, no IP addresses — only daily counters. Suspected bots
/// (<see cref="TrackingUrl.IsSuspectedBot"/>) are never counted; impressions arrive batched from the web app (at most
/// <see cref="PartnerRules.MaxImpressionBatch"/> per request, duplicates in one batch counted once). Counters are updated
/// under the <c>website-partner-stats</c> named lock inside a write transaction, so concurrent requests never lose an
/// increment or race on the unique (partner, slot, page, day) row. Counting never blocks a visitor: when the lock is busy
/// the event is dropped (and logged), the redirect still happens.
/// </summary>
public sealed class PartnerTrackingService(AppDbContext db, IDatabaseDialect dialect, TimeProvider clock, ILogger<PartnerTrackingService> log)
{
    private const string LockName = "website-partner-stats";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    private sealed record Hit(Guid PartnerId, string Slot, string Path);

    /// <summary>Records a batch of impressions; returns how many were counted.</summary>
    public async Task<int> RecordImpressionsAsync(PartnerImpressionsInput input, string? userAgent, CancellationToken ct)
    {
        if (TrackingUrl.IsSuspectedBot(userAgent)) return 0;
        var items = input.Items.OfType<PartnerImpressionInput>().Take(PartnerRules.MaxImpressionBatch).ToList();
        if (items.Count == 0) return 0;
        var slugs = items.Select(i => i.Partner.Trim()).Distinct().ToList();
        var partners = await db.Set<WebsitePartner>().AsNoTracking().Where(p => p.IsActive && slugs.Contains(p.Slug))
            .Select(p => new { p.Id, p.Slug, p.Slots }).ToListAsync(ct);
        var bySlug = partners.ToDictionary(p => p.Slug, StringComparer.Ordinal);

        var hits = new HashSet<Hit>();
        foreach (var item in items)
        {
            if (!bySlug.TryGetValue(item.Partner.Trim(), out var partner)) continue;
            var slot = PartnerSlots.Find(item.Slot.Trim());
            if (slot is null || (slot.Kind != PartnerSlotKind.Page && !partner.Slots.Contains(slot.Name))) continue;
            if (PartnerRules.NormalizePagePath(item.Path) is not { } path) continue;
            hits.Add(new Hit(partner.Id, slot.Name, path));
        }
        if (hits.Count == 0) return 0;
        return await CountAsync(hits, impressions: true, ct) ? hits.Count : 0;
    }

    /// <summary>
    /// The address a partner link sends the visitor to (the partner's site with its UTM tags; utm_campaign falls back to
    /// the slot), counting the click; null when the partner is unknown, inactive or has no website yet.
    /// </summary>
    public async Task<string?> VisitAsync(string slug, string? slotName, string? path, string? userAgent, CancellationToken ct)
    {
        var partner = await db.Set<WebsitePartner>().AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug && p.IsActive, ct);
        if (partner?.WebsiteUrl is null || !TrackingUrl.IsValidDestination(partner.WebsiteUrl)) return null;
        var slot = PartnerSlots.Find(slotName?.Trim()) ?? PartnerSlots.Find(PartnerSlots.Profile)!;
        var target = TrackingUrl.MergeQuery(partner.WebsiteUrl,
            TrackingUrl.Utm(partner.UtmSource, partner.UtmMedium, partner.UtmCampaign ?? slot.Name));
        if (!TrackingUrl.IsSuspectedBot(userAgent) && PartnerRules.NormalizePagePath(path) is { } page)
            await CountAsync(new[] { new Hit(partner.Id, slot.Name, page) }, impressions: false, ct);
        return target;
    }

    private async Task<bool> CountAsync(IReadOnlyCollection<Hit> hits, bool impressions, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        try
        {
            await using var gate = await dialect.AcquireNamedLockAsync(db, LockName, LockTimeout, ct);
            await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
            var partnerIds = hits.Select(h => h.PartnerId).Distinct().ToList();
            var paths = hits.Select(h => h.Path).Distinct().ToList();
            var rows = await db.Set<WebsitePartnerStat>()
                .Where(s => s.Day == day && partnerIds.Contains(s.PartnerId) && paths.Contains(s.PagePath)).ToListAsync(ct);
            foreach (var hit in hits)
            {
                var row = rows.FirstOrDefault(r => r.PartnerId == hit.PartnerId && r.Slot == hit.Slot && r.PagePath == hit.Path);
                if (row is null)
                {
                    row = new WebsitePartnerStat { PartnerId = hit.PartnerId, Slot = hit.Slot, PagePath = hit.Path, Day = day };
                    db.Add(row);
                    rows.Add(row);
                }
                if (impressions) row.Impressions++;
                else row.Clicks++;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (TimeoutException)
        {
            log.LogWarning("Partner statistics are busy; {Count} {Kind} were not counted.", hits.Count, impressions ? "impressions" : "clicks");
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    // ---------------------------------------------------------------- report (site.manage)

    public async Task<PartnerReportDto> ReportAsync(PartnerReportQuery query, CancellationToken ct)
    {
        var (from, to) = Range(query);
        var q = Filter(query, from, to);
        var names = await db.Set<WebsitePartner>().AsNoTracking().Select(p => new { p.Id, p.Name, p.Slug }).ToDictionaryAsync(p => p.Id, ct);

        var byPartner = await q.GroupBy(s => s.PartnerId)
            .Select(g => new { g.Key, Impressions = g.Sum(s => s.Impressions), Clicks = g.Sum(s => s.Clicks) }).ToListAsync(ct);
        var bySlot = await q.GroupBy(s => s.Slot)
            .Select(g => new { g.Key, Impressions = g.Sum(s => s.Impressions), Clicks = g.Sum(s => s.Clicks) }).ToListAsync(ct);
        var byPage = await q.GroupBy(s => s.PagePath)
            .Select(g => new { g.Key, Impressions = g.Sum(s => s.Impressions), Clicks = g.Sum(s => s.Clicks) })
            .OrderByDescending(x => x.Impressions).ThenByDescending(x => x.Clicks).ThenBy(x => x.Key).Take(50).ToListAsync(ct);
        var daily = await q.GroupBy(s => s.Day)
            .Select(g => new { g.Key, Impressions = g.Sum(s => s.Impressions), Clicks = g.Sum(s => s.Clicks) }).ToListAsync(ct);

        var impressions = byPartner.Sum(x => x.Impressions);
        var clicks = byPartner.Sum(x => x.Clicks);
        return new PartnerReportDto(from, to, impressions, clicks, Ctr(impressions, clicks),
            byPartner.Select(x => new PartnerMetricDto(names.TryGetValue(x.Key, out var n) ? n.Slug : x.Key.ToString(),
                    names.TryGetValue(x.Key, out var m) ? m.Name : "Deleted partner", x.Impressions, x.Clicks, Ctr(x.Impressions, x.Clicks)))
                .OrderByDescending(x => x.Impressions).ThenBy(x => x.Label, StringComparer.Ordinal).ToList(),
            bySlot.Select(x => new PartnerMetricDto(x.Key, PartnerSlots.Find(x.Key)?.Label ?? x.Key, x.Impressions, x.Clicks, Ctr(x.Impressions, x.Clicks)))
                .OrderByDescending(x => x.Impressions).ThenBy(x => x.Key, StringComparer.Ordinal).ToList(),
            byPage.Select(x => new PartnerMetricDto(x.Key, x.Key, x.Impressions, x.Clicks, Ctr(x.Impressions, x.Clicks))).ToList(),
            daily.Select(x => new PartnerDayDto(x.Key, x.Impressions, x.Clicks)).OrderBy(x => x.Day).ToList());
    }

    /// <summary>Daily rows for the CSV export (newest first, at most 50,000).</summary>
    public async Task<IReadOnlyList<(DateOnly Day, string Partner, string PartnerName, string Slot, string Page, int Impressions, int Clicks)>> ExportAsync(
        PartnerReportQuery query, CancellationToken ct)
    {
        var (start, end) = Range(query);
        var rows = await (
                from s in Filter(query, start, end)
                join p in db.Set<WebsitePartner>().AsNoTracking() on s.PartnerId equals p.Id
                orderby s.Day descending, p.Slug, s.Slot, s.PagePath
                select new { s.Day, p.Slug, p.Name, s.Slot, s.PagePath, s.Impressions, s.Clicks })
            .Take(50_000).ToListAsync(ct);
        return rows.Select(r => (r.Day, r.Slug, r.Name, r.Slot, r.PagePath, r.Impressions, r.Clicks)).ToList();
    }

    private IQueryable<WebsitePartnerStat> Filter(PartnerReportQuery query, DateOnly from, DateOnly to)
    {
        var q = db.Set<WebsitePartnerStat>().AsNoTracking().Where(s => s.Day >= from && s.Day <= to);
        if (query.PartnerId is { } partnerId) q = q.Where(s => s.PartnerId == partnerId);
        if (WebsiteRules.Clean(query.Slot) is { } slot) q = q.Where(s => s.Slot == slot);
        return q;
    }

    private (DateOnly From, DateOnly To) Range(PartnerReportQuery query)
    {
        var e = new FieldErrors();
        if (query.From is { Year: < 2000 or > 2100 }) e.Add("from", "Enter a date between 2000 and 2100.");
        if (query.To is { Year: < 2000 or > 2100 }) e.Add("to", "Enter a date between 2000 and 2100.");
        e.ThrowIfAny("website.invalid_range", "Check the dates.");
        var to = query.To ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var from = query.From ?? to.AddDays(-29);
        if (from > to) e.Add("from", "The start date must be on or before the end date.");
        else if (to.DayNumber - from.DayNumber > 366) e.Add("from", "Pick at most one year.");
        e.ThrowIfAny("website.invalid_range", "Check the dates.");
        return (from, to);
    }

    public static decimal? Ctr(int impressions, int clicks) =>
        impressions <= 0 ? null : Math.Round((decimal)clicks / impressions, 4, MidpointRounding.AwayFromZero);
}
