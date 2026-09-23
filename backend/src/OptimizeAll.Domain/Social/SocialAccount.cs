using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Social;

public enum SocialAccountVerificationStatus
{
    /// <summary>Added by the participant, not yet checked by staff.</summary>
    Unverified,
    PendingReview,
    Verified,
    Rejected,
}

/// <summary>
/// A participant's established social media profile. <see cref="AccountCreatedAt"/> and
/// <see cref="FollowerCount"/> are participant-declared until a reviewer verifies them; campaign rules
/// can require verification before a profile qualifies.
/// </summary>
public class SocialAccount : AuditedEntity, IConcurrencyStamped
{
    public Guid UserId { get; set; }
    public SocialPlatform Platform { get; set; }
    public string Handle { get; set; } = string.Empty;

    /// <summary>Lower-case handle without '@'. Unique per platform so one profile cannot be registered to two participants.</summary>
    public string NormalizedHandle { get; set; } = string.Empty;
    public string ProfileUrl { get; set; } = string.Empty;

    /// <summary>When the social profile itself was created (not when it was added here).</summary>
    public DateTime AccountCreatedAt { get; set; }
    public int FollowerCount { get; set; }
    public string? PrimaryLanguage { get; set; }
    public string? AudienceCountryCode { get; set; }

    public SocialAccountVerificationStatus VerificationStatus { get; set; } = SocialAccountVerificationStatus.Unverified;
    public DateTime? VerifiedAt { get; set; }
    public Guid? VerifiedByUserId { get; set; }
    public string? VerificationNote { get; set; }

    /// <summary>Participant can deactivate a profile; historical submissions keep referencing it.</summary>
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public int AccountAgeDays(DateTime nowUtc) => (int)Math.Floor((nowUtc - AccountCreatedAt).TotalDays);
}
