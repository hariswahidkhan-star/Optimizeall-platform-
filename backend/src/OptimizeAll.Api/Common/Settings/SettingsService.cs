using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Settings;

/// <summary>Typed access to administrator-editable <see cref="SystemSetting"/> rows with code defaults.</summary>
public interface ISettingsService
{
    Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken ct = default);

    /// <summary>Stages an upsert in the current DbContext; the caller saves (and audits) it.</summary>
    Task SetAsync<T>(string key, T value, Guid? updatedBy, string? description = null, CancellationToken ct = default);

    Task<int> MinAccountAgeDaysAsync(CancellationToken ct = default);
    Task<int> MinFollowersAsync(CancellationToken ct = default);
}

public sealed class SettingsService(AppDbContext db, TimeProvider clock) : ISettingsService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Documented defaults used when no row exists.</summary>
    public static readonly IReadOnlyDictionary<string, object> Defaults = new Dictionary<string, object>
    {
        [SettingKeys.MinAccountAgeDays] = 90,
        [SettingKeys.MinFollowers] = 0,
        [SettingKeys.SubmissionVelocityLimit] = 10,
        [SettingKeys.HighRiskThreshold] = 50,
        [SettingKeys.ReviewClaimMinutes] = 15,
        [SettingKeys.AppealWindowDays] = 14,
        [SettingKeys.InactivityDays] = 30,
        [SettingKeys.RetentionEnabled] = true,
        [SettingKeys.ReferralProgram] = new ReferralProgramSettings(),
    };

    public async Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken ct = default)
    {
        var row = await db.Set<SystemSetting>().AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) return defaultValue;
        try
        {
            return JsonSerializer.Deserialize<T>(row.ValueJson, Json) ?? defaultValue;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    public async Task SetAsync<T>(string key, T value, Guid? updatedBy, string? description = null, CancellationToken ct = default)
    {
        var row = await db.Set<SystemSetting>().FirstOrDefaultAsync(s => s.Key == key, ct);
        var json = JsonSerializer.Serialize(value, Json);
        if (row is null)
        {
            db.Set<SystemSetting>().Add(new SystemSetting
            {
                Key = key, ValueJson = json, Description = description,
                UpdatedAt = clock.GetUtcNow().UtcDateTime, UpdatedByUserId = updatedBy,
            });
        }
        else
        {
            row.ValueJson = json;
            if (description is not null) row.Description = description;
            row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            row.UpdatedByUserId = updatedBy;
        }
    }

    public Task<int> MinAccountAgeDaysAsync(CancellationToken ct = default) => GetAsync(SettingKeys.MinAccountAgeDays, 90, ct);

    public Task<int> MinFollowersAsync(CancellationToken ct = default) => GetAsync(SettingKeys.MinFollowers, 0, ct);
}
