using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Admin;

/// <summary>Definition + validation rules of every administrator-editable setting.</summary>
public static class SettingDefinitions
{
    public sealed record Definition(string Key, string ValueType, string Description, Func<JsonElement, (object? Value, string? Error)> Validate);

    public static readonly string[] ReferralQualifyingActions = { "EmailVerified", "FirstApprovedSubmission", "FirstPaidPayout" };

    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static readonly IReadOnlyDictionary<string, Definition> All = new[]
    {
        Int(SettingKeys.MinAccountAgeDays, 0, 3650,
            "Minimum age (days) of a social profile before it qualifies for campaigns. Campaigns may require more."),
        Int(SettingKeys.MinFollowers, 0, 10_000_000, "Minimum follower count for a social profile to qualify, platform-wide."),
        Int(SettingKeys.SubmissionVelocityLimit, 1, 1000,
            "Submissions per participant in a rolling 24 hours (all campaigns) before the velocity fraud flag is raised."),
        Int(SettingKeys.HighRiskThreshold, 1, 1000, "Risk score at or above which a submission requires senior review."),
        Int(SettingKeys.ReviewClaimMinutes, 1, 240, "Minutes a reviewer's claim on a submission lasts before it returns to the queue."),
        Int(SettingKeys.AppealWindowDays, 1, 365, "Days after a decision during which the participant may appeal."),
        Int(SettingKeys.InactivityDays, 7, 365, "Days without activity after which a participant counts as inactive (retention and content audiences)."),
        new Definition(SettingKeys.RetentionEnabled, "boolean",
            "Whether retention automations (onboarding reminders, reactivation, campaign alerts) run.",
            v => v.ValueKind is JsonValueKind.True or JsonValueKind.False ? (v.GetBoolean(), null) : (null, "Use true or false.")),
        new Definition(SettingKeys.ReferralProgram, "object", "Referral program: reward, currency, qualifying action and limits.", ValidateReferral),
        new Definition(SettingKeys.LearningIssuerName, "string",
            "Issuing organisation named on course certificates, Open Badges and LinkedIn \"Add to profile\" (e.g. Optimize All Academy).",
            v => v.ValueKind == JsonValueKind.String && v.GetString()!.Trim() is { Length: >= 2 and <= 100 } name
                ? (name, null) : (null, "Use 2 to 100 characters.")),
        new Definition(SettingKeys.LearningLinkedInOrganizationId, "string",
            "LinkedIn company page id of the issuer (digits, from the page admin URL). Empty: LinkedIn gets the issuer name instead.",
            v => v.ValueKind == JsonValueKind.String && v.GetString()!.Trim() is var id && (id.Length == 0 || (id.Length <= 20 && id.All(char.IsAsciiDigit)))
                ? (id, null) : (null, "Use the numeric LinkedIn organization id (up to 20 digits), or leave it empty.")),
    }.ToDictionary(d => d.Key);

    private static Definition Int(string key, int min, int max, string description) => new(key, "integer", description, v =>
        v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i >= min && i <= max ? (i, null) : (null, $"Use a whole number from {min} to {max}.")
            : (null, $"Use a whole number from {min} to {max}."));

    private static (object?, string?) ValidateReferral(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return (null, "Send the referral program settings as an object.");
        ReferralProgramSettings? s;
        try
        {
            s = v.Deserialize<ReferralProgramSettings>(StrictJson);
        }
        catch (JsonException ex)
        {
            return (null, $"Invalid referral settings: {ex.Message}");
        }
        if (s is null) return (null, "Send the referral program settings as an object.");

        var errors = new List<string>();
        if (s.ReferrerRewardAmount < 0 || s.ReferrerRewardAmount > 100_000) errors.Add("referrerRewardAmount must be between 0 and 100,000.");
        s.Currency = Money.Normalize(s.Currency ?? string.Empty);
        if (!Money.IsSupported(s.Currency)) errors.Add($"currency must be one of {string.Join(", ", Money.SupportedCurrencies.OrderBy(c => c))}.");
        else s.ReferrerRewardAmount = Money.Round(s.ReferrerRewardAmount, s.Currency);
        var action = ReferralQualifyingActions.FirstOrDefault(a => string.Equals(a, s.QualifyingAction, StringComparison.OrdinalIgnoreCase));
        if (action is null) errors.Add($"qualifyingAction must be one of {string.Join(", ", ReferralQualifyingActions)}.");
        else s.QualifyingAction = action;
        if (s.QualifyWithinDays is < 1 or > 365) errors.Add("qualifyWithinDays must be from 1 to 365.");
        if (s.MaxRewardedReferralsPerUser is < 0 or > 100_000) errors.Add("maxRewardedReferralsPerUser must be from 0 to 100,000.");
        return errors.Count > 0 ? (null, string.Join(" ", errors)) : (s, null);
    }
}

public sealed class AdminSettingsService(
    AppDbContext db, ISettingsService settings, IAuditLogger audit, ICurrentUser currentUser)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SettingDto>> ListAsync(CancellationToken ct)
    {
        var rows = await db.Set<SystemSetting>().AsNoTracking().ToListAsync(ct);
        var actorIds = rows.Where(r => r.UpdatedByUserId.HasValue).Select(r => r.UpdatedByUserId!.Value).Distinct().ToList();
        var actors = await db.Set<User>().AsNoTracking().Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return SettingDefinitions.All.Values.Select(def =>
        {
            var row = rows.FirstOrDefault(r => r.Key == def.Key);
            return ToDto(def, row, actors);
        }).ToList();
    }

    public async Task<SettingDto> UpdateAsync(string key, UpdateSettingRequest request, CancellationToken ct)
    {
        if (!SettingDefinitions.All.TryGetValue(key, out var def))
            throw DomainException.NotFound("Setting");
        if (!request.Confirm)
            throw FieldRules.FieldError("admin.confirmation_required", "confirm", "Confirm this change by sending \"confirm\": true.");

        var (value, error) = def.Validate(request.Value!.Value);
        if (error is not null) throw FieldRules.FieldError("settings.invalid_value", "value", error);

        var existing = await db.Set<SystemSetting>().AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        var before = existing is null ? DefaultElement(key) : Parse(existing.ValueJson);

        await settings.SetAsync<object>(key, value!, currentUser.Id, def.Description, ct);
        audit.Record("admin.setting_changed", nameof(SystemSetting), key,
            new { value = before }, new { value = JsonSerializer.SerializeToElement<object>(value!, Json) }, request.Reason.Trim());
        await db.SaveChangesAsync(ct);

        return (await ListAsync(ct)).First(s => s.Key == key);
    }

    /// <summary>Removes the saved value so the built-in default applies again (confirmed and audited like an update).</summary>
    public async Task<SettingDto> ResetAsync(string key, ResetSettingRequest request, CancellationToken ct)
    {
        if (!SettingDefinitions.All.ContainsKey(key))
            throw DomainException.NotFound("Setting");
        if (!request.Confirm)
            throw FieldRules.FieldError("admin.confirmation_required", "confirm", "Confirm this change by sending \"confirm\": true.");
        var row = await db.Set<SystemSetting>().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is not null)
        {
            audit.Record("admin.setting_reset", nameof(SystemSetting), key,
                new { value = Parse(row.ValueJson) }, new { value = DefaultElement(key) }, request.Reason.Trim());
            db.Remove(row);
            await db.SaveChangesAsync(ct);
        }
        return (await ListAsync(ct)).First(s => s.Key == key);
    }

    private static SettingDto ToDto(SettingDefinitions.Definition def, SystemSetting? row, IReadOnlyDictionary<Guid, string> actors)
    {
        var defaultValue = DefaultElement(def.Key);
        var value = row is null ? defaultValue : Parse(row.ValueJson);
        SettingActorDto? by = row?.UpdatedByUserId is { } actor
            ? new SettingActorDto(actor, actors.TryGetValue(actor, out var name) ? name : "Unknown")
            : null;
        return new SettingDto(def.Key, value, defaultValue, row is null, def.ValueType, def.Description, row?.UpdatedAt, by);
    }

    private static JsonElement DefaultElement(string key) =>
        SettingsService.Defaults.TryGetValue(key, out var d) ? JsonSerializer.SerializeToElement<object>(d, Json) : JsonSerializer.SerializeToElement<object?>(null);

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
