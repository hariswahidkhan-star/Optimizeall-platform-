namespace OptimizeAll.Domain.Settings;

/// <summary>Administrator-editable platform setting stored as JSON. Keys are listed in <see cref="SettingKeys"/>.</summary>
public class SystemSetting
{
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = "null";
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

public static class SettingKeys
{
    /// <summary>int: default minimum social account age (days) for eligibility. Default 90.</summary>
    public const string MinAccountAgeDays = "eligibility.minAccountAgeDays";

    /// <summary>int: default minimum followers. Default 0.</summary>
    public const string MinFollowers = "eligibility.minFollowers";

    /// <summary>int: submissions per participant per rolling 24h across all campaigns before a velocity flag. Default 10.</summary>
    public const string SubmissionVelocityLimit = "fraud.submissionVelocityPer24h";

    /// <summary>int: risk score at or above which submissions require senior review. Default 50.</summary>
    public const string HighRiskThreshold = "fraud.highRiskThreshold";

    /// <summary>int: minutes a reviewer's claim on a submission lasts. Default 15.</summary>
    public const string ReviewClaimMinutes = "review.claimMinutes";

    /// <summary>int: days after a decision during which the participant may appeal. Default 14.</summary>
    public const string AppealWindowDays = "review.appealWindowDays";

    /// <summary>ReferralProgramSettings JSON.</summary>
    public const string ReferralProgram = "referral.program";

    /// <summary>int: days without activity before a participant is considered inactive. Default 30.</summary>
    public const string InactivityDays = "retention.inactivityDays";

    /// <summary>bool: whether retention automations (reminders, reactivation, alerts) run. Default true.</summary>
    public const string RetentionEnabled = "retention.enabled";

    /// <summary>
    /// int: a new rate card version that raises any rate by more than this percentage needs a second person's approval
    /// (four-eyes). 0 = off. Default 0.
    /// </summary>
    public const string RatesFourEyesIncreasePercent = "rates.fourEyesIncreasePercent";
}

/// <summary>Referral program configuration (setting <see cref="SettingKeys.ReferralProgram"/>).</summary>
public class ReferralProgramSettings
{
    public bool Enabled { get; set; } = true;
    public decimal ReferrerRewardAmount { get; set; } = 5m;
    public string Currency { get; set; } = "USD";
    public string QualifyingAction { get; set; } = "FirstApprovedSubmission";
    public int QualifyWithinDays { get; set; } = 60;
    public bool RequireManualApproval { get; set; } = true;
    public int MaxRewardedReferralsPerUser { get; set; } = 50;
}
