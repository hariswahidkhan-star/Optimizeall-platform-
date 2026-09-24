using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Rewards;

/// <summary>A quote together with the exact rule set version and context it was computed from.</summary>
public sealed record PricedReward(RewardRuleSet RuleSet, RewardContext Context, RewardQuote Quote, SubmissionRate? Rate = null);

/// <summary>
/// Builds <see cref="RewardContext"/>s from the database and prices posts with <see cref="RewardEngine"/>.
/// Aggregates come from the participant's non-reversed, non-declined earnings in the campaign; "today"/"this week"
/// are the campaign-local day/week (Monday start) of the submission's <b>SubmittedAt</b> (server time, not the
/// participant-declared PostedAt). Rate and bonus windows use <see cref="SubmissionTiming.RewardWindowTime"/>.
/// </summary>
public interface IRewardQuoteService
{
    /// <summary>The version in force at <paramref name="atUtc"/> (highest version with EffectiveFrom ≤ at), with rules.</summary>
    Task<RewardRuleSet?> GetCurrentRuleSetAsync(Guid campaignId, DateTime atUtc, CancellationToken ct = default);

    Task<RewardRuleSet> LoadRuleSetAsync(Guid ruleSetId, CancellationToken ct = default);

    /// <summary>
    /// Context for a post by <paramref name="userId"/>. <paramref name="excludeSubmissionId"/> is the submission being
    /// priced (never counted as a prior approval). Windows use min(PostedAt, SubmittedAt); caps use SubmittedAt's day/week.
    /// </summary>
    Task<RewardContext> BuildContextAsync(Campaign campaign, string ruleSetCurrency, Guid userId, SocialPlatform platform,
        DateTime postedAtUtc, DateTime submittedAtUtc, decimal? qualityBonusRequested, Guid? excludeSubmissionId,
        CancellationToken ct = default);

    /// <summary>
    /// Idempotency key for the participant's next first-post bonus in the campaign: <c>firstpost:{campaignId}:{userId}</c>,
    /// or <c>firstpost:{campaignId}:{userId}:{n}</c> once <c>n</c> earlier first-post bonuses have been reversed.
    /// Must be called under the campaign lock.
    /// </summary>
    Task<string> FirstPostKeyAsync(Guid campaignId, Guid userId, CancellationToken ct = default);

    /// <summary>Prices a submission from its <b>recorded</b> rule set version.</summary>
    Task<PricedReward> QuoteSubmissionAsync(Submission submission, decimal? qualityBonusRequested, CancellationToken ct = default);

    /// <summary>Campaign budget minus all non-reversed, non-declined campaign earnings; null when no budget is set.</summary>
    Task<decimal?> BudgetRemainingAsync(Campaign campaign, CancellationToken ct = default);
}

public sealed class RewardQuoteService(AppDbContext db) : IRewardQuoteService
{
    /// <summary>
    /// Earnings that count as spent / earned: every status except Reversed and Declined, written as an explicit
    /// <c>Status IN ('PendingApproval','Approved','Scheduled','Paid')</c> so it can range-scan (CampaignId, Status).
    /// </summary>
    public static readonly Expression<Func<EarningEntry, bool>> IsCounted = e =>
        e.Status == EarningStatus.PendingApproval || e.Status == EarningStatus.Approved ||
        e.Status == EarningStatus.Scheduled || e.Status == EarningStatus.Paid;

    /// <summary>
    /// Key of the first-post bonus after <paramref name="reversedCount"/> earlier ones were reversed (n = 0 keeps the
    /// original key format).
    /// </summary>
    public static string FirstPostKey(Guid campaignId, Guid userId, int reversedCount = 0) =>
        reversedCount == 0 ? $"firstpost:{campaignId}:{userId}" : $"firstpost:{campaignId}:{userId}:{reversedCount}";

    /// <summary>
    /// Campaign earnings that count against the budget. Filters on (CampaignId, Status IN ...) so MySQL can range-scan
    /// the (CampaignId, Status) index instead of the whole table.
    /// </summary>
    public static IQueryable<EarningEntry> SpentQuery(AppDbContext db, Guid campaignId) =>
        db.Set<EarningEntry>().Where(e => e.CampaignId == campaignId).Where(IsCounted);

    /// <summary>First-post bonus entries of the participant in the campaign.</summary>
    private IQueryable<EarningEntry> FirstPostEntries(Guid campaignId, Guid userId) =>
        db.Set<EarningEntry>().Where(e => e.UserId == userId && e.CampaignId == campaignId && e.Type == EarningType.FirstPostBonus);

    public async Task<string> FirstPostKeyAsync(Guid campaignId, Guid userId, CancellationToken ct = default)
    {
        var reversed = await FirstPostEntries(campaignId, userId)
            .CountAsync(e => e.Status == EarningStatus.Reversed || e.ReversedByEntryId != null, ct);
        return FirstPostKey(campaignId, userId, reversed);
    }

    public async Task<RewardRuleSet?> GetCurrentRuleSetAsync(Guid campaignId, DateTime atUtc, CancellationToken ct = default) =>
        await db.Set<RewardRuleSet>().AsNoTracking().Include(r => r.Rules)
            .Where(r => r.CampaignId == campaignId && r.EffectiveFrom <= atUtc)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);

    public async Task<RewardRuleSet> LoadRuleSetAsync(Guid ruleSetId, CancellationToken ct = default) =>
        await db.Set<RewardRuleSet>().AsNoTracking().Include(r => r.Rules).FirstOrDefaultAsync(r => r.Id == ruleSetId, ct)
        ?? throw DomainException.NotFound("RewardRuleSet");

    public async Task<RewardContext> BuildContextAsync(Campaign campaign, string ruleSetCurrency, Guid userId, SocialPlatform platform,
        DateTime postedAtUtc, DateTime submittedAtUtc, decimal? qualityBonusRequested, Guid? excludeSubmissionId,
        CancellationToken ct = default)
    {
        var participant = await db.Set<User>().AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.CountryCode, u.Tier }).FirstOrDefaultAsync(ct)
            ?? throw DomainException.NotFound("User");

        var rows = await (
                from e in db.Set<EarningEntry>().AsNoTracking().Where(IsCounted)
                where e.UserId == userId && e.CampaignId == campaign.Id
                join s in db.Set<Submission>() on e.SubmissionId equals (Guid?)s.Id into sj
                from s in sj.DefaultIfEmpty()
                select new { e.Amount, SubmittedAt = (DateTime?)s.SubmittedAt, e.CreatedAt })
            .ToListAsync(ct);

        // Caps count against the campaign-local day/week of the SUBMISSION time (server-recorded), never the
        // participant-declared post time.
        var capTime = SubmissionTiming.CapTime(submittedAtUtc);
        var zone = CampaignClock.Zone(campaign.TimeZone);
        var (dayStart, dayEnd) = CampaignClock.LocalDay(zone, capTime);
        var (weekStart, weekEnd) = CampaignClock.LocalWeek(zone, capTime);
        decimal InRange(DateTime from, DateTime to) =>
            rows.Where(r => (r.SubmittedAt ?? r.CreatedAt) >= from && (r.SubmittedAt ?? r.CreatedAt) < to).Sum(r => r.Amount);

        // A first-post bonus that was reversed no longer counts as taken (a later approval may earn it again, under a
        // new idempotency key; see FirstPostKeyAsync). Declined ones stay taken.
        var firstPostTaken = await FirstPostEntries(campaign.Id, userId)
            .AnyAsync(e => e.Status != EarningStatus.Reversed && e.ReversedByEntryId == null, ct);
        var hasOtherApproved = await db.Set<Submission>().AnyAsync(s =>
            s.UserId == userId && s.CampaignId == campaign.Id && s.Status == SubmissionStatus.Approved &&
            (excludeSubmissionId == null || s.Id != excludeSubmissionId), ct);

        decimal? budget = null;
        if (campaign.BudgetAmount.HasValue &&
            string.Equals(campaign.BudgetCurrency, ruleSetCurrency, StringComparison.OrdinalIgnoreCase))
            budget = await BudgetRemainingAsync(campaign, ct);

        return new RewardContext
        {
            Platform = platform,
            CountryCode = participant.CountryCode,
            Tier = participant.Tier,
            PostedAtUtc = SubmissionTiming.RewardWindowTime(postedAtUtc, submittedAtUtc),
            IsFirstApprovedPostInCampaign = !firstPostTaken && !hasOtherApproved,
            EarnedTodayInCampaign = InRange(dayStart, dayEnd),
            EarnedThisWeekInCampaign = InRange(weekStart, weekEnd),
            EarnedInCampaignTotal = rows.Sum(r => r.Amount),
            CampaignBudgetRemaining = budget,
            QualityBonusRequested = qualityBonusRequested,
        };
    }

    public async Task<PricedReward> QuoteSubmissionAsync(Submission submission, decimal? qualityBonusRequested, CancellationToken ct = default)
    {
        var campaign = await db.Set<Campaign>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == submission.CampaignId, ct)
            ?? throw DomainException.NotFound("Campaign");
        var ruleSet = await LoadRuleSetAsync(submission.RewardRuleSetId, ct);
        var context = await BuildContextAsync(campaign, ruleSet.Currency, submission.UserId, submission.Platform,
            submission.PostedAt, submission.SubmittedAt, qualityBonusRequested, submission.Id, ct);
        // The person-level rate locked when the submission was created (none = campaign rules), never re-resolved.
        var rate = await db.Set<SubmissionRate>().AsNoTracking().FirstOrDefaultAsync(r => r.SubmissionId == submission.Id, ct);
        if (rate is not null) context = context with { PersonalRate = Rates.PersonalRateService.ToInput(rate) };
        return new PricedReward(ruleSet, context, RewardEngine.Quote(ruleSet, context), rate);
    }

    public async Task<decimal?> BudgetRemainingAsync(Campaign campaign, CancellationToken ct = default)
    {
        if (!campaign.BudgetAmount.HasValue) return null;
        var spent = await SpentQuery(db, campaign.Id).SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        return campaign.BudgetAmount.Value - spent;
    }
}

/// <summary>Campaign-local calendar helpers (IANA zones; weeks start on Monday).</summary>
public static class CampaignClock
{
    public static bool IsValidZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);

    public static TimeZoneInfo Zone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;

    public static (DateTime StartUtc, DateTime EndUtc) LocalDay(TimeZoneInfo zone, DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        return (ToUtc(local.Date, zone), ToUtc(local.Date.AddDays(1), zone));
    }

    public static (DateTime StartUtc, DateTime EndUtc) LocalWeek(TimeZoneInfo zone, DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        var monday = local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7));
        return (ToUtc(monday, zone), ToUtc(monday.AddDays(7), zone));
    }

    /// <summary>Converts a local wall-clock time to UTC, moving forward past a DST gap if midnight does not exist.</summary>
    private static DateTime ToUtc(DateTime localUnspecified, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
