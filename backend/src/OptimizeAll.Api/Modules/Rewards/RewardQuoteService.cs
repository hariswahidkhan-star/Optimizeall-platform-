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
public sealed record PricedReward(RewardRuleSet RuleSet, RewardContext Context, RewardQuote Quote);

/// <summary>
/// Builds <see cref="RewardContext"/>s from the database and prices posts with <see cref="RewardEngine"/>.
/// Aggregates come from the participant's non-reversed, non-declined earnings in the campaign; "today"/"this week"
/// are the campaign-local day/week (Monday start) of the post's PostedAt.
/// </summary>
public interface IRewardQuoteService
{
    /// <summary>The version in force at <paramref name="atUtc"/> (highest version with EffectiveFrom ≤ at), with rules.</summary>
    Task<RewardRuleSet?> GetCurrentRuleSetAsync(Guid campaignId, DateTime atUtc, CancellationToken ct = default);

    Task<RewardRuleSet> LoadRuleSetAsync(Guid ruleSetId, CancellationToken ct = default);

    /// <summary>
    /// Context for a post by <paramref name="userId"/>. <paramref name="excludeSubmissionId"/> is the submission being
    /// priced (never counted as a prior approval).
    /// </summary>
    Task<RewardContext> BuildContextAsync(Campaign campaign, string ruleSetCurrency, Guid userId, SocialPlatform platform,
        DateTime postedAtUtc, decimal? qualityBonusRequested, Guid? excludeSubmissionId, CancellationToken ct = default);

    /// <summary>Prices a submission from its <b>recorded</b> rule set version.</summary>
    Task<PricedReward> QuoteSubmissionAsync(Submission submission, decimal? qualityBonusRequested, CancellationToken ct = default);

    /// <summary>Campaign budget minus all non-reversed, non-declined campaign earnings; null when no budget is set.</summary>
    Task<decimal?> BudgetRemainingAsync(Campaign campaign, CancellationToken ct = default);
}

public sealed class RewardQuoteService(AppDbContext db) : IRewardQuoteService
{
    public static string FirstPostKey(Guid campaignId, Guid userId) => $"firstpost:{campaignId}:{userId}";

    public async Task<RewardRuleSet?> GetCurrentRuleSetAsync(Guid campaignId, DateTime atUtc, CancellationToken ct = default) =>
        await db.Set<RewardRuleSet>().AsNoTracking().Include(r => r.Rules)
            .Where(r => r.CampaignId == campaignId && r.EffectiveFrom <= atUtc)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);

    public async Task<RewardRuleSet> LoadRuleSetAsync(Guid ruleSetId, CancellationToken ct = default) =>
        await db.Set<RewardRuleSet>().AsNoTracking().Include(r => r.Rules).FirstOrDefaultAsync(r => r.Id == ruleSetId, ct)
        ?? throw DomainException.NotFound("RewardRuleSet");

    public async Task<RewardContext> BuildContextAsync(Campaign campaign, string ruleSetCurrency, Guid userId, SocialPlatform platform,
        DateTime postedAtUtc, decimal? qualityBonusRequested, Guid? excludeSubmissionId, CancellationToken ct = default)
    {
        var participant = await db.Set<User>().AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.CountryCode, u.Tier }).FirstOrDefaultAsync(ct)
            ?? throw DomainException.NotFound("User");

        var rows = await (
                from e in db.Set<EarningEntry>().AsNoTracking()
                where e.UserId == userId && e.CampaignId == campaign.Id &&
                      e.Status != EarningStatus.Reversed && e.Status != EarningStatus.Declined
                join s in db.Set<Submission>() on e.SubmissionId equals (Guid?)s.Id into sj
                from s in sj.DefaultIfEmpty()
                select new { e.Amount, PostedAt = (DateTime?)s.PostedAt, e.CreatedAt })
            .ToListAsync(ct);

        var zone = CampaignClock.Zone(campaign.TimeZone);
        var (dayStart, dayEnd) = CampaignClock.LocalDay(zone, postedAtUtc);
        var (weekStart, weekEnd) = CampaignClock.LocalWeek(zone, postedAtUtc);
        decimal InRange(DateTime from, DateTime to) =>
            rows.Where(r => (r.PostedAt ?? r.CreatedAt) >= from && (r.PostedAt ?? r.CreatedAt) < to).Sum(r => r.Amount);

        var firstPostTaken = await db.Set<EarningEntry>().AnyAsync(e => e.IdempotencyKey == FirstPostKey(campaign.Id, userId), ct);
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
            PostedAtUtc = postedAtUtc,
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
            submission.PostedAt, qualityBonusRequested, submission.Id, ct);
        return new PricedReward(ruleSet, context, RewardEngine.Quote(ruleSet, context));
    }

    public async Task<decimal?> BudgetRemainingAsync(Campaign campaign, CancellationToken ct = default)
    {
        if (!campaign.BudgetAmount.HasValue) return null;
        var spent = await db.Set<EarningEntry>()
            .Where(e => e.CampaignId == campaign.Id && e.Status != EarningStatus.Reversed && e.Status != EarningStatus.Declined)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
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
