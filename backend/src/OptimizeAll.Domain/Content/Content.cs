using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Content;

/// <summary>Which participants see a banner/announcement. Evaluated server-side from account state.</summary>
public enum ContentAudience
{
    Everyone,
    /// <summary>Email not verified or no qualifying social profile yet.</summary>
    Onboarding,
    /// <summary>Has at least one qualifying social profile.</summary>
    Eligible,
    /// <summary>Has at least one approved submission.</summary>
    ActiveEarners,
    /// <summary>No activity in the configured inactivity window.</summary>
    Inactive,
}

public class HomepageBanner : AuditedEntity, IConcurrencyStamped
{
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? ImageUrl { get; set; }
    public string? CtaLabel { get; set; }
    public string? CtaUrl { get; set; }
    public ContentAudience Audience { get; set; } = ContentAudience.Everyone;
    public string? CountryCode { get; set; }
    public string? LanguageCode { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public enum AnnouncementSeverity
{
    Info,
    Success,
    Warning,
    Critical,
}

public class Announcement : AuditedEntity, IConcurrencyStamped
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public AnnouncementSeverity Severity { get; set; } = AnnouncementSeverity.Info;
    public ContentAudience Audience { get; set; } = ContentAudience.Everyone;
    public DateTime PublishAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class FaqItem : AuditedEntity, IConcurrencyStamped
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>How the backend decides an onboarding step is complete for a participant.</summary>
public enum OnboardingCompletionRule
{
    /// <summary>Only completed when the participant dismisses it (informational step).</summary>
    Manual,
    EmailVerified,
    ProfileCompleted,
    SocialAccountAdded,
    EligibleSocialAccount,
    PayoutProfileAdded,
    FirstSubmission,
    FirstApprovedSubmission,
}

public class OnboardingStep : AuditedEntity, IConcurrencyStamped
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ActionLabel { get; set; }
    public string? ActionUrl { get; set; }
    public OnboardingCompletionRule CompletionRule { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Records steps with <see cref="OnboardingCompletionRule.Manual"/> the participant dismissed.</summary>
public class OnboardingStepCompletion
{
    public Guid UserId { get; set; }
    public Guid StepId { get; set; }
    public DateTime CompletedAt { get; set; }
}
