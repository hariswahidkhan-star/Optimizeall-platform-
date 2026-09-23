using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.EmailMarketing.Campaigns;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Reporting;

public sealed record MoneyTotal(string Currency, decimal Amount);

public sealed record LinkStat(Guid LinkId, string Url, int Position, int UniqueClicks, int TotalClicks);

public sealed record SplitStat(string Key, int Opens, int Clicks);

public sealed record VariantReport(string Key, string? Subject, int Sent, int UniqueOpens, int UniqueClicks, double OpenRate, double ClickRate, bool Winner);

public sealed record TimelinePoint(DateTime Hour, int Opens, int Clicks);

public sealed record CampaignReport(
    Guid Id, Guid? ClientAccountId, string Name, MessageChannel Channel, CampaignType Type, CampaignStatus Status, string? Subject,
    DateTime? SendStartedAt, DateTime? CompletedAt,
    int Recipients, int Sent, int Pending, int Failed, int Skipped, int Cancelled, int Delivered, bool DeliveredIsEstimated, int HardBounces, int SoftBounces,
    int UniqueOpens, int TotalOpens, int MachineOpens, int MachineOnlyOpeners, int UniqueClicks, int TotalClicks, int Unsubscribes, int Complaints,
    int Conversions, IReadOnlyList<MoneyTotal> Revenue,
    double OpenRate, double ClickRate, double ClickToOpenRate, double BounceRate, double UnsubscribeRate, double ComplaintRate,
    int SmsSegments, decimal Cost, string? CostCurrency,
    IReadOnlyList<LinkStat> Links, IReadOnlyList<SplitStat> Devices, IReadOnlyList<SplitStat> MailClients, IReadOnlyList<VariantReport> Variants,
    IReadOnlyList<TimelinePoint> Timeline);

public sealed record GrowthPoint(DateOnly Day, int Subscribed, int Unsubscribed);

public sealed record ListHealth(Guid ListId, string Name, int Subscribed, int Pending, int Unsubscribed, int Bounced, int Complained, int Cleaned,
    int Active, int Warm, int Cold, int New, int EmailConsentGranted, IReadOnlyList<GrowthPoint> Growth, int NetGrowth);

public sealed record EmailKpis(Guid? ClientAccountId, DateTime From, DateTime To, int CampaignsSent, int EmailsSent, int Delivered, int UniqueOpens,
    int UniqueClicks, double OpenRate, double ClickRate, int Unsubscribes, int Complaints, int HardBounces, int Conversions, IReadOnlyList<MoneyTotal> Revenue,
    int SmsSent, decimal SmsCost, int NewSubscribers, int ActiveSubscribers, int AutomationEmailsSent);

public sealed record OverviewDto(int Contacts, int Subscribed, int Lists, int ActiveAutomations, int ScheduledCampaigns, EmailKpis Last30Days,
    IReadOnlyList<CampaignListItem> RecentCampaigns);

/// <summary>Campaign reports, list health, workspace KPIs. Rates are over delivered messages; machine opens never count as opens.</summary>
public sealed class ReportService(AppDbContext db, EmailAccess access, CampaignService campaigns, EmailSettingsStore settings, TimeProvider clock)
{
    public async Task<CampaignReport> CampaignReportAsync(Guid campaignId, IReadOnlyCollection<MessageChannel> channels, CancellationToken ct)
    {
        var c = await campaigns.LoadForChannelAsync(campaignId, channels, ct);
        return await BuildAsync(c, ct);
    }

    /// <summary>Report for a client-portal user (read-only; tenant checked by the caller).</summary>
    public Task<CampaignReport> BuildAsync(EmailCampaign c, CancellationToken ct) => BuildReportAsync(c, ct);

    private async Task<CampaignReport> BuildReportAsync(EmailCampaign c, CancellationToken ct)
    {
        var id = c.Id;
        var r = db.Set<CampaignRecipient>().AsNoTracking().Where(x => x.CampaignId == id);
        var agg = await r.GroupBy(x => 1).Select(g => new
        {
            Total = g.Count(),
            Sent = g.Count(x => x.Status == RecipientStatus.Sent),
            Pending = g.Count(x => x.Status == RecipientStatus.Pending || x.Status == RecipientStatus.Held || x.Status == RecipientStatus.Sending),
            Failed = g.Count(x => x.Status == RecipientStatus.Failed),
            Skipped = g.Count(x => x.Status == RecipientStatus.Skipped),
            Cancelled = g.Count(x => x.Status == RecipientStatus.Cancelled),
            DeliveredReported = g.Count(x => x.DeliveredAt != null),
            Hard = g.Count(x => x.BounceType == BounceType.Hard),
            Soft = g.Count(x => x.BounceType == BounceType.Soft),
            UniqueOpens = g.Count(x => x.OpenedAt != null),
            TotalOpens = g.Sum(x => x.OpenCount),
            MachineOpens = g.Sum(x => x.MachineOpenCount),
            MachineOnly = g.Count(x => x.MachineOpenCount > 0 && x.OpenedAt == null),
            UniqueClicks = g.Count(x => x.ClickedAt != null),
            TotalClicks = g.Sum(x => x.ClickCount),
            Unsubscribes = g.Count(x => x.UnsubscribedAt != null),
            Complaints = g.Count(x => x.ComplainedAt != null),
            Conversions = g.Count(x => x.ConvertedAt != null),
            Segments = g.Sum(x => x.Segments),
        }).FirstOrDefaultAsync(ct);
        // Decimal aggregation happens in memory (SQLite cannot aggregate decimals).
        var cost = c.Channel == MessageChannel.Email ? 0m : (await r.Select(x => x.Cost).ToListAsync(ct)).Sum();

        var sent = agg?.Sent ?? 0;
        var deliveredReported = agg?.DeliveredReported ?? 0;
        // SMTP relays do not report deliveries: estimate as sent minus bounces (flagged in the report).
        var estimated = deliveredReported == 0 && sent > 0;
        var delivered = estimated ? Math.Max(0, sent - (agg?.Hard ?? 0) - (agg?.Soft ?? 0)) : deliveredReported;
        double Rate(int n) => delivered == 0 ? 0 : Math.Round(n / (double)delivered, 4);

        var revenue = Totals(await db.Set<EngagementEvent>().AsNoTracking().Where(e => e.CampaignId == id && e.Type == EngagementType.Conversion && e.Currency != null)
            .Select(e => new { e.Currency, e.Value }).ToListAsync(ct), x => x.Currency!, x => x.Value ?? 0);
        var sourceKey = CampaignSendJob.SourceKey(id);
        var linkClicks = await db.Set<EngagementEvent>().AsNoTracking().Where(e => e.CampaignId == id && e.Type == EngagementType.Click && !e.IsMachine && e.LinkId != null)
            .GroupBy(e => e.LinkId!.Value).Select(g => new { LinkId = g.Key, Total = g.Count(), Unique = g.Select(e => e.SubscriberId).Distinct().Count() }).ToListAsync(ct);
        var links = (await db.Set<TrackedLink>().AsNoTracking().Where(l => l.SourceKey == sourceKey).OrderBy(l => l.Position).ToListAsync(ct))
            .Select(l => { var s = linkClicks.FirstOrDefault(x => x.LinkId == l.Id); return new LinkStat(l.Id, l.Url, l.Position, s?.Unique ?? 0, s?.Total ?? 0); })
            .OrderByDescending(l => l.TotalClicks).ThenBy(l => l.Position).ToList();

        var human = db.Set<EngagementEvent>().AsNoTracking().Where(e => e.CampaignId == id && !e.IsMachine && (e.Type == EngagementType.Open || e.Type == EngagementType.Click));
        var devices = await human.GroupBy(e => new { e.Device, e.Type }).Select(g => new { g.Key.Device, g.Key.Type, Count = g.Count() }).ToListAsync(ct);
        var clients = await human.GroupBy(e => new { e.MailClient, e.Type }).Select(g => new { g.Key.MailClient, g.Key.Type, Count = g.Count() }).ToListAsync(ct);

        var variants = new List<VariantReport>();
        if (c.Type == CampaignType.AbTest)
        {
            var defs = await db.Set<CampaignVariant>().AsNoTracking().Where(v => v.CampaignId == id).OrderBy(v => v.Key).ToListAsync(ct);
            var vstats = await r.Where(x => x.Variant != null).GroupBy(x => x.Variant!).Select(g => new
            {
                Key = g.Key,
                Sent = g.Count(x => x.Status == RecipientStatus.Sent),
                Opens = g.Count(x => x.OpenedAt != null),
                Clicks = g.Count(x => x.ClickedAt != null),
            }).ToListAsync(ct);
            foreach (var v in defs)
            {
                var s = vstats.FirstOrDefault(x => x.Key == v.Key);
                var vs = s?.Sent ?? 0;
                variants.Add(new VariantReport(v.Key, v.Subject ?? c.Subject, vs, s?.Opens ?? 0, s?.Clicks ?? 0,
                    vs == 0 ? 0 : Math.Round((s?.Opens ?? 0) / (double)vs, 4), vs == 0 ? 0 : Math.Round((s?.Clicks ?? 0) / (double)vs, 4), c.AbWinnerVariant == v.Key));
            }
        }

        var timeline = new List<TimelinePoint>();
        if (c.SendStartedAt is { } start)
        {
            var end = start.AddHours(48);
            var events = await human.Where(e => e.OccurredAt >= start && e.OccurredAt < end).Select(e => new { e.OccurredAt, e.Type }).ToListAsync(ct);
            timeline = events.GroupBy(e => new DateTime(e.OccurredAt.Year, e.OccurredAt.Month, e.OccurredAt.Day, e.OccurredAt.Hour, 0, 0, DateTimeKind.Utc))
                .OrderBy(g => g.Key).Select(g => new TimelinePoint(g.Key, g.Count(e => e.Type == EngagementType.Open), g.Count(e => e.Type == EngagementType.Click))).ToList();
        }

        var uniqueOpens = agg?.UniqueOpens ?? 0;
        var uniqueClicks = agg?.UniqueClicks ?? 0;
        var ws = c.Channel == MessageChannel.Email ? null : await settings.GetAsync(c.ClientAccountId, ct);
        return new CampaignReport(c.Id, c.ClientAccountId, c.Name, c.Channel, c.Type, c.Status, c.Channel == MessageChannel.Email ? c.Subject : null,
            c.SendStartedAt, c.CompletedAt, agg?.Total ?? 0, sent, agg?.Pending ?? 0, agg?.Failed ?? 0, agg?.Skipped ?? 0, agg?.Cancelled ?? 0, delivered, estimated,
            agg?.Hard ?? 0, agg?.Soft ?? 0, uniqueOpens, agg?.TotalOpens ?? 0, agg?.MachineOpens ?? 0, agg?.MachineOnly ?? 0, uniqueClicks, agg?.TotalClicks ?? 0,
            agg?.Unsubscribes ?? 0, agg?.Complaints ?? 0, agg?.Conversions ?? 0, revenue,
            Rate(uniqueOpens), Rate(uniqueClicks), uniqueOpens == 0 ? 0 : Math.Round(uniqueClicks / (double)uniqueOpens, 4),
            sent == 0 ? 0 : Math.Round(((agg?.Hard ?? 0) + (agg?.Soft ?? 0)) / (double)sent, 4), Rate(agg?.Unsubscribes ?? 0), Rate(agg?.Complaints ?? 0),
            agg?.Segments ?? 0, Math.Round(cost, 4), ws?.CostCurrency,
            links,
            devices.GroupBy(d => d.Device.ToString()).Select(g => new SplitStat(g.Key, g.Where(x => x.Type == EngagementType.Open).Sum(x => x.Count),
                g.Where(x => x.Type == EngagementType.Click).Sum(x => x.Count))).OrderByDescending(s => s.Opens + s.Clicks).ToList(),
            clients.GroupBy(d => d.MailClient ?? "Unknown").Select(g => new SplitStat(g.Key, g.Where(x => x.Type == EngagementType.Open).Sum(x => x.Count),
                g.Where(x => x.Type == EngagementType.Click).Sum(x => x.Count))).OrderByDescending(s => s.Opens + s.Clicks).ToList(),
            variants, timeline);
    }

    public async Task<ListHealth> ListHealthAsync(Guid listId, int days, CancellationToken ct)
    {
        var list = await access.LoadAsync(db.Set<EmailList>().AsNoTracking().Where(l => l.Id == listId), l => l.ClientAccountId, "List", ct);
        var now = clock.GetUtcNow().UtcDateTime;
        days = Math.Clamp(days, 7, 365);
        var since = now.Date.AddDays(-days + 1);
        var members = db.Set<ListMembership>().AsNoTracking().Where(m => m.ListId == listId);
        var byStatus = await members.GroupBy(m => m.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var subs = from m in members where m.Status == MembershipStatus.Subscribed join s in db.Set<Subscriber>() on m.SubscriberId equals s.Id select s;
        var statusCounts = await (from m in members join s in db.Set<Subscriber>() on m.SubscriberId equals s.Id group s by s.Status into g select new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var active30 = now.AddDays(-30);
        var warm90 = now.AddDays(-90);
        var active = await subs.CountAsync(s => s.LastOpenAt >= active30 || s.LastClickAt >= active30, ct);
        var warm = await subs.CountAsync(s => !(s.LastOpenAt >= active30 || s.LastClickAt >= active30) && (s.LastOpenAt >= warm90 || s.LastClickAt >= warm90), ct);
        var fresh = await subs.CountAsync(s => !(s.LastOpenAt >= warm90 || s.LastClickAt >= warm90) && s.CreatedAt >= active30, ct);
        var total = await subs.CountAsync(ct);
        var consent = await subs.CountAsync(s => s.EmailConsent == ConsentStatus.Granted, ct);
        var joined = await members.Where(m => m.SubscribedAt >= since).Select(m => m.SubscribedAt!.Value).ToListAsync(ct);
        var left = await members.Where(m => m.UnsubscribedAt >= since).Select(m => m.UnsubscribedAt!.Value).ToListAsync(ct);
        var growth = Enumerable.Range(0, days).Select(i => DateOnly.FromDateTime(since.AddDays(i)))
            .Select(d => new GrowthPoint(d, joined.Count(x => DateOnly.FromDateTime(x) == d), left.Count(x => DateOnly.FromDateTime(x) == d))).ToList();
        int S(MembershipStatus st) => byStatus.Where(x => x.Key == st).Sum(x => x.Count);
        int T(SubscriberStatus st) => statusCounts.Where(x => x.Key == st).Sum(x => x.Count);
        return new ListHealth(list.Id, list.Name, S(MembershipStatus.Subscribed), S(MembershipStatus.Pending), S(MembershipStatus.Unsubscribed),
            T(SubscriberStatus.Bounced), T(SubscriberStatus.Complained), T(SubscriberStatus.Cleaned), active, warm, Math.Max(0, total - active - warm - fresh), fresh,
            consent, growth, joined.Count - left.Count);
    }

    public async Task<EmailKpis> KpisAsync(Guid? clientId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(clientId, ct);
        return await ComputeKpisAsync(clientId, from, to, ct);
    }

    public async Task<EmailKpis> ComputeKpisAsync(Guid? clientId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var end = to?.ToUniversalTime() ?? now;
        var start = from?.ToUniversalTime() ?? end.AddDays(-30);
        if (start > end) throw new DomainException("range.invalid", "'from' must be before or equal to 'to'.");
        if ((end - start).TotalDays > 366) throw new DomainException("range.too_long", "The date range may span at most 366 days.");
        var key = Workspace.Key(clientId);
        var recipients = from r in db.Set<CampaignRecipient>().AsNoTracking()
                         join c in db.Set<EmailCampaign>() on r.CampaignId equals c.Id
                         where c.ScopeKey == key && r.SentAt >= start && r.SentAt <= end
                         select r;
        var email = await recipients.Where(r => r.Channel == MessageChannel.Email).GroupBy(r => 1).Select(g => new
        {
            Sent = g.Count(),
            Delivered = g.Count(r => r.DeliveredAt != null),
            Hard = g.Count(r => r.BounceType == BounceType.Hard),
            Soft = g.Count(r => r.BounceType == BounceType.Soft),
            Opens = g.Count(r => r.OpenedAt != null),
            Clicks = g.Count(r => r.ClickedAt != null),
            Unsubs = g.Count(r => r.UnsubscribedAt != null),
            Complaints = g.Count(r => r.ComplainedAt != null),
        }).FirstOrDefaultAsync(ct);
        var smsCosts = await recipients.Where(r => r.Channel != MessageChannel.Email).Select(r => r.Cost).ToListAsync(ct);
        var sent = email?.Sent ?? 0;
        var delivered = (email?.Delivered ?? 0) == 0 ? Math.Max(0, sent - (email?.Hard ?? 0) - (email?.Soft ?? 0)) : email!.Delivered;
        var campaignsSent = await db.Set<EmailCampaign>().AsNoTracking().CountAsync(c => c.ScopeKey == key && c.Status == CampaignStatus.Sent && c.CompletedAt >= start && c.CompletedAt <= end, ct);
        var conversions = db.Set<EngagementEvent>().AsNoTracking().Where(e => e.Type == EngagementType.Conversion && e.OccurredAt >= start && e.OccurredAt <= end &&
                                                                              (e.CampaignId != null || e.AutomationId != null) &&
                                                                              (clientId == null ? e.ClientAccountId == null : e.ClientAccountId == clientId));
        var conversionCount = await conversions.CountAsync(ct);
        var revenue = Totals(await conversions.Where(e => e.Currency != null).Select(e => new { e.Currency, e.Value }).ToListAsync(ct), x => x.Currency!, x => x.Value ?? 0);
        var newSubs = await (from m in db.Set<ListMembership>().AsNoTracking()
                             join l in db.Set<EmailList>() on m.ListId equals l.Id
                             where l.ScopeKey == key && m.SubscribedAt >= start && m.SubscribedAt <= end
                             select m.SubscriberId).Distinct().CountAsync(ct);
        var activeSubs = await db.Set<Subscriber>().AsNoTracking().CountAsync(s => s.ScopeKey == key && s.Status == SubscriberStatus.Subscribed && s.EmailConsent == ConsentStatus.Granted, ct);
        var automationSent = await (from run in db.Set<AutomationStepRun>().AsNoTracking()
                                    join a in db.Set<Automation>() on run.AutomationId equals a.Id
                                    where a.ScopeKey == key && run.SentAt >= start && run.SentAt <= end
                                    select run.Id).CountAsync(ct);
        return new EmailKpis(clientId, start, end, campaignsSent, sent, delivered, email?.Opens ?? 0, email?.Clicks ?? 0,
            delivered == 0 ? 0 : Math.Round((email?.Opens ?? 0) / (double)delivered, 4), delivered == 0 ? 0 : Math.Round((email?.Clicks ?? 0) / (double)delivered, 4),
            email?.Unsubs ?? 0, email?.Complaints ?? 0, email?.Hard ?? 0, conversionCount, revenue, smsCosts.Count, Math.Round(smsCosts.Sum(), 2),
            newSubs, activeSubs, automationSent);
    }

    public async Task<OverviewDto> OverviewAsync(Guid? clientId, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(clientId, ct);
        var key = Workspace.Key(clientId);
        var contacts = await db.Set<Subscriber>().AsNoTracking().CountAsync(s => s.ScopeKey == key, ct);
        var subscribed = await db.Set<Subscriber>().AsNoTracking().CountAsync(s => s.ScopeKey == key && s.Status == SubscriberStatus.Subscribed && s.EmailConsent == ConsentStatus.Granted, ct);
        var lists = await db.Set<EmailList>().AsNoTracking().CountAsync(l => l.ScopeKey == key && !l.IsArchived, ct);
        var automations = await db.Set<Automation>().AsNoTracking().CountAsync(a => a.ScopeKey == key && a.Status == AutomationStatus.Active, ct);
        var scheduled = await db.Set<EmailCampaign>().AsNoTracking().CountAsync(c => c.ScopeKey == key && c.Status == CampaignStatus.Scheduled, ct);
        var kpis = await ComputeKpisAsync(clientId, null, null, ct);
        var recent = await db.Set<EmailCampaign>().AsNoTracking().Where(c => c.ScopeKey == key).OrderByDescending(c => c.UpdatedAt).Take(6).ToListAsync(ct);
        return new OverviewDto(contacts, subscribed, lists, automations, scheduled, kpis, await campaigns.ToListItemsAsync(recent, ct));
    }

    private static List<MoneyTotal> Totals<T>(IEnumerable<T> rows, Func<T, string> currency, Func<T, decimal> value) =>
        rows.GroupBy(currency).Select(g => new MoneyTotal(g.Key, Money.Round(g.Sum(value), g.Key))).OrderBy(m => m.Currency).ToList();
}
