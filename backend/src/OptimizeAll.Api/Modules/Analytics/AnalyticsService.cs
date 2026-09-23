using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Marketing.Shared;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Eligibility;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Analytics;

/// <summary>How a number was obtained. Estimates are never presented as measurements.</summary>
public static class Measurement
{
    /// <summary>Exact count of records in our own database (registrations, submissions, ledger entries).</summary>
    public const string Counted = "counted";

    /// <summary>Observed external behaviour recorded by our tracking (redirect clicks, signed conversion postbacks).</summary>
    public const string Measured = "measured";

    /// <summary>Derived from declared or modelled values, not observed (e.g. reach from declared follower counts).</summary>
    public const string Estimated = "estimated";
}

public static class MetricUnits
{
    public const string Count = "count";
    public const string Percent = "percent";
    public const string Money = "money";
}

/// <summary>One metric. Percent values are 0–100 (2 decimals); money values carry <see cref="Currency"/>. Null = undefined (no data).</summary>
public sealed record MetricDto(string Key, string Label, decimal? Value, string Unit, string Measurement, string? Note, string? Currency = null);

public sealed record SectionDto(string Key, string Title, string Measurement, IReadOnlyList<MetricDto> Metrics);

public sealed record TimeseriesPointDto(DateOnly Date, int Registrations, int Submissions, int Approvals, int Clicks);

public sealed record SpendByCampaignDto(Guid CampaignId, string Title, string Currency, decimal Amount);

public sealed record CampaignRowDto(
    Guid CampaignId, string Title, CampaignStatus Status, int Submitted, int Approved, decimal? ApprovalRate,
    IReadOnlyList<MoneyAmount> Spend, IReadOnlyList<MoneyAmount> CostPerApproved, int Clicks, int UniqueClicks,
    int VerifiedConversions, long EstimatedReach);

public sealed record PlatformRowDto(
    SocialPlatform Platform, int Submitted, int Approved, decimal? ApprovalRate, IReadOnlyList<MoneyAmount> Spend,
    IReadOnlyList<MoneyAmount> CostPerApproved, long EstimatedReach);

public sealed record AnalyticsDto(
    DateTime From, DateTime To, Guid? CampaignId, SocialPlatform? Platform,
    SectionDto Funnel, SectionDto Posts, SectionDto Spend, IReadOnlyList<SpendByCampaignDto> SpendByCampaign,
    SectionDto Reach, SectionDto Traffic, SectionDto Conversions,
    IReadOnlyList<TimeseriesPointDto> Timeseries, IReadOnlyList<CampaignRowDto> Campaigns,
    IReadOnlyList<PlatformRowDto>? Platforms, string TimeBasis);

public sealed record AnalyticsFilter(DateRange Range, Guid? CampaignId, SocialPlatform? Platform);

/// <summary>
/// Performance analytics computed with grouped SQL queries. Time basis: registrations by account creation, posts (and
/// the spend/reach attached to them) by submission time, clicks by click time, conversions by occurrence time.
/// </summary>
public sealed class AnalyticsService(AppDbContext db, ISettingsService settings)
{
    public const string ReachLabel = "Estimated reach (declared follower counts, not measured views)";
    private const int AccountBatch = 2000;

    public async Task<AnalyticsDto> BuildAsync(AnalyticsFilter f, bool includePlatforms, CancellationToken ct)
    {
        var (start, end) = (f.Range.From, f.Range.To);
        var cid = f.CampaignId;
        var platform = f.Platform;

        // ---------- Funnel: cohort of participants registered in range ----------
        var cohort = db.Set<User>().AsNoTracking()
            .Where(u => u.CreatedAt >= start && u.CreatedAt <= end && u.Roles.Any(r => r.Role == Role.Participant));
        var registrations = await cohort.CountAsync(ct);
        var verified = await cohort.CountAsync(u => u.EmailVerifiedAt != null, ct);
        var withSocial = await cohort.CountAsync(u => db.Set<SocialAccount>().Any(s =>
            s.UserId == u.Id && (platform == null || s.Platform == platform)), ct);
        var withSubmission = await cohort.CountAsync(u => db.Set<Submission>().Any(s =>
            s.UserId == u.Id && (cid == null || s.CampaignId == cid) && (platform == null || s.Platform == platform)), ct);
        var eligibleAccounts = await CountEligibleAccountsAsync(cohort, platform, end, ct);

        var funnel = new SectionDto("funnel", "Participant funnel", Measurement.Counted, new[]
        {
            Count("registrations", "Registrations", registrations, "Participants who registered in the period (the cohort for this funnel)."),
            Count("emailVerified", "Email verified", verified, "Cohort participants who verified their email."),
            Count("participantsWithSocialAccount", "Added a social account", withSocial, "Cohort participants with at least one social profile."),
            Count("eligibleAccounts", "Eligible social accounts", eligibleAccounts,
                "Cohort social profiles meeting the global eligibility criteria (minimum account age and followers) at the end of the period."),
            Count("participantsWithSubmission", "Submitted a post", withSubmission, "Cohort participants with at least one submission (any time)."),
            Percent("submissionRate", "Submission rate", withSubmission, registrations, "participantsWithSubmission / registrations."),
        });

        // ---------- Posts: submissions made in range ----------
        var posts = db.Set<Submission>().AsNoTracking().Where(s => s.SubmittedAt >= start && s.SubmittedAt <= end &&
            (cid == null || s.CampaignId == cid) && (platform == null || s.Platform == platform));
        var statusCounts = await posts.GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        int S(SubmissionStatus status) => statusCounts.TryGetValue(status, out var c) ? c : 0;
        var submitted = statusCounts.Values.Sum();
        var approved = S(SubmissionStatus.Approved);
        var rejected = S(SubmissionStatus.Rejected);
        var reversed = S(SubmissionStatus.Reversed);
        var postsSection = new SectionDto("posts", "Posts", Measurement.Counted, new[]
        {
            Count("postsSubmitted", "Posts submitted", submitted, "Submissions made in the period."),
            Count("postsApproved", "Approved", approved, "Currently approved."),
            Count("postsRejected", "Rejected", rejected, null),
            Count("postsNeedingCorrection", "Needs correction", S(SubmissionStatus.NeedsCorrection), null),
            Count("postsPending", "Pending review", S(SubmissionStatus.Pending) + S(SubmissionStatus.UnderReview), "Pending or under review."),
            Count("postsReversed", "Reversed", reversed, "Approved earlier, later reversed (e.g. post removed)."),
            Percent("approvalRate", "Approval rate", approved, approved + rejected + reversed,
                "approved / decided, where decided = approved + rejected + reversed."),
        });

        // ---------- Spend: ledger entries attached to those posts ----------
        var spendRows = from e in db.Set<EarningEntry>().AsNoTracking()
                        join s in posts on e.SubmissionId equals (Guid?)s.Id
                        where (e.Type != EarningType.Reversal && e.Status != EarningStatus.Reversed && e.Status != EarningStatus.Declined) ||
                              (e.Type == EarningType.Reversal && e.Status != EarningStatus.Reversed)
                        select new { s.CampaignId, s.Platform, e.SettlementCurrency, e.SettlementAmount };
        var spendByCampaignCurrency = await spendRows.GroupBy(x => new { x.CampaignId, x.SettlementCurrency })
            .Select(g => new { g.Key.CampaignId, Currency = g.Key.SettlementCurrency, Amount = g.Sum(x => x.SettlementAmount) })
            .ToListAsync(ct);
        var spendTotals = spendByCampaignCurrency.GroupBy(x => x.Currency)
            .Select(g => new MoneyAmount(g.Key, Money.Round(g.Sum(x => x.Amount), g.Key))).OrderBy(m => m.Currency).ToList();

        var spendMetrics = new List<MetricDto>();
        foreach (var total in spendTotals)
            spendMetrics.Add(new MetricDto("spend", "Spend on posts", total.Amount, MetricUnits.Money, Measurement.Counted,
                "Earnings recorded for the period's posts (all statuses except reversed/declined), net of clawbacks.", total.Currency));
        if (spendTotals.Count == 0)
            spendMetrics.Add(new MetricDto("spend", "Spend on posts", 0m, MetricUnits.Money, Measurement.Counted, "No earnings recorded for these posts."));
        foreach (var total in spendTotals)
            spendMetrics.Add(new MetricDto("costPerApprovedPost", "Cost per approved post",
                approved == 0 ? null : Money.Round(total.Amount / approved, total.Currency), MetricUnits.Money, Measurement.Counted,
                approved == 0 ? "No approved posts." : "spend / postsApproved.", total.Currency));
        var spendSection = new SectionDto("spend", "Spend", Measurement.Counted, spendMetrics);

        // ---------- Reach (estimated) ----------
        var reachRows = from s in posts.Where(s => s.Status == SubmissionStatus.Approved)
                        join a in db.Set<SocialAccount>() on s.SocialAccountId equals a.Id
                        select new { s.CampaignId, s.Platform, a.FollowerCount };
        var reachByCampaign = await reachRows.GroupBy(x => x.CampaignId)
            .Select(g => new { CampaignId = g.Key, Reach = g.Sum(x => (long)x.FollowerCount) }).ToListAsync(ct);
        var reach = reachByCampaign.Sum(x => x.Reach);
        var reachSection = new SectionDto("reach", "Reach", Measurement.Estimated, new[]
        {
            new MetricDto("estimatedReach", ReachLabel, reach, MetricUnits.Count, Measurement.Estimated,
                "Sum of the declared follower counts of the profiles used for each approved post. Not measured impressions or views."),
        });

        // ---------- Traffic (measured) ----------
        var clicks = from k in db.Set<TrackingClick>().AsNoTracking()
                     join l in db.Set<TrackingLink>() on k.TrackingLinkId equals l.Id
                     where k.ClickedAt >= start && k.ClickedAt <= end && (cid == null || l.CampaignId == cid)
                     select new { l.CampaignId, k.IsSuspectedBot, k.IsUnique, k.ClickedAt };
        var clicksByCampaign = await clicks.GroupBy(x => x.CampaignId).Select(g => new
        {
            CampaignId = g.Key,
            Human = g.Count(x => !x.IsSuspectedBot),
            Unique = g.Count(x => !x.IsSuspectedBot && x.IsUnique),
            Bots = g.Count(x => x.IsSuspectedBot),
        }).ToListAsync(ct);
        var platformNote = platform is null ? null : "Platform filter not applied: clicks are not attributed to a platform.";
        var trafficSection = new SectionDto("traffic", "Traffic", Measurement.Measured, new[]
        {
            new MetricDto("trackedClicks", "Tracked clicks", clicksByCampaign.Sum(x => x.Human), MetricUnits.Count, Measurement.Measured,
                Join("Clicks on participants' tracking links recorded by our redirect; suspected bots excluded.", platformNote)),
            new MetricDto("uniqueClicks", "Unique clicks", clicksByCampaign.Sum(x => x.Unique), MetricUnits.Count, Measurement.Measured,
                Join("First click per visitor (hashed IP + user agent, per day) per link; suspected bots excluded.", platformNote)),
            new MetricDto("botClicksExcluded", "Suspected bot clicks (excluded)", clicksByCampaign.Sum(x => x.Bots), MetricUnits.Count,
                Measurement.Measured, "Crawler/preview/script user agents; recorded but not counted as traffic."),
        });

        // ---------- Conversions (measured, signature-verified only) ----------
        var conversions = from v in db.Set<TrackingConversion>().AsNoTracking()
                          join l in db.Set<TrackingLink>() on v.TrackingLinkId equals l.Id
                          where v.VerifiedAt != null && v.OccurredAt >= start && v.OccurredAt <= end && (cid == null || l.CampaignId == cid)
                          select new { l.CampaignId, v.Value, v.Currency };
        var conversionsByCampaign = await conversions.GroupBy(x => x.CampaignId)
            .Select(g => new { CampaignId = g.Key, Count = g.Count() }).ToListAsync(ct);
        var conversionValue = await conversions.Where(x => x.Value != null && x.Currency != null).GroupBy(x => x.Currency!)
            .Select(g => new { Currency = g.Key, Amount = g.Sum(x => x.Value!.Value) }).ToListAsync(ct);
        var conversionMetrics = new List<MetricDto>
        {
            new("verifiedConversions", "Verified conversions", conversionsByCampaign.Sum(x => x.Count), MetricUnits.Count, Measurement.Measured,
                Join("Conversions reported by the advertiser with a valid HMAC signature.", platformNote)),
        };
        conversionMetrics.AddRange(conversionValue.OrderBy(x => x.Currency).Select(x => new MetricDto("conversionValue", "Conversion value",
            Money.Round(x.Amount, x.Currency), MetricUnits.Money, Measurement.Measured, "Sum of values reported in verified conversions.", x.Currency)));
        var conversionSection = new SectionDto("conversions", "Conversions", Measurement.Measured, conversionMetrics);

        // ---------- Time series (UTC days) ----------
        var regByDay = await cohort.GroupBy(u => u.CreatedAt.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
        var subByDay = await posts.GroupBy(s => s.SubmittedAt.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
        var apprByDay = await db.Set<Submission>().AsNoTracking()
            .Where(s => s.Status == SubmissionStatus.Approved && s.DecidedAt != null && s.DecidedAt >= start && s.DecidedAt <= end &&
                        (cid == null || s.CampaignId == cid) && (platform == null || s.Platform == platform))
            .GroupBy(s => s.DecidedAt!.Value.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
        var clickByDay = await clicks.Where(x => !x.IsSuspectedBot).GroupBy(x => x.ClickedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
        var series = new List<TimeseriesPointDto>();
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            var d = day;
            series.Add(new TimeseriesPointDto(DateOnly.FromDateTime(d),
                regByDay.FirstOrDefault(x => x.Day == d)?.Count ?? 0,
                subByDay.FirstOrDefault(x => x.Day == d)?.Count ?? 0,
                apprByDay.FirstOrDefault(x => x.Day == d)?.Count ?? 0,
                clickByDay.FirstOrDefault(x => x.Day == d)?.Count ?? 0));
        }

        // ---------- Per-campaign table ----------
        var perCampaignPosts = await posts.GroupBy(s => s.CampaignId).Select(g => new
        {
            CampaignId = g.Key,
            Submitted = g.Count(),
            Approved = g.Count(s => s.Status == SubmissionStatus.Approved),
            Decided = g.Count(s => s.Status == SubmissionStatus.Approved || s.Status == SubmissionStatus.Rejected || s.Status == SubmissionStatus.Reversed),
        }).ToListAsync(ct);
        var campaignIds = perCampaignPosts.Select(x => x.CampaignId)
            .Concat(spendByCampaignCurrency.Select(x => x.CampaignId))
            .Concat(clicksByCampaign.Select(x => x.CampaignId))
            .Concat(conversionsByCampaign.Select(x => x.CampaignId))
            .Concat(cid is { } only ? new[] { only } : Array.Empty<Guid>())
            .Distinct().ToList();
        var campaignInfo = await db.Set<Campaign>().AsNoTracking().Where(c => campaignIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Title, c.Status }).ToDictionaryAsync(c => c.Id, ct);

        var rows = campaignIds.Where(campaignInfo.ContainsKey).Select(id =>
        {
            var p = perCampaignPosts.FirstOrDefault(x => x.CampaignId == id);
            var spend = spendByCampaignCurrency.Where(x => x.CampaignId == id)
                .Select(x => new MoneyAmount(x.Currency, Money.Round(x.Amount, x.Currency))).OrderBy(m => m.Currency).ToList();
            var k = clicksByCampaign.FirstOrDefault(x => x.CampaignId == id);
            return new CampaignRowDto(id, campaignInfo[id].Title, campaignInfo[id].Status, p?.Submitted ?? 0, p?.Approved ?? 0,
                PercentValue(p?.Approved ?? 0, p?.Decided ?? 0), spend, CostPer(spend, p?.Approved ?? 0),
                k?.Human ?? 0, k?.Unique ?? 0, conversionsByCampaign.FirstOrDefault(x => x.CampaignId == id)?.Count ?? 0,
                reachByCampaign.FirstOrDefault(x => x.CampaignId == id)?.Reach ?? 0);
        }).OrderByDescending(r => r.Submitted).ThenBy(r => r.Title).ToList();

        var spendByCampaign = spendByCampaignCurrency.Where(x => campaignInfo.ContainsKey(x.CampaignId))
            .Select(x => new SpendByCampaignDto(x.CampaignId, campaignInfo[x.CampaignId].Title, x.Currency, Money.Round(x.Amount, x.Currency)))
            .OrderByDescending(x => x.Amount).ToList();

        // ---------- Per-platform breakdown (campaign detail) ----------
        List<PlatformRowDto>? platforms = null;
        if (includePlatforms)
        {
            var perPlatform = await posts.GroupBy(s => s.Platform).Select(g => new
            {
                Platform = g.Key,
                Submitted = g.Count(),
                Approved = g.Count(s => s.Status == SubmissionStatus.Approved),
                Decided = g.Count(s => s.Status == SubmissionStatus.Approved || s.Status == SubmissionStatus.Rejected || s.Status == SubmissionStatus.Reversed),
            }).ToListAsync(ct);
            var spendByPlatform = await spendRows.GroupBy(x => new { x.Platform, x.SettlementCurrency })
                .Select(g => new { g.Key.Platform, Currency = g.Key.SettlementCurrency, Amount = g.Sum(x => x.SettlementAmount) }).ToListAsync(ct);
            var reachByPlatform = await reachRows.GroupBy(x => x.Platform)
                .Select(g => new { Platform = g.Key, Reach = g.Sum(x => (long)x.FollowerCount) }).ToListAsync(ct);
            platforms = perPlatform.OrderByDescending(x => x.Submitted).Select(x =>
            {
                var spend = spendByPlatform.Where(s => s.Platform == x.Platform)
                    .Select(s => new MoneyAmount(s.Currency, Money.Round(s.Amount, s.Currency))).OrderBy(m => m.Currency).ToList();
                return new PlatformRowDto(x.Platform, x.Submitted, x.Approved, PercentValue(x.Approved, x.Decided), spend,
                    CostPer(spend, x.Approved), reachByPlatform.FirstOrDefault(r => r.Platform == x.Platform)?.Reach ?? 0);
            }).ToList();
        }

        return new AnalyticsDto(start, end, cid, platform, funnel, postsSection, spendSection, spendByCampaign, reachSection,
            trafficSection, conversionSection, series, rows, platforms,
            "Registrations by account creation; posts, spend and reach by submission time; clicks by click time; conversions by occurrence time (UTC).");
    }

    /// <summary>Evaluates the global eligibility criteria in memory over the cohort's active social accounts, in batches.</summary>
    private async Task<int> CountEligibleAccountsAsync(IQueryable<User> cohort, SocialPlatform? platform, DateTime asOf, CancellationToken ct)
    {
        var criteria = EligibilityCriteria.Global(await settings.MinAccountAgeDaysAsync(ct), await settings.MinFollowersAsync(ct));
        var profile = new ParticipantProfile(Guid.Empty, UserStatus.Active, true, string.Empty, "en", ParticipantTier.Standard, Array.Empty<string>());
        var accounts = db.Set<SocialAccount>().AsNoTracking()
            .Where(s => s.IsActive && cohort.Any(u => u.Id == s.UserId) && (platform == null || s.Platform == platform))
            .OrderBy(s => s.Id);
        var eligible = 0;
        for (var skip = 0; ; skip += AccountBatch)
        {
            var batch = await accounts.Skip(skip).Take(AccountBatch).ToListAsync(ct);
            eligible += batch.Count(a => EligibilityEvaluator.EvaluateAccount(criteria, profile, a, asOf).IsEligible);
            if (batch.Count < AccountBatch) return eligible;
        }
    }

    private static MetricDto Count(string key, string label, long value, string? note) =>
        new(key, label, value, MetricUnits.Count, Measurement.Counted, note);

    private static MetricDto Percent(string key, string label, long numerator, long denominator, string note) =>
        new(key, label, PercentValue(numerator, denominator), MetricUnits.Percent, Measurement.Counted,
            denominator == 0 ? note + " No data in the period." : note);

    public static decimal? PercentValue(long numerator, long denominator) =>
        denominator == 0 ? null : Math.Round(numerator * 100m / denominator, 2, MidpointRounding.AwayFromZero);

    private static List<MoneyAmount> CostPer(IEnumerable<MoneyAmount> spend, int approved) =>
        approved == 0 ? new List<MoneyAmount>() : spend.Select(s => new MoneyAmount(s.Currency, Money.Round(s.Amount / approved, s.Currency))).ToList();

    private static string Join(string text, string? extra) => extra is null ? text : text + " " + extra;
}
