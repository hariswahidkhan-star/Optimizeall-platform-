using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Marketing.Achievements;

/// <summary>
/// Computes a participant's achievement metrics and awards any active achievement whose threshold is met.
/// Awarding is an INSERT IGNORE on the (UserId, AchievementId) primary key, and the notification is only staged when
/// that insert created the row, in the same transaction — so duplicate events can never award or notify twice.
/// </summary>
public sealed class AchievementEvaluator(AppDbContext db, INotificationService notifications, TimeProvider clock)
{
    public async Task<IReadOnlyDictionary<AchievementCriterion, decimal>> MetricsAsync(Guid userId, CancellationToken ct)
    {
        var approved = db.Set<Submission>().AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == SubmissionStatus.Approved);

        var approvedCount = await approved.CountAsync(ct);
        var platforms = await approved.Select(s => s.Platform).Distinct().CountAsync(ct);
        var campaigns = await approved.Select(s => s.CampaignId).Distinct().CountAsync(ct);
        var referrals = await db.Set<Referral>().AsNoTracking()
            .CountAsync(r => r.ReferrerUserId == userId && r.Status == ReferralStatus.Qualified, ct);
        // Net earned in settlement currency: payable/paid entries (clawbacks are negative Approved entries).
        var earned = await db.Set<EarningEntry>().AsNoTracking()
            .Where(e => e.UserId == userId &&
                        (e.Status == EarningStatus.Approved || e.Status == EarningStatus.Scheduled || e.Status == EarningStatus.Paid))
            .SumAsync(e => (decimal?)e.SettlementAmount, ct) ?? 0m;

        return new Dictionary<AchievementCriterion, decimal>
        {
            [AchievementCriterion.ApprovedSubmissions] = approvedCount,
            [AchievementCriterion.PlatformsUsed] = platforms,
            [AchievementCriterion.CampaignsCompleted] = campaigns,
            [AchievementCriterion.QualifiedReferrals] = referrals,
            [AchievementCriterion.TotalEarnedSettlement] = earned,
        };
    }

    /// <summary>Awards every missing active achievement the participant qualifies for. Returns the newly awarded keys.</summary>
    public async Task<IReadOnlyList<string>> EvaluateAsync(Guid userId, CancellationToken ct)
    {
        var awardedIds = await db.Set<UserAchievement>().AsNoTracking()
            .Where(u => u.UserId == userId).Select(u => u.AchievementId).ToListAsync(ct);
        var candidates = await db.Set<Achievement>().AsNoTracking()
            .Where(a => a.IsActive && !awardedIds.Contains(a.Id))
            .OrderBy(a => a.SortOrder).ToListAsync(ct);
        if (candidates.Count == 0) return Array.Empty<string>();

        var metrics = await MetricsAsync(userId, ct);
        var awarded = new List<string>();
        foreach (var achievement in candidates.Where(a => metrics.TryGetValue(a.Criterion, out var v) && v >= a.Threshold))
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT IGNORE INTO user_achievements (UserId, AchievementId, AwardedAt) VALUES ({userId.ToString()}, {achievement.Id.ToString()}, {now})",
                ct);
            if (inserted == 1)
            {
                await notifications.StageAsync(new NotificationRequest(
                    userId, NotificationTypes.Achievement, $"Achievement unlocked: {achievement.Name}",
                    achievement.Description, "/achievements", new[] { NotificationChannel.InApp }), ct);
                await db.SaveChangesAsync(ct);
                awarded.Add(achievement.Key);
            }
            await tx.CommitAsync(ct);
        }
        return awarded;
    }
}

public sealed class AchievementSubmissionApprovedHandler(AchievementEvaluator evaluator) : IEventHandler<SubmissionApproved>
{
    public Task HandleAsync(SubmissionApproved domainEvent, CancellationToken cancellationToken) =>
        evaluator.EvaluateAsync(domainEvent.UserId, cancellationToken);
}

public sealed class AchievementPayoutPaidHandler(AchievementEvaluator evaluator) : IEventHandler<PayoutItemPaid>
{
    public Task HandleAsync(PayoutItemPaid domainEvent, CancellationToken cancellationToken) =>
        evaluator.EvaluateAsync(domainEvent.UserId, cancellationToken);
}

/// <summary>Baseline participant milestones (idempotent by Key; existing rows are left as edited by marketing).</summary>
public sealed class AchievementSeeder : ISeeder
{
    public string Profile => "Baseline";
    public int Order => 60;

    public static readonly IReadOnlyList<Achievement> Defaults = new[]
    {
        New("first-approved-post", "First approved post", "Your first post was approved.", "badge-check", AchievementCriterion.ApprovedSubmissions, 1, 10),
        New("five-approved", "Five approved posts", "Five of your posts have been approved.", "star", AchievementCriterion.ApprovedSubmissions, 5, 20),
        New("twenty-five-approved", "25 approved posts", "25 approved posts — you're a regular.", "stars", AchievementCriterion.ApprovedSubmissions, 25, 30),
        New("hundred-approved", "100 approved posts", "100 approved posts. Outstanding!", "trophy", AchievementCriterion.ApprovedSubmissions, 100, 40),
        New("multi-platform", "Multi-platform", "Approved posts on at least three different platforms.", "layers", AchievementCriterion.PlatformsUsed, 3, 50),
        New("first-100", "First 100 earned", "You've earned your first 100 (settlement currency).", "wallet", AchievementCriterion.TotalEarnedSettlement, 100, 60),
        New("referral-star", "Referral star", "Five people you invited qualified.", "users", AchievementCriterion.QualifiedReferrals, 5, 70),
        New("campaign-explorer", "Campaign explorer", "Approved posts in five different campaigns.", "compass", AchievementCriterion.CampaignsCompleted, 5, 80),
    };

    private static Achievement New(string key, string name, string description, string icon, AchievementCriterion criterion, decimal threshold, int order) =>
        new() { Key = key, Name = name, Description = description, Icon = icon, Criterion = criterion, Threshold = threshold, SortOrder = order };

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = (await db.Set<Achievement>().Select(a => a.Key).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var template in Defaults.Where(d => !existing.Contains(d.Key)))
        {
            db.Set<Achievement>().Add(new Achievement
            {
                Key = template.Key, Name = template.Name, Description = template.Description, Icon = template.Icon,
                Criterion = template.Criterion, Threshold = template.Threshold, SortOrder = template.SortOrder, IsActive = true,
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
