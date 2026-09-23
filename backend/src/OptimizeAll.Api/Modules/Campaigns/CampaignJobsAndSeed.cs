using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Campaigns;

/// <summary>
/// Moves campaigns along their schedule: Scheduled → Active once StartsAt has passed, Active → Ended once the
/// submission deadline has passed. Each transition is a conditional update (safe to retry / run concurrently).
/// </summary>
public sealed class CampaignScheduleJob(AppDbContext db, IAuditLogger audit, TimeProvider clock) : IJob
{
    public string Name => "campaign-schedule";

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var activated = await MoveAsync(CampaignStatus.Scheduled, CampaignStatus.Active, c => c.StartsAt <= now, "campaign.activated", now, ct);
        var ended = await MoveAsync(CampaignStatus.Active, CampaignStatus.Ended, c => c.SubmissionDeadline < now, "campaign.ended", now, ct);
        return $"activated {activated}, ended {ended}";
    }

    private async Task<int> MoveAsync(CampaignStatus from, CampaignStatus to, System.Linq.Expressions.Expression<Func<Campaign, bool>> due,
        string action, DateTime now, CancellationToken ct)
    {
        var ids = await db.Set<Campaign>().Where(c => c.Status == from).Where(due).Select(c => c.Id).ToListAsync(ct);
        var moved = 0;
        foreach (var id in ids)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var updated = await db.Set<Campaign>().Where(c => c.Id == id && c.Status == from)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, to).SetProperty(c => c.UpdatedAt, now), ct);
            if (updated == 1)
            {
                audit.RecordSystem(action, nameof(Campaign), id, new { From = from.ToString(), To = to.ToString() }, "Campaign schedule");
                await db.SaveChangesAsync(ct);
                moved++;
            }
            await tx.CommitAsync(ct);
        }
        return moved;
    }
}

/// <summary>Baseline campaign categories (idempotent by slug).</summary>
public sealed class CampaignCategorySeeder : ISeeder
{
    public string Profile => "Baseline";
    public int Order => 20;

    private static readonly (string Name, string Slug, string Icon)[] Categories =
    {
        ("Technology", "technology", "cpu"),
        ("Fashion & Beauty", "fashion-beauty", "shirt"),
        ("Food & Drink", "food-drink", "utensils"),
        ("Travel", "travel", "plane"),
        ("Finance", "finance", "landmark"),
        ("Gaming", "gaming", "gamepad-2"),
        ("Health & Fitness", "health-fitness", "dumbbell"),
        ("Education", "education", "graduation-cap"),
        ("Lifestyle", "lifestyle", "sparkles"),
    };

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.Set<CampaignCategory>().Select(c => c.Slug).ToListAsync(ct);
        var order = 0;
        foreach (var (name, slug, icon) in Categories)
        {
            order += 10;
            if (existing.Contains(slug)) continue;
            db.Add(new CampaignCategory { Name = name, Slug = slug, Icon = icon, SortOrder = order, IsActive = true });
        }
        await db.SaveChangesAsync(ct);
    }
}
