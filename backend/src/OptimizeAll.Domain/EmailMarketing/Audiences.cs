using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.EmailMarketing;

/// <summary>Channel a marketing message travels on.</summary>
public enum MessageChannel
{
    Email,
    Sms,
    WhatsApp,
}

/// <summary>Deliverability status of a contact's email address within one workspace.</summary>
public enum SubscriberStatus
{
    Subscribed,
    Unsubscribed,
    Bounced,
    Complained,
    /// <summary>Removed by list hygiene (invalid address, repeated soft bounces, long-term inactivity).</summary>
    Cleaned,
}

/// <summary>Consent for one channel. Only <see cref="Granted"/> allows marketing messages on that channel.</summary>
public enum ConsentStatus
{
    /// <summary>Never recorded (e.g. created from a form without a consent box).</summary>
    Unknown,
    /// <summary>Waiting for double opt-in confirmation.</summary>
    Pending,
    Granted,
    Withdrawn,
}

public enum MembershipStatus
{
    /// <summary>Double opt-in confirmation email sent, not yet confirmed.</summary>
    Pending,
    Subscribed,
    Unsubscribed,
}

public enum SuppressionReason
{
    Unsubscribed,
    HardBounce,
    Complaint,
    /// <summary>SMS STOP keyword (or WhatsApp opt-out).</summary>
    StopKeyword,
    InvalidAddress,
    Manual,
}

/// <summary>How often a contact wants to hear from a workspace (preference center).</summary>
public enum EmailFrequency
{
    Any,
    Weekly,
    Monthly,
}

/// <summary>
/// Workspace keys make per-workspace uniqueness portable: the agency's own marketing has no client
/// (<c>ClientAccountId = null</c>) and a nullable column cannot be part of a unique index on every provider.
/// </summary>
public static class Workspace
{
    public const string AgencyKey = "agency";

    public static string Key(Guid? clientAccountId) => clientAccountId?.ToString("D") ?? AgencyKey;

    /// <summary>Parses a workspace key ("agency" or a client id).</summary>
    public static bool TryParse(string? value, out Guid? clientAccountId)
    {
        clientAccountId = null;
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, AgencyKey, StringComparison.OrdinalIgnoreCase)) return true;
        if (Guid.TryParse(value, out var id)) { clientAccountId = id; return true; }
        return false;
    }
}

/// <summary>An audience list (newsletter, customers, VIP...). Lists marked for the preference center act as topics.</summary>
public class EmailList : AuditedEntity, IConcurrencyStamped
{
    public Guid? ClientAccountId { get; set; }
    public string ScopeKey { get; set; } = Workspace.AgencyKey;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>New sign-ups (forms, manual adds without attestation) must confirm by email before they receive anything.</summary>
    public bool DoubleOptIn { get; set; } = true;

    /// <summary>Shown as a topic in the preference center.</summary>
    public bool ShowInPreferenceCenter { get; set; } = true;

    /// <summary>Random public key used by the hosted signup form (<c>/email/subscribe/{key}</c>).</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Version of the consent wording shown on this list's signup form.</summary>
    public string ConsentTextVersion { get; set; } = "v1";
    public string ConsentText { get; set; } = "Yes, send me news and offers by email. I can unsubscribe at any time.";
    public bool IsArchived { get; set; }

    /// <summary>Stable key for seeded lists (idempotent seeding).</summary>
    public string? SeedKey { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>A contact in one workspace. Consent is tracked per channel; <see cref="Status"/> is the email deliverability status.</summary>
public class Subscriber : AuditedEntity, IConcurrencyStamped
{
    public Guid? ClientAccountId { get; set; }
    public string ScopeKey { get; set; } = Workspace.AgencyKey;

    public string? Email { get; set; }
    /// <summary>Lower-case trimmed email, unique per workspace.</summary>
    public string? NormalizedEmail { get; set; }

    /// <summary>E.164 phone number (+ and 8-15 digits).</summary>
    public string? Phone { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>BCP 47 primary language, e.g. "en".</summary>
    public string? Language { get; set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? CountryCode { get; set; }

    /// <summary>IANA time zone used for "send in the recipient's time zone" and SMS quiet hours.</summary>
    public string? TimeZone { get; set; }

    /// <summary>Where the contact came from: import, form, website, api, manual, automation.</summary>
    public string Source { get; set; } = "manual";

    public SubscriberStatus Status { get; set; } = SubscriberStatus.Subscribed;
    public ConsentStatus EmailConsent { get; set; } = ConsentStatus.Unknown;
    public DateTime? EmailConsentAt { get; set; }
    public ConsentStatus SmsConsent { get; set; } = ConsentStatus.Unknown;
    public DateTime? SmsConsentAt { get; set; }
    public ConsentStatus WhatsAppConsent { get; set; } = ConsentStatus.Unknown;
    public DateTime? WhatsAppConsentAt { get; set; }
    public EmailFrequency Frequency { get; set; } = EmailFrequency.Any;

    public DateTime? LastSentAt { get; set; }
    public DateTime? LastOpenAt { get; set; }
    public DateTime? LastClickAt { get; set; }
    public int SoftBounceCount { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Membership of a subscriber in a list (double opt-in state lives here).</summary>
public class ListMembership : Entity
{
    public Guid ListId { get; set; }
    public Guid SubscriberId { get; set; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Pending;
    public string Source { get; set; } = "manual";
    public DateTime CreatedAt { get; set; }
    public DateTime? SubscribedAt { get; set; }
    public DateTime? UnsubscribedAt { get; set; }
    public DateTime? ConfirmationSentAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}

public class SubscriberTag
{
    public Guid SubscriberId { get; set; }
    /// <summary>Lower-case tag, max 50 characters.</summary>
    public string Tag { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
}

/// <summary>A custom field value (validated scalar stored as text; dates as yyyy-MM-dd).</summary>
public class SubscriberField
{
    public Guid SubscriberId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>Append-only consent history (proof of consent for GDPR/PECR/CAN-SPAM/TCPA).</summary>
public class ConsentRecord : Entity
{
    public Guid SubscriberId { get; set; }
    public Guid? ClientAccountId { get; set; }
    public MessageChannel Channel { get; set; }
    public ConsentStatus Status { get; set; }
    public DateTime RecordedAt { get; set; }
    /// <summary>form, import, double-opt-in, preference-center, unsubscribe, sms-stop, manual, api, website.</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Keyed hash of the IP address the consent came from (never the raw IP).</summary>
    public string? IpHash { get; set; }
    public string? ConsentTextVersion { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Workspace-wide suppression list (unsubscribes, hard bounces, complaints, STOP). Always enforced: a suppressed address
/// is never sent to, whatever list, segment, automation or import it appears in.
/// </summary>
public class Suppression : Entity
{
    public Guid? ClientAccountId { get; set; }
    public string ScopeKey { get; set; } = Workspace.AgencyKey;
    /// <summary>Email (address) or Sms/WhatsApp (E.164 phone).</summary>
    public MessageChannel Channel { get; set; }
    /// <summary>Normalized email (lower case) or E.164 phone.</summary>
    public string Value { get; set; } = string.Empty;
    public SuppressionReason Reason { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

public enum ImportStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
}

/// <summary>A CSV import. Small files are processed immediately; large files in chunks by the import job (resumable).</summary>
public class SubscriberImport : AuditedEntity
{
    public Guid? ClientAccountId { get; set; }
    public Guid ListId { get; set; }
    public ImportStatus Status { get; set; } = ImportStatus.Pending;
    public string FileName { get; set; } = string.Empty;
    /// <summary>The uploaded CSV (UTF-8). Cleared after completion.</summary>
    public string? CsvContent { get; set; }
    /// <summary>JSON object: CSV column header → target field (email, phone, first_name, ..., tags, custom.&lt;key&gt;).</summary>
    public string MappingJson { get; set; } = "{}";
    public string TagsJson { get; set; } = "[]";
    /// <summary>The attestation text the staff member confirmed (consent was obtained lawfully).</summary>
    public string ConsentAttestation { get; set; } = string.Empty;
    public string ConsentSource { get; set; } = string.Empty;
    public bool GrantSmsConsent { get; set; }
    public Guid AttestedByUserId { get; set; }
    public int TotalRows { get; set; }
    public int ProcessedRows { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    /// <summary>JSON array of {row, message} (first 500).</summary>
    public string ErrorsJson { get; set; } = "[]";
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LockedUntil { get; set; }
}

/// <summary>Sender identity (From/Reply-To). Must be verified (code sent to the address) before campaigns can use it.</summary>
public class SenderProfile : AuditedEntity, IConcurrencyStamped
{
    public Guid? ClientAccountId { get; set; }
    public string ScopeKey { get; set; } = Workspace.AgencyKey;
    public string FromName { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string? ReplyTo { get; set; }
    public bool IsDefault { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public Guid? VerifiedByUserId { get; set; }
    public string? VerificationCodeHash { get; set; }
    public DateTime? VerificationSentAt { get; set; }
    public int VerificationAttempts { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Per-workspace compliance and sending settings.</summary>
public class EmailWorkspaceSettings : AuditedEntity, IConcurrencyStamped
{
    public Guid? ClientAccountId { get; set; }
    public string ScopeKey { get; set; } = Workspace.AgencyKey;

    /// <summary>Legal sender name shown in the footer.</summary>
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary>Physical postal address required in every commercial email (CAN-SPAM).</summary>
    public string PhysicalAddress { get; set; } = string.Empty;

    /// <summary>When on, a client Approver/Owner must approve each campaign before the send job starts it.</summary>
    public bool RequireClientApproval { get; set; }

    /// <summary>"smtp" (default), "sendgrid", "mailgun", "ses", "postmark".</summary>
    public string EmailProvider { get; set; } = "smtp";
    public int DefaultThrottlePerMinute { get; set; } = 600;

    /// <summary>SMS quiet hours in the recipient's local time (start inclusive, end exclusive; may wrap midnight).</summary>
    public int QuietHoursStart { get; set; } = 21;
    public int QuietHoursEnd { get; set; } = 8;

    /// <summary>IANA time zone used when a contact has none.</summary>
    public string DefaultTimeZone { get; set; } = "UTC";
    public decimal SmsCostPerSegment { get; set; } = 0.0079m;
    public decimal WhatsAppCostPerMessage { get; set; } = 0.025m;
    public string CostCurrency { get; set; } = "USD";
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
