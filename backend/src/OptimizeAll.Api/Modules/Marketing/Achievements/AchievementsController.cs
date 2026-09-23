using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Marketing.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Marketing.Achievements;

public sealed record MyAchievementDto(
    string Key, string Name, string Description, string? Icon, AchievementCriterion Criterion, decimal Threshold,
    decimal Progress, DateTime? AwardedAt);

public sealed record AchievementDto(
    Guid Id, string Key, string Name, string Description, string? Icon, AchievementCriterion Criterion, decimal Threshold,
    int SortOrder, bool IsActive, int AwardedCount, DateTime CreatedAt, DateTime UpdatedAt);

public sealed class AchievementRequest
{
    [Required, RegularExpression("^[a-z0-9][a-z0-9-]{1,59}$", ErrorMessage = "Use lower-case letters, digits and dashes.")]
    public string Key { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Lucide icon name, e.g. "trophy".</summary>
    [MaxLength(50), RegularExpression("^[a-z0-9-]*$")]
    public string? Icon { get; set; }

    [Required]
    public AchievementCriterion? Criterion { get; set; }

    [Range(typeof(decimal), "0.0001", "1000000000")]
    public decimal Threshold { get; set; }

    [Range(0, 100000)]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

[ApiController]
public sealed class AchievementsController(
    AppDbContext db, AchievementEvaluator evaluator, IAuditLogger audit, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Every active achievement with the caller's progress (current metric value) and award time.</summary>
    [HttpGet("api/v1/me/achievements")]
    [HasPermission(Permissions.ParticipantPortal)]
    public async Task<IReadOnlyList<MyAchievementDto>> Mine(CancellationToken ct)
    {
        var userId = currentUser.Id;
        var metrics = await evaluator.MetricsAsync(userId, ct);
        var awarded = await db.Set<UserAchievement>().AsNoTracking().Where(u => u.UserId == userId)
            .ToDictionaryAsync(u => u.AchievementId, u => u.AwardedAt, ct);
        var achievements = await db.Set<Achievement>().AsNoTracking()
            .Where(a => a.IsActive).OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToListAsync(ct);
        return achievements.Select(a => new MyAchievementDto(a.Key, a.Name, a.Description, a.Icon, a.Criterion, a.Threshold,
            metrics.TryGetValue(a.Criterion, out var v) ? v : 0m,
            awarded.TryGetValue(a.Id, out var at) ? at : null)).ToList();
    }

    [HttpGet("api/v1/marketing/achievements")]
    [HasPermission(Permissions.MarketingManage)]
    public async Task<IReadOnlyList<AchievementDto>> List(CancellationToken ct) =>
        await db.Set<Achievement>().AsNoTracking().OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .Select(a => new AchievementDto(a.Id, a.Key, a.Name, a.Description, a.Icon, a.Criterion, a.Threshold, a.SortOrder,
                a.IsActive, db.Set<UserAchievement>().Count(u => u.AchievementId == a.Id), a.CreatedAt, a.UpdatedAt))
            .ToListAsync(ct);

    [HttpGet("api/v1/marketing/achievements/{id:guid}")]
    [HasPermission(Permissions.MarketingManage)]
    public async Task<AchievementDto> Get(Guid id, CancellationToken ct) =>
        await db.Set<Achievement>().AsNoTracking().Where(a => a.Id == id)
            .Select(a => new AchievementDto(a.Id, a.Key, a.Name, a.Description, a.Icon, a.Criterion, a.Threshold, a.SortOrder,
                a.IsActive, db.Set<UserAchievement>().Count(u => u.AchievementId == a.Id), a.CreatedAt, a.UpdatedAt))
            .FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("Achievement");

    [HttpPost("api/v1/marketing/achievements")]
    [HasPermission(Permissions.MarketingManage)]
    public async Task<ActionResult<AchievementDto>> Create(AchievementRequest request, CancellationToken ct)
    {
        if (await db.Set<Achievement>().AnyAsync(a => a.Key == request.Key, ct))
            throw DomainException.Conflict("achievement.key_taken", "An achievement with this key already exists.");
        var achievement = new Achievement();
        Apply(achievement, request);
        db.Set<Achievement>().Add(achievement);
        audit.Record("achievement.created", nameof(Achievement), achievement.Id, after: request);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("achievement.key_taken", "An achievement with this key already exists.");
        }
        return CreatedAtAction(nameof(Get), new { id = achievement.Id }, await Get(achievement.Id, ct));
    }

    /// <summary>Updates an achievement. The key cannot change once the achievement has been awarded.</summary>
    [HttpPut("api/v1/marketing/achievements/{id:guid}")]
    [HasPermission(Permissions.MarketingManage)]
    public async Task<AchievementDto> Update(Guid id, AchievementRequest request, CancellationToken ct)
    {
        var achievement = await db.Set<Achievement>().FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw DomainException.NotFound("Achievement");
        var awarded = await db.Set<UserAchievement>().AnyAsync(u => u.AchievementId == id, ct);
        if (awarded && !string.Equals(achievement.Key, request.Key, StringComparison.Ordinal))
            throw DomainException.Conflict("achievement.awarded", "The key of an awarded achievement cannot be changed.");
        if (!string.Equals(achievement.Key, request.Key, StringComparison.Ordinal) &&
            await db.Set<Achievement>().AnyAsync(a => a.Key == request.Key && a.Id != id, ct))
            throw DomainException.Conflict("achievement.key_taken", "An achievement with this key already exists.");

        var before = new { achievement.Key, achievement.Name, achievement.Criterion, achievement.Threshold, achievement.IsActive };
        Apply(achievement, request);
        audit.Record("achievement.updated", nameof(Achievement), id, before, request);
        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    /// <summary>Deletes an achievement that was never awarded; awarded ones can only be deactivated (409 achievement.awarded).</summary>
    [HttpDelete("api/v1/marketing/achievements/{id:guid}")]
    [HasPermission(Permissions.MarketingManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var achievement = await db.Set<Achievement>().FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw DomainException.NotFound("Achievement");
        if (await db.Set<UserAchievement>().AnyAsync(u => u.AchievementId == id, ct))
            throw DomainException.Conflict("achievement.awarded",
                "This achievement has been awarded to participants. Deactivate it instead of deleting it.");
        db.Set<Achievement>().Remove(achievement);
        audit.Record("achievement.deleted", nameof(Achievement), id, before: new { achievement.Key, achievement.Name });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(Achievement a, AchievementRequest r)
    {
        a.Key = r.Key.Trim();
        a.Name = r.Name.Trim();
        a.Description = r.Description.Trim();
        a.Icon = string.IsNullOrWhiteSpace(r.Icon) ? null : r.Icon.Trim();
        a.Criterion = r.Criterion!.Value;
        a.Threshold = r.Threshold;
        a.SortOrder = r.SortOrder;
        a.IsActive = r.IsActive;
    }
}
