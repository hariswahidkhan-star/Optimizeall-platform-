using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Identity;

public enum UserStatus
{
    Active,
    Suspended,
    Deactivated,
}

/// <summary>Roles map to permission sets in the API (see Api/Common/Security/RolePermissions).</summary>
public enum Role
{
    Participant,
    Reviewer,
    CampaignManager,
    Finance,
    Admin,

    // Agency delivery team
    AccountManager,
    Strategist,
    ContentCreator,
    Designer,
    SeoSpecialist,
    AdsSpecialist,
    SocialMediaManager,
    SalesRep,

    /// <summary>A user of a client organization (client portal). Always scoped to their organization.</summary>
    Client,
}

public class User : AuditedEntity, IConcurrencyStamped
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 country code (upper case).</summary>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>BCP 47 primary language, e.g. "en", "ar", "ur".</summary>
    public string LanguageCode { get; set; } = "en";

    /// <summary>IANA time zone id, e.g. "Asia/Karachi". Used for display and local-date rules.</summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Participant interests used for segmentation (lower-case tags).</summary>
    public List<string> Interests { get; set; } = new();

    public DateTime? EmailVerifiedAt { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    public string? StatusReason { get; set; }
    public DateTime? StatusChangedAt { get; set; }
    public ParticipantTier Tier { get; set; } = ParticipantTier.Standard;

    /// <summary>Public code used in the participant's referral link.</summary>
    public string ReferralCode { get; set; } = string.Empty;

    /// <summary>E.164 phone number, only stored when the participant opts into WhatsApp notifications.</summary>
    public string? WhatsAppNumber { get; set; }
    public bool WhatsAppOptIn { get; set; }
    public bool MarketingEmailOptIn { get; set; }

    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndsAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime? LastActiveAt { get; set; }

    /// <summary>Incremented on password change / forced sign-out; embedded in access tokens and checked on refresh.</summary>
    public int SecurityVersion { get; set; }

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<UserRole> Roles { get; set; } = new();

    public bool IsEmailVerified => EmailVerifiedAt.HasValue;
    public bool HasRole(Role role) => Roles.Any(r => r.Role == role);
}

public class UserRole
{
    public Guid UserId { get; set; }
    public Role Role { get; set; }
    public DateTime GrantedAt { get; set; }
    public Guid? GrantedByUserId { get; set; }
}

public class RefreshToken : Entity
{
    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the opaque token; the raw token only ever exists in the client's HttpOnly cookie.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>All tokens rotated from the same login share a family; reuse of a rotated token revokes the family.</summary>
    public Guid FamilyId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
}

public enum UserTokenPurpose
{
    EmailVerification,
    PasswordReset,
}

/// <summary>Single-use, expiring token (email verification, password reset). Only its hash is stored.</summary>
public class UserToken : Entity
{
    public Guid UserId { get; set; }
    public UserTokenPurpose Purpose { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}

public enum PayoutMethod
{
    BankTransfer,
    PayPal,
    MobileWallet,
    Other,
}

/// <summary>
/// Minimal payout destination data. Full account details are encrypted at rest (ASP.NET Data Protection)
/// and only a masked hint is kept in clear text for display.
/// </summary>
public class PayoutProfile : AuditedEntity
{
    public Guid UserId { get; set; }
    public PayoutMethod Method { get; set; }
    public string AccountHolderName { get; set; } = string.Empty;
    public string MaskedDestination { get; set; } = string.Empty;
    public string EncryptedDestination { get; set; } = string.Empty;
    public string PreferredCurrency { get; set; } = "USD";
    public string? CountryCode { get; set; }
}
