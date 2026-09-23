using System.Text.Json;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Admin;
using OptimizeAll.Domain.Settings;

namespace OptimizeAll.UnitTests.Admin;

public sealed class SettingDefinitionsTests
{
    private static (object? Value, string? Error) Validate(string key, string json) =>
        SettingDefinitions.All[key].Validate(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void Every_setting_key_has_a_definition_and_a_default()
    {
        var keys = typeof(SettingKeys).GetFields().Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.Equal(keys.OrderBy(k => k), SettingDefinitions.All.Keys.OrderBy(k => k));
        Assert.All(keys, k => Assert.True(SettingsService.Defaults.ContainsKey(k)));
    }

    [Theory]
    [InlineData(SettingKeys.MinAccountAgeDays, "0", true)]
    [InlineData(SettingKeys.MinAccountAgeDays, "3650", true)]
    [InlineData(SettingKeys.MinAccountAgeDays, "3651", false)]
    [InlineData(SettingKeys.MinFollowers, "10000000", true)]
    [InlineData(SettingKeys.MinFollowers, "10000001", false)]
    [InlineData(SettingKeys.SubmissionVelocityLimit, "0", false)]
    [InlineData(SettingKeys.HighRiskThreshold, "1000", true)]
    [InlineData(SettingKeys.ReviewClaimMinutes, "241", false)]
    [InlineData(SettingKeys.AppealWindowDays, "365", true)]
    [InlineData(SettingKeys.InactivityDays, "7", true)]
    [InlineData(SettingKeys.InactivityDays, "6", false)]
    [InlineData(SettingKeys.MinAccountAgeDays, "1.5", false)]
    [InlineData(SettingKeys.MinAccountAgeDays, "\"30\"", false)]
    [InlineData(SettingKeys.RetentionEnabled, "true", true)]
    [InlineData(SettingKeys.RetentionEnabled, "1", false)]
    public void Scalar_ranges(string key, string json, bool valid) => Assert.Equal(valid, Validate(key, json).Error is null);

    [Fact]
    public void Referral_program_is_validated_and_normalized()
    {
        var (value, error) = Validate(SettingKeys.ReferralProgram,
            """{"enabled":true,"referrerRewardAmount":5.555,"currency":"usd","qualifyingAction":"emailverified","qualifyWithinDays":30}""");
        Assert.Null(error);
        var s = Assert.IsType<ReferralProgramSettings>(value);
        Assert.Equal("USD", s.Currency);
        Assert.Equal("EmailVerified", s.QualifyingAction);
        Assert.Equal(5.56m, s.ReferrerRewardAmount);

        Assert.NotNull(Validate(SettingKeys.ReferralProgram, """{"currency":"USD","qualifyingAction":"Other","qualifyWithinDays":30}""").Error);
        Assert.NotNull(Validate(SettingKeys.ReferralProgram, """{"currency":"USD","qualifyingAction":"EmailVerified","qualifyWithinDays":0}""").Error);
        Assert.NotNull(Validate(SettingKeys.ReferralProgram, """{"currency":"USD","qualifyingAction":"EmailVerified","qualifyWithinDays":30,"extra":1}""").Error);
        Assert.NotNull(Validate(SettingKeys.ReferralProgram, "[]").Error);
    }
}
